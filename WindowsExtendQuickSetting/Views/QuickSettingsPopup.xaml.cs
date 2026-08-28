using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using WindowsEthernetControl.Models;
using WindowsEthernetControl.Services;
using WinRT.Interop;
using WinRT;
using Windows.Devices.Radios;
using Color = Windows.UI.Color;
using Colors = Microsoft.UI.Colors;

using static WindowsEthernetControl.Services.NetworkService;

namespace WindowsEthernetControl.Views;

public sealed partial class QuickSettingsPopup : Window
{
    private readonly NetworkService _networkService = App.Network;
    private NetworkAdapter? _selectedAdapter;
    private NetworkAdapter? _selectedWirelessAdapter;
    private NetworkAdapter? _activeNetworkAdapter;
    private NetworkAdapter? _selectedUsbAdapter;
    private bool _isEthernetEnabled = true;
    private bool _isOperationInProgress;
    private AppWindow? _appWindow;

    internal StackPanel CardsPanel = new() { Spacing = 14 };
    private Border? _toastHost;
    private TextBlock? _toastText;
    private Button? _toastActionButton;
    private CancellationTokenSource? _toastCts;
    private TextBox? _savedWifiSearchBox;
    private List<NetworkService.SavedWifiProfile> _savedWifiProfiles = new();
    private ScrollViewer? _contentScroll;

    private StackPanel? _adapterListPanel;
    private TextBlock? _adapterNameText;
    private TextBlock? _adapterStatusText;
    private TextBlock? _adapterIpText;
    private Button? _toggleBtn;
    private Button? _wifiButton;
    private Button? _usbButton;
    private Button? _ethernetExpandButton;
    private Button? _wifiExpandButton;
    private Button? _usbExpandButton;
    private Button? _bluetoothButton;
    private Button? _bluetoothExpandButton;
    private TextBlock? _ethernetCaption;
    private TextBlock? _wifiCaption;
    private TextBlock? _usbCaption;
    private TextBlock? _bluetoothCaption;
    private TextBlock? _bluetoothStatus;
    private TextBlock? _networkNameText;
    private TextBlock? _networkAddressText;
    private TextBlock? _networkDnsText;
    private TextBlock? _networkDohText;
    private Border? _currentNetworkHost;
    private StackPanel? _networkDetailsPanel;
    private FrameworkElement? _networkDohControlPanel;
    private Button? _currentNetworkExpandButton;
    private bool _currentNetworkExpanded = true;
    private Button? _dohToggle;
    private TextBox? _dohServerInput;
    private TextBox? _dohTemplateInput;
    private string? _editingDohServer;
    private WifiState _wifiState = WifiState.Disabled;
    private string? _wifiSsid;
    private string? _wifiInterfaceName;
    private StackPanel? _dohSystemList;
    private TextBox? _dohImportBox;
    private AutoSuggestBox? _dohPrimaryCombo;
    private AutoSuggestBox? _dohBackupCombo;
    private StackPanel? _dohAddModeContent;
    private Microsoft.UI.Xaml.Controls.Primitives.ToggleButton? _dohQuickModeButton;
    private Microsoft.UI.Xaml.Controls.Primitives.ToggleButton? _dohManualModeButton;
    private bool _dohManualAddMode;
    private TextBox? _dohSystemSearchBox;
    private List<SystemDohServer> _systemDohCache = new();
    private bool _updatingDohSelections;
    private Radio? _bluetoothRadio;
    private Radio? _wifiRadio;
    private Border? _adapterDetailsCard;
    private Expander? _adapterExpander;
    private StackPanel? _networkExtrasPanel;
    private bool _showWirelessDetails;
    private bool _showUsbDetails;
    private bool _showBluetoothDetails;
    private TextBlock? _titleText;
    private Button? _navigationButton;
    private bool _showingSettings;
    private bool _showingDohSettings;
    private int _pageChangeVersion;
    private CancellationTokenSource? _windowAnimationCts;
    private DesktopAcrylicController? _acrylicController;
    private MicaController? _micaController;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private SolidColorBrush ActiveBrush => (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"];
    private SolidColorBrush InactiveBrush => (SolidColorBrush)Application.Current.Resources["ControlFillColorDefaultBrush"];
    private Brush ControlBorderBrush => (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];

    public QuickSettingsPopup()
    {
        ExtendsContentIntoTitleBar = true;
        this.Activated += OnActivated;
        this.Closed += OnClosed;

        var root = new Grid
        {
            Background = new SolidColorBrush(Colors.Transparent)
        };
        InitializeBackdrop(root.ActualTheme);
        root.ActualThemeChanged += (_, _) => ConfigureBackdropTheme(root.ActualTheme);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new Grid { Height = 8 };
        _titleText = new TextBlock
        {
            Text = "",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        };
        titleBar.Children.Add(_titleText);
        SetTitleBar(titleBar);
        root.Children.Add(titleBar);

        _contentScroll = new ScrollViewer { Padding = new Thickness(24) };
        Grid.SetRow(_contentScroll, 1);
        _contentScroll.Content = CardsPanel;
        root.Children.Add(_contentScroll);

        _currentNetworkHost = new Border
        {
            Margin = new Thickness(24, 0, 24, 10),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(_currentNetworkHost, 2);
        root.Children.Add(_currentNetworkHost);

        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _navigationButton = new Button
        {
            Width = 36, Height = 36, Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Content = new FontIcon { Glyph = "\uE713", FontSize = 16 }
        };
        _navigationButton.Click += SettingsBtn_Click;
        Grid.SetColumn(_navigationButton, 1);
        footerGrid.Children.Add(_navigationButton);
        var footer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 6, 24, 6), Child = footerGrid
        };
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        _toastText = new TextBlock
        {
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 220, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _toastActionButton = new Button { Visibility = Visibility.Collapsed, Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center };
        var toastContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        toastContent.Children.Add(_toastText);
        toastContent.Children.Add(_toastActionButton);
        _toastHost = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(238, 35, 35, 38)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(18, 0, 18, 54),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
            Child = toastContent
        };
        Grid.SetRowSpan(_toastHost, 4);
        Canvas.SetZIndex(_toastHost, 100);
        root.Children.Add(_toastHost);

        this.Content = root;

        SetupWindow();
        BuildUI();
        RefreshAdapters();

        _networkService.AdaptersChanged += OnAdaptersChanged;
    }

