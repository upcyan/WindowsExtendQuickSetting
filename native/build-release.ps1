$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'release\native'
Get-Process -Name 'WindowsExtendQuickSetting.Native' -ErrorAction SilentlyContinue |
  Stop-Process -Force -ErrorAction SilentlyContinue
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($vs)) { throw 'Visual Studio C++ x64 build tools not found.' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$icon = Join-Path $PSScriptRoot 'app.ico'
$sourceAssets = Join-Path $root 'winui3\WindowsExtendQuickSetting\Assets'
$projectFile = Join-Path $root 'winui3\WindowsExtendQuickSetting\WindowsExtendQuickSetting.csproj'
$projectVersion = [regex]::Match((Get-Content $projectFile -Raw), '<Version>([^<]+)</Version>').Groups[1].Value
if ([string]::IsNullOrWhiteSpace($projectVersion)) { throw 'WinUI3 project version was not found.' }
$versionParts = $projectVersion.Split('.') | ForEach-Object { [int]$_ }
while ($versionParts.Count -lt 3) { $versionParts += 0 }
$versionQuad = "$($versionParts[0]).$($versionParts[1]).$($versionParts[2]).0"
$versionCsv = "$($versionParts[0]),$($versionParts[1]),$($versionParts[2]),0"
$rcPath = Join-Path $PSScriptRoot 'app.rc'
$rc = Get-Content $rcPath -Raw
$rc = $rc -replace '(?m)^(FILEVERSION|PRODUCTVERSION)\s+.*$', ('${1} ' + $versionCsv)
$rc = $rc -replace '(?m)^(\s*VALUE "(?:File|Product)Version", ").*(")$', ('${1}' + $versionQuad + '${2}')
Set-Content -Path $rcPath -Value $rc -Encoding utf8
# The native executable and tray use the same monochrome WinUI3 icon set.
Copy-Item (Join-Path $sourceAssets 'NetworkControlIconDark.ico') $icon -Force
Copy-Item $sourceAssets (Join-Path $output 'Assets') -Recurse -Force
Push-Location $PSScriptRoot
try {
  $command = 'call "' + $vcvars + '" >nul && rc.exe /nologo app.rc && cl.exe /nologo /std:c++20 /EHsc /utf-8 /DUNICODE /D_UNICODE /O2 main.cpp app.res /link /SUBSYSTEM:WINDOWS /OUT:"' + (Join-Path $output 'WindowsExtendQuickSetting.Native.exe') + '" user32.lib gdi32.lib msimg32.lib dwmapi.lib shell32.lib wlanapi.lib iphlpapi.lib setupapi.lib cfgmgr32.lib advapi32.lib bthprops.lib ws2_32.lib version.lib'
  & cmd.exe /d /c $command
  if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
} finally { Pop-Location }
Write-Host "Native: $(Join-Path $output 'WindowsExtendQuickSetting.Native.exe')"
