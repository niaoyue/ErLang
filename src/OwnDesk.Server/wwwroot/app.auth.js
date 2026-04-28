function setAuthMode(mode) {
  state.authMode = mode;
  const isRegister = mode === "register";
  elements.loginModeButton.classList.toggle("active", !isRegister);
  elements.registerModeButton.classList.toggle("active", isRegister);
  elements.confirmPasswordLabel.hidden = !isRegister;
  elements.confirmPasswordInput.required = isRegister;
  elements.passwordInput.autocomplete = isRegister ? "new-password" : "current-password";
  elements.authSubmitButton.textContent = isRegister ? "注册并登录" : "登录";
  setAuthPanelVisible(true);
  elements.authHint.classList.remove("error");
  elements.authHint.textContent = isRegister
    ? "在当前组织创建成员账号，之后可在客户端和控制台登录。"
    : "登录后显示本组织的设备。";
}

async function handleAuthSubmit(event) {
  event.preventDefault();
  const organization = saveCurrentOrganization(false);
  const organizationToken = organization.token;
  const username = elements.accountInput.value.trim();
  const password = elements.passwordInput.value;
  const confirmPassword = elements.confirmPasswordInput.value;

  if (!organizationToken || !username || !password) {
    setAuthHint("请先填写组织 Token、账号和密码。", true);
    return;
  }

  if (state.authMode === "register" && password !== confirmPassword) {
    setAuthHint("两次输入的密码不一致。", true);
    return;
  }

  const endpoint = state.authMode === "register" ? "/api/register" : "/api/login";
  elements.authSubmitButton.disabled = true;
  setAuthHint(state.authMode === "register" ? "正在注册..." : "正在登录...");

  try {
    const response = await fetch(apiUrl(endpoint), {
      method: "POST",
      headers: {
        "content-type": "application/json"
      },
      body: JSON.stringify({
        organizationToken,
        username,
        password
      })
    });

    if (!response.ok) {
      throw new Error(await authErrorMessage(response));
    }

    const session = await response.json();
    persistAuthenticatedSession(session, username, password);
    setAuthHint(state.authMode === "register" ? "注册成功，已登录。" : "登录成功。");
    startDeviceUpdates();
    await refreshDevicesAfterLogin();
  } catch (error) {
    clearAuthenticatedSession();
    stopDeviceUpdates();
    renderDevices([]);
    setAuthHint(error.message || "认证失败。", true);
  } finally {
    elements.authSubmitButton.disabled = false;
  }
}

async function authErrorMessage(response) {
  if (response.status === 401) {
    return "组织 Token、账号或密码不正确。";
  }

  if (response.status === 409) {
    return "账号已存在，请直接登录。";
  }

  try {
    const payload = await response.json();
    return payload.message || `HTTP ${response.status}`;
  } catch {
    return `HTTP ${response.status}`;
  }
}

function setAuthHint(message, isError = false) {
  elements.authHint.textContent = message;
  elements.authHint.classList.toggle("error", isError);
}

function setAuthPanelVisible(visible) {
  const loggedIn = hasAuthSession();
  elements.loginForm.classList.toggle("is-hidden", state.embeddedClient || !visible || loggedIn);
  elements.memberInfoPanel.classList.toggle("is-hidden", state.embeddedClient || !loggedIn);
}

function logoutMember() {
  stopDeviceUpdates();
  disconnectViewer(false);
  clearAuthenticatedSession();
  renderDevices([]);
  setAuthHint("已退出登录。");
}

async function refreshDevicesAfterLogin() {
  renderDeviceRefreshPending();
  await refreshDevices();
}

function renderDeviceRefreshPending() {
  elements.deviceList.innerHTML = "";
  const pending = document.createElement("div");
  pending.className = "empty-list";
  pending.textContent = "正在刷新设备...";
  elements.deviceList.appendChild(pending);
}

async function refreshDevices() {
  if (!state.account || !state.token || !state.sessionToken) {
    renderDevices([]);
    return;
  }

  try {
    const response = await fetch(apiUrl("/api/devices"), {
      method: "POST",
      headers: {
        "content-type": "application/json"
      },
      body: JSON.stringify(authPayload())
    });
    if (!response.ok) {
      throw new Error(response.status === 401 ? "认证失败" : `HTTP ${response.status}`);
    }

    state.devices = await response.json();
    renderDevices(state.devices);
    renderAuthState();
  } catch (error) {
    elements.serverStatus.textContent = error.message;
    if (error.message === "认证失败") {
      renderDevices([]);
      stopDeviceUpdates();
      clearAuthenticatedSession();
      return;
    }

    renderDevices(state.devices);
    setAuthHint(error.message || "设备刷新失败。", true);
  }
}

