# OwnDesk

OwnDesk 是一个自用远程控制 MVP，结构参考 RustDesk、MeshCentral、Remotely、Guacamole/noVNC 的常见形态：自托管 Server、Windows Agent、浏览器 Viewer。

当前实现只面向合法自有设备和显式运行的 Agent。它不会做隐藏安装、静默控制、权限提升、绕过授权或安全软件规避。

## 项目结构

```text
docs/
  PRD.md
  DESIGN.md
  TASKS.md
src/
  OwnDesk.Shared/
  OwnDesk.Server/
  OwnDesk.Agent.Core/
  OwnDesk.Agent/
  OwnDesk.Client/
tests/
  OwnDesk.Tests/
```

## 运行 Server

开发默认账号是 `demo`，但令牌没有内置默认值，启动 Server 前必须显式设置 `OWNDESK_TOKEN` 或 `OwnDesk:Token`。

```powershell
$env:OWNDESK_ACCOUNT = "me"
$env:OWNDESK_TOKEN = "replace-with-a-long-random-token"
dotnet run --project src\OwnDesk.Server\OwnDesk.Server.csproj
```

打开浏览器：

```text
http://127.0.0.1:5080
```

## 运行 Windows Agent

在要被控制的 Windows 设备上运行：

```powershell
dotnet run --project src\OwnDesk.Agent\OwnDesk.Agent.csproj -- --server http://127.0.0.1:5080 --account me --token replace-with-a-long-random-token --device-id pc-1 --device-name pc-1 --fps 5 --quality 55 --max-width 1280 --max-height 720
```

参数说明：

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `--server` | `http://127.0.0.1:5080` | Server 地址；跨机器访问应使用 HTTPS/WSS 反向代理地址 |
| `--account` | `demo` | 单账号名 |
| `--token` | 无默认值 | 访问令牌，必须显式提供 |
| `--device-id` | 当前机器名 | 账号下唯一设备 ID |
| `--device-name` | 当前机器名 | 界面显示名称 |
| `--fps` | `5` | 采集帧率，范围 1-15 |
| `--quality` | `55` | JPEG 质量，范围 20-90 |
| `--max-width` | `1280` | 发送画面的最大宽度，用于降低带宽 |
| `--max-height` | `720` | 发送画面的最大高度，用于降低带宽 |
| `--quality-profile` | `balanced` | 默认画质档位：`smooth`、`balanced`、`quality`、`ultra` |
| `--webrtc` | `true` | 是否开启实验性 WebRTC VP8 桌面视频流 |
| `--webrtc-codec` | `auto` | WebRTC 编码偏好：`auto`、`vp8`、`h264`、`av1`。当前 H.264/AV1 会降级到 VP8 |
| `--webrtc-bitrate-kbps` | `1200` | WebRTC VP8 目标码率，范围 150-20000 |
| `--capture-backend` | `auto` | 屏幕采集后端：`auto`、`gdi`、`dxgi`、`wgc`。当前 DXGI/WGC 会降级到 GDI |

也可以用环境变量：`OWNDESK_SERVER`、`OWNDESK_ACCOUNT`、`OWNDESK_TOKEN`、`OWNDESK_DEVICE_ID`、`OWNDESK_DEVICE_NAME`、`OWNDESK_FPS`、`OWNDESK_JPEG_QUALITY`、`OWNDESK_MAX_WIDTH`、`OWNDESK_MAX_HEIGHT`、`OWNDESK_QUALITY_PROFILE`、`OWNDESK_WEBRTC`、`OWNDESK_WEBRTC_CODEC`、`OWNDESK_WEBRTC_BITRATE_KBPS`、`OWNDESK_CAPTURE_BACKEND`。

## 运行 Windows 一体化 Client

`OwnDesk.Client` 是 ToDesk 类似的 Windows 一体化入口：Server 仍然部署在云服务器，Client 在 Windows 电脑上同时运行本机 Agent 和内嵌 Viewer。

