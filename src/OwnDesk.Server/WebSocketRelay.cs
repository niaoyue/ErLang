using System.Net.WebSockets;
using System.Text.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;

namespace OwnDesk.Server;

internal sealed class WebSocketRelay
{
    private const int MaxAgentMessageBytes = 12 * 1024 * 1024;
    private const int MaxViewerMessageBytes = 256 * 1024;
    private readonly DeviceRegistry _registry;
    private readonly ILogger<WebSocketRelay> _logger;

    public WebSocketRelay(DeviceRegistry registry, ILogger<WebSocketRelay> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task HandleAgentAsync(
        string organizationId,
        string deviceId,
        string deviceName,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var session = _registry.RegisterAgent(organizationId, deviceId, deviceName, socket);
        _logger.LogInformation("Agent connected: {OrganizationId}/{DeviceId} ({DeviceName})", organizationId, deviceId, deviceName);

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var message = await WebSocketMessages.ReceiveAsync(socket, MaxAgentMessageBytes, cancellationToken);
                if (message is null)
                {
                    break;
                }

                if (message.IsBinary)
                {
                    if (TryGetBinaryFrameMetadata(message.Payload, out var width, out var height))
                    {
                        _registry.UpdateFrame(session, width, height);
                        await _registry.BroadcastBinaryToViewersAsync(session, message.Payload, cancellationToken);
                    }

                    continue;
                }

                if (!message.IsText)
                {
                    continue;
                }

                var text = WebSocketMessages.AsText(message);
                if (!TryGetMessageType(text, out var type))
                {
                    continue;
                }

                if (type == OwnDeskMessageTypes.AgentHello)
                {
                    var hello = JsonSerializer.Deserialize<AgentHelloMessage>(text, JsonDefaults.Options);
                    if (hello is not null)
                    {
                        _registry.UpdateHello(session, hello.DeviceName, hello.ScreenWidth, hello.ScreenHeight);
                    }
                }
                else if (type == OwnDeskMessageTypes.Frame && TryGetFrameMetadata(text, out var width, out var height))
                {
                    _registry.UpdateFrame(session, width, height);
                    await _registry.BroadcastToViewersAsync(session, text, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogInformation(ex, "Agent websocket closed: {OrganizationId}/{DeviceId}", organizationId, deviceId);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Agent sent invalid JSON: {OrganizationId}/{DeviceId}", organizationId, deviceId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Agent websocket message rejected: {OrganizationId}/{DeviceId}", organizationId, deviceId);
        }
        finally
        {
            _registry.UnregisterAgent(session);
            _logger.LogInformation("Agent disconnected: {OrganizationId}/{DeviceId}", organizationId, deviceId);
        }
    }

    public async Task HandleViewerAsync(
        string organizationId,
        string deviceId,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var viewer = _registry.AddViewer(organizationId, deviceId, socket);
        if (viewer is null)
        {
            var safeSocket = new SafeWebSocket(socket);
            await safeSocket.SendTextAsync(
                JsonSerializer.Serialize(new ErrorMessage { Message = "Device is offline." }, JsonDefaults.Options),
                cancellationToken);
            socket.Abort();
            return;
        }

        _logger.LogInformation("Viewer connected: {OrganizationId}/{DeviceId}/{ViewerId}", organizationId, deviceId, viewer.Id);

        try
        {
            if (_registry.TryGetDevice(organizationId, deviceId, out var device) && device is not null)
            {
                await viewer.Connection.SendTextAsync(
                    JsonSerializer.Serialize(new
                    {
                        type = OwnDeskMessageTypes.Device,
                        device
                    }, JsonDefaults.Options),
                    cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var message = await WebSocketMessages.ReceiveAsync(socket, MaxViewerMessageBytes, cancellationToken);
                if (message is null)
                {
                    break;
                }

                if (!message.IsText)
                {
                    continue;
                }

                var text = WebSocketMessages.AsText(message);
                if (TryGetMessageType(text, out var type) && IsViewerToAgentMessage(type))
                {
                    await _registry.SendToAgentAsync(organizationId, deviceId, text, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogInformation(ex, "Viewer websocket closed: {OrganizationId}/{DeviceId}/{ViewerId}", organizationId, deviceId, viewer.Id);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Viewer sent invalid JSON: {OrganizationId}/{DeviceId}/{ViewerId}", organizationId, deviceId, viewer.Id);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Viewer websocket message rejected: {OrganizationId}/{DeviceId}/{ViewerId}", organizationId, deviceId, viewer.Id);
        }
        finally
        {
            _registry.RemoveViewer(viewer);
            _logger.LogInformation("Viewer disconnected: {OrganizationId}/{DeviceId}/{ViewerId}", organizationId, deviceId, viewer.Id);
        }
    }

    private static bool TryGetMessageType(string text, out string type)
    {
        type = string.Empty;

        using var document = JsonDocument.Parse(text);
        if (!document.RootElement.TryGetProperty("type", out var typeElement))
        {
            return false;
        }

        type = typeElement.GetString() ?? string.Empty;
        return type.Length > 0;
    }

    private static bool TryGetFrameMetadata(string text, out int width, out int height)
    {
        width = 0;
        height = 0;

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (!root.TryGetProperty("width", out var widthElement) ||
            !root.TryGetProperty("height", out var heightElement))
        {
            return false;
        }

        width = widthElement.GetInt32();
        height = heightElement.GetInt32();
        return width > 0 && height > 0;
    }

    private static bool TryGetBinaryFrameMetadata(byte[] payload, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (!BinaryFrameCodec.TryDecode(payload, out var header, out _))
        {
            return false;
        }

        width = header.Width;
        height = header.Height;
        return width > 0 && height > 0;
    }

    private static bool IsViewerToAgentMessage(string type)
    {
        return type is OwnDeskMessageTypes.Input or OwnDeskMessageTypes.StreamQuality;
    }
}
