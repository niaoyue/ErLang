namespace OwnDesk.Agent;

internal sealed record StreamQualitySettings(
    StreamQualityProfile Profile,
    int FramesPerSecond,
    long JpegQuality,
    int MaxWidth,
    int MaxHeight,
    int WebRtcBitrateKbps)
{
    public TimeSpan FrameDelay => TimeSpan.FromMilliseconds(1000.0 / FramesPerSecond);

    public uint FrameDurationRtpUnits(int clockRate)
    {
        return Math.Max(1, (uint)Math.Round((double)clockRate / FramesPerSecond));
    }

    public static StreamQualitySettings FromOptions(AgentOptions options)
    {
        return new StreamQualitySettings(
            options.QualityProfile,
            options.FramesPerSecond,
            options.JpegQuality,
            options.MaxWidth,
            options.MaxHeight,
            options.WebRtcBitrateKbps);
    }

    public static StreamQualitySettings FromProfile(StreamQualityProfile profile)
    {
        return profile switch
        {
            StreamQualityProfile.Smooth => new StreamQualitySettings(profile, 3, 42, 960, 540, 700),
            StreamQualityProfile.Quality => new StreamQualitySettings(profile, 8, 68, 1600, 900, 2500),
            StreamQualityProfile.Ultra => new StreamQualitySettings(profile, 12, 78, 2560, 1440, 4500),
            _ => new StreamQualitySettings(StreamQualityProfile.Balanced, 5, 55, 1280, 720, 1200)
        };
    }
}

internal sealed class StreamQualityController
{
    private readonly object _gate = new();
    private StreamQualitySettings _current;

    public StreamQualityController(AgentOptions options)
    {
        _current = StreamQualitySettings.FromOptions(options);
    }

    public StreamQualitySettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool TryApplyProfile(string profileName, out StreamQualitySettings settings)
    {
        if (!Enum.TryParse<StreamQualityProfile>(profileName, ignoreCase: true, out var profile) ||
            profile == StreamQualityProfile.Custom)
        {
            settings = Current;
            return false;
        }

        settings = StreamQualitySettings.FromProfile(profile);
        lock (_gate)
        {
            _current = settings;
        }

        return true;
    }
}
