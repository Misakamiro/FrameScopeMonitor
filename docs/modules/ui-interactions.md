# UI 交互设计模块说明

## 这个板块负责什么

UI 交互模块负责用户操作如何影响软件状态。它覆盖页面切换、按钮点击、设置保存、恢复默认、表格双击编辑、进程选择、实时日志暂停/清空、实时监控页进入/离开、图表刷新和图表清空。

这个模块不负责底层采样器如何采样，也不负责 HTML 报告如何绘制。它负责把用户动作转换成对配置、监听器、报告生成器和实时 UI 刷新的调用。

## 对应文件

- `src\app\FrameScopeNativeMonitor.cs`
  - `BuildUi`
  - `ShowPage`
  - `StartLiveRefresh`
  - `StopLiveRefresh`
  - `RefreshLivePage`
  - 目标表格事件。
  - 设置保存/恢复默认/选择目录事件。
  - 启动/停止 watcher 事件。
  - 报告生成和打开目录按钮事件。
- `src\ui\FrameScopeUiState.cs`
  - `FrameScopeLiveRuntime`
  - `FrameScopeTargetEditRules`
  - 可测试交互规则。
- `src\ui\FrameScopeLiveData.cs`
  - 实时页数据读取。
- `src\ui\FrameScopeReportPage.cs`
  - 报告页交互。
- `src\core\FrameScopeConfigStore.cs`
  - 设置保存和目标配置合并。

## 页面切换逻辑

页面切换由 `ShowPage` 管理。关键规则：

- 切到实时监控页时启动实时 UI 刷新。
- 离开实时监控页时停止实时 UI 刷新。
- 切页时重建当前页面内容，避免旧控件状态残留。
- 不能创建重复实时刷新定时器。

## 按钮事件

按钮必须接真实逻辑：

- 启动监测：启动 watcher。
- 停止监测：停止 watcher 和相关状态刷新。
- 打开输出目录：打开真实数据目录。
- 保存设置：写入 `framescope-config.json` 并立即刷新配置。
- 恢复默认：通过 `FrameScopeConfigStore.CreateDefaultConfig()` 恢复真实默认配置。
- 选择目录：打开目录选择并写回输入框。
- 刷新进程：读取当前系统进程列表。
- 添加进程：把用户输入或选择的进程加入配置；watcher 正在运行时先停止，不自动恢复。
- 暂停日志：只暂停实时日志 UI 追加。
- 清空日志：只清空 UI 面板，不删除持久化日志文件。

## 表格编辑

监控目标表格从真实配置读取。维护规则：

- 启用复选框要写回目标配置。
- 自动打开报告复选框要写回目标配置。
- 游戏名、进程名、采样率支持编辑。
- 采样率必须是数字，并满足最小采样间隔约束。
- 保存配置后通过配置存储模块统一规范化。

## 实时监控进入/离开

实时监控必须遵守：

- 软件启动后不刷新图表。
- 进入实时页后才启动 UI 刷新。
- 目标进程必须来自已启用配置目标。
- 找不到目标时清空 FPS 图、帧时间图、当前 FPS、当前进程和 CPU/GPU 状态。
- 目标退出时立即清空旧曲线，不显示上一局游戏残留。
- 离开实时页后停止 UI 定时器，不影响后台监测器。

测试入口：

- `tests\FrameScopeUiStateTests.cs`
- `tests\Build-FrameScopeTests.ps1`

## 最近整理了哪些内容

