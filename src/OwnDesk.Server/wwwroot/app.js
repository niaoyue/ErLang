const state = {
  account: "",
  token: "",
  password: "",
  sessionToken: "",
  authMode: "login",
  socket: null,
  webRtcSocket: null,
  webRtcPeer: null,
  webRtcSessionId: "",
  webRtcVideoFrameCallback: 0,
  devices: [],
  frameWidth: 0,
  frameHeight: 0,
  frameCount: 0,
  lastFrameRenderedAt: 0,
  mediaMode: "jpeg",
  selectedDevice: null,
  lastMoveAt: 0,
  remotePointer: null,
  localDebugMode: false,
  suppressPointerInputUntil: 0,
  fitToScreen: true,
  zoomScale: 1,
  panMode: false,
  scrollMode: false,
  mousePan: null,
  touchPointers: new Map(),
  touchControl: null,
  touchGesture: null,
  touchPan: null,
  touchScroll: null,
  ignoreMouseUntil: 0,
  keyboardOpen: false,
  lastKeyboardCommandAt: 0,
  composingText: false,
  fullscreenToolbarVisible: true,
  activeModifiers: new Map()
};

const zoomLimits = {
  min: 0.05,
  max: 4
};

const decodeStatusStaleMs = 1500;

const elements = {
  loginForm: document.getElementById("loginForm"),
  loginModeButton: document.getElementById("loginModeButton"),
  registerModeButton: document.getElementById("registerModeButton"),
  organizationTokenInput: document.getElementById("organizationTokenInput"),
  accountInput: document.getElementById("accountInput"),
  passwordInput: document.getElementById("passwordInput"),
  confirmPasswordLabel: document.getElementById("confirmPasswordLabel"),
  confirmPasswordInput: document.getElementById("confirmPasswordInput"),
  authSubmitButton: document.getElementById("authSubmitButton"),
  authHint: document.getElementById("authHint"),
  refreshButton: document.getElementById("refreshButton"),
  localDebugButton: document.getElementById("localDebugButton"),
  pointerLockButton: document.getElementById("pointerLockButton"),
  disconnectButton: document.getElementById("disconnectButton"),
  fullscreenButton: document.getElementById("fullscreenButton"),
  zoomOutButton: document.getElementById("zoomOutButton"),
  fitButton: document.getElementById("fitButton"),
  zoomInButton: document.getElementById("zoomInButton"),
  panButton: document.getElementById("panButton"),
  scrollButton: document.getElementById("scrollButton"),
  keyboardButton: document.getElementById("keyboardButton"),
  qualitySelect: document.getElementById("qualitySelect"),
  webrtcButton: document.getElementById("webrtcButton"),
  toolbarToggleButton: document.getElementById("toolbarToggleButton"),
  keyboardPanel: document.getElementById("keyboardPanel"),
  keyboardInput: document.getElementById("keyboardInput"),
  deviceList: document.getElementById("deviceList"),
  serverStatus: document.getElementById("serverStatus"),
  connectionStatus: document.getElementById("connectionStatus"),
  activeDevice: document.getElementById("activeDevice"),
  frameStats: document.getElementById("frameStats"),
  workspace: document.getElementById("workspace"),
  screenShell: document.getElementById("screenShell"),
  screenContent: document.getElementById("screenContent"),
  canvas: document.getElementById("screenCanvas"),
  webrtcVideo: document.getElementById("webrtcVideo"),
  remoteCursor: document.getElementById("remoteCursor"),
  emptyScreen: document.getElementById("emptyScreen")
};

const context = elements.canvas.getContext("2d");
const mobileControlQueries = [
  window.matchMedia("(hover: none)"),
  window.matchMedia("(pointer: coarse)")
];

configureMobileControls();
for (const query of mobileControlQueries) {
  query.addEventListener?.("change", configureMobileControls);
}

elements.loginForm.addEventListener("submit", handleAuthSubmit);
elements.loginModeButton.addEventListener("click", () => setAuthMode("login"));
elements.registerModeButton.addEventListener("click", () => setAuthMode("register"));

elements.refreshButton.addEventListener("click", refreshDevices);
elements.localDebugButton.addEventListener("click", toggleLocalDebugMode);
elements.pointerLockButton.addEventListener("click", togglePointerLock);
elements.disconnectButton.addEventListener("click", () => disconnectViewer());
elements.fullscreenButton.addEventListener("click", toggleFullscreen);
elements.zoomOutButton.addEventListener("click", () => zoomBy(0.8));
elements.fitButton.addEventListener("click", fitScreen);
elements.zoomInButton.addEventListener("click", () => zoomBy(1.25));
elements.panButton.addEventListener("click", togglePanMode);
elements.scrollButton.addEventListener("click", toggleScrollMode);
elements.keyboardButton.addEventListener("pointerdown", (event) => event.preventDefault());
elements.keyboardButton.addEventListener("click", toggleKeyboard);
elements.qualitySelect.addEventListener("change", applyQualityProfile);
elements.webrtcButton.addEventListener("click", toggleWebRtc);
elements.toolbarToggleButton.addEventListener("pointerdown", (event) => event.preventDefault());
elements.toolbarToggleButton.addEventListener("click", toggleFullscreenToolbar);
elements.webrtcVideo.addEventListener("loadedmetadata", handleWebRtcVideoMetadata);
elements.webrtcVideo.addEventListener("loadeddata", activateWebRtcVideo);
elements.webrtcVideo.addEventListener("playing", activateWebRtcVideo);
elements.webrtcVideo.addEventListener("resize", handleWebRtcVideoMetadata);

document.addEventListener("pointerlockchange", handlePointerLockChange);
document.addEventListener("fullscreenchange", handleFullscreenChange);

elements.keyboardInput.addEventListener("focus", () => setKeyboardOpen(true));
elements.keyboardInput.addEventListener("blur", () => setKeyboardOpen(false));
elements.keyboardInput.addEventListener("beforeinput", handleKeyboardBeforeInput);
elements.keyboardInput.addEventListener("input", handleKeyboardInput);
elements.keyboardInput.addEventListener("keydown", handleKeyboardKeyDown);
elements.keyboardInput.addEventListener("compositionstart", () => {
  state.composingText = true;
});
elements.keyboardInput.addEventListener("compositionend", () => {
  state.composingText = false;
  flushKeyboardInput();
});

