function applyQualityProfile() {
  sendStreamQuality(elements.qualitySelect.value);
}

function sendStreamQuality(profile) {
  const message = {
    type: "streamQuality",
    profile
  };
  if (typeof sendControlMessage === "function" && sendControlMessage(message)) {
    return;
  }

  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  state.socket.send(JSON.stringify(message));
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
  if (state.embeddedClient && window.chrome?.webview) {
    setHostFullscreen(!state.hostFullscreen, true);
    return;
  }

  if (document.fullscreenElement) {
    await document.exitFullscreen();
    return;
  }

  if (elements.workspace.requestFullscreen) {
    await elements.workspace.requestFullscreen();
  }
}

function handleFullscreenChange() {
  const fullscreen = isFullscreenActive();
  elements.fullscreenButton.classList.toggle("active", fullscreen);
  elements.fullscreenButton.textContent = fullscreen ? "□" : "⛶";
  document.documentElement.classList.toggle("host-fullscreen", state.hostFullscreen);
  elements.workspace.classList.toggle("host-fullscreen", state.hostFullscreen);
  setFullscreenToolbarVisible(!fullscreen || !shouldAutoHideFullscreenToolbar());
  setTimeout(() => {
    if (state.fitToScreen) {
      applyCanvasScale();
    }
  }, 80);
}

function toggleFullscreenToolbar() {
  if (!isFullscreenActive()) {
    return;
  }

  setFullscreenToolbarVisible(!state.fullscreenToolbarVisible);
}

function isFullscreenActive() {
  return document.fullscreenElement === elements.workspace || state.hostFullscreen;
}

function setHostFullscreen(active, notifyHost) {
  state.hostFullscreen = active;
  handleFullscreenChange();
  if (notifyHost) {
    window.chrome?.webview?.postMessage({ type: "hostFullscreen", active });
  }
}

async function exitFullscreenForDisconnect() {
  if (state.hostFullscreen) {
    setHostFullscreen(false, true);
  }

  if (!document.fullscreenElement || !document.exitFullscreen) {
    handleFullscreenChange();
    return;
  }

  try {
    await document.exitFullscreen();
  } catch {
    handleFullscreenChange();
  }
}

window.__ownDeskSetHostFullscreen = (active) => {
  setHostFullscreen(Boolean(active), false);
};

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
