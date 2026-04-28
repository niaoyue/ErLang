namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private async void OnShownAsync(object? sender, EventArgs eventArgs)
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
            ConfigureWebViewHostBridge();
            _viewerStatus.Text = "控制台：已就绪";
        }
        catch (Exception ex)
        {
            _viewerStatus.Text = "控制台：不可用";
            AppendLog($"WebView2 failed: {ex.Message}");
        }

        await TryAutoLoginCurrentOrganizationAsync();
        await StartAgentIfConfiguredAsync();
        await NavigateViewerAsync(CurrentOrganization.SignedIn);
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

    private async Task StartAgentAsync()
    {
        var organization = SaveCurrentOrganization(rebind: false);
        if (!organization.SignedIn)
        {
            throw new InvalidOperationException("请先登录当前组织。");
        }

        await _agentRunner.StartAsync(organization, _settings);
        SetAgentRunning(true);
        AppendLog($"Local agent started as {_settings.DeviceName} ({_settings.DeviceId})");
        await RefreshEmbeddedDevicesSoonAsync();
    }

    private async Task StopAgentAsync()
    {
        await _agentRunner.StopAsync();
        SetAgentRunning(false);
    }

    private async Task StartAgentIfConfiguredAsync(bool force = false)
    {
        if ((!force && !_settings.StartAgentOnLaunch) || !CurrentOrganization.SignedIn || _agentRunner.IsRunning)
        {
            return;
        }

        await StartAgentAsync();
    }
}
