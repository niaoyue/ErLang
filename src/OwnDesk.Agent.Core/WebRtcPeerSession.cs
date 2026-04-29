using System.Text;
using System.Text.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;
using SIPSorcery.Net;

namespace OwnDesk.Agent;

internal sealed class WebRtcPeerSession
{
    private readonly string _sessionId;
    private readonly string _deviceId;
    private readonly Func<WebRtcSignalMessage, CancellationToken, Task> _sendSignalAsync;
    private readonly DesktopVideoSource _videoSource;
    private readonly RemoteControlHandler _controlHandler;
    private readonly WebRtcMediaState _mediaState;
    private readonly RTCPeerConnection _peerConnection;
    private readonly WebRtcConfigDto _webRtcConfig;
    private int _videoStarted;
    private int _mediaStateRegistered;
    private int _videoInterrupted;
    private int _closed;

    public WebRtcPeerSession(
        string sessionId,
        AgentOptions options,
        ScreenCaptureService screenCapture,
        StreamQualityController qualityController,
        WebRtcEncodingPlan encodingPlan,
        RemoteControlHandler controlHandler,
        WebRtcMediaState mediaState,
        WebRtcConfigDto webRtcConfig,
        Func<WebRtcSignalMessage, CancellationToken, Task> sendSignalAsync)
    {
        _sessionId = sessionId;
        _deviceId = options.DeviceId;
        _sendSignalAsync = sendSignalAsync;
        _controlHandler = controlHandler;
        _mediaState = mediaState;
        _webRtcConfig = webRtcConfig;
        _videoSource = new DesktopVideoSource(screenCapture, qualityController, encodingPlan, controlHandler.UpdateFrameSize);
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
        ReleaseMediaState();
        _videoSource.Dispose();
        _peerConnection.Dispose();
    }

    private RTCPeerConnection CreatePeerConnection()
    {
        var peerConnection = new RTCPeerConnection(CreateConfiguration());
        var videoTrack = new MediaStreamTrack(_videoSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        peerConnection.addTrack(videoTrack);

        _videoSource.OnVideoSourceEncodedSample += (duration, sample) =>
        {
            RegisterMediaState();
            peerConnection.SendVideo(duration, sample);
        };
        _videoSource.OnVideoSourceError += message =>
        {
            Console.WriteLine(message);
        };
        _videoSource.OnVideoSourceInterrupted += () =>
        {
            _ = Task.Run(HandleVideoSourceInterruptedAsync);
        };
        peerConnection.OnVideoFormatsNegotiated += formats =>
        {
            if (formats.Count > 0)
            {
                _videoSource.SetVideoSourceFormat(formats[0]);
            }
        };

        peerConnection.ondatachannel += channel =>
        {
            Console.WriteLine($"WebRTC data channel received: {channel.label}");
            channel.onopen += () => Console.WriteLine($"WebRTC data channel open: {channel.label}");
            channel.onmessage += (_, protocol, data) =>
            {
                if (protocol == DataChannelPayloadProtocols.WebRTC_String)
                {
                    try
                    {
                        _controlHandler.HandleJson(Encoding.UTF8.GetString(data));
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"WebRTC data channel message rejected: {ex.Message}");
                    }
                }
            };
            channel.onerror += error => Console.WriteLine($"WebRTC data channel error: {error}");
            channel.onclose += () => Console.WriteLine($"WebRTC data channel closed: {channel.label}");
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

    private RTCConfiguration CreateConfiguration()
    {
        return new RTCConfiguration
        {
            iceServers = _webRtcConfig.IceServers
                .SelectMany(ToRtcIceServers)
                .ToList(),
            iceTransportPolicy = ParseIceTransportPolicy(_webRtcConfig.IceTransportPolicy),
            X_ICEIncludeAllInterfaceAddresses = true,
            X_GatherTimeoutMs = 5000
        };
    }

    private static IEnumerable<RTCIceServer> ToRtcIceServers(WebRtcIceServerDto server)
    {
        foreach (var url in server.Urls)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            yield return new RTCIceServer
            {
                urls = url.Trim(),
                username = server.Username ?? string.Empty,
                credential = server.Credential ?? string.Empty
            };
        }
    }

    private static RTCIceTransportPolicy ParseIceTransportPolicy(string? policy)
    {
        return policy?.Equals("relay", StringComparison.OrdinalIgnoreCase) == true
            ? RTCIceTransportPolicy.relay
            : RTCIceTransportPolicy.all;
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
                    await StopVideoSourceAsync();
                    ReleaseMediaState();
                    _peerConnection.Close("ice failed");
                    break;
                case RTCPeerConnectionState.disconnected:
                    await StopVideoSourceAsync();
                    ReleaseMediaState();
                    break;
                case RTCPeerConnectionState.closed:
                    await StopVideoSourceAsync();
                    ReleaseMediaState();
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

    private void RegisterMediaState()
    {
        if (Interlocked.Exchange(ref _mediaStateRegistered, 1) == 0)
        {
            _mediaState.AddVideoSession();
        }
    }

    private bool ReleaseMediaState()
    {
        if (Interlocked.Exchange(ref _mediaStateRegistered, 0) == 1)
        {
            _mediaState.RemoveVideoSession();
            return true;
        }

        return false;
    }

    private async Task HandleVideoSourceInterruptedAsync()
    {
        ReleaseMediaState();
        if (Interlocked.Exchange(ref _videoInterrupted, 1) == 1 ||
            Volatile.Read(ref _closed) == 1)
        {
            return;
        }

        await StopVideoSourceAsync();
        await _sendSignalAsync(
            new WebRtcSignalMessage
            {
                Type = OwnDeskMessageTypes.WebRtcError,
                SessionId = _sessionId,
                DeviceId = _deviceId,
                Message = "WebRTC video source interrupted; using JPEG fallback."
            },
            CancellationToken.None);

        try
        {
            _peerConnection.Close("video source interrupted");
        }
        catch (Exception)
        {
        }
    }

    private static RTCSdpType ParseSdpType(string? sdpType, RTCSdpType fallback)
    {
        return Enum.TryParse<RTCSdpType>(sdpType, ignoreCase: true, out var parsed) ? parsed : fallback;
    }
}
