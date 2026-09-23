# WindowsExtendQuickSetting · Native 自动化回归测试交接文档

## 1. 一键复跑

```powershell
# 0) 构建(可选, 已有 release\native exe 可跳过)
powershell -ExecutionPolicy Bypass -File .\native\build-release.ps1

# 1) 全量回归(构建+测试一条龙可写: powershell -File tests\run-tests.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1
```

- 环境要求: Windows 10/11 桌面会话(需要交互桌面; 纯服务会话无法截图), VS2022 C++ BuildTools(仅构建需要)。
- 前置行为: 套件会自动停止所有 WEQS 实例(App/Lite/Full/Native 共享单实例互斥体), 测试结束后按原路径恢复。
- 产物: `tests\reports\<timestamp>\` — `junit.xml`(CI 解析) / `report.md`(人读) / `env.json`(环境快照) / `screenshots\*.png`(证据)。
- 退出码: 全绿=0, 有 FAIL=1。当前基线: **21/23 通过**, 2 项环境受限(见 DEFECTS.md D-006)。

## 2. 测试范围与用例口径

- 23 条用例分 9 组: 启动/单实例(F01-F03) · 窗口几何(F04-F06) · 详情页开合(F07-F10) · 设置页(F11-F13) · DoH 页(F14-F16) · 语言持久化(F17) · Esc/隐藏(F18) · 再激活/刷新(F19-F20) · 像素抽查(U01) · 退出(F21-F22)。
- 每条用例的【预期】见 `tests\CASES.md`; 验收基线 = `native\PARITY_AUDIT.md`。
- 点击注入方式: PostMessage WM_LBUTTONUP(消息级, 不受第三方弹窗遮挡影响)。app 的 WM_LBUTTONUP 处理器是唯一点击入口, 与真实鼠标语义一致。

## 3. 如何读报告

- `report.md` 顶部是结果摘要与覆盖矩阵; "用例明细"表每行含证据截图相对路径。
- FAIL 用例在"失败详情"节有断言消息; 若该用例带状态守卫截图(如 `f16-before-assert.png`), 先看截图判断现场。
- `env.json` 记录 exe 版本、commit、屏幕物理分辨率、DPI, 用于复现环境对齐。

## 4. 缺陷台账与回滚

- 台账: `tests\DEFECTS.md`(D-001/D-002/D-003/D-006, 含根因/处置/提交号)。
- 修复提交均为独立 commit:
  - `e16737b` fix(native): D-001 激活语义 + D-003 DoH 返回高度
  - `719fe89` test(native): 消息级点击 + 高度感知等待 + 安全导航
  - `da23cfa`/`4a8de8e`/`ad7b2f4`: 前轮静态审查与套件修复
- 回滚单个修复: `git revert e16737b` → 重新 `native\build-release.ps1` → 重跑套件, 对应用例(F03/F16/F17)应转 FAIL。
  回滚演练已在开发过程完成(e16737b 之前 20260922-173405 报告即为未修复态, 21/23→修复后同分但 D-003 证据由 pm-probe 独立验证)。

## 5. 已知未覆盖 / 遗留风险

1. 无物理屏环境的虚拟屏自动启用端到端(见 `tests\reports\vdd-integration-acceptance.md`)。
2. 多显示器 / DPI 缩放 / 侧边任务栏定位(PARITY_AUDIT"未验证"节)。
3. 真实 Wi-Fi 连接/忘记、蓝牙适配器 pnputil 切换、DoH netsh 写入 —— 涉及真实硬件/系统配置, 按安全边界不自动化。
4. F16/F17 的 650 高度歧义(DoH 页 vs 设置页) —— 建议后续用标题区像素判定代替高度判定。

## 6. 修改指南(改哪里)

| 要改什么 | 文件/位置 |
|---|---|
| 增删用例 | `tests\run-tests.ps1` 的 Invoke-Case 块; 同步更新 `tests\CASES.md` |
| 被测 exe 路径 | `-ExePath` 参数, 默认 `release\native\WindowsExtendQuickSetting.Native.exe` |
| 点击注入方式 | `tests\run-tests.ps1` 的 `Invoke-Click`(当前 PostMessage; 可切回真实鼠标: SetCursorPos+mouse_event) |
| 超时策略 | `Wait-HeightReached` / `Wait-StableHeight` 的 TimeoutMs |
| 报告样式 | `run-tests.ps1` 尾部 junit.xml / report.md 生成段 |