using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace OwnDesk.Client;

internal sealed partial class MainForm : Form
{
    private const int SettingsPanelWidth = 340;
    private const int SettingsPanelMinWidth = 300;
    private const int SettingsCollapsedWidth = 52;
    private const int ViewerPanelMinWidth = 620;
    private static readonly Color AppBackground = Color.FromArgb(248, 248, 249);
    private static readonly Color SurfaceColor = Color.White;
    private static readonly Color SurfaceMutedColor = Color.FromArgb(246, 246, 247);
    private static readonly Color LineColor = Color.FromArgb(228, 228, 231);
    private static readonly Color TextColor = Color.FromArgb(24, 24, 27);
    private static readonly Color MutedTextColor = Color.FromArgb(82, 82, 91);
    private static readonly Color AccentColor = Color.FromArgb(63, 63, 70);
    private static readonly Color DangerColor = Color.FromArgb(220, 38, 38);

    private readonly AgentRunner _agentRunner = new();
    private readonly MemberAuthClient _authClient = new();
    private readonly RoundedSelectBox _organizationSelect = new();
    private readonly TextBox _organizationNameInput = new();
    private readonly TextBox _serverInput = new();
    private readonly TextBox _tokenInput = new();
    private readonly Button _addOrganizationButton = new RoundedButton();
    private readonly Button _saveOrganizationButton = new RoundedButton();
    private readonly Button _deleteOrganizationButton = new RoundedButton();
    private readonly Button _loginModeButton = new RoundedButton();
    private readonly Button _registerModeButton = new RoundedButton();
    private readonly Button _authSubmitButton = new RoundedButton();
    private readonly Button _logoutButton = new RoundedButton();
    private readonly TextBox _accountInput = new();
    private readonly TextBox _passwordInput = new();
    private readonly Label _confirmPasswordLabel = new();
    private readonly TextBox _confirmPasswordInput = new();
    private RoundedInputHost? _confirmPasswordHost;
    private readonly TableLayoutPanel _authFieldsPanel = new();
    private readonly TableLayoutPanel _memberPanel = new();
    private readonly Label _identityStatus = new();
    private readonly TextBox _deviceIdInput = new();
    private readonly TextBox _deviceNameInput = new();
    private readonly CheckBox _startOnLaunchInput = new();
    private readonly CheckBox _webRtcInput = new();
    private readonly Button _startAgentButton = new RoundedButton();
    private readonly Button _stopAgentButton = new RoundedButton();
    private readonly Button _openViewerButton = new RoundedButton();
    private readonly Button _collapseSettingsButton = new RoundedButton();
    private readonly Button _expandSettingsButton = new RoundedButton();
    private readonly Label _agentStatus = new();
    private readonly Label _viewerStatus = new();
    private readonly TextBox _logOutput = new();
    private readonly WebView2 _webView = new();
    private SplitContainer? _rootSplit;
    private Control? _settingsContentPanel;
    private Control? _settingsCollapsedRail;
    private bool _allowClose;
    private bool _hostFullscreen;
    private bool _isBinding;
    private bool _registerMode;
    private bool _settingsCollapsed;
    private ClientSettings _settings;

    public MainForm()
    {
        _settings = ClientSettings.Load().Normalize();

        Text = "OwnDesk Client";
        Width = 1320;
        Height = 840;
        MinimumSize = new Size(1040, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBackground;
        Font = new Font("Microsoft YaHei UI", 9F);
        KeyPreview = true;

        BuildLayout();
        BindSettings();
        SetAgentRunning(false);

        _agentRunner.StatusChanged += (_, status) => PostStatus(status);
        _agentRunner.RunningChanged += (_, running) => PostAgentRunning(running);
        Shown += OnShownAsync;
        FormClosing += OnFormClosingAsync;
        KeyDown += OnMainFormKeyDown;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _agentRunner.Dispose();
            _webView.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        _rootSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = 8,
            FixedPanel = FixedPanel.Panel1,
            BackColor = LineColor
        };

        _rootSplit.Layout += (_, _) => ApplySplitLayout();
        _rootSplit.Panel1.BackColor = AppBackground;
        _rootSplit.Panel2.BackColor = AppBackground;
        _rootSplit.Panel1.Controls.Add(BuildSettingsShell());
        _rootSplit.Panel2.Controls.Add(_webView);
        _webView.Dock = DockStyle.Fill;

        Controls.Add(_rootSplit);
        ApplySplitLayout();
    }

