param(
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$PreviousManifest
)
$ErrorActionPreference = 'Stop'
$runtime = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$archivePath = Join-Path $output 'multiplayer.zip'
$manifestPath = Join-Path $output 'multiplayer.json'
$compressed = Join-Path $output 'MCTier.exe.br'
$prior = if ($PreviousManifest) { Get-Content -LiteralPath $PreviousManifest -Raw | ConvertFrom-Json }
$files = @(Get-ChildItem -LiteralPath $runtime -File -Recurse | Sort-Object FullName | ForEach-Object {
    $name = [IO.Path]::GetRelativePath($runtime, $_.FullName).Replace('\', '/')
    if ($name -eq 'builtin-emoji/builtin-v3.pack.gz') { return }
    if ($name.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase) -and $_.Name -cne 'README.md') {
        throw "Unexpected Markdown in runtime payload: $name"
    }
    [ordered]@{
        path = $name
        length = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
})

if ($prior) {
    if ($prior.schemaVersion -ne 1 -or $prior.files.Count -ne $files.Count) { throw 'Unexpected previous manifest.' }
    $expected = @{}
    foreach ($file in $prior.files) { $expected.Add($file.path, $file) }
    foreach ($file in $files) {
        if (!$expected.ContainsKey($file.path) -or $expected[$file.path].length -ne $file.length -or
            $expected[$file.path].sha256 -ne $file.sha256) {
            throw "Repacking changed a multiplayer file: $($file.path)"
        }
    }
}

$offsetsJson = & node (Join-Path $PSScriptRoot 'Compress-EntryPoint.mjs') $runtime $compressed
if ($LASTEXITCODE -ne 0) { throw 'Compact MCTier compression failed.' }
$offsets = $offsetsJson | ConvertFrom-Json

Add-Type -AssemblyName System.IO.Compression
$archiveStream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try {
    $archive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $files) {
            if ($offsets.PSObject.Properties[$file.path]) {
                $file.entryPointOffset = $offsets.PSObject.Properties[$file.path].Value
                continue
            }

            $entryName = $file.path
            $inputPath = Join-Path $runtime $file.path
            $compression = [IO.Compression.CompressionLevel]::Optimal
            if ($file.path -eq 'MCTier.exe') {
                $file.brotli = $true
                $entryName += '.br'
                $inputPath = $compressed
                $compression = [IO.Compression.CompressionLevel]::NoCompression
            }

            $entry = $archive.CreateEntry($entryName, $compression)
            $entry.LastWriteTime = [DateTimeOffset]::new(2020, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $source = [IO.File]::OpenRead($inputPath)
            try {
                $target = $entry.Open()
                try { $source.CopyTo($target) } finally { $target.Dispose() }
            } finally { $source.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $archiveStream.Dispose() }

$packageHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
$manifest = [ordered]@{ schemaVersion = 2; packageSha256 = $packageHash; entryPoint = 'MCTier.exe'; files = $files }
if ($prior) { $manifest.compatiblePackageSha256 = $prior.packageSha256 }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
[pscustomobject]@{ Archive = $archivePath; Manifest = $manifestPath; Sha256 = $packageHash; FileCount = $files.Count }
