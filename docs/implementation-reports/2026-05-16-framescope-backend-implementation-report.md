# 2026-05-16 FrameScope 后端实现报告

## 修改目标

本轮目标是补齐当前 UI 按钮、状态卡、报告入口和底部报告生成卡背后的真实后端支撑。执行范围限定在后端/core/report/status seam，不重新设计 UI，不修改视觉/layout 文件，不接回 GameLite/WMI/SGuard。

本轮实际确认的后端缺口是：底部“报告生成”卡原来由 `UiStatusRefresh.LatestReportProgress()` 直接扫描 DataRoot 下最新写入的 `status.json`，只要包含 `ReportProgressPercent` 就展示。这个逻辑可能把被重新触碰过的旧 run、过期完成状态或无关 run 的报告进度显示成当前进度。

## 已读取资料

本轮读取并参考了以下文件：

- `AGENTS.md`
- `docs\orchestration\FrameScopeMonitor-BackendPrompt-Role.md`
- `docs\orchestration\FrameScopeMonitor-BackendPrompt-Worklog.md`
- `docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md`
- `docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md`
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\modules\backend-monitoring.md`
- `docs\modules\ui-interactions.md`
- `docs\modules\software-ui.md`
- `docs\implementation-reports\2026-05-16-framescope-ui-design-implementation-report.md`
- `docs\implementation-reports\2026-05-16-framescope-ui-interaction-implementation-report.md`
- `docs\FrameScopeMonitor-progress.md`，只读参考，未修改
- `docs\FrameScopeMonitor-next-prompt.md`，只读参考，未修改
- `docs\superpowers\plans\2026-05-16-framescope-backend-ui-support-prompt-plan.md`

本轮实际查看了 UI/后端代码，包括 `PageOverview`、`PageSettings`、`PageTargets*`、`FrameScopeReportPage*`、`UiHelpers`、`UiStatusRefresh`、`UiConfigActions`、`UiWatcherControls`、`UiDiagnosticActions`、watcher/session/report/status/config/progress/diagnostics/report generator 和相关测试。

## UI 与后端能力对应关系

- 概览“启动监测”：`StartWatcher()` 保存真实配置后启动 `FrameScopeMonitor.exe --watcher`；watcher 写 `framescope-watcher-state.json`、`framescope-watcher.log` 和 run 目录状态。
- 概览/目标/报告/设置“打开输出/报告目录”：`OpenDataRoot()` 或选中报告目录打开真实 DataRoot/run dir；DataRoot 不存在时创建目录，打开失败写状态栏错误。
- 顶部“监测器/已启用目标/软件状态”：读取 watcher pid/state、配置目标数量、状态栏错误和日志输出。
- 概览“捕获链状态/最近捕获/最近报告/输出目录/诊断模式”：来自 `framescope-config.json`、watcher state、history、run status/summary/manifest 和 DataRoot 存在性。
- 目标表格：来自 `framescope-config.json`；`FrameScopeConfigStore` 保留别名、采样间隔、auto-open、慢采样和隐藏诊断字段。
- 目标“刷新进程/添加进程/保存配置/启动/停止”：真实进程枚举、配置写入、watcher/session 进程生命周期。
- 设置“采样间隔/自动打开报告/详细日志/自动诊断/性能诊断/保留天数/最大 MB/数据目录”：由 `FrameScopeConfigStore` 和 diagnostics retention/log 清理逻辑支撑。
- 报告中心“最近报告状态/已生成报告/打开 HTML/详细报告/重新生成”：来自 history、选中 run dir、ReportHtml、status.json、diagnostics、report generator。
- 底部“报告生成”卡：本轮改为通过 `FrameScopeReportProgress.FindLatestEffectiveStatus()` 读取最新有效 report progress/status，按 `ReportProgressUpdatedAt` 和新鲜度过滤，避免显示旧 run 的陈旧进度。

## 修改文件

- `src\core\FrameScopeReportProgress.cs`
  - 新增 `FindLatestEffectiveStatus(string dataRoot)` 和可测试 overload `FindLatestEffectiveStatus(string dataRoot, DateTime now)`。
  - 扫描 DataRoot 下的 run `status.json`，只接受包含 `ReportProgressPercent` 的真实报告状态。
  - 优先使用 `ReportProgressUpdatedAt` 判断进度更新时间；缺失时才回退文件写入时间。
  - 活动进度保留 10 分钟窗口，完成/失败/可重试状态保留 30 分钟窗口，过期则返回空结果。
  - 返回字典中附加 `ReportProgressStatusPath` 和 `ReportProgressRunDir`，只用于 UI 读取，不写入 status/manifest。
- `src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs`
  - 最小联动修改 `LatestReportProgress()`，改为调用 core seam。
  - 未修改视觉、布局、按钮样式或页面结构。
- `tests\FrameScopeReportProgressTests.cs`
  - 新增 TDD 回归测试 `FindsFreshProgressInsteadOfTouchedStaleStatus()`。
  - 测试证明旧 run 的 `status.json` 即使文件写入时间更新，只要 `ReportProgressUpdatedAt` 已过期，也不会覆盖新鲜活动 run。

## 未修改文件和边界

- 未修改 UI 视觉/layout 文件：`FrameScopeUiTheme.cs`、`FrameScopeRoundedDrawing.cs`、`FrameScopePanels.cs`、`FrameScopeButtons.cs`、`FrameScopeStatusControls.cs`、`FrameScopeReferenceSidebar*.cs`、`FrameScopeNativeMonitor.UiVisual*.cs`、`FrameScopeNativeMonitor.PageLive.Layout.cs`、`FrameScopeNativeMonitor.PageOverview.cs`、`FrameScopeNativeMonitor.PageSettings.cs`、`FrameScopeNativeMonitor.PageTargets.Layout.cs`、`FrameScopeNativeMonitor.PageTargets.Actions.cs`、`FrameScopeReportPage.Layout.cs`。
- 未修改 `docs\FrameScopeMonitor-progress.md` 或 `docs\FrameScopeMonitor-next-prompt.md`。
- 未修改 `scripts\lightweight\`、根目录 GameLite wrapper、WMI trigger、SGuard 策略、`packaging\`。
- 未修改 CSV schema、HTML template、data.js 或 manifest schema。
- 未修改 `build.ps1`；本轮未新增/删除 C# 文件，不需要 build 脚本集成。

## 测试和验证结果

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`：先按 TDD 红灯失败，报 `FrameScopeReportProgress` 缺少 `FindLatestEffectiveStatus`；实现后通过。
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`：通过，生成 `dist\FrameScopeMonitor-Setup.exe`。
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`：通过，`FrameScope tests rebuilt.`
- `.\tests\FrameScopeConfigStoreTests.exe`：PASS。
- `.\tests\FrameScopeCapturePlannerTests.exe`：PASS。
- `.\tests\FrameScopeReportProgressTests.exe`：PASS。
- `.\tests\FrameScopeDiagnosticsTests.exe`：PASS。
- `.\tests\FrameScopePubgSimulatorTests.exe`：PASS。
- `.\tests\FrameScopeUiStateTests.exe`：PASS。
- `node .\tests\chart-sampling-tests.js`：首次命中 WindowsApps `Access is denied`；按提示把 `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin` 放到 `PATH` 前面后重跑，通过，`chart-sampling-tests: PASS`。
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`：通过，0 warnings，0 errors。
- `"C:\Program Files\Git\cmd\git.exe" diff --check`：退出码 0；仅输出既有 tracked 文件 CRLF 提示：`README.md`、`build.ps1`、`framescope-config.example.json`。

## simulator 和报告生成结果

执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario stable -DurationSeconds 4
```

