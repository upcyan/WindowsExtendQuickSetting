using Microsoft.UI.Xaml;
using WindowsEthernetControl.Services;

namespace WindowsEthernetControl;

public partial class App : Application
{
    public static SettingsService Settings { get; } = new();
    public static NetworkService Network { get; } = new();
    public static bool IsElevated { get; private set; }
    public static Microsoft.UI.Dispatching.DispatcherQueue? MainDispatcherQueue { get; set; }

    private TrayService? _trayService;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        IsElevated = ElevateHelper.CheckElevated();

        if (!IsElevated)
        {
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

        Settings.Load();
        Network.StartMonitoring();

        _trayService = new TrayService();
        _trayService.Initialize();
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
