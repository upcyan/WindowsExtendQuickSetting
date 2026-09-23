# WEQS Native 自动化回归测试报告

- 时间: 20260923-082950
- 被测程序: `E:\project\WindowsExtendQuickSetting\release\native\WindowsExtendQuickSetting.Native.exe`
- 版本: 1.2.15.0  |  commit: 865575f
- 结果: **21 / 23 通过**, 2 失败

## 覆盖矩阵

| 组 | 用例数 | 通过 | 失败 |
|---|---|---|---|
| launch | 3 | 3 | 0 |
| home | 3 | 3 | 0 |
| details | 4 | 4 | 0 |
| settings | 3 | 3 | 0 |
| doh | 3 | 2 | 1 |
| language | 1 | 0 | 1 |
| hide | 1 | 1 | 0 |
| reactivate | 2 | 2 | 0 |
| visual | 1 | 1 | 0 |
| exit | 2 | 2 | 0 |

## 用例明细

| # | ID | 用例 | 结果 | 耗时ms | 证据 |
|---|---|---|---|---|---|
| 1 | F01 | 启动后创建主窗体 | PASS | 13 |  |
| 2 | F02 | 启动后主窗体自动可见(托盘弹出) | PASS | 673 | screenshots\01-home-initial.png |
| 3 | F03 | 单实例: 二次启动激活现有实例且自身退出 | PASS | 876 |  |
| 4 | F04 | 窗口宽度恒为 368px(物理像素) | PASS | 3 |  |
| 5 | F05 | 紧凑高度 570 / 详情高度 660 | PASS | 959 | screenshots\02-home-wifi-details.png |
| 6 | F06 | 窗口定位在工作区内且贴近视觉任务栏角 | PASS | 15 |  |
| 7 | F07 | 以太网右半区进入网络优先级详情(660) | PASS | 284 | screenshots\03-details-ethernet.png |
| 8 | F08 | 显示卡右半区进入屏幕管理详情(含复制/扩展/HDR/虚拟屏) | PASS | 1294 | screenshots\04-details-display.png |
| 9 | F09 | 蓝牙右半区进入蓝牙详情; 扫描按钮完成一次扫描 | PASS | 4344 | screenshots\06-details-bluetooth-scanned.png |
| 10 | F10 | USB 右半区进入 USB 共享详情 | PASS | 933 | screenshots\07-details-usb.png |
| 11 | F11 | 底部齿轮进入设置页(650) | PASS | 3712 | screenshots\08-settings-page.png |
| 12 | F12 | 设置页: 跳转网络优先级详情 | PASS | 1958 | screenshots\09-settings-to-priority.png |
| 13 | F13 | 开机自启动: 读取注册表当前值与界面一致 | PASS | 14 | HKCU Run value present: False |
| 14 | F14 | 当前网络卡 DoH 摘要行进入 DoH 页(650) | PASS | 3960 | screenshots\10-doh-page.png |
| 15 | F15 | 首选 DoH 选择器: 打开/筛选/Esc 关闭 | PASS | 1233 | screenshots\12-doh-picker-filtered.png |
| 16 | F16 | DoH 搜索框: 弹出 Prompt 对话框后取消 | FAIL | 20363 | screenshots\13-f16-before-assert.png |
| 17 | F17 | Toggle English -> registry en-US -> restore zh-CN | FAIL | 20361 | ASSERT FAILED: not on compact home, height=650 |
| 18 | F18 | Esc 键隐藏窗口 | PASS | 171 | hidden via WM_KEYDOWN Escape |
| 19 | F19 | 再次启动进程令现有实例重新弹出窗口 | PASS | 784 | screenshots\14-reactivated.png |
| 20 | F20 | 切换页后的 3 秒刷新周期不崩溃(等待 2 个周期) | PASS | 6534 | screenshots\15-after-refresh-cycles.png |
| 21 | U01 | 主页快捷卡配色: 启用卡为蓝色主题色(0,103,192) | PASS | 1927 | screenshots\tmp-u01.png |
| 22 | F21 | 托盘退出: WM_COMMAND kTrayExit(1003) 正常退出 | PASS | 9 |  |
| 23 | F22 | 退出后托盘图标与互斥体清理(窗口销毁) | PASS | 525 |  |

## 失败详情

### F16 DoH 搜索框: 弹出 Prompt 对话框后取消

``````
ASSERT FAILED: not on compact home, height=650
``````

### F17 Toggle English -> registry en-US -> restore zh-CN

``````
ASSERT FAILED: not on compact home, height=650
``````

