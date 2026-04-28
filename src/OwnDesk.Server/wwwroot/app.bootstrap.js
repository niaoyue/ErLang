
configureMobileControls();
for (const query of mobileControlQueries) {
  query.addEventListener?.("change", configureMobileControls);
}

elements.loginForm.addEventListener("submit", handleAuthSubmit);
elements.loginModeButton.addEventListener("click", () => setAuthMode("login"));
elements.registerModeButton.addEventListener("click", () => setAuthMode("register"));
elements.organizationSelect.addEventListener("change", switchOrganization);
elements.addOrganizationButton.addEventListener("click", addOrganization);
elements.saveOrganizationButton.addEventListener("click", () => {
  saveCurrentOrganization(true);
  renderAuthState();
});
elements.deleteOrganizationButton.addEventListener("click", deleteOrganization);
elements.logoutButton.addEventListener("click", logoutMember);

for (const button of document.querySelectorAll("[data-toggle-section]")) {
  button.addEventListener("click", () => toggleSection(button.dataset.toggleSection));
}

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

initializeWebConsole();
