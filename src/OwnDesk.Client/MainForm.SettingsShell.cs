namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private Control BuildSettingsShell()
    {
        var shell = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBackground
        };

        _settingsContentPanel = BuildSettingsPanel();
        _settingsCollapsedRail = BuildSettingsCollapsedRail();
        _settingsCollapsedRail.Visible = false;
        shell.Controls.Add(_settingsContentPanel);
        shell.Controls.Add(_settingsCollapsedRail);
        return shell;
    }

    private Control BuildSettingsPanel()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = AppBackground
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var titleBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(14, 9, 12, 7),
            BackColor = AppBackground
        };
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        var title = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            ForeColor = TextColor,
            Text = "OwnDesk",
            TextAlign = ContentAlignment.MiddleLeft
        };
        ConfigureSquareButton(_collapseSettingsButton, "收起", (_, _) => SetSettingsCollapsed(true));
        _collapseSettingsButton.Dock = DockStyle.Fill;
        titleBar.Controls.Add(title, 0, 0);
        titleBar.Controls.Add(_collapseSettingsButton, 1, 0);

        var scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(0),
            BackColor = AppBackground
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(9, 8, 9, 8),
            BackColor = AppBackground
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddStacked(content, CreateCollapsibleSection("组织", BuildOrganizationPanel()));
        AddStacked(content, CreateCollapsibleSection("成员", BuildAuthPanel()));
        AddStacked(content, CreateCollapsibleSection("本机设备", BuildDevicePanel()));
        AddStacked(content, CreateCollapsibleSection("状态", BuildLogPanel()));
        scrollPanel.Controls.Add(content);
        shell.Controls.Add(titleBar, 0, 0);
        shell.Controls.Add(scrollPanel, 0, 1);
        return shell;
    }

    private Control BuildSettingsCollapsedRail()
    {
        var rail = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(7, 10, 7, 0),
            BackColor = AppBackground
        };
        ConfigureSquareButton(_expandSettingsButton, "›", (_, _) => SetSettingsCollapsed(false));
        _expandSettingsButton.Dock = DockStyle.Top;
        _expandSettingsButton.Height = 36;
        rail.Controls.Add(_expandSettingsButton);
        return rail;
    }

    private void SetSettingsCollapsed(bool collapsed)
    {
        if (_settingsCollapsed == collapsed || _hostFullscreen)
        {
            return;
        }

        _settingsCollapsed = collapsed;
        if (_settingsContentPanel is not null)
        {
            _settingsContentPanel.Visible = !collapsed;
        }

        if (_settingsCollapsedRail is not null)
        {
            _settingsCollapsedRail.Visible = collapsed;
        }

        if (_rootSplit is not null)
        {
            _rootSplit.SplitterWidth = collapsed ? 4 : 8;
        }

        ApplySplitLayout();
    }
}
