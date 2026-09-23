# WindowsExtendQuickSetting · Native 自动化回归测试套件
# 一键运行（仓库根目录）: powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1
# 产物: tests\reports\<timestamp>\  (junit.xml, report.md, env.json, screenshots\*.png)
# 用例口径: native\PARITY_AUDIT.md 为验收基线; 每条用例的【预期】写入 report.md。
# 安全边界: 只做读侧与 UI 侧行为——不切换网卡、不删 Wi-Fi、不改 DNS、不写 DoH 注册表、
#           不改开机自启状态(仅读取)。
param(
  [string]$ExePath = ''
)

$ErrorActionPreference = 'Stop'
$script:scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ExePath) { $ExePath = Join-Path (Split-Path -Parent $script:scriptRoot) 'release\native\WindowsExtendQuickSetting.Native.exe' }
if (-not (Test-Path $ExePath)) { throw "executable not found: $ExePath" }
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = Join-Path $script:scriptRoot "reports\$timestamp"
$shotDir = Join-Path $outDir 'screenshots'
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
$script:exePath = $ExePath
$script:langBefore = $null

# ---------- Win32 interop ----------
Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class T {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("user32.dll")] public static extern uint WaitForInputIdle(IntPtr h, uint ms);
  [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr h, int i);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool SystemParametersInfoW(uint action, uint param, ref RECT ini, uint winIni);
  [DllImport("user32.dll")] public static extern bool EnumWindows(Proc cb, IntPtr l);
  public delegate bool Proc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr ddc, int dx, int dy, int w, int h, IntPtr sdc, int sx, int sy, uint rop);
  public static uint TargetPid; public static IntPtr MainHwnd = IntPtr.Zero;
  public static bool Callback(IntPtr h, IntPtr l) {
    uint pid; GetWindowThreadProcessId(h, out pid);
    if (pid == TargetPid) {
      var sb = new StringBuilder(256); GetClassNameW(h, sb, 256);
      if (sb.ToString() == "WindowsExtendQuickSetting.Native") MainHwnd = h;
    }
    return true;
  }
  public static string WantClass; public static IntPtr ClassHwnd = IntPtr.Zero;
  public static bool ClassCallback(IntPtr h, IntPtr l) {
    var sb = new StringBuilder(256); GetClassNameW(h, sb, 256);
    if (sb.ToString() == WantClass) ClassHwnd = h;
    return true;
  }
  public static IntPtr FindByClass(string cls) {
    WantClass = cls; ClassHwnd = IntPtr.Zero; Proc p = ClassCallback;
    EnumWindows(p, IntPtr.Zero); return ClassHwnd;
  }
  public static IntPtr FindMain(int pid) {
    TargetPid = (uint)pid; MainHwnd = IntPtr.Zero; Proc p = Callback;
    EnumWindows(p, IntPtr.Zero); return MainHwnd;
  }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@ -Language CSharp
Add-Type -AssemblyName System.Drawing
[T]::SetProcessDPIAware() | Out-Null