结果：

- `outputRoot`: `artifacts\pubg-simulator\20260516-221024-390-stable`
- `runDir`: `artifacts\pubg-simulator\20260516-221024-390-stable\runs\SyntheticPUBG-20260516-221024`
- `monitorExit`: 0
- `reportExit`: 0
- `phase`: `done`
- `presentMonCaptureMode`: `process_name`
- `presentMonCaptureTarget`: `TslGame.exe;TslGame-Win64-Shipping.exe`
- `presentMonCsvRows`: 240
- `frameCaptureStatus`: `captured`
- `hasFrameData`: true
- `frames`: 240
- `reportKind`: `full`
- `usedInitialPid`: true

报告产物检查：

- `presentmon.csv` 存在，22179 bytes。
- `process-samples.csv` 存在，534265 bytes。
- `system-samples.csv` 存在，585 bytes。
- `summary.json` 存在，4721 bytes。
- `status.json` 存在，5936 bytes。
- `charts\framescope-interactive-report.html` 存在，40277 bytes。
- `charts\framescope-interactive-data.js` 存在，57617 bytes。
- `charts\framescope-interactive-manifest.json` 存在，1137 bytes。

manifest 用 UTF-8 和 Node `JSON.parse` 验证有效：

- `hasFrameData`: true
- `reportKind`: `full`
- `frames`: 240
- `processSamples`: 59
- `systemSamples`: 2
- `frameCaptureStatus`: `captured`

