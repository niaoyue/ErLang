namespace OwnDesk.Agent;

internal sealed record AgentOptions(
    string Server,
    string Account,
    string Token,
    string DeviceId,
    string DeviceName,
    int FramesPerSecond,
    long JpegQuality,
    int MaxWidth,
    int MaxHeight,
    bool EnableWebRtc,
    StreamQualityProfile QualityProfile,
    WebRtcCodecPreference WebRtcCodec,
    int WebRtcBitrateKbps,
    ScreenCaptureBackendPreference CaptureBackend)
{
    public static AgentOptions Parse(string[] args)
    {
        var switches = ParseSwitches(args);

        var server = Read("server", "OWNDESK_SERVER", "http://127.0.0.1:5080");
        var account = Read("account", "OWNDESK_ACCOUNT", "demo");
        var token = Read("token", "OWNDESK_TOKEN", string.Empty);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("OwnDesk token is required. Pass --token or set OWNDESK_TOKEN.");
        }
        var deviceId = Read("device-id", "OWNDESK_DEVICE_ID", Environment.MachineName);
        var deviceName = Read("device-name", "OWNDESK_DEVICE_NAME", Environment.MachineName);
        var enableWebRtc = ReadBool("webrtc", "OWNDESK_WEBRTC", true);
        var requestedQualityProfile = ReadQualityProfile("quality-profile", "OWNDESK_QUALITY_PROFILE", StreamQualityProfile.Balanced);
        var profileExplicit = HasExplicitValue("quality-profile", "OWNDESK_QUALITY_PROFILE");
        var streamSettingExplicit =
            HasExplicitValue("fps", "OWNDESK_FPS") ||
            HasExplicitValue("quality", "OWNDESK_JPEG_QUALITY") ||
            HasExplicitValue("max-width", "OWNDESK_MAX_WIDTH") ||
            HasExplicitValue("max-height", "OWNDESK_MAX_HEIGHT") ||
            HasExplicitValue("webrtc-bitrate-kbps", "OWNDESK_WEBRTC_BITRATE_KBPS");
        var baseline = StreamQualitySettings.FromProfile(profileExplicit ? requestedQualityProfile : StreamQualityProfile.Balanced);
        var fps = ReadInt("fps", "OWNDESK_FPS", baseline.FramesPerSecond, 1, 15);
        var quality = ReadInt("quality", "OWNDESK_JPEG_QUALITY", (int)baseline.JpegQuality, 20, 90);
        var maxWidth = ReadInt("max-width", "OWNDESK_MAX_WIDTH", baseline.MaxWidth, 320, 7680);
        var maxHeight = ReadInt("max-height", "OWNDESK_MAX_HEIGHT", baseline.MaxHeight, 240, 4320);
        var actualQualityProfile = profileExplicit
            ? requestedQualityProfile
            : streamSettingExplicit
                ? StreamQualityProfile.Custom
                : StreamQualityProfile.Balanced;
        var webRtcCodec = ReadCodec("webrtc-codec", "OWNDESK_WEBRTC_CODEC", WebRtcCodecPreference.Auto);
        var webRtcBitrateKbps = ReadInt("webrtc-bitrate-kbps", "OWNDESK_WEBRTC_BITRATE_KBPS", baseline.WebRtcBitrateKbps, 150, 20000);
        var captureBackend = ReadCaptureBackend("capture-backend", "OWNDESK_CAPTURE_BACKEND", ScreenCaptureBackendPreference.Auto);

        return new AgentOptions(
            server,
            account,
            token,
            deviceId,
            deviceName,
            fps,
            quality,
            maxWidth,
            maxHeight,
            enableWebRtc,
            actualQualityProfile,
            webRtcCodec,
            webRtcBitrateKbps,
            captureBackend);

        string Read(string key, string environmentName, string fallback)
        {
            if (switches.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var environmentValue = Environment.GetEnvironmentVariable(environmentName);
            return string.IsNullOrWhiteSpace(environmentValue) ? fallback : environmentValue;
        }

        int ReadInt(string key, string environmentName, int fallback, int min, int max)
        {
            var rawValue = Read(key, environmentName, fallback.ToString());
            return int.TryParse(rawValue, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
        }

        bool ReadBool(string key, string environmentName, bool fallback)
        {
            var rawValue = Read(key, environmentName, fallback ? "true" : "false");
            return rawValue.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   rawValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   rawValue.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   rawValue.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        WebRtcCodecPreference ReadCodec(string key, string environmentName, WebRtcCodecPreference fallback)
        {
            var rawValue = Read(key, environmentName, fallback.ToString());
            return Enum.TryParse<WebRtcCodecPreference>(rawValue, ignoreCase: true, out var parsed)
                ? parsed
                : fallback;
        }

        StreamQualityProfile ReadQualityProfile(string key, string environmentName, StreamQualityProfile fallback)
        {
            var rawValue = Read(key, environmentName, fallback.ToString());
            return Enum.TryParse<StreamQualityProfile>(rawValue, ignoreCase: true, out var parsed)
                ? parsed
                : fallback;
        }

        ScreenCaptureBackendPreference ReadCaptureBackend(
            string key,
            string environmentName,
            ScreenCaptureBackendPreference fallback)
        {
            var rawValue = Read(key, environmentName, fallback.ToString());
            return Enum.TryParse<ScreenCaptureBackendPreference>(rawValue, ignoreCase: true, out var parsed)
                ? parsed
                : fallback;
        }

        bool HasExplicitValue(string key, string environmentName)
        {
            return switches.ContainsKey(key) ||
                   !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentName));
        }
    }

    private static Dictionary<string, string> ParseSwitches(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var trimmed = arg[2..];
            var equalsIndex = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex >= 0)
            {
                values[trimmed[..equalsIndex]] = trimmed[(equalsIndex + 1)..];
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[trimmed] = args[++i];
            }
            else
            {
                values[trimmed] = "true";
            }
        }

        return values;
    }
}

internal enum WebRtcCodecPreference
{
    Auto,
    Vp8,
    H264,
    Av1
}

internal enum StreamQualityProfile
{
    Custom,
    Smooth,
    Balanced,
    Quality,
    Ultra
}

internal enum ScreenCaptureBackendPreference
{
    Auto,
    Gdi,
    Dxgi,
    Wgc
}
