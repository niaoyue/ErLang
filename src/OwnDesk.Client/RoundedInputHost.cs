using System.Drawing.Drawing2D;

namespace OwnDesk.Client;

internal sealed class RoundedInputHost : Panel
{
    private const int CornerRadius = 7;
    private readonly Color _borderColor;
    private readonly Color _focusBorderColor;
    private readonly Color _surfaceColor;
    private bool _focused;

    public RoundedInputHost(TextBox input, Color surfaceColor, Color borderColor, Color focusBorderColor)
    {
        _surfaceColor = surfaceColor;
        _borderColor = borderColor;
        _focusBorderColor = focusBorderColor;
        Dock = DockStyle.Top;
        Height = 36;
        Padding = new Padding(10, 8, 10, 6);
        Margin = new Padding(0, 0, 0, 6);
        BackColor = Color.Transparent;

        input.Dock = DockStyle.Fill;
        input.BorderStyle = BorderStyle.None;
        input.BackColor = surfaceColor;
        input.Margin = new Padding(0);
        input.Enter += (_, _) =>
        {
            _focused = true;
            Invalidate();
        };
        input.Leave += (_, _) =>
        {
            _focused = false;
            Invalidate();
        };
        Controls.Add(input);

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var path = RoundedRectangle(bounds, CornerRadius);
        using var surfaceBrush = new SolidBrush(_surfaceColor);
        using var borderPen = new Pen(_focused ? _focusBorderColor : _borderColor);
        e.Graphics.FillPath(surfaceBrush, path);
        e.Graphics.DrawPath(borderPen, path);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
