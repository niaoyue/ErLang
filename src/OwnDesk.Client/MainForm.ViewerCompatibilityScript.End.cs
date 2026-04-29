namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private const string EmbeddedCompatibilityScriptEnd =
            """
                }

                return `${Math.round(bytesPerSecond)}B/S`;
              };
              const refreshBandwidthCompat = () => {
                const now = performance.now();
                const bandwidth = window.__ownDeskBandwidth || { samples: [], bytesPerSecond: 0 };
                bandwidth.samples = (bandwidth.samples || []).filter((sample) => now - sample.at <= 2000);
                if (!bandwidth.samples.length) {
                  bandwidth.bytesPerSecond = 0;
                  window.__ownDeskBandwidth = bandwidth;
                  return;
                }

                const total = bandwidth.samples.reduce((sum, sample) => sum + sample.bytes, 0);
                const oldest = bandwidth.samples[0].at;
                const span = Math.max(1000, Math.min(2000, now - oldest));
                bandwidth.bytesPerSecond = total * 1000 / span;
                window.__ownDeskBandwidth = bandwidth;
              };
              const recordBandwidth = (byteCount) => {
                if (!Number.isFinite(byteCount) || byteCount <= 0) {
                  return;
                }

                const samples = window.__ownDeskBandwidth.samples;
                samples.push({ at: performance.now(), bytes: byteCount });
                refreshBandwidthCompat();
                writeFrameStatsCompat();
              };
              const bandwidthRate = () => {
                const stateValue = pageState();
                if (stateValue) {
                  const now = performance.now();
                  const samples = Array.isArray(stateValue.bandwidthSamples)
                    ? stateValue.bandwidthSamples.filter((sample) => now - sample.at <= 2000)
                    : [];
                  stateValue.bandwidthSamples = samples;
                  if (!samples.length) {
                    stateValue.bandwidthBytesPerSecond = 0;
                    return 0;
                  }

                  const total = samples.reduce((sum, sample) => sum + sample.bytes, 0);
                  const oldest = samples[0].at;
                  const span = Math.max(1000, Math.min(2000, now - oldest));
                  stateValue.bandwidthBytesPerSecond = total * 1000 / span;
                  return stateValue.bandwidthBytesPerSecond;
                }

                refreshBandwidthCompat();
                return window.__ownDeskBandwidth.bytesPerSecond;
              };
              const connectionMode = () => {
                const stateValue = pageState();
                if (stateValue?.connectionMode) {
                  return stateValue.connectionMode;
                }

                return stateValue?.mediaMode === "webrtc" ? "检测中" : "中继";
              };
              const writeFrameStatsCompat = () => {
                const stats = document.getElementById("frameStats");
                if (!stats || window.__ownDeskWritingFrameStats) {
                  return;
                }

                const text = stats.textContent || "";
                if (!text || text === "等待画面" || text.includes("解码失败") || text.includes("正在解码")) {
                  return;
                }

                const cleanParts = text
                  .split(" · ")
                  .map((part) => part.trim())
                  .filter((part) => part && !bandwidthTextPattern.test(part) && !connectionModePattern.test(part));
                const bandwidth = formatBandwidth(bandwidthRate());
                const mode = connectionMode();
                const next = `${cleanParts.join(" · ")} · ${bandwidth} · ${mode}`;
                if (next !== text) {
                  window.__ownDeskWritingFrameStats = true;
                  stats.textContent = next;
                  window.__ownDeskWritingFrameStats = false;
                }
              };

              window.__ownDeskBandwidth = { samples: [], bytesPerSecond: 0 };
              window.__ownDeskCompatibilityRenderDevices = renderDevices;
              window.__ownDeskRefreshDevicesCompat = refreshDevicesCompat;
              window.__ownDeskRemoveDeviceCompat = removeDeviceCompat;
              window.__ownDeskStartDeviceWatcherCompat = startDeviceWatcherCompat;
              window.__ownDeskUpdateFrameStatsCompat = writeFrameStatsCompat;
              window.__ownDeskApplyClientCompatibility = () => {
                updateLocalDeviceId();

                installRtcPeerConnectionPatch();
                replaceLabels();
                for (const [name, value] of [
                  ["renderDevices", renderDevices],
                  ["removeDevice", removeDeviceCompat]
                ]) {
                  if (!window[name]?.__ownDeskWrapped) {
                    const wrapped = (...args) => value(...args);
                    wrapped.__ownDeskWrapped = true;
                    window[name] = wrapped;
                  }
                }

                for (const name of ["disconnectViewer", "markDisconnected"]) {
                  if (typeof window[name] === "function" && !window[name].__ownDeskDeviceStateWrapped) {
                    const current = window[name];
                    const wrapped = function (...args) {
                      const result = current.apply(this, args);
                      if (name === "disconnectViewer" && args[0] === false) {
                        return result;
                      }

                      window.__ownDeskConnectingDeviceId = "";
                      const stateValue = pageState();
                      if (stateValue) {
                        stateValue.selectedDevice = null;
                      }

                      const activeDevice = document.getElementById("activeDevice");
                      if (activeDevice) {
                        activeDevice.textContent = "未连接";
                      }

                      const frameStats = document.getElementById("frameStats");
                      if (frameStats) {
                        frameStats.textContent = "0 帧";
                      }

                      rerenderDevicesSoon();
                      refreshDevicesCompat("disconnect");
                      return result;
                    };
                    wrapped.__ownDeskDeviceStateWrapped = true;
                    window[name] = wrapped;
                  }
                }

                if (window.updateFrameStats && !window.updateFrameStats.__ownDeskWrapped) {
                  const current = window.updateFrameStats;
                  const wrapped = function (...args) {
                    const result = current.apply(this, args);
                    writeFrameStatsCompat();
                    return result;
                  };
                  wrapped.__ownDeskWrapped = true;
                  window.updateFrameStats = wrapped;
                }

                if (window.handleViewerMessage && !window.handleViewerMessage.__ownDeskWrapped) {
                  const current = window.handleViewerMessage;
                  const wrapped = function (socket, data) {
                    const byteCount = typeof data === "string" ? data.length : data?.byteLength || data?.size || 0;
                    if (pageState()?.mediaMode !== "webrtc") {
                      recordBandwidth(byteCount);
                    }

                    return current.apply(this, arguments);
                  };
                  wrapped.__ownDeskWrapped = true;
                  window.handleViewerMessage = wrapped;
                }

                startDeviceWatcherCompat();
                writeFrameStatsCompat();
              };
              window.__ownDeskRefreshDevicesSoon = () => {
                for (const delay of retryDelays) {
                  window.setTimeout(() => {
                    window.__ownDeskApplyClientCompatibility?.();
                    refreshDevicesCompat("scheduled");
                  }, delay);
                }
              };
              window.__ownDeskSetLocalAgentRunning = (running) => {
                window.__ownDeskClientSession = {
                  ...(window.__ownDeskClientSession || {}),
                  localAgentRunning: Boolean(running)
                };
                updateLocalDeviceId();
                renderDevices(pageState()?.devices || loadHistory());
                refreshDevicesCompat(running ? "local-agent-started" : "local-agent-stopped");
              };

              window.setTimeout(() => {
                window.__ownDeskApplyClientCompatibility();
                refreshDevicesCompat("initial");
              }, 0);
              new MutationObserver(scheduleReplaceLabels).observe(document.body, { childList: true, subtree: true });
              const frameStats = document.getElementById("frameStats");
              if (frameStats) {
                new MutationObserver(writeFrameStatsCompat).observe(frameStats, { childList: true, subtree: true });
              }

              window.setInterval(writeFrameStatsCompat, 1000);
              return "compat-applied";
            })();
            """;
}
