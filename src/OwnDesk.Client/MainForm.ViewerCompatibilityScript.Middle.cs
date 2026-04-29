namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private const string EmbeddedCompatibilityScriptMiddle =
            """
                      node.textContent = nextText;
                    }
                  }
                }
              };
              let labelRefreshPending = false;
              const scheduleReplaceLabels = () => {
                if (labelRefreshPending) {
                  return;
                }

                labelRefreshPending = true;
                window.setTimeout(() => {
                  labelRefreshPending = false;
                  replaceLabels();
                }, 50);
              };
              const renderDevices = (devices) => {
                const list = document.getElementById("deviceList");
                if (!list) {
                  return;
                }

                const merged = mergeHistory(devices);
                list.innerHTML = "";
                if (!merged.length) {
                  const empty = document.createElement("div");
                  empty.className = "empty-list";
                  empty.textContent = "没有设备";
                  list.appendChild(empty);
                  return;
                }

                const stateValue = pageState();
                if (stateValue) {
                  stateValue.devices = merged;
                }

                for (const device of merged) {
                  const connected = isConnectedDevice(device);
                  const online = device.online || connected || isLocalRunningDevice(device);
                  const card = document.createElement("div");
                  card.className = "device-card";
                  card.classList.toggle("is-offline", !online);

                  const meta = document.createElement("div");
                  const name = document.createElement("strong");
                  name.textContent = device.deviceName || device.deviceId;
                  const detail = document.createElement("div");
                  detail.className = "subtle";
                  detail.textContent = `${online ? "在线" : "离线"} · ${device.screenWidth || "-"}×${device.screenHeight || "-"} · ${device.deviceId}`;
                  meta.append(name, detail);

                  const actions = document.createElement("div");
                  actions.className = "device-actions";
                  const connect = document.createElement("button");
                  connect.type = "button";
                  connect.className = connected ? "disconnect" : "connect";
                  connect.textContent = connected ? "断开" : "连接";
                  connect.disabled = !online && !connected;
                  connect.addEventListener("click", () => connectDeviceCompat(device));
                  const remove = document.createElement("button");
                  remove.type = "button";
                  remove.className = "danger";
                  remove.textContent = "移除";
                  remove.addEventListener("click", async () => {
                    saveHistory(loadHistory().filter((item) => item.deviceId !== device.deviceId));
                    await removeDeviceCompat(device.deviceId);
                  });
                  actions.append(connect, remove);
                  card.append(meta, actions);
                  list.appendChild(card);
                }
              };
              const fetchAndRenderDevices = async (reason = "manual") => {
                const auth = readAuth();
                if (!auth.account || !auth.token || !auth.sessionToken) {
                  renderDevices([]);
                  return false;
                }

                const response = await fetch(apiEndpoint("/api/devices"), {
                  method: "POST",
                  headers: {
                    "content-type": "application/json"
                  },
                  body: JSON.stringify(authPayload())
                });
                if (!response.ok) {
                  throw new Error(response.status === 401 ? "认证失败" : `HTTP ${response.status}`);
                }

                const devices = await response.json();
                renderDevices(Array.isArray(devices) ? devices : []);
                document.getElementById("serverStatus")?.replaceChildren(document.createTextNode("已登录"));
                replaceLabels();
                return true;
              };
              const refreshDevicesCompat = async (reason = "manual") => {
                try {
                  return await fetchAndRenderDevices(reason);
                } catch (error) {
                  const status = document.getElementById("serverStatus");
                  if (status) {
                    status.textContent = error?.message || "设备刷新失败";
                  }

                  if (reason !== "retry" && /^(HTTP 5|Failed to fetch|NetworkError|Load failed|超时)/.test(error?.message || "")) {
                    window.setTimeout(() => refreshDevicesCompat("retry"), 2000);
                  }

                  return false;
                }
              };
              const removeDeviceCompat = async (deviceId) => {
                if (!deviceId) {
                  return;
                }

                try {
                  await fetch(apiEndpoint("/api/devices/remove"), {
                    method: "POST",
                    headers: {
                      "content-type": "application/json"
                    },
                    body: JSON.stringify(authPayload({ deviceId }))
                  });
                } finally {
                  await refreshDevicesCompat("remove");
                }
              };
              const startDeviceWatcherCompat = () => {
                const auth = readAuth();
                if (!auth.account || !auth.token || !auth.sessionToken || window.__ownDeskDeviceWatcher?.readyState <= WebSocket.OPEN) {
                  return;
                }

                let socket;
                try {
                  socket = new WebSocket(socketEndpoint("/ws/devices"));
                } catch {
                  return;
                }

                window.__ownDeskDeviceWatcher = socket;
                socket.addEventListener("open", () => {
                  if (window.__ownDeskDeviceWatcher === socket) {
                    socket.send(JSON.stringify(authPayload()));
                  }
                });
                socket.addEventListener("message", (event) => {
                  if (window.__ownDeskDeviceWatcher !== socket) {
                    return;
                  }

                  try {
                    const message = JSON.parse(event.data);
                    if (message.type === "deviceListChanged") {
                      refreshDevicesCompat("watcher");
                    }
                  } catch {
                    refreshDevicesCompat("watcher");
                  }
                });
                socket.addEventListener("close", () => {
                  if (window.__ownDeskDeviceWatcher === socket) {
                    window.__ownDeskDeviceWatcher = null;
                    window.setTimeout(startDeviceWatcherCompat, 3000);
                  }
                });
              };
              const formatBandwidth = (bytesPerSecond) => {
                if (bytesPerSecond >= 1024 * 1024) {
                  return `${(bytesPerSecond / 1024 / 1024).toFixed(1)}M/S`;
                }

                if (bytesPerSecond >= 1024) {
                  return `${Math.round(bytesPerSecond / 1024)}KB/S`;
            """;
}
