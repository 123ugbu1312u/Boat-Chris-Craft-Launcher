param()
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination = Join-Path $sourceRoot 'ThirdParty/MCTier/shared/builtin-emoji/builtin-v3.pack.gz'
$expectedHash = '2F538E09F787AB7DFAC69120BE1A7DDF42FF38C4D8521A62AA445A5DD91882F6'
if ((Test-Path -LiteralPath $destination -PathType Leaf) -and
    (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $expectedHash) {
    Write-Output 'Original built-in emoji resource verified.'
    return
}
New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
$pending = "$destination.partial"
$url = 'https://raw.githubusercontent.com/pmh1314520/MCTier/d6af338e793941e15b777ec8abf6c9109d9591b1/shared/builtin-emoji/builtin-v3.pack.gz'
Invoke-WebRequest -Uri $url -OutFile $pending
if ((Get-FileHash -LiteralPath $pending -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Original built-in emoji resource checksum mismatch; the download was not installed.'
}
Move-Item -LiteralPath $pending -Destination $destination -Force
Write-Output 'Original built-in emoji resource downloaded and verified.'
