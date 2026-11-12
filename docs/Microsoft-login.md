# Client ID 正版登录

BCCL 使用所提供的 Microsoft 应用 Client ID 进行设备代码授权。账号仍需在 Microsoft 网页上由本人授权，并通过 Xbox、Minecraft Java 版许可与玩家档案核验。

## 在启动器中配置

1. 选择“添加新的玩家 → 正版验证 → 正版登录设置”。
2. 填入应用程序（客户端）ID 并保存。
3. 点击“开始正版验证”，按界面提示完成 Microsoft 网页授权。

Client ID 保存在启动器同目录 `BCCL/MicrosoftOAuth.json` 中，格式为 `{"clientId":"应用程序GUID"}`。只需公开 Client ID，不需要 client secret。无效或全零 GUID 不会覆盖已有配置；已有配置文件损坏时可从设置入口重新填写。

运行时读取顺序为 `BCCL_MS_CLIENT_ID` 环境变量 → 本地配置文件 → 构建内置 ID。没有 ID 时显示设置入口；该版本不读取官方启动器缓存。

## 内置到发布包

收到 Client ID 后，可复制 `scripts/MicrosoftOAuth.example.json` 为 `scripts/MicrosoftOAuth.local.json`，填入 `clientId`，然后运行：

```powershell
./scripts/Publish-BCCL.ps1
```

也可以直接传参：

```powershell
./scripts/Publish-BCCL.ps1 -MicrosoftClientId '<应用程序GUID>'
```

暂未收到 ID、需要先交付可配置版本时使用 `-AllowUnconfiguredMicrosoftLogin`。该包启动后仍可从“正版登录设置”填写 ID，无需重新编译。

应用需支持个人 Microsoft 账户、公共客户端设备代码授权，以及登录所需的 Xbox/Minecraft 服务访问。更换应用 ID 后，旧的刷新凭据可能需要重新授权。

## 验证

```powershell
dotnet test BCCL.Core.Test/BCCL.Core.Test.csproj -c Release --filter "FullyQualifiedName~MicrosoftOAuthConfigurationTest|FullyQualifiedName~MicrosoftLoginRegressionTest|FullyQualifiedName~ProfileManagementTest"
./scripts/Test-BCCLPackage.ps1 -Executable '../Temp/release/Boat Chris Craft Launcher.exe'
```

回归使用合成应用 ID 和模拟网络响应。收到实际 Client ID 后，仍需通过真实账号的端到端授权确认服务准入。
