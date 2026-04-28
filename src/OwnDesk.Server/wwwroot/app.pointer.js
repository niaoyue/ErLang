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
  if (state.localDebugMode && (payload.event === "mouseClick" || payload.event === "wheel")) {
    state.suppressPointerInputUntil = performance.now() + 250;
  }

  const message = {
    type: "input",
    ...payload
  };
  if (typeof sendControlMessage === "function" && sendControlMessage(message)) {
    return;
  }

  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  state.socket.send(JSON.stringify(message));
}