for (const button of elements.keyboardPanel.querySelectorAll("button[data-key-code]")) {
  button.addEventListener("pointerdown", (event) => event.preventDefault());
  button.addEventListener("click", () => {
    sendKeyTap(Number(button.dataset.keyCode), button.dataset.key ?? "");
    focusKeyboardSoon();
  });
}

for (const button of elements.keyboardPanel.querySelectorAll("button[data-scroll]")) {
  button.addEventListener("pointerdown", (event) => event.preventDefault());
  button.addEventListener("click", () => {
    sendWheel(Number(button.dataset.scroll));
    focusKeyboardSoon();
  });
}

for (const button of elements.keyboardPanel.querySelectorAll("button[data-pointer-click]")) {
  button.addEventListener("pointerdown", (event) => event.preventDefault());
  button.addEventListener("click", () => {
    sendPointerClick(button.dataset.pointerClick || "left");
    focusKeyboardSoon();
  });
}

for (const button of elements.keyboardPanel.querySelectorAll("button[data-modifier-key-code]")) {
  button.addEventListener("pointerdown", (event) => event.preventDefault());
  button.addEventListener("click", () => {
    toggleModifier(button);
    focusKeyboardSoon();
  });
}

window.addEventListener("beforeunload", () => releaseActiveModifiers(true));

elements.canvas.addEventListener("contextmenu", (event) => event.preventDefault());
elements.canvas.addEventListener("mousedown", (event) => {
  if (shouldIgnoreMouseEvent()) {
    return;
  }

  event.preventDefault();
  elements.canvas.focus();

  if (state.panMode) {
    startMousePan(event);
    return;
  }

  if (state.localDebugMode) {
    if (!isPointerLocked()) {
      updateRemotePointerFromEvent(event);
    }

    return;
  }

  sendPointerEvent("mouseDown", event, { useCurrentPointer: isPointerLocked() });
});
elements.canvas.addEventListener("mouseup", (event) => {
  if (shouldIgnoreMouseEvent()) {
    return;
  }

  event.preventDefault();

  if (state.mousePan) {
    stopMousePan();
    return;
  }

  if (state.localDebugMode) {
    if (!isPointerLocked()) {
      updateRemotePointerFromEvent(event);
    }

    sendPointerEvent("mouseClick", event, { useCurrentPointer: true });
    return;
  }

  sendPointerEvent("mouseUp", event, { useCurrentPointer: isPointerLocked() });
});
elements.canvas.addEventListener("mousemove", (event) => {
  if (shouldIgnoreMouseEvent()) {
    return;
  }

  if (state.mousePan) {
    event.preventDefault();
    applyMousePan(event);
    return;
  }

  if (isPointerLocked()) {
    event.preventDefault();

    if (state.localDebugMode && performance.now() < state.suppressPointerInputUntil) {
      return;
    }

    moveRemotePointerBy(event.movementX, event.movementY);

    if (state.localDebugMode) {
      return;
    }

    sendPointerEvent("mouseMove", event, { useCurrentPointer: true });
    return;
  }

  const now = performance.now();
  if (now - state.lastMoveAt < 30) {
    return;
  }

  state.lastMoveAt = now;

  if (state.localDebugMode) {
    updateRemotePointerFromEvent(event);
    return;
  }

  sendPointerEvent("mouseMove", event);
});
elements.canvas.addEventListener("wheel", (event) => {
  if (shouldIgnoreMouseEvent()) {
    return;
  }

  event.preventDefault();

  if (state.localDebugMode && !isPointerLocked()) {
    updateRemotePointerFromEvent(event);
  }

  sendPointerEvent("wheel", event, { deltaY: event.deltaY, useCurrentPointer: state.localDebugMode || isPointerLocked() });
}, { passive: false });

elements.canvas.addEventListener("pointerdown", handleTouchPointerDown);
elements.canvas.addEventListener("pointermove", handleTouchPointerMove);
elements.canvas.addEventListener("pointerup", handleTouchPointerUp);
elements.canvas.addEventListener("pointercancel", handleTouchPointerCancel);
window.addEventListener("mouseup", stopMousePan);

window.addEventListener("keydown", (event) => {
  if (!shouldCaptureKeyboard(event)) {
    return;
  }

  event.preventDefault();
  sendInput({
    event: "keyDown",
    key: event.key,
    keyCode: event.keyCode
  });
});

window.addEventListener("keyup", (event) => {
  if (!shouldCaptureKeyboard(event)) {
    return;
  }

  event.preventDefault();
  sendInput({
    event: "keyUp",
    key: event.key,
    keyCode: event.keyCode
  });
});

window.addEventListener("resize", () => {
  if (state.fitToScreen) {
    applyCanvasScale();
  }

  positionRemoteCursor();
});

function setAuthMode(mode) {
  state.authMode = mode;
  const isRegister = mode === "register";
  elements.loginModeButton.classList.toggle("active", !isRegister);
  elements.registerModeButton.classList.toggle("active", isRegister);
  elements.confirmPasswordLabel.hidden = !isRegister;
  elements.confirmPasswordInput.required = isRegister;
  elements.passwordInput.autocomplete = isRegister ? "new-password" : "current-password";
  elements.authSubmitButton.textContent = isRegister ? "注册并登录" : "登录";
  elements.authHint.classList.remove("error");
  elements.authHint.textContent = isRegister
    ? "使用组织 Token 创建成员账号，之后可在客户端和控制台登录。"
    : "登录后显示本组织的在线设备。";
}

