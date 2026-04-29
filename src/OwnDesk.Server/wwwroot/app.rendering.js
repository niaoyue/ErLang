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

function resetBandwidthStats() {
  state.bandwidthSamples = [];
  state.bandwidthBytesPerSecond = 0;
}

function refreshBandwidthStats() {
  updateBandwidthRate(performance.now());
  if (state.frameWidth && state.frameHeight) {
    updateFrameStats(getCurrentScale());
  }
}

function recordBandwidthSample(byteCount) {
  if (!Number.isFinite(byteCount) || byteCount <= 0) {
    return;
  }

  const now = performance.now();
  state.bandwidthSamples.push({
    at: now,
    bytes: byteCount
  });
  updateBandwidthRate(now);
  if (state.frameWidth && state.frameHeight) {
    updateFrameStats(getCurrentScale());
  }
}

function updateBandwidthRate(now) {
  const windowMs = 2000;
  state.bandwidthSamples = state.bandwidthSamples.filter((sample) => now - sample.at <= windowMs);
  if (!state.bandwidthSamples.length) {
    state.bandwidthBytesPerSecond = 0;
    return;
  }

  const totalBytes = state.bandwidthSamples.reduce((sum, sample) => sum + sample.bytes, 0);
  const oldestSampleAt = state.bandwidthSamples[0].at;
  const spanMs = Math.max(1000, Math.min(windowMs, now - oldestSampleAt));
  state.bandwidthBytesPerSecond = totalBytes * 1000 / spanMs;
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
  const bandwidth = formatBandwidth(state.bandwidthBytesPerSecond);
  elements.frameStats.textContent = `${state.frameWidth}×${state.frameHeight} · ${mode} · ${Math.round(scale * 100)}% · ${bandwidth} · ${state.connectionMode}`;
}

function formatBandwidth(bytesPerSecond) {
  if (bytesPerSecond >= 1024 * 1024) {
    return `${(bytesPerSecond / 1024 / 1024).toFixed(1)}M/S`;
  }

  if (bytesPerSecond >= 1024) {
    return `${Math.round(bytesPerSecond / 1024)}KB/S`;
  }

  return `${Math.round(bytesPerSecond)}B/S`;
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
