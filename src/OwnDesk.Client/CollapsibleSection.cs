using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace OwnDesk.Client;

internal sealed class CollapsibleSection : UserControl
{
    private const int CornerRadius = 10;
    private const int HeaderHeight = 40;
    private const int CardInset = 1;
    private readonly Label _header = new();
    private readonly Panel _bodyHost = new();
    private readonly string _title;
    private readonly Color _backgroundColor;
    private readonly Color _surfaceColor;
    private readonly Color _mutedSurfaceColor;
    private bool _expanded = true;

    public CollapsibleSection(
        string title,
        Control body,
        Color backgroundColor,
        Color surfaceColor,
        Color mutedSurfaceColor,
        Color lineColor,
        Color textColor)
    {
        _title = title;
        _backgroundColor = backgroundColor;
        _surfaceColor = surfaceColor;
        _mutedSurfaceColor = mutedSurfaceColor;
        Dock = DockStyle.Top;
        AutoSize = true;
        BackColor = backgroundColor;
        Margin = new Padding(0, 0, 0, 8);
        Padding = new Padding(CardInset, CardInset, CardInset, CardInset);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _header.Dock = DockStyle.Top;
        _header.Height = HeaderHeight;
        _header.BackColor = Color.Transparent;
        _header.ForeColor = textColor;
        _header.TextAlign = ContentAlignment.MiddleLeft;
        _header.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        _header.Padding = new Padding(14, 0, 10, 0);
        _header.Margin = new Padding(0);
        _header.Cursor = Cursors.Hand;
        _header.Click += (_, _) => Expanded = !Expanded;

        body.Dock = DockStyle.Top;
        body.Margin = new Padding(0);
        _bodyHost.Dock = DockStyle.Top;
        _bodyHost.AutoSize = true;
        _bodyHost.Padding = new Padding(14, 11, 14, 13);
        _bodyHost.BackColor = surfaceColor;
        _bodyHost.Cursor = Cursors.Default;
        _bodyHost.Controls.Add(body);

        layout.Controls.Add(_header);
        layout.Controls.Add(_bodyHost);
        Controls.Add(layout);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BorderColor = lineColor;
        UpdateHeader();
    }

    private Color BorderColor { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;
            _bodyHost.Visible = value;
            UpdateHeader();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(_backgroundColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var pen = new Pen(BorderColor);
        using var surfaceBrush = new SolidBrush(_surfaceColor);
        using var headerBrush = new SolidBrush(_mutedSurfaceColor);
        var bounds = new Rectangle(CardInset, CardInset, Width - CardInset * 2 - 1, Height - CardInset * 2 - 1);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using (var surfacePath = RoundedRectangle(bounds, CornerRadius))
        {
            e.Graphics.FillPath(surfaceBrush, surfacePath);
        }

        var headerBounds = new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width - 1, Math.Min(HeaderHeight, bounds.Height - 1));
        using var headerPath = RoundedTopRectangle(headerBounds, CornerRadius);
        e.Graphics.FillPath(headerBrush, headerPath);
        e.Graphics.DrawLine(pen, bounds.X + 1, bounds.Y + HeaderHeight, bounds.Right - 1, bounds.Y + HeaderHeight);
        using var borderPath = RoundedRectangle(bounds, CornerRadius);
        e.Graphics.DrawPath(pen, borderPath);
    }

    private void UpdateHeader()
    {
        _header.Text = $"{(_expanded ? "▾" : "▸")}  {_title}";
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

    private static GraphicsPath RoundedTopRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        path.AddLine(bounds.Right, bounds.Top + radius, bounds.Right, bounds.Bottom);
        path.AddLine(bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom);
        path.AddLine(bounds.Left, bounds.Bottom, bounds.Left, bounds.Top + radius);
        path.CloseFigure();
        return path;
    }
}
