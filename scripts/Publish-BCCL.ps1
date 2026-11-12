param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../Temp/release'),
    [string]$MicrosoftClientId = $env:BCCL_MS_CLIENT_ID,
    [string]$MicrosoftOAuthConfig = (Join-Path $PSScriptRoot 'MicrosoftOAuth.local.json'),
    [switch]$AllowUnconfiguredMicrosoftLogin
)

$ErrorActionPreference = 'Stop'
# Fail before publishing a launcher whose Microsoft login cannot start.
# This value is a public application ID, never an OAuth client secret.
if ([string]::IsNullOrWhiteSpace($MicrosoftClientId) -and (Test-Path -LiteralPath $MicrosoftOAuthConfig -PathType Leaf)) {
    $oauthConfig = Get-Content -LiteralPath $MicrosoftOAuthConfig -Raw | ConvertFrom-Json
    $MicrosoftClientId = [string]$oauthConfig.clientId
}
if ([string]::IsNullOrWhiteSpace($MicrosoftClientId) -and !$AllowUnconfiguredMicrosoftLogin) {
    throw 'Microsoft 登录 Client ID 未配置。请使用 -MicrosoftClientId <应用程序ID>，或填写 scripts/MicrosoftOAuth.local.json；说明见 docs/Microsoft-login.md。'
}
if (![string]::IsNullOrWhiteSpace($MicrosoftClientId)) {
$clientGuid = [Guid]::Empty
if (![Guid]::TryParse($MicrosoftClientId.Trim(), [ref]$clientGuid) -or $clientGuid -eq [Guid]::Empty) {
    throw 'MicrosoftClientId 必须是 Microsoft Entra 应用注册页提供的有效应用程序（客户端）ID。'
}
$MicrosoftClientId = $clientGuid.ToString('D')
} else {
    Write-Warning '此包未内置 Microsoft Client ID，正版登录需在启动器的“正版登录设置”中配置。'
}
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repositoryRoot 'BCCL/BCCL.csproj'
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
# Always publish into a fresh folder. Stale hostfxr/coreclr DLLs can make a
# framework-dependent app search the wrong location for the installed runtime.
$temporaryRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot '../Temp'))
$publishPath = Join-Path $temporaryRoot ('publish-' + [Guid]::NewGuid().ToString('N'))

& dotnet publish $project -c Release -r win-x64 --self-contained true `
    "-p:MicrosoftOAuthClientId=$MicrosoftClientId" `
    -p:PublishProfile=BCCL -o $publishPath --nologo -v minimal `
    '-clp:ErrorsOnly;Summary'
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

$publishedExe = Join-Path $publishPath 'BCCL.exe'
if (!(Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw 'The single-file executable was not generated.'
}
$externalLibraries = @(Get-ChildItem -LiteralPath $publishPath -Filter '*.dll' -Recurse -File)
if ($externalLibraries.Count -gt 0) {
    throw 'Publish left external DLLs. Refusing to ship an incomplete standalone EXE.'
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$destination = Join-Path $outputPath 'Boat Chris Craft Launcher.exe'
Copy-Item -LiteralPath $publishedExe -Destination $destination -Force
# Rename only the apphost; the bundled assembly name must match WPF pack URIs.
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $outputPath 'LICENSE-UPSTREAM.txt') -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'BCCL/LICENCE') -Destination (Join-Path $outputPath 'LICENCE-UPSTREAM.txt') -Force
Get-FileHash -LiteralPath $destination -Algorithm SHA256
Get-Item -LiteralPath $destination | Select-Object FullName, Length