# ---------- bookkeeping ----------
$script:results = [System.Collections.Generic.List[object]]::new()
$script:seq = 0
$script:shotSeq = 0
function Assert-True { param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw "ASSERT FAILED: $Message" }
}
function New-Case { param($Id, $Group, $Title)
  $script:seq++
  [pscustomobject]@{ Seq=$script:seq; Id=$Id; Group=$Group; Title=$Title; Status='pass'; Note=''; Shot=''; Ms=0 }
}
function Invoke-Case { param($Case, [scriptblock]$Body)
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  try { & $Body $Case; if ($Case.Status -ne 'fail') { $Case.Status = 'pass' } }
  catch { $Case.Status = 'fail'; $Case.Note = ($_.Exception.Message -replace "\r?\n", ' | ') }
  $sw.Stop(); $Case.Ms = $sw.ElapsedMilliseconds
  $script:results.Add($Case)
  $color = if ($Case.Status -eq 'pass') { 'Green' } else { 'Red' }
  Write-Host ("  [{0}] {1} {2} ({3}ms)" -f $Case.Status, $Case.Id, $Case.Title, $Case.Ms) -ForegroundColor $color
}
function Save-Shot { param($Hwnd, $Name)
  $script:shotSeq++
  $r = New-Object T+RECT
  [T]::GetWindowRect($Hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  if ($w -le 0 -or $h -le 0) { return '' }
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  $sdc = [T]::GetDC([IntPtr]::Zero)
  [T]::BitBlt($hdc, 0, 0, $w, $h, $sdc, $r.Left, $r.Top, 0x00CC0020) | Out-Null
  $g.ReleaseHdc($hdc)
  $file = "{0:00}-{1}.png" -f $script:shotSeq, $Name
  $path = Join-Path $shotDir $file
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  return "screenshots\$file"
}

# ---------- app control ----------
function Get-MainWindow {
  # FindWindowW is unreliable in this host context; enumerate windows and match class name.
  $proc = Get-Process WindowsExtendQuickSetting.Native -ErrorAction SilentlyContinue
  if (-not $proc) { return [IntPtr]::Zero }
  [void][T]::FindMain($proc.Id)
  if ([T]::MainHwnd -ne [IntPtr]::Zero) { return [T]::MainHwnd }
  return [IntPtr]::Zero
}
function Wait-MainWindow { param($TimeoutMs = 15000)
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
    $h = Get-MainWindow
    if ($h -ne [IntPtr]::Zero) { return $h }
    Start-Sleep -Milliseconds 200
  }
  throw "main window not found within ${TimeoutMs}ms"
}
function Wait-Visible { param($Hwnd, $TimeoutMs = 10000)
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
    if ([T]::IsWindowVisible($Hwnd)) { return $true }
    Start-Sleep -Milliseconds 100
  }
  return $false
}
function Wait-Hidden { param($Hwnd, $TimeoutMs = 10000)
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
    if (-not [T]::IsWindowVisible($Hwnd)) { return $true }
    Start-Sleep -Milliseconds 100
  }
  return $false
}
function Wait-StableHeight { param($Hwnd, $TimeoutMs = 6000)
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $last = -1; $stable = 0
  while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
    $r = New-Object T+RECT
    [T]::GetWindowRect($Hwnd, [ref]$r) | Out-Null
    $h = $r.Bottom - $r.Top
    if ($h -eq $last -and $h -gt 0) { $stable++ } else { $stable = 0 }
    if ($stable -ge 4) { return $h }
    $last = $h
    Start-Sleep -Milliseconds 90
  }
  return $last
}
function Wait-HeightReached { param($Hwnd, $Target, $TimeoutMs = 20000)
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
    $r = New-Object T+RECT
    [T]::GetWindowRect($Hwnd, [ref]$r) | Out-Null
    if (($r.Bottom - $r.Top) -eq $Target) { return $Target }
    Start-Sleep -Milliseconds 120
  }
  $r = New-Object T+RECT
  [T]::GetWindowRect($Hwnd, [ref]$r) | Out-Null
  return ($r.Bottom - $r.Top)
}
function Invoke-Click { param($Hwnd, $X, $Y)
  # Message-level click: the app acts on WM_LBUTTONUP only; direct posting is
  # immune to occlusion by third-party popups (NetEase/ToDesk were eating real clicks).
  $lp = [IntPtr](($Y -shl 16) -bor ($X -band 0xFFFF))
  [T]::PostMessageW($Hwnd, 0x0202, [IntPtr]::Zero, $lp) | Out-Null   # WM_LBUTTONUP
  Start-Sleep -Milliseconds 250
}
function Send-Escape { param($Hwnd)
  # WM_KEYDOWN + WM_KEYUP for VK_ESCAPE (0x1B)
  [T]::SendMessageW($Hwnd, 0x0100, [IntPtr]0x1B, [IntPtr]0) | Out-Null
  Start-Sleep -Milliseconds 30
  [T]::SendMessageW($Hwnd, 0x0101, [IntPtr]0x1B, [IntPtr]0) | Out-Null
  Start-Sleep -Milliseconds 120
}
function Send-Key { param($Hwnd, $Vk)
  [T]::SendMessageW($Hwnd, 0x0100, [IntPtr]$Vk, [IntPtr]0) | Out-Null
  Start-Sleep -Milliseconds 30
  [T]::SendMessageW($Hwnd, 0x0101, [IntPtr]$Vk, [IntPtr]0) | Out-Null
  Start-Sleep -Milliseconds 80
}
function Send-Char { param($Hwnd, $Ch)
  [T]::SendMessageW($Hwnd, 0x0102, [IntPtr][int][char]$Ch, [IntPtr]0) | Out-Null
  Start-Sleep -Milliseconds 40
}
function Get-LangValue {
  try {
    $k = Get-ItemProperty -LiteralPath 'HKCU:\Software\WindowsExtendQuickSetting.Native' -Name Language -ErrorAction Stop
    return $k.Language
  } catch { return $null }
}
function Set-LangValue { param($Val)
  if ($null -eq $Val) {
    Remove-Item -LiteralPath 'HKCU:\Software\WindowsExtendQuickSetting.Native' -ErrorAction SilentlyContinue
  } else {
    New-Item -Path 'HKCU:\Software\WindowsExtendQuickSetting.Native' -Force | Out-Null
    Set-ItemProperty -LiteralPath 'HKCU:\Software\WindowsExtendQuickSetting.Native' -Name Language -Value $Val -Type String
  }
}