    private void SetupWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var borderColor = unchecked((int)0xFFFFFFFE);
        NativeMethods.DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));
        var nonClientRenderingDisabled = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, 2, ref nonClientRenderingDisabled, sizeof(int));
        var style = NativeMethods.GetWindowLong(hwnd, -16);
        style &= unchecked((int)~(0x00C00000u | 0x00040000u | 0x00800000u | 0x00400000u));
        NativeMethods.SetWindowLong(hwnd, -16, style);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow != null)
        {
            _appWindow.IsShownInSwitchers = false;
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
        }
    }

    public void PositionNearTray()
    {
        try
        {
            if (_appWindow == null) return;
            var workArea = DisplayArea.Primary.WorkArea;
            var panelW = 367;
            var panelH = _showingSettings || _showingDohSettings ? 650 : _adapterDetailsCard?.Visibility == Visibility.Visible ? 660 : 570;
            var x = workArea.X + workArea.Width - panelW - 16;
            var y = workArea.Y + workArea.Height - panelH - 8;
            if (x < workArea.X) x = workArea.X;
            if (y < workArea.Y) y = workArea.Y;
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, panelW, panelH));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_backdropConfiguration != null)
            _backdropConfiguration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _networkService.AdaptersChanged -= OnAdaptersChanged;
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _windowAnimationCts?.Cancel();
        _windowAnimationCts?.Dispose();
        _acrylicController?.Dispose();
        _micaController?.Dispose();
        _acrylicController = null;
        _micaController = null;
        _backdropConfiguration = null;
    }

    private async void ShowToast(string message, string? actionText = null, Func<Task>? action = null, int durationMs = 2600)
    {
        if (_toastHost == null || _toastText == null || _toastActionButton == null || string.IsNullOrWhiteSpace(message)) return;
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;
        _toastText.Text = message;
        _toastActionButton.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
        _toastActionButton.Content = actionText;
        _toastActionButton.Click -= ToastActionButton_Click;
        _toastAction = action;
        _toastActionButton.Click += ToastActionButton_Click;
        _toastHost.Opacity = 1;
        _toastHost.Visibility = Visibility.Visible;
        try
        {
            await Task.Delay(durationMs, token);
            for (var step = 9; step >= 0; step--)
            {
                token.ThrowIfCancellationRequested();
                _toastHost.Opacity = step / 10d;
                await Task.Delay(20, token);
            }
            _toastHost.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
    }

    private Func<Task>? _toastAction;

    private async void ToastActionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _toastAction;
        if (action == null || _toastActionButton == null) return;
        _toastActionButton.IsEnabled = false;
        _toastCts?.Cancel();
        await action();
        _toastActionButton.IsEnabled = true;
    }

    private void OnAdaptersChanged(object? sender, EventArgs e) =>
        App.MainDispatcherQueue?.TryEnqueue(RefreshAdapters);

    private void BuildUI()
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        var quickGrid = new Grid { ColumnSpacing = 12 };
        for (var i = 0; i < 3; i++)
            quickGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        quickGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        quickGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var ethernetTile = CreateSplitTile("\uE839", isZh ? "未连接" : "Disconnected", out _toggleBtn, out _ethernetExpandButton, out _ethernetCaption);
        _toggleBtn.Click += EthernetToggle_Click;
        _ethernetExpandButton.Click += (_, _) => ShowAdapterDetails(false);
        ToolTipService.SetToolTip(_ethernetExpandButton, isZh ? "查看有线网卡" : "Show Ethernet adapters");
        quickGrid.Children.Add(ethernetTile);

        var wifiTile = CreateSplitTile("\uE701", isZh ? "未连接" : "Disconnected", out _wifiButton, out _wifiExpandButton, out _wifiCaption);
        _wifiButton.Click += WifiToggle_Click;
        _wifiExpandButton.Click += (_, _) => ShowAdapterDetails(true);
        ToolTipService.SetToolTip(_wifiExpandButton, isZh ? "查看并连接 Wi-Fi 网络（SSID）" : "Show and connect to Wi-Fi networks (SSIDs)");
        Grid.SetColumn(wifiTile, 1);
        quickGrid.Children.Add(wifiTile);

        var bluetoothTile = CreateSplitTile("\uE702", isZh ? "未连接" : "Disconnected", out _bluetoothButton, out _bluetoothExpandButton, out _bluetoothCaption);
        _bluetoothButton.Click += BluetoothButton_Click;
        _bluetoothExpandButton.Click += (_, _) => ShowBluetoothDetails();
        ToolTipService.SetToolTip(_bluetoothExpandButton, isZh ? "管理蓝牙设备" : "Manage Bluetooth devices");
        Grid.SetColumn(bluetoothTile, 2);
        quickGrid.Children.Add(bluetoothTile);

        var usbTile = CreateSplitTile("\uE88E", isZh ? "未连接" : "Disconnected", out _usbButton, out _usbExpandButton, out _usbCaption);
        _usbButton.Click += UsbToggle_Click;
        _usbExpandButton.Click += (_, _) => ShowUsbDetails();
        ToolTipService.SetToolTip(_usbButton, isZh ? "启用或断开手机 USB 网络共享" : "Enable or disconnect phone USB tethering");
        ToolTipService.SetToolTip(_usbExpandButton, isZh ? "查看 USB/RNDIS 网络共享设备" : "Show USB/RNDIS tethering devices");
        Grid.SetRow(usbTile, 1);
        quickGrid.Children.Add(usbTile);
        CardsPanel.Children.Add(quickGrid);

        var networkInfo = new StackPanel { Spacing = 5 };
        var networkHeader = new Grid();
        networkHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        networkHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        networkHeader.Children.Add(new TextBlock
        {
            Text = isZh ? "当前网络" : "Current network", FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        _currentNetworkExpandButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE70D", FontSize = 11 },
            Width = 30, Height = 28, Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Center
        };
        SetExpandRotation(_currentNetworkExpandButton, _currentNetworkExpanded ? 180 : 0);
        ToolTipService.SetToolTip(_currentNetworkExpandButton, isZh ? "收起当前网络详情" : "Collapse current network details");
        _currentNetworkExpandButton.Click += (_, _) => ToggleCurrentNetworkDetails();
        Grid.SetColumn(_currentNetworkExpandButton, 1);
        networkHeader.Children.Add(_currentNetworkExpandButton);
        _networkNameText = new TextBlock { FontSize = 12 };
        _networkAddressText = new TextBlock { FontSize = 11, Opacity = 0.72, TextWrapping = TextWrapping.Wrap };
        _networkDnsText = new TextBlock { FontSize = 11, Opacity = 0.72, TextWrapping = TextWrapping.Wrap };
        _networkDohText = new TextBlock { FontSize = 11, Opacity = 0.78, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        networkInfo.Children.Add(_networkNameText);
        _networkDetailsPanel = new StackPanel { Spacing = 3, Visibility = _currentNetworkExpanded ? Visibility.Visible : Visibility.Collapsed };
        _networkDetailsPanel.Children.Add(_networkAddressText);
        _networkDetailsPanel.Children.Add(_networkDnsText);
        _networkDetailsPanel.Children.Add(_networkDohText);
        networkInfo.Children.Add(_networkDetailsPanel);
        var infoGrid = new Grid { ColumnSpacing = 10 };
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoGrid.Children.Add(networkInfo);
        var dohPanel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var dohHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        dohHeader.Children.Add(new TextBlock { Text = "DoH", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var dohSettingsButton = new Button
        {
            Width = 24, Height = 24, Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE713", FontSize = 12 }
        };
        ToolTipService.SetToolTip(dohSettingsButton, isZh ? "DoH 设置" : "DoH settings");
        dohSettingsButton.Click += (_, _) =>
        {
            _showingSettings = false;
            _showingDohSettings = true;
            ShowCurrentPage();
        };
        dohHeader.Children.Add(dohSettingsButton);
        dohPanel.Children.Add(dohHeader);
        _dohToggle = new Button { Width = 42, Height = 22, Padding = new Thickness(3, 2, 3, 2), CornerRadius = new CornerRadius(11) };
        _dohToggle.Click += DohToggle_Click;
        dohPanel.Children.Add(_dohToggle);
        _networkDohControlPanel = dohPanel;
        dohPanel.Visibility = _currentNetworkExpanded ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(dohPanel, 1);
        infoGrid.Children.Add(dohPanel);
        var currentNetworkContent = new StackPanel { Spacing = 8 };
        currentNetworkContent.Children.Add(networkHeader);
        currentNetworkContent.Children.Add(infoGrid);
        var currentNetworkCard = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12), Child = currentNetworkContent
        };

        _bluetoothStatus = new TextBlock { Visibility = Visibility.Collapsed };

        var detailPanel = new StackPanel { Spacing = 10 };
        var adapterContent = new StackPanel { Spacing = 10 };
        var infoPanel = new StackPanel { Spacing = 2 };
        _adapterNameText = new TextBlock { Text = isZh ? "正在检测中..." : "Detecting...", FontSize = 13, Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] };
        _adapterStatusText = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 175, 175, 175)) };
        _adapterIpText = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 175, 175, 175)) };
        infoPanel.Children.Add(_adapterNameText);
        infoPanel.Children.Add(_adapterStatusText);
        infoPanel.Children.Add(_adapterIpText);
        adapterContent.Children.Add(infoPanel);
        _adapterListPanel = new StackPanel { Spacing = 6 };
        adapterContent.Children.Add(_adapterListPanel);
        _adapterExpander = new Expander
        {
            Header = isZh ? "网络适配器" : "Network adapters",
            IsExpanded = false,
            Content = adapterContent
        };
        detailPanel.Children.Add(_adapterExpander);
        _networkExtrasPanel = new StackPanel { Spacing = 8 };
        detailPanel.Children.Add(_networkExtrasPanel);
        _adapterDetailsCard = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(14),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1), Child = detailPanel,
            Visibility = Visibility.Collapsed
        };
        CardsPanel.Children.Add(_adapterDetailsCard);
        if (_currentNetworkHost != null)
        {
            _currentNetworkHost.Child = currentNetworkCard;
            _currentNetworkHost.Visibility = Visibility.Visible;
        }

        UpdateToggleVisual();
        UpdateDohVisual();
        _ = RefreshBluetoothAsync();
    }

    private Grid CreateSplitTile(string glyph, string label, out Button mainButton, out Button expandButton, out TextBlock caption)
    {
        var tile = new Grid { RowSpacing = 7 };
        tile.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        tile.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainButton = new Button
        {
            Height = 48, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Background = InactiveBrush,
            BorderBrush = ControlBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8, 0, 0, 8),
            Padding = new Thickness(0), Content = new FontIcon { Glyph = glyph, FontSize = 17 }
        };
        expandButton = new Button
        {
            Height = 48, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Background = InactiveBrush,
            BorderThickness = new Thickness(1, 0, 0, 0),
            BorderBrush = ControlBorderBrush,
            CornerRadius = new CornerRadius(0, 8, 8, 0), Padding = new Thickness(0),
            Content = CreateChevronIcon()
        };
        ConfigureTileHover(mainButton);
        ConfigureTileHover(expandButton);
        Grid.SetColumn(expandButton, 1);
        caption = new TextBlock
        {
            Text = label, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(caption, 1);
        Grid.SetColumnSpan(caption, 2);
        tile.Children.Add(mainButton);
        tile.Children.Add(expandButton);
        tile.Children.Add(caption);
        return tile;
    }

    private async Task RefreshBluetoothAsync()
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        _bluetoothRadio = await BluetoothService.GetRadioAsync();
        if (_bluetoothStatus == null || _bluetoothButton == null) return;
        if (_bluetoothRadio == null)
        {
            _bluetoothStatus.Text = isZh ? "不可用或未授权" : "Unavailable or access denied";
            _bluetoothButton.IsEnabled = false;
            return;
        }
        var enabled = _bluetoothRadio.State == RadioState.On;
        _bluetoothStatus.Text = enabled ? (isZh ? "已开启" : "On") : (isZh ? "已关闭" : "Off");
        _bluetoothButton.Background = enabled ? ActiveBrush : InactiveBrush;
        if (_bluetoothExpandButton != null) _bluetoothExpandButton.Background = enabled ? ActiveBrush : InactiveBrush;
        UpdateTileForeground(_bluetoothButton, _bluetoothExpandButton, enabled);
        if (_bluetoothCaption != null) _bluetoothCaption.Text = enabled
            ? (isZh ? "已开启" : "On") : (isZh ? "已关闭" : "Off");
        _bluetoothButton.Foreground = enabled
            ? new SolidColorBrush(Colors.White)
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        _bluetoothButton.Content = new FontIcon
        {
            Glyph = "\uE702",
            FontSize = 17,
            Foreground = enabled
                ? new SolidColorBrush(Colors.White)
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        };
        ToolTipService.SetToolTip(_bluetoothButton, enabled ? (isZh ? "蓝牙已开启" : "Bluetooth on") : (isZh ? "蓝牙已关闭" : "Bluetooth off"));
        _bluetoothButton.IsEnabled = true;
    }

    private async void BluetoothButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bluetoothRadio == null || _bluetoothButton == null) return;
        _bluetoothButton.IsEnabled = false;
        var success = await BluetoothService.SetExclusivelyEnabledAsync(
            _bluetoothRadio, _bluetoothRadio.State != RadioState.On);
        if (!success)
            ShowToast(App.Settings.Settings.Language == "zh-CN" ? "蓝牙操作失败" : "Bluetooth operation failed");
        await RefreshBluetoothAsync();
    }

    private void RefreshAdapters()
    {
        var ethernetAdapters = _networkService.GetEthernetAdapters();
        var wirelessAdapters = _networkService.GetWirelessAdapters();
        var usbAdapters = _networkService.GetUsbTetheringAdapters();
        var isZh = App.Settings.Settings.Language == "zh-CN";
        _selectedAdapter = ethernetAdapters.FirstOrDefault(a => a.IsUp) ?? ethernetAdapters.FirstOrDefault();
        _selectedWirelessAdapter = wirelessAdapters.FirstOrDefault(a => a.IsUp) ?? wirelessAdapters.FirstOrDefault();
        _selectedUsbAdapter = usbAdapters.FirstOrDefault(a => a.IsUp) ?? usbAdapters.FirstOrDefault();
        _activeNetworkAdapter = ethernetAdapters.Concat(wirelessAdapters).Concat(usbAdapters)
            .Where(a => a.IsUp)
            .OrderByDescending(a => a.Gateways.Count > 0)
            .FirstOrDefault();
        _isEthernetEnabled = _selectedAdapter?.IsUp == true;

        if (_toggleBtn != null) _toggleBtn.IsEnabled = _selectedAdapter != null;
        if (_ethernetCaption != null) _ethernetCaption.Text = _selectedAdapter?.IsUp == true
            ? (isZh ? $"以太网 · {_selectedAdapter.Name}" : $"Ethernet · {_selectedAdapter.Name}")
            : (isZh ? "以太网 · 未连接" : "Ethernet · Disconnected");
        if (_wifiButton != null)
        {
            _wifiButton.IsEnabled = _selectedWirelessAdapter != null;
            if (_wifiCaption != null)
                _wifiCaption.Text = _selectedWirelessAdapter == null
                    ? (isZh ? "Wi-Fi · 不可用" : "Wi-Fi · Unavailable")
                    : (isZh ? "Wi-Fi · 检测中…" : "Wi-Fi · Detecting…");
        }
        _ = RefreshWifiStateAsync();

        if (_usbButton != null)
        {
            // Keep the button actionable when no adapter is present so the user gets
            // an explicit phone-side USB tethering instruction.
            _usbButton.IsEnabled = true;
            var usbEnabled = _selectedUsbAdapter?.IsUp == true;
            _usbButton.Background = usbEnabled ? ActiveBrush : InactiveBrush;
            if (_usbExpandButton != null) _usbExpandButton.Background = usbEnabled ? ActiveBrush : InactiveBrush;
            _usbButton.Content = new FontIcon
            {
                Glyph = "\uE88E", FontSize = 17,
                Foreground = usbEnabled
                    ? new SolidColorBrush(Colors.White)
                    : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
            };
            UpdateTileForeground(_usbButton, _usbExpandButton, usbEnabled);
            if (_usbCaption != null) _usbCaption.Text = usbEnabled
                ? (isZh ? "USB 共享 · 已连接" : "USB tethering · Connected")
                : (isZh ? "USB 共享 · 未连接" : "USB tethering · Disconnected");
        }

        UpdateToggleVisual();
        UpdateNetworkDetails();
        if (_adapterDetailsCard?.Visibility == Visibility.Visible)
        {
            if (_showUsbDetails) PopulateUsbAdapterDetails();
            else if (_showBluetoothDetails) { }
            else PopulateAdapterDetails(_showWirelessDetails);
        }
    }

    private void UpdateNetworkDetails()
    {
        if (_networkNameText == null || _networkAddressText == null || _networkDnsText == null) return;
        var isZh = App.Settings.Settings.Language == "zh-CN";
        if (_activeNetworkAdapter == null)
        {
            _networkNameText.Text = isZh ? "未连接" : "Not connected";
            _networkAddressText.Text = "";
            _networkDnsText.Text = "";
            if (_networkDohText != null) { _networkDohText.Text = ""; _networkDohText.Visibility = Visibility.Collapsed; }
            return;
        }
        var type = _activeNetworkAdapter.IsWireless ? "Wi-Fi" : (_activeNetworkAdapter.IsUsbTethering ? (isZh ? "USB 网络共享" : "USB tethering") : (isZh ? "有线网络" : "Ethernet"));
        _networkNameText.Text = $"{type} · {_activeNetworkAdapter.Name}";
        _networkAddressText.Text = $"IP: {_activeNetworkAdapter.IpAddresses.FirstOrDefault() ?? "—"}    " +
            $"{(isZh ? "网关" : "Gateway")}: {_activeNetworkAdapter.Gateways.FirstOrDefault() ?? "—"}";
        _networkDnsText.Text = $"DNS: {string.Join(", ", _activeNetworkAdapter.DnsServers.Take(2).DefaultIfEmpty("—"))}";
        _ = UpdateEffectiveDohInfoAsync(_activeNetworkAdapter, isZh);
    }

    private void ToggleCurrentNetworkDetails()
    {
        _currentNetworkExpanded = !_currentNetworkExpanded;
        if (_networkDetailsPanel != null)
            _networkDetailsPanel.Visibility = _currentNetworkExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (_networkDohControlPanel != null)
            _networkDohControlPanel.Visibility = _currentNetworkExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (_currentNetworkExpandButton != null)
        {
            SetExpandRotation(_currentNetworkExpandButton, _currentNetworkExpanded ? 180 : 0);
            ToolTipService.SetToolTip(_currentNetworkExpandButton, _currentNetworkExpanded
                ? (App.Settings.Settings.Language == "zh-CN" ? "收起当前网络详情" : "Collapse current network details")
                : (App.Settings.Settings.Language == "zh-CN" ? "展开当前网络详情" : "Expand current network details"));
        }
    }

    private async Task UpdateEffectiveDohInfoAsync(NetworkAdapter adapter, bool isZh)
    {
        if (_networkDohText == null) return;
        var target = _networkDohText;
        if (!App.Settings.Settings.DnsOverHttpsEnabled)
        {
            target.Visibility = Visibility.Collapsed;
            target.Text = "";
            return;
        }
        target.Visibility = Visibility.Visible;
        target.Text = isZh ? "DoH：正在确认生效状态…" : "DoH: Checking effective status…";
        var system = await GetSystemDohServersAsync();
        if (!ReferenceEquals(target, _networkDohText) || !ReferenceEquals(adapter, _activeNetworkAdapter)) return;
        var templates = system.ToDictionary(item => item.Server, item => item.Template, StringComparer.OrdinalIgnoreCase);
        var effective = adapter.DnsServers.Where(templates.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        target.Text = effective.Count > 0
            ? (isZh ? $"DoH：已生效 · {string.Join(", ", effective)}" : $"DoH: Active · {string.Join(", ", effective)}")
            : (isZh ? "DoH：已开启，但当前 DNS 未匹配系统 DoH 模板" : "DoH: Enabled, but current DNS does not match a system DoH template");
    }

    private void UpdateDohVisual()
    {
        if (_dohToggle == null) return;
        var enabled = App.Settings.Settings.DnsOverHttpsEnabled;
        UpdateCompactToggle(_dohToggle, enabled);
        ToolTipService.SetToolTip(_dohToggle, enabled ? "DNS over HTTPS: On" : "DNS over HTTPS: Off");
    }

    private async void DohToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_dohToggle == null || _isOperationInProgress) return;
        _isOperationInProgress = true;
        _dohToggle.IsEnabled = false;
        var enabled = !App.Settings.Settings.DnsOverHttpsEnabled;
        var success = await NetworkService.SetDohEnabledAsync(enabled);
        if (success)
        {
            App.Settings.Settings.DnsOverHttpsEnabled = enabled;
            App.Settings.Save();
            UpdateDohVisual();
            UpdateNetworkDetails();
        }
        ShowToast(success
            ? (enabled ? "DoH enabled" : "DoH disabled")
            : "Unable to change DoH");
        _isOperationInProgress = false;
        _dohToggle.IsEnabled = true;
    }

    private async Task RefreshWifiStateAsync()
    {
        if (_selectedWirelessAdapter == null)
        {
            _wifiState = WifiState.Disabled;
            _wifiSsid = null;
            _wifiInterfaceName = null;
        }
        else
        {
            _wifiRadio = await BluetoothService.GetWifiRadioAsync();
            _wifiSsid = await NetworkService.GetConnectedWifiSsidAsync();
            // OperationalStatus.Up is authoritative: a connected adapter cannot be
            // considered disabled even if the Radio API reports stale state.
            _wifiState = _selectedWirelessAdapter.IsUp || !string.IsNullOrWhiteSpace(_wifiSsid)
                ? WifiState.Connected
                : _wifiRadio?.State == RadioState.Off
                    ? WifiState.Disabled
                    : WifiState.EnabledDisconnected;
            _wifiInterfaceName = _selectedWirelessAdapter.InterfaceName;
        }
        App.MainDispatcherQueue?.TryEnqueue(UpdateWifiTileVisual);
    }

    private void UpdateWifiTileVisual()
    {
        if (_wifiButton == null) return;
        var isZh = App.Settings.Settings.Language == "zh-CN";
        var disabled = _wifiState == WifiState.Disabled;
        var connected = _wifiState == WifiState.Connected;

        _wifiButton.Background = disabled ? InactiveBrush : ActiveBrush;
        if (_wifiExpandButton != null) _wifiExpandButton.Background = disabled ? InactiveBrush : ActiveBrush;
        UpdateTileForeground(_wifiButton, _wifiExpandButton, !disabled);

        var grid = new Grid();
        var icon = new FontIcon
        {
            Glyph = "\uE701",
            FontSize = 17,
            Foreground = disabled
                ? (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                : new SolidColorBrush(Colors.White)
        };
        grid.Children.Add(icon);

        if (disabled || !connected)
        {
            grid.Children.Add(new TextBlock
            {
                Text = disabled ? "×" : "?",
                FontSize = disabled ? 12 : 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(disabled
                    ? Color.FromArgb(255, 220, 90, 90)
                    : Color.FromArgb(255, 245, 195, 55)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 2, 1)
            });
        }

        _wifiButton.Content = grid;

        if (_wifiCaption != null)
        {
            _wifiCaption.Text = disabled
                ? (isZh ? "Wi-Fi · 已禁用" : "Wi-Fi · Disabled")
                : connected
                    ? $"Wi-Fi · {_wifiSsid ?? _selectedWirelessAdapter?.Name ?? "SSID"}"
                    : (isZh ? "Wi-Fi · 未连接" : "Wi-Fi · Not connected");
            _wifiCaption.Foreground = disabled
                ? (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        }

        ToolTipService.SetToolTip(_wifiButton, disabled
            ? (isZh ? "Wi-Fi 已禁用，点击启用" : "Wi-Fi disabled, click to enable")
            : connected
                ? (isZh ? $"已连接：{_wifiSsid}" : $"Connected: {_wifiSsid}")
                : (isZh ? "Wi-Fi 已开启，未连接到网络" : "Wi-Fi on, not connected"));
    }

    private void UpdateToggleVisual()
    {
        if (_toggleBtn == null) return;
        _toggleBtn.Background = _isEthernetEnabled ? ActiveBrush : InactiveBrush;
        if (_ethernetExpandButton != null) _ethernetExpandButton.Background = _isEthernetEnabled ? ActiveBrush : InactiveBrush;
        var isZh = App.Settings.Settings.Language == "zh-CN";
        _toggleBtn.Content = new FontIcon
        {
            Glyph = "\uE839", FontSize = 17,
            Foreground = _isEthernetEnabled
                ? new SolidColorBrush(Colors.White)
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        };
        UpdateTileForeground(_toggleBtn, _ethernetExpandButton, _isEthernetEnabled);
        ToolTipService.SetToolTip(_toggleBtn, _isEthernetEnabled
            ? (isZh ? "有线网卡已启用" : "Ethernet enabled")
            : (isZh ? "有线网卡已关闭" : "Ethernet off"));
    }

    public void ShowAndActivate()
    {
        PositionNearTray();
        var hwnd = WindowNative.GetWindowHandle(this);
        PopupNativeMethods.ShowWindow(hwnd, PopupNativeMethods.SW_RESTORE);
        Activate();
        PopupNativeMethods.BringWindowToTop(hwnd);
        PopupNativeMethods.SetForegroundWindow(hwnd);
    }

    public void ToggleVisibility()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        if (PopupNativeMethods.IsWindowVisible(hwnd) && !PopupNativeMethods.IsIconic(hwnd))
        {
            PopupNativeMethods.ShowWindow(hwnd, PopupNativeMethods.SW_MINIMIZE);
            return;
        }
        ShowAndActivate();
    }

    private void ShowUsbDetails()
    {
        if (_adapterDetailsCard == null) return;
        if (_adapterDetailsCard.Visibility == Visibility.Visible && _showUsbDetails)
        {
            _adapterDetailsCard.Visibility = Visibility.Collapsed;
            SetExpandRotation(_usbExpandButton, 0);
            AnimateWindowNearTray();
            return;
        }
        _showUsbDetails = true;
        _showWirelessDetails = false;
        _showBluetoothDetails = false;
        if (_adapterExpander != null) _adapterExpander.Visibility = Visibility.Visible;
        if (_adapterExpander != null) _adapterExpander.IsExpanded = false;
        _adapterDetailsCard.Visibility = Visibility.Visible;
        SetExpandRotation(_ethernetExpandButton, 0);
        SetExpandRotation(_wifiExpandButton, 0);
        SetExpandRotation(_usbExpandButton, 90);
        SetExpandRotation(_bluetoothExpandButton, 0);
        PopulateUsbAdapterDetails();
        AnimateDetailsCardIn();
        AnimateWindowNearTray();
    }
    private void ShowAdapterDetails(bool wireless)
    {
        if (_adapterDetailsCard == null) return;
        if (_adapterDetailsCard.Visibility == Visibility.Visible && !_showUsbDetails && !_showBluetoothDetails && _showWirelessDetails == wireless)
        {
            _adapterDetailsCard.Visibility = Visibility.Collapsed;
            SetExpandRotation(wireless ? _wifiExpandButton : _ethernetExpandButton, 0);
            AnimateWindowNearTray();
            return;
        }
        _showWirelessDetails = wireless;
        _showUsbDetails = false;
        _showBluetoothDetails = false;
        if (_adapterExpander != null) _adapterExpander.Visibility = Visibility.Visible;
        if (_adapterExpander != null) _adapterExpander.IsExpanded = false;
        _adapterDetailsCard.Visibility = Visibility.Visible;
        SetExpandRotation(_ethernetExpandButton, wireless ? 0 : 90);
        SetExpandRotation(_wifiExpandButton, wireless ? 90 : 0);
        SetExpandRotation(_usbExpandButton, 0);
        SetExpandRotation(_bluetoothExpandButton, 0);
        PopulateAdapterDetails(wireless);
        AnimateDetailsCardIn();
        AnimateWindowNearTray();
    }

    private void ShowBluetoothDetails()
    {
        if (_adapterDetailsCard == null) return;
        if (_adapterDetailsCard.Visibility == Visibility.Visible && _showBluetoothDetails)
        {
            _adapterDetailsCard.Visibility = Visibility.Collapsed;
            SetExpandRotation(_bluetoothExpandButton, 0);
            AnimateWindowNearTray();
            return;
        }
        _showBluetoothDetails = true;
        _showWirelessDetails = false;
        _showUsbDetails = false;
        _adapterDetailsCard.Visibility = Visibility.Visible;
        if (_adapterExpander != null) _adapterExpander.Visibility = Visibility.Collapsed;
        _adapterListPanel?.Children.Clear();
        _networkExtrasPanel?.Children.Clear();
        SetExpandRotation(_ethernetExpandButton, 0);
        SetExpandRotation(_wifiExpandButton, 0);
        SetExpandRotation(_usbExpandButton, 0);
        SetExpandRotation(_bluetoothExpandButton, 90);
        _ = PopulateBluetoothDevicesAsync();
        AnimateDetailsCardIn();
        AnimateWindowNearTray();
    }

    private async Task PopulateBluetoothDevicesAsync()
    {
        if (_networkExtrasPanel == null || !_showBluetoothDetails) return;
        var target = _networkExtrasPanel;
        var isZh = App.Settings.Settings.Language == "zh-CN";
        target.Children.Clear();

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = isZh ? "蓝牙设备" : "Bluetooth devices",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var refresh = new Button { Content = isZh ? "扫描" : "Scan", Padding = new Thickness(10, 4, 10, 4) };
        refresh.Click += async (_, _) => await PopulateBluetoothDevicesAsync();
        refresh.IsEnabled = false;
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        target.Children.Add(header);

        var transferRow = new Grid { ColumnSpacing = 8 };
        transferRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        transferRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sendFile = new Button { Content = isZh ? "发送文件" : "Send files", HorizontalAlignment = HorizontalAlignment.Stretch };
        var receiveFile = new Button { Content = isZh ? "接收文件" : "Receive files", HorizontalAlignment = HorizontalAlignment.Stretch };
        sendFile.Click += (_, _) =>
        {
            if (!BluetoothService.LaunchFileTransfer(false)) ShowToast(isZh ? "无法打开蓝牙文件传输" : "Could not open Bluetooth File Transfer");
        };
        receiveFile.Click += (_, _) =>
        {
            if (!BluetoothService.LaunchFileTransfer(true)) ShowToast(isZh ? "无法打开蓝牙文件接收" : "Could not open Bluetooth file reception");
        };
        Grid.SetColumn(receiveFile, 1);
        transferRow.Children.Add(sendFile);
        transferRow.Children.Add(receiveFile);
        target.Children.Add(transferRow);

        var loading = new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };
        target.Children.Add(loading);

        var adapters = await BluetoothService.GetBluetoothAdaptersAsync();
        target.Children.Add(new TextBlock
        {
            Text = isZh ? "蓝牙适配器切换（同一时间仅启用一个）" : "Bluetooth adapter switch (one active at a time)",
            FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.78, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
        });
        if (adapters.Count == 0)
            target.Children.Add(new TextBlock { Text = isZh ? "未检测到可切换的蓝牙适配器" : "No switchable Bluetooth adapter found", FontSize = 11, Opacity = 0.65 });
        foreach (var adapter in adapters)
        {
            var isCurrent = adapter.Radio.State == RadioState.On;
            var radioRow = new Grid { ColumnSpacing = 8 };
            radioRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            radioRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            radioRow.Children.Add(new TextBlock
            {
                Text = $"{adapter.Name} · {(isCurrent ? (isZh ? "当前使用" : "Current") : (isZh ? "未使用" : "Inactive"))}",
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            });
            var radioToggle = new Button { Content = isCurrent ? (isZh ? "当前" : "Current") : (isZh ? "切换" : "Switch"), IsEnabled = !isCurrent, Padding = new Thickness(10, 4, 10, 4) };
            radioToggle.Click += async (_, _) =>
            {
                radioToggle.IsEnabled = false;
                var success = await BluetoothService.SwitchBluetoothAdapterAsync(adapter, adapters);
                ShowToast(success ? (isZh ? $"已切换到 {adapter.Name}" : $"Switched to {adapter.Name}") : (isZh ? "蓝牙适配器切换失败" : "Bluetooth adapter switch failed"));
                await RefreshBluetoothAsync();
                await PopulateBluetoothDevicesAsync();
            };
            Grid.SetColumn(radioToggle, 1);
            radioRow.Children.Add(radioToggle);
            target.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 8, 6), Child = radioRow });
        }

        var devices = await BluetoothService.GetDevicesAsync();
        if (!ReferenceEquals(target, _networkExtrasPanel) || !_showBluetoothDetails) return;
        target.Children.Remove(loading);
        refresh.IsEnabled = true;
        if (devices.Count == 0)
        {
            target.Children.Add(new TextBlock
            {
                Text = isZh ? "未发现蓝牙设备，请确认蓝牙已开启并让设备进入配对模式。" : "No Bluetooth devices found. Turn Bluetooth on and put the device in pairing mode.",
                FontSize = 11, Opacity = 0.72, TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var device in devices.Take(20))
            target.Children.Add(CreateBluetoothDeviceRow(device, isZh));
    }

    private Border CreateBluetoothDeviceRow(BluetoothService.DeviceEntry device, bool isZh)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = device.Name, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = device.IsConnected ? (isZh ? "已连接" : "Connected") : device.IsPaired ? (isZh ? "已配对" : "Paired") : (isZh ? "可配对" : "Available"),
            FontSize = 10, Opacity = 0.7
        });
        row.Children.Add(text);
        var action = new Button
        {
            Content = device.IsPaired ? (isZh ? "移除" : "Remove") : (isZh ? "配对" : "Pair"),
            Padding = new Thickness(10, 4, 10, 4)
        };
        action.Click += async (_, _) =>
        {
            action.IsEnabled = false;
            var success = device.IsPaired
                ? await BluetoothService.UnpairAsync(device)
                : await BluetoothService.PairAsync(device);
            ShowToast(success
                ? (device.IsPaired ? (isZh ? "蓝牙设备已移除" : "Bluetooth device removed") : (isZh ? "蓝牙设备已配对" : "Bluetooth device paired"))
                : (isZh ? "蓝牙设备操作失败" : "Bluetooth device operation failed"));
            await PopulateBluetoothDevicesAsync();
        };
        Grid.SetColumn(action, 1);
        row.Children.Add(action);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 8, 7), Child = row
        };
    }

    private static Polyline CreateChevronIcon()
    {
        var points = new PointCollection
        {
            new Windows.Foundation.Point(1, 2),
            new Windows.Foundation.Point(7, 8),
            new Windows.Foundation.Point(13, 2)
        };
        return new Polyline
        {
            Points = points, Width = 14, Height = 10,
            Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 1.2,
            StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static void SetExpandRotation(Button? button, double angle)
    {
        if (button?.Content is not FrameworkElement icon) return;
        icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        icon.RenderTransform = new RotateTransform { Angle = angle };
    }

    private void PopulateAdapterDetails(bool wireless)
    {
        var adapters = wireless ? _networkService.GetWirelessAdapters() : _networkService.GetEthernetAdapters();
        _selectedAdapter = adapters.FirstOrDefault(a => a.IsUp) ?? adapters.FirstOrDefault();
        UpdateSelectedAdapterInfo();
        _adapterListPanel?.Children.Clear();
        _networkExtrasPanel?.Children.Clear();
        foreach (var adapter in adapters)
            _adapterListPanel?.Children.Add(CreateAdapterItem(adapter));
        if (wireless) _networkExtrasPanel?.Children.Add(CreateWifiNetworkControls());
    }

    private void PopulateUsbAdapterDetails()
    {
        var adapters = _networkService.GetUsbTetheringAdapters();
        _selectedAdapter = _selectedUsbAdapter = adapters.FirstOrDefault(a => a.IsUp) ?? adapters.FirstOrDefault();
        UpdateSelectedAdapterInfo();
        _adapterListPanel?.Children.Clear();
        _networkExtrasPanel?.Children.Clear();
        foreach (var adapter in adapters)
            _adapterListPanel?.Children.Add(CreateAdapterItem(adapter));
        if (adapters.Count == 0)
            _adapterListPanel?.Children.Add(new TextBlock
            {
                Text = App.Settings.Settings.Language == "zh-CN"
                    ? "未检测到 USB/RNDIS 网卡。请连接手机并先在手机设置中开启 USB 网络共享。"
                    : "No USB/RNDIS adapter detected. Connect the phone and enable USB tethering on the phone first.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Opacity = 0.75
            });
    }

    private async void UsbToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperationInProgress) return;
        _isOperationInProgress = true;
        if (_usbButton != null) _usbButton.IsEnabled = false;
        var isZh = App.Settings.Settings.Language == "zh-CN";
        try
        {
            var result = await _networkService.SetUsbTetheringEnabledAsync(_selectedUsbAdapter?.IsUp != true);
            ShowToast(result switch
            {
                UsbTetheringResult.Enabled => isZh ? "USB 网络共享已连接" : "USB tethering connected",
                UsbTetheringResult.EnabledWaitingForPhone => isZh ? "USB 网卡已启用，请确认手机端已开启 USB 网络共享" : "USB adapter enabled; confirm USB tethering is enabled on the phone",
                UsbTetheringResult.Disabled => isZh ? "USB 网络共享已断开" : "USB tethering disconnected",
                UsbTetheringResult.AdapterNotFound => isZh ? "未检测到 USB/RNDIS 网卡，请先连接手机并开启 USB 网络共享" : "No USB/RNDIS adapter found; connect the phone and enable USB tethering",
                _ => isZh ? "USB 网络共享操作失败" : "USB tethering operation failed"
            });
            _networkService.RefreshAdapters();
        }
        finally
        {
            _isOperationInProgress = false;
            if (_usbButton != null) _usbButton.IsEnabled = true;
        }
    }
    private Border CreateAdapterItem(NetworkAdapter adapter)
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";

        var border = new Border
        {
            Background = new SolidColorBrush(adapter.IsUp
                ? Color.FromArgb(225, 0, 95, 184)
                : Color.FromArgb(24, 255, 255, 255)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        border.Tapped += async (s, e) =>
        {
            if (adapter.IsUp || _isOperationInProgress) return;
            _isOperationInProgress = true;

            ShowToast(isZh ? "正在切换..." : "Switching...");

            try
            {
                var enabled = await NetworkService.EnableAdapterAsync(adapter.InterfaceName);
                if (enabled && _selectedAdapter is { IsUp: true } current && current.Id != adapter.Id && current.Type == adapter.Type)
                    await NetworkService.DisableAdapterAsync(current.InterfaceName);
                _networkService.RefreshAdapters();
                ShowToast(enabled
                    ? (isZh ? $"已启用：{adapter.Name}" : $"Enabled {adapter.Name}")
                    : (isZh ? "操作失败，请检查管理员权限" : "Operation failed; check administrator privileges"));
            }
            finally { _isOperationInProgress = false; }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = adapter.IsUp ? "\uE77A" : "\uE711",
            FontSize = 14,
            Foreground = new SolidColorBrush(adapter.IsUp
                ? Color.FromArgb(255, 0, 200, 0)
                : Color.FromArgb(255, 150, 150, 150)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(icon, 0);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = adapter.Name,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        info.Children.Add(new TextBlock
        {
            Text = adapter.GetDisplayInfo(),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(info, 1);

        grid.Children.Add(icon);
        grid.Children.Add(info);

        var priority = new Button
        {
            Content = isZh ? "优先级" : "Priority", Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        priority.Tapped += (_, e) => e.Handled = true;
        priority.Click += async (_, _) => await ShowAdapterPriorityDialogAsync(adapter, isZh);
        Grid.SetColumn(priority, 2);
        grid.Children.Add(priority);

        if (adapter.IsUp)
        {
            var check = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(check, 3);
            grid.Children.Add(check);
        }

        border.Child = grid;
        return border;
    }

    private async Task ShowAdapterPriorityDialogAsync(NetworkAdapter adapter, bool isZh)
    {
        var currentMetric = await NetworkService.GetInterfaceMetricAsync(adapter.InterfaceName) ?? 25;
        var metricInput = new NumberBox
        {
            Value = currentMetric, Minimum = 1, Maximum = 9999,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Header = isZh ? "接口跃点数（数值越小，优先级越高）" : "Interface metric (lower values have higher priority)"
        };
        var dialog = new ContentDialog
        {
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
            Title = adapter.Name, Content = metricInput,
            PrimaryButtonText = isZh ? "应用" : "Apply", CloseButtonText = isZh ? "取消" : "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || double.IsNaN(metricInput.Value)) return;
        var metric = (int)Math.Round(metricInput.Value);
        var success = await NetworkService.SetInterfaceMetricAsync(adapter.InterfaceName, metric);
        ShowToast(success
            ? (isZh ? $"已将 {adapter.Name} 的优先级设为 {metric}" : $"Set {adapter.Name} priority metric to {metric}")
            : (isZh ? "设置优先级失败，请检查管理员权限" : "Failed to set priority; check administrator privileges"));
    }

    private Border CreateWifiNetworkControls()
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = isZh ? "Wi-Fi 网络" : "Wi-Fi networks", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _ = PopulateWifiNetworksAsync(panel, isZh);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8), Child = panel
        };
    }

    private async Task PopulateWifiNetworksAsync(StackPanel panel, bool isZh)
    {
        var adapter = _selectedWirelessAdapter;
        if (adapter == null) return;
        var ssid = await NetworkService.GetConnectedWifiSsidAsync();
        if (!string.IsNullOrWhiteSpace(ssid))
        {
            var connectedRow = new Grid { ColumnSpacing = 6 };
            connectedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var disconnect = new Button { Content = isZh ? $"断开 {ssid}" : $"Disconnect {ssid}", HorizontalAlignment = HorizontalAlignment.Stretch };
            disconnect.Click += async (_, _) =>
            {
                disconnect.IsEnabled = false;
                var success = await NetworkService.DisconnectWifiAsync(adapter.InterfaceName);
                ShowToast(success ? (isZh ? "Wi-Fi 已断开连接" : "Wi-Fi disconnected") : (isZh ? "断开失败" : "Disconnect failed"));
                _networkService.RefreshAdapters();
            };
            connectedRow.Children.Add(disconnect);
            panel.Children.Add(connectedRow);
        }
        panel.Children.Add(new TextBlock { Text = isZh ? "可用网络" : "Available networks", FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0) });
        var networks = await NetworkService.GetAvailableWifiNetworksAsync();
        if (networks.Count == 0) panel.Children.Add(new TextBlock { Text = isZh ? "未发现网络" : "No networks found", FontSize = 11, Opacity = 0.7 });
        foreach (var network in networks.Take(8))
        {
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = network, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var isConnected = string.Equals(network, ssid, StringComparison.OrdinalIgnoreCase);
            var connect = new Button { Content = isConnected ? (isZh ? "已连接" : "Connected") : (isZh ? "连接" : "Connect"), IsEnabled = !isConnected };
            connect.Click += async (_, _) =>
            {
                connect.IsEnabled = false;
                var success = await NetworkService.ConnectWifiAsync(network, adapter.InterfaceName);
                if (!success) success = await PromptForWifiPasswordAndConnectAsync(network, adapter.InterfaceName, isZh);
                ShowToast(success ? (isZh ? $"已连接 {network}，密码已由 Windows 保存" : $"Connected to {network}; password saved by Windows") : (isZh ? "连接失败，请检查密码或网络安全类型" : "Connection failed; check the password or network security type"));
                _networkService.RefreshAdapters();
                connect.IsEnabled = true;
            };
            Grid.SetColumn(connect, 1);
            row.Children.Add(connect);
            panel.Children.Add(row);
        }

        panel.Children.Add(new TextBlock { Text = isZh ? "已保存 Wi-Fi" : "Saved Wi-Fi", FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0) });
        _savedWifiSearchBox = new TextBox
        {
            PlaceholderText = isZh ? "搜索已保存 Wi-Fi" : "Search saved Wi-Fi",
            FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var savedPanel = new StackPanel { Spacing = 6 };
        _savedWifiSearchBox.TextChanged += (_, _) => RenderSavedWifiProfiles(savedPanel, isZh);
        panel.Children.Add(_savedWifiSearchBox);
        panel.Children.Add(savedPanel);
        await PopulateSavedWifiProfilesAsync(savedPanel, isZh);
    }

    private async Task PopulateSavedWifiProfilesAsync(StackPanel panel, bool isZh)
    {
        panel.Children.Clear();
        _savedWifiProfiles = await NetworkService.GetSavedWifiProfilesAsync();
        RenderSavedWifiProfiles(panel, isZh);
    }

    private void RenderSavedWifiProfiles(StackPanel panel, bool isZh)
    {
        panel.Children.Clear();
        var search = _savedWifiSearchBox?.Text.Trim() ?? "";
        var profiles = _savedWifiProfiles.Where(profile => string.IsNullOrWhiteSpace(search) || profile.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        if (profiles.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(search) ? (isZh ? "没有已保存的网络" : "No saved networks") : (isZh ? "没有匹配的已保存网络" : "No matching saved networks"), FontSize = 11, Opacity = 0.7 });
            return;
        }

        foreach (var profile in profiles)
        {
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = profile.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

            var viewPassword = new Button { Content = isZh ? "查看密码" : "View password" };
            viewPassword.Click += async (_, _) =>
            {
                viewPassword.IsEnabled = false;
                var password = await NetworkService.GetSavedWifiPasswordAsync(profile);
                viewPassword.IsEnabled = true;
                if (password == null)
                {
                    ShowToast(isZh ? "无法读取密码，请确认以管理员权限运行" : "Unable to read the password; run as administrator");
                    return;
                }
                var dialog = new ContentDialog
                {
                    XamlRoot = (Content as FrameworkElement)?.XamlRoot,
                    Title = isZh ? $"{profile.Name} 的密码" : $"Password for {profile.Name}",
                    Content = new TextBox { Text = string.IsNullOrEmpty(password) ? (isZh ? "开放网络或未保存密码" : "Open network or no saved password") : password, IsReadOnly = true },
                    CloseButtonText = isZh ? "关闭" : "Close"
                };
                await dialog.ShowAsync();
            };
            Grid.SetColumn(viewPassword, 1);
            row.Children.Add(viewPassword);

            var forget = new Button { Content = isZh ? "忘记" : "Forget" };
            forget.Click += async (_, _) =>
            {
                var confirm = new ContentDialog
                {
                    XamlRoot = (Content as FrameworkElement)?.XamlRoot,
                    Title = isZh ? "忘记 Wi-Fi？" : "Forget Wi-Fi?",
                    Content = isZh ? $"将从 Windows 删除“{profile.Name}”的已保存配置。" : $"Remove the saved profile for '{profile.Name}' from Windows?",
                    PrimaryButtonText = isZh ? "忘记" : "Forget",
                    CloseButtonText = isZh ? "取消" : "Cancel",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                forget.IsEnabled = false;
                var profileXml = await NetworkService.ExportSavedWifiProfileAsync(profile);
                var success = await NetworkService.DeleteSavedWifiProfileAsync(profile);
                if (success)
                {
                    _savedWifiProfiles.RemoveAll(item => item.InterfaceId == profile.InterfaceId && string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
                    RenderSavedWifiProfiles(panel, isZh);
                    ShowToast(isZh ? $"已忘记 {profile.Name}" : $"Forgot {profile.Name}",
                        profileXml == null ? null : (isZh ? "撤销" : "Undo"),
                        profileXml == null ? null : async () =>
                        {
                            var restored = await NetworkService.RestoreSavedWifiProfileAsync(profile, profileXml);
                            ShowToast(restored ? (isZh ? $"已恢复 {profile.Name}" : $"Restored {profile.Name}") : (isZh ? "恢复 Wi-Fi 失败" : "Failed to restore Wi-Fi"));
                            if (restored) await PopulateSavedWifiProfilesAsync(panel, isZh);
                        }, 6000);
                }
                else forget.IsEnabled = true;
                if (!success) ShowToast(isZh ? "忘记网络失败" : "Failed to forget network");
                _networkService.RefreshAdapters();
            };
            Grid.SetColumn(forget, 2);
            row.Children.Add(forget);
            panel.Children.Add(row);
        }
    }

    private async Task<bool> PromptForWifiPasswordAndConnectAsync(string ssid, string interfaceName, bool isZh)
    {
        var password = new PasswordBox
        {
            PlaceholderText = isZh ? "Wi-Fi 密码（开放网络留空）" : "Wi-Fi password (leave empty for open networks)",
            PasswordRevealMode = PasswordRevealMode.Peek
        };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = isZh ? "连接成功后，Windows 会保存此网络及密码。" : "Windows will save this network and password after connecting.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(password);
        var dialog = new ContentDialog
        {
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
            Title = isZh ? $"连接到 {ssid}" : $"Connect to {ssid}",
            Content = content,
            PrimaryButtonText = isZh ? "连接" : "Connect",
            CloseButtonText = isZh ? "取消" : "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
        if (password.Password.Length is > 0 and < 8)
        {
            ShowToast(isZh ? "WPA/WPA2 密码至少需要 8 个字符" : "WPA/WPA2 passwords require at least 8 characters");
            return false;
        }
        return await NetworkService.ConnectWifiWithPasswordAsync(ssid, password.Password, interfaceName);
    }

    private void UpdateSelectedAdapterInfo()
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        if (_selectedAdapter == null)
        {
            if (_adapterNameText != null) _adapterNameText.Text = isZh ? "无可用网络适配器" : "No adapter available";
            if (_adapterStatusText != null) _adapterStatusText.Text = "";
            if (_adapterIpText != null) _adapterIpText.Text = "";
            return;
        }

        if (_adapterNameText != null) _adapterNameText.Text = _selectedAdapter.Name;
        if (_adapterStatusText != null) _adapterStatusText.Text = isZh
            ? $"状态: {_selectedAdapter.Status} | 速度: {_selectedAdapter.SpeedText}"
            : $"Status: {_selectedAdapter.Status} | Speed: {_selectedAdapter.SpeedText}";
        if (_adapterIpText != null) _adapterIpText.Text = isZh
            ? $"IP: {_selectedAdapter.IpAddresses.FirstOrDefault() ?? "未分配"}"
            : $"IP: {_selectedAdapter.IpAddresses.FirstOrDefault() ?? "N/A"}";
    }

    private async void EthernetToggle_Click(object sender, RoutedEventArgs e)
    {
        var ethernetAdapter = _networkService.GetEthernetAdapters().FirstOrDefault(a => a.IsUp)
            ?? _networkService.GetEthernetAdapters().FirstOrDefault();
        if (ethernetAdapter == null || _isOperationInProgress) return;
        var isZh = App.Settings.Settings.Language == "zh-CN";
        _isOperationInProgress = true;
        if (_toggleBtn != null) _toggleBtn.IsEnabled = false;

        try
        {
            var success = _isEthernetEnabled
                ? await NetworkService.DisableAdapterAsync(ethernetAdapter.InterfaceName)
                : await NetworkService.EnableAdapterAsync(ethernetAdapter.InterfaceName);
            ShowToast(success
                ? (isZh ? "操作成功" : "Operation completed")
                : (isZh ? "操作失败，请检查管理员权限" : "Operation failed; check administrator privileges"));
            _networkService.RefreshAdapters();
        }
        finally
        {
            _isOperationInProgress = false;
            if (_toggleBtn != null) _toggleBtn.IsEnabled = true;
        }
    }

    private async void WifiToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperationInProgress) return;
        var isZh = App.Settings.Settings.Language == "zh-CN";
        _isOperationInProgress = true;
        if (_wifiButton != null) _wifiButton.IsEnabled = false;
        try
        {
            var target = _wifiInterfaceName ?? _selectedWirelessAdapter?.InterfaceName;
            if (string.IsNullOrWhiteSpace(target))
            {
                ShowToast(isZh ? "未找到 Wi-Fi 接口" : "No Wi-Fi interface found");
                return;
            }
            var enable = _wifiState == WifiState.Disabled;
            var success = _wifiRadio != null
                ? await BluetoothService.SetEnabledAsync(_wifiRadio, enable)
                : enable
                    ? await NetworkService.EnableAdapterAsync(target)
                    : await NetworkService.DisableAdapterAsync(target);
            ShowToast(success
                ? (isZh ? "Wi-Fi 操作成功" : "Wi-Fi operation completed")
                : (isZh ? "Wi-Fi 操作失败" : "Wi-Fi operation failed"));
            _networkService.RefreshAdapters();
        }
        finally
        {
            _isOperationInProgress = false;
            if (_wifiButton != null) _wifiButton.IsEnabled = _selectedWirelessAdapter != null;
        }
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_showingSettings || _showingDohSettings)
        {
            _showingSettings = false;
            _showingDohSettings = false;
        }
        else
        {
            _showingSettings = true;
        }
        ShowCurrentPage();
    }

    public void ShowSettingsPage()
    {
        _showingSettings = true;
        _showingDohSettings = false;
        ShowCurrentPage();
    }

    private async void ShowCurrentPage()
    {
        var pageChangeVersion = ++_pageChangeVersion;
        CardsPanel.Opacity = 1;
        CardsPanel.Children.Clear();
        if (_currentNetworkHost != null) _currentNetworkHost.Visibility = Visibility.Collapsed;
        if (_showingDohSettings)
        {
            if (_titleText != null) _titleText.Text = "";
            if (_navigationButton != null) _navigationButton.Content = new FontIcon { Glyph = "\uE72B", FontSize = 16 };
            BuildDohSettingsView();
        }
        else if (_showingSettings)
        {
            if (_titleText != null) _titleText.Text = "";
            if (_navigationButton != null) _navigationButton.Content = new FontIcon { Glyph = "\uE72B", FontSize = 16 };
            BuildSettingsView();
        }
        else
        {
            if (_titleText != null) _titleText.Text = "";
            if (_navigationButton != null) _navigationButton.Content = new FontIcon { Glyph = "\uE713", FontSize = 16 };
            BuildUI();
            RefreshAdapters();
        }
        AnimatePageIn();
        // Page navigation rebuilds a large visual tree. Resize once after the new
        // page has painted, so the old page never appears at the new window size.
        _contentScroll?.ChangeView(null, 0, null, true);
        CardsPanel.UpdateLayout();
        await Task.Delay(20);
        if (pageChangeVersion != _pageChangeVersion) return;
        PositionNearTray();
    }

    private static void UpdateTileForeground(Button mainButton, Button? expandButton, bool enabled)
    {
        var foreground = enabled
            ? new SolidColorBrush(Colors.White)
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        mainButton.Foreground = foreground;
        mainButton.Tag = enabled;
        SetButtonIconForeground(mainButton, foreground);
        if (expandButton == null) return;
        expandButton.Foreground = foreground;
        expandButton.Tag = enabled;
        SetButtonIconForeground(expandButton, foreground);
    }

    private void ConfigureTileHover(Button button)
    {
        var hoverBackground = new SolidColorBrush(Color.FromArgb(255, 0, 62, 120));
        var hoverForeground = new SolidColorBrush(Colors.White);
        button.Resources["ButtonBackgroundPointerOver"] = hoverBackground;
        button.Resources["ButtonForegroundPointerOver"] = hoverForeground;
        button.Resources["ButtonBorderBrushPointerOver"] = new SolidColorBrush(Color.FromArgb(255, 35, 115, 190));
        button.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Color.FromArgb(255, 0, 48, 94));
        button.Resources["ButtonForegroundPressed"] = hoverForeground;
        button.PointerEntered += (_, _) =>
        {
            button.Background = hoverBackground;
            SetButtonIconForeground(button, hoverForeground);
            if (button.Content is FontIcon hoverIcon) hoverIcon.Opacity = 1;
        };
        button.PointerExited += (_, _) =>
        {
            var enabled = button.Tag is true;
            button.Background = enabled ? ActiveBrush : InactiveBrush;
            SetButtonIconForeground(button, enabled
                ? new SolidColorBrush(Colors.White)
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]);
        };
    }

    private static void SetButtonIconForeground(Button button, Brush foreground)
    {
        button.Foreground = foreground;
        if (button.Content is FontIcon icon) icon.Foreground = foreground;
        if (button.Content is Shape shape) shape.Stroke = foreground;
        if (button.Content is Panel panel)
        {
            foreach (var childIcon in panel.Children.OfType<FontIcon>()) childIcon.Foreground = foreground;
            foreach (var childShape in panel.Children.OfType<Shape>()) childShape.Stroke = foreground;
        }
    }

    private void AnimatePageIn()
    {
        var transform = new TranslateTransform { Y = 8 };
        CardsPanel.RenderTransform = transform;
        CardsPanel.Opacity = 0;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();
        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(160), EasingFunction = easing };
        Storyboard.SetTarget(fade, CardsPanel);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var move = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = easing, EnableDependentAnimation = true };
        Storyboard.SetTarget(move, transform);
        Storyboard.SetTargetProperty(move, "Y");
        storyboard.Children.Add(fade);
        storyboard.Children.Add(move);
        storyboard.Begin();
    }

    private void AnimateDetailsCardIn()
    {
        if (_adapterDetailsCard == null) return;
        var transform = new TranslateTransform { Y = -6 };
        _adapterDetailsCard.RenderTransform = transform;
        _adapterDetailsCard.Opacity = 0;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();
        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(140), EasingFunction = easing };
        Storyboard.SetTarget(fade, _adapterDetailsCard);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var move = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(160), EasingFunction = easing, EnableDependentAnimation = true };
        Storyboard.SetTarget(move, transform);
        Storyboard.SetTargetProperty(move, "Y");
        storyboard.Children.Add(fade);
        storyboard.Children.Add(move);
        storyboard.Begin();
    }

    private async void AnimateWindowNearTray()
    {
        if (_appWindow == null) return;
        _windowAnimationCts?.Cancel();
        _windowAnimationCts?.Dispose();
        _windowAnimationCts = new CancellationTokenSource();
        var token = _windowAnimationCts.Token;
        try
        {
            var workArea = DisplayArea.Primary.WorkArea;
            const int targetWidth = 367;
            var targetHeight = _showingSettings || _showingDohSettings ? 650 : _adapterDetailsCard?.Visibility == Visibility.Visible ? 660 : 570;
            var targetX = workArea.X + workArea.Width - targetWidth - 16;
            var targetY = workArea.Y + workArea.Height - targetHeight - 8;
            var start = _appWindow.Position;
            var startSize = _appWindow.Size;
            const int frames = 12;
            for (var frame = 1; frame <= frames; frame++)
            {
                token.ThrowIfCancellationRequested();
                var progress = frame / (double)frames;
                var eased = 1 - Math.Pow(1 - progress, 3);
                _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                    (int)Math.Round(start.X + (targetX - start.X) * eased),
                    (int)Math.Round(start.Y + (targetY - start.Y) * eased),
                    (int)Math.Round(startSize.Width + (targetWidth - startSize.Width) * eased),
                    (int)Math.Round(startSize.Height + (targetHeight - startSize.Height) * eased)));
                await Task.Delay(12, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void InitializeBackdrop(ElementTheme theme)
    {
        _backdropConfiguration ??= new SystemBackdropConfiguration { IsInputActive = true };
        if (App.Settings.Settings.BackdropEffect == "Mica" && MicaController.IsSupported())
        {
            _micaController = new MicaController { Kind = MicaKind.Base };
            _micaController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
            _micaController.SetSystemBackdropConfiguration(_backdropConfiguration);
            ConfigureBackdropTheme(theme);
            return;
        }
        if (!DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
            return;
        }
        _acrylicController = new DesktopAcrylicController();
        _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
        ConfigureBackdropTheme(theme);
    }

    private void ConfigureBackdropTheme(ElementTheme theme)
    {
        var dark = theme == ElementTheme.Dark;
        if (_backdropConfiguration != null)
            _backdropConfiguration.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        if (_acrylicController == null) return;
        _acrylicController.TintColor = dark
            ? Color.FromArgb(255, 32, 32, 32)
            : Color.FromArgb(255, 243, 243, 243);
        _acrylicController.TintOpacity = dark ? 0.65f : 0.52f;
        _acrylicController.LuminosityOpacity = dark ? 0.55f : 0.68f;
        _acrylicController.FallbackColor = dark
            ? Color.FromArgb(255, 32, 32, 32)
            : Color.FromArgb(255, 243, 243, 243);
    }

    private void SwitchBackdrop(string effect)
    {
        _acrylicController?.Dispose();
        _micaController?.Dispose();
        _acrylicController = null;
        _micaController = null;
        SystemBackdrop = null;
        InitializeBackdrop((Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default);
    }

    private void BuildSettingsView()
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        var cardBackground = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        var cardBorder = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

        CardsPanel.Children.Add(new TextBlock
        {
            Text = isZh ? "设置" : "Settings",
            FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(2, 4, 0, 6)
        });

        var startupEnabled = App.Settings.IsStartWithWindowsEnabled();
        var startupToggle = new Button
        {
            Width = 42, Height = 22, Padding = new Thickness(3, 2, 3, 2),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        UpdateCompactToggle(startupToggle, startupEnabled);
        startupToggle.Click += (_, _) =>
        {
            startupEnabled = !startupEnabled;
            App.Settings.SetStartWithWindows(startupEnabled);
            UpdateCompactToggle(startupToggle, startupEnabled);
        };
        CardsPanel.Children.Add(CreateSettingsCard(
            "\uE7E8", isZh ? "开机自启动" : "Start with Windows",
            isZh ? "登录 Windows 后自动运行" : "Run automatically after sign-in",
            startupToggle, cardBackground, cardBorder));

        var languageCombo = new ComboBox { MinWidth = 110, VerticalAlignment = VerticalAlignment.Center };
        languageCombo.Items.Add("中文");
        languageCombo.Items.Add("English");
        languageCombo.SelectedIndex = App.Settings.Settings.Language == "zh-CN" ? 0 : 1;
        languageCombo.SelectionChanged += (_, _) =>
        {
            App.Settings.Settings.Language = languageCombo.SelectedIndex == 0 ? "zh-CN" : "en-US";
            App.Settings.Save();
            App.MainDispatcherQueue?.TryEnqueue(() =>
            {
                CardsPanel.Children.Clear();
                BuildSettingsView();
            });
        };
        CardsPanel.Children.Add(CreateSettingsCard(
            "\uE8C1", isZh ? "语言" : "Language",
            isZh ? "界面显示语言" : "Display language",
            languageCombo, cardBackground, cardBorder));

        var backdropCombo = new ComboBox { MinWidth = 110, VerticalAlignment = VerticalAlignment.Center };
        backdropCombo.Items.Add(isZh ? "亚克力" : "Acrylic");
        backdropCombo.Items.Add(isZh ? "云母" : "Mica");
        backdropCombo.SelectedIndex = App.Settings.Settings.BackdropEffect == "Mica" ? 1 : 0;
        backdropCombo.SelectionChanged += (_, _) =>
        {
            var effect = backdropCombo.SelectedIndex == 1 ? "Mica" : "Acrylic";
            if (App.Settings.Settings.BackdropEffect == effect) return;
            App.Settings.Settings.BackdropEffect = effect;
            App.Settings.Save();
            SwitchBackdrop(effect);
        };
        CardsPanel.Children.Add(CreateSettingsCard(
            "\uE790", isZh ? "背景特效" : "Backdrop effect",
            isZh ? "选择亚克力或云母材质" : "Choose Acrylic or Mica material",
            backdropCombo, cardBackground, cardBorder));

        var about = new Border { Width = 0 };
        CardsPanel.Children.Add(CreateSettingsCard(
            "\uE946", isZh ? "关于" : "About",
            isZh ? $"WindowsExtendQuickSetting {GetDisplayVersion()}\n网络与蓝牙快速控制"
                 : $"WindowsExtendQuickSetting {GetDisplayVersion()}\nQuick network and Bluetooth controls",
            about, cardBackground, cardBorder));
    }

    private static string GetDisplayVersion()
    {
        var version = typeof(App).Assembly.GetName().Version;
        return version == null ? "—" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void BuildDohServerManager(bool isZh, Brush background, Brush borderBrush)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = isZh ? "DoH 服务器（DNS over HTTPS)" : "DoH servers (DNS over HTTPS)",
            FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        // Unified add area: switch between batch import and manual entry.
        var addPanel = new StackPanel { Spacing = 8 };
        addPanel.Children.Add(new TextBlock { Text = isZh ? "添加 DoH 服务器" : "Add DoH server", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.85 });
        var modeRow = new Grid { ColumnSpacing = 6 };
        modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _dohQuickModeButton = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Content = isZh ? "快速导入" : "Quick import", IsChecked = !_dohManualAddMode, HorizontalAlignment = HorizontalAlignment.Stretch };
        _dohManualModeButton = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Content = isZh ? "手动添加" : "Manual entry", IsChecked = _dohManualAddMode, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(_dohManualModeButton, 1);
        modeRow.Children.Add(_dohQuickModeButton);
        modeRow.Children.Add(_dohManualModeButton);
        addPanel.Children.Add(modeRow);
        _dohAddModeContent = new StackPanel { Spacing = 6 };
        addPanel.Children.Add(_dohAddModeContent);
        void RebuildAddMode()
        {
            if (_dohAddModeContent == null) return;
            _dohAddModeContent.Children.Clear();
            if (_dohManualAddMode)
            {
                _dohServerInput = new TextBox { PlaceholderText = isZh ? "DNS 服务器 IP，例如 1.1.1.1" : "DNS server IP, e.g. 1.1.1.1" };
                _dohTemplateInput = new TextBox { PlaceholderText = isZh ? "DoH 地址，例如 https://.../dns-query" : "DoH template, e.g. https://.../dns-query" };
                _dohAddModeContent.Children.Add(_dohServerInput);
                _dohAddModeContent.Children.Add(_dohTemplateInput);
                var addButton = new Button { Content = isZh ? "添加" : "Add", HorizontalAlignment = HorizontalAlignment.Stretch };
                addButton.Click += DohServerSave_Click;
                _dohAddModeContent.Children.Add(addButton);
            }
            else
            {
                _dohImportBox = new TextBox
                {
                    AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 120,
                    PlaceholderText = isZh
                        ? "粘贴 netsh dns add encryption server=1.1.1.1 dohtemplate=https://.../dns-query，可多行导入"
                        : "Paste netsh DoH commands; multiple lines are supported"
                };
                _dohAddModeContent.Children.Add(_dohImportBox);
                var importButton = new Button { Content = isZh ? "批量导入" : "Import", HorizontalAlignment = HorizontalAlignment.Stretch };
                importButton.Click += DohImport_Click;
                _dohAddModeContent.Children.Add(importButton);
            }
        }
        _dohQuickModeButton.Click += (_, _) =>
        {
            _dohManualAddMode = false;
            _dohQuickModeButton.IsChecked = true;
            if (_dohManualModeButton != null) _dohManualModeButton.IsChecked = false;
            RebuildAddMode();
        };
        _dohManualModeButton.Click += (_, _) =>
        {
            _dohManualAddMode = true;
            _dohManualModeButton.IsChecked = true;
            if (_dohQuickModeButton != null) _dohQuickModeButton.IsChecked = false;
            RebuildAddMode();
        };
        RebuildAddMode();
        panel.Children.Add(WrapDohSubCard(addPanel, isZh ? "切换快速导入或手动添加；成功后自动刷新系统列表" : "Switch between quick import and manual entry; the system list refreshes automatically", background, borderBrush));

        // Primary / Backup
        var pbPanel = new StackPanel { Spacing = 6 };
        pbPanel.Children.Add(new TextBlock { Text = isZh ? "系统首选 / 备用 DoH" : "System primary / backup DoH", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.85 });
        _dohPrimaryCombo = CreateDohSearchSelector(isZh ? "首选 DoH" : "Primary DoH", isZh ? "展开并搜索系统 DoH" : "Open and search system DoH", false);
        _dohBackupCombo = CreateDohSearchSelector(isZh ? "备用 DoH" : "Backup DoH", isZh ? "展开并搜索，可留空" : "Open and search; optional", true);
        pbPanel.Children.Add(_dohPrimaryCombo);
        pbPanel.Children.Add(_dohBackupCombo);
        var applyBtn = new Button { Content = isZh ? "应用首选 / 备用到系统" : "Apply primary / backup to system", HorizontalAlignment = HorizontalAlignment.Stretch };
        applyBtn.Click += DohApplyPrimaryBackup_Click;
        pbPanel.Children.Add(applyBtn);
        panel.Children.Add(WrapDohSubCard(pbPanel, isZh ? "将首选/备用设为网卡 DNS，系统即通过这些 DoH 解析" : "Sets NIC DNS to the chosen servers so the system resolves via these DoH", background, borderBrush));

        // Keep the complete system list last.
        var sysPanel = new StackPanel { Spacing = 6 };
        sysPanel.Children.Add(new TextBlock { Text = isZh ? "系统已有 DoH 列表" : "System DoH list", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.85 });
        var systemHeader = new Grid { ColumnSpacing = 6 };
        systemHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        systemHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _dohSystemSearchBox = new TextBox { PlaceholderText = isZh ? "IP 或地址" : "IP or URL", FontSize = 11 };
        systemHeader.Children.Add(_dohSystemSearchBox);
        var searchButton = new Button { Width = 32, Height = 32, Padding = new Thickness(0), Content = new FontIcon { Glyph = "\uE721", FontSize = 13 } };
        searchButton.Click += (_, _) => RenderSystemDohList(isZh);
        _dohSystemSearchBox.TextChanged += (_, _) => RenderSystemDohList(isZh);
        Grid.SetColumn(searchButton, 1);
        systemHeader.Children.Add(searchButton);
        sysPanel.Children.Add(systemHeader);
        _dohSystemList = new StackPanel { Spacing = 5 };
        sysPanel.Children.Add(_dohSystemList);
        panel.Children.Add(WrapDohSubCard(sysPanel, isZh ? "系统当前注册的 DoH 模板" : "DoH templates currently registered in Windows", background, borderBrush));

        CardsPanel.Children.Add(new Border
        {
            Background = background, BorderBrush = borderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Child = panel
        });

        _ = PopulateSystemDohListAsync(isZh);
    }

    private Border WrapDohSubCard(UIElement content, string description, Brush background, Brush border)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(content);
        stack.Children.Add(new TextBlock { Text = description, FontSize = 10, Opacity = 0.55, TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
            BorderBrush = border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Child = stack
        };
    }

    private async Task PopulateSystemDohListAsync(bool isZh)
    {
        if (_dohSystemList == null) return;
        var target = _dohSystemList;
        target.Children.Clear();
        target.Children.Add(new TextBlock
        {
            Text = isZh ? "正在读取系统 DoH…" : "Reading system DoH…",
            FontSize = 11,
            Opacity = 0.6
        });
        var system = await GetSystemDohServersAsync();
        if (!ReferenceEquals(target, _dohSystemList)) return;
        target.Children.Clear();
        _systemDohCache = system;
        UpdateDohSelectors(system);
        if (system.Count == 0)
        {
            target.Children.Add(new TextBlock
            {
                Text = isZh ? "未能读取系统 DoH 模板，请检查管理员权限" : "Could not read system DoH templates; check administrator access",
                FontSize = 11,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        RenderSystemDohList(isZh);
    }

    private void RenderSystemDohList(bool isZh)
    {
        if (_dohSystemList == null) return;
        var search = _dohSystemSearchBox?.Text.Trim() ?? "";
        var filtered = string.IsNullOrWhiteSpace(search)
            ? _systemDohCache
            : _systemDohCache.Where(item => item.Server.Contains(search, StringComparison.OrdinalIgnoreCase) || item.Template.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        _dohSystemList.Children.Clear();
        foreach (var item in filtered)
            _dohSystemList.Children.Add(CreateDohRow(item.Server, item.Template, true));
        if (filtered.Count == 0)
            _dohSystemList.Children.Add(new TextBlock { Text = isZh ? "未找到匹配的 DoH" : "No matching DoH entries", FontSize = 11, Opacity = 0.65 });
    }

    private void UpdateDohSelectors(IReadOnlyList<SystemDohServer> system)
    {
        if (_dohPrimaryCombo == null || _dohBackupCombo == null) return;
        _updatingDohSelections = true;
        try
        {
            var primary = App.Settings.Settings.PrimaryDohServer;
            var backup = App.Settings.Settings.BackupDohServer;
            _dohPrimaryCombo.ItemsSource = system.Select(item => item.Server).ToList();
            _dohBackupCombo.ItemsSource = DohSuggestions(system, "", true).ToList();
            _dohPrimaryCombo.Text = primary ?? "";
            _dohBackupCombo.Text = backup ?? "";
        }
        finally { _updatingDohSelections = false; }
    }

    private AutoSuggestBox CreateDohSearchSelector(string header, string placeholder, bool includeNone)
    {
        var selector = new AutoSuggestBox { Header = header, PlaceholderText = placeholder, HorizontalAlignment = HorizontalAlignment.Stretch, QueryIcon = new SymbolIcon(Symbol.Find) };
        selector.GotFocus += (_, _) =>
        {
            selector.ItemsSource = DohSuggestions(_systemDohCache, selector.Text, includeNone).ToList();
            selector.IsSuggestionListOpen = true;
        };
        selector.TextChanged += (_, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                selector.ItemsSource = DohSuggestions(_systemDohCache, selector.Text, includeNone).ToList();
        };
        selector.SuggestionChosen += (_, args) =>
        {
            selector.Text = args.SelectedItem as string == NoBackupLabel() ? "" : args.SelectedItem as string ?? "";
            SaveDohSelections();
        };
        selector.QuerySubmitted += (_, args) =>
        {
            if (args.ChosenSuggestion != null)
                selector.Text = args.ChosenSuggestion as string == NoBackupLabel() ? "" : args.ChosenSuggestion as string ?? "";
            SaveDohSelections();
        };
        return selector;
    }

    private IEnumerable<string> DohSuggestions(IReadOnlyList<SystemDohServer> system, string search, bool includeNone)
    {
        if (includeNone && string.IsNullOrWhiteSpace(search)) yield return NoBackupLabel();
        foreach (var item in system.Where(item => string.IsNullOrWhiteSpace(search) || item.Server.Contains(search, StringComparison.OrdinalIgnoreCase) || item.Template.Contains(search, StringComparison.OrdinalIgnoreCase)))
            yield return item.Server;
    }

    private string NoBackupLabel() => App.Settings.Settings.Language == "zh-CN" ? "（无备用）" : "(No backup)";

    private void SaveDohSelections()
    {
        if (_updatingDohSelections) return;
        App.Settings.Settings.PrimaryDohServer = string.IsNullOrWhiteSpace(_dohPrimaryCombo?.Text) ? null : _dohPrimaryCombo.Text.Trim();
        var backup = _dohBackupCombo?.Text.Trim();
        App.Settings.Settings.BackupDohServer = string.IsNullOrWhiteSpace(backup) ? null : backup;
        App.Settings.Save();
    }

    private void BuildDohSettingsView()
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        CardsPanel.Children.Add(new TextBlock
        {
            Text = isZh ? "DoH 设置" : "DoH settings",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(2, 4, 0, 6)
        });
        BuildDohServerManager(isZh,
            (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]);
    }

    private async Task ImportSystemDohListAsync(bool isZh)
    {
        var system = await GetSystemDohServersAsync();
        var existing = new HashSet<string>(
            App.Settings.Settings.DohServers.Select(item => item.Server),
            StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var item in system)
        {
            if (!existing.Add(item.Server)) continue;
            App.Settings.Settings.DohServers.Add(new DohServerEntry
            {
                Server = item.Server,
                Template = item.Template
            });
            added++;
        }
        App.Settings.Save();
        ShowToast(isZh
            ? $"已读取系统 DoH：共 {system.Count} 个，新导入 {added} 个"
            : $"Read {system.Count} system DoH entries; imported {added} new entries");
        RefreshDohManager();
    }

    private void RefreshDohManager()
    {
        CardsPanel.Children.Clear();
        BuildDohSettingsView();
    }

    private Border CreateDohRow(string server, string template, bool isSystem)
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var primary = App.Settings.Settings.PrimaryDohServer;
        var backup = App.Settings.Settings.BackupDohServer;
        var tag = string.Equals(primary, server, StringComparison.OrdinalIgnoreCase)
            ? (isZh ? "  ★首选" : "  ★Primary")
            : string.Equals(backup, server, StringComparison.OrdinalIgnoreCase)
                ? (isZh ? "  ☆备用" : "  ☆Backup")
                : "";

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = server + tag, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(new TextBlock
        {
            Text = template, FontSize = 10, Opacity = 0.7, TextWrapping = TextWrapping.Wrap
        });
        row.Children.Add(text);

        if (!isSystem)
        {
            var edit = new Button { Content = isZh ? "编辑" : "Edit", Padding = new Thickness(6, 3, 6, 3), FontSize = 11 };
            edit.Click += (_, _) =>
            {
                _editingDohServer = server;
                _dohManualAddMode = true;
                RefreshDohManager();
                if (_dohServerInput != null) _dohServerInput.Text = server;
                if (_dohTemplateInput != null) _dohTemplateInput.Text = template;
            };
            Grid.SetColumn(edit, 1);
            row.Children.Add(edit);
        }

        var remove = new Button { Content = "×", Padding = new Thickness(6, 3, 6, 3), FontSize = 11 };
        remove.Click += async (_, _) =>
        {
            if (isSystem)
                await DeleteSystemDohServerAsync(server);
            else
                await DeleteDohServerAsync(new DohServerEntry { Server = server, Template = template });
        };
        Grid.SetColumn(remove, isSystem ? 1 : 2);
        row.Children.Add(remove);

        var brush = string.Equals(primary, server, StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.FromArgb(45, 0, 120, 215))
            : string.Equals(backup, server, StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Color.FromArgb(45, 130, 130, 130))
                : new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));

        return new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 6, 8, 6), Child = row
        };
    }

    private void DohSetPrimary_Click(string server)
    {
        App.Settings.Settings.PrimaryDohServer = server;
        App.Settings.Save();
        RefreshDohManager();
    }

    private void DohSetBackup_Click(string server)
    {
        App.Settings.Settings.BackupDohServer = server;
        App.Settings.Save();
        RefreshDohManager();
    }

    private async void DohImport_Click(object sender, RoutedEventArgs e)
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        var text = _dohImportBox?.Text ?? "";
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var parsed = lines.Select(ParseDohImportLine).Where(p => p.HasValue).Select(p => p!.Value).ToList();
        if (parsed.Count == 0)
        {
            ShowToast(isZh ? "未识别到有效的 DoH 配置" : "No valid DoH configuration found");
            return;
        }
        var system = await GetSystemDohServersAsync();
        var existing = new HashSet<string>(system.Select(s => s.Server), StringComparer.OrdinalIgnoreCase);
        existing.UnionWith(App.Settings.Settings.DohServers.Select(d => d.Server));
        var added = 0;
        var skipped = 0;
        foreach (var p in parsed)
        {
            if (existing.Contains(p.Server)) { skipped++; continue; }
            var ok = await AddDohServerAsync(p.Server, p.Template);
            if (ok)
            {
                App.Settings.Settings.DohServers.Add(new DohServerEntry { Server = p.Server, Template = p.Template });
                added++;
            }
            else skipped++;
        }
        App.Settings.Save();
        ShowToast(isZh
            ? $"已导入 {added} 个，跳过重复/失败 {skipped} 个"
            : $"Imported {added}, skipped/failed {skipped}");
        RefreshDohManager();
    }

    private async Task DeleteSystemDohServerAsync(string server)
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        if (!await RemoveDohServerAsync(server))
        {
            ShowToast(isZh ? "从系统删除 DoH 失败" : "Could not remove DoH from system");
            return;
        }
        if (string.Equals(App.Settings.Settings.PrimaryDohServer, server, StringComparison.OrdinalIgnoreCase))
            App.Settings.Settings.PrimaryDohServer = null;
        if (string.Equals(App.Settings.Settings.BackupDohServer, server, StringComparison.OrdinalIgnoreCase))
            App.Settings.Settings.BackupDohServer = null;
        App.Settings.Save();
        ShowToast(isZh ? "已从系统删除 DoH" : "DoH removed from system");
        RefreshDohManager();
    }

    private async void DohApplyPrimaryBackup_Click(object sender, RoutedEventArgs e)
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        var primary = App.Settings.Settings.PrimaryDohServer;
        var backup = App.Settings.Settings.BackupDohServer;
        if (string.IsNullOrWhiteSpace(primary))
        {
            ShowToast(isZh ? "请先从下拉列表选择首选 DoH" : "Select a primary DoH from the list first");
            return;
        }
        var iface = _networkService.GetActiveInterfaceName();
        if (string.IsNullOrWhiteSpace(iface))
        {
            ShowToast(isZh ? "未找到可用的网络接口" : "No available network interface");
            return;
        }
        var backupNote = string.IsNullOrWhiteSpace(backup) ? "" : $"，备用 {backup}";
        var confirm = new ContentDialog
        {
            XamlRoot = (this.Content as Microsoft.UI.Xaml.FrameworkElement)?.XamlRoot,
            Title = isZh ? "应用首选 / 备用 DoH" : "Apply primary / backup DoH",
            Content = isZh
                ? $"将把网络接口“{iface}”的 DNS 设为:\n首选 {primary}{backupNote}\nWindows 将通过对应的 DoH 模板进行加密解析（会覆盖当前 DNS 设置）。"
                : $"Set DNS on interface '{iface}' to:\nprimary {primary}{(string.IsNullOrWhiteSpace(backup) ? "" : ", backup " + backup)}\nWindows will resolve via the matching DoH templates (overwrites current DNS).",
            PrimaryButtonText = isZh ? "应用" : "Apply",
            CloseButtonText = isZh ? "取消" : "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        // ensure templates exist
        var system = await GetSystemDohServersAsync();
        var sysMap = system.ToDictionary(s => s.Server, StringComparer.OrdinalIgnoreCase);
        async Task EnsureTemplate(string ip)
        {
            if (sysMap.ContainsKey(ip)) return;
            var app = App.Settings.Settings.DohServers.FirstOrDefault(d => string.Equals(d.Server, ip, StringComparison.OrdinalIgnoreCase));
            if (app != null) await AddDohServerAsync(app.Server, app.Template);
        }
        await EnsureTemplate(primary);
        if (!string.IsNullOrWhiteSpace(backup)) await EnsureTemplate(backup);

        var ok = await SetInterfaceDnsAsync(iface, primary, backup);
        if (ok) _networkService.RefreshAdapters();
        ShowToast(ok
            ? (isZh ? "已设定系统首选/备用 DoH" : "System primary/backup DoH applied")
            : (isZh ? "设定失败，请检查权限" : "Failed to apply; check permissions"));
    }

    private async void DohServerSave_Click(object sender, RoutedEventArgs e)
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        var server = _dohServerInput?.Text.Trim() ?? "";
        var template = _dohTemplateInput?.Text.Trim() ?? "";
        if (!IsValidDohServer(server, template))
        {
            if (!System.Net.IPAddress.TryParse(server, out _))
                ShowInputTip(_dohServerInput, isZh ? "请输入有效的 IP 地址，例如 1.1.1.1" : "Enter a valid IP address, for example 1.1.1.1");
            else
                ShowInputTip(_dohTemplateInput, isZh ? "请输入有效的 HTTPS DoH 地址，例如 https://example.com/dns-query" : "Enter a valid HTTPS DoH URL, for example https://example.com/dns-query");
            return;
        }
        // editing an existing entry: replace old record first
        if (!string.IsNullOrEmpty(_editingDohServer) &&
            !string.Equals(_editingDohServer, server, StringComparison.OrdinalIgnoreCase))
        {
            App.Settings.Settings.DohServers.RemoveAll(d =>
                string.Equals(d.Server, _editingDohServer, StringComparison.OrdinalIgnoreCase));
        }
        if (!await AddDohServerAsync(server, template))
        {
            ShowToast(isZh ? "添加 DoH 服务器失败" : "Could not add DoH server");
            return;
        }
        var entry = App.Settings.Settings.DohServers.FirstOrDefault(d => string.Equals(d.Server, server, StringComparison.OrdinalIgnoreCase));
        if (entry != null) entry.Template = template;
        else App.Settings.Settings.DohServers.Add(new DohServerEntry { Server = server, Template = template });
        App.Settings.Save();
        _editingDohServer = null;
        if (_dohServerInput != null) _dohServerInput.Text = "";
        if (_dohTemplateInput != null) _dohTemplateInput.Text = "";
        ShowToast(isZh ? "DoH 服务器已保存" : "DoH server saved");
        RefreshDohManager();
    }

    private static void ShowInputTip(TextBox? input, string message)
    {
        if (input == null) return;
        input.Focus(FocusState.Programmatic);
        var tip = new Flyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280
            }
        };
        tip.ShowAt(input);
    }

    private async Task DeleteDohServerAsync(DohServerEntry entry)
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        App.Settings.Settings.DohServers.RemoveAll(d =>
            string.Equals(d.Server, entry.Server, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(App.Settings.Settings.PrimaryDohServer, entry.Server, StringComparison.OrdinalIgnoreCase))
            App.Settings.Settings.PrimaryDohServer = null;
        if (string.Equals(App.Settings.Settings.BackupDohServer, entry.Server, StringComparison.OrdinalIgnoreCase))
            App.Settings.Settings.BackupDohServer = null;
        App.Settings.Save();
        if (await GetSystemDohServersAsync() is var system && system.Any(s =>
                string.Equals(s.Server, entry.Server, StringComparison.OrdinalIgnoreCase)) &&
            !await RemoveDohServerAsync(entry.Server))
        {
            ShowToast(isZh ? "删除 DoH 服务器失败" : "Could not remove DoH server");
            RefreshDohManager();
            return;
        }
        ShowToast(isZh ? "DoH 服务器已删除" : "DoH server removed");
        RefreshDohManager();
    }

    private void UpdateCompactToggle(Button toggle, bool enabled)
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        toggle.Content = new Border
        {
            Width = 34, Height = 16, CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = enabled
                ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
                : new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)),
            Child = new Ellipse
            {
                Width = 12, Height = 12, Fill = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 2, 0)
            }
        };
        ToolTipService.SetToolTip(toggle, enabled ? (isZh ? "已开启" : "On") : (isZh ? "已关闭" : "Off"));
    }

    private Border CreateSettingsCard(string glyph, string title, string description, FrameworkElement content, Brush background, Brush borderBrush)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = glyph, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
            FontSize = 18, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        textPanel.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        textPanel.Children.Add(new TextBlock { Text = description, FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        content.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(content, 2);
        grid.Children.Add(content);

        return new Border
        {
            Background = background, BorderBrush = borderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Child = grid
        };
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    }

    private static class PopupNativeMethods
    {
        public const int SW_MINIMIZE = 6;
        public const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
