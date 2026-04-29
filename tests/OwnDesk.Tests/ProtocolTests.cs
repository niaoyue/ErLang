using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OwnDesk.Agent;
using OwnDesk.Server;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;
using OwnDesk.Shared.Security;
using OwnDesk.Shared.Transport;
using Xunit;

namespace OwnDesk.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void AuthRegistersAndAuthenticatesMember()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var auth = new OrganizationAuthenticator("org-secret", storePath);

        var session = auth.Register(new RegisterMemberRequest
        {
            OrganizationToken = "org-secret",
            Username = "demo",
            Password = "password-123"
        });

        var passwordMember = auth.Authenticate(new AuthMessage
        {
            Account = "demo",
            Token = "org-secret",
            Password = "password-123"
        });
        var sessionMember = auth.Authenticate(new AuthMessage
        {
            Account = "demo",
            Token = "org-secret",
            SessionToken = session.SessionToken
        });

        Assert.NotNull(passwordMember);
        Assert.NotNull(sessionMember);
        Assert.Equal(passwordMember.OrganizationId, sessionMember.OrganizationId);
    }

    [Fact]
    public void AuthRejectsWrongOrganizationToken()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var auth = new OrganizationAuthenticator("org-secret", storePath);
        auth.Register(new RegisterMemberRequest
        {
            OrganizationToken = "org-secret",
            Username = "demo",
            Password = "password-123"
        });

        var member = auth.Authenticate(new AuthMessage
        {
            Account = "demo",
            Token = "bad",
            Password = "password-123"
        });

        Assert.Null(member);
    }

    [Fact]
    public void WebSocketUriMapsHttpToWsAndEscapesQuery()
    {
        var uri = EndpointUris.BuildWebSocketUri(
            "http://localhost:5080/base",
            "/ws/agent",
            new Dictionary<string, string>
            {
                ["account"] = "demo user",
                ["token"] = "a+b"
            });

        Assert.Equal("ws", uri.Scheme);
        Assert.Equal("localhost", uri.Host);
        Assert.Equal(5080, uri.Port);
        Assert.Equal("/base/ws/agent", uri.AbsolutePath);
        Assert.Contains("account=demo%20user", uri.Query, StringComparison.Ordinal);
        Assert.Contains("token=a%2Bb", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceRegistryKeepsOfflineDeviceUntilRemoved()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var registry = new DeviceRegistry(new JsonDeviceRecordStore(storePath));
        var socket = new TestWebSocket();

        var session = await registry.RegisterAgentAsync(
            "org-1",
            "pc-1",
            "PC 1",
            socket,
            CancellationToken.None);
        await registry.UpdateHelloAsync(session, "PC 1", 1920, 1080, CancellationToken.None);

        var online = Assert.Single(registry.ListDevices("org-1"));
        Assert.True(online.Online);
        Assert.Equal("PC 1", online.DeviceName);

        await registry.UnregisterAgentAsync(session, CancellationToken.None);

        var offline = Assert.Single(registry.ListDevices("org-1"));
        Assert.False(offline.Online);
        Assert.Equal(1920, offline.ScreenWidth);

        var restarted = new DeviceRegistry(new JsonDeviceRecordStore(storePath));
        var persisted = Assert.Single(restarted.ListDevices("org-1"));
        Assert.False(persisted.Online);

        Assert.True(await restarted.RemoveDeviceAsync("org-1", "pc-1", CancellationToken.None));
        Assert.Empty(restarted.ListDevices("org-1"));
    }

    [Fact]
    public async Task DeviceRegistryNotifiesWatchersWhenAgentRequestIsCanceled()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var registry = new DeviceRegistry(new JsonDeviceRecordStore(storePath));
        var watcherSocket = new RecordingWebSocket();
        registry.AddWatcher("org-1", watcherSocket);

        var session = await registry.RegisterAgentAsync(
            "org-1",
            "pc-1",
            "PC 1",
            new TestWebSocket(),
            CancellationToken.None);
        var onlineNotifications = watcherSocket.SentText.Count;

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await registry.UnregisterAgentAsync(session, canceled.Token);

        Assert.True(watcherSocket.SentText.Count > onlineNotifications);
        Assert.Contains(watcherSocket.SentText, text => text.Contains(OwnDeskMessageTypes.DeviceListChanged, StringComparison.Ordinal));
    }

    [Fact]
    public void AuthMessageJsonRoundTrip()
    {
        var message = new AuthMessage
        {
            Account = "demo",
            Token = "secret",
            Password = "password-123",
            SessionToken = "viewer-session",
            DeviceId = "pc-1",
            DeviceName = "PC 1",
            SessionId = "session-1"
        };

        var json = JsonSerializer.Serialize(message, JsonDefaults.Options);
        var parsed = JsonSerializer.Deserialize<AuthMessage>(json, JsonDefaults.Options);

        Assert.NotNull(parsed);
        Assert.Equal(OwnDeskMessageTypes.Auth, parsed.Type);
        Assert.Equal("demo", parsed.Account);
        Assert.Equal("secret", parsed.Token);
        Assert.Equal("password-123", parsed.Password);
        Assert.Equal("viewer-session", parsed.SessionToken);
        Assert.Equal("pc-1", parsed.DeviceId);
        Assert.Equal("PC 1", parsed.DeviceName);
        Assert.Equal("session-1", parsed.SessionId);
    }

    [Fact]
    public void FrameMessageJsonRoundTrip()
    {
        var frame = new FrameMessage
        {
            Sequence = 12,
            Width = 800,
            Height = 600,
            CapturedAtUtc = DateTimeOffset.Parse("2026-04-26T10:00:00Z"),
            ImageBase64 = Convert.ToBase64String([1, 2, 3, 4])
        };

        var json = JsonSerializer.Serialize(frame, JsonDefaults.Options);
        var parsed = JsonSerializer.Deserialize<FrameMessage>(json, JsonDefaults.Options);

        Assert.NotNull(parsed);
        Assert.Equal(OwnDeskMessageTypes.Frame, parsed.Type);
        Assert.Equal(12, parsed.Sequence);
        Assert.Equal(800, parsed.Width);
        Assert.Equal(600, parsed.Height);
        Assert.Equal(frame.ImageBase64, parsed.ImageBase64);
    }

    [Fact]
    public void TextInputCommandJsonRoundTrip()
    {
        var command = new InputCommand
        {
            Event = "text",
            Text = "hello 输入"
        };

        var json = JsonSerializer.Serialize(command, JsonDefaults.Options);
        var parsed = JsonSerializer.Deserialize<InputCommand>(json, JsonDefaults.Options);

        Assert.NotNull(parsed);
        Assert.Equal(OwnDeskMessageTypes.Input, parsed.Type);
        Assert.Equal("text", parsed.Event);
        Assert.Equal("hello 输入", parsed.Text);
    }

    [Fact]
    public void StreamQualityMessageJsonRoundTrip()
    {
        var message = new StreamQualityMessage
        {
            Profile = "quality"
        };

        var json = JsonSerializer.Serialize(message, JsonDefaults.Options);
        var parsed = JsonSerializer.Deserialize<StreamQualityMessage>(json, JsonDefaults.Options);

        Assert.NotNull(parsed);
        Assert.Equal(OwnDeskMessageTypes.StreamQuality, parsed.Type);
        Assert.Equal("quality", parsed.Profile);
    }

    [Fact]
    public void BinaryFrameCodecRoundTrip()
    {
        var header = new BinaryFrameHeader
        {
            Sequence = 7,
            Width = 1280,
            Height = 720,
            CapturedAtUtc = DateTimeOffset.Parse("2026-04-26T10:30:00Z"),
            ByteLength = 4
        };
        byte[] imageBytes = [10, 20, 30, 40];

        var payload = BinaryFrameCodec.Encode(header, imageBytes);

        Assert.True(BinaryFrameCodec.TryDecode(payload, out var parsedHeader, out var parsedImageBytes));
        Assert.Equal(OwnDeskMessageTypes.Frame, parsedHeader.Type);
        Assert.Equal(7, parsedHeader.Sequence);
        Assert.Equal(1280, parsedHeader.Width);
        Assert.Equal(720, parsedHeader.Height);
        Assert.Equal(imageBytes.Length, parsedHeader.ByteLength);
        Assert.True(parsedImageBytes.SequenceEqual(imageBytes));
    }

    [Fact]
    public void WebRtcSignalMessageJsonRoundTrip()
    {
        var candidate = JsonSerializer.Deserialize<JsonElement>(
            "{\"candidate\":\"candidate:1 1 udp 2122260223 192.168.1.2 5000 typ host\",\"sdpMid\":\"0\",\"sdpMLineIndex\":0}");
        var signal = new WebRtcSignalMessage
        {
            Type = OwnDeskMessageTypes.WebRtcOffer,
            SessionId = "session-1",
            DeviceId = "pc-1",
            SdpType = "offer",
            Sdp = "v=0",
            Candidate = candidate,
            Codecs = ["VP8"],
            HardwareEncoding = false,
            Mode = "sipsorcery-vp8-desktop",
            RequestedCodec = "AUTO",
            SelectedCodec = "VP8",
            CaptureBackend = "gdi-copyfromscreen-bgra",
            RequestedCaptureBackend = "AUTO",
            SelectedCaptureBackend = "GDI",
            EncoderName = "libvpx-vp8-software",
            TargetKbps = 1200,
            Notes = ["Auto currently selects VP8."]
        };

        var json = JsonSerializer.Serialize(signal, JsonDefaults.Options);
        var parsed = JsonSerializer.Deserialize<WebRtcSignalMessage>(json, JsonDefaults.Options);

        Assert.NotNull(parsed);
        Assert.Equal(OwnDeskMessageTypes.WebRtcOffer, parsed.Type);
        Assert.Equal("session-1", parsed.SessionId);
        Assert.Equal("pc-1", parsed.DeviceId);
        Assert.Equal("offer", parsed.SdpType);
        Assert.Equal("v=0", parsed.Sdp);
        Assert.True(parsed.Candidate.HasValue);
        Assert.StartsWith("candidate:", parsed.Candidate.Value.GetProperty("candidate").GetString());
        Assert.True(parsed.Codecs.SequenceEqual(["VP8"]));
        Assert.Equal("sipsorcery-vp8-desktop", parsed.Mode);
        Assert.Equal("AUTO", parsed.RequestedCodec);
        Assert.Equal("VP8", parsed.SelectedCodec);
        Assert.Equal("gdi-copyfromscreen-bgra", parsed.CaptureBackend);
        Assert.Equal("AUTO", parsed.RequestedCaptureBackend);
        Assert.Equal("GDI", parsed.SelectedCaptureBackend);
        Assert.Equal("libvpx-vp8-software", parsed.EncoderName);
        Assert.Equal(1200, parsed.TargetKbps);
        Assert.True(parsed.Notes.SequenceEqual(["Auto currently selects VP8."]));
    }

    [Fact]
    public void WebRtcConfigMessageJsonRoundTrip()
    {
        var config = new WebRtcConfigDto
        {
            IceServers =
            [
                new WebRtcIceServerDto
                {
                    Urls = ["stun:stun.example.com:3478"],
                    Username = "user",
                    Credential = "pass",
                    CredentialType = "password"
                }
            ],
            IceTransportPolicy = "relay",
            RelayConfigured = true
        };

        var json = JsonSerializer.Serialize(config, JsonDefaults.Options);
        var parsed = JsonSerializer.Deserialize<WebRtcConfigDto>(json, JsonDefaults.Options);

        Assert.NotNull(parsed);
        var server = Assert.Single(parsed.IceServers);
        Assert.Equal("stun:stun.example.com:3478", Assert.Single(server.Urls));
        Assert.Equal("user", server.Username);
        Assert.Equal("pass", server.Credential);
        Assert.Equal("password", server.CredentialType);
        Assert.Equal("relay", parsed.IceTransportPolicy);
        Assert.True(parsed.RelayConfigured);
    }

    [Fact]
    public void WebRtcConfigProviderReadsIceServers()
    {
        var oldServers = Environment.GetEnvironmentVariable("OWNDESK_WEBRTC_ICE_SERVERS");
        var oldPolicy = Environment.GetEnvironmentVariable("OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY");

        try
        {
            Environment.SetEnvironmentVariable("OWNDESK_WEBRTC_ICE_SERVERS", null);
            Environment.SetEnvironmentVariable("OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY", null);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OwnDesk:IceServers:0:Urls:0"] = "stun:stun.example.com:3478",
                    ["OwnDesk:IceServers:1:Urls:0"] = "turn:turn.example.com:3478?transport=udp",
                    ["OwnDesk:IceServers:1:Username"] = "turn-user",
                    ["OwnDesk:IceServers:1:Credential"] = "turn-pass",
                    ["OwnDesk:IceTransportPolicy"] = "relay"
                })
                .Build();

            var config = new WebRtcConfigProvider(configuration).GetConfig();

            Assert.Equal(2, config.IceServers.Length);
            Assert.Equal("stun:stun.example.com:3478", Assert.Single(config.IceServers[0].Urls));
            Assert.Equal("turn-user", config.IceServers[1].Username);
            Assert.Equal("turn-pass", config.IceServers[1].Credential);
            Assert.Equal("relay", config.IceTransportPolicy);
            Assert.True(config.RelayConfigured);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OWNDESK_WEBRTC_ICE_SERVERS", oldServers);
            Environment.SetEnvironmentVariable("OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY", oldPolicy);
        }
    }

    [Fact]
    public void WebRtcConfigProviderReadsEnvironmentIceServers()
    {
        var oldServers = Environment.GetEnvironmentVariable("OWNDESK_WEBRTC_ICE_SERVERS");
        var oldPolicy = Environment.GetEnvironmentVariable("OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY");

        try
        {
            Environment.SetEnvironmentVariable(
                "OWNDESK_WEBRTC_ICE_SERVERS",
                "stun:stun.example.com:3478|turn:turn.example.com:3478?transport=udp;turn-user;turn-pass");
            Environment.SetEnvironmentVariable("OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY", "relay");

            var config = new WebRtcConfigProvider(new ConfigurationBuilder().Build()).GetConfig();

            Assert.Equal(2, config.IceServers.Length);
            Assert.Equal("stun:stun.example.com:3478", Assert.Single(config.IceServers[0].Urls));
            Assert.Equal("turn:turn.example.com:3478?transport=udp", Assert.Single(config.IceServers[1].Urls));
            Assert.Equal("turn-user", config.IceServers[1].Username);
            Assert.Equal("turn-pass", config.IceServers[1].Credential);
            Assert.Equal("relay", config.IceTransportPolicy);
            Assert.True(config.RelayConfigured);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OWNDESK_WEBRTC_ICE_SERVERS", oldServers);
            Environment.SetEnvironmentVariable("OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY", oldPolicy);
        }
    }

    [Fact]
    public void WebRtcConfigProviderDoesNotForceRelayWithoutTurnServer()
    {
        var oldServers = Environment.GetEnvironmentVariable("OWNDESK_WEBRTC_ICE_SERVERS");
        var oldPolicy = Environment.GetEnvironmentVariable("OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY");

        try
        {
            Environment.SetEnvironmentVariable("OWNDESK_WEBRTC_ICE_SERVERS", null);
            Environment.SetEnvironmentVariable("OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY", null);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OwnDesk:IceServers:0:Urls:0"] = "stun:stun.example.com:3478",
                    ["OwnDesk:IceTransportPolicy"] = "relay"
                })
                .Build();

            var config = new WebRtcConfigProvider(configuration).GetConfig();

            Assert.Equal("all", config.IceTransportPolicy);
            Assert.False(config.RelayConfigured);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OWNDESK_WEBRTC_ICE_SERVERS", oldServers);
            Environment.SetEnvironmentVariable("OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY", oldPolicy);
        }
    }

    [Fact]
    public void FrameChangeDetectorPublishesSmallSampledChange()
    {
        var detector = new FrameChangeDetector();
        var startedAt = DateTimeOffset.Parse("2026-04-29T00:00:00Z");

        Assert.True(detector.ShouldPublish(CreateRawFrame(32, 18), startedAt));
        Assert.False(detector.ShouldPublish(CreateRawFrame(32, 18), startedAt.AddMilliseconds(200)));
        Assert.False(detector.ShouldPublish(CreateRawFrame(32, 18, (0, 0, 9, 9, 9)), startedAt.AddMilliseconds(400)));
        Assert.True(detector.ShouldPublish(CreateRawFrame(32, 18, (0, 0, 10, 10, 10)), startedAt.AddMilliseconds(600)));
    }

    [Fact]
    public void FrameChangeDetectorRefreshesStaticFramePeriodically()
    {
        var detector = new FrameChangeDetector();
        var startedAt = DateTimeOffset.Parse("2026-04-29T00:00:00Z");
        var frame = CreateRawFrame(32, 18);

        Assert.True(detector.ShouldPublish(frame, startedAt));
        Assert.False(detector.ShouldPublish(frame, startedAt.AddMilliseconds(2400)));
        Assert.True(detector.ShouldPublish(frame, startedAt.AddMilliseconds(2500)));
    }

    [Fact]
    public void WebRtcMediaStateTracksActiveSessionsWithoutUnderflow()
    {
        var state = new WebRtcMediaState();

        Assert.False(state.HasActiveVideo);
        state.RemoveVideoSession();
        Assert.False(state.HasActiveVideo);

        state.AddVideoSession();
        state.AddVideoSession();
        Assert.True(state.HasActiveVideo);

        state.RemoveVideoSession();
        Assert.True(state.HasActiveVideo);
        state.RemoveVideoSession();
        Assert.False(state.HasActiveVideo);
        state.RemoveVideoSession();
        Assert.False(state.HasActiveVideo);
    }

    private static ScreenRawFrame CreateRawFrame(
        int width,
        int height,
        params (int X, int Y, byte R, byte G, byte B)[] pixels)
    {
        var bytes = new byte[width * height * 4];
        foreach (var pixel in pixels)
        {
            var offset = ((pixel.Y * width) + pixel.X) * 4;
            bytes[offset] = pixel.B;
            bytes[offset + 1] = pixel.G;
            bytes[offset + 2] = pixel.R;
            bytes[offset + 3] = 255;
        }

        return new ScreenRawFrame(width, height, bytes);
    }

    private class TestWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _state = WebSocketState.Closed;
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingWebSocket : TestWebSocket
    {
        public List<string> SentText { get; } = [];

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            if (messageType == WebSocketMessageType.Text)
            {
                SentText.Add(System.Text.Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            }

            return Task.CompletedTask;
        }
    }
}