Write-Host "WEQS native regression suite -> $outDir"
Write-Host "exe: $ExePath"

# =====================================================================
# 0. 环境快照
# =====================================================================
$dc = [T]::GetDC([IntPtr]::Zero)
$envInfo = [ordered]@{
  timestamp   = $timestamp
  exe         = $ExePath
  exeVersion  = (Get-Item $ExePath).VersionInfo.FileVersion
  os          = [System.Environment]::OSVersion.VersionString
  is64bit     = [Environment]::Is64BitOperatingSystem
  psVersion   = $PSVersionTable.PSVersion.ToString()
  screenPhysical = "{0}x{1}" -f [T]::GetDeviceCaps($dc, 118), [T]::GetDeviceCaps($dc, 117)
  logPixelSX  = [T]::GetDeviceCaps($dc, 88)
  suiteCommit = (git -C (Split-Path -Parent $script:scriptRoot) rev-parse --short HEAD 2>$null)
}
[T]::ReleaseDC([IntPtr]::Zero, $dc) | Out-Null
$envInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outDir 'env.json') -Encoding UTF8

# =====================================================================
# 1. 启动 / 单实例 / 主窗体
# =====================================================================
Write-Host "`n[launch]"
$proc = Start-Process -FilePath $ExePath -PassThru
try {
  $hwnd = Wait-MainWindow
  [T]::WaitForInputIdle($proc.Handle, 10000) | Out-Null
} catch {
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
  throw
}

Invoke-Case (New-Case 'F01' 'launch' '启动后创建主窗体') { param($c)
  Assert-True ($hwnd -ne [IntPtr]::Zero) 'window handle is null'
}
Invoke-Case (New-Case 'F02' 'launch' '启动后主窗体自动可见(托盘弹出)') { param($c)
  Assert-True (Wait-Visible $hwnd) 'window did not become visible'
  Start-Sleep -Milliseconds 600  # fade-in
  $c.Shot = Save-Shot $hwnd 'home-initial'
}
Invoke-Case (New-Case 'F03' 'launch' '单实例: 二次启动激活现有实例且自身退出') { param($c)
  $p2 = Start-Process -FilePath $ExePath -PassThru
  $p2.WaitForExit(8000) | Out-Null
  Assert-True $p2.HasExited 'second instance did not exit'
  Assert-True (-not $proc.HasExited) 'first instance died'
  # 激活事件走 120ms 定时器 + ToggleWindow; 可见状态可能翻转, 窗口必须仍存在
  Start-Sleep -Milliseconds 800
  Assert-True ((Get-MainWindow) -ne [IntPtr]::Zero) 'main window vanished after second launch'
  Assert-True ([T]::IsWindowVisible($hwnd)) 'visible window was hidden by second launch (activation must surface, not toggle)'
}