```text
OwnDesk.Server  -> 云服务器
OwnDesk.Client  -> Windows 一体化客户端，自动让本机上线，也能控制在线设备
```

开发运行：

```powershell
dotnet run --project src\OwnDesk.Client\OwnDesk.Client.csproj
```

首次打开后填写：

| 字段 | 示例 | 说明 |
| --- | --- | --- |
| `Server` | `https://owndesk.zhonglehd.cn` | 云端 Server 地址 |
| `Account` | `me` | 与 Server 的 `OWNDESK_ACCOUNT` 一致 |
| `Token` | `replace-with-a-long-random-token` | 与 Server 的 `OWNDESK_TOKEN` 一致 |
| `Device ID` | `pc-1` | 账号下唯一设备 ID，默认当前机器名 |
| `Device Name` | `pc-1` | 界面显示名称，默认当前机器名 |

点 `Start Agent` 后，这台 Windows 电脑会注册为在线设备；右侧内嵌 WebView2 Viewer 会打开 Server 控制台并自动填入账号令牌。也可以勾选 `Start local agent on launch`，下次启动 Client 时自动上线本机。

配置保存到：

```text
%APPDATA%\OwnDesk\client-settings.json
```

这个文件会保存访问令牌，应只放在自有受信任的 Windows 账号下使用。

发布 Windows x64 客户端：

```powershell
dotnet publish src\OwnDesk.Client\OwnDesk.Client.csproj -c Release -r win-x64 --self-contained false
```

生成目录：

```text
src\OwnDesk.Client\bin\Release\net9.0-windows\win-x64\publish\
```

上面的发布方式依赖目标机器已安装 .NET 9 Desktop Runtime；如果希望把 .NET 运行时一起打包，可以改用 `--self-contained true`。

Client 使用 Microsoft WebView2 内嵌浏览器。大多数 Windows 10/11 已自带 WebView2 Runtime；如果目标机器没有，需要先安装 Microsoft Edge WebView2 Runtime。

## 测试

```powershell
dotnet build OwnDesk.sln
dotnet test OwnDesk.sln
```

如果在 Linux/macOS 上交叉构建 Windows Agent/Client，需要加：

```powershell
dotnet build OwnDesk.sln /p:EnableWindowsTargeting=true
dotnet test OwnDesk.sln /p:EnableWindowsTargeting=true
```

本轮已执行并通过：

```text
dotnet build OwnDesk.sln
dotnet test OwnDesk.sln
Server /api/health
Server /api/devices
Agent 注册冒烟测试
Agent -> Server -> Viewer 画面中继冒烟测试
Agent -> Server -> Viewer 二进制画面中继冒烟测试
WebRTC 信令消息序列化测试
```

## 安全注意事项

1. 公网使用必须放在 HTTPS/WSS 后面，例如 Nginx、Caddy、Traefik 反向代理。
2. 令牌需要使用长随机值，不要放进源码、日志或公开文档。
3. Agent 只应在自有设备上显式启动。
4. 当前 MVP 不做审计、设备二次确认和细粒度权限；后续版本应补这些能力。
5. 当前画面流为二进制 JPEG 帧流，适合 MVP 和低帧率远控；生产级高帧率应升级到真正的视频编码。

## 画面传输和带宽

当前输入控制仍然走 JSON 文本消息，画面流已经改为二进制 WebSocket 帧：

```text
8 字节前缀: magic ODF1 + header 长度
JSON header: sequence、width、height、capturedAtUtc、format、byteLength
JPEG bytes: 原始 JPEG 二进制
```

这比早期的 `base64 JPEG + JSON` 少掉约 33% 的 base64 膨胀，也减少了浏览器和 Server 的 JSON 解析开销。当前仍然不是 H.264/VP8/AV1 这类真正的视频编码，而是“二进制 JPEG 帧流”。

默认 Agent 使用 `balanced` 档位：`5 FPS / JPEG 55 / 1280x720 / WebRTC 1200kbps`。Viewer 顶部工具栏可以在连接后切换画质档位，切换会实时下发到 Agent，下一帧开始生效。

