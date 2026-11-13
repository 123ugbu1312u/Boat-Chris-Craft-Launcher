param([switch]$SkipFrontend, [switch]$FetchOnly)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$root = Split-Path $sourceRoot -Parent
$work = Join-Path $root 'Temp/MCTier-embedded'
$tools = Join-Path $work 'Tools'
$source = Join-Path $sourceRoot 'ThirdParty/MCTier'
$env:RUSTUP_HOME = Join-Path $tools 'rustup'
$env:CARGO_HOME = Join-Path $tools 'cargo'
$env:CARGO_TARGET_DIR = Join-Path $work 'target'
$vc = (Get-ChildItem (Join-Path $tools 'msvc/VC/Tools/MSVC') -Directory | Select-Object -First 1).FullName
$sdk = Join-Path $tools 'sdk/microsoft.windows.sdk.cpp/c'
$sdkLib = Join-Path $tools 'sdk/microsoft.windows.sdk.cpp.x64/c'
$vcBin = Join-Path $vc 'bin/Hostx64/x64'
$sdkBin = Join-Path $sdk 'bin/10.0.26100.0/x64'
$env:PATH = "$vcBin;$sdkBin;$tools/cargo/bin;$tools/python/cmake/data/bin;$tools/python/bin;$env:PATH"
$env:INCLUDE = "$vc/include;$sdk/Include/10.0.26100.0/ucrt;$sdk/Include/10.0.26100.0/shared;$sdk/Include/10.0.26100.0/um;$sdk/Include/10.0.26100.0/winrt"
$env:LIB = "$vc/lib/x64;$sdkLib/ucrt/x64;$sdkLib/um/x64"
$env:VCToolsInstallDir = "$vc/"
$env:VSINSTALLDIR = "$tools/msvc/"
$env:WindowsSdkDir = "$sdk/"
$env:WindowsSDKVersion = '10.0.26100.0/'
$env:CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_LINKER = Join-Path $vcBin 'link.exe'
$env:CC_x86_64_pc_windows_msvc = Join-Path $vcBin 'cl.exe'
$env:AR_x86_64_pc_windows_msvc = Join-Path $vcBin 'lib.exe'
$env:LIBCLANG_PATH = Join-Path $tools 'python/clang/native'
$env:CMAKE_GENERATOR = 'Ninja'
$env:CMAKE_POLICY_VERSION_MINIMUM = '3.5'
$env:CMAKE = Join-Path $tools 'python/cmake/data/bin/cmake.exe'
$env:VITE_BCCL_EMBEDDED = '1'
$env:HUSKY = '0'
Push-Location $source
try {
    if ($FetchOnly) { cargo fetch --locked; if ($LASTEXITCODE) { throw 'Cargo fetch failed' }; return }
    & (Join-Path $PSScriptRoot 'Prepare-Assets.ps1')
    if (!$SkipFrontend) { npm run build; if ($LASTEXITCODE) { throw 'MCTier frontend build failed' } }
    & (Join-Path $PSScriptRoot 'Prepare-Runtime.ps1')
    $binaries = Join-Path $source 'src-tauri/resources/binaries'
    foreach ($file in @('easytier-core.exe', 'easytier-cli.exe', 'wintun.dll', 'WinDivert64.sys')) {
        if (!(Test-Path (Join-Path $binaries $file))) { throw "Missing original runtime resource: $file" }
    }
    & (Join-Path $source 'scripts/prepare-sherpa-windows.ps1') -CargoTargetDirectory $env:CARGO_TARGET_DIR
    Push-Location 'src-tauri'
    try { cargo build --release --locked --features tauri/custom-protocol; if ($LASTEXITCODE) { throw 'MCTier native build failed' } }
    finally { Pop-Location }
    [ordered]@{
        upstream = 'https://github.com/pmh1314520/MCTier'
        upstreamCommit = 'd6af338e793941e15b777ec8abf6c9109d9591b1'
        upstreamVersion = '3.10.0'
        adaptation = 'BCCL-original-embedded-v1'
        sha256 = (Get-FileHash -LiteralPath (Join-Path $env:CARGO_TARGET_DIR 'release/mctier.exe') -Algorithm SHA256).Hash
        builtAt = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'embedded-build.json') -Encoding utf8
} finally { Pop-Location }
