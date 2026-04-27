using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OwnDesk.Agent;

internal interface IScreenCaptureBackend : IDisposable
{
    string Name { get; }

    ScreenSize GetPrimaryScreenSize();

    ScreenRawFrame CapturePrimaryBgra();
}

internal sealed record ScreenCaptureBackendPlan(
    string RequestedBackend,
    string SelectedBackend,
    string CaptureBackendId,
    string[] Notes)
{
    public static ScreenCaptureBackendPlan Create(AgentOptions options)
    {
        var requested = options.CaptureBackend.ToString().ToUpperInvariant();
        var notes = new List<string>();

        if (options.CaptureBackend is ScreenCaptureBackendPreference.Dxgi or ScreenCaptureBackendPreference.Wgc)
        {
            notes.Add($"{requested} capture was requested, but only the GDI backend is wired into this build.");
            notes.Add("Falling back to GDI CopyFromScreen until the GPU capture backend is implemented.");
        }

        if (options.CaptureBackend is ScreenCaptureBackendPreference.Auto)
        {
            notes.Add("Auto currently selects GDI because DXGI/WGC capture is not wired into this build yet.");
        }

        return new ScreenCaptureBackendPlan(
            RequestedBackend: requested,
            SelectedBackend: "GDI",
            CaptureBackendId: GdiScreenCaptureBackend.BackendId,
            Notes: notes.ToArray());
    }
}

internal static class ScreenCaptureBackendFactory
{
    public static IScreenCaptureBackend Create(ScreenCaptureBackendPlan plan)
    {
        return plan.SelectedBackend.Equals("GDI", StringComparison.OrdinalIgnoreCase)
            ? new GdiScreenCaptureBackend()
            : throw new NotSupportedException($"Unsupported screen capture backend: {plan.SelectedBackend}.");
    }
}

internal sealed class GdiScreenCaptureBackend : IScreenCaptureBackend
{
    public const string BackendId = "gdi-copyfromscreen-bgra";

    public string Name => BackendId;

    public ScreenSize GetPrimaryScreenSize()
    {
        var bounds = PrimaryBounds();
        return new ScreenSize(bounds.Width, bounds.Height);
    }

    public ScreenRawFrame CapturePrimaryBgra()
    {
        var bounds = PrimaryBounds();
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        return CopyBgra(bitmap);
    }

    public void Dispose()
    {
    }

    private static Rectangle PrimaryBounds()
    {
        return Screen.PrimaryScreen?.Bounds
               ?? throw new InvalidOperationException("No primary screen is available.");
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
}