    private void ApplySplitLayout()
    {
        if (_rootSplit is null || _hostFullscreen)
        {
            return;
        }

        var width = _rootSplit.Width;
        var splitterWidth = Math.Max(1, _rootSplit.SplitterWidth);
        var usableWidth = width - splitterWidth;
        if (usableWidth <= 2)
        {
            return;
        }

        var desiredPanel1Min = _settingsCollapsed ? SettingsCollapsedWidth : SettingsPanelMinWidth;
        var panel1Min = Math.Min(desiredPanel1Min, Math.Max(1, usableWidth / 2));
        var panel2Min = Math.Min(ViewerPanelMinWidth, Math.Max(1, usableWidth - panel1Min));
        if (panel1Min + panel2Min > usableWidth)
        {
            panel2Min = Math.Max(1, usableWidth - panel1Min);
        }

        var minDistance = panel1Min;
        var maxDistance = Math.Max(minDistance, width - splitterWidth - panel2Min);
        var preferredDistance = _settingsCollapsed ? SettingsCollapsedWidth : SettingsPanelWidth;
        var distance = Math.Clamp(preferredDistance, minDistance, maxDistance);

        _rootSplit.Panel1MinSize = 0;
        _rootSplit.Panel2MinSize = 0;
        if (_rootSplit.SplitterDistance != distance)
        {
            _rootSplit.SplitterDistance = distance;
        }

        _rootSplit.Panel1MinSize = panel1Min;
        _rootSplit.Panel2MinSize = panel2Min;

        if (_settingsCollapsed)
        {
            return;
        }
    }

    private Control BuildOrganizationPanel()
    {
        var panel = CreateSection();
        ConfigureComboBox(_organizationSelect);
        _organizationSelect.SelectedIndexChanged += (_, _) => _ = RunUiActionAsync(SwitchOrganizationAsync);
        panel.Controls.Add(_organizationSelect);

        var buttons = CreateButtonGrid(3);
        ConfigureButton(_addOrganizationButton, "新增", (_, _) => _ = RunUiActionAsync(AddOrganizationAsync));
        ConfigureButton(_saveOrganizationButton, "保存组织", (_, _) => SaveCurrentOrganization(rebind: true));
        ConfigureButton(_deleteOrganizationButton, "删除", (_, _) => _ = RunUiActionAsync(DeleteOrganizationAsync), danger: true);
        AddGridButton(buttons, _addOrganizationButton, 0);
        AddGridButton(buttons, _saveOrganizationButton, 1);
        AddGridButton(buttons, _deleteOrganizationButton, 2);
        panel.Controls.Add(buttons);

        ConfigureTextBox(_organizationNameInput, "组织名称");
        ConfigureTextBox(_serverInput, "例如 http://127.0.0.1:5080");
        ConfigureTextBox(_tokenInput, "组织 Token");
        _tokenInput.UseSystemPasswordChar = true;
        AddField(panel, "组织名称", _organizationNameInput);
        AddField(panel, "服务器 URL", _serverInput);
        AddField(panel, "组织 Token", _tokenInput);
        return panel;
    }

    private Control BuildAuthPanel()
    {
        var panel = CreateSection();
        var tabs = CreateButtonRow();
        ConfigureButton(_loginModeButton, "登录", (_, _) => SetAuthMode(registerMode: false));
        ConfigureButton(_registerModeButton, "注册", (_, _) => SetAuthMode(registerMode: true));
        tabs.Controls.AddRange([_loginModeButton, _registerModeButton]);
        panel.Controls.Add(tabs);

        ConfigureAuthFields();
        panel.Controls.Add(_authFieldsPanel);

        _memberPanel.Dock = DockStyle.Top;
        _memberPanel.AutoSize = true;
        _memberPanel.ColumnCount = 1;
        _memberPanel.BackColor = SurfaceColor;
        _identityStatus.AutoSize = true;
        _identityStatus.ForeColor = TextColor;
        _identityStatus.MaximumSize = new Size(SettingsPanelWidth - 48, 0);
        _memberPanel.Controls.Add(_identityStatus);
        ConfigureButton(_logoutButton, "退出登录", (_, _) => _ = RunUiActionAsync(LogoutAsync), danger: true);
        _memberPanel.Controls.Add(_logoutButton);
        panel.Controls.Add(_memberPanel);
        return panel;
    }