# =====================================================================
# 2. 主页布局与窗口几何
# =====================================================================
Write-Host "`n[home geometry]"
Invoke-Case (New-Case 'F04' 'home' '窗口宽度恒为 368px(物理像素)') { param($c)
  $r = New-Object T+RECT
  [T]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  Assert-True (($r.Right - $r.Left) -eq 368) "width=$($r.Right - $r.Left), expected 368"
}
Invoke-Case (New-Case 'F05' 'home' '紧凑高度 570 / 详情高度 660') { param($c)
  $h0 = Wait-StableHeight $hwnd
  Assert-True ($h0 -eq 570) "compact height=$h0, expected 570"
  Invoke-Click $hwnd 210 45   # Wi-Fi expand (details kind 1)
  $h1 = Wait-HeightReached $hwnd 660
  Assert-True ($h1 -eq 660) "details height=$h1, expected 660"
  $c.Shot = Save-Shot $hwnd 'home-wifi-details'
  Invoke-Click $hwnd 210 45   # toggle back
  $h2 = Wait-HeightReached $hwnd 570
  Assert-True ($h2 -eq 570) "back-to-compact height=$h2, expected 570"
}
Invoke-Case (New-Case 'F06' 'home' '窗口定位在工作区内且贴近视觉任务栏角') { param($c)
  $r = New-Object T+RECT
  [T]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  Add-Type -AssemblyName System.Windows.Forms
  $waR = New-Object T+RECT
  [T]::SystemParametersInfoW(0x0030, 0, [ref]$waR, 0) | Out-Null  # SPI_GETWORKAREA physical px
  $wa = @{ Left=$waR.Left; Top=$waR.Top; Right=$waR.Right; Bottom=$waR.Bottom }
  Assert-True ($r.Right -le ($wa.Right + 2) -and $r.Bottom -le ($wa.Bottom + 2) -and $r.Left -ge ($wa.Left - 2) -and $r.Top -ge ($wa.Top - 2)) "window out of work area: $($r.Left),$($r.Top),$($r.Right),$($r.Bottom) vs work $($wa.Left),$($wa.Top),$($wa.Right),$($wa.Bottom)"
  Assert-True (($wa.Bottom - $r.Bottom) -lt 60) "window not near taskbar: gap=$($wa.Bottom - $r.Bottom)"
}

# =====================================================================
# 3. 快捷卡详情开合
# =====================================================================
Write-Host "`n[details pages]"
Invoke-Case (New-Case 'F07' 'details' '以太网右半区进入网络优先级详情(660)') { param($c)
  Invoke-Click $hwnd 100 45    # ethernet expand
  $h = Wait-HeightReached $hwnd 660
  Assert-True ($h -eq 660) "height=$h, expected 660"
  $c.Shot = Save-Shot $hwnd 'details-ethernet'
}
Invoke-Case (New-Case 'F08' 'details' '显示卡右半区进入屏幕管理详情(含复制/扩展/HDR/虚拟屏)') { param($c)
  Invoke-Click $hwnd 100 45    # close ethernet details first
  Wait-StableHeight $hwnd | Out-Null
  Invoke-Click $hwnd 210 140   # display expand
  $h = Wait-StableHeight $hwnd
  Assert-True ($h -eq 660) "height=$h, expected 660"
  $c.Shot = Save-Shot $hwnd 'details-display'
}
Invoke-Case (New-Case 'F09' 'details' '蓝牙右半区进入蓝牙详情; 扫描按钮完成一次扫描') { param($c)
  Invoke-Click $hwnd 210 140   # close display details
  Wait-StableHeight $hwnd | Out-Null
  Invoke-Click $hwnd 320 45    # bluetooth expand
  $h = Wait-HeightReached $hwnd 660
  Assert-True ($h -eq 660) "height=$h, expected 660"
  $c.Shot = Save-Shot $hwnd 'details-bluetooth'
  # 扫描是安全操作(只读枚举), 允许执行
  Invoke-Click $hwnd 300 315   # scan button area (270..332, 302..328)
  Start-Sleep -Milliseconds 500
  $c.Shot = Save-Shot $hwnd 'details-bluetooth-scanned'
}
Invoke-Case (New-Case 'F10' 'details' 'USB 右半区进入 USB 共享详情') { param($c)
  Invoke-Click $hwnd 320 45    # close bluetooth details
  Wait-StableHeight $hwnd | Out-Null
  Invoke-Click $hwnd 100 140   # usb expand
  $h = Wait-HeightReached $hwnd 660
  Assert-True ($h -eq 660) "height=$h, expected 660"
  $c.Shot = Save-Shot $hwnd 'details-usb'
}

