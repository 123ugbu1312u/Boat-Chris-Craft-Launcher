# Boat Chris Craft Launcher

**BCCL Alpha 0.2.2**，Windows Minecraft 启动器。

个性化提供 13 种常驻配色，浅色与深色模式可分别选择；原“龙猫蓝”显示为“PCL蓝”，已有配色设置继续沿用。联机原生窗口进入时使用 240 毫秒的轻微上移过渡，切换页面保留后台会话和预加载，液态玻璃效果及选项不变。

- [GitHub](https://github.com/123ugbu1312u/Boat-Chris-Craft-Launcher)
- [GitCode](https://gitcode.com/BoatMinecraft/BoatChrisCraftLauncher)

支持游戏安装、实例管理、Java 与账号配置、模组资源下载，以及跟随启动器主题的 Liquid Glass 界面。

## 下载

下载导航分为可独立折叠的自动安装、社区资源、Java 下载器和手动安装四个板块。Java 下载器在线合并 Adoptium 版本目录与 Oracle 下载页中的版本信息，选择版本和父目录后，再确认 Oracle（默认）或 OpenJDK（Eclipse Temurin）。完整 Windows x64 JDK 校验 SHA-256 后解压到 `Java版本号/bin`、`Java版本号/lib` 等目录，保留许可证，不覆盖已有安装；支持取消并清理临时文件，安装成功后添加到 Java 管理。

Oracle 需要官网账号的版本会提供官网入口或 OpenJDK 选择。版本出现在目录中不代表两家均已提供该版本的免登录 Windows ZIP，暂未提供时会显示错误并允许重试。官方来源：[Oracle](https://www.oracle.com/java/technologies/downloads/)、[Adoptium API](https://api.adoptium.net/q/swagger-ui/)。验证入口为 `scripts/JavaDownloadVerification`。

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

发布默认输出到源码目录旁的 `Temp/release`，入口为 `Boat Chris Craft Launcher.exe`。发布脚本将完整联机程序、资源、驱动和许可一起封装到启动器内，只需复制这一个 EXE。首次打开联机时自动准备到用户本地缓存 `BCCL/Multiplayer/Runtime`，不需要外置 `Extensions` 文件夹，也不在首次运行时下载这些组件；后续校验并复用缓存，文件损坏时从 EXE 内置资源修复。运行数据和缓存仍会按正常软件行为写入磁盘。

表情包沿用原版联机程序内的 Windows 资源，不在外层重复封装。打包前校验内置副本的长度和 SHA-256 与原资源一致；网络引擎、驱动、语音模型、表情内容和许可均保留。启动器启用 .NET 单文件无损压缩，仍为完整自带运行时的版本，不启用程序集裁剪。发布脚本默认要求最终 EXE 小于 250,000,000 字节，不通过裁剪功能或首次联网下载来缩小文件。后台预加载、液态玻璃的折射、色散、动画及全部设置选项保持原有实现。

BCCL 主界面首次显示后，在后台准备并预加载联机。原生窗口保持隐藏，页面完成初始化和主题同步后才允许嵌入显示；加载底色跟随启动器，避免白底、黑底交替。预加载与提前点击联机共用同一次启动，自动加入大厅仅在进入联机页后触发，退出 BCCL 同时清理后台组件。

Microsoft 正版登录的公开 Client ID 可在启动器“正版登录设置”中填写，也可通过构建参数 `-MicrosoftClientId` 提供。无内置 ID 时使用上述 `-AllowUnconfiguredMicrosoftLogin`，之后在界面配置。不要提交本地 OAuth 配置、令牌或密钥。

## 验证与许可

`scripts` 下保留独立验证工具；MCTier 前端测试在 `ThirdParty/MCTier` 中运行 `npm test`。加载成功或单机测试不代表双机语音、共享、文件传输、远程控制全部通过。

单文件包启用完整运行依赖解压，确保 WPF 提示框所需的 Accessibility 与延迟加载库具有稳定的磁盘路径；首次运行会准备缓存。`scripts/Packaging/Test-PopupPackage.ps1` 在无外置 DLL、全新解压缓存、无已安装运行时路径的目录内验证程序集加载及透明 Popup，并覆盖 EXE 被重命名后的延迟加载。该测试通过 `--verify-popup` 入口执行，不启动账号、配置或启动器后台服务。

启动器上游许可见根目录 `LICENSE`、`BCCL/LICENCE` 和 `Licenses`。MCTier 使用其原非商业许可；各第三方依赖遵循各自许可。本仓库的上传不改变原作者署名或许可条款。
