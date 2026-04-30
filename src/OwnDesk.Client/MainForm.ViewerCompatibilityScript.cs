namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private static string BuildEmbeddedCompatibilityScript(
        string server,
        string account,
        string token,
        string sessionToken,
        string localDeviceId,
        string localAgentRunning)
    {
        return string.Concat(
            $$"""
            (() => {
              const clientSession = {
                server: {{server}},
                account: {{account}},
                token: {{token}},
                sessionToken: {{sessionToken}},
                localDeviceId: {{localDeviceId}},
                localAgentRunning: {{localAgentRunning}}
              };
              window.__ownDeskClientSession = {
                ...(window.__ownDeskClientSession || {}),
                ...clientSession
              };
              const pageState = () => {
                let value = window.state;
                if (!value) {
                  try {
                    value = typeof state !== "undefined" ? state : null;
                  } catch {
                    value = null;
                  }

                  if (value && typeof value === "object") {
                    window.state = value;
                  }
                }

                return value || null;
              };
              const updateLocalDeviceId = () => {
                const stateValue = pageState();
                if (stateValue) {
                  stateValue.localDeviceId = window.__ownDeskClientSession?.localDeviceId || "";
                }
              };
              updateLocalDeviceId();

              if (window.__ownDeskApplyClientCompatibility) {
                window.__ownDeskApplyClientCompatibility();
                return "compat-refreshed";
              }

              const retryDelays = [250, 900, 1800, 3500, 6500];
              const bandwidthTextPattern = /^[\d.]+(?:B|KB|M|GB)\/S$/;
              const connectionModePattern = /^(中继|直连|检测中)$/;
              const isLocalAgentRunning = () => Boolean(window.__ownDeskClientSession?.localAgentRunning);
              const isLocalDevice = (device) => {
                const stateValue = pageState();
                const localDeviceId = window.__ownDeskClientSession?.localDeviceId || stateValue?.localDeviceId || "";
                return Boolean(localDeviceId && device?.deviceId === localDeviceId);
              };
              const isLocalRunningDevice = (device) => isLocalAgentRunning() && isLocalDevice(device);

              const historyKey = () => {
                const auth = readAuth();
                return `ownDesk.embeddedDevices.${location.origin}.${auth.account || "anonymous"}`;
              };
              const normalize = (device, defaultOnline) => {
                const hasOnline = typeof device.online === "boolean" || typeof device.Online === "boolean";
                const normalized = {
                  deviceId: device.deviceId || device.DeviceId || "",
                  deviceName: device.deviceName || device.DeviceName || device.deviceId || device.DeviceId || "",
                  screenWidth: device.screenWidth || device.ScreenWidth || 0,
                  screenHeight: device.screenHeight || device.ScreenHeight || 0,
                  online: hasOnline ? Boolean(device.online ?? device.Online) : defaultOnline
                };
                if (isLocalRunningDevice(normalized)) {
                  normalized.online = true;
                }

                return normalized;
              };
              const loadHistory = () => {
                try {
                  return JSON.parse(localStorage.getItem(historyKey()) || "[]");
                } catch {
                  return [];
                }
              };
              const saveHistory = (devices) => {
                localStorage.setItem(historyKey(), JSON.stringify(devices));
              };
              const mergeHistory = (devices) => {
                const map = new Map(loadHistory().map((device) => {
                  const normalized = normalize({ ...device, online: false, Online: false }, false);
                  return [normalized.deviceId, normalized];
                }));
                for (const item of devices || []) {
                  const device = normalize(item, true);
                  if (device.deviceId) {
                    map.set(device.deviceId, device);
                  }
                }

                const merged = [...map.values()].sort((left, right) =>
                  Number(right.online) - Number(left.online) ||
                  left.deviceName.localeCompare(right.deviceName));
                saveHistory(merged.map((device) => ({ ...device, online: false })));
                return merged;
              };
              const readAuth = () => {
                const injected = window.__ownDeskClientSession || {};
                const stateValue = pageState() || {};
                return {
                  account: stateValue.account || injected.account || document.getElementById("accountInput")?.value?.trim() || "",
                  token: stateValue.token || injected.token || document.getElementById("organizationTokenInput")?.value?.trim() || "",
                  sessionToken: stateValue.sessionToken || injected.sessionToken || ""
                };
              };
              const apiEndpoint = (path) => {
                if (typeof window.apiUrl === "function") {
                  return window.apiUrl(path);
                }

                const injected = window.__ownDeskClientSession || {};
                const base = String(injected.server || location.origin).replace(/\/+$/, "");
                return `${base}${path}`;
              };
              const socketEndpoint = (path) => {
                if (typeof window.webSocketUrl === "function") {
                  return window.webSocketUrl(path);
                }

                const endpoint = new URL(apiEndpoint(path));
                endpoint.protocol = endpoint.protocol === "https:" ? "wss:" : "ws:";
                return endpoint.toString();
              };
              const authPayload = (extra = {}) => {
                const auth = readAuth();
                return {
                  type: "auth",
                  account: auth.account,
                  token: auth.token,
                  sessionToken: auth.sessionToken,
                  ...extra
                };
              };
              const hasTurnServerCompat = (servers) =>
                (servers || []).some((server) => {
                  const urls = Array.isArray(server?.urls) ? server.urls : [server?.urls];
                  return urls.some((url) => /^turns?:/i.test(String(url || "")));
                });
              const normalizeIceTransportPolicyCompat = (policy, servers) =>
                policy === "relay" && hasTurnServerCompat(servers) ? "relay" : "all";
              const ensureIceServersCompat = async () => {
                if (window.__ownDeskWebRtcIceLoaded) {
                  return;
                }

                window.__ownDeskWebRtcIceLoaded = true;
                try {
                  const response = await fetch(apiEndpoint("/api/webrtc/config"), {
                    method: "POST",
                    headers: { "content-type": "application/json" },
                    body: JSON.stringify(authPayload())
                  });
                  const config = response.ok ? await response.json() : {};
                  const iceServers = Array.isArray(config.iceServers) ? config.iceServers : [];
                  window.__ownDeskWebRtcIceServers = iceServers;
                  window.__ownDeskWebRtcIceTransportPolicy = normalizeIceTransportPolicyCompat(
                    config.iceTransportPolicy,
                    iceServers);
                } catch {
                  window.__ownDeskWebRtcIceServers = [];
                  window.__ownDeskWebRtcIceTransportPolicy = "all";
                }
              };
              const installRtcPeerConnectionPatch = () => {
                if (!window.RTCPeerConnection || window.RTCPeerConnection.__ownDeskIcePatched) {
                  return;
                }

                const NativePeerConnection = window.RTCPeerConnection;
                const PatchedPeerConnection = function (config = {}, ...rest) {
                  const iceServers = window.__ownDeskWebRtcIceServers || [];
                  const iceTransportPolicy = window.__ownDeskWebRtcIceTransportPolicy || "all";
                  const nextConfig = { ...config };
                  if ((!nextConfig.iceServers || nextConfig.iceServers.length === 0) && iceServers.length > 0) {
                    nextConfig.iceServers = iceServers;
                  }
                  if (!nextConfig.iceTransportPolicy) {
                    nextConfig.iceTransportPolicy = iceTransportPolicy;
                  }

                  return new NativePeerConnection(nextConfig, ...rest);
                };
                PatchedPeerConnection.prototype = NativePeerConnection.prototype;
                Object.setPrototypeOf(PatchedPeerConnection, NativePeerConnection);
                PatchedPeerConnection.__ownDeskIcePatched = true;
                window.RTCPeerConnection = PatchedPeerConnection;
              };
              const isSelectedDevice = (device) => {
                const stateValue = pageState();
                return Boolean(stateValue?.selectedDevice?.deviceId && stateValue.selectedDevice.deviceId === device.deviceId);
              };
              const isConnectedDevice = (device) => {
                const stateValue = pageState();
                const activeSocket = stateValue?.socket;
                if (device?.deviceId && window.__ownDeskConnectingDeviceId === device.deviceId) {
                  return true;
                }

                if (isSelectedDevice(device) && activeSocket?.readyState <= WebSocket.OPEN) {
                  return true;
                }

                const activeText = document.getElementById("activeDevice")?.textContent?.trim() || "";
                return Boolean(
                  activeText &&
                  activeText !== "未连接" &&
                  activeSocket?.readyState <= WebSocket.OPEN &&
                  (activeText === device?.deviceId || activeText === device?.deviceName));
              };
              const rerenderDevices = () => renderDevices(pageState()?.devices || loadHistory());
              const rerenderDevicesSoon = () => window.setTimeout(rerenderDevices, 80);
              const startDirectForConnectedSoon = (device) => {
                if ((!device?.online && !isConnectedDevice(device) && !isLocalRunningDevice(device)) || typeof window.startWebRtc !== "function") {
                  return;
                }

                for (const delay of [150, 450, 1000, 2200, 4500, 8000]) {
                  window.setTimeout(() => {
                    if (isConnectedDevice(device) && !pageState()?.webRtcPeer) {
                      ensureIceServersCompat().finally(() => Promise.resolve(window.startWebRtc()).catch(() => {}));
                    }
                  }, delay);
                }
              };
              const connectDeviceCompat = (device) => {
                if (!device?.deviceId) {
                  return;
                }

                if (isConnectedDevice(device)) {
                  window.__ownDeskConnectingDeviceId = "";
                  window.disconnectViewer?.();
                  rerenderDevicesSoon();
                  return;
                }

                window.__ownDeskConnectingDeviceId = device.deviceId;
                const stateValue = pageState();
                if (stateValue) {
                  stateValue.selectedDevice = { ...device, online: true };
                }

                rerenderDevices();
                window.connectViewer?.(device);
                startDirectForConnectedSoon(device);
                rerenderDevicesSoon();
              };
              const replaceLabels = () => {
                for (const node of document.querySelectorAll("span, div, strong, button")) {
                  if (node.childNodes.length === 1 && node.firstChild?.nodeType === Node.TEXT_NODE) {
                    const currentText = node.textContent || "";
                    const nextText = currentText
                      .replace("在线设备", "设备")
                      .replace("没有在线设备", "没有设备")
                      .replace("登录后显示本组织的在线设备。", "登录后显示本组织的设备。");
                    if (nextText !== currentText) {
            """,
            EmbeddedCompatibilityScriptMiddle,
            EmbeddedCompatibilityScriptEnd);
    }
}