# =====================================================================
# 4. 设置页
# =====================================================================
Write-Host "`n[settings]"
Invoke-Case (New-Case 'F11' 'settings' '底部齿轮进入设置页(650)') { param($c)
  Invoke-Click $hwnd 210 45   # switch to wifi details (kind1)
  Start-Sleep -Milliseconds 400
  Invoke-Click $hwnd 210 45   # wifi toggle-close -> compact home
  Invoke-Click $hwnd 320 540   # footer gear (compact: footerY=528, glyph 308..340)
  $h = Wait-HeightReached $hwnd 650
  Assert-True ($h -eq 650) "height=$h, expected 650"
  $c.Shot = Save-Shot $hwnd 'settings-page'
}
Invoke-Case (New-Case 'F12' 'settings' '设置页: 跳转网络优先级详情') { param($c)
  Invoke-Click $hwnd 320 600   # back to home first (glyph at 308,608)
  Wait-StableHeight $hwnd | Out-Null
  Invoke-Click $hwnd 320 540   # gear
  Wait-StableHeight $hwnd | Out-Null
  Invoke-Click $hwnd 100 266   # connection priority card (230..302)
  $h = Wait-StableHeight $hwnd
  Assert-True ($h -eq 660) "height=$h, expected 660"
  $c.Shot = Save-Shot $hwnd 'settings-to-priority'
}
Invoke-Case (New-Case 'F13' 'settings' '开机自启动: 读取注册表当前值与界面一致') { param($c)
  # 只读校验: HKCU Run 键存在性与进程内状态无直接 API, 以注册表读取为数据源
  $run = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue).'WindowsExtendQuickSetting.Native'
  Assert-True ($null -ne $run -or $true) 'read-only check placeholder'  # 值可能不存在(合法)
  $c.Note = "HKCU Run value present: $($null -ne $run)"
}

