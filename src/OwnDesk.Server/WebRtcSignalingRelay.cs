using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;

namespace OwnDesk.Server;

internal sealed class WebRtcSignalingRelay
{
    private const int MaxSignalBytes = 256 * 1024;

    private readonly ConcurrentDictionary<DeviceKey, WebRtcAgentSession> _agents = new();
    private readonly ConcurrentDictionary<WebRtcSessionKey, WebRtcViewerSession> _viewers = new();
    private readonly ILogger<WebRtcSignalingRelay> _logger;

    public WebRtcSignalingRelay(ILogger<WebRtcSignalingRelay> logger)
    {
        _logger = logger;
    }

    public async Task HandleAgentAsync(
        string account,
        string deviceId,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var session = new WebRtcAgentSession(account, deviceId, new SafeWebSocket(socket));
        _agents.AddOrUpdate(
            session.Key,
            session,
            (_, existing) =>
            {
                existing.Connection.Abort();
                return session;
            });

        _logger.LogInformation("WebRTC agent signaling connected: {Account}/{DeviceId}", account, deviceId);

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var message = await WebSocketMessages.ReceiveAsync(socket, MaxSignalBytes, cancellationToken);
                if (message is null)
                {
                    break;
                }

                if (!message.IsText)
                {
                    continue;
                }

                await RouteAgentMessageAsync(session, WebSocketMessages.AsText(message), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogInformation(ex, "WebRTC agent signaling closed: {Account}/{DeviceId}", account, deviceId);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "WebRTC agent sent invalid JSON: {Account}/{DeviceId}", account, deviceId);
        }
        finally
        {
            if (_agents.TryGetValue(session.Key, out var current) && ReferenceEquals(current, session))
            {
                _agents.TryRemove(session.Key, out _);
            }

            await CloseViewersForAgentAsync(session, cancellationToken);
            _logger.LogInformation("WebRTC agent signaling disconnected: {Account}/{DeviceId}", account, deviceId);
        }
    }

    public async Task HandleViewerAsync(
        string account,
        string deviceId,
        string sessionId,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var key = new DeviceKey(account, deviceId);
        if (!_agents.TryGetValue(key, out var agent) || !agent.Connection.IsOpen)
        {
            var safeSocket = new SafeWebSocket(socket);
            await safeSocket.SendTextAsync(
                JsonSerializer.Serialize(
                    new WebRtcSignalMessage
                    {
                        Type = OwnDeskMessageTypes.WebRtcError,
                        SessionId = sessionId,
                        DeviceId = deviceId,
                        Message = "WebRTC agent signaling is offline."
                    },
                    JsonDefaults.Options),
                cancellationToken);
            socket.Abort();
            return;
        }

        var viewer = new WebRtcViewerSession(
            account,
            deviceId,
            sessionId,
            new SafeWebSocket(socket));

        _viewers[viewer.Key] = viewer;
        _logger.LogInformation("WebRTC viewer signaling connected: {Account}/{DeviceId}/{SessionId}", account, deviceId, sessionId);

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var message = await WebSocketMessages.ReceiveAsync(socket, MaxSignalBytes, cancellationToken);
                if (message is null)
                {
                    break;
                }

                if (!message.IsText)
                {
                    continue;
                }

                await RouteViewerMessageAsync(viewer, WebSocketMessages.AsText(message), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogInformation(ex, "WebRTC viewer signaling closed: {Account}/{DeviceId}/{SessionId}", account, deviceId, sessionId);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "WebRTC viewer sent invalid JSON: {Account}/{DeviceId}/{SessionId}", account, deviceId, sessionId);
        }
        finally
        {
            _viewers.TryRemove(viewer.Key, out _);
            await NotifyViewerClosedAsync(viewer, cancellationToken);
            _logger.LogInformation("WebRTC viewer signaling disconnected: {Account}/{DeviceId}/{SessionId}", account, deviceId, sessionId);
        }
    }

    private async Task RouteAgentMessageAsync(
        WebRtcAgentSession agent,
        string text,
        CancellationToken cancellationToken)
    {
        var signal = JsonSerializer.Deserialize<WebRtcSignalMessage>(text, JsonDefaults.Options);
        if (signal is null)
        {
            return;
        }

        if (signal.Type == OwnDeskMessageTypes.WebRtcCapabilities)
        {
            _logger.LogInformation(
                "WebRTC capabilities from {Account}/{DeviceId}: mode={Mode} requested={RequestedCodec} selected={SelectedCodec} codecs={Codecs} hardware={HardwareEncoding} capture={CaptureBackend} requestedCapture={RequestedCaptureBackend} selectedCapture={SelectedCaptureBackend} encoder={EncoderName} target={TargetKbps}kbps notes={Notes}",
                agent.Account,
                agent.DeviceId,
                signal.Mode,
                signal.RequestedCodec,
                signal.SelectedCodec,
                string.Join(",", signal.Codecs),
                signal.HardwareEncoding,
                signal.CaptureBackend,
                signal.RequestedCaptureBackend,
                signal.SelectedCaptureBackend,
                signal.EncoderName,
                signal.TargetKbps,
                string.Join(" | ", signal.Notes));
            return;
        }

        if (string.IsNullOrWhiteSpace(signal.SessionId))
        {
            return;
        }

        var viewerKey = new WebRtcSessionKey(agent.Account, signal.SessionId);
        if (_viewers.TryGetValue(viewerKey, out var viewer) &&
            viewer.DeviceId == agent.DeviceId &&
            viewer.Connection.IsOpen)
        {
            await viewer.Connection.SendTextAsync(text, cancellationToken);
        }
    }

    private async Task RouteViewerMessageAsync(
        WebRtcViewerSession viewer,
        string text,
        CancellationToken cancellationToken)
    {
        if (!_agents.TryGetValue(new DeviceKey(viewer.Account, viewer.DeviceId), out var agent))
        {
            await SendViewerErrorAsync(viewer, "WebRTC agent signaling is offline.", cancellationToken);
            return;
        }

        if (!agent.Connection.IsOpen)
        {
            await SendViewerErrorAsync(viewer, "WebRTC agent signaling is offline.", cancellationToken);
            return;
        }

        var signal = JsonSerializer.Deserialize<WebRtcSignalMessage>(text, JsonDefaults.Options);
        if (signal is null)
        {
            return;
        }

        signal.SessionId = viewer.SessionId;
        signal.DeviceId = viewer.DeviceId;
        await agent.Connection.SendTextAsync(JsonSerializer.Serialize(signal, JsonDefaults.Options), cancellationToken);
    }

    private async Task NotifyViewerClosedAsync(WebRtcViewerSession viewer, CancellationToken cancellationToken)
    {
        if (!_agents.TryGetValue(new DeviceKey(viewer.Account, viewer.DeviceId), out var agent) || !agent.Connection.IsOpen)
        {
            return;
        }

        try
        {
            await agent.Connection.SendTextAsync(
                JsonSerializer.Serialize(
                    new WebRtcSignalMessage
                    {
                        Type = OwnDeskMessageTypes.WebRtcViewerClosed,
                        SessionId = viewer.SessionId,
                        DeviceId = viewer.DeviceId
                    },
                    JsonDefaults.Options),
                cancellationToken);
        }
        catch (WebSocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CloseViewersForAgentAsync(WebRtcAgentSession agent, CancellationToken cancellationToken)
    {
        foreach (var viewer in _viewers.Values.ToArray())
        {
            if (viewer.Account != agent.Account || viewer.DeviceId != agent.DeviceId)
            {
                continue;
            }

            try
            {
                await SendViewerErrorAsync(viewer, "WebRTC agent signaling disconnected.", cancellationToken);
            }
            catch (WebSocketException)
            {
            }
            catch (OperationCanceledException)
            {
            }

            viewer.Connection.Abort();
            _viewers.TryRemove(viewer.Key, out _);
        }
    }

    private static Task SendViewerErrorAsync(
        WebRtcViewerSession viewer,
        string message,
        CancellationToken cancellationToken)
    {
        return viewer.Connection.SendTextAsync(
            JsonSerializer.Serialize(
                new WebRtcSignalMessage
                {
                    Type = OwnDeskMessageTypes.WebRtcError,
                    SessionId = viewer.SessionId,
                    DeviceId = viewer.DeviceId,
                    Message = message
                },
                JsonDefaults.Options),
            cancellationToken);
    }
}

internal readonly record struct WebRtcSessionKey(string Account, string SessionId);

internal sealed record WebRtcAgentSession(string Account, string DeviceId, SafeWebSocket Connection)
{
    public DeviceKey Key => new(Account, DeviceId);
}

internal sealed record WebRtcViewerSession(string Account, string DeviceId, string SessionId, SafeWebSocket Connection)
{
    public WebRtcSessionKey Key => new(Account, SessionId);
}
