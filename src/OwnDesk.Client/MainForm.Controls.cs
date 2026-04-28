namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private static Control CreateCollapsibleSection(string title, Control body)
    {
        return new CollapsibleSection(title, body, AppBackground, SurfaceColor, SurfaceMutedColor, LineColor, TextColor);
    }

    private static void AddStacked(TableLayoutPanel panel, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Top;
        panel.Controls.Add(control, 0, row);
    }

    private static TableLayoutPanel CreateSection()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = SurfaceColor,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    private static FlowLayoutPanel CreateButtonRow()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            BackColor = SurfaceColor,
            Margin = new Padding(0, 0, 0, 3)
        };
    }

    private static TableLayoutPanel CreateButtonGrid(int columnCount)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 39,
            ColumnCount = columnCount,
            RowCount = 1,
            BackColor = SurfaceColor,
            Margin = new Padding(0, 0, 0, 4),
            Padding = new Padding(0)
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var index = 0; index < columnCount; index++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columnCount));
        }

        return grid;
    }

    private static void AddGridButton(TableLayoutPanel grid, Button button, int column)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(column == 0 ? 0 : 4, 0, column == grid.ColumnCount - 1 ? 0 : 4, 5);
        grid.Controls.Add(button, column, 0);
    }

    private static void AddField(TableLayoutPanel form, string labelText, TextBox input)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = MutedTextColor,
            Margin = new Padding(0, 3, 0, 1)
        };

        form.Controls.Add(label);
        form.Controls.Add(new RoundedInputHost(input, Color.FromArgb(252, 252, 253), LineColor, AccentColor));
    }

    private static void ConfigureTextBox(TextBox input, string placeholder)
    {
        input.Dock = DockStyle.Fill;
        input.AutoSize = false;
        input.Font = new Font("Microsoft YaHei UI", 9F);
        input.Height = 20;
        input.BorderStyle = BorderStyle.None;
        input.BackColor = Color.FromArgb(252, 252, 253);
        input.ForeColor = TextColor;
        input.Margin = new Padding(0);
        input.PlaceholderText = placeholder;
    }

    private static void ConfigureComboBox(RoundedSelectBox input)
    {
        input.Dock = DockStyle.Top;
        input.Font = new Font("Microsoft YaHei UI", 9F);
        input.Height = 36;
        input.SurfaceColor = Color.FromArgb(252, 252, 253);
        input.BorderColor = LineColor;
        input.FocusBorderColor = AccentColor;
        input.ForeColor = TextColor;
        input.Margin = new Padding(0, 0, 0, 8);
    }

    private static void ConfigureButton(
        Button button,
        string text,
        EventHandler handler,
        bool primary = false,
        bool danger = false)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Font = new Font("Microsoft YaHei UI", 9F);
        button.Height = 34;
        button.MinimumSize = new Size(66, 34);
        button.Width = Math.Max(76, TextRenderer.MeasureText(text, button.Font).Width + 22);
        button.Padding = new Padding(6, 1, 6, 1);
        button.Margin = new Padding(0, 0, 7, 5);
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        ApplyButtonStyle(button, primary, danger);
        button.Click += handler;
    }

    private static void ConfigureSquareButton(Button button, string text, EventHandler handler)
    {
        button.Text = text;
        button.TabStop = false;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = SurfaceMutedColor;
        button.ForeColor = TextColor;
        button.FlatAppearance.BorderColor = LineColor;
        button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Height = 34;
        button.MinimumSize = new Size(38, 34);
        button.Click += handler;
    }

    private static void ApplyButtonStyle(Button button, bool primary = false, bool danger = false, bool active = false)
    {
        if (primary || active)
        {
            button.BackColor = AccentColor;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = AccentColor;
            return;
        }

        if (danger)
        {
            button.BackColor = Color.FromArgb(255, 244, 243);
            button.ForeColor = DangerColor;
            button.FlatAppearance.BorderColor = Color.FromArgb(236, 196, 193);
            return;
        }

        button.BackColor = SurfaceMutedColor;
        button.ForeColor = TextColor;
        button.FlatAppearance.BorderColor = LineColor;
    }
}