当前档位：

| 档位 | FPS | JPEG | 最大分辨率 | WebRTC 码率 |
| --- | ---: | ---: | --- | ---: |
| `smooth` 流畅 | 3 | 42 | 960x540 | 700 kbps |
| `balanced` 均衡 | 5 | 55 | 1280x720 | 1200 kbps |
| `quality` 清晰 | 8 | 68 | 1600x900 | 2500 kbps |
| `ultra` 超清 | 12 | 78 | 2560x1440 | 4500 kbps |

也可以用启动参数指定默认档位：

```powershell
dotnet run --project src\OwnDesk.Agent\OwnDesk.Agent.csproj -- --server https://YOUR_HTTPS_DOMAIN --account me --token replace-with-a-long-random-token --quality-profile quality
```

仍然可以用独立参数覆盖默认值，例如更省流量：

```powershell
dotnet run --project src\OwnDesk.Agent\OwnDesk.Agent.csproj -- --server https://YOUR_HTTPS_DOMAIN --account me --token replace-with-a-long-random-token --max-width 960 --max-height 540 --quality 45 --fps 3
```

坐标控制会按发送画面的尺寸映射回真实屏幕尺寸，所以降低分辨率不会导致点击位置整体偏移。

## WebRTC 实验视频

当前版本新增了实验性的 WebRTC MediaStream 通道：

```text
Browser Viewer WebRTC <-> Server 信令中转 <-> Windows Agent WebRTC peer
```

控制台连接设备后，点 `◉` 可以发起 WebRTC 视频。这个入口当前使用 SIPSorcery + libvpx 将 Agent 侧桌面采集帧编码为 VP8 MediaStream，再由浏览器 `<video>` 播放。原有二进制 JPEG 画面和输入通道仍保留作为可用 fallback。

当前 WebRTC 状态：

| 项目 | 状态 |
| --- | --- |
| SDP/ICE 信令 | 已通过 `/ws/webrtc/viewer` 和 `/ws/webrtc/agent` 中转 |
| 浏览器播放 | 已接入 `<video autoplay playsinline>` |
| Agent 编码 | VP8/libvpx 软件编码桌面帧 |
| 编码配置 | 支持 `--webrtc-codec` 和 `--webrtc-bitrate-kbps`，H.264/AV1 当前会显式降级到 VP8 |
| 屏幕采集接入 | 已抽象采集后端，当前实际后端为 GDI `Graphics.CopyFromScreen` |
| 采集后端配置 | 支持 `--capture-backend`，DXGI/WGC 当前会显式降级到 GDI |
| H.264 硬编 | 未接入，需要 Windows Graphics Capture/DXGI + Media Foundation 或 NVENC |
| AV1 硬编 | 当前 GTX 960 不支持，只能未来做能力探测 |

如果需要临时关闭 WebRTC 实验通道：

```powershell
dotnet run --project src\OwnDesk.Agent\OwnDesk.Agent.csproj -- --server https://YOUR_HTTPS_DOMAIN --account me --token replace-with-a-long-random-token --webrtc false
```

## 连接架构路线

当前实现仍是中继模式：

```text
Viewer <-> OwnDesk.Server <-> Agent
```

后续按三层连接策略演进：

| 优先级 | 模式 | 说明 |
| --- | --- | --- |
| 1 | 局域网直连 | Server 只负责认证和交换候选地址；Viewer 与 Agent 在局域网内直接传画面和输入 |
| 2 | 公网 P2P | 使用 WebRTC ICE/STUN 做 NAT 探测和打洞，成功后 Viewer 与 Agent 直连 |
| 3 | Server Relay | 直连失败时回退到当前中继模式 |

传输层演进建议：