function renderDevices(devices) {
  elements.deviceList.innerHTML = "";

  if (!devices.length) {
    const empty = document.createElement("div");
    empty.className = "empty-list";
    empty.textContent = "没有设备";
    elements.deviceList.appendChild(empty);
    return;
  }

  for (const device of devices) {
    const connected = isConnectedDevice(device);
    const online = device.online || connected;
    const card = document.createElement("div");
    card.className = "device-card";

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
    connect.addEventListener("click", () => connectOrDisconnectDevice(device));

    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "danger";
    remove.textContent = "移除";
    remove.addEventListener("click", () => removeDevice(device.deviceId));

    actions.append(connect, remove);
    card.classList.toggle("is-offline", !online);
    card.append(meta, actions);
    elements.deviceList.appendChild(card);
  }
}

function isSelectedDevice(device) {
  return Boolean(device?.deviceId && state.selectedDevice?.deviceId === device.deviceId);
}

function isConnectedDevice(device) {
  return Boolean(isSelectedDevice(device) && state.socket?.readyState === WebSocket.OPEN);
}

function updateConnectedDevice(device) {
  if (!device?.deviceId) {
    return;
  }

  const nextDevice = {
    ...state.selectedDevice,
    ...device,
    online: true
  };
  state.selectedDevice = nextDevice;
  state.devices = state.devices.map((item) =>
    item.deviceId === nextDevice.deviceId ? { ...item, ...nextDevice, online: true } : item);

  if (!state.devices.some((item) => item.deviceId === nextDevice.deviceId)) {
    state.devices = [nextDevice, ...state.devices];
  }

  renderCurrentDevicesSoon();
}

function renderCurrentDevicesSoon() {
  window.setTimeout(() => renderDevices(state.devices), 80);
}

function connectOrDisconnectDevice(device) {
  if (isSelectedDevice(device)) {
    disconnectViewer();
    return;
  }

  connectViewer(device);
}

function startDirectForConnectedDevice(device) {
  if ((!device?.online && !isConnectedDevice(device)) || typeof startWebRtc !== "function") {
    return;
  }

  for (const delay of [100, 450, 1000, 2200, 4500, 8000]) {
    window.setTimeout(() => {
      if (isSelectedDevice(device) && state.socket?.readyState === WebSocket.OPEN && !state.webRtcPeer) {
        void startWebRtc().catch(() => {});
      }
    }, delay);
  }
}

async function removeDevice(deviceId) {
  if (!deviceId) {
    return;
  }

  if (state.selectedDevice?.deviceId === deviceId) {
    disconnectViewer();
  }

  await fetch(apiUrl("/api/devices/remove"), {
    method: "POST",
    headers: {
      "content-type": "application/json"
    },
    body: JSON.stringify({
      ...authPayload(),
      deviceId
    })
  });
  await refreshDevices();
}

function startDeviceUpdates() {
  stopDeviceUpdates();
  if (!state.account || !state.token || !state.sessionToken) {
    return;
  }

  const socket = new WebSocket(webSocketUrl("/ws/devices"));
  state.deviceUpdatesSocket = socket;

  socket.addEventListener("open", () => {
    if (state.deviceUpdatesSocket === socket) {
      state.deviceUpdatesReconnectAttempt = 0;
      socket.send(JSON.stringify(authPayload()));
      refreshDevices();
    }
  });
  socket.addEventListener("message", (event) => {
    if (state.deviceUpdatesSocket !== socket) {
      return;
    }

    const message = JSON.parse(event.data);
    if (message.type === "deviceListChanged") {
      refreshDevices();
    }
  });
  socket.addEventListener("close", () => {
    if (state.deviceUpdatesSocket === socket) {
      state.deviceUpdatesSocket = null;
      scheduleDeviceUpdatesReconnect();
    }
  });
  socket.addEventListener("error", () => {
    if (state.deviceUpdatesSocket === socket) {
      socket.close();
    }
  });
}

function stopDeviceUpdates() {
  if (state.deviceUpdatesReconnectTimer) {
    window.clearTimeout(state.deviceUpdatesReconnectTimer);
    state.deviceUpdatesReconnectTimer = 0;
  }

  const socket = state.deviceUpdatesSocket;
  state.deviceUpdatesSocket = null;
  if (socket && socket.readyState <= WebSocket.OPEN) {
    socket.close();
  }
}

function scheduleDeviceUpdatesReconnect() {
  if (!state.account || !state.token || !state.sessionToken || state.deviceUpdatesReconnectTimer) {
    return;
  }

  const delay = Math.min(30000, 1000 * (2 ** Math.min(5, state.deviceUpdatesReconnectAttempt)));
  state.deviceUpdatesReconnectAttempt += 1;
  state.deviceUpdatesReconnectTimer = window.setTimeout(() => {
    state.deviceUpdatesReconnectTimer = 0;
    startDeviceUpdates();
  }, delay);
}

