using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace WindowsEthernetControl.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly Services.SettingsService _settings = App.Settings;
    private bool _initializing = true;

    public SettingsWindow()
    {
        this.InitializeComponent();

        this.Title = "设置 / Settings";

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow != null)
        {
            appWindow.IsShownInSwitchers = true;
            appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 480, Height = 400 });
            appWindow.Move(new Windows.Graphics.PointInt32
            {
                X = (DisplayArea.Primary.WorkArea.Width - 480) / 2,
                Y = (DisplayArea.Primary.WorkArea.Height - 400) / 2
            });
        }

        this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        StartToggle.IsOn = _settings.IsStartWithWindowsEnabled();
        LangCombo.SelectedIndex = _settings.Settings.Language == "zh-CN" ? 0 : 1;
        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = version == null ? "WindowsExtendQuickSetting" : $"WindowsExtendQuickSetting {version.Major}.{version.Minor}.{version.Build}";
        _initializing = false;
    }

    private void StartToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _settings.SetStartWithWindows(StartToggle.IsOn);
    }

    private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _settings.Settings.Language = LangCombo.SelectedIndex == 0 ? "zh-CN" : "en-US";
        _settings.Save();
    }
}
