# Boat Chris Craft Launcher

**BCCL Alpha 0.1.2**，Windows Minecraft 启动器。

- [GitHub](https://github.com/123ugbu1312u/Boat-Chris-Craft-Launcher)
- [GitCode](https://gitcode.com/BoatMinecraft/BoatChrisCraftLauncher)

支持游戏安装、实例管理、Java 与账号配置、模组资源下载，以及跟随启动器主题的 Liquid Glass 界面。

## 联机

联机页内嵌完整 MCTier 3.10.0，保留原版房间、聊天、文件共享、语音、屏幕共享、远程控制和房间工具。界面使用本地 WebView，嵌入启动器窗口；尚未改为全部 WPF 原生控件。

适配包括背景随启动器主题变化、内容随窗口尺寸调整、BCCL 名称与图标，以及移除赞助、关于软件、启动推广、可选更新提示和不适用于内嵌场景的独立窗口设置。必要的权限与远程控制确认保留。切换启动器页面保留组件进程，退出启动器时执行组件清理。

原项目：[MCTier](https://github.com/pmh1314520/MCTier)，原作者 MCTier Team。当前固定上游提交为 `d6af338e793941e15b777ec8abf6c9109d9591b1`。适配源码放在 `ThirdParty/MCTier`，原许可和第三方声明保留在其 `LICENSE`、`THIRD_PARTY_NOTICES.txt`、`licenses`，以及本仓库 `Licenses/MCTier`。

旧 WPF 联机页保留供回溯，已退出主导航；它使用不同的媒体与通信实现，不能作为完整原版兼容性的替代。

## 源码目录

| 目录 | 内容 |
| --- | --- |
| `BCCL` | WPF 启动器，程序集和命名空间为 BCCL |
| `BCCL.Core` | 核心库 |
| `BCCL.Core.SourceGenerators` | 源码生成器 |
| `BCCL.Core.Test` | 核心测试 |
| `ThirdParty/MCTier` | 固定版本的原版联机源码及适配 |
| `scripts` | 构建、打包及验证工具 |
| `Licenses` | 上游及第三方许可 |

解决方案入口为 `BCCL.slnx`。构建缓存放在源码目录旁的 `Temp` 中。运行数据、账号配置、日志、备份、源码归档和编译产物不作为源码上传。Markdown 文件仅保留各目录的 `README.md`。

## 构建

启动器构建使用 Windows x64、.NET 10 SDK 和 PowerShell 7；发布目标为 .NET 8 Windows Desktop，自带运行时。

完整联机组件还需要 Node.js（执行 `npm ci`）、Rust MSVC 工具链、Visual C++ 编译工具、Windows SDK、CMake、Ninja 与 libclang。`scripts/MCTier/Build-Embedded.ps1` 使用位于源码目录旁 `Temp/MCTier-embedded/Tools` 的工具布局，路径配置见脚本开头。`prepare-portable-tools.py` 可准备 MSVC/SDK 等依赖，需预先准备 Python、Rust、CMake、Ninja、libclang。该脚本不是机器全局安装程序。

在本仓库根目录运行：

```powershell
Push-Location ThirdParty/MCTier
npm ci
Pop-Location
./scripts/MCTier/Build-Embedded.ps1
./scripts/Publish-BCCL.ps1 -AllowUnconfiguredMicrosoftLogin
```

`Prepare-Assets.ps1` 从固定上游提交下载内置表情资源，并校验 SHA-256；该大文件不直接纳入本源码仓库。`Prepare-Runtime.ps1` 校验官方便携包和 EXE 后提取 EasyTier、Wintun、WinDivert。语音依赖及模型由原版准备脚本下载并校验。运行时仍保留原版组件校验。

发布默认输出到源码目录旁的 `Temp/release`，入口为 `Boat Chris Craft Launcher.exe`。交付时必须一起复制 `Extensions/Multiplayer/MCTier`，其中包含组件、资源和许可。单独复制启动器 EXE 不包含完整联机组件。

Microsoft 正版登录的公开 Client ID 可在启动器“正版登录设置”中填写，也可通过构建参数 `-MicrosoftClientId` 提供。无内置 ID 时使用上述 `-AllowUnconfiguredMicrosoftLogin`，之后在界面配置。不要提交本地 OAuth 配置、令牌或密钥。

## 验证与许可

`scripts` 下保留独立验证工具；MCTier 前端测试在 `ThirdParty/MCTier` 中运行 `npm test`。加载成功或单机测试不代表双机语音、共享、文件传输、远程控制全部通过。

启动器上游许可见根目录 `LICENSE`、`BCCL/LICENCE` 和 `Licenses`。MCTier 使用其原非商业许可；各第三方依赖遵循各自许可。本仓库的上传不改变原作者署名或许可条款。
