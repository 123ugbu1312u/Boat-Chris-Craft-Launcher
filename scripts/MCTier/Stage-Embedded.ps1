param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$root = Split-Path $sourceRoot -Parent
$source = Join-Path $sourceRoot 'ThirdParty/MCTier'
$built = Join-Path $root 'Temp/MCTier-embedded/target/release/mctier.exe'
$stampPath = Join-Path $root 'Temp/MCTier-embedded/embedded-build.json'
if (!(Test-Path -LiteralPath $stampPath)) { throw 'Build the original embedded MCTier component first: scripts/MCTier/Build-Embedded.ps1' }
$stamp = Get-Content -LiteralPath $stampPath -Raw | ConvertFrom-Json
if ($stamp.sha256 -ne (Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash) { throw 'MCTier build checksum mismatch' }
$destination = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'Extensions/Multiplayer/MCTier'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $built -Destination (Join-Path $destination 'MCTier.exe') -Force
Copy-Item -LiteralPath $stampPath -Destination (Join-Path $destination 'embedded-build.json') -Force
$licenses = Join-Path $destination 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination $licenses -Force
Copy-Item -LiteralPath (Join-Path $source 'THIRD_PARTY_NOTICES.txt') -Destination $licenses -Force
Get-ChildItem -LiteralPath (Join-Path $source 'licenses') -File | Copy-Item -Destination $licenses -Force
Copy-Item -LiteralPath (Join-Path $source 'patches') -Destination $licenses -Recurse -Force
# Original resources are still included in the executable; materialize their normal bundle paths too.
$emoji = Join-Path $destination 'builtin-emoji'
New-Item -ItemType Directory -Path $emoji -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'shared/builtin-emoji/builtin-v3.pack.gz') -Destination $emoji -Force
$runtime = Join-Path $destination 'runtime'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
foreach ($file in @('easytier-core.exe', 'easytier-cli.exe', 'wintun.dll', 'WinDivert64.sys')) {
    Copy-Item -LiteralPath (Join-Path $source "src-tauri/resources/binaries/$file") -Destination $runtime -Force
}
Get-FileHash -LiteralPath (Join-Path $destination 'MCTier.exe') -Algorithm SHA256