function connectViewer(device) {
  disconnectViewer(false);

  state.selectedDevice = device;
  state.frameCount = 0;
  state.frameWidth = 0;
  state.frameHeight = 0;
  state.lastFrameRenderedAt = 0;
  state.connectionMode = "中继";
  resetBandwidthStats();
  state.remotePointer = null;
  state.mediaMode = "jpeg";
  state.localDebugMode = false;
  state.fitToScreen = true;
  state.zoomScale = 1;
  state.panMode = false;
  state.scrollMode = false;
  state.mousePan = null;
  state.touchPointers.clear();
  state.touchControl = null;
  state.touchGesture = null;
  state.touchPan = null;
  state.touchScroll = null;
  elements.panButton.classList.remove("active");
  elements.panButton.title = "拖动画面";
  elements.scrollButton.classList.remove("active");
  elements.scrollButton.title = "远端滚轮";
  elements.webrtcButton.classList.remove("active");
  elements.webrtcButton.title = "WebRTC 视频";
  elements.webrtcVideo.classList.remove("visible");
  elements.webrtcVideo.srcObject = null;
  elements.localDebugButton.classList.remove("active");
  elements.localDebugButton.title = "本机调试模式";
  elements.activeDevice.textContent = device.deviceName || device.deviceId;
  elements.frameStats.textContent = "等待画面";
  elements.connectionStatus.textContent = "连接中";
  elements.connectionStatus.classList.remove("offline");
  setRemoteControlsEnabled(true);
  renderDevices(state.devices);

  const socket = new WebSocket(webSocketUrl(`/ws/viewer?deviceId=${encodeURIComponent(device.deviceId)}`));
  socket.binaryType = "arraybuffer";
  state.socket = socket;

  socket.addEventListener("open", () => {
    sendSocketAuth(socket);
    elements.connectionStatus.textContent = "已连接";
    elements.canvas.focus();
    sendStreamQuality(elements.qualitySelect.value);
    startDirectForConnectedDevice(device);
    renderCurrentDevicesSoon();
  });

  socket.addEventListener("message", (event) => handleViewerMessage(socket, event.data));
  socket.addEventListener("close", () => {
    if (state.socket === socket) {
      markDisconnected();
    }
  });
  socket.addEventListener("error", () => {
    if (state.socket === socket) {
      elements.connectionStatus.textContent = "错误";
      elements.connectionStatus.classList.add("offline");
    }
  });
}

function disconnectViewer(updateUi = true) {
  stopWebRtc(false);
  releaseActiveModifiers(true);

  if (isPointerLocked()) {
    document.exitPointerLock();
  }

  if (state.keyboardOpen) {
    elements.keyboardInput.blur();
  }

  if (state.socket) {
    state.socket.close();
  }

  state.socket = null;

  if (updateUi) {
    markDisconnected();
  }
}

function markDisconnected() {
  if (typeof exitFullscreenForDisconnect === "function") {
    void exitFullscreenForDisconnect();
  }

  stopWebRtc(false);
  releaseActiveModifiers(false);
  state.selectedDevice = null;
  state.remotePointer = null;
  state.connectionMode = "中继";
  resetBandwidthStats();
  state.localDebugMode = false;
  state.mediaMode = "jpeg";
  state.panMode = false;
  state.scrollMode = false;
  state.mousePan = null;
  state.touchPointers.clear();
  state.touchControl = null;
  state.touchGesture = null;
  state.touchPan = null;
  state.touchScroll = null;
  elements.panButton.classList.remove("active");
  elements.panButton.title = "拖动画面";
  elements.scrollButton.classList.remove("active");
  elements.scrollButton.title = "远端滚轮";
  elements.webrtcButton.classList.remove("active");
  elements.webrtcButton.title = "WebRTC 视频";
  elements.webrtcVideo.classList.remove("visible");
  elements.localDebugButton.classList.remove("active");
  elements.localDebugButton.title = "本机调试模式";
  elements.activeDevice.textContent = "未连接";
  elements.frameStats.textContent = "0 帧";
  elements.connectionStatus.textContent = "离线";
  elements.connectionStatus.classList.add("offline");
  elements.canvas.classList.remove("pointer-locked");
  elements.remoteCursor.classList.remove("visible");
  setRemoteControlsEnabled(false);
  setKeyboardOpen(false);
  renderDevices(state.devices);
}

function setRemoteControlsEnabled(enabled) {
  elements.localDebugButton.disabled = !enabled;
  elements.pointerLockButton.disabled = !enabled;
  elements.disconnectButton.disabled = !enabled;
  elements.fullscreenButton.disabled = !enabled;
  elements.zoomOutButton.disabled = !enabled;
  elements.fitButton.disabled = !enabled;
  elements.zoomInButton.disabled = !enabled;
  elements.panButton.disabled = !enabled;
  elements.scrollButton.disabled = !enabled;
  elements.keyboardButton.disabled = !enabled;
  elements.qualitySelect.disabled = !enabled;
  elements.webrtcButton.disabled = !enabled;
}
