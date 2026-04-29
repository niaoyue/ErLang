# WebRTC MediaStream Roadmap

## 目标

将 OwnDesk 的画面传输从当前的 `Binary JPEG frame over WebSocket Relay` 演进到：

```text
Browser Viewer <-> WebRTC MediaStream <-> Windows Agent
```

期望能力：

1. 浏览器使用 `<video>` 播放远端桌面，而不是 canvas 解码 JPEG。
2. WebRTC 负责 ICE、DTLS、SRTP、拥塞控制和 jitter buffer。
3. 画面编码优先使用硬件编码。
4. 连接策略采用三层框架：局域网直连、公网 P2P、Server Relay 兜底。

## 现实约束

当前机器：

```text
GPU: NVIDIA GeForce GTX 960
Driver: 560.94
FFmpeg: not installed
GStreamer: not installed
```

硬件编码现实情况：

| Codec | 当前机器可行性 | 说明 |
| --- | --- | --- |
| H.264 | 可作为首选硬编目标 | GTX 960 支持 NVENC H.264，或走 Windows Media Foundation H.264 MFT |
| VP8 | 通常不是硬编目标 | WebRTC 常用 VP8，但 Windows/NVIDIA 硬编 VP8 并不现实，通常走软件 libvpx |
| AV1 | 当前机器不可硬编 | AV1 硬编需要更新一代显卡；GTX 960 不具备 AV1 编码硬件 |

所以“一步到位 H.264/VP8/AV1 全部硬编”不符合当前硬件和运行环境。可落地目标应是：

```text
WebRTC MediaStream + H.264 硬编优先 + VP8 软件回退 + AV1 作为未来硬件能力探测项
```

## 推荐实现路线

### Stage 1: WebRTC 信令和 MediaStream 骨架

当前已落地实验版：

```text
Viewer /ws/webrtc/viewer
  -> Server WebRtcSignalingRelay
  -> Agent /ws/webrtc/agent
```

已完成内容：

| 能力 | 状态 |
| --- | --- |
| Server SDP/ICE 中转 | 已完成 |
| Shared WebRTC 信令 DTO | 已完成 |
| Browser `RTCPeerConnection` | 已完成 |
| Browser `<video>` 播放入口 | 已完成 |
| Agent SIPSorcery peer | 已完成 |
| VP8 软件编码测试图案 | 已完成 |
| VP8 软件编码桌面采集帧 | 已完成 |
| 编码偏好和能力上报 | 已完成 |
| 画质档位 | 已完成，Viewer 可实时切换 |
| ICE/TURN 配置下发 | 已完成，Server 统一下发给 Viewer 和 Agent |
| WebRTC 自动尝试 | 已完成，连接设备后自动尝试视频，失败保留 JPEG fallback |
| 带宽基础优化 | 已完成，WebRTC 活跃时暂停 JPEG fallback，并跳过静止重复帧 |
| 采集后端抽象 | 已完成，当前实现为 GDI fallback |
| JPEG fallback | 保留 |

当前实验入口已经能把 Agent 侧桌面采集帧送入 VP8 MediaStream。采集层已经拆成可替换后端，当前实际实现仍然是 `Graphics.CopyFromScreen` + CPU libvpx；下一步是实现 Windows Graphics Capture/DXGI 后端，并接入 H.264 硬件编码。

Agent 目前支持这些 WebRTC 参数：

```powershell
--webrtc-codec auto|vp8|h264|av1
--webrtc-bitrate-kbps 1200
--quality-profile smooth|balanced|quality|ultra
--capture-backend auto|gdi|dxgi|wgc
```

当前实际可发送编码仍是 VP8；如果请求 H.264 或 AV1，Agent 会在能力上报中记录 requested/selected codec 和降级原因，然后使用 VP8 软件编码继续工作。
当前实际采集后端仍是 GDI；如果请求 DXGI 或 WGC，Agent 会在能力上报中记录 requested/selected capture backend 和降级原因，然后使用 GDI 继续工作。
Viewer 可通过 `streamQuality` 控制消息实时切换流畅/均衡/清晰/超清档位，Agent 下一帧应用新的 FPS、JPEG 质量、最大分辨率和 VP8 目标码率。Viewer 连接设备后会自动尝试 WebRTC；如果信令、ICE 或媒体连接失败，控制和画面会回到 Binary JPEG fallback。WebRTC 视频活跃时 Agent 会暂停 JPEG fallback 画面帧，并跳过几乎无变化的重复 VP8 帧。

Server 增加信令通道：

