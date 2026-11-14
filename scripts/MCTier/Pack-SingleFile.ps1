param()
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$work = Join-Path (Split-Path $sourceRoot -Parent) ('Temp/MCTier-embedded/SingleFile/' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $work 'stage'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
& (Join-Path $PSScriptRoot 'Stage-Embedded.ps1') -OutputDirectory $stage | Out-Null
$runtime = Join-Path $stage 'Extensions/Multiplayer/MCTier'
# MCTier already contains this exact pack as Windows resource type 11 and uses
# that resource when the standalone file is absent. Verify before deduplicating.
if (-not ('BCCL.Build.Multiplayer.EmbeddedEmojiVerifier' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'EmbeddedEmojiVerifier.cs')
}
$embeddedEmojiHash = [BCCL.Build.Multiplayer.EmbeddedEmojiVerifier]::Verify(
    (Join-Path $runtime 'MCTier.exe'), (Join-Path $runtime 'builtin-emoji/builtin-v3.pack.gz'))
Write-Host "Verified embedded emoji pack; omitting its duplicate: $embeddedEmojiHash"
$archivePath = Join-Path $work 'multiplayer.zip'
$manifestPath = Join-Path $work 'multiplayer.json'
$files = @()
Add-Type -AssemblyName System.IO.Compression
$archiveStream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try {
    $archive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $runtime -File -Recurse | Sort-Object FullName)) {
            $relative = [IO.Path]::GetRelativePath($runtime, $file.FullName).Replace('\', '/')
            if ($relative -eq 'builtin-emoji/builtin-v3.pack.gz') { continue }
            if ($relative.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase) -and $file.Name -cne 'README.md') {
                throw "Unexpected Markdown in runtime payload: $relative"
            }
            $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2020, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $inputStream = [IO.File]::OpenRead($file.FullName)
            try {
                $entryStream = $entry.Open()
                try { $inputStream.CopyTo($entryStream) } finally { $entryStream.Dispose() }
            } finally { $inputStream.Dispose() }
            $files += [ordered]@{ path=$relative; length=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
        }
    } finally { $archive.Dispose() }
} finally { $archiveStream.Dispose() }
$packageHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
[ordered]@{ schemaVersion=1; packageSha256=$packageHash; entryPoint='MCTier.exe'; files=$files } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
[pscustomobject]@{ Archive=$archivePath; Manifest=$manifestPath; Sha256=$packageHash; FileCount=$files.Count }
