param(
    [string]$Executable = (Join-Path $PSScriptRoot '../../Boat Chris Craft Launcher.exe'),
    [string]$TestDirectory = (Join-Path $PSScriptRoot '../../Temp/独立运行验证 with spaces')
)

$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$testPath = [System.IO.Path]::GetFullPath($TestDirectory)
New-Item -ItemType Directory -Path $testPath -Force | Out-Null
$testExe = Join-Path $testPath 'Boat Chris Craft Launcher.exe'
Copy-Item -LiteralPath $executablePath -Destination $testExe -Force
if (@(Get-ChildItem -LiteralPath $testPath -Filter '*.dll' -File -Recurse).Count -ne 0) {
    throw 'The test folder must have no external DLLs.'
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new($testExe)
# The existing --gpu command without a path exits with Fail (1), before any
# GPU setting is written or main window is created. This exercises real WPF
# resource loading and lifecycle startup without desktop input or settings edits.
$startInfo.ArgumentList.Add('--gpu')
$startInfo.WorkingDirectory = [System.IO.Path]::GetTempPath()
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$missingRuntime = Join-Path $testPath 'no-installed-dotnet'
$startInfo.Environment['DOTNET_ROOT'] = $missingRuntime
$startInfo.Environment['DOTNET_ROOT_X64'] = $missingRuntime
$startInfo.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$startInfo.Environment['DOTNET_DISABLE_GUI_ERRORS'] = '1'
# Force fresh native-runtime extraction so a previous single-file build's
# cached DLLs cannot mask an incomplete or broken compressed package.
$startInfo.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $testPath 'bundle-cache'

$process = [System.Diagnostics.Process]::Start($startInfo)
try {
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(30000)) {
        $process.Kill()
        throw 'Package startup timed out.'
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    Set-Content -LiteralPath (Join-Path $testPath 'stdout.log') -Value $stdout -Encoding utf8
    Set-Content -LiteralPath (Join-Path $testPath 'stderr.log') -Value $stderr -Encoding utf8
    if ($process.ExitCode -ne 1 -or ![string]::IsNullOrWhiteSpace($stderr)) {
        throw "Unexpected package result: exit=$($process.ExitCode), stderr=$stderr"
    }
    $startupLog = Get-ChildItem -LiteralPath (Join-Path $testPath 'BCCL/Log') -File |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$startupLog -or !(Select-String -LiteralPath $startupLog.FullName -SimpleMatch '状态改变: Loaded' -Quiet)) {
        throw 'The package did not finish WPF application initialization.'
    }
    if (Select-String -LiteralPath $startupLog.FullName -Pattern '\[FATAL\]|未捕获的异常' -Quiet) {
        throw 'A fatal startup exception was recorded.'
    }
    $legacyDirectoryName = [string]::new([char[]]@(80, 67, 76))
    if (Test-Path -LiteralPath (Join-Path $testPath $legacyDirectoryName)) {
        throw 'The launcher left or recreated the legacy data directory.'
    }
    Write-Output 'PASS: standalone EXE reached Loaded and handled --gpu without a path (exit 1).'
    Write-Output 'PASS: Chinese/spaced path, different working directory, no adjacent DLLs, no DOTNET_ROOT runtime.'
    Get-FileHash -LiteralPath $testExe -Algorithm SHA256
}
finally {
    $process.Dispose()
}
# --gpu intentionally exits 1 above; successful verification should return 0 to callers.
exit 0
