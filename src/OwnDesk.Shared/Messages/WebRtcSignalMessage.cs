using System.Text.Json;

namespace OwnDesk.Shared.Messages;

public sealed class WebRtcSignalMessage
{
    public string Type { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public string? SdpType { get; set; }

    public string? Sdp { get; set; }

    public JsonElement? Candidate { get; set; }

    public string? Message { get; set; }

    public string[] Codecs { get; set; } = [];

    public bool HardwareEncoding { get; set; }

    public string Mode { get; set; } = string.Empty;

    public string RequestedCodec { get; set; } = string.Empty;

    public string SelectedCodec { get; set; } = string.Empty;

    public string CaptureBackend { get; set; } = string.Empty;

    public string RequestedCaptureBackend { get; set; } = string.Empty;

    public string SelectedCaptureBackend { get; set; } = string.Empty;

    public string EncoderName { get; set; } = string.Empty;

    public int TargetKbps { get; set; }

    public string[] Notes { get; set; } = [];
}