    private Control BuildDevicePanel()
    {
        var panel = CreateSection();
        ConfigureTextBox(_deviceIdInput, Environment.MachineName);
        ConfigureTextBox(_deviceNameInput, Environment.MachineName);
        AddField(panel, "设备 ID", _deviceIdInput);
        AddField(panel, "设备名称", _deviceNameInput);

        _startOnLaunchInput.Text = "启动时自动上线";
        _startOnLaunchInput.AutoSize = true;
        _startOnLaunchInput.BackColor = SurfaceColor;
        _webRtcInput.Text = "启用 WebRTC";
        _webRtcInput.AutoSize = true;
        _webRtcInput.BackColor = SurfaceColor;
        panel.Controls.Add(_startOnLaunchInput);
        panel.Controls.Add(_webRtcInput);

        var buttons = CreateButtonGrid(3);
        ConfigureButton(_startAgentButton, "上线本机", (_, _) => _ = RunUiActionAsync(StartAgentAsync), primary: true);
        ConfigureButton(_stopAgentButton, "停止", (_, _) => _ = RunUiActionAsync(StopAgentAsync), danger: true);
        ConfigureButton(_openViewerButton, "打开控制台", (_, _) => _ = RunUiActionAsync(OpenViewerAsync));
        AddGridButton(buttons, _startAgentButton, 0);
        AddGridButton(buttons, _stopAgentButton, 1);
        AddGridButton(buttons, _openViewerButton, 2);
        panel.Controls.Add(buttons);
        return panel;
    }

    private Control BuildLogPanel()
    {
        var panel = CreateSection();
        _logOutput.Dock = DockStyle.Top;
        _logOutput.Multiline = true;
        _logOutput.ReadOnly = true;
        _logOutput.ScrollBars = ScrollBars.Vertical;
        _logOutput.BorderStyle = BorderStyle.FixedSingle;
        _logOutput.BackColor = SurfaceColor;
        _logOutput.ForeColor = TextColor;
        _logOutput.Height = 84;
        panel.Controls.Add(_logOutput);
        panel.Controls.Add(BuildStatusPanel());
        return panel;
    }

    private Control BuildStatusPanel()
    {
        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = SurfaceColor,
            Padding = new Padding(0, 6, 0, 0)
        };
        _agentStatus.AutoSize = true;
        _viewerStatus.AutoSize = true;
        _agentStatus.ForeColor = MutedTextColor;
        _viewerStatus.ForeColor = MutedTextColor;
        statusPanel.Controls.Add(_agentStatus);
        statusPanel.Controls.Add(_viewerStatus);
        return statusPanel;
    }

    private void ConfigureAuthFields()
    {
        _authFieldsPanel.Dock = DockStyle.Top;
        _authFieldsPanel.AutoSize = true;
        _authFieldsPanel.ColumnCount = 1;
        _authFieldsPanel.BackColor = SurfaceColor;
        ConfigureTextBox(_accountInput, "成员账号");
        ConfigureTextBox(_passwordInput, "成员密码");
        ConfigureTextBox(_confirmPasswordInput, "再次输入密码");
        _passwordInput.UseSystemPasswordChar = true;
        _confirmPasswordInput.UseSystemPasswordChar = true;
        AddField(_authFieldsPanel, "成员账号", _accountInput);
        AddField(_authFieldsPanel, "密码", _passwordInput);
        _confirmPasswordLabel.Text = "确认密码";
        _confirmPasswordLabel.AutoSize = true;
        _confirmPasswordLabel.ForeColor = MutedTextColor;
        _confirmPasswordLabel.Margin = new Padding(0, 5, 0, 2);
        _authFieldsPanel.Controls.Add(_confirmPasswordLabel);
        _confirmPasswordHost = new RoundedInputHost(_confirmPasswordInput, Color.FromArgb(252, 252, 253), LineColor, AccentColor);
        _authFieldsPanel.Controls.Add(_confirmPasswordHost);
        ConfigureButton(_authSubmitButton, "登录", (_, _) => _ = RunUiActionAsync(AuthenticateAsync), primary: true);
        _authFieldsPanel.Controls.Add(_authSubmitButton);
    }

}
