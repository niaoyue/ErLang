using OwnDesk.Server;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;
using OwnDesk.Shared.Security;
using System.Net.WebSockets;
using System.Text.Json;

const string LocalhostUrl = "http://127.0.0.1:5080";

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = ResolveWebRootPath()
});

if (args.Length == 0 && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls(LocalhostUrl);
}

var token = Environment.GetEnvironmentVariable("OWNDESK_TOKEN")
            ?? builder.Configuration["OwnDesk:Token"];
var authStorePath = Environment.GetEnvironmentVariable("OWNDESK_AUTH_STORE")
                    ?? builder.Configuration["OwnDesk:AuthStorePath"]
                    ?? Path.Combine(AppContext.BaseDirectory, "owndesk-auth.json");
var deviceStorePath = Environment.GetEnvironmentVariable("OWNDESK_DEVICE_STORE")
                      ?? builder.Configuration["OwnDesk:DeviceStorePath"]
                      ?? Path.Combine(AppContext.BaseDirectory, "owndesk-devices.json");

if (string.IsNullOrWhiteSpace(token))
{
    throw new InvalidOperationException("OwnDesk token is required. Set OWNDESK_TOKEN or OwnDesk:Token before starting the server.");
}

// Keep framework request logging terse; WebSocket auth tokens are never placed in URLs.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

builder.Services.AddSingleton(new OrganizationAuthenticator(token, authStorePath));
builder.Services.AddSingleton<IDeviceRecordStore>(new JsonDeviceRecordStore(deviceStorePath));
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<WebRtcConfigProvider>();
builder.Services.AddSingleton<WebSocketRelay>();
builder.Services.AddSingleton<WebRtcSignalingRelay>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});

app.MapGet("/", () => Results.Redirect("/index.html"));

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    nowUtc = DateTimeOffset.UtcNow
}));

app.MapPost("/api/register", async (HttpContext context, OrganizationAuthenticator authenticator) =>
{
    RegisterMemberRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<RegisterMemberRequest>(context.Request.Body, JsonDefaults.Options, context.RequestAborted);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new ErrorMessage { Message = "Invalid request body." });
    }

    if (request is null)
    {
        return Results.BadRequest(new ErrorMessage { Message = "Invalid request body." });
    }

    try
    {
        return Results.Ok(authenticator.Register(request));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new ErrorMessage { Message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new ErrorMessage { Message = ex.Message });
    }
});

app.MapPost("/api/login", async (HttpContext context, OrganizationAuthenticator authenticator) =>
{
    LoginMemberRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<LoginMemberRequest>(context.Request.Body, JsonDefaults.Options, context.RequestAborted);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new ErrorMessage { Message = "Invalid request body." });
    }

    if (request is null)
    {
        return Results.BadRequest(new ErrorMessage { Message = "Invalid request body." });
    }

    try
    {
        return Results.Ok(authenticator.Login(request));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
});

app.MapPost("/api/devices", async (HttpContext context, OrganizationAuthenticator authenticator, DeviceRegistry registry) =>
{
    AuthMessage? auth;
    try
    {
        auth = await JsonSerializer.DeserializeAsync<AuthMessage>(context.Request.Body, JsonDefaults.Options, context.RequestAborted);
    }
    catch (JsonException)
    {
        return Results.BadRequest();
    }

    if (auth is null)
    {
        return Results.Unauthorized();
    }

    var member = authenticator.Authenticate(auth);
    if (member is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(registry.ListDevices(member.OrganizationId));
});

app.MapPost("/api/devices/remove", async (HttpContext context, OrganizationAuthenticator authenticator, DeviceRegistry registry) =>
{
    AuthMessage? auth;
    try
    {
        auth = await JsonSerializer.DeserializeAsync<AuthMessage>(context.Request.Body, JsonDefaults.Options, context.RequestAborted);
    }
    catch (JsonException)
    {
        return Results.BadRequest();
    }

    if (auth is null)
    {
        return Results.Unauthorized();
    }

    var member = authenticator.Authenticate(auth);
    if (member is null)
    {
        return Results.Unauthorized();
    }

    var deviceIdToRemove = auth.DeviceId?.Trim();
    if (string.IsNullOrWhiteSpace(deviceIdToRemove))
    {
        return Results.BadRequest(new ErrorMessage { Message = "Device id is required." });
    }

    await registry.RemoveDeviceAsync(member.OrganizationId, deviceIdToRemove, context.RequestAborted);
    return Results.Ok(new { removed = true });
});

app.MapPost("/api/webrtc/config", async (
    HttpContext context,
    OrganizationAuthenticator authenticator,
    WebRtcConfigProvider webRtcConfig) =>
{
    AuthMessage? auth;
    try
    {
        auth = await JsonSerializer.DeserializeAsync<AuthMessage>(context.Request.Body, JsonDefaults.Options, context.RequestAborted);
    }
    catch (JsonException)
    {
        return Results.BadRequest();
    }

    if (auth is null || authenticator.Authenticate(auth) is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(webRtcConfig.GetConfig());
});

app.Map("/ws/agent", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var authenticator = context.RequestServices.GetRequiredService<OrganizationAuthenticator>();
    var auth = await ReceiveAuthAsync(socket, authenticator, context.RequestAborted);
    if (auth is null || string.IsNullOrWhiteSpace(auth.Message.DeviceId) || string.IsNullOrWhiteSpace(auth.Message.DeviceName))
    {
        socket.Abort();
        return;
    }

    var relay = context.RequestServices.GetRequiredService<WebSocketRelay>();
    await relay.HandleAgentAsync(auth.Member.OrganizationId, auth.Message.DeviceId, auth.Message.DeviceName, socket, context.RequestAborted);
});

app.Map("/ws/viewer", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var deviceId = RequiredQuery(context, "deviceId");
    if (string.IsNullOrWhiteSpace(deviceId))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var authenticator = context.RequestServices.GetRequiredService<OrganizationAuthenticator>();
    var auth = await ReceiveAuthAsync(socket, authenticator, context.RequestAborted);
    if (auth is null)
    {
        socket.Abort();
        return;
    }

    var relay = context.RequestServices.GetRequiredService<WebSocketRelay>();
    await relay.HandleViewerAsync(auth.Member.OrganizationId, deviceId, socket, context.RequestAborted);
});

