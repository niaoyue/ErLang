using System.Diagnostics;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;

namespace OwnDesk.Agent;

internal sealed class DesktopVideoSource : IVideoSource, IDisposable
{
    private const int RtpVideoClockRate = 90000;

    private readonly ScreenCaptureService _screenCapture;
    private readonly StreamQualityController _qualityController;
    private readonly WebRtcEncodingPlan _encodingPlan;
    private readonly VpxVideoEncoder _encoder;
    private readonly object _stateGate = new();
    private readonly object _encoderGate = new();
    private readonly List<VideoFormat> _formats;
    private readonly FrameChangeDetector _changeDetector = new();
    private readonly Action<int, int>? _frameSizeChanged;

    private VideoFormat _selectedFormat;
    private CancellationTokenSource? _captureStopped;
    private Task? _captureTask;
    private int _currentTargetKbps;
    private bool _paused;
    private bool _disposed;
    private int _lastEncodedWidth;
    private int _lastEncodedHeight;
    private DateTimeOffset _lastSampleSentAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastErrorAt = DateTimeOffset.MinValue;

    public DesktopVideoSource(
        ScreenCaptureService screenCapture,
        StreamQualityController qualityController,
        WebRtcEncodingPlan encodingPlan,
        Action<int, int>? frameSizeChanged = null)
    {
        _screenCapture = screenCapture;
        _qualityController = qualityController;
        _encodingPlan = encodingPlan;
        _frameSizeChanged = frameSizeChanged;
        _currentTargetKbps = _encodingPlan.TargetKbps;
        _encoder = new VpxVideoEncoder
        {
            TargetKbps = (uint)_encodingPlan.TargetKbps
        };
        _formats = _encoder.SupportedFormats.Select(format => new VideoFormat(format)).ToList();
        _selectedFormat = _formats.Count > 0
            ? new VideoFormat(_formats[0])
            : new VideoFormat(VideoCodecsEnum.VP8, 96, RtpVideoClockRate, string.Empty);
    }

    public event EncodedSampleDelegate? OnVideoSourceEncodedSample;
    public event RawVideoSampleDelegate? OnVideoSourceRawSample;
    public event RawVideoSampleFasterDelegate? OnVideoSourceRawSampleFaster;
    public event SourceErrorDelegate? OnVideoSourceError;
    public event Action? OnVideoSourceInterrupted;