HTML 关键结构检查：

- chart canvas：存在。
- gauges：存在。
- process rows：存在。
- summary rows：存在。
- data include `framescope-interactive-data.js`：存在。
- chart sampling script：存在。

Edge headless 打开报告并截图：

- 截图路径：`artifacts\backend-report-headless-20260516-221024.png`
- 截图大小：496560 bytes，非空。

说明：第一次用 PowerShell 默认编码读取 manifest 时 `ConvertFrom-Json` 失败；改用 `Get-Content -Encoding UTF8` 后通过。manifest 本身有效，失败原因是 Windows PowerShell 默认编码误读中文内容。

## 残留进程检查

验证后执行 FrameScope 相关残留进程检查，未发现以下进程残留：

- `FrameScopeMonitor`
- `FrameScopeReportGenerator`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `PresentMon`
- `FakePresentMon`
- `TslGame`
- `GameLite`

Edge headless 截图后未发现带本轮截图路径、`--headless` 或本轮 report HTML 的 `msedge.exe` 命令行残留。系统中存在其他 Edge/WebView 进程，不属于本轮 FrameScope 残留范围。

## 给测试员和 bug 修复对话框的定位提示

- 如果底部“报告生成”卡再次显示旧 run 进度，优先检查 `src\core\FrameScopeReportProgress.cs::FindLatestEffectiveStatus()`。
- 构造复现时，在同一个 DataRoot 下放两个 run：旧 run 的 `status.json` 文件写入时间较新，但 `ReportProgressUpdatedAt` 早于 30 分钟；新 run 的 `ReportProgressUpdatedAt` 在 10 分钟内。UI 应选择新 run 或在只有旧 run 时显示空闲。
- 如果报告中心条目存在但 HTML 缺失，优先查 `src\ui\FrameScopeReportPage.Actions.cs` 的按钮可用性和 `FrameScopeReportActionRules`，不要改视觉文件。
- 如果 simulator 报 `hasFrameData=false`，不要把空报告当 full 成功；先查 `presentmon.csv`、`status.json` 的 `FrameCaptureStatus`、manifest 的 `reportKind`。

## 未覆盖项和真实 PUBG 手动验证步骤

真实 PUBG 没有在本机运行验证。需要手动验证：

1. 打开 FrameScope Monitor，确认 PUBG/TslGame 目标启用。
2. 启动监测。
3. 启动 PUBG 并进入一段真实渲染场景。
4. 退出 PUBG。
5. 确认 run 目录生成 `presentmon.csv`、`process-samples.csv`、`system-samples.csv`、`summary.json`、`status.json`。
6. 确认 `reportExit=0`、`hasFrameData=true`、`reportKind=full`。
7. 打开 HTML 报告，检查 FPS、1% Low、0.1% Low、process rows、system charts。
8. 确认退出后没有 `FrameScopeMonitor --monitor-session`、`FrameScopeProcessSampler`、`FrameScopeSystemSampler`、`PresentMon` 残留。

## 自我检查

- 只改了允许文件：是。
- 触碰 UI 视觉或 layout：否。
- 触碰 GameLite/WMI/SGuard：否。
- 改 CSV/JSON/manifest/status 字段语义：否。仅新增内存返回字段 `ReportProgressStatusPath`、`ReportProgressRunDir` 给 UI 读取，不写入持久文件。
- 删除或缩水原始性能数据：否。
- 破坏 status/summary/manifest：验证未发现。
- 残留进程风险：FrameScope/PresentMon/sampler/FakePresentMon/TslGame/GameLite 未残留。
- 是否需要 `build.ps1` 集成：否，本轮未新增 C# 文件。