app.Map("/ws/devices", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var authenticator = context.RequestServices.GetRequiredService<OrganizationAuthenticator>();
    var registry = context.RequestServices.GetRequiredService<DeviceRegistry>();
    var auth = await ReceiveAuthAsync(socket, authenticator, context.RequestAborted);
    if (auth is null)
    {
        socket.Abort();
        return;
    }

    var watcher = registry.AddWatcher(auth.Member.OrganizationId, socket);
    await registry.NotifyDeviceListChangedAsync(auth.Member.OrganizationId, context.RequestAborted);

    try
    {
        while (!context.RequestAborted.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var message = await WebSocketMessages.ReceiveAsync(socket, 4096, context.RequestAborted);
            if (message is null)
            {
                break;
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (WebSocketException)
    {
    }
    finally
    {
        registry.RemoveWatcher(watcher);
    }
});

app.Map("/ws/webrtc/agent", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var deviceId = RequiredQuery(context, "deviceId");
    if (string.IsNullOrWhiteSpace(deviceId))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var authenticator = context.RequestServices.GetRequiredService<OrganizationAuthenticator>();
    var auth = await ReceiveAuthAsync(socket, authenticator, context.RequestAborted);
    if (auth is null)
    {
        socket.Abort();
        return;
    }

    var relay = context.RequestServices.GetRequiredService<WebRtcSignalingRelay>();
    await relay.HandleAgentAsync(auth.Member.OrganizationId, deviceId, socket, context.RequestAborted);
});

app.Map("/ws/webrtc/viewer", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var deviceId = RequiredQuery(context, "deviceId");
    var sessionId = RequiredQuery(context, "sessionId");
    if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(sessionId))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var authenticator = context.RequestServices.GetRequiredService<OrganizationAuthenticator>();
    var auth = await ReceiveAuthAsync(socket, authenticator, context.RequestAborted);
    if (auth is null)
    {
        socket.Abort();
        return;
    }

    var relay = context.RequestServices.GetRequiredService<WebRtcSignalingRelay>();
    await relay.HandleViewerAsync(auth.Member.OrganizationId, deviceId, sessionId, socket, context.RequestAborted);
});

app.Run();

static string RequiredQuery(HttpContext context, string key)
{
    return context.Request.Query[key].ToString().Trim();
}

static async Task<AuthorizedAuth?> ReceiveAuthAsync(
    WebSocket socket,
    OrganizationAuthenticator authenticator,
    CancellationToken cancellationToken)
{
    const int maxAuthBytes = 4096;
    var timeout = TimeSpan.FromSeconds(5);

    try
    {
        using var authTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        authTimeout.CancelAfter(timeout);
        var message = await WebSocketMessages.ReceiveAsync(socket, maxAuthBytes, authTimeout.Token);
        if (message is null || !message.IsText)
        {
            return null;
        }

        var auth = JsonSerializer.Deserialize<AuthMessage>(WebSocketMessages.AsText(message), JsonDefaults.Options);
        var member = auth is null ? null : authenticator.Authenticate(auth);
        if (auth is null || member is null)
        {
            await new SafeWebSocket(socket).SendTextAsync(
                JsonSerializer.Serialize(new ErrorMessage { Message = "Unauthorized." }, JsonDefaults.Options),
                cancellationToken);
            return null;
        }

        return new AuthorizedAuth(auth, member);
    }
    catch (JsonException)
    {
        return null;
    }
    catch (InvalidOperationException)
    {
        return null;
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return null;
    }
}

static string ResolveWebRootPath()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
        Path.Combine(Directory.GetCurrentDirectory(), "src", "OwnDesk.Server", "wwwroot"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"))
    };

    foreach (var candidate in candidates)
    {
        if (File.Exists(Path.Combine(candidate, "index.html")))
        {
            return candidate;
        }
    }

    return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
}

internal sealed record AuthorizedAuth(AuthMessage Message, AuthenticatedMember Member);
