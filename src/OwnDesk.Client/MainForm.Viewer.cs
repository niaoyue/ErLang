using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private async Task OpenViewerAsync()
    {
        SaveCurrentOrganization(rebind: false);
        await NavigateViewerAsync(CurrentOrganization.SignedIn);
    }

    private async Task NavigateViewerAsync(bool autoLogin)
    {
        var organization = CurrentOrganization;
        if (string.IsNullOrWhiteSpace(organization.Server))
        {
            _viewerStatus.Text = "控制台：未配置服务器";
            return;
        }

        var server = organization.Server.TrimEnd('/');
        if (!Uri.TryCreate($"{server}/index.html?embedded=client&shell=20260429-relay-state1", UriKind.Absolute, out var uri))
        {
            _viewerStatus.Text = "控制台：服务器地址无效";
            return;
        }

        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var navigation = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            _webView.NavigationCompleted -= Handler;
            navigation.TrySetResult(args);
        }

        _webView.NavigationCompleted += Handler;
        _webView.Source = uri;
        _viewerStatus.Text = "控制台：已打开";
        var completed = await navigation.Task;
        ClientLog.Write($"WebView navigation completed: success={completed.IsSuccess}, status={completed.WebErrorStatus}");
        if (!completed.IsSuccess)
        {
            _viewerStatus.Text = $"控制台：加载失败 {completed.WebErrorStatus}";
            await ShowViewerErrorAsync($"控制台加载失败：{completed.WebErrorStatus}");
            return;
        }

        ClientLog.Write("WebView applying embedded shell");
        await ApplyEmbeddedViewerShellAsync();
        ClientLog.Write("WebView embedded shell applied");
        await VerifyEmbeddedViewerReadyAsync();

        if (autoLogin)
        {
            ClientLog.Write("WebView auto login starting");
            await AutoLoginViewerAsync();
            ClientLog.Write("WebView auto login finished");
        }
    }

    private Task ShowViewerErrorAsync(string message)
    {
        if (_webView.CoreWebView2 is null)
        {
            return Task.CompletedTask;
        }

        var safeMessage = System.Net.WebUtility.HtmlEncode(message);
        _webView.CoreWebView2.NavigateToString($$"""
            <!doctype html>
            <meta charset="utf-8">
            <style>
              body {
                margin: 0;
                height: 100vh;
                display: grid;
                place-items: center;
                font: 14px "Microsoft YaHei UI", sans-serif;
                color: #18181b;
                background: #f8f8f9;
              }

              .message {
                max-width: 520px;
                padding: 18px;
                border: 1px solid #e4e4e7;
                border-radius: 8px;
                background: #fff;
              }
            </style>
            <div class="message">{{safeMessage}}</div>
            """);
        return Task.CompletedTask;
    }

    private async Task VerifyEmbeddedViewerReadyAsync()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        const string script = """
            (() => JSON.stringify({
              title: document.title || "",
              hasBody: Boolean(document.body),
              hasWorkspace: Boolean(document.getElementById("workspace")),
              hasDeviceList: Boolean(document.getElementById("deviceList")),
              textLength: document.body?.innerText?.trim().length || 0
            }))();
            """;

        var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
        ClientLog.Write($"WebView ready probe: {result}");
    }

    private async Task ApplyEmbeddedViewerShellAsync()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        const string script = """
            (() => {
              const root = document.documentElement;
              root.classList.add("embedded-client");

              if (!document.getElementById("ownDeskClientShellStyle")) {
                const style = document.createElement("style");
                style.id = "ownDeskClientShellStyle";
                style.textContent = `
                  html.embedded-client .topbar,
                  html.embedded-client .organization-panel,
                  html.embedded-client .member-panel,
                  html.embedded-client .auth-panel {
                    display: none !important;
                  }

                  html.embedded-client .shell {
                    height: 100vh !important;
                    min-height: 0 !important;
                    padding: 8px !important;
                    grid-template-rows: 1fr !important;
                    gap: 0 !important;
                    overflow: hidden !important;
                  }

                  html.embedded-client .layout {
                    height: calc(100vh - 16px) !important;
                    min-height: 0 !important;
                    overflow: hidden !important;
                  }

                  html.embedded-client .sidebar {
                    gap: 8px !important;
                  }

                  html.embedded-client .device-actions {
                    display: inline-flex !important;
                    align-items: center !important;
                    gap: 14px !important;
                    column-gap: 14px !important;
                  }

                  html.embedded-client .device-actions > button + button {
                    margin-left: 8px !important;
                  }

                  html.embedded-client .device-card button {
                    min-width: 56px !important;
                  }

                  html.embedded-client .device-card button.disconnect {
                    border-color: #d7a34d !important;
                    color: #7a4a06 !important;
                    background: #fff7e8 !important;
                  }

                  html.embedded-client .workspace {
                    height: 100% !important;
                    overflow: hidden !important;
                  }

                  html.embedded-client .screen-shell {
                    overflow: hidden !important;
                    scrollbar-width: none !important;
                    -ms-overflow-style: none !important;
                  }

                  html.embedded-client .screen-shell::-webkit-scrollbar {
                    width: 0 !important;
                    height: 0 !important;
                  }

                  html.embedded-client .empty-screen {
                    left: 14px !important;
                    bottom: 12px !important;
                  }

                  html.host-fullscreen body {
                    overflow: hidden !important;
                    background: #050606 !important;
                  }

                  html.host-fullscreen .shell {
                    min-height: 100vh !important;
                    padding: 0 !important;
                    display: block !important;
                  }

                  html.host-fullscreen .layout {
                    height: 100vh !important;
                    display: block !important;
                  }

                  html.host-fullscreen .sidebar {
                    display: none !important;
                  }

                  html.host-fullscreen .workspace {
                    width: 100vw !important;
                    height: 100vh !important;
                    position: relative !important;
                    display: block !important;
                    overflow: hidden !important;
                    padding: 0 !important;
                    background: #050606 !important;
                  }

                  html.host-fullscreen .screen-bar {
                    position: absolute !important;
                    z-index: 20 !important;
                    top: 8px !important;
                    left: 8px !important;
                    right: 8px !important;
                    min-height: 0 !important;
                    justify-content: center !important;
                    padding: 6px !important;
                    border-radius: 8px !important;
                    background: rgb(16 20 19 / 78%) !important;
                  }

                  html.host-fullscreen .screen-shell {
                    position: absolute !important;
                    inset: 0 !important;
                    min-height: 0 !important;
                    padding: 0 !important;
                    border: 0 !important;
                    border-radius: 0 !important;
                    box-shadow: none !important;
                  }
                `;
                document.head.appendChild(style);
              }

              function setHostFullscreen(active, notifyHost) {
                root.classList.toggle("host-fullscreen", active);
                document.getElementById("workspace")?.classList.toggle("host-fullscreen", active);
                const button = document.getElementById("fullscreenButton");
                if (button) {
                  button.classList.toggle("active", active);
                  button.textContent = active ? "□" : "⛶";
                }

                if (notifyHost) {
                  window.chrome?.webview?.postMessage({ type: "hostFullscreen", active });
                }
              }

              function exitFullscreenForDisconnect() {
                setHostFullscreen(false, true);
                if (document.fullscreenElement && document.exitFullscreen) {
                  document.exitFullscreen().catch(() => {});
                }
              }

              window.__ownDeskSetHostFullscreen = (active) => setHostFullscreen(Boolean(active), false);
              window.__ownDeskExitFullscreenForDisconnect = exitFullscreenForDisconnect;

              for (const name of ["disconnectViewer", "markDisconnected"]) {
                const current = window[name];
                if (typeof current === "function" && !current.__ownDeskExitFullscreenWrapped) {
                  const wrapped = function (...args) {
                    exitFullscreenForDisconnect();
                    return current.apply(this, args);
                  };
                  wrapped.__ownDeskExitFullscreenWrapped = true;
                  window[name] = wrapped;
                }
              }

              const button = document.getElementById("fullscreenButton");
              if (button && !button.dataset.ownDeskHostFullscreen) {
                button.dataset.ownDeskHostFullscreen = "1";
                button.addEventListener("click", (event) => {
                  event.preventDefault();
                  event.stopImmediatePropagation();
                  setHostFullscreen(!root.classList.contains("host-fullscreen"), true);
                }, true);
              }

              const disconnectButton = document.getElementById("disconnectButton");
              if (disconnectButton && !disconnectButton.dataset.ownDeskExitFullscreen) {
                disconnectButton.dataset.ownDeskExitFullscreen = "1";
                disconnectButton.addEventListener("click", exitFullscreenForDisconnect, true);
              }

              const status = document.getElementById("connectionStatus");
              if (status && !status.dataset.ownDeskExitFullscreenObserver) {
                status.dataset.ownDeskExitFullscreenObserver = "1";
                const observer = new MutationObserver(() => {
                  const offline = status.classList.contains("offline") || status.textContent?.includes("离线");
                  if (offline) {
                    exitFullscreenForDisconnect();
                  }
                });
                observer.observe(status, { attributes: true, childList: true, subtree: true });
              }

              return "embedded-shell-applied";
            })();
            """;

        try
        {
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
            ClientLog.Write($"WebView shell script result: {result}");
        }
        catch (Exception ex)
        {
            ClientLog.WriteException("WebView shell script failed", ex);
            throw;
        }

        await ApplyEmbeddedCompatibilityPatchAsync();
        await ApplyEmbeddedWebRtcCompatibilityPatchAsync();
    }

    private async Task AutoLoginViewerAsync()
    {
        var organization = CurrentOrganization;
        if (_webView.CoreWebView2 is null || !organization.SignedIn || !organization.HasSavedCredentials)
        {
            return;
        }

        var account = JsonSerializer.Serialize(organization.Account);
        var token = JsonSerializer.Serialize(organization.Token);
        var password = JsonSerializer.Serialize(organization.Password);
        var sessionToken = JsonSerializer.Serialize(organization.SessionToken);
        var script = $$"""
            (() => {
              const accountValue = {{account}};
              const tokenValue = {{token}};
              const passwordValue = {{password}};
              const sessionTokenValue = {{sessionToken}};
              const refreshAfterLogin = () => {
                if (typeof refreshDevicesAfterLogin === "function") {
                  refreshDevicesAfterLogin();
                  return true;
                }

                if (typeof refreshDevices === "function") {
                  refreshDevices();
                  return true;
                }

                return false;
              };

              if (typeof state === "object" && state && sessionTokenValue) {
                state.account = accountValue;
                state.token = tokenValue;
                state.password = passwordValue;
                state.sessionToken = sessionTokenValue;
                document.getElementById("serverStatus").textContent = "已登录";
                if (typeof window.__ownDeskApplyClientCompatibility === "function") {
                  window.__ownDeskApplyClientCompatibility();
                }

                if (typeof setAuthPanelVisible === "function") {
                  setAuthPanelVisible(false);
                }

                if (typeof startDeviceUpdates === "function") {
                  startDeviceUpdates();
                }

                refreshAfterLogin();
                if (typeof window.__ownDeskRefreshDevicesSoon === "function") {
                  window.__ownDeskRefreshDevicesSoon();
                }

                return "session-refreshed";
              }

              const loginButton = document.getElementById("loginModeButton");
              if (loginButton) {
                loginButton.click();
              }
              const account = document.getElementById("accountInput");
              const token = document.getElementById("organizationTokenInput");
              const password = document.getElementById("passwordInput");
              const form = document.getElementById("loginForm");
              if (!account || !token || !password || !form) {
                return "missing-login-form";
              }
              account.value = accountValue;
              token.value = tokenValue;
              password.value = passwordValue;
              if (typeof form.requestSubmit === "function") {
                form.requestSubmit();
              } else {
                form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
              }
              window.setTimeout(refreshAfterLogin, 800);
              if (typeof window.__ownDeskRefreshDevicesSoon === "function") {
                window.__ownDeskRefreshDevicesSoon();
              }

              return "submitted";
            })();
            """;

        await _webView.CoreWebView2.ExecuteScriptAsync(script);
        await RefreshEmbeddedDevicesSoonAsync();
    }
}
