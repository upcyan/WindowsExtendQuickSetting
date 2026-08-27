using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WindowsEthernetControl.Models;
using WindowsEthernetControl.Services;
using WinRT.Interop;
using WinRT;
using Windows.Devices.Radios;
using Color = Windows.UI.Color;
using Colors = Microsoft.UI.Colors;

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
    internal TextBlock StatusText = new() { Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)), FontSize = 11 };

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
    private Button? _dohToggle;
    private StackPanel? _dohServerList;
    private TextBox? _dohServerInput;
    private TextBox? _dohTemplateInput;
    private string? _editingDohServer;
    private Radio? _bluetoothRadio;
    private Border? _adapterDetailsCard;
    private bool _showWirelessDetails;
    private bool _showUsbDetails;
    private TextBlock? _titleText;
    private Button? _navigationButton;
    private bool _showingSettings;
    private DesktopAcrylicController? _acrylicController;
    private MicaController? _micaController;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private CancellationTokenSource? _resizeAnimationCts;
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

        var scroll = new ScrollViewer { Padding = new Thickness(30, 12, 30, 12) };
        Grid.SetRow(scroll, 1);
        scroll.Content = CardsPanel;
        root.Children.Add(scroll);

        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.Children.Add(StatusText);
        _navigationButton = new Button
        {
            Width = 36, Height = 36, Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
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
            Padding = new Thickness(22, 6, 16, 6), Child = footerGrid
        };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

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
            var panelH = _showingSettings ? 650 : _adapterDetailsCard?.Visibility == Visibility.Visible ? 660 : 570;
            var x = workArea.X + workArea.Width - panelW - 16;
            var y = workArea.Y + workArea.Height - panelH - 8;
            if (x < workArea.X) x = workArea.X;
            if (y < workArea.Y) y = workArea.Y;
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, panelW, panelH));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private async void AnimatePositionNearTray()
    {
        if (_appWindow == null) return;
        _resizeAnimationCts?.Cancel();
        _resizeAnimationCts?.Dispose();
        _resizeAnimationCts = new CancellationTokenSource();
        var token = _resizeAnimationCts.Token;

        try
        {
            var workArea = DisplayArea.Primary.WorkArea;
            const int targetWidth = 367;
            var targetHeight = _showingSettings ? 650
                : _adapterDetailsCard?.Visibility == Visibility.Visible ? 660 : 570;
            var targetX = workArea.X + workArea.Width - targetWidth - 16;
            var targetY = workArea.Y + workArea.Height - targetHeight - 8;
            var start = _appWindow.Position;
            var startSize = _appWindow.Size;

            const int frames = 14;
            for (var frame = 1; frame <= frames; frame++)
            {
                token.ThrowIfCancellationRequested();
                var progress = frame / (double)frames;
                var eased = 1 - Math.Pow(1 - progress, 3);
                var x = (int)Math.Round(start.X + (targetX - start.X) * eased);
                var y = (int)Math.Round(start.Y + (targetY - start.Y) * eased);
                var width = (int)Math.Round(startSize.Width + (targetWidth - startSize.Width) * eased);
                var height = (int)Math.Round(startSize.Height + (targetHeight - startSize.Height) * eased);
                _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
                await Task.Delay(12, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Window resize animation failed: {ex}"); }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_backdropConfiguration != null)
            _backdropConfiguration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _networkService.AdaptersChanged -= OnAdaptersChanged;
        _resizeAnimationCts?.Cancel();
        _resizeAnimationCts?.Dispose();
        _acrylicController?.Dispose();
        _micaController?.Dispose();
        _acrylicController = null;
        _micaController = null;
        _backdropConfiguration = null;
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
        quickGrid.Children.Add(ethernetTile);

        var wifiTile = CreateSplitTile("\uE701", isZh ? "未连接" : "Disconnected", out _wifiButton, out _wifiExpandButton, out _wifiCaption);
        _wifiButton.Click += WifiToggle_Click;
        _wifiExpandButton.Click += (_, _) => ShowAdapterDetails(true);
        Grid.SetColumn(wifiTile, 1);
        quickGrid.Children.Add(wifiTile);

        var bluetoothTile = CreateSplitTile("\uE702", isZh ? "未连接" : "Disconnected", out _bluetoothButton, out _bluetoothExpandButton, out _bluetoothCaption);
        _bluetoothButton.Click += BluetoothButton_Click;
        _bluetoothExpandButton.Click += (_, _) => StatusText.Text = isZh
            ? "蓝牙设备管理将在后续版本中提供"
            : "Bluetooth device management will be available in a later version";
        Grid.SetColumn(bluetoothTile, 2);
        quickGrid.Children.Add(bluetoothTile);

        var usbTile = CreateSplitTile("\uE88E", isZh ? "未连接" : "Disconnected", out _usbButton, out _usbExpandButton, out _usbCaption);
        _usbButton.Click += UsbToggle_Click;
        _usbExpandButton.Click += (_, _) => ShowUsbDetails();
        Grid.SetRow(usbTile, 1);
        quickGrid.Children.Add(usbTile);
        CardsPanel.Children.Add(quickGrid);

        var networkInfo = new StackPanel { Spacing = 5 };
        networkInfo.Children.Add(new TextBlock
        {
            Text = isZh ? "当前网络" : "Current network", FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        _networkNameText = new TextBlock { FontSize = 12 };
        _networkAddressText = new TextBlock { FontSize = 11, Opacity = 0.72, TextWrapping = TextWrapping.Wrap };
        _networkDnsText = new TextBlock { FontSize = 11, Opacity = 0.72, TextWrapping = TextWrapping.Wrap };
        networkInfo.Children.Add(_networkNameText);
        networkInfo.Children.Add(_networkAddressText);
        networkInfo.Children.Add(_networkDnsText);
        var infoGrid = new Grid { ColumnSpacing = 10 };
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoGrid.Children.Add(networkInfo);
        var dohPanel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        dohPanel.Children.Add(new TextBlock { Text = "DoH", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });
        _dohToggle = new Button { Width = 42, Height = 22, Padding = new Thickness(3), CornerRadius = new CornerRadius(11) };
        _dohToggle.Click += DohToggle_Click;
        dohPanel.Children.Add(_dohToggle);
        Grid.SetColumn(dohPanel, 1);
        infoGrid.Children.Add(dohPanel);
        CardsPanel.Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12), Child = infoGrid
        });

        _bluetoothStatus = new TextBlock { Visibility = Visibility.Collapsed };

        var detailPanel = new StackPanel { Spacing = 10 };
        detailPanel.Children.Add(new TextBlock
        {
            Text = isZh ? "网络适配器" : "Network adapters", FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        });
        var infoPanel = new StackPanel { Spacing = 2 };
        _adapterNameText = new TextBlock { Text = isZh ? "正在检测..." : "Detecting...", FontSize = 13, Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] };
        _adapterStatusText = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 175, 175, 175)) };
        _adapterIpText = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 175, 175, 175)) };
        infoPanel.Children.Add(_adapterNameText);
        infoPanel.Children.Add(_adapterStatusText);
        infoPanel.Children.Add(_adapterIpText);
        detailPanel.Children.Add(infoPanel);
        _adapterListPanel = new StackPanel { Spacing = 6 };
        detailPanel.Children.Add(_adapterListPanel);
        _adapterDetailsCard = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(14),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1), Child = detailPanel,
            Visibility = Visibility.Collapsed
        };
        CardsPanel.Children.Add(_adapterDetailsCard);

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
            Content = new FontIcon { Glyph = "\uE76C", FontSize = 11 }
        };
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
        if (_bluetoothCaption != null) _bluetoothCaption.Text = enabled
            ? (isZh ? "已开启" : "On") : (isZh ? "已关闭" : "Off");
        _bluetoothButton.Content = new FontIcon { Glyph = "\uE702", FontSize = 17 };
        ToolTipService.SetToolTip(_bluetoothButton, enabled ? (isZh ? "蓝牙已开启" : "Bluetooth on") : (isZh ? "蓝牙已关闭" : "Bluetooth off"));
        _bluetoothButton.IsEnabled = true;
    }

    private async void BluetoothButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bluetoothRadio == null || _bluetoothButton == null) return;
        _bluetoothButton.IsEnabled = false;
        var success = await BluetoothService.SetEnabledAsync(
            _bluetoothRadio, _bluetoothRadio.State != RadioState.On);
        if (!success && _bluetoothStatus != null)
            _bluetoothStatus.Text = App.Settings.Settings.Language == "zh-CN" ? "操作失败" : "Operation failed";
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
            ? _selectedAdapter.Name
            : (isZh ? "未连接" : "Disconnected");
        if (_wifiButton != null)
        {
            _wifiButton.IsEnabled = _selectedWirelessAdapter != null;
            var wifiEnabled = _selectedWirelessAdapter?.IsUp == true;
            _wifiButton.Background = wifiEnabled ? ActiveBrush : InactiveBrush;
            if (_wifiExpandButton != null) _wifiExpandButton.Background = wifiEnabled ? ActiveBrush : InactiveBrush;
            _wifiButton.Content = new FontIcon { Glyph = "\uE701", FontSize = 17 };
            ToolTipService.SetToolTip(_wifiButton, wifiEnabled
                ? (isZh ? "Wi-Fi 已启用" : "Wi-Fi enabled")
                : (isZh ? "Wi-Fi 已关闭" : "Wi-Fi off"));
            if (_wifiCaption != null) _wifiCaption.Text = wifiEnabled
                ? (_selectedWirelessAdapter?.Name ?? "Wi-Fi")
                : (isZh ? "未连接" : "Disconnected");
            if (wifiEnabled) _ = RefreshWifiCaptionAsync();
        }

        if (_usbButton != null)
        {
            _usbButton.IsEnabled = _selectedUsbAdapter != null;
            var usbEnabled = _selectedUsbAdapter?.IsUp == true;
            _usbButton.Background = usbEnabled ? ActiveBrush : InactiveBrush;
            if (_usbExpandButton != null) _usbExpandButton.Background = usbEnabled ? ActiveBrush : InactiveBrush;
            _usbButton.Content = new FontIcon { Glyph = "\uE88E", FontSize = 17 };
            if (_usbCaption != null) _usbCaption.Text = usbEnabled ? (isZh ? "USB 网络共享" : "USB tethering") : (isZh ? "未连接" : "Disconnected");
        }

        UpdateToggleVisual();
        UpdateNetworkDetails();
        if (_adapterDetailsCard?.Visibility == Visibility.Visible)
        {
            if (_showUsbDetails) PopulateUsbAdapterDetails();
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
            return;
        }
        var type = _activeNetworkAdapter.IsWireless ? "Wi-Fi" : (_activeNetworkAdapter.IsUsbTethering ? (isZh ? "USB 网络共享" : "USB tethering") : (isZh ? "有线网络" : "Ethernet"));
        _networkNameText.Text = $"{type} · {_activeNetworkAdapter.Name}";
        _networkAddressText.Text = $"IP: {_activeNetworkAdapter.IpAddresses.FirstOrDefault() ?? "—"}    " +
            $"{(isZh ? "网关" : "Gateway")}: {_activeNetworkAdapter.Gateways.FirstOrDefault() ?? "—"}";
        _networkDnsText.Text = $"DNS: {string.Join(", ", _activeNetworkAdapter.DnsServers.Take(2).DefaultIfEmpty("—"))}";
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
        }
        StatusText.Text = success
            ? (enabled ? "DoH enabled" : "DoH disabled")
            : "Unable to change DoH";
        _isOperationInProgress = false;
        _dohToggle.IsEnabled = true;
    }

    private async Task RefreshWifiCaptionAsync()
    {
        var ssid = await NetworkService.GetConnectedWifiSsidAsync();
        if (!string.IsNullOrWhiteSpace(ssid) && _wifiCaption != null)
            _wifiCaption.Text = ssid;
    }

    private void UpdateToggleVisual()
    {
        if (_toggleBtn == null) return;
        _toggleBtn.Background = _isEthernetEnabled ? ActiveBrush : InactiveBrush;
        if (_ethernetExpandButton != null) _ethernetExpandButton.Background = _isEthernetEnabled ? ActiveBrush : InactiveBrush;
        var isZh = App.Settings.Settings.Language == "zh-CN";
        _toggleBtn.Content = new FontIcon { Glyph = "\uE839", FontSize = 17 };
        ToolTipService.SetToolTip(_toggleBtn, _isEthernetEnabled
            ? (isZh ? "有线网卡已启用" : "Ethernet enabled")
            : (isZh ? "有线网卡已关闭" : "Ethernet off"));
    }

    public void ShowAndActivate()
    {
        PositionNearTray();
        Activate();
        var hwnd = WindowNative.GetWindowHandle(this);
        PopupNativeMethods.ShowWindow(hwnd, 9);
        PopupNativeMethods.BringWindowToTop(hwnd);
        PopupNativeMethods.SetForegroundWindow(hwnd);
    }

    private void ShowUsbDetails()
    {
        if (_adapterDetailsCard == null) return;
        if (_adapterDetailsCard.Visibility == Visibility.Visible && _showUsbDetails)
        {
            _adapterDetailsCard.Visibility = Visibility.Collapsed;
            SetExpandRotation(_usbExpandButton, 0);
            AnimatePositionNearTray();
            return;
        }
        _showUsbDetails = true;
        _showWirelessDetails = false;
        _adapterDetailsCard.Visibility = Visibility.Visible;
        SetExpandRotation(_ethernetExpandButton, 0);
        SetExpandRotation(_wifiExpandButton, 0);
        SetExpandRotation(_usbExpandButton, 90);
        PopulateUsbAdapterDetails();
        AnimatePositionNearTray();
    }
    private void ShowAdapterDetails(bool wireless)
    {
        if (_adapterDetailsCard == null) return;
        if (_adapterDetailsCard.Visibility == Visibility.Visible && _showWirelessDetails == wireless)
        {
            _adapterDetailsCard.Visibility = Visibility.Collapsed;
            SetExpandRotation(wireless ? _wifiExpandButton : _ethernetExpandButton, 0);
            AnimatePositionNearTray();
            return;
        }
        _showWirelessDetails = wireless;
        _showUsbDetails = false;
        _adapterDetailsCard.Visibility = Visibility.Visible;
        SetExpandRotation(_ethernetExpandButton, wireless ? 0 : 90);
        SetExpandRotation(_wifiExpandButton, wireless ? 90 : 0);
        SetExpandRotation(_usbExpandButton, 0);
        PopulateAdapterDetails(wireless);
        AnimatePositionNearTray();
    }

    private static void SetExpandRotation(Button? button, double angle)
    {
        if (button?.Content is not FontIcon icon) return;
        icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        icon.RenderTransform = new RotateTransform { Angle = angle };
    }

    private void PopulateAdapterDetails(bool wireless)
    {
        var adapters = wireless ? _networkService.GetWirelessAdapters() : _networkService.GetEthernetAdapters();
        _selectedAdapter = adapters.FirstOrDefault(a => a.IsUp) ?? adapters.FirstOrDefault();
        UpdateSelectedAdapterInfo();
        _adapterListPanel?.Children.Clear();
        foreach (var adapter in adapters)
            _adapterListPanel?.Children.Add(CreateAdapterItem(adapter));
        if (wireless) _adapterListPanel?.Children.Add(CreateWifiNetworkControls());
    }

    private void PopulateUsbAdapterDetails()
    {
        var adapters = _networkService.GetUsbTetheringAdapters();
        _selectedAdapter = _selectedUsbAdapter = adapters.FirstOrDefault(a => a.IsUp) ?? adapters.FirstOrDefault();
        UpdateSelectedAdapterInfo();
        _adapterListPanel?.Children.Clear();
        foreach (var adapter in adapters)
            _adapterListPanel?.Children.Add(CreateAdapterItem(adapter));
    }

    private async void UsbToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUsbAdapter == null || _isOperationInProgress) return;
        _isOperationInProgress = true;
        if (_usbButton != null) _usbButton.IsEnabled = false;
        var success = _selectedUsbAdapter.IsUp
            ? await NetworkService.DisableAdapterAsync(_selectedUsbAdapter.InterfaceName)
            : await NetworkService.EnableAdapterAsync(_selectedUsbAdapter.InterfaceName);
        StatusText.Text = success
            ? (App.Settings.Settings.Language == "zh-CN" ? "USB 网络共享操作成功" : "USB tethering updated")
            : (App.Settings.Settings.Language == "zh-CN" ? "USB 网络共享操作失败" : "USB tethering operation failed");
        _networkService.RefreshAdapters();
        _isOperationInProgress = false;
        if (_usbButton != null) _usbButton.IsEnabled = true;
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

            var statusText = StatusText;
            if (statusText != null)
                statusText.Text = isZh ? "正在切换..." : "Switching...";

            try
            {
                var enabled = await NetworkService.EnableAdapterAsync(adapter.InterfaceName);
                if (enabled && _selectedAdapter is { IsUp: true } current && current.Id != adapter.Id && current.Type == adapter.Type)
                    await NetworkService.DisableAdapterAsync(current.InterfaceName);
                _networkService.RefreshAdapters();
                if (statusText != null) statusText.Text = enabled
                    ? (isZh ? $"已启用 {adapter.Name}" : $"Enabled {adapter.Name}")
                    : (isZh ? "操作失败，请检查管理员权限" : "Operation failed; check administrator privileges");
            }
            finally { _isOperationInProgress = false; }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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

        if (adapter.IsUp)
        {
            var check = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(check, 2);
            grid.Children.Add(check);
        }

        border.Child = grid;
        return border;
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
            var disconnect = new Button { Content = isZh ? $"断开 {ssid}" : $"Disconnect {ssid}", HorizontalAlignment = HorizontalAlignment.Stretch };
            disconnect.Click += async (_, _) =>
            {
                disconnect.IsEnabled = false;
                var success = await NetworkService.DisconnectWifiAsync(adapter.InterfaceName);
                StatusText.Text = success ? (isZh ? "Wi-Fi 已断开" : "Wi-Fi disconnected") : (isZh ? "断开失败" : "Disconnect failed");
                _networkService.RefreshAdapters();
            };
            panel.Children.Add(disconnect);
        }
        panel.Children.Add(new TextBlock { Text = isZh ? "可用网络" : "Available networks", FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0) });
        var networks = await NetworkService.GetAvailableWifiNetworksAsync();
        if (networks.Count == 0) panel.Children.Add(new TextBlock { Text = isZh ? "未发现网络" : "No networks found", FontSize = 11, Opacity = 0.7 });
        foreach (var network in networks.Take(8))
        {
            var connect = new Button { Content = network, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            connect.Click += async (_, _) =>
            {
                connect.IsEnabled = false;
                var success = await NetworkService.ConnectWifiAsync(network, adapter.InterfaceName);
                StatusText.Text = success ? (isZh ? $"正在连接 {network}" : $"Connecting to {network}") : (isZh ? "连接失败：请先在 Windows 中保存此网络" : "Connection failed: save this network in Windows first");
                _networkService.RefreshAdapters();
            };
            panel.Children.Add(connect);
        }
    }
    private void UpdateSelectedAdapterInfo()
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        if (_selectedAdapter == null)
        {
            if (_adapterNameText != null) _adapterNameText.Text = isZh ? "无可用适配器" : "No adapter available";
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
            StatusText.Text = success
                ? (isZh ? "操作成功" : "Operation completed")
                : (isZh ? "操作失败，请检查管理员权限" : "Operation failed; check administrator privileges");
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
        if (_selectedWirelessAdapter == null || _isOperationInProgress) return;
        var isZh = App.Settings.Settings.Language == "zh-CN";
        _isOperationInProgress = true;
        if (_wifiButton != null) _wifiButton.IsEnabled = false;
        try
        {
            var enabled = _selectedWirelessAdapter.IsUp;
            var success = enabled
                ? await NetworkService.DisableAdapterAsync(_selectedWirelessAdapter.InterfaceName)
                : await NetworkService.EnableAdapterAsync(_selectedWirelessAdapter.InterfaceName);
            StatusText.Text = success
                ? (isZh ? "Wi-Fi 操作成功" : "Wi-Fi operation completed")
                : (isZh ? "Wi-Fi 操作失败" : "Wi-Fi operation failed");
            _networkService.RefreshAdapters();
        }
        finally
        {
            _isOperationInProgress = false;
            if (_wifiButton != null) _wifiButton.IsEnabled = true;
        }
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        _showingSettings = !_showingSettings;
        ShowCurrentPage();
    }

    public void ShowSettingsPage()
    {
        _showingSettings = true;
        ShowCurrentPage();
    }

    private void ShowCurrentPage()
    {
        CardsPanel.Opacity = 1;
        CardsPanel.Children.Clear();
        if (_showingSettings)
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
        AnimatePositionNearTray();
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
            Width = 42, Height = 22, Padding = new Thickness(3),
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

        BuildDohServerManager(isZh, cardBackground, cardBorder);

        var about = new Border { Width = 0 };
        CardsPanel.Children.Add(CreateSettingsCard(
            "\uE946", isZh ? "关于" : "About",
            isZh ? "WindowsExtendQuickSetting 1.0\n网络与蓝牙快速控制"
                 : "WindowsExtendQuickSetting 1.0\nQuick network and Bluetooth controls",
            about, cardBackground, cardBorder));
    }

    private void BuildDohServerManager(bool isZh, Brush background, Brush borderBrush)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = isZh ? "DoH 服务器" : "DoH servers", FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        _dohServerInput = new TextBox { PlaceholderText = isZh ? "DNS 服务器 IP，例如 1.1.1.1" : "DNS server IP, e.g. 1.1.1.1" };
        _dohTemplateInput = new TextBox { PlaceholderText = isZh ? "DoH 地址，例如 https://.../dns-query" : "DoH template, e.g. https://.../dns-query" };
        panel.Children.Add(_dohServerInput);
        panel.Children.Add(_dohTemplateInput);

        var addButton = new Button
        {
            Content = isZh ? "添加 DoH 服务器" : "Add DoH server",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        addButton.Click += DohServerSave_Click;
        panel.Children.Add(addButton);

        _dohServerList = new StackPanel { Spacing = 5 };
        foreach (var entry in App.Settings.Settings.DohServers)
            _dohServerList.Children.Add(CreateDohServerRow(entry, isZh));
        panel.Children.Add(_dohServerList);

        CardsPanel.Children.Add(new Border
        {
            Background = background, BorderBrush = borderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Child = panel
        });
    }

    private Border CreateDohServerRow(DohServerEntry entry, bool isZh)
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = $"{entry.Server}\n{entry.Template}", FontSize = 10,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
        });
        var edit = new Button { Content = isZh ? "编辑" : "Edit", Padding = new Thickness(8, 4, 8, 4) };
        edit.Click += (_, _) =>
        {
            _editingDohServer = entry.Server;
            if (_dohServerInput != null) _dohServerInput.Text = entry.Server;
            if (_dohTemplateInput != null) _dohTemplateInput.Text = entry.Template;
        };
        Grid.SetColumn(edit, 1);
        row.Children.Add(edit);
        var remove = new Button { Content = "×", Padding = new Thickness(8, 4, 8, 4) };
        remove.Click += async (_, _) => await DeleteDohServerAsync(entry);
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Child = row
        };
    }

    private async void DohServerSave_Click(object sender, RoutedEventArgs e)
    {
        var server = _dohServerInput?.Text.Trim() ?? "";
        var template = _dohTemplateInput?.Text.Trim() ?? "";
        var isZh = App.Settings.Settings.Language == "zh-CN";
        if (!NetworkService.IsValidDohServer(server, template))
        {
            StatusText.Text = isZh ? "请输入有效的 IP 地址和 HTTPS DoH 地址" : "Enter a valid IP address and HTTPS DoH template";
            return;
        }
        if (_editingDohServer != null)
            await NetworkService.RemoveDohServerAsync(_editingDohServer);
        var success = await NetworkService.AddDohServerAsync(server, template);
        if (!success)
        {
            StatusText.Text = isZh ? "添加 DoH 服务器失败" : "Could not add DoH server";
            return;
        }
        var entries = App.Settings.Settings.DohServers;
        entries.RemoveAll(x => x.Server == (_editingDohServer ?? server) || x.Server == server);
        entries.Add(new DohServerEntry { Server = server, Template = template });
        App.Settings.Save();
        _editingDohServer = null;
        StatusText.Text = isZh ? "DoH 服务器已保存" : "DoH server saved";
        CardsPanel.Children.Clear();
        BuildSettingsView();
    }

    private async Task DeleteDohServerAsync(DohServerEntry entry)
    {
        var isZh = App.Settings.Settings.Language == "zh-CN";
        if (!await NetworkService.RemoveDohServerAsync(entry.Server))
        {
            StatusText.Text = isZh ? "删除 DoH 服务器失败" : "Could not remove DoH server";
            return;
        }
        App.Settings.Settings.DohServers.RemoveAll(x => x.Server == entry.Server);
        App.Settings.Save();
        StatusText.Text = isZh ? "DoH 服务器已删除" : "DoH server removed";
        CardsPanel.Children.Clear();
        BuildSettingsView();
    }

    private static void UpdateCompactToggle(Button button, bool enabled)
    {
        button.Background = enabled
            ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];
        button.BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
        button.HorizontalContentAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        button.Content = new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(7),
            Background = enabled
                ? new SolidColorBrush(Colors.White)
                : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            HorizontalAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
    }

    private static Border CreateSettingsCard(
        string glyph, string title, string description, FrameworkElement trailing, Brush background, Brush borderBrush)
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new FontIcon { Glyph = glyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = description, FontSize = 11, Opacity = 0.65 });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(trailing, 2);
        trailing.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(text);
        grid.Children.Add(trailing);
        return new Border
        {
            Background = background, BorderBrush = borderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch, Child = grid
        };
    }
}

internal static class PopupNativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool BringWindowToTop(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
}

internal static class Win32PInvoke
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    internal static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
