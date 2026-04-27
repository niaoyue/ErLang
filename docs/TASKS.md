# OwnDesk 任务清单

状态说明：`[x]` 完成，`[~]` 进行中，`[ ]` 未开始。

## A. 调研与产品设计

- [x] A1. 调研 GitHub 可参考项目：RustDesk、MeshCentral、Remotely、Guacamole、noVNC
- [x] A2. 明确合法自用、显式登录、显式 Agent 的产品边界
- [x] A3. 编写 PRD
- [x] A4. 编写细化设计

## B. 工程骨架

- [x] B1. 创建 .NET solution
- [x] B2. 创建 Shared、Server、Agent、Tests 项目
- [x] B3. 配置项目引用和 Windows Agent 目标框架
- [x] B4. 添加基础 `.gitignore`

## C. Shared 协议与认证

- [x] C1. 定义 WebSocket 消息类型
- [x] C2. 定义设备、帧、输入 DTO
- [x] C3. 实现单账号认证
- [x] C4. 实现 Server/WebSocket URI 构造工具

## D. Server

- [x] D1. 实现认证保护的 `/api/devices`
- [x] D2. 实现 `/ws/agent` 注册和帧接收
- [x] D3. 实现 `/ws/viewer` 连接和输入接收
- [x] D4. 实现设备注册表和 WebSocket 安全发送
- [x] D5. 托管浏览器控制台静态资源

## E. Windows Agent

- [x] E1. 解析命令行和环境变量
- [x] E2. 连接 Server 并断线重连
- [x] E3. 采集主屏幕并压缩为 JPEG
- [x] E4. 上传帧
- [x] E5. 接收并注入鼠标键盘输入

## F. Browser Viewer

- [x] F1. 登录表单
- [x] F2. 在线设备列表
- [x] F3. WebSocket 连接和画面渲染
- [x] F4. 鼠标/键盘事件映射
- [x] F5. 连接状态展示和断开控制

## G. 自检与测试

- [x] G1. 编写基础自动化测试
- [x] G2. `dotnet build OwnDesk.sln`
- [x] G3. `dotnet test OwnDesk.sln`
- [x] G4. 启动 Server 做健康检查
- [x] G5. 安全边界自检
- [x] G6. Agent 注册冒烟测试
- [x] G7. Agent -> Server -> Viewer 画面中继冒烟测试

## H. WebRTC MediaStream 实验链路

- [x] H1. 增加 WebRTC 信令消息类型和 DTO
- [x] H2. Server 增加 `/ws/webrtc/agent` 与 `/ws/webrtc/viewer`
- [x] H3. Server 按 `sessionId` 中转 SDP/ICE
- [x] H4. Agent 增加 SIPSorcery WebRTC peer
- [x] H5. Agent 增加 VP8/libvpx 测试图案 MediaStream
- [x] H6. Browser Viewer 增加 `RTCPeerConnection` 和 `<video>` 播放入口
- [x] H7. 保留 Binary JPEG + 输入控制 fallback
- [x] H8. 将 VP8 测试图案替换为屏幕采集帧源
- [x] H8.1. 增加 WebRTC 编码偏好、码率参数和能力上报
- [x] H8.2. 增加画质档位和 Viewer 实时切换
- [x] H9.1. 抽象屏幕采集后端并保留 GDI 实现
- [x] H9.2. 增加采集后端配置和 DXGI/WGC 显式降级上报
- [x] H9.2.1. 移动端全屏工具栏改为悬浮按钮显示/隐藏
- [x] H9.2.2. 移动端补充右键和 Ctrl/Shift/Alt/Win 修饰键，桌面端隐藏移动专用控件
- [ ] H9.3. 实现 Windows Graphics Capture/DXGI 实际采集后端
- [ ] H10. 接入 H.264 硬件编码

## 验证结果

- [x] `dotnet build OwnDesk.sln`：成功，0 warning，0 error
- [x] `dotnet test OwnDesk.sln`：包含 WebRTC 信令 DTO 的测试通过
- [x] `/api/health`：返回 `ok`
- [x] `/api/devices`：POST 认证后返回 200
- [x] Agent 注册：Server 看到 `RelayTest` 设备，分辨率 1920×1080
- [x] 画面中继：临时 Viewer 收到 `device` 消息和 `frame` 消息
- [x] WebRTC MediaStream：已接入 VP8 桌面采集帧，浏览器 `<video>` 可播放
- [x] WebRTC 能力上报：Server 日志记录 requested/selected codec、capture backend、encoder、target kbps 和降级原因
- [x] 采集后端上报：Server 日志记录 requested/selected capture backend，DXGI/WGC 请求会降级到 GDI
- [x] 画质档位：Viewer 可切换流畅/均衡/清晰/超清，Agent 下一帧应用新的 FPS、JPEG 质量、最大分辨率和 WebRTC 码率
