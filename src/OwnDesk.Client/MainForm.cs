using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OwnDesk.Client;

internal sealed class MainForm : Form
{
    private const int SettingsPanelWidth = 520;
    private static readonly Color AppBackground = Color.FromArgb(244, 246, 243);
    private static readonly Color SurfaceColor = Color.White;
    private static readonly Color SurfaceMutedColor = Color.FromArgb(237, 241, 236);
    private static readonly Color LineColor = Color.FromArgb(217, 223, 215);
    private static readonly Color TextColor = Color.FromArgb(32, 36, 33);
    private static readonly Color MutedTextColor = Color.FromArgb(102, 112, 103);
    private static readonly Color AccentColor = Color.FromArgb(32, 119, 102);
    private static readonly Color AccentStrongColor = Color.FromArgb(23, 92, 80);
    private static readonly Color DangerColor = Color.FromArgb(182, 66, 60);

    private readonly AgentRunner _agentRunner = new();
    private readonly TextBox _serverInput = new();
    private readonly TextBox _accountInput = new();
    private readonly TextBox _tokenInput = new();
    private readonly TextBox _passwordInput = new();
    private readonly TextBox _deviceIdInput = new();
    private readonly TextBox _deviceNameInput = new();
    private readonly CheckBox _startOnLaunchInput = new();
    private readonly CheckBox _webRtcInput = new();
    private readonly Button _connectionSettingsButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _startAgentButton = new();
    private readonly Button _stopAgentButton = new();
    private readonly Button _openViewerButton = new();
    private readonly Label _agentStatus = new();
    private readonly Label _viewerStatus = new();
    private readonly TextBox _logOutput = new();
    private readonly WebView2 _webView = new();
    private bool _allowClose;
    private ClientSettings _settings;

    public MainForm()
    {
        _settings = ClientSettings.Load().Normalize();

        Text = "OwnDesk Client";
        Width = 1360;
        Height = 860;
        MinimumSize = new Size(1240, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBackground;
        Font = new Font("Segoe UI", 9F);

        BuildLayout();
        BindSettings(_settings);
        SetAgentRunning(false);

        _agentRunner.StatusChanged += (_, status) => PostStatus(status);
        _agentRunner.RunningChanged += (_, running) => PostAgentRunning(running);
        Shown += OnShownAsync;
        FormClosing += OnFormClosingAsync;
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
        var root = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = SettingsPanelWidth,
            SplitterWidth = 8,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 430,
            Panel2MinSize = 640,
            BackColor = LineColor
        };

        root.Panel1.BackColor = AppBackground;
        root.Panel2.BackColor = AppBackground;
        root.Panel1.Controls.Add(BuildSettingsPanel());
        root.Panel2.Controls.Add(_webView);
        _webView.Dock = DockStyle.Fill;

        Controls.Add(root);
    }

