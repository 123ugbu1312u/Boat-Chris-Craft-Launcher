# 联机验证

在源码根目录运行：

```powershell
dotnet test BCCL.Core.Test/BCCL.Core.Test.csproj -c Release --filter FullyQualifiedName~MCTierRoomTest
dotnet build scripts/MultiplayerVerification/MultiplayerVerification.csproj -c Release -o ../Temp/MCTier-integration/Verification
../Temp/MCTier-integration/Verification/MultiplayerVerification.exe --verify-ui
../Temp/MCTier-integration/Verification/MultiplayerVerification.exe --network '<包含 easytier-core.exe、easytier-cli.exe、Packet.dll、WinDivert64.sys、wintun.dll 的目录>'
../Temp/MCTier-integration/Verification/MultiplayerVerification.exe --session
../Temp/MCTier-integration/Verification/MultiplayerVerification.exe --directory-live
../Temp/MCTier-integration/Verification/MultiplayerVerification.exe --verify-name-live
../Temp/MCTier-integration/Verification/MultiplayerVerification.exe --managed-session-live
```

`--network` 在隔离数据目录运行两个真实的免网卡 EasyTier 实例，经本机 TCP 节点发现、SOCKS、本机游戏入口和虚拟 IP 传送数据并检查返回值，最后关闭自己创建的两个核心。`--session` 使用前一步准备的隔离运行时，验证生产代码的私有命名管道、辅助进程、核心 RPC 及断开清理。两项均不创建系统网卡或修改防火墙。

不传参数时打开独立的原生页面验证窗口，使用内存配置提供程序和 `verification-data`，不会启动完整启动器生命周期或读取玩家账号。关闭验证窗口后可重新构建。此工具不代表真实公网双机 Minecraft 对局验证，也不自动处理 Windows UAC 授权。

`--verify-ui` 在内存配置与隔离会话中验证首页、创建/加入切换、自动邀请解析、手动无密码加入、高级端口绑定、错误提示、已连接界面和手动地址保护。布局检查使用与主窗口相同的 Auto 左栏，在 740/900/1260 像素内容区宽度验证左栏固定宽度与右侧可用空间。

三个 `*-live` 检查会访问默认名称服务，只使用随机生成的 `BCCL-*-QA-...` 私有测试房间，并在结束时释放。`--directory-live` 验证签名注册、不同密码的同名创建被拒绝、地址转交、按名称加入和名称释放；`--verify-name-live` 通过实际 WPF 输入事件验证自动检测、重名提示、快速修改与退出释放；`--managed-session-live` 使用实际辅助进程与 EasyTier 核心确认名称登记和虚拟地址，需先通过 `--network` 准备隔离组件。它们不发布公开大厅、不发送聊天消息、不创建系统网卡。
