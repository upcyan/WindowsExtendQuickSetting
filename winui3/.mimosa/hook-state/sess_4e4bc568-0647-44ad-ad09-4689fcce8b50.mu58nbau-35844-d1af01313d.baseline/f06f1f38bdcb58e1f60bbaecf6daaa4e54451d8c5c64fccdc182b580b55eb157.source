using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;
using WindowsEthernetControl.Views;

namespace WindowsEthernetControl.Services;

public class TrayService
{
    private IntPtr _hWnd;
    private NOTIFYICONDATA _nid;
    private QuickSettingsPopup? _popup;
    private WndProcDelegate? _wndProcRef;
    private Thread? _msgThread;
    private const int WM_TRAYICON = 0x0400 + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private static readonly Guid TrayIconGuid = new("7E67D7A3-09CC-4CB2-9DB0-D0ECDA8CC63D");

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public void Initialize()
    {
        _wndProcRef = WndProc;
        _msgThread = new Thread(MsgPump) { IsBackground = true, Name = "TrayMsgPump" };
        _msgThread.SetApartmentState(ApartmentState.STA);
        _msgThread.Start();
    }

    private void MsgPump()
    {
        var className = "EthernetControlTray_" + Environment.ProcessId;
        var wc = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcRef!),
            hInstance = Marshal.GetHINSTANCE(typeof(TrayService).Module),
            lpszClassName = className
        };
        NativeMethods.RegisterClass(ref wc);

        _hWnd = NativeMethods.CreateWindowEx(
            0x80, className, "", 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        _nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP | NIF_GUID,
            uCallbackMessage = WM_TRAYICON,
            hIcon = LoadTrayIcon(),
            szTip = "WindowsExtendQuickSetting",
            guidItem = TrayIconGuid
        };

        NativeMethods.Shell_NotifyIcon(NIM_ADD, ref _nid);
        _nid.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NIM_SETVERSION, ref _nid);

        MSG msg;
        while (NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
    }

    private static IntPtr CreateBlueIcon()
    {
        int size = 32;
        IntPtr hBmp = NativeMethods.CreateBitmap(size, size, 1, 32, IntPtr.Zero);
        IntPtr hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        IntPtr oldBmp = NativeMethods.SelectObject(hdc, hBmp);
        IntPtr brush = NativeMethods.CreateSolidBrush(0x00D47800);
        var rect = new RECT { left = 0, top = 0, right = size, bottom = size };
        NativeMethods.FillRect(hdc, ref rect, brush);
        NativeMethods.DeleteObject(brush);
        NativeMethods.SelectObject(hdc, oldBmp);
        NativeMethods.DeleteDC(hdc);
        var ii = new ICONINFO { fIcon = true, hbmColor = hBmp, hbmMask = hBmp };
        IntPtr hIcon = NativeMethods.CreateIconIndirect(ref ii);
        NativeMethods.DeleteObject(hBmp);
        return hIcon;
    }

    private static IntPtr LoadTrayIcon()
    {
        var iconName = IsLightTaskbar() ? "NetworkControlIconDark.ico" : "NetworkControlIconLight.ico";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", iconName);
        var icon = NativeMethods.LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 33, 33, LR_LOADFROMFILE);
        return icon != IntPtr.Zero ? icon : CreateBlueIcon();
    }

    private static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch { return false; }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            int mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == WM_LBUTTONUP)
            {
                TogglePopup();
            }
            else if (mouseMsg == WM_RBUTTONUP || mouseMsg == WM_CONTEXTMENU)
            {
                var pt = new POINT();
                NativeMethods.GetCursorPos(ref pt);
                IntPtr menu = NativeMethods.CreatePopupMenu();
                var isZh = App.Settings.Settings.Language == "zh-CN";
                NativeMethods.AppendMenu(menu, MF_STRING, 1, isZh ? "设置" : "Settings");
                NativeMethods.AppendMenu(menu, MF_SEPARATOR, 0, null);
                NativeMethods.AppendMenu(menu, MF_STRING, 2, isZh ? "退出" : "Exit");
                NativeMethods.SetForegroundWindow(_hWnd);
                int cmd = NativeMethods.TrackPopupMenu(menu, TPM_RETURNCMD, pt.X, pt.Y, 0, _hWnd, IntPtr.Zero);
                NativeMethods.DestroyMenu(menu);
                if (cmd == 1) OpenSettings();
                else if (cmd == 2) ExitApp();
            }
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OpenSettings()
    {
        App.MainDispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
                if (_popup == null)
                {
                    _popup = new QuickSettingsPopup();
                    _popup.Closed += (s, e) => _popup = null;
                }
                _popup.ShowAndActivate();
                _popup.ShowSettingsPage();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open settings from tray: {ex}");
            }
        });
    }

    public void ShowPopup()
    {
        App.MainDispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
                if (_popup == null)
                {
                    _popup = new QuickSettingsPopup();
                    _popup.Closed += (s, e) => _popup = null;
                }
                _popup.ShowAndActivate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show quick settings: {ex}");
                _popup = null;
            }
        });
    }

    private void TogglePopup()
    {
        App.MainDispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
                if (_popup == null)
                {
                    _popup = new QuickSettingsPopup();
                    _popup.Closed += (s, e) => _popup = null;
                }
                _popup.ToggleVisibility();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to toggle quick settings: {ex}");
                _popup = null;
            }
        });
    }

    private void ExitApp()
    {
        NativeMethods.Shell_NotifyIcon(NIM_DELETE, ref _nid);
        if (_nid.hIcon != IntPtr.Zero) NativeMethods.DestroyIcon(_nid.hIcon);
        App.MainDispatcherQueue?.TryEnqueue(() =>
        {
            _popup?.Close();
            Microsoft.UI.Xaml.Application.Current.Exit();
        });
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public uint cbSize; public IntPtr hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState; public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

    const uint NIF_ICON = 0x02, NIF_MESSAGE = 0x01, NIF_TIP = 0x04, NIF_GUID = 0x20;
    const uint NIM_ADD = 0x00, NIM_DELETE = 0x02, NIM_SETVERSION = 0x04, NOTIFYICON_VERSION_4 = 4;
    const uint MF_STRING = 0x00, MF_SEPARATOR = 0x0800;
    const uint TPM_RETURNCMD = 0x0100, TPM_NONOTIFY = 0x0080;
    const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x0010;

    static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("shell32.dll", SetLastError = true)]
        public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr ho);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitsPerPel, IntPtr lpvBits);
        [DllImport("user32.dll")] public static extern int FillRect(IntPtr hdc, ref RECT lprc, IntPtr hbr);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(uint crColor);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(ref POINT lpPoint);
        [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
        [DllImport("user32.dll")]
        public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}