| 阶段 | 画面格式 | 价值 |
| --- | --- | --- |
| 当前 | Binary JPEG frame | 实现简单，已去掉 base64 开销，适合 MVP 和低帧率远控 |
| 下一步 | Dirty rect / 变化检测 | 桌面静止时少发或不发，减少无效带宽 |
| 中期 | WebRTC DataChannel + Binary JPEG | 先把直连/P2P 跑通，仍复用现有帧格式 |
| 长期 | WebRTC MediaStream + H.264/VP8/AV1 | 真正的视频编码，带宽和延迟更接近成熟远控软件 |

## 鼠标跳回的原因

如果 Viewer 和 Agent 运行在同一台 Windows 桌面上，浏览器里的 canvas 正在显示“包含浏览器自己的远端屏幕”。鼠标移入 canvas 后，Viewer 会把 canvas 内坐标发给 Agent，Agent 又会调用 Windows API 把同一个本机系统鼠标移动到远端屏幕坐标。这个远端坐标通常不是当前浏览器 canvas 上的物理位置，于是系统鼠标会被拉出 canvas，浏览器继续发送新坐标，形成反馈环路，看起来就是“移过去又跳回去”。

真实远控时 Viewer 应该在另一台设备、虚拟机或另一套桌面会话里。为了便于同机自测，控制台提供了两个按钮：

| 按钮 | 场景 | 行为 |
| --- | --- | --- |
| `⌖` | 跨设备远控时鼠标不容易停在画布内 | 浏览器进入 Pointer Lock，用相对位移实时移动远端鼠标 |
| `◎` | Viewer 和 Agent 在同一台 Windows 桌面上 | 本机调试模式，只移动前端虚拟指针；点击/滚轮时才让 Agent 短暂移动系统鼠标并执行动作 |

本机调试模式适合验证点击和键盘路径，不适合验证拖拽、悬停菜单、游戏视角这类强依赖连续鼠标移动的场景。这类体验需要用另一台设备或虚拟机测试。

## 手机控制

手机访问建议通过 HTTPS/WSS 反向代理暴露 Server，例如：

```text
https://YOUR_HTTPS_DOMAIN/index.html
```

仅在完全受控的本地开发网络里，才建议临时使用 `--urls http://0.0.0.0:5080` 监听局域网明文 HTTP。明文 HTTP 会让同网段设备窃听令牌、输入和画面信令，不适合长期使用或任何公共网络。

连接设备后，控制条提供：

| 按钮 | 行为 |
| --- | --- |
| `⛶` | 进入或退出全屏，Android Chrome 支持较好；iPhone Safari 对页面全屏支持有限 |
| `−` / `+` | 缩小或放大远程画面 |
| `⤢` | 重新适配屏幕 |
| `↔` | 拖动画面模式，放大后用单指拖动画布查看边缘区域 |
| `⇅` | 手机端显示，远端滚轮模式，用单指上下拖动发送鼠标滚轮 |
| `⌨` | 手机端显示，打开手机键盘和特殊键面板 |
| `☰` / `×` | 手机全屏时显示或隐藏悬浮工具栏 |

触控手势：

| 手势 | 行为 |
| --- | --- |
| 单指拖动 | 移动远端鼠标 |
| 单指轻点 | 左键点击 |
| 双指捏合 | 放大或缩小画面 |
| 双指拖动 | 平移已放大的画面 |
| `↔` 开启后单指拖动 | 平移画面，不移动远端鼠标 |
| `⇅` 开启后单指上下拖动 | 滚动远端当前鼠标所在位置的内容 |

键盘输入：

1. 先在远程画面里点一下目标输入框，让远端窗口获得焦点。
2. 点 `⌨` 打开手机键盘，面板里会出现一个真实输入框。
3. 在这个输入框里输入文字；普通文字会通过 Unicode 输入发送到 Windows Agent，中文输入法提交后的字符也会发送。
4. 特殊键面板支持 `Esc`、`Tab`、`Backspace`、`Enter`、滚上、滚下和方向键。
5. 手机端特殊键面板额外支持 `右键`、`Ctrl`、`Shift`、`Alt`、`Win`。其中 `Ctrl`、`Shift`、`Alt`、`Win` 是锁定键，点一次按下，再点一次释放；断开连接时会尝试自动释放。