- UI 交互相关源文件移动到 `src\app\` 和 `src\ui\`。
- 可测试交互规则保留在 `src\ui\FrameScopeUiState.cs`。
- 构建脚本和测试重编译脚本已改为新路径。

## 后续修改交互逻辑应该看哪些文件

- 页面切换、按钮、WinForms 事件：`src\app\FrameScopeNativeMonitor.cs`
- 可测试规则：`src\ui\FrameScopeUiState.cs`
- 配置写入：`src\core\FrameScopeConfigStore.cs`
- 实时页数据读取：`src\ui\FrameScopeLiveData.cs`

维护原则：

- 交互必须接真实逻辑。
- 不要把 UI 清空等同于删除持久化文件。
- 修改实时监控生命周期时必须跑 `FrameScopeUiStateTests.exe`。
## Stage 21 partial split ownership

Stage 21 moved UI interaction logic out of the single large `src\app\FrameScopeNativeMonitor.cs` file.

Primary UI interaction files:

- `src\app\FrameScopeNativeMonitor.UiShell.cs`: `ShowPage`, page routing, page reset, shell startup/shutdown hooks, status timer setup, and screenshot entry points.
- `src\app\FrameScopeNativeMonitor.UiInteractions.cs`: real button/action handlers, watcher start/stop UI flow, config save/reset, process refresh/add, directory open/browse, report progress UI refresh, diagnostic report UI action, and safe status updates from background threads.
- `src\app\FrameScopeNativeMonitor.PageLive.cs`: live-page enter/leave refresh lifecycle, live refresh timer, pause/clear log UI handlers.
- `src\app\FrameScopeNativeMonitor.PageTargets.cs`: target table event binding, process picker controls, target action row bindings.
- `src\app\FrameScopeNativeMonitor.PageSettings.cs`: settings page controls and save/reset/browse bindings.
- `src\ui\FrameScopeUiState.cs`: testable interaction rules such as live refresh state and target edit validation.
- `src\ui\FrameScopeLiveData.cs`: live page data loading from latest run files.
- `src\ui\FrameScopeReportPage.cs`: report page actions such as open latest, open selected, regenerate, history, and support bundle generation.

Do not put PresentMon capture details, sampler implementation, monitor-session loops, report data parsing, or report HTML rendering into UI interaction files. Those belong in backend monitoring or reporting files.

Parallel editing rule:

- UI interaction conversations can edit the files listed above.
- `src\app\FrameScopeNativeMonitor.UiShell.cs` should be exclusive when changing page-routing semantics because every page uses it.
- `src\app\FrameScopeNativeMonitor.UiInteractions.cs` should be exclusive when changing watcher start/stop, config save, or process kill behavior.
- `src\app\FrameScopeNativeMonitor.cs` should stay as the app entry/shared helper file and should be edited only when startup arguments or shared app constants change.

## Stage 25-26 refined UI interaction ownership

`FrameScopeNativeMonitor.UiShell.cs` and `FrameScopeNativeMonitor.UiInteractions.cs` are no longer the main edit targets for every interaction change. Use these focused files instead:

- `src\app\FrameScopeNativeMonitor.UiFields.cs`: shared WinForms field references used across page builders and handlers.
- `src\app\FrameScopeNativeMonitor.UiRouting.cs`: `ShowPage`, page reset, nav-button creation, and active nav state.
- `src\app\FrameScopeNativeMonitor.UiHelpers.cs`: small UI helper readers such as history entries, byte formatting, current data root, enabled target count, and watcher quiet check.
- `src\app\FrameScopeNativeMonitor.UiConfigActions.cs`: grid-to-config read, save config, reset defaults, and browse data root.
- `src\app\FrameScopeNativeMonitor.UiProcessPicker.cs`: refresh process list, add selected process, and process picker text resolution.
- `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`: watcher running check, start watcher, and stop watcher.
- `src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs`: FrameScope background process enumeration, tree cleanup, and process kill helpers.
- `src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs`: watcher status refresh, report progress UI refresh, progress message localization, and latest progress lookup.
- `src\app\FrameScopeNativeMonitor.UiDiagnosticActions.cs`: open data root, generate diagnostic report from UI, open diagnostic folder, and thread-safe status update.
- `src\app\FrameScopeNativeMonitor.PageLive.cs`: live page enter/leave refresh lifecycle, live refresh timer, pause/clear log handlers.
- `src\app\FrameScopeNativeMonitor.PageTargets.cs`: target grid event binding and target action row bindings.
- `src\app\FrameScopeNativeMonitor.PageSettings.cs`: settings page save/reset/browse bindings.

Parallel UI interaction rule:

- Page routing changes should be exclusive in `UiRouting.cs`.
- Watcher start/stop changes should be exclusive in `UiWatcherControls.cs`.
- Config save/reset changes should be exclusive in `UiConfigActions.cs`.
- Process cleanup changes should be exclusive in `UiProcessCleanup.cs` because it can kill real background monitor processes.
- `FrameScopeNativeMonitor.UiInteractions.cs` is now a small placeholder and should not receive new feature logic.

## Stage 29-32 final UI interaction ownership

Report and target page interaction surfaces were split from their layout files:

- `src\ui\FrameScopeReportPage.Actions.cs`: open latest report, open history file, open selected report, open selected report folder, generate selected diagnostic report, and regenerate selected report.
- `src\ui\FrameScopeReportPage.Detail.cs`: selected report detail refresh and latest report lookup. Edit here for detail text/status lookup behavior.
- `src\ui\FrameScopeReportPage.Layout.cs`: report list selection wiring and report detail/action control binding. Edit here only when changing report page control wiring/layout together.
- `src\app\FrameScopeNativeMonitor.PageTargets.Grid.cs`: target table validation, double-click edit start, dirty checkbox commit, process-name normalization, and checkbox cell painting.
- `src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`: refresh-process, add-process, save-config, start-watcher, and stop-watcher button bindings on the target page.
- `src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs`: target list and target settings visual layout.

Parallel UI interaction rule:

- Report action behavior should be exclusive in `FrameScopeReportPage.Actions.cs` because it can start background diagnostics or report generation.
- Target grid editing behavior should be exclusive in `PageTargets.Grid.cs` and coordinated with `FrameScopeUiState.cs` tests.
- Target action-row button behavior should be exclusive in `PageTargets.Actions.cs`; do not duplicate watcher start/stop logic there.
- Keep watcher lifecycle logic in `UiWatcherControls.cs` and report generation orchestration in backend report files; page action files should only call those existing helpers.

## Stage 33 residual UI interaction cleanup

The report and live page interaction seams are now more focused:

- `src\ui\FrameScopeReportPage.Actions.cs`: owns report button bindings for latest report, data root, diagnostic report, history, selected report folder, selected report open, support bundle export, selected report regeneration, and report page refresh.
- `src\ui\FrameScopeReportPage.Layout.cs`: still creates buttons and passes them to action binding helpers, but no longer owns direct `Click +=` actions.
- `src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs`: owns `StartLiveRefresh`, `StopLiveRefresh`, `RefreshLivePage`, the WinForms timer, and page rebuild lifecycle when the live page is active.
- `src\app\FrameScopeNativeMonitor.PageLive.Log.cs`: owns pause/continue and clear-log UI behavior. Clearing the UI log panel must not delete persistent log files.
- `src\app\FrameScopeNativeMonitor.PageLive.Layout.cs`: owns only the live page visual structure and should not receive timer or log action changes.

Parallel UI interaction rule:

- Report action changes should be exclusive in `FrameScopeReportPage.Actions.cs`.
- Live refresh lifecycle changes should be exclusive in `PageLive.Lifecycle.cs`.
- Live log pause/clear behavior should be exclusive in `PageLive.Log.cs`.
- Report page layout-only work can proceed in `FrameScopeReportPage.Layout.cs` if no button behavior is changed.
