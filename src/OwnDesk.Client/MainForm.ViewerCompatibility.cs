using System.Text.Json;

namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private async Task ApplyEmbeddedCompatibilityPatchAsync()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var organization = CurrentOrganization;
        var script = BuildEmbeddedCompatibilityScript(
            JsonSerializer.Serialize(organization.Server),
            JsonSerializer.Serialize(organization.Account),
            JsonSerializer.Serialize(organization.Token),
            JsonSerializer.Serialize(organization.SessionToken),
            JsonSerializer.Serialize(_settings.DeviceId),
            _agentRunner.IsRunning ? "true" : "false");

        try
        {
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
            ClientLog.Write($"WebView compatibility script result: {result}");
        }
        catch (Exception ex)
        {
            ClientLog.WriteException("WebView compatibility script failed", ex);
            throw;
        }
    }
}