    private Control BuildSettingsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(18),
            BackColor = AppBackground
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            ForeColor = TextColor,
            Text = "设备与成员",
            Margin = new Padding(0, 0, 0, 2)
        };
        panel.Controls.Add(title);

        var subtitle = new Label
        {
            AutoSize = true,
            ForeColor = MutedTextColor,
            Margin = new Padding(0, 0, 0, 2),
            MaximumSize = new Size(SettingsPanelWidth - 42, 0),
            Text = "组织 Token 用于识别部署；账号和密码用于成员登录。"
        };
        panel.Controls.Add(subtitle);

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(0, 14, 0, 10),
            BackColor = AppBackground
        };

        AddField(form, "组织 Token", _tokenInput);
        AddField(form, "成员账号", _accountInput);
        AddField(form, "成员密码", _passwordInput);
        AddField(form, "设备 ID", _deviceIdInput);
        AddField(form, "设备名称", _deviceNameInput);

        _tokenInput.UseSystemPasswordChar = true;
        _passwordInput.UseSystemPasswordChar = true;

        _startOnLaunchInput.Text = "启动客户端时自动上线本机";
        _startOnLaunchInput.AutoSize = true;
        _webRtcInput.Text = "启用 WebRTC 实验视频";
        _webRtcInput.AutoSize = true;
        form.Controls.Add(_startOnLaunchInput);
        form.Controls.Add(_webRtcInput);

        panel.Controls.Add(form);

        var connectionPanel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Visible = false,
            BackColor = AppBackground,
            Padding = new Padding(0, 0, 0, 8)
        };
        var connectionForm = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = AppBackground
        };
        AddField(connectionForm, "连接地址", _serverInput);
        connectionPanel.Controls.Add(connectionForm);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            BackColor = AppBackground,
            Margin = new Padding(0, 0, 0, 8)
        };

        ConfigureButton(_connectionSettingsButton, "连接设置", (_, _) =>
        {
            connectionPanel.Visible = !connectionPanel.Visible;
            _connectionSettingsButton.Text = connectionPanel.Visible ? "隐藏连接设置" : "连接设置";
        });
        ConfigureButton(_saveButton, "保存", async (_, _) => await SaveSettingsAsync());
        ConfigureButton(_startAgentButton, "上线本机", async (_, _) => await StartAgentAsync(), primary: true);
        ConfigureButton(_stopAgentButton, "停止上线", async (_, _) => await StopAgentAsync(), danger: true);
        ConfigureButton(_openViewerButton, "打开控制台", async (_, _) => await NavigateViewerAsync(autoLogin: true));

        buttons.Controls.AddRange([_connectionSettingsButton, _saveButton, _startAgentButton, _stopAgentButton, _openViewerButton]);
        panel.Controls.Add(buttons);
        panel.Controls.Add(connectionPanel);

        _logOutput.Dock = DockStyle.Fill;
        _logOutput.Multiline = true;
        _logOutput.ReadOnly = true;
        _logOutput.ScrollBars = ScrollBars.Vertical;
        _logOutput.BorderStyle = BorderStyle.FixedSingle;
        _logOutput.BackColor = SurfaceColor;
        _logOutput.ForeColor = TextColor;
        _logOutput.Margin = new Padding(0, 4, 0, 10);
        panel.Controls.Add(_logOutput);

        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = AppBackground
        };

        _agentStatus.AutoSize = true;
        _viewerStatus.AutoSize = true;
        _agentStatus.ForeColor = MutedTextColor;
        _viewerStatus.ForeColor = MutedTextColor;
        statusPanel.Controls.Add(_agentStatus);
        statusPanel.Controls.Add(_viewerStatus);
        panel.Controls.Add(statusPanel);

        return panel;
    }

    private static void AddField(TableLayoutPanel form, string labelText, TextBox input)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = MutedTextColor,
            Margin = new Padding(0, 8, 0, 4)
        };

        input.Dock = DockStyle.Top;
        input.AutoSize = false;
        input.Height = 34;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.BackColor = SurfaceColor;
        input.ForeColor = TextColor;
        input.Margin = new Padding(0, 0, 0, 4);

        form.Controls.Add(label);
        form.Controls.Add(input);
    }

    private static void ConfigureButton(Button button, string text, EventHandler handler, bool primary = false, bool danger = false)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 34;
        button.Padding = new Padding(12, 0, 12, 0);
        button.Margin = new Padding(0, 0, 8, 8);
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        if (primary)
        {
            button.BackColor = AccentColor;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = AccentColor;
        }
        else if (danger)
        {
            button.BackColor = Color.FromArgb(252, 241, 240);
            button.ForeColor = DangerColor;
            button.FlatAppearance.BorderColor = Color.FromArgb(230, 188, 185);
        }
        else
        {
            button.BackColor = SurfaceMutedColor;
            button.ForeColor = primary ? Color.White : TextColor;
            button.FlatAppearance.BorderColor = LineColor;
        }

        button.Click += handler;
    }

    private void BindSettings(ClientSettings settings)
    {
        _serverInput.Text = settings.Server;
        _accountInput.Text = settings.Account;
        _tokenInput.Text = settings.Token;
        _passwordInput.Text = settings.Password;
        _deviceIdInput.Text = settings.DeviceId;
        _deviceNameInput.Text = settings.DeviceName;
        _startOnLaunchInput.Checked = settings.StartAgentOnLaunch;
        _webRtcInput.Checked = settings.EnableWebRtc;
    }

    private ClientSettings ReadSettings()
    {
        return new ClientSettings
        {
            Server = _serverInput.Text,
            Account = _accountInput.Text,
            Token = _tokenInput.Text,
            Password = _passwordInput.Text,
            DeviceId = _deviceIdInput.Text,
            DeviceName = _deviceNameInput.Text,
            StartAgentOnLaunch = _startOnLaunchInput.Checked,
            EnableWebRtc = _webRtcInput.Checked
        }.Normalize();
    }

    private async void OnShownAsync(object? sender, EventArgs eventArgs)
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _viewerStatus.Text = "控制台：已就绪";
            await NavigateViewerAsync(autoLogin: false);
        }
        catch (Exception ex)
        {
            _viewerStatus.Text = "控制台：不可用";
            AppendLog($"WebView2 failed: {ex.Message}");
        }

        if (_settings.StartAgentOnLaunch &&
            !string.IsNullOrWhiteSpace(_settings.Token) &&
            !string.IsNullOrWhiteSpace(_settings.Account) &&
            !string.IsNullOrWhiteSpace(_settings.Password))
        {
            await StartAgentAsync();
            await NavigateViewerAsync(autoLogin: true);
        }
    }

    private async void OnFormClosingAsync(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose)
        {
            return;
        }

        eventArgs.Cancel = true;
        _allowClose = true;
        Enabled = false;

        try
        {
            await _agentRunner.StopAsync();
        }
        finally
        {
            Close();
        }
    }

    private Task SaveSettingsAsync()
    {
        _settings = ReadSettings();
        _settings.Save();
        AppendLog($"Settings saved to {ClientSettings.SettingsPath}");
        return Task.CompletedTask;
    }

    private async Task StartAgentAsync()
    {
        try
        {
            _settings = ReadSettings();
            _settings.Save();
            await _agentRunner.StartAsync(_settings);
            SetAgentRunning(true);
            AppendLog($"Local agent started as {_settings.DeviceName} ({_settings.DeviceId})");
        }
        catch (Exception ex)
        {
            SetAgentRunning(false);
            AppendLog($"Start failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "OwnDesk Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task StopAgentAsync()
    {
        await _agentRunner.StopAsync();
        SetAgentRunning(false);
    }

    private async Task NavigateViewerAsync(bool autoLogin)
    {
        _settings = ReadSettings();
        if (autoLogin)
        {
            _settings.Save();
        }

        if (!Uri.TryCreate($"{_settings.Server}/index.html", UriKind.Absolute, out var uri))
        {
            _viewerStatus.Text = "连接地址无效";
            return;
        }

        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        TaskCompletionSource? navigation = null;

        if (autoLogin)
        {
            navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                _webView.NavigationCompleted -= Handler;
                navigation.TrySetResult();
            }

            _webView.NavigationCompleted += Handler;
        }

        _webView.Source = uri;
        _viewerStatus.Text = "控制台：已打开";

        if (navigation is not null)
        {
            await navigation.Task;
            await AutoLoginViewerAsync();
        }
    }

    private async Task AutoLoginViewerAsync()
    {
        if (_webView.CoreWebView2 is null ||
            string.IsNullOrWhiteSpace(_settings.Token) ||
            string.IsNullOrWhiteSpace(_settings.Account) ||
            string.IsNullOrWhiteSpace(_settings.Password))
        {
            return;
        }

        var account = JsonSerializer.Serialize(_settings.Account);
        var token = JsonSerializer.Serialize(_settings.Token);
        var password = JsonSerializer.Serialize(_settings.Password);
        var script = $$"""
            (() => {
              const account = document.getElementById("accountInput");
              const token = document.getElementById("organizationTokenInput");
              const password = document.getElementById("passwordInput");
              const form = document.getElementById("loginForm");
              if (!account || !token || !password || !form) {
                return "missing-login-form";
              }
              account.value = {{account}};
              token.value = {{token}};
              password.value = {{password}};
              if (typeof form.requestSubmit === "function") {
                form.requestSubmit();
              } else {
                form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
              }
              return "submitted";
            })();
            """;

        await _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private void SetAgentRunning(bool running)
    {
        _agentStatus.Text = running ? "本机上线：运行中" : "本机上线：已停止";
        _startAgentButton.Enabled = !running;
        _stopAgentButton.Enabled = running;
    }

    private void PostStatus(string status)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => PostStatus(status));
            return;
        }

        _agentStatus.Text = status;
        AppendLog(status);
    }

    private void PostAgentRunning(bool running)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => PostAgentRunning(running)));
            return;
        }

        SetAgentRunning(running);
    }

    private void AppendLog(string message)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss} {message}{Environment.NewLine}";
        _logOutput.AppendText(line);
    }
}
