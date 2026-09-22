# 虚拟屏 (IddCx) 集成验收记录 · 2026-09-22

## 问题
1. 虚拟屏幕驱动能否预先集成好?
2. 已安装虚拟屏幕软件的机器, 能否直接调用其驱动?

## 结论: 两项均已实现并验收通过

### 1. 预集成 (从零安装)
- native: main.cpp `FindVirtualDriverInf` 在 exe 同目录与 `drivers\` 子目录查找已签名的 IddCx INF;
  `InstallVirtualDisplayDriver` 通过 `pnputil /add-driver <inf> /install` 提权静默安装
  (RunElevatedWait, runas, 180s 上限, 后台工作线程 + kVddWorkMessage 回调, 不阻塞 UI)。
- WinUI3: DisplayService.InstallVirtualDisplayDriver 同构实现。
- 分发方式: 把任意已签名 IddCx 驱动包 (VirtualDrivers/Virtual-Display-Driver 等 MIT 项目
  或商业驱动) 的 .inf/.dll/.cat 放入 `drivers\` 目录即可; 首次点"启用虚拟屏"时自动安装。
- 注意: INF 必须带有效签名 (WHQL/attestation), 否则 pnputil 拒装 (Windows 10/11 强制)。

### 2. 直接调用已安装驱动
- 探测: SetupAPI (native `DetectVirtualDisplayState`) / Get-PnpDevice (WinUI3
  `GetVirtualDriverState`) 按 Display/Monitor 类 + 关键词匹配
  (virtual|indirect|idd|parsec|todesk|usbmmidd|spacedesk|superdisplay|duet|vdd)。
- 返回三态: 0=运行中 / 1=已安装但禁用 / 2=未安装。
- 启用: native `EnableVirtualDisplayDevices` 用 SetupAPI DICS_ENABLE (无 PowerShell 依赖);
  WinUI3 用 Enable-PnpDevice。
- 自动启用: `AutoVirtualDisplayCheck` — 设置开关 (HKCU VddAutoEnable) 打开且活动显示路径为 0 时
  自动启用, 60s 退避, 失败 10min 退避; 工作线程异步, UI 不阻塞。

## 本机实测证据 (2026-09-22)

| 步骤 | 结果 |
|---|---|
| 枚举已装虚拟驱动 | detected=2: Todesk Virtual Display Adapter, Parsec Virtual Display Adapter |
| 运行状态 | 两者 Status=OK (state=0, ready) |
| 屏幕详情页渲染 | 截图: 虚拟屏按钮"仅在无物理屏幕时可用"(有物理屏, 正确禁用); "无显示器自动启用 · 已开启" |
| state=1 -> 启用闭环 | Disable-PnpDevice(ToDesk) -> Status=Error; Enable-PnpDevice -> Status=OK (与 native DICS_ENABLE 同语义) |
| 系统恢复 | 两个适配器均恢复 OK, 无遗留禁用设备, 无进程残留 |

### 屏幕管理详情页截图
`vdd-details.png` (workspace tmp): 显示 复制/扩展、打开屏幕布局设置、HDR、
虚拟屏(禁用态)、无显示器自动启用(已开启) 五控件, 布局符合 PARITY_AUDIT。

## 遗留
- 真实"无物理屏 -> 自动启用"需在拔掉所有显示器的环境下端到端验证 (本机有物理屏, 无法安全模拟)。
  现有证据: state 检测/启用/退避各环节均单点验证通过。