# FrameScope UI Motion Implementation Report

日期：2026-05-17  
角色：FrameScope Monitor UI 交互实现负责人  
范围：UI 视觉 polish 后的 handler 回归复核与交互动效补丁。

## 1. 用户反馈的问题

用户反馈当前界面动画异常闪烁、卡顿，表现像简单闪烁和生硬切换。要求在不重做视觉主题、不恢复 Live / FPS 实时页面、不改后端采样与报告数据结构的前提下，修复页面切换、左侧导航、按钮、状态反馈和进程选择器的真实交互体验。

## 2. UI 设计 polish 后的交互复核结果

- 左侧导航只保留 `overview / targets / reports / settings / about`，未恢复 `live`。
- `FrameScopeVisiblePageRules.NormalizeKey("live")` 仍归一到 `overview`，`--ui-page live` 截图输出为 overview 页面。
- `UiRouting.ShowPage()` 归一页面 key 后只执行 `StopLiveRefresh()`，没有调用 `StartLiveRefresh()`。
- Report / Settings / Targets 的主要按钮仍绑定真实 handler，没有把按钮降级成文案或假按钮。
- 当前 git 工作树存在既有/并行变更，本报告以本文“修改文件清单”列出的交互源码、测试、构建 wiring 和报告产物为本线程范围。

## 3. 已确认真实有效的 handler

### 左侧导航

- `overview`：`FrameScopeReferenceSidebar.NavigationRequested` -> `ShowPage("overview")`
- `targets`：`NavigationRequested` -> `ShowPage("targets")`
- `reports`：`NavigationRequested` -> `ShowPage("reports")`
- `settings`：`NavigationRequested` -> `ShowPage("settings")`
- `about`：`NavigationRequested` -> `ShowPage("about")`
- 兼容 key：`ShowPage("live")` -> `FrameScopeVisiblePageRules.NormalizeKey("live")` -> `overview`

### Reports 页面

- 打开报告目录：`OpenSelectedReportFolder()`
- 打开 HTML 报告：`OpenSelectedReport()`
- 打开详细报告：`OpenSelectedDetailedReport(Button)` -> `FrameScopeDiagnostics.GenerateReport(...)` -> 打开生成的 markdown
- 刷新列表：`ShowPage("reports")`
- 重新生成：`RegenerateSelectedReport(Button)`
- 缺失选中项、缺失 HTML、缺失 runDir 时由 `FrameScopeReportActionRules.ResolveAvailability(...)` 控制禁用或提示。

### Settings 页面

- checkbox / 输入框 / grid 变更：`AttachConfigDirtyFeedback(...)` -> `SetConfigDirtyStatus()` -> 状态提示“设置已修改，点击保存设置生效”
- 保存设置：`SaveConfigFromGrid()`，失败时 `SetStatus(...)` + `MessageBox.Show(...)`
- 恢复默认：`ResetConfigToDefaultsFromUi()`
- 选择目录：`BrowseDataRoot()` -> `FolderBrowserDialog` -> `SetConfigDirtyStatus()`

### Targets 页面

- 刷新进程：`RefreshProcessList()` -> `OpenProcessPickerDialog()`
- 添加进程：`AddSelectedProcess()`，会规范化进程名并保存配置
- 保存配置：`SaveConfigFromGrid()`
- 启动监控：`StartWatcher()`，保留 watcher in-flight 防重复点击逻辑
- 停止监控：`StopWatcher()`，保留 watcher in-flight 防重复点击逻辑
- 进程输入框 / 下拉箭头：`OpenProcessPickerDialog()`，打开、取消、选择成功都有状态反馈

## 4. 本轮动画设计原则

- 不重做视觉主题，不改 UI 设计侧刚统一的圆角、间距、卡片层级、色彩和布局比例。
- 动画只服务真实状态变化：页面切换、导航 active/hover、按钮 hover/disabled、状态 pill、进程选择器反馈。
- 统一使用轻量 WinForms `Timer` 和可 Dispose motion handle，避免每个控件散落独立短 timer。
- 动画时长保持低成本：按钮 hover 约 120ms，按钮 press 约 80ms，nav active 约 160ms，状态 pulse 约 220ms，页面 settle 约 200ms。
- 页面切换期间用 `pageTransitionInFlight` 防止重复触发，避免状态错乱。
- 不调用后端采样、watcher、MonitorSession、report generator 数据结构、GameLite / WMI / SGuard。

