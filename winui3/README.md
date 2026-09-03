# Windows Extend Quick Setting

Windows 11 风格的系统托盘快速设置面板，用于管理有线网络适配器。

## 功能

- **系统托盘图标** — 左键点击弹出快速设置面板，右键打开设置/退出菜单
- **以太网卡管理** — 列出所有物理有线网卡，支持启用/禁用切换
- **多网卡切换** — 点击列表中的网卡即可自动断开当前、连接目标网卡
- **开机自启** — 通过注册表实现（设置中可开关）
- **双语支持** — 中文/英文界面

## 技术栈

| 组件 | 版本 |
|------|------|
| .NET | 9.0 |
| Windows App SDK | 1.6 |
| WinUI 3 | 是 |
| 通知图标 | 原生 Win32 Shell_NotifyIcon |

## 构建

```bash
dotnet build -c Debug
```

或使用 MSBuild（推荐，可获得完整 XAML 编译错误）：

```bash
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" .csproj -t:Build -p:Configuration=Debug
```

## 运行要求

- Windows 10 1809+ / Windows 11
- Windows App Runtime 1.6（首次运行如未安装，WinAppSDK 引导程序会提示下载）
- 管理员权限（启用/禁用网卡需要）

## 项目结构

```
WindowsExtendQuickSetting/
├── App.xaml / App.xaml.cs          # 应用入口，权限检查
├── Helpers/
│   └── ElevateHelper.cs            # UAC 提权
├── Models/
│   ├── AppSettings.cs              # 设置模型
│   └── NetworkAdapter.cs           # 网卡数据模型
├── Services/
│   ├── NetworkService.cs           # 网卡发现与操作（netsh）
│   ├── SettingsService.cs          # JSON 持久化 + 注册表自启
│   └── TrayService.cs              # Win32 托盘图标（P/Invoke）
└── Views/
    ├── QuickSettingsPopup.xaml.cs  # 快速设置弹窗（纯 code-behind）
    └── SettingsWindow.xaml         # 设置窗口
```
