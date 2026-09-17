# WindowsExtendQuickSetting · Native 自动化回归测试套件

## 一键运行（仓库根目录）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1
```

- 被测程序默认取 `release\native\WindowsExtendQuickSetting.Native.exe`，可用 `-ExePath` 覆盖。
- 产物写入 `tests\reports\<timestamp>\`：`junit.xml`（CI 可解析）、`report.md`（人读报告）、`env.json`（执行环境快照）、`screenshots\*.png`（界面证据）。
- 退出码：全绿 `0`，有失败 `1`。

## 用例口径

以 `native\PARITY_AUDIT.md` 为验收基线，每条用例的预期写在 `tests\CASES.md` 与报告中。覆盖：

- 启动 / 单实例互斥与激活 / 托盘退出
- 窗口几何（宽度 368、紧凑 570 / 详情 660 / 设置与 DoH 650）与任务栏定位
- 五张快捷卡详情开合（以太网、Wi-Fi、蓝牙、USB、屏幕）
- 设置页、DoH 页（选择器、搜索、取消路径）
- 语言切换的真实注册表写入与恢复（测试后自动还原）
- Esc 隐藏、二次启动再激活、3 秒刷新周期稳定性
- 界面像素抽查（主题色渲染）

## 安全边界

测试只做读侧与 UI 侧行为：不切换网卡、不删除 Wi-Fi、不改 DNS、不写 DoH 注册表、不改开机自启状态（仅读取）。语言切换会写 `HKCU\Software\WindowsExtendQuickSetting.Native\Language`，用例内验证后立即恢复原值。

## 手工前置

- 需要 Windows 桌面会话（真实交互桌面，不能是纯 Service 会话）。
- 先构建：`powershell -ExecutionPolicy Bypass -File .\native\build-release.ps1`。
- 测试运行期间请勿移动鼠标（真实鼠标事件注入）。
