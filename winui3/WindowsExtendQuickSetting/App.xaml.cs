using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowsEthernetControl.Services;

namespace WindowsEthernetControl;

public partial class App : Application
{
    public static SettingsService Settings { get; } = new();
    public static NetworkService Network { get; } = new();
    public static bool IsElevated { get; private set; }
    public static Microsoft.UI.Dispatching.DispatcherQueue? MainDispatcherQueue { get; set; }

    private TrayService? _trayService;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateExistingEvent;
    private EventWaitHandle? _shutdownForUpgradeEvent;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // WQS_SKIP_ELEVATE=1 runs a non-elevated, uncoordinated instance for testing.
        var testMode = Environment.GetEnvironmentVariable("WQS_SKIP_ELEVATE") == "1";
        if (!testMode && !AcquireSingleInstance())
        {
            Exit();
            return;
        }

        MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        IsElevated = ElevateHelper.CheckElevated();

        if (!testMode && !IsElevated)
        {
            // The elevated replacement must be able to acquire the mutex immediately.
            // Keeping it until Exit() creates a race where the replacement sees this
            // short-lived process as the existing instance and exits itself.
            ReleaseSingleInstance();
            var result = ElevateHelper.RequestElevation();
            if (!result)
            {
                ShowElevationDialog();
                Exit();
                return;
            }
            Exit();
            return;
        }

        if (testMode) InitializeInstanceCoordination();
        Settings.Load();
        Network.StartMonitoring();

