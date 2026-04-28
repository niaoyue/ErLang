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
    private readonly RTCPeerConnection _peerConnection;
    private readonly IReadOnlyList<WebRtcIceServerDto> _iceServers;
    private int _videoStarted;
    private int _closed;

    public WebRtcPeerSession(
        string sessionId,
        AgentOptions options,
        ScreenCaptureService screenCapture,
        StreamQualityController qualityController,
        WebRtcEncodingPlan encodingPlan,
        RemoteControlHandler controlHandler,
        IReadOnlyList<WebRtcIceServerDto> iceServers,
        Func<WebRtcSignalMessage, CancellationToken, Task> sendSignalAsync)
    {
        _sessionId = sessionId;
        _deviceId = options.DeviceId;
        _sendSignalAsync = sendSignalAsync;
        _controlHandler = controlHandler;
        _iceServers = iceServers;
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
        var peerConnection = new RTCPeerConnection(CreateConfiguration());
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
            iceServers = _iceServers
                .Where(server => server.Urls.Length > 0)
                .Select(server => new RTCIceServer
                {
                    urls = string.Join(",", server.Urls),
                    username = server.Username,
                    credential = server.Credential
                })
                .ToList(),
            X_ICEIncludeAllInterfaceAddresses = true,
            X_GatherTimeoutMs = 5000
        };
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
