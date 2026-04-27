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

var account = Environment.GetEnvironmentVariable("OWNDESK_ACCOUNT")
              ?? builder.Configuration["OwnDesk:Account"]
              ?? "demo";
var token = Environment.GetEnvironmentVariable("OWNDESK_TOKEN")
            ?? builder.Configuration["OwnDesk:Token"];

if (string.IsNullOrWhiteSpace(token))
{
    throw new InvalidOperationException("OwnDesk token is required. Set OWNDESK_TOKEN or OwnDesk:Token before starting the server.");
}

// Keep framework request logging terse; WebSocket auth tokens are never placed in URLs.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

builder.Services.AddSingleton(new SingleAccountAuthenticator(account, token));
builder.Services.AddSingleton<DeviceRegistry>();
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

app.MapPost("/api/devices", async (HttpContext context, SingleAccountAuthenticator authenticator, DeviceRegistry registry) =>
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

    if (auth is null || auth.Type != OwnDeskMessageTypes.Auth || !authenticator.IsAuthorized(auth.Account, auth.Token))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(registry.ListDevices(auth.Account));
});

app.Map("/ws/agent", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var authenticator = context.RequestServices.GetRequiredService<SingleAccountAuthenticator>();
    var auth = await ReceiveAuthAsync(socket, authenticator, context.RequestAborted);
    if (auth is null || string.IsNullOrWhiteSpace(auth.DeviceId) || string.IsNullOrWhiteSpace(auth.DeviceName))
    {
        socket.Abort();
        return;
    }

    var relay = context.RequestServices.GetRequiredService<WebSocketRelay>();
    await relay.HandleAgentAsync(auth.Account, auth.DeviceId, auth.DeviceName, socket, context.RequestAborted);
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
    var authenticator = context.RequestServices.GetRequiredService<SingleAccountAuthenticator>();
    var auth = await ReceiveAuthAsync(socket, authenticator, context.RequestAborted);
    if (auth is null)
    {
        socket.Abort();
        return;
    }

    var relay = context.RequestServices.GetRequiredService<WebSocketRelay>();
    await relay.HandleViewerAsync(auth.Account, deviceId, socket, context.RequestAborted);
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
    var authenticator = context.RequestServices.GetRequiredService<SingleAccountAuthenticator>();
    var auth = await ReceiveAuthAsync(socket, authenticator, context.RequestAborted);
    if (auth is null)
    {
        socket.Abort();
        return;
    }

    var relay = context.RequestServices.GetRequiredService<WebRtcSignalingRelay>();
    await relay.HandleAgentAsync(auth.Account, deviceId, socket, context.RequestAborted);
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
    var authenticator = context.RequestServices.GetRequiredService<SingleAccountAuthenticator>();
    var auth = await ReceiveAuthAsync(socket, authenticator, context.RequestAborted);
    if (auth is null)
    {
        socket.Abort();
        return;
    }

    var relay = context.RequestServices.GetRequiredService<WebRtcSignalingRelay>();
    await relay.HandleViewerAsync(auth.Account, deviceId, sessionId, socket, context.RequestAborted);
});

app.Run();

static string RequiredQuery(HttpContext context, string key)
{
    return context.Request.Query[key].ToString().Trim();
}

static async Task<AuthMessage?> ReceiveAuthAsync(
    WebSocket socket,
    SingleAccountAuthenticator authenticator,
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
        if (auth is null || auth.Type != OwnDeskMessageTypes.Auth ||
            !authenticator.IsAuthorized(auth.Account, auth.Token))
        {
            await new SafeWebSocket(socket).SendTextAsync(
                JsonSerializer.Serialize(new ErrorMessage { Message = "Unauthorized." }, JsonDefaults.Options),
                cancellationToken);
            return null;
        }

        return auth;
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
