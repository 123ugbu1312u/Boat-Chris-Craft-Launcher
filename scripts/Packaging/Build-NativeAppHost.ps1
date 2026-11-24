param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$HostingPack
)

$ErrorActionPreference = 'Stop'
$compiler = Get-Command cl.exe -CommandType Application -ErrorAction SilentlyContinue
if ($compiler) {
    $compilerPath = $compiler.Source
} else {
    $tools = Join-Path $PSScriptRoot '../../../Temp/MCTier-embedded/Tools'
    $compilerRoot = Join-Path $tools 'msvc/VC/Tools/MSVC'
    $vc = Get-ChildItem -LiteralPath $compilerRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (!$vc) {
        throw 'Use a Visual Studio x64 developer shell or prepare the portable tools with scripts/MCTier/prepare-portable-tools.py.'
    }
    $sdkInclude = Join-Path $tools 'sdk/microsoft.windows.sdk.cpp/c/Include/10.0.26100.0'
    $sdkLib = Join-Path $tools 'sdk/microsoft.windows.sdk.cpp.x64/c'
    $compilerPath = Join-Path $vc.FullName 'bin/Hostx64/x64/cl.exe'
    $env:INCLUDE = "$($vc.FullName)/include;$sdkInclude/ucrt;$sdkInclude/shared;$sdkInclude/um"
    $env:LIB = "$($vc.FullName)/lib/x64;$sdkLib/ucrt/x64;$sdkLib/um/x64"
}

$hostingLibrary = Join-Path $HostingPack 'libnethost.lib'
if (!(Test-Path -LiteralPath $hostingLibrary -PathType Leaf)) {
    throw "The .NET apphost pack is missing its static hosting library at $hostingLibrary."
}

New-Item -ItemType Directory -Path (Split-Path $Output -Parent) -Force | Out-Null
$obj = [IO.Path]::ChangeExtension($Output, '.obj')
$options = @(
    '/nologo', '/std:c++17', '/utf-8', '/EHsc', '/O2', '/MT', '/DNETHOST_USE_AS_STATIC', "/I$HostingPack",
    "/Fo$obj", "/Fe$Output", $Source, $hostingLibrary, '/link', '/MACHINE:X64', '/SUBSYSTEM:CONSOLE',
    'Shell32.lib', 'User32.lib', 'Advapi32.lib', 'Ole32.lib'
)
& $compilerPath @options
if ($LASTEXITCODE -ne 0) {
    throw "Native apphost compilation failed (exit $LASTEXITCODE)."
}
