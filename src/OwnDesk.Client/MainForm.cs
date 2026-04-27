using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OwnDesk.Client;

internal sealed class MainForm : Form
{
    private readonly AgentRunner _agentRunner = new();
    private readonly TextBox _serverInput = new();
    private readonly TextBox _accountInput = new();
    private readonly TextBox _tokenInput = new();
    private readonly TextBox _deviceIdInput = new();
    private readonly TextBox _deviceNameInput = new();
    private readonly CheckBox _startOnLaunchInput = new();
    private readonly CheckBox _webRtcInput = new();
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
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;

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
            SplitterDistance = 360,
            FixedPanel = FixedPanel.Panel1
        };

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
            RowCount = 5,
            Padding = new Padding(16)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "OwnDesk Client"
        };
        panel.Controls.Add(title);

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(0, 16, 0, 8)
        };

        AddField(form, "Server", _serverInput);
        AddField(form, "Account", _accountInput);
        AddField(form, "Token", _tokenInput);
        AddField(form, "Device ID", _deviceIdInput);
        AddField(form, "Device Name", _deviceNameInput);

        _tokenInput.UseSystemPasswordChar = true;

        _startOnLaunchInput.Text = "Start local agent on launch";
        _startOnLaunchInput.AutoSize = true;
        _webRtcInput.Text = "Enable WebRTC video";
        _webRtcInput.AutoSize = true;
        form.Controls.Add(_startOnLaunchInput);
        form.Controls.Add(_webRtcInput);

        panel.Controls.Add(form);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true
        };

        ConfigureButton(_saveButton, "Save", async (_, _) => await SaveSettingsAsync());
        ConfigureButton(_startAgentButton, "Start Agent", async (_, _) => await StartAgentAsync());
        ConfigureButton(_stopAgentButton, "Stop Agent", async (_, _) => await StopAgentAsync());
        ConfigureButton(_openViewerButton, "Open Viewer", async (_, _) => await NavigateViewerAsync(autoLogin: true));

        buttons.Controls.AddRange([_saveButton, _startAgentButton, _stopAgentButton, _openViewerButton]);
        panel.Controls.Add(buttons);

        _logOutput.Dock = DockStyle.Fill;
        _logOutput.Multiline = true;
        _logOutput.ReadOnly = true;
        _logOutput.ScrollBars = ScrollBars.Vertical;
        _logOutput.BorderStyle = BorderStyle.FixedSingle;
        panel.Controls.Add(_logOutput);

        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 1
        };

        _agentStatus.AutoSize = true;
        _viewerStatus.AutoSize = true;
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
            Margin = new Padding(0, 8, 0, 3)
        };

        input.Dock = DockStyle.Top;
        input.Margin = new Padding(0, 0, 0, 4);

        form.Controls.Add(label);
        form.Controls.Add(input);
    }

    private static void ConfigureButton(Button button, string text, EventHandler handler)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Margin = new Padding(0, 0, 8, 8);
        button.Click += handler;
    }

    private void BindSettings(ClientSettings settings)
    {
        _serverInput.Text = settings.Server;
        _accountInput.Text = settings.Account;
        _tokenInput.Text = settings.Token;
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
            _viewerStatus.Text = "Viewer ready";
            await NavigateViewerAsync(autoLogin: false);
        }
        catch (Exception ex)
        {
            _viewerStatus.Text = "Viewer unavailable";
            AppendLog($"WebView2 failed: {ex.Message}");
        }

        if (_settings.StartAgentOnLaunch && !string.IsNullOrWhiteSpace(_settings.Token))
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
            _viewerStatus.Text = "Invalid server URL";
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
        _viewerStatus.Text = $"Viewer: {uri.Host}";

        if (navigation is not null)
        {
            await navigation.Task;
            await AutoLoginViewerAsync();
        }
    }

    private async Task AutoLoginViewerAsync()
    {
        if (_webView.CoreWebView2 is null || string.IsNullOrWhiteSpace(_settings.Token))
        {
            return;
        }

        var account = JsonSerializer.Serialize(_settings.Account);
        var token = JsonSerializer.Serialize(_settings.Token);
        var script = $$"""
            (() => {
              const account = document.getElementById("accountInput");
              const token = document.getElementById("tokenInput");
              const form = document.getElementById("loginForm");
              if (!account || !token || !form) {
                return "missing-login-form";
              }
              account.value = {{account}};
              token.value = {{token}};
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
        _agentStatus.Text = running ? "Agent: running" : "Agent: stopped";
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