```text
/ws/webrtc/viewer
/ws/webrtc/agent
```

信令消息：

```json
{ "type": "webrtcOffer", "sessionId": "...", "sdpType": "offer", "sdp": "..." }
{ "type": "webrtcAnswer", "sessionId": "...", "sdpType": "answer", "sdp": "..." }
{ "type": "webrtcIce", "sessionId": "...", "candidate": { } }
{ "type": "webrtcCapabilities", "codecs": ["VP8"], "hardwareEncoding": false, "requestedCodec": "AUTO", "selectedCodec": "VP8", "requestedCaptureBackend": "AUTO", "selectedCaptureBackend": "GDI" }
```

Server 可配置 ICE/TURN：

```powershell
$env:OWNDESK_WEBRTC_ICE_SERVERS='[{"urls":["turn:relay.example.com:3478?transport=udp"],"username":"own","credential":"secret"}]'
$env:OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY="all"
```

配置会通过 `/api/webrtc/config` 提供给 Browser Viewer 和 Agent。`OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY=relay` 会在至少配置了一个 `turn:` / `turns:` server 时强制只使用 TURN relay 候选；没有 TURN server 时会降级为 `all`，避免 relay-only 配置导致 WebRTC 必然失败。

Viewer 使用浏览器原生 `RTCPeerConnection`：

```text
pc.ontrack -> video.srcObject = event.streams[0]
```

Agent 先用 VP8 软件编码桌面帧验证 MediaStream 和信令。

### Stage 2: Windows 屏幕采集改造

当前 `Graphics.CopyFromScreen` 已封装为 GDI capture backend，并同时服务 JPEG fallback 和 VP8 WebRTC 桌面帧验证。它是 CPU/GDI 路径，不适合长期的视频编码管线。

推荐改为：

```text
Windows Graphics Capture / DXGI Desktop Duplication
-> GPU texture / D3D11 frame
-> encoder input
```

### Stage 3: H.264 硬件编码

首选实现：

```text
Windows Media Foundation H.264 encoder MFT
```

候选实现：

```text
NVIDIA NVENC SDK
```

Media Foundation 的优点是 Windows 原生、部署压力小；NVENC 控制力更强，但引入 NVIDIA SDK 和设备兼容分支。

### Stage 4: WebRTC RTP 接入

需要将编码后的 H.264 Annex B/AVCC 帧转换为 WebRTC 能协商和发送的 RTP payload：

```text
H.264 frame
-> packetization-mode=1
-> RTP FU-A/STAP-A packetization
-> SRTP via WebRTC stack
```

如果使用 Google WebRTC native，可以把编码帧交给 WebRTC video source/encoder pipeline；如果使用 SIPSorcery，需要自己处理或接入现有 H.264 packetizer 能力。

### Stage 5: 三层连接策略

连接优先级：

| 优先级 | 模式 | 说明 |
| --- | --- | --- |
| 1 | LAN direct | Server 交换候选地址，Viewer 与 Agent 局域网直连 |
| 2 | P2P | 使用 ICE/STUN 尝试 NAT 打洞 |
| 3 | TURN/Relay | 打洞失败回退中继 |

Server Relay 兜底时不应回退到 JPEG，而应作为 TURN/SFU/relay 形态转发 WebRTC 媒体。

## 技术选型判断

| 方案 | 优点 | 风险 |
| --- | --- | --- |
| SIPSorcery + SIPSorceryMedia.Encoders | .NET 集成快，可先跑通 WebRTC MediaStream | 官方编码器主要是 VP8/libvpx，不是硬件 H.264/AV1 |
| Microsoft.MixedReality.WebRTC | Native WebRTC 封装，更接近真正 MediaStream | 包较旧，维护状态和 .NET 9/WinUI 兼容性需要验证 |
| Google WebRTC native | 能力完整，硬编/ICE/MediaStream 最接近目标 | C++/native 构建和 .NET interop 成本最高 |
| FFmpeg/GStreamer 外部进程 | 编码能力强，可用 NVENC | 当前机器未安装；WebRTC 接入仍复杂 |

## 当前结论

1. 立即可落地：WebRTC 信令骨架、浏览器 `<video>` Viewer、Agent WebRTC peer。
2. 已验证：SIPSorcery VP8 软件编码桌面帧 MediaStream。
3. 真正目标：Windows Graphics Capture + Media Foundation H.264 硬编 + WebRTC native/SRTP。
4. 当前硬件不支持 AV1 硬编，只能作为未来能力探测项。
