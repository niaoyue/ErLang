async function loadWebRtcConfig() {
  if (state.webRtcIceConfigLoaded) {
    return {
      iceServers: state.webRtcIceServers,
      iceTransportPolicy: state.webRtcIceTransportPolicy
    };
  }

  if (state.webRtcIceConfigRetryAt && performance.now() < state.webRtcIceConfigRetryAt) {
    return {
      iceServers: [],
      iceTransportPolicy: "all"
    };
  }

  try {
    const response = await fetch(apiUrl("/api/webrtc/config"), {
      method: "POST",
      headers: {
        "content-type": "application/json"
      },
      body: JSON.stringify(authPayload())
    });

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    const config = await response.json();
    state.webRtcIceServers = normalizeIceServers(config.iceServers);
    state.webRtcIceTransportPolicy = normalizeIceTransportPolicy(config.iceTransportPolicy);
    state.webRtcIceConfigLoaded = true;
    state.webRtcIceConfigRetryAt = 0;
  } catch (error) {
    state.webRtcIceServers = [];
    state.webRtcIceTransportPolicy = "all";
    state.webRtcIceConfigLoaded = false;
    state.webRtcIceConfigRetryAt = performance.now() + 5000;
    reportWebRtcDiagnostic("ice-config-fallback", { message: error?.message || "unknown" });
  }

  return {
    iceServers: state.webRtcIceServers,
    iceTransportPolicy: state.webRtcIceTransportPolicy
  };
}

function resetWebRtcConfigCache() {
  state.webRtcIceServers = [];
  state.webRtcIceTransportPolicy = "all";
  state.webRtcIceConfigLoaded = false;
  state.webRtcIceConfigRetryAt = 0;
}

function normalizeIceServers(servers) {
  if (!Array.isArray(servers)) {
    return [];
  }

  return servers
    .map((server) => ({
      urls: server.urls || server.Urls || [],
      username: server.username || server.Username || undefined,
      credential: server.credential || server.Credential || undefined,
      credentialType: server.credentialType || server.CredentialType || undefined
    }))
    .filter((server) => Array.isArray(server.urls) ? server.urls.length > 0 : Boolean(server.urls));
}

function normalizeIceTransportPolicy(policy) {
  return policy === "relay" ? "relay" : "all";
}

async function updateWebRtcCandidateMode(peer) {
  if (!peer) {
    return false;
  }

  const types = await selectedCandidateTypes(peer);
  if (!types) {
    state.connectionMode = "检测中";
    reportWebRtcDiagnostic("selected-candidate-unavailable");
    if (state.frameWidth && state.frameHeight) {
      updateFrameStats(getCurrentScale());
    }

    return false;
  }

  state.webRtcSelectedCandidateTypes = types;
  state.connectionMode = types.includes("relay") ? "中继" : "直连";
  reportWebRtcDiagnostic("selected-candidate", { types, mode: state.connectionMode });
  if (state.frameWidth && state.frameHeight) {
    updateFrameStats(getCurrentScale());
  }

  return true;
}

function scheduleWebRtcCandidateModeRefresh(peer, attempt = 0) {
  if (state.webRtcPeer !== peer) {
    return;
  }

  const delays = [0, 250, 750, 1500, 3000, 5000];
  const delay = delays[Math.min(attempt, delays.length - 1)];
  if (state.webRtcCandidateModeTimer) {
    window.clearTimeout(state.webRtcCandidateModeTimer);
  }

  state.webRtcCandidateModeTimer = window.setTimeout(async () => {
    state.webRtcCandidateModeTimer = 0;
    if (state.webRtcPeer !== peer) {
      return;
    }

    const resolved = await updateWebRtcCandidateMode(peer);
    if (!resolved && attempt + 1 < delays.length) {
      scheduleWebRtcCandidateModeRefresh(peer, attempt + 1);
    }
  }, delay);
}

async function selectedCandidateTypes(peer) {
  let stats;
  try {
    stats = await peer.getStats();
  } catch {
    return "";
  }

  for (const report of stats.values()) {
    if (report.type !== "transport" || !report.selectedCandidatePairId) {
      continue;
    }

    return candidatePairTypes(stats, report.selectedCandidatePairId);
  }

  for (const report of stats.values()) {
    if (report.type === "candidate-pair" && (report.selected || report.nominated) && report.state === "succeeded") {
      return candidatePairTypes(stats, report.id);
    }
  }

  return "";
}

function candidatePairTypes(stats, pairId) {
  const pair = stats.get(pairId);
  if (!pair) {
    return "";
  }

  const local = stats.get(pair.localCandidateId);
  const remote = stats.get(pair.remoteCandidateId);
  return [local?.candidateType, remote?.candidateType].filter(Boolean).join("/");
}

function candidateType(candidate) {
  if (candidate?.type) {
    return candidate.type;
  }

  const text = candidate?.candidate || "";
  const match = /\styp\s([a-z0-9]+)/i.exec(text);
  return match ? match[1].toLowerCase() : "unknown";
}

function reportWebRtcDiagnostic(event, detail = {}) {
  const payload = { type: "webrtcDiagnostic", event, detail };
  window.chrome?.webview?.postMessage(payload);
  console.debug?.("OwnDesk WebRTC", event, detail);
}

function setupWebRtcControlChannel(channel) {
  state.webRtcControlChannel = channel;
  channel.addEventListener("open", () => {
    reportWebRtcDiagnostic("data-channel-open", { label: channel.label });
  });
  channel.addEventListener("close", () => {
    if (state.webRtcControlChannel === channel) {
      state.webRtcControlChannel = null;
    }

    reportWebRtcDiagnostic("data-channel-close", { label: channel.label });
  });
  channel.addEventListener("error", () => {
    reportWebRtcDiagnostic("data-channel-error", { label: channel.label });
  });
}

function sendControlMessage(payload) {
  const channel = state.webRtcControlChannel;
  if (!channel || channel.readyState !== "open") {
    return false;
  }

  channel.send(JSON.stringify(payload));
  return true;
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