## 5. 修改文件清单

核心 UI 交互源码：

- `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- `src\app\FrameScopeNativeMonitor.UiStatusDisplay.cs`
- `src\app\FrameScopeNativeMonitor.UiScreenshots.cs`
- `src\app\FrameScopeNativeMonitor.UiProcessPicker.cs`
- `src\ui\FrameScopeButtons.cs`
- `src\ui\FrameScopeReferenceSidebar.cs`
- `src\ui\FrameScopeReferenceSidebar.CompactDrawing.cs`
- `src\ui\FrameScopeReferenceSidebar.ReferenceDrawing.cs`
- `src\ui\FrameScopeMotion.cs`

TDD / 构建支撑：

- `tests\FrameScopeUiStateTests.cs`
- `tests\Build-FrameScopeTests.ps1`
- `build.ps1`：本轮因新增 `src\ui\FrameScopeMotion.cs`，仅用于编译 source list wiring，作为本轮独占构建 wiring 修改说明。

交付报告：

- `docs\implementation-reports\2026-05-17-framescope-ui-motion-implementation-report.md`

## 6. 具体交互与动画改动

页面切换：

- `ShowPage()` 仍先归一 key，再停止 live refresh，再构建目标页面。
- 新增 `pageTransitionInFlight`，动画过程中拒绝重复页面切换。
- 新页面完成一次同步绘制后做 8px -> 0px 的轻量 settle 动画，避免透明 overlay 在 WinForms 中遮掉子控件导致黑屏/骨架闪烁。
- 失败页面仍走 `BuildPageLoadError(...)`，并保留状态提示。

左侧导航：

- `FrameScopeReferenceSidebar` 新增 per-item `activeAmounts` / `hoverAmounts`。
- `ActiveKey` 和 hover key 变化时通过 `FrameScopeMotion.Animate(...)` 插值，不再直接跳颜色。
- compact/reference 两套 drawing 都读取 active/hover amount，边框、填充、文字颜色自然过渡。

按钮：

- `FrameScopeRoundedButton` 的 BackColor 与 Enabled 状态改为插值过渡。
- `FrameScopeNavButton` 的 hover、pressed、active 改为独立 amount 动画。
- 禁用态通过 disabled amount 过渡到暗化背景、边框、文字，不直接跳变。

状态栏 / 状态 pill：

- `SetStatus()` 不再每次创建互相叠加的短 timer。
- 新增单个 `statusPulseMotion`，新状态到来时先 Dispose 上一个 pulse，再执行 220ms 颜色插值。
- watcher summary 与 status pill 的最终 READY/RUNNING 文案在动画完成后落定。

进程选择器：

- 打开进程选择器时显示“正在打开进程选择器...”。
- 用户取消时显示“已关闭进程选择器，未添加新目标”。
- 选择进程后显示“已选择进程：xxx”，随后继续走 `AddSelectedProcess()` 真实添加逻辑。

## 7. 是否新增统一 motion helper

已新增 `src\ui\FrameScopeMotion.cs`，包含：

- `Clamp01(float)`
- `EaseOutCubic(float)`
- `EaseInOutCubic(float)`
- `LerpFloat(float, float, float)`
- `LerpColor(Color, Color, float)`
- `Animate(Control, int, Action<float>[, Action])`

`Animate(...)` 返回 `IDisposable`，调用方在新动画开始或控件 Dispose 时释放，避免 timer 泄漏。

## 8. 测试结果

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`：PASS，生成 `dist\FrameScopeMonitor-Setup.exe`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`：PASS，输出 `FrameScope tests rebuilt.`
- `.\tests\FrameScopeUiStateTests.exe`：PASS
- `.\tests\FrameScopeReportProgressTests.exe`：PASS
- `node .\tests\chart-sampling-tests.js`：PASS。本机默认 WindowsApps `node.exe` 曾返回 Access denied，本轮最终验证通过临时把 Codex bundled Node 放到 PATH 前面执行同一命令。
- `"C:\Program Files\Git\cmd\git.exe" diff --check`：PASS，只有既有 CRLF warning：`README.md`、`build.ps1`、`framescope-config.example.json`
- 残留进程检查：PASS，未发现 `FrameScopeMonitor / PresentMon / FrameScopeProcessSampler / FrameScopeSystemSampler / FrameScopeReportGenerator / FakePresentMon / TslGame / GameLite`。

## 9. 静态截图路径

- `artifacts\ui-motion-20260517-overview.png`
- `artifacts\ui-motion-20260517-targets.png`
- `artifacts\ui-motion-20260517-settings.png`
- `artifacts\ui-motion-20260517-reports.png`
- `artifacts\ui-motion-20260517-about.png`
- `artifacts\ui-motion-20260517-live-normalized-overview.png`：验证 `--ui-page live` 归一到 overview，不代表恢复 Live 页面。

## 10. 动画连续帧路径

目录：`artifacts\ui-motion-20260517-frames\`

- `baseline-overview-00.png`
- `transition-overview-targets-00.png` 到 `transition-overview-targets-08.png`
- `transition-targets-reports-00.png` 到 `transition-targets-reports-08.png`
- `transition-reports-settings-00.png` 到 `transition-reports-settings-08.png`
- `sidebar-hover-about-00.png` 到 `sidebar-hover-about-06.png`
- `sidebar-hover-return-00.png` 到 `sidebar-hover-return-04.png`
- `button-hover-save-00.png` 到 `button-hover-save-05.png`
- `button-pressed-save-00.png` 到 `button-pressed-save-04.png`
- `status-dirty-feedback-00.png` 到 `status-dirty-feedback-08.png`

观察结果：未见整页白屏/黑屏闪一下；页面切换有轻量 settle；sidebar hover/active 无抖动；按钮 hover/pressed 有过渡；checkbox 触发 settings dirty 状态反馈。WinForms 的 DataGridView / 子控件在极早期帧仍可能先画框再补文字，这是 WinForms 原生控件绘制限制，已通过同步 `Update()` 和轻量位移动画降低可见程度。

## 11. 未修改的禁止范围

本轮未修改：

- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession*.cs`
- `src\app\FrameScopeNativeMonitor.ReportOpen*.cs`
- `src\app\FrameScopeNativeMonitor.ReportStatus.cs`
- `src\core\`
- `src\monitoring\`
- `src\diagnostics\`
- `src\reporting\`
- `scripts\lightweight\`
- GameLite / WMI / SGuard
- `packaging\`
- Live / FPS 实时页面未恢复
- 报告数据结构未修改
- UI 视觉主题未整体重做

## 12. 已知 WinForms 限制

- WinForms 透明 overlay 会以父背景模拟透明，覆盖子控件时容易造成黑块或骨架帧，因此最终没有使用全屏透明遮罩。
- DataGridView 和部分子控件的首次绘制不是严格逐像素同步，连续帧里可能看到极早期的框线先出现、文字后出现。
- 当前实现选择低 CPU 的短时 `System.Windows.Forms.Timer`，不引入高频 compositor 或后台线程动画，避免影响监控链路。

## 13. 给测试员和 bug 修复窗口的定位提示

- 如果页面切换仍反馈闪烁，优先检查 `src\app\FrameScopeNativeMonitor.UiRouting.cs` 的 `PreparePageForTransition(...)`、`StartPageTransition(...)` 和 `pageTransitionInFlight`。
- 如果 nav hover/active 抖动，检查 `src\ui\FrameScopeReferenceSidebar.cs` 的 `AnimateItemAmount(...)` 以及 compact/reference drawing 对 `GetActiveAmount(...)` / `GetHoverAmount(...)` 的使用。
- 如果按钮状态跳变，检查 `src\ui\FrameScopeButtons.cs` 中 `FrameScopeRoundedButton.OnBackColorChanged(...)`、`OnEnabledChanged(...)` 和 `FrameScopeNavButton.AnimateAmount(...)`。
- 如果状态 pill 闪色或 timer 堆叠，检查 `src\app\FrameScopeNativeMonitor.UiStatusDisplay.cs` 的 `statusPulseMotion` Dispose 流程。
- 如果进程选择器没有反馈，检查 `src\app\FrameScopeNativeMonitor.UiProcessPicker.cs` 的 `OpenProcessPickerDialog()`。
- 如果报告页按钮被怀疑是假按钮，检查 `src\ui\FrameScopeReportPage.Actions.cs` 中 `BindReportDetailActionButtons(...)` 与 `OpenSelectedDetailedReport(...)`。
- 如果 `node` 测试在本机报 Access denied，优先确认是否命中 WindowsApps `node.exe`；可临时将 `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin` 放到 PATH 前面后重跑同一脚本。