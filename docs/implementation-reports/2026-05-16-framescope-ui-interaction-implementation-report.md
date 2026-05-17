# 2026-05-16 FrameScope UI Interaction Implementation Report

## 修改目标

按 `2026-05-16-framescope-ui-interaction-reference-prompt-plan.md` 的下游提示词 v2 实现 UI 交互 seam：连接真实 handler、补齐状态反馈、防止长操作重复点击、保持 live 页进入启动刷新和离开停止刷新，不改后端采样、report generator 数据结构、GameLite/WMI/SGuard。

边界说明：核心 UI 交互源码只改允许的交互文件；另有测试文件和实现报告作为 TDD/交付支撑产物。

## 用户路径

- 左侧导航：概览、监控目标、实时、报告、设置、关于我们页面切换。
- 概览快捷操作：启动监测、打开输出目录。
- 监控目标：编辑表格、刷新进程、添加进程、保存配置、启动/停止监测。
- 设置：修改采样间隔、自动打开报告、详细日志、自动诊断、性能诊断、日志保留和数据目录后保存。
- 实时页：进入后刷新实时数据，离开后停止刷新；日志支持暂停、继续、清空面板。
- 报告页：打开最近报告、打开输出目录、生成诊断报告、打开历史、选择报告后打开目录/HTML、导出支持包、重新生成、刷新列表。
- 关于我们页：UI 设计侧完成视觉修复后，页面可正常生成截图；`PageAbout.cs` / `FrameScopeStatusControls.cs` 属于 UI 设计侧改动，不计入本 UI 交互线程。

## 修改了哪些交互

- 报告动作可用性规则抽到 `FrameScopeReportActionRules`，选中报告、HTML 缺失、运行目录缺失时按钮状态和禁用原因可测试。
- 报告页按钮统一保持真实 handler：打开报告、打开目录、支持包导出、重新生成、刷新均未替换为假动作。
- 支持包导出、报告重新生成、诊断报告生成加入 in-flight 标记，重复点击会给出“后台执行中”状态。
- 诊断报告生成、支持包导出、报告重新生成会在开始、成功、失败时更新状态栏。
- 启动/停止 watcher 加入 in-flight 标记，避免连续点击导致状态混乱。
- 保存配置和恢复默认配置加入开始/成功/失败状态；保存后重新读取并刷新当前 settings/targets 页面摘要。
- 实时日志暂停/继续/清空提供状态反馈，清空只清 UI 面板，不删除持久化日志。
- `OpenDataRoot`、报告历史和选中报告目录打开失败时写入状态栏。
- `UiRouting.ShowPage()` 增加页面构建失败 fallback：如果某页构建异常，路由不会直接崩溃，会显示页面加载失败状态并写入 FrameScope log。

## 修改文件清单

本 UI 交互线程的修改清单以本节为准。当前 git 工作树存在既有/并行变更，不能把整个 `git status` 直接等同于本线程改动范围。

### 核心 UI 交互源码

