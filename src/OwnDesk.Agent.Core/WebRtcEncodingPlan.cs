namespace OwnDesk.Agent;

internal sealed record WebRtcEncodingPlan(
    string RequestedCodec,
    string SelectedCodec,
    string Mode,
    string CaptureBackend,
    string EncoderName,
    bool HardwareEncoding,
    int TargetKbps,
    string[] AdvertisedCodecs,
    string[] Notes)
{
    public static WebRtcEncodingPlan Create(
        AgentOptions options,
        ScreenCaptureBackendPlan captureBackendPlan,
        StreamQualitySettings qualitySettings)
    {
        var requestedCodec = options.WebRtcCodec.ToString().ToUpperInvariant();
        var notes = new List<string>(captureBackendPlan.Notes);

        if (options.WebRtcCodec is WebRtcCodecPreference.H264 or WebRtcCodecPreference.Av1)
        {
            notes.Add($"{requestedCodec} was requested, but the current WebRTC media path only has a VP8/libvpx encoder wired in.");
            notes.Add("Falling back to VP8 software encoding until the Windows Graphics Capture/DXGI + hardware encoder path is implemented.");
        }

        if (options.WebRtcCodec is WebRtcCodecPreference.Auto)
        {
            notes.Add("Auto currently selects VP8 because H.264/AV1 hardware encoding is not wired into the WebRTC sender yet.");
        }

        return new WebRtcEncodingPlan(
            RequestedCodec: requestedCodec,
            SelectedCodec: "VP8",
            Mode: "sipsorcery-vp8-desktop",
            CaptureBackend: captureBackendPlan.CaptureBackendId,
            EncoderName: "libvpx-vp8-software",
            HardwareEncoding: false,
            TargetKbps: qualitySettings.WebRtcBitrateKbps,
            AdvertisedCodecs: ["VP8"],
            Notes: notes.ToArray());
    }
}
