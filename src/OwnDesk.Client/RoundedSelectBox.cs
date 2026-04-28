using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace OwnDesk.Client;

internal sealed class RoundedSelectBox : UserControl
{
    private const int CornerRadius = 7;
    private readonly Label _arrow = new();
    private readonly Label _display = new();
    private ContextMenuStrip? _activeMenu;
    private int _selectedIndex = -1;
    private int _updateDepth;
    private bool _focused;

    public RoundedSelectBox()
    {
        Items = new SelectItemCollection(this);
        Dock = DockStyle.Top;
        Height = 36;
        Margin = new Padding(0, 0, 0, 8);
        Padding = new Padding(10, 0, 8, 0);
        BackColor = Color.Transparent;
        SurfaceColor = Color.FromArgb(252, 252, 253);
        BorderColor = Color.FromArgb(228, 228, 231);
        FocusBorderColor = Color.FromArgb(63, 63, 70);
        Cursor = Cursors.Hand;
        TabStop = true;

        _arrow.Dock = DockStyle.Right;
        _arrow.Width = 20;
        _arrow.Text = "v";
        _arrow.TextAlign = ContentAlignment.MiddleCenter;
        _arrow.BackColor = Color.Transparent;
        _arrow.Cursor = Cursors.Hand;
        _arrow.Click += (_, _) => ShowMenu();

        _display.Dock = DockStyle.Fill;
        _display.AutoEllipsis = true;
        _display.TextAlign = ContentAlignment.MiddleLeft;
        _display.BackColor = Color.Transparent;
        _display.Cursor = Cursors.Hand;
        _display.Click += (_, _) => ShowMenu();

        Controls.Add(_display);
        Controls.Add(_arrow);
        Click += (_, _) => ShowMenu();

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    public event EventHandler? SelectedIndexChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SelectItemCollection Items { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem => IsSelectionValid ? Items[_selectedIndex] : null;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceColor { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FocusBorderColor { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetSelectedIndex(value, raiseEvent: true);
    }

    public void BeginUpdate()
    {
        _updateDepth++;
    }

    public void EndUpdate()
    {
        if (_updateDepth > 0)
        {
            _updateDepth--;
        }

        if (_updateDepth == 0)
        {
            CoerceSelection();
            UpdateDisplay();
            Invalidate();
        }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _display.Font = Font;
        _arrow.Font = Font;
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        _display.ForeColor = ForeColor;
        _arrow.ForeColor = ForeColor;
    }

    protected override void OnEnter(EventArgs e)
    {
        _focused = true;
        Invalidate();
        base.OnEnter(e);
    }

    protected override void OnLeave(EventArgs e)
    {
        _focused = false;
        Invalidate();
        base.OnLeave(e);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space || e.Alt && e.KeyCode == Keys.Down)
        {
            ShowMenu();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Down && Items.Count > 0)
        {
            SelectedIndex = Math.Min(Items.Count - 1, _selectedIndex + 1);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Up && Items.Count > 0)
        {
            SelectedIndex = Math.Max(0, _selectedIndex - 1);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activeMenu?.Close();
            _activeMenu = null;
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var path = RoundedRectangle(bounds, CornerRadius);
        using var fill = new SolidBrush(SurfaceColor);
        using var pen = new Pen(_focused ? FocusBorderColor : BorderColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }

    private bool IsSelectionValid => _selectedIndex >= 0 && _selectedIndex < Items.Count;

    private void ShowMenu()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        Focus();
        if (Items.Count == 0)
        {
            return;
        }

        _activeMenu?.Close();
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = SurfaceColor,
            ForeColor = ForeColor,
            Font = Font
        };
        _activeMenu = menu;

        for (var index = 0; index < Items.Count; index++)
        {
            var itemIndex = index;
            var menuItem = new ToolStripMenuItem(Items[index].ToString())
            {
                Checked = itemIndex == _selectedIndex
            };
            menuItem.Click += (_, _) => SelectedIndex = itemIndex;
            menu.Items.Add(menuItem);
        }

        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeMenu, menu))
            {
                _activeMenu = null;
            }

            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(() =>
            {
                if (!menu.IsDisposed)
                {
                    menu.Dispose();
                }
            });
        };
        menu.Show(this, new Point(0, Height + 2));
    }

    private void SetSelectedIndex(int value, bool raiseEvent)
    {
        var coerced = value < -1 ? -1 : value;
        if (coerced >= Items.Count)
        {
            coerced = Items.Count - 1;
        }

        if (_selectedIndex == coerced)
        {
            return;
        }

        _selectedIndex = coerced;
        UpdateDisplay();
        Invalidate();
        if (raiseEvent)
        {
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CoerceSelection()
    {
        if (_selectedIndex >= Items.Count)
        {
            SetSelectedIndex(Items.Count - 1, raiseEvent: true);
        }
    }

    private void UpdateDisplay()
    {
        _display.Text = IsSelectionValid ? Items[_selectedIndex].ToString() : string.Empty;
    }

    private void NotifyItemsChanged()
    {
        if (_updateDepth > 0)
        {
            return;
        }

        CoerceSelection();
        UpdateDisplay();
        Invalidate();
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

    internal sealed class SelectItemCollection
    {
        private readonly List<object> _items = [];
        private readonly RoundedSelectBox _owner;

        public SelectItemCollection(RoundedSelectBox owner)
        {
            _owner = owner;
        }

        public int Count => _items.Count;

        public object this[int index] => _items[index];

        public void Add(object item)
        {
            _items.Add(item);
            _owner.NotifyItemsChanged();
        }

        public void Clear()
        {
            _items.Clear();
            _owner.NotifyItemsChanged();
        }
    }
}
