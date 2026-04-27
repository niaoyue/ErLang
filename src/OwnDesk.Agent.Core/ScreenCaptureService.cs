using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OwnDesk.Agent;

internal sealed class ScreenCaptureService : IDisposable
{
    private readonly object _captureGate = new();
    private readonly IScreenCaptureBackend _backend;

    public ScreenCaptureService(IScreenCaptureBackend backend, ScreenCaptureBackendPlan backendPlan)
    {
        _backend = backend;
        BackendPlan = backendPlan;
    }

    public ScreenCaptureBackendPlan BackendPlan { get; }

    public ScreenSize GetPrimaryScreenSize()
    {
        return _backend.GetPrimaryScreenSize();
    }

    public ScreenFrame CaptureJpeg(long quality, int maxWidth, int maxHeight)
    {
        lock (_captureGate)
        {
            var frame = _backend.CapturePrimaryBgra();
            using var bitmap = BitmapFromBgra(frame);

            var outputSize = FitWithin(frame.Width, frame.Height, maxWidth, maxHeight);
            using var output = outputSize.Width == frame.Width && outputSize.Height == frame.Height
                ? (Bitmap)bitmap.Clone()
                : Resize(bitmap, outputSize.Width, outputSize.Height);

            using var stream = new MemoryStream();
            var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.MimeType == "image/jpeg");
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            output.Save(stream, encoder, parameters);

            return new ScreenFrame(output.Width, output.Height, stream.ToArray());
        }
    }

    public ScreenRawFrame CaptureBgra(int maxWidth, int maxHeight)
    {
        lock (_captureGate)
        {
            var frame = _backend.CapturePrimaryBgra();

            var outputSize = FitWithin(frame.Width, frame.Height, maxWidth, maxHeight, forceEven: true);
            if (outputSize.Width == frame.Width && outputSize.Height == frame.Height)
            {
                return frame;
            }

            using var bitmap = BitmapFromBgra(frame);
            using var output = ResizeBgra(bitmap, outputSize.Width, outputSize.Height);
            return CopyBgra(output);
        }
    }

    public void Dispose()
    {
        _backend.Dispose();
    }

    private static ScreenSize FitWithin(int width, int height, int maxWidth, int maxHeight, bool forceEven = false)
    {
        var scale = Math.Min(1.0, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        var fitted = new ScreenSize(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));

        if (!forceEven)
        {
            return fitted;
        }

        return new ScreenSize(MakeEven(fitted.Width), MakeEven(fitted.Height));
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        var resized = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(resized);
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(source, 0, 0, width, height);
        return resized;
    }

    private static Bitmap ResizeBgra(Bitmap source, int width, int height)
    {
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(source, 0, 0, width, height);
        return resized;
    }

    private static Bitmap BitmapFromBgra(ScreenRawFrame frame)
    {
        var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var bounds = new Rectangle(0, 0, frame.Width, frame.Height);
        var bitmapData = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            var bytesPerRow = frame.Width * 4;
            var destinationStride = bitmapData.Stride;

            for (var y = 0; y < frame.Height; y++)
            {
                var destinationOffset = destinationStride >= 0
                    ? y * destinationStride
                    : (frame.Height - 1 - y) * Math.Abs(destinationStride);
                Marshal.Copy(frame.BgraBytes, y * bytesPerRow, IntPtr.Add(bitmapData.Scan0, destinationOffset), bytesPerRow);
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }

    private static ScreenRawFrame CopyBgra(Bitmap source)
    {
        var bounds = new Rectangle(0, 0, source.Width, source.Height);
        var bitmapData = source.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var width = source.Width;
            var height = source.Height;
            var bytesPerRow = width * 4;
            var bytes = new byte[bytesPerRow * height];
            var sourceStride = bitmapData.Stride;
            var sourceRowSize = Math.Min(bytesPerRow, Math.Abs(sourceStride));

            for (var y = 0; y < height; y++)
            {
                var sourceOffset = sourceStride >= 0
                    ? y * sourceStride
                    : (height - 1 - y) * Math.Abs(sourceStride);
                Marshal.Copy(IntPtr.Add(bitmapData.Scan0, sourceOffset), bytes, y * bytesPerRow, sourceRowSize);
            }

            return new ScreenRawFrame(width, height, bytes);
        }
        finally
        {
            source.UnlockBits(bitmapData);
        }
    }

    private static int MakeEven(int value)
    {
        if (value <= 2)
        {
            return 2;
        }

        return value % 2 == 0 ? value : value - 1;
    }
}

internal sealed record ScreenSize(int Width, int Height);

internal sealed record ScreenFrame(int Width, int Height, byte[] JpegBytes);

internal sealed record ScreenRawFrame(int Width, int Height, byte[] BgraBytes);