- `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- `src\app\FrameScopeNativeMonitor.UiConfigActions.cs`
- `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`
- `src\app\FrameScopeNativeMonitor.UiDiagnosticActions.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Log.cs`
- `src\ui\FrameScopeReportPage.Actions.cs`
- `src\ui\FrameScopeReportPage.Detail.cs`
- `src\ui\FrameScopeUiState.cs`

### TDD / 交付支撑产物

- `tests\FrameScopeUiStateTests.cs`
- `docs\implementation-reports\2026-05-16-framescope-ui-interaction-implementation-report.md`

### 不计入本 UI 交互线程的并行 UI 设计侧改动

- `src\app\FrameScopeNativeMonitor.PageAbout.cs`
- `src\ui\FrameScopeStatusControls.cs`

## 每个按钮/状态对应的真实 handler

- 报告页“打开最近报告”：`OpenLatestReport()` -> `TryOpenPath(reportPath)`。
- 报告页“打开输出目录”：`OpenDataRoot()` -> `ResolveCurrentDataRoot()` + `Process.Start`。
- 报告页“生成诊断报告”：`GenerateDiagnosticReportFromUi(Button)` -> `FrameScopeDiagnostics.GenerateReport(...)`。
- 报告页“打开历史记录”：`OpenHistory()` -> `HistoryPath` + `Process.Start`。
- 报告详情“打开报告目录”：`OpenSelectedReportFolder()` -> 选中报告目录/运行目录/当前数据目录。
- 报告详情“打开 HTML 报告”：`OpenSelectedReport()` -> `TryOpenPath(entry.ReportHtml)`。
- 报告详情“导出支持包”：`GenerateSelectedDiagnosticReport(Button)` -> `FrameScopeDiagnostics.GenerateReport(...)`。
- 报告详情“重新生成”：`RegenerateSelectedReport(Button)` -> `RunReportGeneration(entry.RunDir)` + `UpdateStatusAfterReportGeneration(...)`。
- 报告详情“刷新列表”：`ShowPage("reports")`。
- 概览/目标页“启动监测”：`StartWatcher()` -> 保存真实配置并启动 `Application.ExecutablePath --watcher --config ...`。
- 目标页“停止监测”：`StopWatcher()` -> `StopFrameScopeBackgroundProcesses()`。
- 设置/目标页“保存配置”：`SaveConfigFromGrid()` -> `ReadGridConfig()` + `SaveConfig(...)` + `LoadConfig()`。
- 设置页“恢复默认”：`ResetConfigToDefaultsFromUi()` -> `FrameScopeConfigStore.CreateDefaultConfig()` + `SaveConfig(...)`。
- 实时日志“暂停/继续”：切换 `liveLogPaused`，只影响当前 UI 显示刷新。
- 实时日志“清空”：清空 `liveLogDisplayLines` 和当前 label，不删除磁盘日志。
- 页面导航按钮：`NavButton(...).Click` -> `ShowPage(key)`，并由 `ShowPage()` 维护 active nav、live timer、页面构建和错误反馈。

## 没有修改哪些禁止文件

本交互实现线程没有修改视觉主题、纯 layout、后端采样、report generator、GameLite/WMI/SGuard、`scripts\lightweight`、`packaging`、`build.ps1`。特别是没有修改以下禁止文件：`src\ui\FrameScopeUiTheme.cs`、`src\ui\FrameScopeButtons.cs`、`src\ui\FrameScopePanels.cs`、`src\app\FrameScopeNativeMonitor.PageOverview.cs`、`src\app\FrameScopeNativeMonitor.PageSettings.cs`、`src\app\FrameScopeNativeMonitor.PageLive.Layout.cs`、`src\ui\FrameScopeReportPage.Layout.cs`、`src\app\FrameScopeNativeMonitor.Watcher.cs`、`src\app\FrameScopeNativeMonitor.MonitorSession*.cs`、`src\app\FrameScopeNativeMonitor.ReportOpen*.cs`、`src\reporting\*`。

说明：当前工作树中 `src\app\FrameScopeNativeMonitor.PageAbout.cs` 和 `src\ui\FrameScopeStatusControls.cs` 已由 UI 设计侧调整，本交互线程没有编辑这些视觉/layout 文件；它们不计入本 UI 交互线程修改清单。

## 测试结果

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`：PASS。
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`：PASS。
- `.\tests\FrameScopeUiStateTests.exe`：PASS。
- `.\tests\FrameScopeReportProgressTests.exe`：PASS。
- `node .\tests\chart-sampling-tests.js`：PASS。
- `"C:\Program Files\Git\cmd\git.exe" diff --check`：PASS，只有既有 LF/CRLF warning。
- report buttons wiring：PASS，静态检查确认仍绑定真实 handler。
- live timer：PASS，`ShowPage()` 非 live 调用 `StopLiveRefresh()`，live 页构建成功后调用 `StartLiveRefresh()`，`RefreshLivePage()` 离开 live 时停止 timer。
- about 截图错误日志复查：`NO_ABOUT_SCREENSHOT_ERROR`。
- 残留进程检查：`NO_RESIDUAL_PROCESSES`。

## 截图验证路径

- overview：`artifacts\ui-interaction-20260516-overview.png`
- targets：`artifacts\ui-interaction-20260516-targets.png`
- settings：`artifacts\ui-interaction-20260516-settings.png`
- live：`artifacts\ui-interaction-20260516-live.png`
- reports：`artifacts\ui-interaction-20260516-reports.png`
- about：`artifacts\ui-interaction-20260516-about.png`

## 给 bug 修复对话框的定位提示

此前 about 页截图失败的错误为 `System.ArgumentException: 控件不支持透明的背景色。`，定位在 `src\app\FrameScopeNativeMonitor.PageAbout.cs` 的 `AboutLogoBlock()` 和 `src\ui\FrameScopeStatusControls.cs` 的 `FrameScopeSidebarLogo` 透明背景支持。UI 设计侧完成后，本轮复查 about 截图已通过；若后续复现，优先检查这两个视觉/layout 文件，不要在 UI 交互文件里绕过真实页面。

## 完成审计

| 要求 | 证据 | 结论 |
| --- | --- | --- |
| 核心 UI 交互源码只改允许的交互文件 | 本交互实现文件在允许的 `UiRouting`、`UiConfigActions`、`UiWatcherControls`、`UiDiagnosticActions`、`PageLive.Log`、`FrameScopeReportPage.Actions`、`FrameScopeReportPage.Detail`、`FrameScopeUiState` 内；测试和报告为 TDD/交付支撑产物 | PASS |
| 所有按钮连接真实 handler | `FrameScopeReportPage.Layout.cs` 调用 `BindReportActionsCardButtons` / `BindReportDetailActionButtons`，动作实现仍调用真实打开、导出、重新生成、刷新逻辑 | PASS |
| live 页进入启动刷新、离开停止刷新 | `UiRouting.ShowPage()` 非 live 调用 `StopLiveRefresh()`，live 页面构建成功后调用 `StartLiveRefresh()`；`RefreshLivePage()` 离开 live 时会停止 timer | PASS |
| 设置保存反馈 | `SaveConfigFromGrid()` 有正在保存、保存成功、保存失败状态，并重新读取配置 | PASS |
| 开始/停止监测防重复点击 | `UiWatcherControls.cs` 使用 `watcherActionInFlight` 包住 Start/Stop | PASS |
| 日志暂停/清空状态反馈 | `PageLive.Log.cs` 暂停、继续、清空均调用 `SetStatus()` | PASS |
| 报告按钮仍可打开报告、目录、重新生成、导出支持包、刷新 | `FrameScopeReportPage.Actions.cs` 对应 `OpenLatestReport`、`OpenDataRoot`、`OpenSelectedReportFolder`、`OpenSelectedReport`、`GenerateSelectedDiagnosticReport`、`RegenerateSelectedReport`、`ShowPage("reports")` | PASS |
| 构建和测试 | build、测试构建、UI 状态测试、report progress 测试、chart sampling、diff check 均已运行 | PASS |
| 截图 | overview、targets、settings、live、reports、about 均生成 PNG，具体路径见“截图验证路径” | PASS |
| 残留进程 | `NO_RESIDUAL_PROCESSES` | PASS |
| 禁止文件不修改 | 本交互线程没有修改视觉主题、后端采样、report generator、GameLite/WMI/SGuard、`scripts\lightweight`、`packaging` | PASS |
| 并行变更说明 | 当前 git 工作树存在既有/并行变更，本线程修改清单以本报告列出的交互源码、测试、实现报告为准；`PageAbout.cs` / `FrameScopeStatusControls.cs` 属于 UI 设计侧改动 | PASS |

审计结论：当前 UI 交互目标已达成。UI 设计侧完成 about 修复后，六页截图、构建、测试、按钮 wiring、live timer 和残留进程验证均通过。