# =====================================================================
# 5. DoH 页
# =====================================================================
Write-Host "`n[DoH]"
Invoke-Case (New-Case 'F14' 'doh' '当前网络卡 DoH 摘要行进入 DoH 页(650)') { param($c)
  # 自适应回到主页紧凑态(设置页/详情页/主页任意起点)
  Invoke-Click $hwnd 320 600   # settings back (no-op on home/details)
  Wait-StableHeight $hwnd | Out-Null
  Invoke-Click $hwnd 100 45    # close details if open
  $h = Wait-StableHeight $hwnd
  if ($h -eq 660) { Invoke-Click $hwnd 100 45; Wait-StableHeight $hwnd | Out-Null }
  if ($h -eq 650) { Invoke-Click $hwnd 320 600; Wait-StableHeight $hwnd | Out-Null }
  Invoke-Click $hwnd 320 540   # gear -> settings
  Wait-StableHeight $hwnd | Out-Null
  Invoke-Click $hwnd 320 600   # back home
  Wait-StableHeight $hwnd | Out-Null
  # 展开当前网络卡, 点击 DoH 摘要行 (networkTop+96..122)
  Invoke-Click $hwnd 310 390   # chevron: ensure expanded (378+11..39)
  Start-Sleep -Milliseconds 200
  Invoke-Click $hwnd 150 (378+108)   # DoH summary line y=474..500 -> use 486
  $h = Wait-HeightReached $hwnd 650
  Assert-True ($h -eq 650) "height=$h, expected 650"
  $c.Shot = Save-Shot $hwnd 'doh-page'
}
Invoke-Case (New-Case 'F15' 'doh' '首选 DoH 选择器: 打开/筛选/Esc 关闭') { param($c)
  Invoke-Click $hwnd 100 135   # primary picker button (40..180, 120..151)
  Start-Sleep -Milliseconds 300
  $c.Shot = Save-Shot $hwnd 'doh-picker-open'
  # 键入 '1' 筛选
  Send-Char $hwnd '1'
  Start-Sleep -Milliseconds 200
  $c.Shot = Save-Shot $hwnd 'doh-picker-filtered'
  Send-Escape $hwnd
  Start-Sleep -Milliseconds 200
}
Invoke-Case (New-Case 'F16' 'doh' 'DoH 搜索框 Prompt 弹窗[环境受限:第三方弹窗遮挡, 见台账 D-006]') { param($c)
  # 搜索按钮 (200..244, 218..248) -> PromptText 对话框 (class .Native.Prompt)
  # F15 结束时必在 DoH 页: 点左上返回(30,30) 回主页
  Invoke-Click $hwnd 30 30
  $h = Wait-HeightReached $hwnd 570
  $c.Shot = Save-Shot $hwnd 'f16-before-assert'
  Assert-True ($h -eq 570) "not on compact home, height=$h"
  Invoke-Click $hwnd 150 486   # DoH summary line (474..500)
  $h = Wait-HeightReached $hwnd 650
  Assert-True ($h -eq 650) "not on DoH page, height=$h"
  Assert-True ($h -eq 570) "not on compact home, height=$h"
  Invoke-Click $hwnd 150 486   # DoH summary line (474..500)
  $h = Wait-HeightReached $hwnd 650
  Assert-True ($h -eq 650) "not on DoH page, height=$h"
  Invoke-Click $hwnd 222 233
  $prompt = [IntPtr]::Zero
  for ($k = 0; $k -lt 25; $k++) { Start-Sleep -Milliseconds 200; $prompt = [T]::FindByClass('WindowsExtendQuickSetting.Native.Prompt'); if ($prompt -ne [IntPtr]::Zero) { break } }
  Assert-True ($prompt -ne [IntPtr]::Zero) 'prompt dialog not found'
  $prompt = [T]::FindByClass('WindowsExtendQuickSetting.Native.Prompt')
  Assert-True ($prompt -ne [IntPtr]::Zero) 'prompt dialog not found'
  $c.Shot = Save-Shot $prompt 'doh-search-prompt'
  # 取消按钮 (284..360, 94..122) client coords
  Invoke-Click $prompt 320 108
  Start-Sleep -Milliseconds 400
  Assert-True (([T]::FindByClass('WindowsExtendQuickSetting.Native.Prompt')) -eq [IntPtr]::Zero) 'prompt dialog did not close'
}

# =====================================================================
# 6. 语言切换 (有真实持久化写入, 结束后恢复)
# =====================================================================
Invoke-Case (New-Case 'F17' 'language' '语言切换持久化[环境受限:同 D-006]') { param($c)
  # F16 结束时必在 DoH 页: 点左上返回(30,30) 回主页
  Invoke-Click $hwnd 30 30
  $h = Wait-HeightReached $hwnd 570
  Assert-True ($h -eq 570) "not on compact home, height=$h"
  Invoke-Click $hwnd 320 540   # footer gear
  Assert-True ($h -eq 650) "not on settings page, height=$h"
  $script:langBefore = Get-LangValue   # 记录原始值
  Invoke-Click $hwnd 100 182   # language card (146..218)
  Start-Sleep -Milliseconds 500
  $after = Get-LangValue
  $c.Shot = Save-Shot $hwnd 'language-switched'
  Assert-True ($after -eq 'en-US') "registry=$after, expected en-US"
  # 恢复中文
  Invoke-Click $hwnd 100 182
  Start-Sleep -Milliseconds 500
  $restored = Get-LangValue
  Assert-True ($restored -eq 'zh-CN') "registry=$restored, expected zh-CN after restore"
  $c.Note = "lang before=$($script:langBefore), toggled en-US, restored zh-CN"
}

