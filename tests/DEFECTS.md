# WEQS 回归测试缺陷台账 (Defect Ledger)

基线: native\PARITY_AUDIT.md · 测试套件: tests\run-tests.ps1 · 最近报告: tests\reports\20260923-083332

状态图例: FIXED=已修复并复测通过 · OPEN=未修复 · WAIVED=环境受限挂起(非产品缺陷)

| ID | 严重级 | 现象 | 复现步骤 | 根因 | 处置 | 提交 | 复测证据 |
|---|---|---|---|---|---|---|---|
| D-001 | 中 | 二次启动会隐藏已可见的弹窗(与 WinUI3 基线"激活即显示"相反); 连锁导致后续自动化用例状态漂移 | 启动 native → 窗口可见 → 再次启动 exe → 窗口淡出隐藏 | kActivateTimer 对 Activate 事件调用 ToggleWindow()(切换语义) | FIXED: 仅在窗口不可见时 ToggleWindow, 可见时只 SetForegroundWindow | e16737b | F03 增加"可见窗口不得被二次启动隐藏"断言, 全量 21/23 |
| D-003 | 中 | 从 DoH 页返回主页后窗口保持 650px 高, 底部 80px 空白 | 进 DoH 页 → 点左上返回箭头 → 主页内容 + 底部空白 | DoH 分支的 y<55 返回路径只改 g_page, 缺 ResizeNearTray()(Settings 分支有) | FIXED: DoH 分支结尾补 ResizeNearTray() | e16737b | F16/F17 导航链中 (30,30) 返回后高度回到 570(pm-probe 实测: 650→570) |
| D-006 | 低(环境) | UI 自动化真实鼠标点击被第三方置顶弹窗(网易云音乐每日推荐、ToDesk 悬浮球)吞掉, 用例随机失败 | 桌面存在第三方置顶弹窗时运行套件 → 部分点击落在弹窗上 | 测试注入的物理鼠标事件命中遮挡窗口; 非产品缺陷 | WAIVED→改用消息级点击(PostMessage WM_LBUTTONUP, app 仅处理该消息); F16/F17 因页面歧义(650 同时是 DoH/设置页高度)保留为环境受限项 | 719fe89 | f16-before-assert.png 截图显示遮挡现场; 消息级点击后 F01-F15/F18-F22/U01 稳定全绿 |
| D-002 | 提示 | USB/以太网/蓝牙/显示卡右半区再次点击不收起详情(仅 Wi-Fi 卡有 toggle-close) | 打开 USB 详情 → 再点 USB 卡右半 → 详情保持 | Tile 点击处理无 kind==当前 的关闭分支; WinUI3 侧行为未核对 | OPEN(记录在案, 不阻塞): 属交互设计歧义, 留待与 WinUI3 基线对齐时统一 | - | PARITY_AUDIT 无对应条目 |

## 数据写入验证记录

| 写入项 | 路径 | 验证方式 | 结果 |
|---|---|---|---|
| 语言切换 | HKCU\Software\WindowsExtendQuickSetting.Native\Language | F17 设计: 点击→读注册表=en-US→恢复→zh-CN | 逻辑路径经代码审查+WinUI3 同源实现验证; 自动化路径受 D-006 遮挡影响挂起 |
| 开机自启动 | HKCU\...\Run\WindowsExtendQuickSetting.Native | F13 只读核对 | PASS(当前值: 不存在, 与界面"已关闭"一致) |
| 虚拟屏驱动启停 | PnP 设备节点 (SetupAPI DICS_ENABLE) | Disable-PnpDevice→Error→Enable-PnpDevice→OK(与 native 同语义) | OK(见 vdd-integration-acceptance.md) |
| DoH 服务器增删 | HKLM Dnscache\Parameters\DohWellKnownServers | 设计为 netsh 提权写入 | 本轮未触发真实写入(安全边界), UI 渲染/校验/取消路径已覆盖 |

## 遗留风险

1. 真实"无物理屏→自动启用虚拟屏"端到端场景需无显示器环境(见 vdd-integration-acceptance.md 遗留节)。
2. F16/F17 的页面身份判定依赖窗口高度, 650 同时是 DoH 与设置页高度; 后续可加标题区像素判定消除歧义。
3. 多显示器/DPI 缩放/侧边任务栏窗口定位(PARITY_AUDIT 未验证项)未在本轮覆盖。
## D-007 (2026-09-24): 适配器启动 ≠ 虚拟屏已开启

| 项 | 内容 |
|---|---|
| 严重级 | 高(功能语义错误) |
| 现象 | ToDesk/Parsec 在设备管理器中"已启动", 但虚拟显示器从未创建; 旧判定把适配器启动当成"已开启", 无屏自动启用从未生效 |
| 根因 | IddCx 模型中适配器(Display 类)与显示器(Monitor 类)是两层; ToDesk/Parsec 的显示器由各自宿主程序按需创建, 驱动装好不会自动出现 |
| 修复 | 判定改为: installed=Display 类有虚拟适配器; active=Monitor 类有 OK 的虚拟监视器(DN_STARTED 且 problem==0)。启用动作后重新检测, 仅在虚拟监视器真出现时报告成功 |
| 提交 | b1af9f7 (native + WinUI3 同步) |
| 验证 | 安装 Virtual-Display-Driver(MttVDD, MIT)后 Monitor 类出现 OK 的 "Generic Monitor (VDD by MTT)", 活动路径 1→2; 弹窗按钮渲染正常 |

### 推荐的虚拟屏驱动
**Virtual-Display-Driver (MttVDD)** — https://github.com/VirtualDrivers/Virtual-Display-Driver (MIT)
设备启动即按 vdd_settings.xml 自动创建显示器(monitors count 可调), 无需宿主程序, 是"装好即用/断物理屏自动在"场景的正确选择。
安装: 把仓库 Releases 的 VDD.Control 包内 SignedDrivers\x64(目录名为 x86 但实际是 amd64 INF)\VDD\ 三件套(MttVDD.inf/.dll/.cat + vdd_settings.xml)放入 drivers 文件夹即可, 产品会在用户点击启用时通过 pnputil 安装。