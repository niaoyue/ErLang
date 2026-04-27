using System.Text.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;
using OwnDesk.Shared.Security;
using OwnDesk.Shared.Transport;
using Xunit;

namespace OwnDesk.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void AuthAcceptsExactAccountAndToken()
    {
        var auth = new SingleAccountAuthenticator("demo", "secret");

        Assert.True(auth.IsAuthorized("demo", "secret"));
    }

    [Fact]
    public void AuthRejectsWrongToken()
    {
        var auth = new SingleAccountAuthenticator("demo", "secret");

        Assert.False(auth.IsAuthorized("demo", "bad"));
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
    public void AuthMessageJsonRoundTrip()
    {
        var message = new AuthMessage
        {
            Account = "demo",
            Token = "secret",
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
}