        _trayService = new TrayService();
        _trayService.Initialize();
        StartInstanceEventListener();
    }

    // Returns true when this process may continue starting up.
    private bool AcquireSingleInstance()
    {
        const string mutexName = @"Local\WindowsExtendQuickSetting.SingleInstance";
        var mutex = new Mutex(true, mutexName, out var createdNew);
        if (createdNew)
        {
            _singleInstanceMutex = mutex;
            InitializeInstanceCoordination();
            return true;
        }

        var running = GetRunningInstances();
        if (running.Count == 0)
        {
            // Mutex is held by a stale/abnormal owner (e.g. different exe name across
            // full and lite distributions). Retry acquisition briefly.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (mutex.WaitOne(TimeSpan.FromMilliseconds(300)))
                    {
                        _singleInstanceMutex = mutex;
                        InitializeInstanceCoordination();
                        return true;
                    }
                }
                catch (AbandonedMutexException)
                {
                    _singleInstanceMutex = mutex;
                    InitializeInstanceCoordination();
                    return true;
                }
            }
            Native.MessageBoxW(IntPtr.Zero, "检测到已有实例正在运行，但无法获取控制权。", "WindowsExtendQuickSetting", 0x30);
            mutex.Dispose();
            return false;
        }

        var existingVersion = ReadRunningVersion();
        var currentVersion = typeof(App).Assembly.GetName().Version ?? new Version(0, 0);

        if (existingVersion is not null && existingVersion < currentVersion)
        {
            const int yes = 6;
            var choice = Native.MessageBoxW(IntPtr.Zero,
                $"检测到旧版本（{existingVersion}）正在运行。\n\n是否结束旧版本并启动新版本？",
                "WindowsExtendQuickSetting", 0x34);
            if (choice != yes)
            {
                SignalExistingInstance(false);
                mutex.Dispose();
                return false;
            }
            SignalExistingInstance(true);
            foreach (var process in running)
            {
                try { process.Kill(); } catch { /* may have exited already */ }
            }
            foreach (var process in running)
            {
                try { process.WaitForExit(5000); } catch { }
            }
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (mutex.WaitOne(TimeSpan.FromMilliseconds(200)))
                    {
                        _singleInstanceMutex = mutex;
                        InitializeInstanceCoordination();
                        return true;
                    }
                }
                catch (AbandonedMutexException)
                {
                    _singleInstanceMutex = mutex;
                    InitializeInstanceCoordination();
                    return true;
                }
            }
            Native.MessageBoxW(IntPtr.Zero, "无法结束旧版本进程，请手动关闭后重试。", "WindowsExtendQuickSetting", 0x30);
            mutex.Dispose();
            return false;
        }

        // Same or newer version already running: just surface its window.
        SignalExistingInstance(false);
        ActivateExistingWindows(running); // compatibility fallback for pre-1.2 instances
        mutex.Dispose();
        return false;
    }

    private void InitializeInstanceCoordination()
    {
        // Test mode (WQS_SKIP_ELEVATE=1) uses private event names so it never
        // talks to a real instance.
        var suffix = Environment.GetEnvironmentVariable("WQS_SKIP_ELEVATE") == "1" ? ".Test" : "";
        _activateExistingEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\WindowsExtendQuickSetting.Activate" + suffix);
        _shutdownForUpgradeEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\WindowsExtendQuickSetting.ShutdownForUpgrade" + suffix);
        try
        {
            var path = GetRunningVersionPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var version = typeof(App).Assembly.GetName().Version ?? new Version(0, 0);
            File.WriteAllText(path, $"{Environment.ProcessId}|{version}");
        }
        catch { }
    }

    private void StartInstanceEventListener()
    {
        if (_activateExistingEvent == null || _shutdownForUpgradeEvent == null) return;
        var activate = _activateExistingEvent;
        var shutdown = _shutdownForUpgradeEvent;
        _ = Task.Run(() =>
        {
            while (true)
            {
                int signal;
                try { signal = WaitHandle.WaitAny(new WaitHandle[] { activate, shutdown }); }
                catch (ObjectDisposedException) { return; }
                if (signal == 1)
                {
                    MainDispatcherQueue?.TryEnqueue(Exit);
                    return;
                }
                MainDispatcherQueue?.TryEnqueue(() => _trayService?.ShowPopup());
            }
        });
    }

    private static void SignalExistingInstance(bool shutdown)
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(shutdown
                ? @"Local\WindowsExtendQuickSetting.ShutdownForUpgrade"
                : @"Local\WindowsExtendQuickSetting.Activate");
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Version? ReadRunningVersion()
    {
        try
        {
            var parts = File.ReadAllText(GetRunningVersionPath()).Split('|', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var pid)) return null;
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return null;
            return Version.TryParse(parts[1], out var version) ? version : null;
        }
        catch { return null; }
    }

    private static string GetRunningVersionPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsExtendQuickSetting", "running-instance.txt");

    private static List<Process> GetRunningInstances()
    {
        var currentId = Environment.ProcessId;
        var currentName = Process.GetCurrentProcess().ProcessName;
        // Full and lite distributions use different exe names; treat both as "self".
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            currentName, "WindowsExtendQuickSetting", "WindowsExtendQuickSetting.App",
            "WindowsExtendQuickSetting.Native"
        };
        var result = new List<Process>();
        try
        {
            foreach (var name in names)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    if (process.Id != currentId) result.Add(process);
                    else process.Dispose();
                }
            }
        }
        catch { /* enumeration failure: treat as no other instance */ }
        return result;
    }

    private static Version? GetHighestVersion(List<Process> processes)
    {
        Version? highest = null;
        foreach (var process in processes)
        {
            try
            {
                var info = process.MainModule?.FileVersionInfo;
                if (info == null) continue;
                var version = new Version(info.FileMajorPart, info.FileMinorPart,
                    info.FileBuildPart, info.FilePrivatePart);
                if (highest is null || version > highest) highest = version;
            }
            catch { /* module access denied: ignore this instance */ }
        }
        return highest;
    }

    private void ReleaseSingleInstance()
    {
        if (_singleInstanceMutex is null) return;
        try { _singleInstanceMutex.ReleaseMutex(); }
        catch (ApplicationException) { }
        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;
        _activateExistingEvent?.Dispose();
        _activateExistingEvent = null;
        _shutdownForUpgradeEvent?.Dispose();
        _shutdownForUpgradeEvent = null;
    }

    private static void ActivateExistingWindows(List<Process> processes)
    {
        foreach (var process in processes)
        {
            Native.EnumWindows((handle, _) =>
            {
                Native.GetWindowThreadProcessId(handle, out var pid);
                if (pid != process.Id || !Native.IsWindowVisible(handle)) return true;
                Native.ShowWindow(handle, 9); // SW_RESTORE
                Native.SetForegroundWindow(handle);
                return false; // one main window per process is enough
            }, IntPtr.Zero);
        }
    }

    private static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    }

    private void ShowElevationDialog()
    {
        var window = new Window();
        window.Title = "WindowsExtendQuickSetting";

        var stack = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Padding = new Thickness(32),
            Spacing = 16,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        stack.Children.Add(new Microsoft.UI.Xaml.Controls.FontIcon
        {
            Glyph = "\uE730",
            FontSize = 48,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        stack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "需要管理员权限",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        stack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "启用/禁用有线网卡需要管理员权限才能执行。\n" +
                   "Enabling/disabling network adapters requires administrator privileges.\n\n" +
                   "请点击\"重新提权\"按钮，允许应用以管理员身份运行。",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 500
        });

        var retryBtn = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = "重新提权 / Retry",
            HorizontalAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(24, 8, 24, 8)
        };
        retryBtn.Click += (s, e) => ElevateHelper.RequestElevation();
        stack.Children.Add(retryBtn);

        var exitBtn = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = "退出 / Exit",
            HorizontalAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(24, 8, 24, 8)
        };
        exitBtn.Click += (s, e) => window.Close();
        stack.Children.Add(exitBtn);

        window.Content = stack;
        window.Activate();
    }
}
