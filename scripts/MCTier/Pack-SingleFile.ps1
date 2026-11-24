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
& (Join-Path $PSScriptRoot 'Pack-CompactPayload.ps1') -RuntimeDirectory $runtime -OutputDirectory $work
