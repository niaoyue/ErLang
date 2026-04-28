using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;
using OwnDesk.Shared.Transport;

namespace OwnDesk.Agent;

internal sealed class WebRtcAgentSignalingClient
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private const int MaxSignalMessageBytes = 256 * 1024;

    private readonly AgentOptions _options;
    private readonly ScreenCaptureService _screenCapture;
    private readonly StreamQualityController _qualityController;
    private readonly RemoteControlHandler _controlHandler;
    private readonly WebRtcEncodingPlan _encodingPlan;
    private readonly IReadOnlyList<WebRtcIceServerDto> _iceServers;
    private readonly ConcurrentDictionary<string, WebRtcPeerSession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private ClientWebSocket? _socket;

    public WebRtcAgentSignalingClient(
        AgentOptions options,
        ScreenCaptureService screenCapture,
        StreamQualityController qualityController,
        RemoteControlHandler controlHandler,
        IReadOnlyList<WebRtcIceServerDto> iceServers)
    {
        _options = options;
        _screenCapture = screenCapture;
        _qualityController = qualityController;
        _controlHandler = controlHandler;
        _encodingPlan = WebRtcEncodingPlan.Create(options, screenCapture.BackendPlan, _qualityController.Current);
        _iceServers = iceServers;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebRTC signaling error: {ex.Message}");
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"WebRTC signaling reconnecting in {ReconnectDelay.TotalSeconds:n0}s...");
                await Task.Delay(ReconnectDelay, cancellationToken);
            }
        }
    }

    private async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket
        {
            Options =
            {
                KeepAliveInterval = TimeSpan.FromSeconds(20)
            }
        };

        var uri = EndpointUris.BuildWebSocketUri(
            _options.Server,
            "/ws/webrtc/agent",
            new Dictionary<string, string>
            {
                ["deviceId"] = _options.DeviceId
            });

        Console.WriteLine($"Connecting WebRTC signaling to {uri}...");
        await socket.ConnectAsync(uri, cancellationToken);
        _socket = socket;
        await SendAuthAsync(socket, cancellationToken);
        Console.WriteLine("WebRTC signaling connected.");

        await SendSignalAsync(
            new WebRtcSignalMessage
            {
                Type = OwnDeskMessageTypes.WebRtcCapabilities,
                DeviceId = _options.DeviceId,
                Mode = _encodingPlan.Mode,
                Codecs = _encodingPlan.AdvertisedCodecs,
                HardwareEncoding = _encodingPlan.HardwareEncoding,
                RequestedCodec = _encodingPlan.RequestedCodec,
                SelectedCodec = _encodingPlan.SelectedCodec,
                CaptureBackend = _encodingPlan.CaptureBackend,
                RequestedCaptureBackend = _screenCapture.BackendPlan.RequestedBackend,
                SelectedCaptureBackend = _screenCapture.BackendPlan.SelectedBackend,
                EncoderName = _encodingPlan.EncoderName,
                TargetKbps = _encodingPlan.TargetKbps,
                Notes = _encodingPlan.Notes
            },
            cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var text = await AgentWebSocket.ReceiveStringAsync(socket, MaxSignalMessageBytes, cancellationToken);
                if (text is null)
                {
                    break;
                }

                await HandleSignalAsync(text, cancellationToken);
            }
        }
        finally
        {
            _socket = null;
            await CloseAllPeerSessionsAsync();
        }
    }

    private async Task HandleSignalAsync(string text, CancellationToken cancellationToken)
    {
        var signal = JsonSerializer.Deserialize<WebRtcSignalMessage>(text, JsonDefaults.Options);
        if (signal is null || string.IsNullOrWhiteSpace(signal.SessionId))
        {
            return;
        }

        switch (signal.Type)
        {
            case OwnDeskMessageTypes.WebRtcOffer:
                await AcceptOfferAsync(signal, cancellationToken);
                break;
            case OwnDeskMessageTypes.WebRtcIce:
                if (_sessions.TryGetValue(signal.SessionId, out var session))
                {
                    session.AddIceCandidate(signal);
                }
                break;
            case OwnDeskMessageTypes.WebRtcViewerClosed:
                await ClosePeerSessionAsync(signal.SessionId);
                break;
        }
    }

    private async Task AcceptOfferAsync(WebRtcSignalMessage signal, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(signal.Sdp))
        {
            await SendSignalSafeAsync(
                new WebRtcSignalMessage
                {
                    Type = OwnDeskMessageTypes.WebRtcError,
                    SessionId = signal.SessionId,
                    DeviceId = _options.DeviceId,
                    Message = "Missing WebRTC offer SDP."
                },
                cancellationToken);
            return;
        }

        await ClosePeerSessionAsync(signal.SessionId);

        var session = new WebRtcPeerSession(
            signal.SessionId,
            _options,
            _screenCapture,
            _qualityController,
            _encodingPlan,
            _controlHandler,
            _iceServers,
            SendSignalSafeAsync);
        _sessions[signal.SessionId] = session;

        try
        {
            await session.AcceptOfferAsync(signal, cancellationToken);
        }
        catch (Exception ex)
        {
            await ClosePeerSessionAsync(signal.SessionId);
            await SendSignalSafeAsync(
                new WebRtcSignalMessage
                {
                    Type = OwnDeskMessageTypes.WebRtcError,
                    SessionId = signal.SessionId,
                    DeviceId = _options.DeviceId,
                    Message = ex.Message
                },
                cancellationToken);
        }
    }

    private async Task SendSignalSafeAsync(WebRtcSignalMessage signal, CancellationToken cancellationToken)
    {
        try
        {
            await SendSignalAsync(signal, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task SendSignalAsync(WebRtcSignalMessage signal, CancellationToken cancellationToken)
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            return;
        }

        signal.DeviceId = string.IsNullOrWhiteSpace(signal.DeviceId) ? _options.DeviceId : signal.DeviceId;
        var text = JsonSerializer.Serialize(signal, JsonDefaults.Options);

        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await AgentWebSocket.SendTextAsync(socket, text, cancellationToken);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task SendAuthAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        await AgentWebSocket.SendTextAsync(
            socket,
            JsonSerializer.Serialize(
                new AuthMessage
                {
                    Account = _options.Account,
                    Token = _options.Token,
                    Password = _options.Password,
                    DeviceId = _options.DeviceId
                },
                JsonDefaults.Options),
            cancellationToken);
    }

    private async Task ClosePeerSessionAsync(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            await session.CloseAsync("session closed");
        }
    }

    private async Task CloseAllPeerSessionsAsync()
    {
        foreach (var sessionId in _sessions.Keys.ToArray())
        {
            await ClosePeerSessionAsync(sessionId);
        }
    }
}