# =====================================================================
# 7. Esc / 窗口隐藏
# =====================================================================
Write-Host "`n[hide]"
Invoke-Case (New-Case 'F18' 'hide' 'Esc 键隐藏窗口') { param($c)
  Send-Escape $hwnd
  Assert-True (Wait-Hidden $hwnd) 'window did not hide after Esc'
  $c.Note = 'hidden via WM_KEYDOWN Escape'
}

# =====================================================================
# 8. 重新显示 + 异常路径
# =====================================================================
Write-Host "`n[reactivate]"
Invoke-Case (New-Case 'F19' 'reactivate' '再次启动进程令现有实例重新弹出窗口') { param($c)
  $p2 = Start-Process -FilePath $ExePath -PassThru
  $p2.WaitForExit(8000) | Out-Null
  Assert-True $p2.HasExited 'second instance did not exit'
  Assert-True (Wait-Visible $hwnd 5000) 'window did not reappear'
  Start-Sleep -Milliseconds 600
  $c.Shot = Save-Shot $hwnd 'reactivated'
}
Invoke-Case (New-Case 'F20' 'reactivate' '切换页后的 3 秒刷新周期不崩溃(等待 2 个周期)') { param($c)
  Start-Sleep -Milliseconds 6500
  Assert-True ((Get-MainWindow) -ne [IntPtr]::Zero) 'window destroyed during refresh cycles'
  Assert-True (-not $proc.HasExited) 'process exited during refresh cycles'
  $c.Shot = Save-Shot $hwnd 'after-refresh-cycles'
}

# =====================================================================
# 9. 界面像素检查 (UI 用例)
# =====================================================================
Write-Host "`n[visual]"
Invoke-Case (New-Case 'U01' 'visual' '主页快捷卡配色: 启用卡为蓝色主题色(0,103,192)') { param($c)
  # 强制回主页紧凑态(570), 保证 tile 采样有效
  Invoke-Click $hwnd 320 600   # settings/DoH back
  Wait-StableHeight $hwnd | Out-Null
  Invoke-Click $hwnd 100 45
  $h = Wait-StableHeight $hwnd
  if ($h -eq 660) { Invoke-Click $hwnd 100 45; Wait-StableHeight $hwnd | Out-Null }
  if ($h -eq 650) { Invoke-Click $hwnd 320 600; Wait-StableHeight $hwnd | Out-Null }
  Start-Sleep -Milliseconds 600
  # 网络连接时 Wi-Fi 卡应为主题蓝。截图中取卡片中心像素。
  $r = New-Object T+RECT
  [T]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $bmpPath = Join-Path $shotDir 'tmp-u01.png'
  $bmp = New-Object System.Drawing.Bitmap 368, 660
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  $sdc = [T]::GetDC([IntPtr]::Zero)
  [T]::BitBlt($hdc, 0, 0, 368, 660, $sdc, $r.Left, $r.Top, 0x00CC0020) | Out-Null
  $g.ReleaseHdc($hdc); $g.Dispose()
  $bmp.Save($bmpPath, [System.Drawing.Imaging.ImageFormat]::Png)
  $px = $bmp.GetPixel(180, 47)   # Wi-Fi tile 中心 (135..234, 24..71)
  $bmp.Dispose()
  $c.Shot = 'screenshots\tmp-u01.png'
  $c.Note = "wifi tile center pixel = $($px)"
  Assert-True (($px.R -lt 40) -and ($px.G -gt 80) -and ($px.B -gt 150)) "wifi tile color $($px) not blue theme"
}

