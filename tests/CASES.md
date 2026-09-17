# WEQS Native 测试用例清单（预期口径）

基线：`native\PARITY_AUDIT.md`（WinUI3 为基准的奇偶性审计）+ `native/main.cpp` 的窗口几何常量。

| ID | 组 | 用例 | 预期 |
|---|---|---|---|
| F01 | launch | 启动后创建主窗体 | 窗口类 `WindowsExtendQuickSetting.Native` 的 HWND 存在 |
| F02 | launch | 启动后主窗体自动可见 | `ToggleWindow()` 在 `wWinMain` 尾部调用，窗口可见（淡入完成后 alpha=255） |
| F03 | launch | 单实例：二次启动激活现有实例且自身退出 | 第二进程通过 `Local\WindowsExtendQuickSetting.Activate` 事件通知后立即退出；第一进程存活 |
| F04 | home | 窗口宽度恒为 368px | `kWindowWidth=368`，物理像素 |
| F05 | home | 紧凑高度 570 / 详情高度 660 | `kCompactHeight=570`、`kDetailsHeight=660`；展开/收起动画结束后窗口高度稳定为预期值 |
| F06 | home | 窗口定位在工作区内且贴近任务栏 | `PopupBoundsNearTaskbar`：窗口完整位于当前监视器工作区，底边距工作区底 < 60px（任务栏侧） |
| F07 | details | 以太网右半区进入网络优先级详情 | 高度 660；detailsKind=3 |
| F08 | details | 显示卡右半区进入屏幕管理详情 | 高度 660；含复制/扩展/系统设置/HDR/虚拟屏五个按钮 |
| F09 | details | 蓝牙右半区进入蓝牙详情；扫描完成 | 高度 660；扫描为只读枚举，完成后状态行提示"扫描完成" |
| F10 | details | USB 右半区进入 USB 共享详情 | 高度 660；无设备时显示引导文案（本机当前无 RNDIS 设备） |
| F11 | settings | 底部齿轮进入设置页 | 高度 650（`kPageHeight`） |
| F12 | settings | 设置页跳转网络优先级详情 | 高度 660，detailsKind=3 |
| F13 | settings | 开机自启动注册表读取 | 只读：HKCU Run 键值存在性记录进报告（两种状态都合法） |
| F14 | doh | 当前网络卡 DoH 摘要行进入 DoH 页 | 高度 650；页内含全局开关卡、首选/备用选择、服务器管理卡 |
| F15 | doh | 首选 DoH 选择器：打开/筛选/Esc 关闭 | 选择器浮层出现；键入字符过滤列表；Esc 关闭浮层不崩溃 |
| F16 | doh | DoH 搜索框弹出 Prompt 后取消 | `WindowsExtendQuickSetting.Native.Prompt` 对话框出现；点取消后关闭、返回 DoH 页 |
| F17 | language | 切换 English 后写入 HKCU 并恢复 | 注册表 `Language=en-US`；界面语言切换；用例内恢复 `zh-CN`（并记录原值） |
| F18 | hide | Esc 键隐藏窗口 | `WM_KEYDOWN` Escape → `ShowWindow(SW_HIDE)`，窗口不可见 |
| F19 | reactivate | 再次启动进程令现有实例重新弹出 | 第二进程退出；现有窗口重新可见 |
| F20 | reactivate | 3 秒刷新周期不崩溃 | 等待 ≥2 个刷新周期，进程存活、窗口存在 |
| F21 | exit | 托盘退出命令正常退出 | `WM_COMMAND kTrayExit(1003)` → 进程退出码 0 |
| F22 | exit | 退出后窗口销毁 | HWND 失效，托盘图标与互斥体清理 |
| U01 | visual | 主页启用快捷卡为主题蓝 | Wi-Fi 卡中心像素 ≈ RGB(0,103,192)±容差（网络已连接时卡片启用） |

## 证据要求

- 每条界面用例截图存 `screenshots/`，报告表格内引用相对路径。
- 窗口几何用例记录实测像素值。
- 语言切换记录注册表 before/after。
