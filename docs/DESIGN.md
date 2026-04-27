# OwnDesk 细化设计

## 1. 架构

```text
Browser Viewer  <--WSS/HTTPS-->  OwnDesk.Server  <--WSS-->  OwnDesk.Agent
       |                              |                         |
       | canvas render/input          | auth/device registry     | screen capture/input injection
```

MVP 使用 Server 中继所有控制流和画面流。这样部署简单，先保证自用可运行；后续再评估 WebRTC、P2P、TURN/relay、硬件编码等优化。

## 2. 项目结构

```text
src/
  OwnDesk.Shared/   协议 DTO、JSON 选项、账号认证、端点 URI 构造
  OwnDesk.Server/   ASP.NET Core API、WebSocket 中继、浏览器静态资源
  OwnDesk.Agent/    Windows Agent、屏幕采集、输入注入
tests/
  OwnDesk.Tests/    xUnit 协议、认证和消息序列化测试
docs/
  PRD.md
  DESIGN.md
  TASKS.md
```

## 3. Server 设计

### 3.1 HTTP API

| Endpoint | 方法 | 作用 |
| --- | --- | --- |
| `/` | GET | 重定向到 `/index.html` |
| `/api/health` | GET | 健康检查 |
| `/api/devices` | POST | 请求体携带认证信息，返回当前账号在线设备 |

### 3.2 WebSocket

| Endpoint | 连接方 | 参数 | 作用 |
| --- | --- | --- | --- |
| `/ws/agent` | Agent | WebSocket 首条 `auth` 消息携带账号、令牌、设备信息 | 注册设备，上传帧，接收输入 |
| `/ws/viewer` | Browser | Query 只携带 `deviceId`，WebSocket 首条 `auth` 消息携带账号和令牌 | 连接设备，接收帧，发送输入 |

### 3.3 连接管理

1. `DeviceRegistry` 用账号和设备 ID 作为 key。
2. 每台设备只有一个 Agent 连接，重复连接会替换旧连接。
3. 每台设备可以有多个 Viewer。
4. 每个 WebSocket 发送端有独立 `SemaphoreSlim`，避免并发发送破坏帧序。
5. Server 不落盘保存帧和输入事件。

## 4. 协议设计

### 4.1 Agent Hello

```json
{
  "type": "agentHello",
  "deviceId": "DESKTOP-001",
  "deviceName": "Workstation",
  "screenWidth": 1920,
  "screenHeight": 1080,
  "sentAtUtc": "2026-04-26T10:00:00Z"
}
```

### 4.2 Frame

```json
{
  "type": "frame",
  "sequence": 1,
  "width": 1920,
  "height": 1080,
  "capturedAtUtc": "2026-04-26T10:00:00Z",
  "imageBase64": "/9j/4AAQSk..."
}
```

### 4.3 Input

```json
{
  "type": "input",
  "event": "mouseDown",
  "x": 100,
  "y": 200,
  "button": "left"
}
```

键盘事件示例：

```json
{
  "type": "input",
  "event": "keyDown",
  "keyCode": 65,
  "key": "a"
}
```

## 5. Agent 设计

1. `AgentOptions` 从命令行或环境变量读取 Server、账号、令牌、设备 ID、FPS、JPEG 质量和最大发送分辨率。
2. `ScreenCaptureService` 使用 Windows Desktop API 采集主屏幕，按最大宽高缩放，并压缩 JPEG。
3. `RemoteAgent` 维护 WebSocket，断线后延迟重连。
4. `InputController` 使用 Windows `user32.dll` 注入鼠标和键盘事件。
5. Agent 只在用户显式启动的控制台进程中运行。

## 6. 浏览器控制台设计

1. 登录信息只保存在当前页面内存，不写 localStorage。
2. 设备列表通过 `/api/devices` 拉取。
3. 连接设备后打开 `/ws/viewer`。
4. canvas 根据远端帧尺寸设置内部分辨率，根据页面空间缩放显示。
5. 鼠标坐标按 canvas 显示尺寸换算到发送帧像素坐标。
6. Agent 将发送帧像素坐标映射回真实主屏幕坐标。

## 7. 后续扩展

| 方向 | 说明 |
| --- | --- |
| 安全 | HTTPS 反向代理、令牌哈希、短期 session、设备授权确认、操作审计 |
| 性能 | 变化检测、差分帧、WebP/AV1、WebRTC DataChannel/MediaStream、UDP relay |
| 多平台 | macOS ScreenCaptureKit、Linux PipeWire/X11、跨平台输入注入 |
| 功能 | 剪贴板、文件传输、远程终端、多屏选择、只看模式 |

## 8. 连接架构路线

当前 MVP 使用 Server Relay：

```text
Viewer <-> OwnDesk.Server <-> Agent
```

后续按三层连接策略演进：

| 优先级 | 模式 | 说明 |
| --- | --- | --- |
| 1 | 局域网直连 | Server 只做认证、设备列表和候选地址交换；Viewer 与 Agent 在局域网内直连 |
| 2 | 公网 P2P | 使用 WebRTC ICE/STUN 做 NAT 探测和打洞，成功后直连 |
| 3 | Server Relay | 直连失败时回退到当前中继 |

画面传输也分阶段：

| 阶段 | 格式 | 说明 |
| --- | --- | --- |
| 当前 | Binary JPEG frame | 自定义 `ODF1` 二进制 WebSocket 帧，去掉 base64 膨胀 |
| 下一步 | Binary JPEG + 变化检测 | 桌面不变时跳帧，减少无效带宽 |
| 中期 | WebRTC DataChannel + Binary JPEG | 先跑通直连/P2P |
| 长期 | WebRTC MediaStream + H.264/VP8/AV1 | 使用真正视频编码和硬件编码 |
