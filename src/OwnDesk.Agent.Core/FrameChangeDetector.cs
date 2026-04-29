namespace OwnDesk.Agent;

internal sealed class FrameChangeDetector
{
    private const int MaxSampleColumns = 96;
    private const int MaxSampleRows = 54;
    private const int MinSampleColumns = 16;
    private const int MinSampleRows = 9;
    private const int PixelDifferenceThreshold = 28;
    private const int StaticRefreshMilliseconds = 2500;

    private uint[] _lastPublishedSamples = [];
    private int _lastWidth;
    private int _lastHeight;
    private int _lastColumns;
    private int _lastRows;
    private DateTimeOffset _lastPublishedAt = DateTimeOffset.MinValue;

    public bool ShouldPublish(ScreenRawFrame frame, DateTimeOffset nowUtc)
    {
        var columns = SampleAxis(frame.Width, MaxSampleColumns, MinSampleColumns);
        var rows = SampleAxis(frame.Height, MaxSampleRows, MinSampleRows);
        var samples = Sample(frame, columns, rows);

        var forcePublish =
            _lastPublishedSamples.Length == 0 ||
            frame.Width != _lastWidth ||
            frame.Height != _lastHeight ||
            columns != _lastColumns ||
            rows != _lastRows ||
            nowUtc - _lastPublishedAt >= TimeSpan.FromMilliseconds(StaticRefreshMilliseconds) ||
            IsSignificantChange(samples, _lastPublishedSamples);

        if (forcePublish)
        {
            _lastPublishedSamples = samples;
            _lastWidth = frame.Width;
            _lastHeight = frame.Height;
            _lastColumns = columns;
            _lastRows = rows;
            _lastPublishedAt = nowUtc;
        }

        return forcePublish;
    }

    private static bool IsSignificantChange(uint[] current, uint[] previous)
    {
        if (current.Length != previous.Length)
        {
            return true;
        }

        for (var i = 0; i < current.Length; i++)
        {
            if (ColorDifference(current[i], previous[i]) <= PixelDifferenceThreshold)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static uint[] Sample(ScreenRawFrame frame, int columns, int rows)
    {
        var samples = new uint[columns * rows];
        var bytes = frame.BgraBytes;
        var index = 0;

        for (var row = 0; row < rows; row++)
        {
            var y = rows == 1 ? 0 : row * (frame.Height - 1) / (rows - 1);
            for (var column = 0; column < columns; column++)
            {
                var x = columns == 1 ? 0 : column * (frame.Width - 1) / (columns - 1);
                var offset = ((y * frame.Width) + x) * 4;
                samples[index++] =
                    bytes[offset] |
                    ((uint)bytes[offset + 1] << 8) |
                    ((uint)bytes[offset + 2] << 16);
            }
        }

        return samples;
    }

    private static int SampleAxis(int length, int maxSamples, int minSamples)
    {
        if (length <= 1)
        {
            return 1;
        }

        return Math.Clamp(length / 16, Math.Min(minSamples, length), Math.Min(maxSamples, length));
    }

    private static int ColorDifference(uint left, uint right)
    {
        var db = Math.Abs((int)(left & 0xff) - (int)(right & 0xff));
        var dg = Math.Abs((int)((left >> 8) & 0xff) - (int)((right >> 8) & 0xff));
        var dr = Math.Abs((int)((left >> 16) & 0xff) - (int)((right >> 16) & 0xff));
        return db + dg + dr;
    }
}
