namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private async Task RefreshEmbeddedDevicesSoonAsync()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        const string script = """
            (() => {
              window.__ownDeskApplyClientCompatibility?.();
              if (typeof window.__ownDeskRefreshDevicesSoon === "function") {
                window.__ownDeskRefreshDevicesSoon();
                return "refresh-scheduled";
              }

              if (typeof window.refreshDevices === "function") {
                window.refreshDevices();
                return "refresh-called";
              }

              return "refresh-unavailable";
            })();
            """;

        try
        {
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
            if (!string.Equals(result, "\"refresh-unavailable\"", StringComparison.Ordinal))
            {
                ClientLog.Write($"WebView device refresh script result: {result}");
            }
        }
        catch (Exception ex)
        {
            ClientLog.WriteException("WebView device refresh script failed", ex);
        }
    }

    private async Task UpdateEmbeddedLocalAgentRunningAsync(bool running)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var script = $"window.__ownDeskSetLocalAgentRunning?.({running.ToString().ToLowerInvariant()});";
        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            ClientLog.WriteException("WebView local agent state script failed", ex);
        }
    }
}
