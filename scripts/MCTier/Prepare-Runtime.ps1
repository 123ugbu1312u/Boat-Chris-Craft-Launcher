param([string]$OriginalExecutable)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$root = Split-Path $sourceRoot -Parent
$cache = Join-Path $root 'Temp/MCTier-embedded/Upstream'
$officialHash = '3647E5A919890EA3208DEB95DA7FA559FC08CA5B337A97F189AEDDADCB562466'
if (!$OriginalExecutable) {
    $existing = Join-Path $root 'Temp/BCCL-Beta-0.1.0/Extensions/MCTier.exe'
    if (Test-Path -LiteralPath $existing) { $OriginalExecutable = $existing }
    else {
        New-Item -ItemType Directory -Force -Path $cache | Out-Null
        $archive = Join-Path $cache 'MCTier-Portable.7z'
        if (!(Test-Path -LiteralPath $archive)) {
            Invoke-WebRequest 'https://github.com/pmh1314520/MCTier/releases/download/v3.10.0/MCTier-Portable.7z' -OutFile $archive
        }
        if ((Get-FileHash -LiteralPath $archive).Hash -ne '25BFB0917A77BFB66A8F7D4AA0BBE81DF81D18BDB59B695FFD6DC4D4312E0272') { throw 'Original archive checksum mismatch' }
        & tar -xf $archive -C $cache 'MCTier.exe'
        if ($LASTEXITCODE) { throw 'Unable to read the original portable archive' }
        $OriginalExecutable = Join-Path $cache 'MCTier.exe'
    }
}
if ((Get-FileHash -LiteralPath $OriginalExecutable).Hash -ne $officialHash) { throw 'Expected the official MCTier 3.10.0 portable executable' }
# Read the original include_bytes payloads, without running or modifying the application.
$image = [IO.File]::ReadAllBytes($OriginalExecutable)
$entries = @(
    @('easytier-core.exe', 51304816, 15838208, 'B6418DE6BDA2D63A408F6A8802809BFACB460ED35F86A374F02083A7825B89D2'),
    @('easytier-cli.exe', 47109488, 4195328, '7D9A9FE3899816B2356D6C0219CE504732C3125B03449911B9F0438048B93376'),
    @('wintun.dll', 46681936, 427552, 'E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE'),
    @('WinDivert64.sys', 67143024, 94144, '8DA085332782708D8767BCACE5327A6EC7283C17CFB85E40B03CD2323A90DDC2')
)
$destination = Join-Path $sourceRoot 'ThirdParty/MCTier/src-tauri/resources/binaries'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($entry in $entries) {
    $bytes = New-Object byte[] $entry[2]
    [Buffer]::BlockCopy($image, $entry[1], $bytes, 0, $bytes.Length)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($bytes)).Replace('-', '') }
    finally { $algorithm.Dispose() }
    if ($hash -ne $entry[3]) { throw "Original runtime checksum mismatch: $($entry[0])" }
    $path = Join-Path $destination $entry[0]
    if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $hash) {
        [IO.File]::WriteAllBytes($path, $bytes)
    }
    Write-Output "$($entry[0]): original bundled bytes verified ($hash)"
}
