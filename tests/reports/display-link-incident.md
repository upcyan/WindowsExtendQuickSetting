# 显示链路状态快照 (2026-09-24 09:20 左右, 重启前)

## 现象
- 物理显示器 TCL R27Q71 (2560x1440 原生) 当前以 640x480@64 基本模式工作
- 两次 GPU 驱动重启(pnputil restart-device NVIDIA + AMD)与监视器设备重枚举均未恢复链路
- 软件可用的最高模式只有 640x480@64 → GPU↔显示器链路握手失败, 需要重启电脑(或重新插拔 HDMI/DP 线)重新训练

## 试验期间产生的虚拟屏设备状态(重启后核对用)
- ROOT\DISPLAY\0000: Todesk Virtual Display Adapter (旧占位设备, OK/已禁用又启用)
- ROOT\DISPLAY\0001: Parsec Virtual Display Adapter (OK, 已禁用又启用)
- ROOT\DISPLAY\0002: Parsec 重复设备 (已 pnputil /remove-device 删除)
- ROOT\DISPLAY\0003: Todesk tdIdd (devcon install 新建, OK)
- MttVDD (Virtual-Display-Driver): 已下载未安装, 包在 %TEMP%\vdd-control\x\SignedDrivers\x86\VDD\
- Parsec 官方控制工具: %TEMP%\parsec-vdd-tool\parsec-vdd-0.45.0.0.exe (CLI add 会挂起, 未成功创建显示器)

## 重启后恢复步骤
1. 确认物理屏恢复 2560x1440 (若无: 设置→系统→屏幕→分辨率)
2. 决定保留哪些虚拟屏设备; 不需要的用 pnputil /remove-device <实例ID> 清理
3. 用户产品正确集成方向见 tests\reports\vdd-integration-acceptance.md 更新版