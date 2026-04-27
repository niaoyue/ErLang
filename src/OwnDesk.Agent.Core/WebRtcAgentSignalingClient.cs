using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;
using OwnDesk.Shared.Transport;
using SIPSorcery.Net;

namespace OwnDesk.Agent;

internal sealed class WebRtcAgentSignalingClient
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private const int MaxSignalMessageBytes = 256 * 1024;

    private readonly AgentOptions _options;
    private readonly ScreenCaptureService _screenCapture;
    private readonly StreamQualityController _qualityController;
    private readonly WebRtcEncodingPlan _encodingPlan;
    private readonly ConcurrentDictionary<string, WebRtcPeerSession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private ClientWebSocket? _socket;

    public WebRtcAgentSignalingClient(
        AgentOptions options,
        ScreenCaptureService screenCapture,
        StreamQualityController qualityController)
    {
        _options = options;
        _screenCapture = screenCapture;
        _qualityController = qualityController;
        _encodingPlan = WebRtcEncodingPlan.Create(options, screenCapture.BackendPlan, _qualityController.Current);
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

internal sealed class WebRtcPeerSession
{
    private readonly string _sessionId;
    private readonly string _deviceId;
    private readonly Func<WebRtcSignalMessage, CancellationToken, Task> _sendSignalAsync;
    private readonly DesktopVideoSource _videoSource;
    private readonly RTCPeerConnection _peerConnection;
    private int _videoStarted;
    private int _closed;

    public WebRtcPeerSession(
        string sessionId,
        AgentOptions options,
        ScreenCaptureService screenCapture,
        StreamQualityController qualityController,
        WebRtcEncodingPlan encodingPlan,
        Func<WebRtcSignalMessage, CancellationToken, Task> sendSignalAsync)
    {
        _sessionId = sessionId;
        _deviceId = options.DeviceId;
        _sendSignalAsync = sendSignalAsync;
        _videoSource = new DesktopVideoSource(screenCapture, qualityController, encodingPlan);
        _peerConnection = CreatePeerConnection();
    }

    public async Task AcceptOfferAsync(WebRtcSignalMessage offer, CancellationToken cancellationToken)
    {
        var description = new RTCSessionDescriptionInit
        {
            type = ParseSdpType(offer.SdpType, RTCSdpType.offer),
            sdp = offer.Sdp
        };

        var result = _peerConnection.setRemoteDescription(description);
        if (result != SetDescriptionResultEnum.OK)
        {
            throw new InvalidOperationException($"WebRTC remote description failed: {result}.");
        }

        var answer = _peerConnection.createAnswer(null);
        await _peerConnection.setLocalDescription(answer);

        await _sendSignalAsync(
            new WebRtcSignalMessage
            {
                Type = OwnDeskMessageTypes.WebRtcAnswer,
                SessionId = _sessionId,
                DeviceId = _deviceId,
                SdpType = answer.type.ToString(),
                Sdp = answer.sdp
            },
            cancellationToken);
    }

    public void AddIceCandidate(WebRtcSignalMessage signal)
    {
        if (signal.Candidate is not { } candidateElement)
        {
            return;
        }

        var candidateJson = candidateElement.GetRawText();
        if (!RTCIceCandidateInit.TryParse(candidateJson, out var candidate))
        {
            candidate = JsonSerializer.Deserialize<RTCIceCandidateInit>(candidateJson, JsonDefaults.Options);
        }

        if (!string.IsNullOrWhiteSpace(candidate?.candidate))
        {
            _peerConnection.addIceCandidate(candidate);
        }
    }

    public async Task CloseAsync(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        try
        {
            _peerConnection.Close(reason);
        }
        catch (Exception)
        {
        }

        await StopVideoSourceAsync();
        _videoSource.Dispose();
        _peerConnection.Dispose();
    }

    private RTCPeerConnection CreatePeerConnection()
    {
        var peerConnection = new RTCPeerConnection();
        var videoTrack = new MediaStreamTrack(_videoSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        peerConnection.addTrack(videoTrack);

        _videoSource.OnVideoSourceEncodedSample += peerConnection.SendVideo;
        _videoSource.OnVideoSourceError += message => Console.WriteLine(message);
        peerConnection.OnVideoFormatsNegotiated += formats =>
        {
            if (formats.Count > 0)
            {
                _videoSource.SetVideoSourceFormat(formats[0]);
            }
        };

        peerConnection.onicecandidate += candidate =>
        {
            if (candidate is null)
            {
                return;
            }

            _ = SendIceCandidateAsync(candidate);
        };

        peerConnection.onconnectionstatechange += state =>
        {
            _ = HandleConnectionStateChangedAsync(state);
        };

        return peerConnection;
    }

    private async Task SendIceCandidateAsync(RTCIceCandidate candidate)
    {
        try
        {
            var candidateElement = JsonSerializer.Deserialize<JsonElement>(candidate.toJSON(), JsonDefaults.Options);
            await _sendSignalAsync(
                new WebRtcSignalMessage
                {
                    Type = OwnDeskMessageTypes.WebRtcIce,
                    SessionId = _sessionId,
                    DeviceId = _deviceId,
                    Candidate = candidateElement
                },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebRTC ICE send error: {ex.Message}");
        }
    }

    private async Task HandleConnectionStateChangedAsync(RTCPeerConnectionState state)
    {
        Console.WriteLine($"WebRTC peer {_sessionId} state: {state}");

        try
        {
            switch (state)
            {
                case RTCPeerConnectionState.connected:
                    await StartVideoSourceAsync();
                    break;
                case RTCPeerConnectionState.failed:
                    _peerConnection.Close("ice failed");
                    break;
                case RTCPeerConnectionState.disconnected:
                    break;
                case RTCPeerConnectionState.closed:
                    await StopVideoSourceAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebRTC peer state handler error: {ex.Message}");
        }
    }

    private async Task StartVideoSourceAsync()
    {
        if (Interlocked.Exchange(ref _videoStarted, 1) == 1)
        {
            return;
        }

        try
        {
            await _videoSource.StartVideo();
        }
        catch
        {
            Interlocked.Exchange(ref _videoStarted, 0);
            throw;
        }
    }

    private async Task StopVideoSourceAsync()
    {
        try
        {
            await _videoSource.CloseVideo();
        }
        catch (Exception)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _videoStarted, 0);
        }
    }

    private static RTCSdpType ParseSdpType(string? sdpType, RTCSdpType fallback)
    {
        return Enum.TryParse<RTCSdpType>(sdpType, ignoreCase: true, out var parsed) ? parsed : fallback;
    }
}
