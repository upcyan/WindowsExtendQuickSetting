param(
    [ValidateSet('All', 'Full', 'Lite')]
    [string]$Target = 'All'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$appProject = Join-Path $root 'WindowsExtendQuickSetting\WindowsExtendQuickSetting.csproj'
$fullDir = Join-Path $root 'release\full'
$liteDir = Join-Path $root 'release\lite'
$payloadArchive = Join-Path $root 'release\lite-payload.zip'
$buildDir = Join-Path $root 'artifacts\publish-release'
$litePayloadDir = Join-Path $buildDir 'lite-payload'

# The generated single-file binaries cannot be overwritten while the app is running.
Get-Process -Name 'WindowsExtendQuickSetting', 'WindowsExtendQuickSetting.App' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path $fullDir, $liteDir, $litePayloadDir | Out-Null

if ($Target -in 'All', 'Full') {
    & dotnet publish $appProject -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $fullDir
    if ($LASTEXITCODE -ne 0) { throw 'Full publish failed.' }
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

    Compress-Archive -Path (Join-Path $litePayloadDir '*') -DestinationPath $payloadArchive -Force

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'Visual Studio C++ Build Tools (vswhere.exe) not found.' }
    $vsInstall = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ([string]::IsNullOrWhiteSpace($vsInstall)) { throw 'Visual Studio C++ x64 build tools not found.' }
    $vcvars = Join-Path $vsInstall 'VC\Auxiliary\Build\vcvars64.bat'
    if (-not (Test-Path $vcvars)) { throw 'vcvars64.bat not found.' }

    $nativeDir = Join-Path $root 'native-launcher'
    Push-Location $nativeDir
    try {
        $compile = 'call "' + $vcvars + '" >nul && rc.exe /nologo launcher.rc && cl.exe /nologo /std:c++17 /EHsc /utf-8 /DUNICODE /D_UNICODE main.cpp launcher.res /link /SUBSYSTEM:WINDOWS /OUT:"' + (Join-Path $liteDir 'WindowsExtendQuickSetting.Lite.exe') + '" wininet.lib comctl32.lib shell32.lib user32.lib gdi32.lib advapi32.lib'
        & cmd.exe /d /c $compile
        if ($LASTEXITCODE -ne 0) { throw 'Lite launcher build failed.' }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Full: $(Join-Path $fullDir 'WindowsExtendQuickSetting.exe')"
Write-Host "Lite: $(Join-Path $liteDir 'WindowsExtendQuickSetting.Lite.exe')"
