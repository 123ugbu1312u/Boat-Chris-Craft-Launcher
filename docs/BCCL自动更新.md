# BCCL 自动更新与发布

启动器从公开仓库 `BoatMinecraft/BoatChrisCraftLauncher` 的 GitCode 发行版检查更新，包含预发行版。它要求附件名严格为 `Boat Chris Craft Launcher.exe`，并使用 GitCode 返回的 SHA-256 校验下载文件。失败时不替换现有启动器。

当前程序显示版本为 `0.0.1 Alpha`，内部版本号为 `0.0.1-alpha`。GitCode 发行版 Tag 与内部版本号必须一致。

## 发布当前版本

1. 在 GitCode 仓库创建发行版，Tag 填写 `v0.0.1-alpha`，标题可填写 `BCCL v0.0.1 Alpha`。
2. 上传本次构建的 `Boat Chris Craft Launcher.exe`，等待上传完成；测试版可以勾选预发行版。
3. 发布后，在未登录的浏览器中确认附件可以下载。检查发行版 API 中该附件存在 `sha256:` 开头的 `digest`。
4. 通知使用旧版的用户手动下载并替换一次；后续版本才可自动更新。

## 发布以后版本

1. 修改 `BCCL/Modules/Updates/UpdateManager.cs` 的 `BcclVersion`，例如 `0.0.3-alpha`。版本必须比当前版本高，遵循 `主版本.次版本.修订号`，可附加 `-alpha` 等后缀。
2. 构建和测试启动器。使用 `scripts/Publish-BCCL.ps1` 生成单文件版本；正版登录 Client ID 的设置见 `docs/Microsoft-login.md`。
3. 创建对应的 GitCode Tag，例如 `v0.0.3-alpha`，上传文件名为 `Boat Chris Craft Launcher.exe` 的构建产物。Tag 与程序内部版本必须一致。
4. 发布后，先用测试目录中的旧版验证：检查到新版本、下载、哈希校验以及关闭后替换和下次启动。再通知用户。

启动器默认自动下载，并在退出时安装；以前更改过更新模式的用户可在“设置 → 软件更新”自行调整。只有一个 x64 安装包，现阶段所有发行版（含预发行版）使用同一个更新源。GitCode 可访问性因网络环境而异。