    public Task StartVideo()
    {
        lock (_stateGate)
        {
            ThrowIfDisposed();

            if (_captureTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _captureStopped?.Dispose();
            _captureStopped = new CancellationTokenSource();
            _lastSampleSentAt = DateTimeOffset.MinValue;
            _captureTask = Task.Run(() => CaptureLoopAsync(_captureStopped.Token));
            return Task.CompletedTask;
        }
    }

    public async Task CloseVideo()
    {
        Task? task;
        CancellationTokenSource? stopped;

        lock (_stateGate)
        {
            task = _captureTask;
            stopped = _captureStopped;
            _captureTask = null;
            _captureStopped = null;
        }

        if (stopped is not null)
        {
            await stopped.CancelAsync();
            stopped.Dispose();
        }

        if (task is not null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public Task PauseVideo()
    {
        _paused = true;
        return Task.CompletedTask;
    }

    public Task ResumeVideo()
    {
        _paused = false;
        return Task.CompletedTask;
    }

    public List<VideoFormat> GetVideoSourceFormats()
    {
        lock (_stateGate)
        {
            return _formats.Select(format => new VideoFormat(format)).ToList();
        }
    }

    public void SetVideoSourceFormat(VideoFormat videoFormat)
    {
        lock (_stateGate)
        {
            var matchIndex = _formats.FindIndex(format => format.Codec == videoFormat.Codec);
            _selectedFormat = matchIndex >= 0 ? new VideoFormat(_formats[matchIndex]) : new VideoFormat(videoFormat);
        }
    }

    public void RestrictFormats(Func<VideoFormat, bool> filter)
    {
        lock (_stateGate)
        {
            var filtered = _formats.Where(filter).Select(format => new VideoFormat(format)).ToList();
            if (filtered.Count == 0)
            {
                return;
            }

            _formats.Clear();
            _formats.AddRange(filtered);
            if (!_formats.Any(format => format.Codec == _selectedFormat.Codec))
            {
                _selectedFormat = new VideoFormat(_formats[0]);
            }
        }
    }

    public void ExternalVideoSourceRawSample(
        uint durationRtpUnits,
        int width,
        int height,
        byte[] sample,
        VideoPixelFormatsEnum pixelFormat)
    {
        PublishEncodedSample(durationRtpUnits, width, height, sample, pixelFormat);
    }

    public void ExternalVideoSourceRawSampleFaster(uint durationRtpUnits, RawImage rawImage)
    {
        if (_disposed)
        {
            return;
        }

        OnVideoSourceRawSampleFaster?.Invoke(durationRtpUnits, rawImage);

        var format = GetSelectedFormat();
        byte[] encoded;
        lock (_encoderGate)
        {
            encoded = _encoder.EncodeVideoFaster(rawImage, ToEncodeCodec(format));
        }

        if (encoded.Length > 0)
        {
            OnVideoSourceEncodedSample?.Invoke(durationRtpUnits, encoded);
        }
    }

    public void ForceKeyFrame()
    {
        lock (_encoderGate)
        {
            _encoder.ForceKeyFrame();
        }
    }

    public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample is not null;

    public bool IsVideoSourcePaused() => _paused;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CloseVideo().GetAwaiter().GetResult();
        _encoder.Dispose();
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var startedAt = Stopwatch.GetTimestamp();

            if (!_paused && HasEncodedVideoSubscribers())
            {
                try
                {
                    var settings = _qualityController.Current;
                    UpdateEncoderBitrate(settings.WebRtcBitrateKbps);

                    var frame = _screenCapture.CaptureBgra(settings.MaxWidth, settings.MaxHeight);
                    _frameSizeChanged?.Invoke(frame.Width, frame.Height);
                    ForceKeyFrameIfFrameSizeChanged(frame.Width, frame.Height);
                    var nowUtc = DateTimeOffset.UtcNow;
                    if (_changeDetector.ShouldPublish(frame, nowUtc))
                    {
                        PublishEncodedSample(
                            GetSampleDurationRtpUnits(settings, nowUtc),
                            frame.Width,
                            frame.Height,
                            frame.BgraBytes,
                            VideoPixelFormatsEnum.Bgra);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    OnVideoSourceInterrupted?.Invoke();
                    ReportError($"WebRTC desktop capture failed: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }

            var elapsed = TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - startedAt) / (double)Stopwatch.Frequency);
            var remaining = _qualityController.Current.FrameDelay - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }
        }
    }

    private uint GetSampleDurationRtpUnits(StreamQualitySettings settings, DateTimeOffset nowUtc)
    {
        var defaultDuration = settings.FrameDurationRtpUnits(RtpVideoClockRate);
        if (_lastSampleSentAt == DateTimeOffset.MinValue)
        {
            _lastSampleSentAt = nowUtc;
            return defaultDuration;
        }

        var elapsed = nowUtc - _lastSampleSentAt;
        _lastSampleSentAt = nowUtc;
        var elapsedUnits = elapsed.TotalSeconds * RtpVideoClockRate;
        if (!double.IsFinite(elapsedUnits))
        {
            return defaultDuration;
        }

        var clampedUnits = Math.Clamp(
            (long)Math.Round(elapsedUnits),
            defaultDuration,
            RtpVideoClockRate * 5L);
        return (uint)clampedUnits;
    }

    private void PublishEncodedSample(
        uint durationRtpUnits,
        int width,
        int height,
        byte[] sample,
        VideoPixelFormatsEnum pixelFormat)
    {
        if (_disposed)
        {
            return;
        }

        OnVideoSourceRawSample?.Invoke(durationRtpUnits, width, height, sample, pixelFormat);

        var format = GetSelectedFormat();
        byte[] encoded;
        lock (_encoderGate)
        {
            encoded = _encoder.EncodeVideo(width, height, sample, pixelFormat, ToEncodeCodec(format));
        }

        if (encoded.Length > 0)
        {
            OnVideoSourceEncodedSample?.Invoke(durationRtpUnits, encoded);
        }
    }

    private VideoFormat GetSelectedFormat()
    {
        lock (_stateGate)
        {
            return new VideoFormat(_selectedFormat);
        }
    }

    private void UpdateEncoderBitrate(int targetKbps)
    {
        if (_currentTargetKbps == targetKbps)
        {
            return;
        }

        lock (_encoderGate)
        {
            if (_currentTargetKbps == targetKbps)
            {
                return;
            }

            _encoder.TargetKbps = (uint)targetKbps;
            _currentTargetKbps = targetKbps;
        }
    }

    private void ForceKeyFrameIfFrameSizeChanged(int width, int height)
    {
        if (_lastEncodedWidth == width && _lastEncodedHeight == height)
        {
            return;
        }

        _lastEncodedWidth = width;
        _lastEncodedHeight = height;
        ForceKeyFrame();
    }

    private void ReportError(string message)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastErrorAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastErrorAt = now;
        OnVideoSourceError?.Invoke(message);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DesktopVideoSource));
        }
    }

    private static VideoCodecsEnum ToEncodeCodec(VideoFormat format)
    {
        return format.Codec == VideoCodecsEnum.Unknown ? VideoCodecsEnum.VP8 : format.Codec;
    }
}
