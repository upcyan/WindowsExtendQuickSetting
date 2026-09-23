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