namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private async Task ApplyEmbeddedWebRtcCompatibilityPatchAsync()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        const string script = """
            (() => {
              const report = (event, detail = {}) =>
                window.chrome?.webview?.postMessage({ type: "webrtcDiagnostic", event, detail });
              const sendControl = (payload) => {
                const channel = window.state?.webRtcControlChannel;
                if (!channel || channel.readyState !== "open") {
                  return false;
                }

                channel.send(JSON.stringify(payload));
                return true;
              };
              const setupControlChannel = (channel) => {
                if (!channel || channel.__ownDeskControlSetup) {
                  return;
                }

                channel.__ownDeskControlSetup = "1";
                window.state.webRtcControlChannel = channel;
                channel.addEventListener("open", () => report("data-channel-open", { label: channel.label }));
                channel.addEventListener("close", () => {
                  if (window.state?.webRtcControlChannel === channel) {
                    window.state.webRtcControlChannel = null;
                  }

                  report("data-channel-close", { label: channel.label });
                });
                channel.addEventListener("error", () => report("data-channel-error", { label: channel.label }));
              };
              if (window.RTCPeerConnection && !window.RTCPeerConnection.__ownDeskDataChannelPatched) {
                const NativePeerConnection = window.RTCPeerConnection;
                const PatchedPeerConnection = function (config = {}, ...rest) {
                  const peer = new NativePeerConnection(config, ...rest);
                  if (typeof window.setupWebRtcControlChannel !== "function" &&
                      window.state &&
                      !window.state.webRtcControlChannel) {
                    setupControlChannel(peer.createDataChannel("control", { ordered: true }));
                  }

                  return peer;
                };
                PatchedPeerConnection.prototype = NativePeerConnection.prototype;
                Object.setPrototypeOf(PatchedPeerConnection, NativePeerConnection);
                PatchedPeerConnection.__ownDeskDataChannelPatched = true;
                window.RTCPeerConnection = PatchedPeerConnection;
              }

              if (typeof window.sendInput === "function" && !window.sendInput.__ownDeskDataChannelWrapped) {
                const current = window.sendInput;
                const wrapped = function (payload) {
                  if (sendControl({ type: "input", ...payload })) {
                    return;
                  }

                  return current.apply(this, arguments);
                };
                wrapped.__ownDeskDataChannelWrapped = true;
                window.sendInput = wrapped;
              }

              if (typeof window.sendStreamQuality === "function" && !window.sendStreamQuality.__ownDeskDataChannelWrapped) {
                const current = window.sendStreamQuality;
                const wrapped = function (profile) {
                  if (sendControl({ type: "streamQuality", profile })) {
                    return;
                  }

                  return current.apply(this, arguments);
                };
                wrapped.__ownDeskDataChannelWrapped = true;
                window.sendStreamQuality = wrapped;
              }

              return "webrtc-compat-applied";
            })();
            """;

        try
        {
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
            ClientLog.Write($"WebView WebRTC compatibility script result: {result}");
        }
        catch (Exception ex)
        {
            ClientLog.WriteException("WebView WebRTC compatibility script failed", ex);
        }
    }
}
