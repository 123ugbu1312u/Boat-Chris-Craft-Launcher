param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$TestDirectory
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $Executable).Path
$root = [IO.Path]::GetFullPath($TestDirectory)
if (Test-Path -LiteralPath $root) { throw 'Choose a new test directory to force fresh extraction.' }
New-Item -ItemType Directory -Path $root | Out-Null
$exe = Join-Path $root 'BCCL.exe'
Copy-Item -LiteralPath $source -Destination $exe
$signals = Join-Path $root 'signals'
$start = [Diagnostics.ProcessStartInfo]::new($exe)
$start.ArgumentList.Add('--verify-popup')
$start.ArgumentList.Add($signals)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['DOTNET_ROOT'] = Join-Path $root 'no-installed-runtime'
$start.Environment['DOTNET_ROOT_X64'] = Join-Path $root 'no-installed-runtime'
$start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $root 'runtime-cache'
$process = [Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while (!(Test-Path -LiteralPath (Join-Path $signals 'ready'))) {
        if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Package did not reach the popup test entry point.' }
        Start-Sleep -Milliseconds 100
    }
    # Exercise late loading after the executable is renamed. Only this isolated
    # test copy moves; all assemblies must remain available from the cache.
    Move-Item -LiteralPath $exe -Destination (Join-Path $root 'renamed.exe')
    New-Item -ItemType File -Path (Join-Path $signals 'continue') | Out-Null
    if (!$process.WaitForExit(30000)) { throw 'Popup package test timed out.' }
    $output = $stdout.GetAwaiter().GetResult()
    $errors = $stderr.GetAwaiter().GetResult()
    Write-Output $output
    if ($process.ExitCode -ne 0 -or $errors) { throw "Popup test failed: exit=$($process.ExitCode), stderr=$errors" }
    if (@(Get-ChildItem -LiteralPath $root -Filter '*.dll' -File).Count -gt 0) { throw 'Package depended on adjacent DLLs.' }
    Write-Output 'PASS: fresh single-file extraction, no adjacent DLLs, no installed runtime, late loading after EXE rename.'
} finally {
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
