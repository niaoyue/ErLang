function handleViewerMessage(socket, data) {
  if (state.socket !== socket) {
    return;
  }

  if (data instanceof ArrayBuffer) {
    if (state.mediaMode !== "webrtc") {
      recordBandwidthSample(data.byteLength);
    }

    drawBinaryFrame(data);
    return;
  }

  if (data instanceof Blob) {
    data.arrayBuffer().then((buffer) => {
      if (state.socket === socket) {
        if (state.mediaMode !== "webrtc") {
          recordBandwidthSample(buffer.byteLength);
        }

        drawBinaryFrame(buffer);
      }
    });
    return;
  }

  const text = data;
  const message = JSON.parse(text);
  if (message.type === "frame") {
    if (state.mediaMode !== "webrtc") {
      recordBandwidthSample(text.length);
    }

    drawFrame(message);
  } else if (message.type === "device" && message.device) {
    elements.activeDevice.textContent = message.device.deviceName || message.device.deviceId;
    updateConnectedDevice(message.device);
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

  const config = await loadWebRtcConfig();
  const sessionId = createSessionId();
  const peer = new RTCPeerConnection(config);
  const socket = new WebSocket(webSocketUrl(`/ws/webrtc/viewer?deviceId=${encodeURIComponent(state.selectedDevice.deviceId)}&sessionId=${encodeURIComponent(sessionId)}`));

  state.webRtcPeer = peer;
  state.webRtcSocket = socket;
  state.webRtcSessionId = sessionId;
  state.webRtcSelectedCandidateTypes = "";
  state.connectionMode = "检测中";
  setupWebRtcControlChannel(peer.createDataChannel("control", { ordered: true }));
  elements.webrtcButton.classList.add("active");
  elements.webrtcButton.title = "关闭 WebRTC 视频";
  elements.connectionStatus.textContent = "WebRTC 连接中";
  elements.connectionStatus.classList.remove("offline");
  reportWebRtcDiagnostic("start", {
    iceServers: config.iceServers.length,
    iceTransportPolicy: config.iceTransportPolicy
  });

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
      reportWebRtcDiagnostic("local-candidate", { type: candidateType(event.candidate) });
      sendWebRtcSignal({
        type: "webrtcIce",
        candidate: event.candidate.toJSON()
      });
    } else {
      reportWebRtcDiagnostic("local-candidates-complete");
    }
  };
  peer.oniceconnectionstatechange = () => {
    reportWebRtcDiagnostic("ice-state", { state: peer.iceConnectionState });
  };
  peer.onconnectionstatechange = () => {
    if (state.webRtcPeer !== peer) {
      return;
    }

    reportWebRtcDiagnostic("peer-state", { state: peer.connectionState });
    if (peer.connectionState === "connected") {
      scheduleWebRtcCandidateModeRefresh(peer);
      elements.connectionStatus.textContent = "WebRTC 已连接";
      elements.connectionStatus.classList.remove("offline");
    } else if (peer.connectionState === "disconnected") {
      stopWebRtc(true);
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
    reportWebRtcDiagnostic("answer");
  } else if (message.type === "webrtcIce" && message.candidate) {
    reportWebRtcDiagnostic("remote-candidate", { type: candidateType(message.candidate) });
    await peer.addIceCandidate(message.candidate);
  } else if (message.type === "webrtcError") {
    elements.connectionStatus.textContent = message.message || "WebRTC 错误";
    stopWebRtc(true);
  }
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

  const switchedToWebRtc = state.mediaMode !== "webrtc";
  state.mediaMode = "webrtc";
  setRelayVideoEnabled(false);
  if (switchedToWebRtc) {
    resetBandwidthStats();
  }

  scheduleWebRtcCandidateModeRefresh(state.webRtcPeer);
  elements.webrtcVideo.classList.add("visible");
  elements.canvas.classList.add("visible");
  elements.emptyScreen.classList.add("hidden");
  elements.connectionStatus.textContent = "WebRTC 视频";
  elements.connectionStatus.classList.remove("offline");
  syncWebRtcVideoSize();
  startWebRtcBandwidthMonitor();
  startWebRtcFrameMonitor();
}

function setRelayVideoEnabled(enabled) {
  if (state.relayVideoPaused === !enabled) {
    return;
  }

  state.relayVideoPaused = !enabled;
  if (!enabled) {
    resetBandwidthStats();
    state.webRtcLastBytesReceived = 0;
  }

  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
    return;
  }

  state.socket.send(JSON.stringify({
    type: "relayVideo",
    enabled
  }));
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

function startWebRtcBandwidthMonitor() {
  if (state.webRtcBandwidthTimer) {
    return;
  }

  state.webRtcLastBytesReceived = 0;
  state.webRtcBandwidthTimer = window.setInterval(updateWebRtcBandwidth, 1000);
}

async function updateWebRtcBandwidth() {
  const peer = state.webRtcPeer;
  if (!peer) {
    return;
  }

  let stats;
  try {
    stats = await peer.getStats();
  } catch {
    return;
  }

  let bytesReceived = 0;
  stats.forEach((report) => {
    if (report.type === "inbound-rtp" && report.kind === "video") {
      bytesReceived += report.bytesReceived || 0;
    }
  });

  if (state.webRtcLastBytesReceived > 0 && bytesReceived >= state.webRtcLastBytesReceived) {
    const byteDelta = bytesReceived - state.webRtcLastBytesReceived;
    if (byteDelta > 0) {
      recordBandwidthSample(byteDelta);
    } else {
      refreshBandwidthStats();
    }
  } else {
    refreshBandwidthStats();
  }

  state.webRtcLastBytesReceived = bytesReceived;
}

function stopWebRtcBandwidthMonitor() {
  if (!state.webRtcBandwidthTimer) {
    return;
  }

  window.clearInterval(state.webRtcBandwidthTimer);
  state.webRtcBandwidthTimer = 0;
  state.webRtcLastBytesReceived = 0;
}

function stopWebRtc(updateUi = true) {
  const peer = state.webRtcPeer;
  const socket = state.webRtcSocket;
  const controlChannel = state.webRtcControlChannel;
  const stream = elements.webrtcVideo.srcObject;

  setRelayVideoEnabled(true);

  if (state.webRtcCandidateModeTimer) {
    window.clearTimeout(state.webRtcCandidateModeTimer);
    state.webRtcCandidateModeTimer = 0;
  }

  stopWebRtcFrameMonitor();
  stopWebRtcBandwidthMonitor();
  state.webRtcPeer = null;
  state.webRtcSocket = null;
  state.webRtcSessionId = "";
  state.webRtcControlChannel = null;
  state.webRtcSelectedCandidateTypes = "";
  state.mediaMode = "jpeg";
  state.connectionMode = "中继";

  if (peer) {
    peer.ontrack = null;
    peer.onicecandidate = null;
    peer.onconnectionstatechange = null;
    peer.close();
  }

  if (socket) {
    socket.close();
  }

  if (controlChannel) {
    controlChannel.close();
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
