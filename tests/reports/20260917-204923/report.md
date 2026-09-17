# WEQS 回归测试修复与代码审查报告

- 时间: 2026-09-17 20:49
- 基线: 上次测试结果 0/23 通过 (20260917-203032)
- 操作: 修复测试脚本 3 个阻断性 Bug + 修复 WinUI3 源码 2 个缺陷

---

## 1. 测试脚本 Bug 修复 (run-tests.ps1)

上次回归测试 23/23 全部失败，根因是测试脚本自身的 3 个 Bug：

| # | Bug | 影响 | 修复方式 |
|---|-----|------|----------|
| 1 | `Assert-True` 函数未定义 | 18 个用例报 "无法将 Assert-True 识别为 cmdlet" | 在 bookkeeping 区域添加函数定义 |
| 2 | `& $Body` 未传递 `$Case` 参数 | 3 个用例 (F15/F17/U01) 报 "$c.Shot 属性找不到" | 改为 `& $Body $Case` |
| 3 | `[T]::FindWindowW` 方法不存在 | F16 报 "方法调用失败" | 删除重复的无效调用，保留正确的 `FindByClass` |

额外修复：
- **UTF-8 BOM 丢失**：编辑过程中文件头 BOM 被移除，导致 PowerShell 5.x 解析报 "字符串缺少终止符"。已恢复 BOM。

## 2. WinUI3 源码缺陷修复

| # | 文件 | 缺陷 | 修复 |
|---|------|------|------|
| 1 | `NetworkAdapter.cs:42` | `GetDisplayInfo()` 硬编码中文 `"未分配"` 作为 IP 缺省值 | 改为参数化 `fallbackIp = "N/A"`，调用方按语言传入 |
| 2 | `NetworkAdapter.cs:33-40` | `SpeedText` 第一个分支 `>= 10_000_000_000` 与第二个 `>= 1_000_000_000` 完全重复，属死代码 | 删除冗余分支 |
| 3 | `QuickSettingsPopup.xaml.cs:1526` | `GetDisplayInfo()` 调用时未传入语言感知的 fallback | 改为 `GetDisplayInfo(isZh ? "未分配" : "N/A")` |

## 3. 静态代码审查摘要

对全部 12 个源文件进行了系统性审查：

### 已确认无问题的区域

| 区域 | 审查结论 |
|------|----------|
| 线程安全 | `NetworkService._syncRoot` 正确保护 `_adapters` 列表；`BluetoothService._bluetoothRefreshInProgress` 防并发刷新 |
| 单实例互斥 | Mutex + EventWaitHandle 协调正确，包含 stale owner 重试和版本升级路径 |
| Settings 持久化 | JSON 文件 + Registry Run 键双重写入，Load/Save 路径一致 |
| 托盘图标 | 自适应浅色/深色任务栏，GDI fallback 蓝色位图兜底 |
| 窗口定位 | 多显示器 MONITORINFO.rcWork 定位，边界 clamp 到工作区 |
| DoH 管理 | 首选/备用/导入/删除/系统注册表路径均正确 |
| 蓝牙适配器 | 单适配器约束、切换原子失败语义、热插拔 Toast 提示 |
| 屏幕管理 | DisplayConfig API 活动路径检测、HDR 读写后回读验证 |
| Event 订阅 | `OnClosed` 中正确取消所有事件订阅、释放 DispatcherTimer/CancellationToken/Controller |

### 已知未验证区域（无 Bug 但缺实测证据）

- 多显示器 DPI 缩放下的窗口定位
- 企业 Wi-Fi 证书认证
- BLE-only 设备配对
- IddCx 虚拟屏驱动启用
- 侧边任务栏定位

## 4. 变更文件清单

| 文件 | 变更类型 |
|------|----------|
| `tests/run-tests.ps1` | Bug 修复 × 3 + BOM 恢复 |
| `winui3/.../Models/NetworkAdapter.cs` | 缺陷修复 × 2 |
| `winui3/.../Views/QuickSettingsPopup.xaml.cs` | 缺陷修复 × 1 |

## 5. 测试运行说明

测试脚本在无桌面会话的沙箱环境中启动成功但无法找到窗口（符合预期）。在真实桌面环境下运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1
```

全量 23 条用例覆盖：启动/单实例、窗口几何、快捷卡详情开合、设置页、DoH 页、语言切换、Esc 隐藏、刷新稳定性、像素检查、退出。