async function handleAuthSubmit(event) {
  event.preventDefault();
  const organizationToken = elements.organizationTokenInput.value.trim();
  const username = elements.accountInput.value.trim();
  const password = elements.passwordInput.value;
  const confirmPassword = elements.confirmPasswordInput.value;

  if (!organizationToken || !username || !password) {
    setAuthHint("请填写组织 Token、账号和密码。", true);
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
    const response = await fetch(endpoint, {
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
    state.account = session.username || username;
    state.token = organizationToken;
    state.password = password;
    state.sessionToken = session.sessionToken || "";
    elements.serverStatus.textContent = "已登录";
    setAuthHint(state.authMode === "register" ? "注册成功，已登录。" : "登录成功。");
    await refreshDevices();
  } catch (error) {
    state.sessionToken = "";
    renderDevices([]);
    elements.serverStatus.textContent = "未登录";
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

async function refreshDevices() {
  if (!state.account || !state.token || !state.sessionToken) {
    renderDevices([]);
    return;
  }

  try {
    const response = await fetch("/api/devices", {
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
    elements.serverStatus.textContent = "已登录";
  } catch (error) {
    renderDevices([]);
    elements.serverStatus.textContent = error.message;
  }
}

function renderDevices(devices) {
  elements.deviceList.innerHTML = "";

  if (!devices.length) {
    const empty = document.createElement("div");
    empty.className = "empty-list";
    empty.textContent = "没有在线设备";
    elements.deviceList.appendChild(empty);
    return;
  }

  for (const device of devices) {
    const card = document.createElement("div");
    card.className = "device-card";

    const meta = document.createElement("div");
    const name = document.createElement("strong");
    name.textContent = device.deviceName || device.deviceId;
    const detail = document.createElement("div");
    detail.className = "subtle";
    detail.textContent = `${device.screenWidth || "-"}×${device.screenHeight || "-"} · ${device.deviceId}`;
    meta.append(name, detail);

    const connect = document.createElement("button");
    connect.type = "button";
    connect.textContent = "连接";
    connect.addEventListener("click", () => connectViewer(device));

    card.append(meta, connect);
    elements.deviceList.appendChild(card);
  }
}

function connectViewer(device) {
  disconnectViewer(false);

  state.selectedDevice = device;
  state.frameCount = 0;
  state.frameWidth = 0;
  state.frameHeight = 0;
  state.lastFrameRenderedAt = 0;
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

  const scheme = location.protocol === "https:" ? "wss" : "ws";
  const socket = new WebSocket(`${scheme}://${location.host}/ws/viewer?deviceId=${encodeURIComponent(device.deviceId)}`);
  socket.binaryType = "arraybuffer";
  state.socket = socket;

  socket.addEventListener("open", () => {
    sendSocketAuth(socket);
    elements.connectionStatus.textContent = "已连接";
    elements.canvas.focus();
    sendStreamQuality(elements.qualitySelect.value);
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
  stopWebRtc(false);
  releaseActiveModifiers(false);
  state.remotePointer = null;
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
  elements.connectionStatus.textContent = "离线";
  elements.connectionStatus.classList.add("offline");
  elements.canvas.classList.remove("pointer-locked");
  elements.remoteCursor.classList.remove("visible");
  setRemoteControlsEnabled(false);
  setKeyboardOpen(false);
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

function handleViewerMessage(socket, data) {
  if (state.socket !== socket) {
    return;
  }

  if (data instanceof ArrayBuffer) {
    drawBinaryFrame(data);
    return;
  }

  if (data instanceof Blob) {
    data.arrayBuffer().then((buffer) => {
      if (state.socket === socket) {
        drawBinaryFrame(buffer);
      }
    });
    return;
  }

  const text = data;
  const message = JSON.parse(text);
  if (message.type === "frame") {
    drawFrame(message);
  } else if (message.type === "device" && message.device) {
    elements.activeDevice.textContent = message.device.deviceName || message.device.deviceId;
  } else if (message.type === "error") {
    elements.connectionStatus.textContent = message.message || "错误";
    elements.connectionStatus.classList.add("offline");
  }
}

async function toggleWebRtc() {
  if (state.webRtcPeer || state.webRtcSocket) {
    stopWebRtc();
    return;
  }

  await startWebRtc();
}

async function startWebRtc() {
  if (!state.selectedDevice || !state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  if (!window.RTCPeerConnection) {
    elements.connectionStatus.textContent = "不支持 WebRTC";
    elements.connectionStatus.classList.add("offline");
    return;
  }

  const scheme = location.protocol === "https:" ? "wss" : "ws";
  const sessionId = createSessionId();
  const peer = new RTCPeerConnection({ iceServers: [] });
  const socket = new WebSocket(`${scheme}://${location.host}/ws/webrtc/viewer?deviceId=${encodeURIComponent(state.selectedDevice.deviceId)}&sessionId=${encodeURIComponent(sessionId)}`);

  state.webRtcPeer = peer;
  state.webRtcSocket = socket;
  state.webRtcSessionId = sessionId;
  elements.webrtcButton.classList.add("active");
  elements.webrtcButton.title = "关闭 WebRTC 视频";
  elements.connectionStatus.textContent = "WebRTC 连接中";
  elements.connectionStatus.classList.remove("offline");

  peer.addTransceiver("video", { direction: "recvonly" });
  peer.ontrack = (event) => {
    const stream = event.streams[0] || new MediaStream([event.track]);
    elements.webrtcVideo.srcObject = stream;
    prepareWebRtcVideo();
    elements.webrtcVideo.play().catch(() => {
      elements.connectionStatus.textContent = "WebRTC 等待播放";
    });
  };
  peer.onicecandidate = (event) => {
    if (event.candidate) {
      sendWebRtcSignal({
        type: "webrtcIce",
        candidate: event.candidate.toJSON()
      });
    }
  };
  peer.onconnectionstatechange = () => {
    if (state.webRtcPeer !== peer) {
      return;
    }

    if (peer.connectionState === "connected") {
      elements.connectionStatus.textContent = "WebRTC";
      elements.connectionStatus.classList.remove("offline");
    } else if (peer.connectionState === "disconnected") {
      elements.connectionStatus.textContent = "WebRTC 重连中";
      elements.connectionStatus.classList.remove("offline");
    } else if (peer.connectionState === "failed" || peer.connectionState === "closed") {
      stopWebRtc(true);
    }
  };

  socket.addEventListener("open", async () => {
    if (state.webRtcSocket !== socket || state.webRtcPeer !== peer) {
      return;
    }

    sendSocketAuth(socket);
    const offer = await peer.createOffer();
    await peer.setLocalDescription(offer);
    sendWebRtcSignal({
      type: "webrtcOffer",
      sdpType: offer.type,
      sdp: offer.sdp
    });
  });

  socket.addEventListener("message", (event) => handleWebRtcSignal(socket, peer, event.data));
  socket.addEventListener("close", () => {
    if (state.webRtcSocket === socket) {
      stopWebRtc(true);
    }
  });
  socket.addEventListener("error", () => {
    if (state.webRtcSocket === socket) {
      elements.connectionStatus.textContent = "WebRTC 错误";
      stopWebRtc(true);
    }
  });
}

async function handleWebRtcSignal(socket, peer, data) {
  if (state.webRtcSocket !== socket || state.webRtcPeer !== peer) {
    return;
  }

  const message = JSON.parse(data);
  if (message.type === "webrtcAnswer" && message.sdp) {
    await peer.setRemoteDescription({
      type: message.sdpType || "answer",
      sdp: message.sdp
    });
  } else if (message.type === "webrtcIce" && message.candidate) {
    await peer.addIceCandidate(message.candidate);
  } else if (message.type === "webrtcError") {
    elements.connectionStatus.textContent = message.message || "WebRTC 错误";
    stopWebRtc(true);
  }
}

function sendWebRtcSignal(payload) {
  if (!state.webRtcSocket || state.webRtcSocket.readyState !== WebSocket.OPEN) {
    return;
  }

  state.webRtcSocket.send(JSON.stringify({
    sessionId: state.webRtcSessionId,
    deviceId: state.selectedDevice?.deviceId || "",
    ...payload
  }));
}

function prepareWebRtcVideo() {
  elements.canvas.classList.add("visible");
  elements.emptyScreen.classList.add("hidden");

  if (!state.frameWidth || !state.frameHeight) {
    state.frameWidth = 640;
    state.frameHeight = 480;
    elements.canvas.width = state.frameWidth;
    elements.canvas.height = state.frameHeight;
    applyCanvasScale();
  }
}

function handleWebRtcVideoMetadata() {
  if (!state.webRtcPeer) {
    return;
  }

  syncWebRtcVideoSize();
}

function activateWebRtcVideo() {
  if (!state.webRtcPeer || !elements.webrtcVideo.srcObject) {
    return;
  }

  state.mediaMode = "webrtc";
  elements.webrtcVideo.classList.add("visible");
  elements.canvas.classList.add("visible");
  elements.emptyScreen.classList.add("hidden");
  elements.connectionStatus.textContent = "WebRTC";
  elements.connectionStatus.classList.remove("offline");
  syncWebRtcVideoSize();
  startWebRtcFrameMonitor();
}

function syncWebRtcVideoSize(frameWidth = 0, frameHeight = 0) {
  const width = frameWidth || elements.webrtcVideo.videoWidth || state.frameWidth || 640;
  const height = frameHeight || elements.webrtcVideo.videoHeight || state.frameHeight || 480;
  state.frameWidth = width;
  state.frameHeight = height;
  elements.canvas.width = width;
  elements.canvas.height = height;
  context.clearRect(0, 0, width, height);
  elements.canvas.classList.add("visible");
  elements.emptyScreen.classList.add("hidden");
  ensureRemotePointer();
  applyCanvasScale();
}

function startWebRtcFrameMonitor() {
  if (state.webRtcVideoFrameCallback || typeof elements.webrtcVideo.requestVideoFrameCallback !== "function") {
    return;
  }

  const callbackId = elements.webrtcVideo.requestVideoFrameCallback((_now, metadata) => {
    if (state.webRtcVideoFrameCallback === callbackId) {
      state.webRtcVideoFrameCallback = 0;
    }

    if (!state.webRtcPeer || !elements.webrtcVideo.srcObject) {
      return;
    }

    syncWebRtcVideoSize(metadata?.width || 0, metadata?.height || 0);
    startWebRtcFrameMonitor();
  });

  state.webRtcVideoFrameCallback = callbackId;
}

function stopWebRtcFrameMonitor() {
  if (!state.webRtcVideoFrameCallback) {
    return;
  }

  if (typeof elements.webrtcVideo.cancelVideoFrameCallback === "function") {
    elements.webrtcVideo.cancelVideoFrameCallback(state.webRtcVideoFrameCallback);
  }

  state.webRtcVideoFrameCallback = 0;
}

function stopWebRtc(updateUi = true) {
  const peer = state.webRtcPeer;
  const socket = state.webRtcSocket;
  const stream = elements.webrtcVideo.srcObject;

  stopWebRtcFrameMonitor();
  state.webRtcPeer = null;
  state.webRtcSocket = null;
  state.webRtcSessionId = "";
  state.mediaMode = "jpeg";

  if (peer) {
    peer.ontrack = null;
    peer.onicecandidate = null;
    peer.onconnectionstatechange = null;
    peer.close();
  }

  if (socket) {
    socket.close();
  }

  if (stream) {
    for (const track of stream.getTracks()) {
      track.stop();
    }
  }

  elements.webrtcVideo.srcObject = null;
  elements.webrtcVideo.classList.remove("visible");
  elements.webrtcButton.classList.remove("active");
  elements.webrtcButton.title = "WebRTC 视频";

  if (updateUi && state.socket && state.socket.readyState === WebSocket.OPEN) {
    elements.connectionStatus.textContent = "已连接";
    elements.connectionStatus.classList.remove("offline");
  }
}

function createSessionId() {
  if (crypto.randomUUID) {
    return crypto.randomUUID();
  }

  return `viewer-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

async function drawFrame(message) {
  showDecodingStatusIfStale();

  const image = new Image();
  image.onload = () => {
    if (state.mediaMode === "webrtc") {
      return;
    }

    drawDecodedFrame(image, message.width, message.height);
  };
  image.onerror = () => {
    elements.frameStats.textContent = "画面解码失败";
  };
  image.src = `data:image/jpeg;base64,${message.imageBase64}`;
}

async function drawBinaryFrame(payload) {
  const decoded = decodeBinaryFrame(payload);
  if (!decoded) {
    elements.frameStats.textContent = "画面帧格式错误";
    return;
  }

  if (state.mediaMode === "webrtc") {
    return;
  }

  showDecodingStatusIfStale();
  const imageBlob = new Blob([decoded.imageBytes], { type: "image/jpeg" });

  if ("createImageBitmap" in window) {
    try {
      const bitmap = await createImageBitmap(imageBlob);
      if (state.mediaMode !== "webrtc") {
        drawDecodedFrame(bitmap, decoded.header.width, decoded.header.height);
      }

      bitmap.close?.();
      return;
    } catch {
      // Some mobile browsers expose createImageBitmap but fail for JPEG blobs.
    }
  }

  const image = new Image();
  const url = URL.createObjectURL(imageBlob);
  image.onload = () => {
    URL.revokeObjectURL(url);
    if (state.mediaMode === "webrtc") {
      return;
    }

    drawDecodedFrame(image, decoded.header.width, decoded.header.height);
  };
  image.onerror = () => {
    URL.revokeObjectURL(url);
    elements.frameStats.textContent = "画面解码失败";
  };
  image.src = url;
}

function drawDecodedFrame(source, width, height) {
  state.frameWidth = width;
  state.frameHeight = height;
  state.frameCount += 1;
  state.lastFrameRenderedAt = performance.now();

  elements.canvas.width = width;
  elements.canvas.height = height;
  context.drawImage(source, 0, 0, width, height);
  elements.canvas.classList.add("visible");
  elements.emptyScreen.classList.add("hidden");
  ensureRemotePointer();
  applyCanvasScale();
}

function showDecodingStatusIfStale() {
  if (state.frameCount === 0 || performance.now() - state.lastFrameRenderedAt > decodeStatusStaleMs) {
    elements.frameStats.textContent = "收到画面，正在解码";
  }
}

function decodeBinaryFrame(payload) {
  if (payload.byteLength < 8) {
    return null;
  }

  const view = new DataView(payload);
  if (view.getUint32(0, true) !== 0x3146444f) {
    return null;
  }

  const headerLength = view.getInt32(4, true);
  if (headerLength <= 0 || 8 + headerLength >= payload.byteLength) {
    return null;
  }

  const headerBytes = new Uint8Array(payload, 8, headerLength);
  const header = JSON.parse(new TextDecoder().decode(headerBytes));
  return {
    header,
    imageBytes: payload.slice(8 + headerLength)
  };
}

function applyCanvasScale() {
  if (!state.frameWidth || !state.frameHeight) {
    return;
  }

  const scale = getCurrentScale();
  const width = Math.max(1, Math.round(state.frameWidth * scale));
  const height = Math.max(1, Math.round(state.frameHeight * scale));

  elements.canvas.style.width = `${width}px`;
  elements.canvas.style.height = `${height}px`;
  elements.webrtcVideo.style.width = `${width}px`;
  elements.webrtcVideo.style.height = `${height}px`;
  elements.screenContent.style.width = `${width}px`;
  elements.screenContent.style.height = `${height}px`;
  updateFrameStats(scale);
  positionRemoteCursor();
}

function updateFrameStats(scale) {
  const mode = state.mediaMode === "webrtc" ? "WebRTC" : `${state.frameCount} 帧`;
  elements.frameStats.textContent = `${state.frameWidth}×${state.frameHeight} · ${mode} · ${Math.round(scale * 100)}%`;
}

function getCurrentScale() {
  return state.fitToScreen ? getFitScale() : state.zoomScale;
}

function getFitScale() {
  if (!state.frameWidth || !state.frameHeight) {
    return 1;
  }

  const width = Math.max(1, elements.screenShell.clientWidth - 24);
  const height = Math.max(1, elements.screenShell.clientHeight - 24);
  return clamp(Math.min(width / state.frameWidth, height / state.frameHeight), zoomLimits.min, zoomLimits.max);
}

function zoomBy(multiplier) {
  if (!state.frameWidth || !state.frameHeight) {
    return;
  }

  const anchor = getViewportAnchor();
  state.fitToScreen = false;
  state.zoomScale = clamp(getCurrentScale() * multiplier, zoomLimits.min, zoomLimits.max);
  applyCanvasScale();
  restoreViewportAnchor(anchor);
}

function fitScreen() {
  state.fitToScreen = true;
  applyCanvasScale();
  elements.screenShell.scrollLeft = 0;
  elements.screenShell.scrollTop = 0;
}

function getViewportAnchor() {
  const shellRect = elements.screenShell.getBoundingClientRect();
  const scale = getCurrentScale();
  return {
    remoteX: (elements.screenShell.scrollLeft + elements.screenShell.clientWidth / 2) / Math.max(scale, zoomLimits.min),
    remoteY: (elements.screenShell.scrollTop + elements.screenShell.clientHeight / 2) / Math.max(scale, zoomLimits.min),
    centerX: shellRect.left + elements.screenShell.clientWidth / 2,
    centerY: shellRect.top + elements.screenShell.clientHeight / 2
  };
}

function restoreViewportAnchor(anchor) {
  const scale = getCurrentScale();
  elements.screenShell.scrollLeft = Math.max(0, anchor.remoteX * scale - elements.screenShell.clientWidth / 2);
  elements.screenShell.scrollTop = Math.max(0, anchor.remoteY * scale - elements.screenShell.clientHeight / 2);
}

function sendPointerEvent(name, event, extra = {}) {
  if (!state.frameWidth || !state.frameHeight) {
    return;
  }

  const point = extra.useCurrentPointer ? ensureRemotePointer() : toRemotePoint(event);
  state.remotePointer = point;
  positionRemoteCursor();

  sendInput({
    event: name,
    x: point.x,
    y: point.y,
    button: buttonName(event.button),
    ...withoutLocalOptions(extra)
  });
}

function handleTouchPointerDown(event) {
  if (!isTouchLikePointer(event) || !state.frameWidth || !state.frameHeight) {
    return;
  }

  event.preventDefault();
  state.ignoreMouseUntil = performance.now() + 900;
  elements.canvas.focus();
  elements.canvas.setPointerCapture(event.pointerId);
  state.touchPointers.set(event.pointerId, touchPoint(event));

  if (state.touchPointers.size >= 2) {
    clearTouchDragTimer(state.touchControl);
    state.touchControl = null;
    state.touchPan = null;
    state.touchScroll = null;
    beginTouchGesture();
    return;
  }

  if (state.scrollMode) {
    updateRemotePointerFromEvent(event);
    state.touchScroll = {
      id: event.pointerId,
      lastY: event.clientY,
      remainder: 0
    };
    return;
  }

  if (state.panMode) {
    state.touchPan = {
      id: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      startScrollLeft: elements.screenShell.scrollLeft,
      startScrollTop: elements.screenShell.scrollTop
    };
    return;
  }

  state.touchControl = {
    id: event.pointerId,
    startX: event.clientX,
    startY: event.clientY,
    startAt: performance.now(),
    moved: false,
    dragging: false,
    dragTimer: window.setTimeout(() => beginTouchDrag(event.pointerId), 480)
  };
  updateRemotePointerFromEvent(event);
  sendTouchMouseMove();
}

function handleTouchPointerMove(event) {
  if (!isTouchLikePointer(event) || !state.touchPointers.has(event.pointerId)) {
    return;
  }

  event.preventDefault();
  state.ignoreMouseUntil = performance.now() + 900;
  state.touchPointers.set(event.pointerId, touchPoint(event));

  if (state.touchPointers.size >= 2) {
    applyTouchGesture();
    return;
  }

  if (state.scrollMode && state.touchScroll?.id === event.pointerId) {
    applyTouchScroll(event);
    return;
  }

  if (state.panMode && state.touchPan?.id === event.pointerId) {
    applyTouchPan(event);
    return;
  }

  if (!state.touchControl || state.touchControl.id !== event.pointerId) {
    return;
  }

  if (Math.abs(event.clientX - state.touchControl.startX) > 8 || Math.abs(event.clientY - state.touchControl.startY) > 8) {
    state.touchControl.moved = true;
  }

  updateRemotePointerFromEvent(event);
  sendTouchMouseMove();
}

function handleTouchPointerUp(event) {
  if (!isTouchLikePointer(event) || !state.touchPointers.has(event.pointerId)) {
    return;
  }

  event.preventDefault();
  state.ignoreMouseUntil = performance.now() + 900;

  const wasGesture = state.touchGesture !== null || state.touchPointers.size >= 2;
  const wasPan = state.touchPan?.id === event.pointerId;
  const wasScroll = state.touchScroll?.id === event.pointerId;
  const wasDragging = state.touchControl?.id === event.pointerId && state.touchControl.dragging;
  clearTouchDragTimer(state.touchControl);
  state.touchPointers.delete(event.pointerId);

  if (!wasGesture && !wasPan && !wasScroll && state.touchControl?.id === event.pointerId) {
    updateRemotePointerFromEvent(event);
    if (wasDragging) {
      sendInput({
        event: "mouseUp",
        x: state.remotePointer.x,
        y: state.remotePointer.y,
        button: "left"
      });
    } else {
      const elapsed = performance.now() - state.touchControl.startAt;
      if (!state.touchControl.moved && elapsed < 650) {
        sendInput({
          event: "mouseClick",
          x: state.remotePointer.x,
          y: state.remotePointer.y,
          button: "left"
        });
      }
    }
  }

  releaseTouchPointer(event);

  if (state.touchPointers.size >= 2) {
    beginTouchGesture();
  } else {
    state.touchGesture = null;
    state.touchControl = null;
    state.touchPan = null;
    state.touchScroll = null;
  }
}

function handleTouchPointerCancel(event) {
  if (!isTouchLikePointer(event) || !state.touchPointers.has(event.pointerId)) {
    return;
  }

  if (state.touchControl?.id === event.pointerId && state.touchControl.dragging && state.remotePointer) {
    sendInput({
      event: "mouseUp",
      x: state.remotePointer.x,
      y: state.remotePointer.y,
      button: "left"
    });
  }

  clearTouchDragTimer(state.touchControl);
  state.touchPointers.delete(event.pointerId);
  releaseTouchPointer(event);
  state.touchGesture = null;
  state.touchControl = null;
  state.touchPan = null;
  state.touchScroll = null;
}

function beginTouchDrag(pointerId) {
  if (!state.touchControl || state.touchControl.id !== pointerId || state.touchControl.dragging || !state.remotePointer) {
    return;
  }

  state.touchControl.dragging = true;
  state.touchControl.moved = true;
  sendInput({
    event: "mouseDown",
    x: state.remotePointer.x,
    y: state.remotePointer.y,
    button: "left"
  });
}

function clearTouchDragTimer(control) {
  if (control?.dragTimer) {
    window.clearTimeout(control.dragTimer);
    control.dragTimer = 0;
  }
}

function beginTouchGesture() {
  const points = firstTwoTouchPoints();
  if (!points) {
    return;
  }

  const center = midpoint(points.a, points.b);
  const distance = Math.max(1, pointDistance(points.a, points.b));
  const scale = getCurrentScale();
  const shellRect = elements.screenShell.getBoundingClientRect();

  state.touchGesture = {
    startDistance: distance,
    startScale: scale,
    remoteX: (elements.screenShell.scrollLeft + center.x - shellRect.left) / Math.max(scale, zoomLimits.min),
    remoteY: (elements.screenShell.scrollTop + center.y - shellRect.top) / Math.max(scale, zoomLimits.min)
  };
}

function applyTouchGesture() {
  const points = firstTwoTouchPoints();
  if (!points || !state.touchGesture) {
    return;
  }

  const center = midpoint(points.a, points.b);
  const distance = Math.max(1, pointDistance(points.a, points.b));
  const nextScale = clamp(
    state.touchGesture.startScale * (distance / state.touchGesture.startDistance),
    zoomLimits.min,
    zoomLimits.max);

  state.fitToScreen = false;
  state.zoomScale = nextScale;
  applyCanvasScale();

  const shellRect = elements.screenShell.getBoundingClientRect();
  elements.screenShell.scrollLeft = Math.max(0, state.touchGesture.remoteX * nextScale - (center.x - shellRect.left));
  elements.screenShell.scrollTop = Math.max(0, state.touchGesture.remoteY * nextScale - (center.y - shellRect.top));
}

function sendTouchMouseMove() {
  if (!state.remotePointer) {
    return;
  }

  sendInput({
    event: "mouseMove",
    x: state.remotePointer.x,
    y: state.remotePointer.y,
    button: "left"
  });
}

function startMousePan(event) {
  state.mousePan = {
    startX: event.clientX,
    startY: event.clientY,
    startScrollLeft: elements.screenShell.scrollLeft,
    startScrollTop: elements.screenShell.scrollTop
  };
}

function applyMousePan(event) {
  if (!state.mousePan) {
    return;
  }

  elements.screenShell.scrollLeft = state.mousePan.startScrollLeft - (event.clientX - state.mousePan.startX);
  elements.screenShell.scrollTop = state.mousePan.startScrollTop - (event.clientY - state.mousePan.startY);
}

function stopMousePan() {
  state.mousePan = null;
}

function applyTouchPan(event) {
  if (!state.touchPan) {
    return;
  }

  elements.screenShell.scrollLeft = state.touchPan.startScrollLeft - (event.clientX - state.touchPan.startX);
  elements.screenShell.scrollTop = state.touchPan.startScrollTop - (event.clientY - state.touchPan.startY);
}

function applyTouchScroll(event) {
  if (!state.touchScroll) {
    return;
  }

  const deltaY = event.clientY - state.touchScroll.lastY;
  state.touchScroll.lastY = event.clientY;
  state.touchScroll.remainder += deltaY;

  if (Math.abs(state.touchScroll.remainder) < 6) {
    return;
  }

  const wheelDelta = state.touchScroll.remainder * 4;
  state.touchScroll.remainder = 0;
  sendWheel(wheelDelta);
}

function touchPoint(event) {
  return {
    id: event.pointerId,
    x: event.clientX,
    y: event.clientY
  };
}

function firstTwoTouchPoints() {
  const points = Array.from(state.touchPointers.values());
  if (points.length < 2) {
    return null;
  }

  return {
    a: points[0],
    b: points[1]
  };
}

function midpoint(a, b) {
  return {
    x: (a.x + b.x) / 2,
    y: (a.y + b.y) / 2
  };
}

function pointDistance(a, b) {
  return Math.hypot(a.x - b.x, a.y - b.y);
}

function isTouchLikePointer(event) {
  return event.pointerType && event.pointerType !== "mouse";
}

function releaseTouchPointer(event) {
  if (elements.canvas.hasPointerCapture(event.pointerId)) {
    elements.canvas.releasePointerCapture(event.pointerId);
  }
}

function sendInput(payload) {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  if (state.localDebugMode && (payload.event === "mouseClick" || payload.event === "wheel")) {
    state.suppressPointerInputUntil = performance.now() + 250;
  }

  state.socket.send(JSON.stringify({
    type: "input",
    ...payload
  }));
}

function applyQualityProfile() {
  sendStreamQuality(elements.qualitySelect.value);
}

function sendStreamQuality(profile) {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  state.socket.send(JSON.stringify({
    type: "streamQuality",
    profile
  }));
}

function sendWheel(deltaY) {
  const point = ensureRemotePointer();
  sendInput({
    event: "wheel",
    x: point.x,
    y: point.y,
    button: "left",
    deltaY
  });
}

function sendPointerClick(button) {
  const point = ensureRemotePointer();
  sendInput({
    event: "mouseClick",
    x: point.x,
    y: point.y,
    button
  });
}

function toRemotePoint(event) {
  const rect = elements.canvas.getBoundingClientRect();
  const x = (event.clientX - rect.left) * elements.canvas.width / rect.width;
  const y = (event.clientY - rect.top) * elements.canvas.height / rect.height;

  return {
    x: Math.max(0, Math.min(elements.canvas.width - 1, x)),
    y: Math.max(0, Math.min(elements.canvas.height - 1, y))
  };
}

function updateRemotePointerFromEvent(event) {
  state.remotePointer = toRemotePoint(event);
  positionRemoteCursor();
  return state.remotePointer;
}

function ensureRemotePointer() {
  if (!state.remotePointer) {
    state.remotePointer = {
      x: Math.max(0, Math.floor(elements.canvas.width / 2)),
      y: Math.max(0, Math.floor(elements.canvas.height / 2))
    };
  }

  return state.remotePointer;
}

function moveRemotePointerBy(deltaX, deltaY) {
  const pointer = ensureRemotePointer();
  const rect = elements.canvas.getBoundingClientRect();
  const scaleX = rect.width > 0 ? elements.canvas.width / rect.width : 1;
  const scaleY = rect.height > 0 ? elements.canvas.height / rect.height : 1;

  state.remotePointer = {
    x: Math.max(0, Math.min(elements.canvas.width - 1, pointer.x + deltaX * scaleX)),
    y: Math.max(0, Math.min(elements.canvas.height - 1, pointer.y + deltaY * scaleY))
  };

  positionRemoteCursor();
}

function positionRemoteCursor() {
  if (!state.remotePointer || !state.frameWidth || !state.frameHeight || !elements.canvas.classList.contains("visible")) {
    elements.remoteCursor.classList.remove("visible");
    return;
  }

  const contentRect = elements.screenContent.getBoundingClientRect();
  const canvasRect = elements.canvas.getBoundingClientRect();
  const x = canvasRect.left - contentRect.left + state.remotePointer.x * canvasRect.width / elements.canvas.width;
  const y = canvasRect.top - contentRect.top + state.remotePointer.y * canvasRect.height / elements.canvas.height;

  elements.remoteCursor.style.left = `${x}px`;
  elements.remoteCursor.style.top = `${y}px`;
  elements.remoteCursor.classList.add("visible");
}

async function toggleFullscreen() {
  if (document.fullscreenElement) {
    await document.exitFullscreen();
    return;
  }

  if (elements.workspace.requestFullscreen) {
    await elements.workspace.requestFullscreen();
  }
}

function handleFullscreenChange() {
  const fullscreen = document.fullscreenElement === elements.workspace;
  elements.fullscreenButton.classList.toggle("active", fullscreen);
  elements.fullscreenButton.textContent = fullscreen ? "□" : "⛶";
  setFullscreenToolbarVisible(!fullscreen || !shouldAutoHideFullscreenToolbar());
  setTimeout(() => {
    if (state.fitToScreen) {
      applyCanvasScale();
    }
  }, 80);
}

function toggleFullscreenToolbar() {
  if (document.fullscreenElement !== elements.workspace) {
    return;
  }

  setFullscreenToolbarVisible(!state.fullscreenToolbarVisible);
}

function setFullscreenToolbarVisible(visible) {
  state.fullscreenToolbarVisible = visible;
  elements.workspace.classList.toggle("toolbar-hidden", !visible);
  elements.toolbarToggleButton.textContent = visible ? "×" : "☰";
  elements.toolbarToggleButton.title = visible ? "隐藏工具栏" : "显示工具栏";
  elements.toolbarToggleButton.setAttribute("aria-label", visible ? "隐藏工具栏" : "显示工具栏");
}

function shouldAutoHideFullscreenToolbar() {
  return shouldUseMobileControls();
}

function configureMobileControls() {
  document.body.classList.toggle("mobile-controls", shouldUseMobileControls());
}

function shouldUseMobileControls() {
  return navigator.maxTouchPoints > 0 || mobileControlQueries.some((query) => query.matches);
}

function toggleLocalDebugMode() {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  state.localDebugMode = !state.localDebugMode;
  elements.localDebugButton.classList.toggle("active", state.localDebugMode);
  elements.localDebugButton.title = state.localDebugMode ? "退出本机调试模式" : "本机调试模式";

  if (state.localDebugMode) {
    elements.canvas.focus();
    ensureRemotePointer();

    if (!isPointerLocked()) {
      elements.canvas.requestPointerLock();
    }
  }
}

function togglePointerLock() {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  if (isPointerLocked()) {
    document.exitPointerLock();
    return;
  }

  elements.canvas.focus();
  ensureRemotePointer();
  elements.canvas.requestPointerLock();
}

function handlePointerLockChange() {
  const locked = isPointerLocked();
  elements.canvas.classList.toggle("pointer-locked", locked);
  elements.pointerLockButton.classList.toggle("active", locked);
  elements.pointerLockButton.title = locked ? "释放鼠标" : "锁定鼠标";
  elements.pointerLockButton.textContent = locked ? "⊙" : "⌖";
}

function isPointerLocked() {
  return document.pointerLockElement === elements.canvas;
}

function togglePanMode() {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  state.panMode = !state.panMode;
  state.mousePan = null;
  state.touchPan = null;
  elements.panButton.classList.toggle("active", state.panMode);
  elements.panButton.title = state.panMode ? "退出拖动画面" : "拖动画面";

  if (state.panMode && state.scrollMode) {
    state.scrollMode = false;
    state.touchScroll = null;
    elements.scrollButton.classList.remove("active");
    elements.scrollButton.title = "远端滚轮";
  }

  if (state.panMode && isPointerLocked()) {
    document.exitPointerLock();
  }
}

function toggleScrollMode() {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  state.scrollMode = !state.scrollMode;
  state.touchScroll = null;
  elements.scrollButton.classList.toggle("active", state.scrollMode);
  elements.scrollButton.title = state.scrollMode ? "退出远端滚轮" : "远端滚轮";

  if (state.scrollMode && state.panMode) {
    state.panMode = false;
    state.mousePan = null;
    state.touchPan = null;
    elements.panButton.classList.remove("active");
    elements.panButton.title = "拖动画面";
  }

  if (state.scrollMode && isPointerLocked()) {
    document.exitPointerLock();
  }
}

function toggleKeyboard() {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  if (state.keyboardOpen) {
    elements.keyboardInput.blur();
    setKeyboardOpen(false);
    return;
  }

  setKeyboardOpen(true);
  elements.keyboardInput.value = "";
  elements.keyboardInput.focus({ preventScroll: true });
}

function setKeyboardOpen(open) {
  state.keyboardOpen = open;
  elements.keyboardButton.textContent = open ? "⌨" : "⌨";
  elements.keyboardPanel.hidden = !open;
  updateKeyboardButtonState();
}

function handleKeyboardBeforeInput(event) {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  if (event.inputType === "deleteContentBackward") {
    event.preventDefault();
    state.lastKeyboardCommandAt = performance.now();
    sendKeyTap(8, "Backspace");
  } else if (event.inputType === "insertLineBreak") {
    event.preventDefault();
    state.lastKeyboardCommandAt = performance.now();
    sendKeyTap(13, "Enter");
  }
}

function handleKeyboardInput() {
  if (state.composingText) {
    return;
  }

  flushKeyboardInput();
}

function flushKeyboardInput() {
  const value = elements.keyboardInput.value;
  if (value.length > 0) {
    state.lastKeyboardCommandAt = performance.now();
    sendText(value);
  }

  elements.keyboardInput.value = "";
}

function handleKeyboardKeyDown(event) {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  if (performance.now() - state.lastKeyboardCommandAt < 60) {
    return;
  }

  if (event.key === "Backspace") {
    event.preventDefault();
    sendKeyTap(8, "Backspace");
  } else if (event.key === "Enter") {
    event.preventDefault();
    sendKeyTap(13, "Enter");
  } else if (event.key === "Tab") {
    event.preventDefault();
    sendKeyTap(9, "Tab");
  } else if (event.key === "Escape") {
    event.preventDefault();
    sendKeyTap(27, "Escape");
  }
}

function focusKeyboardSoon() {
  if (!state.keyboardOpen) {
    return;
  }

  setTimeout(() => elements.keyboardInput.focus({ preventScroll: true }), 0);
}

function sendText(text) {
  if (!text) {
    return;
  }

  sendInput({
    event: "text",
    text
  });
}

function sendKeyTap(keyCode, key) {
  sendInput({
    event: "keyDown",
    key,
    keyCode
  });
  sendInput({
    event: "keyUp",
    key,
    keyCode
  });
}

function toggleModifier(button) {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  const keyCode = Number(button.dataset.modifierKeyCode);
  if (!Number.isInteger(keyCode)) {
    return;
  }

  const id = String(keyCode);
  const key = button.dataset.key ?? "";
  const active = state.activeModifiers.has(id);
  sendInput({
    event: active ? "keyUp" : "keyDown",
    key,
    keyCode
  });

  button.classList.toggle("active", !active);
  button.setAttribute("aria-pressed", active ? "false" : "true");

  if (active) {
    state.activeModifiers.delete(id);
  } else {
    state.activeModifiers.set(id, { keyCode, key, button });
  }

  updateKeyboardButtonState();
}

function releaseActiveModifiers(sendKeyUp) {
  for (const modifier of state.activeModifiers.values()) {
    if (sendKeyUp) {
      sendInput({
        event: "keyUp",
        key: modifier.key,
        keyCode: modifier.keyCode
      });
    }

    modifier.button.classList.remove("active");
    modifier.button.setAttribute("aria-pressed", "false");
  }

  state.activeModifiers.clear();
  updateKeyboardButtonState();
}

function updateKeyboardButtonState() {
  const hasActiveModifiers = state.activeModifiers.size > 0;
  elements.keyboardButton.classList.toggle("active", state.keyboardOpen || hasActiveModifiers);
  elements.keyboardButton.title = hasActiveModifiers ? "手机键盘（修饰键已按下）" : "手机键盘";
}

function shouldCaptureKeyboard(event) {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return false;
  }

  const tagName = document.activeElement?.tagName;
  if (tagName === "INPUT" || tagName === "TEXTAREA" || tagName === "SELECT") {
    return false;
  }

  return isPointerLocked() || document.activeElement === elements.canvas || event.target === document.body;
}

function shouldIgnoreMouseEvent() {
  return performance.now() < state.ignoreMouseUntil;
}

function withoutLocalOptions(options) {
  const { useCurrentPointer, ...rest } = options;
  return rest;
}

function buttonName(button) {
  if (button === 2) {
    return "right";
  }

  if (button === 1) {
    return "middle";
  }

  return "left";
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function authPayload() {
  return {
    type: "auth",
    account: state.account,
    token: state.token,
    sessionToken: state.sessionToken
  };
}

function sendSocketAuth(socket) {
  socket.send(JSON.stringify(authPayload()));
}
