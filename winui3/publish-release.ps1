param(
    [ValidateSet('All', 'Full', 'Lite')]
    [string]$Target = 'All'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repoRoot = Split-Path -Parent $root
$appProject = Join-Path $root 'WindowsExtendQuickSetting\WindowsExtendQuickSetting.csproj'
$releaseRoot = Join-Path $repoRoot 'release\winui3'
$fullDir = Join-Path $releaseRoot 'full'
$liteDir = Join-Path $releaseRoot 'lite'
$buildDir = Join-Path $repoRoot 'artifacts\publish-release'
$litePayloadDir = Join-Path $buildDir 'lite-payload'
$fullPayloadDir = Join-Path $buildDir 'full-payload'
$litePayloadArchive = Join-Path $releaseRoot 'lite-payload.zip'
$fullPayloadArchive = Join-Path $buildDir 'full-payload.zip'

# The generated single-file binaries cannot be overwritten while the app is running.
Get-Process -Name 'WindowsExtendQuickSetting', 'WindowsExtendQuickSetting.App', 'WindowsExtendQuickSetting.FullApp' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path $fullDir, $liteDir, $litePayloadDir, $fullPayloadDir | Out-Null

if ($Target -in 'All', 'Full') {
    Remove-Item $fullPayloadDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $fullPayloadDir | Out-Null
    & dotnet publish $appProject -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false `
        -p:AssemblyName=WindowsExtendQuickSetting.FullApp `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $fullPayloadDir
    if ($LASTEXITCODE -ne 0) { throw 'Full publish failed.' }
    Compress-Archive -Path (Join-Path $fullPayloadDir '*') -DestinationPath $fullPayloadArchive -Force
}

if ($Target -in 'All', 'Lite') {
    Remove-Item $litePayloadDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $litePayloadDir | Out-Null

    # Lite uses the installed .NET Desktop Runtime and Windows App Runtime.
    & dotnet publish $appProject -c Release -r win-x64 --self-contained false `
        -p:WindowsAppSDKSelfContained=false `
        -p:AssemblyName=WindowsExtendQuickSetting.App `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $litePayloadDir
    if ($LASTEXITCODE -ne 0) { throw 'Lite payload publish failed.' }

    Compress-Archive -Path (Join-Path $litePayloadDir '*') -DestinationPath $litePayloadArchive -Force

}

if ($Target -in 'All', 'Full', 'Lite') {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'Visual Studio C++ Build Tools (vswhere.exe) not found.' }
    $vsInstall = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ([string]::IsNullOrWhiteSpace($vsInstall)) { throw 'Visual Studio C++ x64 build tools not found.' }
    $vcvars = Join-Path $vsInstall 'VC\Auxiliary\Build\vcvars64.bat'
    if (-not (Test-Path $vcvars)) { throw 'vcvars64.bat not found.' }
    $nativeDir = Join-Path $root 'native-launcher'
    Push-Location $nativeDir
    try {
        if ($Target -in 'All', 'Full') {
            $compile = 'call "' + $vcvars + '" >nul && rc.exe /nologo /fo full-launcher.res full-launcher.rc && cl.exe /nologo /std:c++17 /EHsc /utf-8 /DFULL_LAUNCHER /DUNICODE /D_UNICODE main.cpp full-launcher.res /link /SUBSYSTEM:WINDOWS /OUT:"' + (Join-Path $fullDir 'WindowsExtendQuickSetting.exe') + '" wininet.lib comctl32.lib shell32.lib user32.lib gdi32.lib advapi32.lib'
            & cmd.exe /d /c $compile
            if ($LASTEXITCODE -ne 0) { throw 'Full launcher build failed.' }
        }
        if ($Target -in 'All', 'Lite') {
            $compile = 'call "' + $vcvars + '" >nul && rc.exe /nologo launcher.rc && cl.exe /nologo /std:c++17 /EHsc /utf-8 /DUNICODE /D_UNICODE main.cpp launcher.res /link /SUBSYSTEM:WINDOWS /OUT:"' + (Join-Path $liteDir 'WindowsExtendQuickSetting.Lite.exe') + '" wininet.lib comctl32.lib shell32.lib user32.lib gdi32.lib advapi32.lib'
            & cmd.exe /d /c $compile
            if ($LASTEXITCODE -ne 0) { throw 'Lite launcher build failed.' }
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Full: $(Join-Path $fullDir 'WindowsExtendQuickSetting.exe')"
Write-Host "Lite: $(Join-Path $liteDir 'WindowsExtendQuickSetting.Lite.exe')"
