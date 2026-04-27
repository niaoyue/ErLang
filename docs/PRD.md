# OwnDesk PRD

## 1. 背景

目标是做一个自用远程控制软件，体验方向参考向日葵、ToDesk：在不同设备上登录同一个账号后，可以看到自己的在线设备，并从浏览器发起远程控制。

本项目只覆盖合法、自有设备、显式登录、显式运行 Agent 的场景。不做隐藏驻留、绕过系统授权、静默控制、权限提升、凭据窃取、杀软规避等能力。

## 2. GitHub 参考项目

调研日期：2026-04-26。

| 项目 | 地址 | 可借鉴点 | 本项目取舍 |
| --- | --- | --- | --- |
| RustDesk | https://github.com/rustdesk/rustdesk | 开源远程桌面，自托管 rendezvous/relay 思路成熟 | 借鉴“自托管中继 + Agent + Viewer”形态，不在 MVP 阶段实现 P2P/NAT 打洞 |
| MeshCentral | https://github.com/Ylianst/MeshCentral | Web 管理端、设备 Agent、远程桌面/文件/终端一体化 | 借鉴“账号下设备列表 + 浏览器控制台”，MVP 只做远程桌面 |
| Remotely | https://github.com/immense/Remotely | Server/Agent 架构，浏览器远控体验接近需求 | 借鉴 Signal/WebSocket 中继模式，避免早期引入复杂部署 |
| Apache Guacamole | https://github.com/apache/guacamole-server | Clientless remote desktop gateway | 借鉴浏览器端无插件观看/控制思路，不接 RDP/VNC 协议 |
| noVNC | https://github.com/novnc/noVNC | 浏览器 canvas 展示远程画面和转发输入 | 借鉴 canvas 输入映射模式，协议使用本项目自定义 JSON |

## 3. 产品目标

1. 同一个账号下的 Agent 设备能自动出现在设备列表。
2. Viewer 能从浏览器连接任意在线设备。
3. Viewer 能实时看到远端屏幕画面。
4. Viewer 能发送鼠标和键盘输入到远端设备。
5. Server 能作为单账号自托管中继运行，便于内网或公网部署。

## 4. 非目标

1. 不做隐藏控制、静默安装、自动提权、规避安全软件。
2. 不做公网商业级账号体系、计费、风控、审计后台。
3. MVP 不做 P2P、NAT 穿透、UDP 媒体传输。
4. MVP 不做移动端、Linux/macOS Agent。
5. MVP 不实现文件传输、剪贴板同步、远程终端、多人协作。

## 5. 用户与场景

| 用户 | 场景 | 诉求 |
| --- | --- | --- |
| 个人用户 | 家里电脑运行 Agent，外出用浏览器连接 | 快速看到在线设备并控制 |
| 开发者本人 | 多台自有 Windows 设备维护 | 低限制、可自托管、可扩展 |
| 内网用户 | 服务端部署在局域网 | 不依赖第三方 SaaS |

## 6. 功能需求

### 6.1 账号认证

1. Server 配置一个账号和一个访问令牌。
2. Agent 和 Viewer 必须使用同一组账号/令牌连接。
3. 认证失败的 HTTP API 和 WebSocket 连接应被拒绝。

### 6.2 设备注册

1. Agent 启动后向 Server 注册设备 ID、设备名称和屏幕尺寸。
2. Server 维护在线设备列表。
3. Agent 断开后设备状态变为离线或从在线列表移除。

### 6.3 远程画面

1. Agent 周期性采集主屏幕。
2. Agent 将画面压缩为 JPEG 后通过 WebSocket 发给 Server。
3. Server 将画面转发给已连接 Viewer。
4. Viewer 用 canvas 渲染最新帧。

### 6.4 输入控制

1. Viewer 将鼠标移动、按下、释放、滚轮事件发送给 Agent。
2. Viewer 将键盘按下、释放事件发送给 Agent。
3. Agent 将浏览器坐标映射到远端主屏幕坐标并注入输入。

### 6.5 管理界面

1. 浏览器首页显示登录表单。
2. 登录后显示在线设备列表。
3. 连接设备后显示远程画布、连接状态、分辨率和帧计数。
4. 支持刷新设备列表和断开连接。

## 7. 非功能需求

| 分类 | 要求 |
| --- | --- |
| 安全 | 默认要求账号/令牌；生产部署必须使用 HTTPS/WSS；Agent 显式运行并在控制台显示连接状态 |
| 性能 | MVP 目标 3-8 FPS，可通过参数调整；优先稳定性 |
| 可维护性 | Server、Agent、Shared 协议分层；协议字段保持 JSON 可读 |
| 可部署性 | .NET 9；Server 可运行在 Windows/Linux；Agent MVP 只支持 Windows |
| 可测试性 | 认证、URI、协议序列化有基础自动化测试 |

## 8. 成功标准

1. `dotnet build OwnDesk.sln` 成功。
2. `dotnet test OwnDesk.sln` 成功。
3. Server 启动后浏览器可以打开控制台。
4. Agent 使用正确账号连接后，设备能出现在列表。
5. Viewer 连接设备后能看到持续刷新的画面。
6. 鼠标和键盘输入能传递到远端 Windows 主屏幕。
