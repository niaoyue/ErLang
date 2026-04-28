using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private bool _settingsCollapsedBeforeFullscreen;
    private Rectangle _windowedBounds;
    private FormBorderStyle _windowedBorderStyle;
    private FormWindowState _windowedState;
    private bool _windowedTopMost;
    private bool _webMessageHooked;
    private bool _webDiagnosticsHooked;

    private void ConfigureWebViewHostBridge()
    {
        if (_webView.CoreWebView2 is null || _webMessageHooked)
        {
            return;
        }

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webMessageHooked = true;
        ConfigureWebViewDiagnostics();
    }

    private void ConfigureWebViewDiagnostics()
    {
        if (_webView.CoreWebView2 is null || _webDiagnosticsHooked)
        {
            return;
        }

        _webDiagnosticsHooked = true;
        _webView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            _viewerStatus.Text = "控制台：加载中";
            ClientLog.Write($"WebView navigating {args.Uri}");
        };
        _webView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            var status = args.IsSuccess
                ? "success"
                : $"failed {args.WebErrorStatus}";
            ClientLog.Write($"WebView navigation {status}");
        };
        _webView.CoreWebView2.ProcessFailed += (_, args) =>
            ClientLog.Write($"WebView process failed: {args.ProcessFailedKind}");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            if (!message.RootElement.TryGetProperty("type", out var type))
            {
                return;
            }

            switch (type.GetString())
            {
                case "hostFullscreen":
                    var active = message.RootElement.TryGetProperty("active", out var activeElement) &&
                                 activeElement.ValueKind == JsonValueKind.True;
                    SetHostFullscreen(active, notifyWeb: false);
                    break;
                case "webrtcDiagnostic":
                    ClientLog.Write($"WebRTC diagnostic: {message.RootElement}");
                    break;
            }
        }
        catch (JsonException ex)
        {
            AppendLog($"Host message ignored: {ex.Message}");
        }
    }

    private void OnMainFormKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyCode != Keys.Escape || !_hostFullscreen)
        {
            return;
        }

        args.Handled = true;
        SetHostFullscreen(false, notifyWeb: true);
    }

    private void SetHostFullscreen(bool active, bool notifyWeb)
    {
        if (_hostFullscreen == active)
        {
            return;
        }

        if (active)
        {
            EnterHostFullscreen();
        }
        else
        {
            ExitHostFullscreen();
        }

        if (notifyWeb)
        {
            _ = SyncHostFullscreenToWebAsync(active);
        }
    }

    private void EnterHostFullscreen()
    {
        _hostFullscreen = true;
        _settingsCollapsedBeforeFullscreen = _settingsCollapsed;
        _windowedBounds = Bounds;
        _windowedBorderStyle = FormBorderStyle;
        _windowedState = WindowState;
        _windowedTopMost = TopMost;

        if (_rootSplit is not null)
        {
            _rootSplit.Panel1Collapsed = true;
            _rootSplit.SplitterWidth = 1;
        }

        var screenBounds = Screen.FromControl(this).Bounds;
        SuspendLayout();
        WindowState = FormWindowState.Normal;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        Bounds = screenBounds;
        ResumeLayout(performLayout: true);
    }

    private void ExitHostFullscreen()
    {
        SuspendLayout();
        TopMost = _windowedTopMost;
        FormBorderStyle = _windowedBorderStyle;
        Bounds = _windowedBounds;
        WindowState = _windowedState;

        if (_rootSplit is not null)
        {
            _rootSplit.Panel1Collapsed = false;
            _rootSplit.SplitterWidth = _settingsCollapsedBeforeFullscreen ? 4 : 8;
        }

        _hostFullscreen = false;
        ResumeLayout(performLayout: true);
        SetSettingsCollapsed(_settingsCollapsedBeforeFullscreen);
    }

    private async Task SyncHostFullscreenToWebAsync(bool active)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var value = active ? "true" : "false";
        await _webView.CoreWebView2.ExecuteScriptAsync($"window.__ownDeskSetHostFullscreen?.({value});");
    }
}