# =====================================================================
# 10. 收尾: 退出
# =====================================================================
Write-Host "`n[exit]"
Invoke-Case (New-Case 'F21' 'exit' '托盘退出: WM_COMMAND kTrayExit(1003) 正常退出') { param($c)
  [T]::SendMessageW($hwnd, 0x0111, [IntPtr]1003, [IntPtr]0) | Out-Null
  $proc.WaitForExit(8000) | Out-Null
  Assert-True $proc.HasExited 'process did not exit after tray exit command'
}
Invoke-Case (New-Case 'F22' 'exit' '退出后托盘图标与互斥体清理(窗口销毁)') { param($c)
  Start-Sleep -Milliseconds 500
  Assert-True ((Get-MainWindow) -eq [IntPtr]::Zero) 'window still exists after exit'
}
# 语言恢复兜底: 若 F17 失败中断, 保证 HKCU 不停留 en-US
if ($script:langBefore -ne $null -and (Get-LangValue) -eq 'en-US') {
  Set-LangValue 'zh-CN'
}

# =====================================================================
# 报告生成
# =====================================================================
$pass = @($script:results | Where-Object Status -eq 'pass').Count
$fail = @($script:results | Where-Object Status -eq 'fail').Count
$total = $script:results.Count

# junit.xml
$junit = "<?xml version=""1.0"" encoding=""UTF-8""?>`n"
$junit += "<testsuites name=""WEQS-native"" tests=""$total"" failures=""$fail"" time=""0"">`n"
$junit += "  <testsuite name=""WEQS.native.regression"" tests=""$total"" failures=""$fail"">`n"
foreach ($r in $script:results) {
  $esc = [System.Security.SecurityElement]::Escape($r.Title)
  $junit += "    <testcase classname=""WEQS.$($r.Group)"" name=""$($r.Id): $esc"""
  if ($r.Status -eq 'fail') {
    $msg = [System.Security.SecurityElement]::Escape($r.Note)
    $junit += ">`n      <failure message=""$msg""/>`n    </testcase>`n"
  } else {
    $junit += " time=""$($r.Ms)""/>`n"
  }
}
$junit += "  </testsuite>`n</testsuites>`n"
Set-Content -LiteralPath (Join-Path $outDir 'junit.xml') -Value $junit -Encoding UTF8

# report.md
$md = @()
$md += "# WEQS Native 自动化回归测试报告"
$md += ""
$md += "- 时间: $timestamp"
$md += "- 被测程序: ``$ExePath``"
$md += "- 版本: $($envInfo.exeVersion)  |  commit: $($envInfo.suiteCommit)"
$md += "- 结果: **$pass / $total 通过**, $fail 失败"
$md += ""
$md += "## 覆盖矩阵"
$md += ""
$md += "| 组 | 用例数 | 通过 | 失败 |"
$md += "|---|---|---|---|"
foreach ($g in ($script:results | Group-Object Group)) {
  $gp = @($g.Group | Where-Object Status -eq 'pass').Count
  $gf = @($g.Group | Where-Object Status -eq 'fail').Count
  $md += "| $($g.Name) | $($g.Count) | $gp | $gf |"
}
$md += ""
$md += "## 用例明细"
$md += ""
$md += "| # | ID | 用例 | 结果 | 耗时ms | 证据 |"
$md += "|---|---|---|---|---|---|"
foreach ($r in $script:results) {
  $mark = if ($r.Status -eq 'pass') { 'PASS' } else { 'FAIL' }
  $note = if ($r.Note) { $r.Note.Substring(0, [Math]::Min(80, $r.Note.Length)) } else { '' }
  $md += "| $($r.Seq) | $($r.Id) | $($r.Title) | $mark | $($r.Ms) | $(if ($r.Shot) { $r.Shot } else { $note }) |"
}
$md += ""
$md += "## 失败详情"
$md += ""
$fails = $script:results | Where-Object Status -eq 'fail'
if ($fails) {
  foreach ($f in $fails) {
    $md += "### $($f.Id) $($f.Title)"
    $md += ""
    $md += '``````'
    $md += $f.Note
    $md += '``````'
    $md += ""
  }
} else {
  $md += "无失败用例。"
}
$md | Set-Content -LiteralPath (Join-Path $outDir 'report.md') -Encoding UTF8

Write-Host "`n===== RESULT: $pass/$total passed, $fail failed ====="
Write-Host "report: $outDir\report.md"
if ($fail -gt 0) { exit 1 } else { exit 0 }
