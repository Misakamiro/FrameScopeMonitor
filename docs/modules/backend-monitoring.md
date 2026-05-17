# 后端监测模块说明

## 这个板块负责什么

后端监测模块负责 FrameScope Monitor 的核心数据链路：目标进程识别、PUBG/TslGame 别名、PresentMon 帧时间采集、进程采样、系统采样、监测器启动/停止、报告生成、日志、诊断和数据存储。

它不负责 UI 颜色和控件绘制。UI 只展示这些模块产生的状态和数据。

## 对应目录和文件

- `src\app\FrameScopeNativeMonitor.cs`
  - watcher 循环。
  - `--monitor-session` 单次监测会话。
  - 启动 PresentMon、进程采样器、系统采样器。
  - 游戏退出后的停止、状态写入、报告生成编排。
- `src\core\FrameScopeCapturePlanner.cs`
  - 目标进程别名。
  - PUBG/TslGame 特殊识别。
  - PresentMon 捕获模式和参数规划。
  - 目标未找到诊断。
- `src\core\FrameScopeConfigStore.cs`
  - 配置加载、保存、默认目标、采样间隔规范化。
- `src\monitoring\FrameScopeProcessSampler.cs`
  - 进程 CPU、内存、IO 采样。
  - 输出 `process-samples.csv`、`topcpu-samples.csv`、`topio-samples.csv`、`sample-alerts.csv`。
- `src\monitoring\FrameScopeSystemSampler.cs`
  - CPU/GPU/内存/磁盘/网络/GPU 频率/显存/功耗采样。
  - 输出 `system-samples.csv`。
- `src\reporting\FrameScopeReportGenerator.cs`
  - 原生 HTML 报告生成。
  - 生成报告 HTML、数据 JS、manifest。
- `src\core\FrameScopeReportProgress.cs`
  - 报告进度 JSON 读写。
- `src\diagnostics\FrameScopeDiagnostics.cs`
  - 诊断报告、日志清理、隐私脱敏、状态汇总。

## 进程识别

目标进程来自 `framescope-config.json` 的 `Targets`。重要规则：

- 只有 `Enabled=true` 的目标参与监听。
- 进程名可以包含 `.exe`，内部会规范化为 base name。
- PUBG 目标必须包含 `TslGame` 和 `TslGame-Win64-Shipping` 别名。
- 不允许实时监控页随便找一个系统进程当目标。

## FPS 和帧时间采集

帧时间由 PresentMon 负责写出 `presentmon.csv`。FrameScope 只负责：

- 规划 PresentMon 参数。
- 启动 PresentMon。
- 游戏退出后请求停止 PresentMon。
- 读取 CSV 和日志判断是否有帧数据。

不要把 UI 图表刷新频率改成采样频率。实时页 1 秒刷新的是显示层，不是 PresentMon 或采样器频率。

## CPU/GPU/内存数据采集

- 进程采样器负责高频进程数据。
- 系统采样器负责较慢系统数据。
- `ProcessSampleIntervalMs` 最低按 100ms 处理。
- `SlowSampleIntervalMs` 通常为 1000ms，且不应快于主采样间隔。

## 报告生成

报告链路：

1. 监测会话写出 run 目录。
2. watcher 或 UI 调用 `FrameScopeReportGenerator.exe`。
3. 生成进度写到 `report-progress.json`。
4. 成功后写 manifest、status、history，并按配置自动打开报告。

报告必须保留完整数据。优化只允许发生在渲染层、抽样层、缓存层和交互层。

## 最近整理了哪些内容

- 后端核心源码移动到 `src\core\`、`src\monitoring\`、`src\diagnostics\`、`src\reporting\`。
- `build.ps1` 已改为从新目录编译主程序、采样器和报告生成器。
- `tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1` 已改为引用 `src\core\FrameScopeConfigStore.cs`。
- `tests\Build-FrameScopeTests.ps1` 可重新编译后端测试。

## 后续修改监测逻辑应该看哪些文件

- 改进程识别/PUBG 别名/PresentMon 参数：`src\core\FrameScopeCapturePlanner.cs`
- 改配置字段、默认目标、采样间隔规范化：`src\core\FrameScopeConfigStore.cs`
- 改 watcher 或 monitor-session 编排：`src\app\FrameScopeNativeMonitor.cs`
- 改进程采样：`src\monitoring\FrameScopeProcessSampler.cs`
- 改系统/GPU 采样：`src\monitoring\FrameScopeSystemSampler.cs`
- 改报告生成：`src\reporting\FrameScopeReportGenerator.cs`
- 改诊断报告/日志清理：`src\diagnostics\FrameScopeDiagnostics.cs`

维护原则：

- 先看真实 run 目录数据，再判断监测问题。
- 不要把缺帧空报告当成功。
- 修改后必须跑构建、相关测试和至少一次模拟器链路验证。
## Stage 21 partial split ownership

Stage 21 moved backend app orchestration out of the single large `src\app\FrameScopeNativeMonitor.cs` file.

Primary backend monitoring files:

- `src\app\FrameScopeNativeMonitor.Watcher.cs`: native watcher loop, enabled-target scanning, active monitor tracking, completed-run handling, watcher state writing, watcher logs.
- `src\app\FrameScopeNativeMonitor.MonitorSession.cs`: `--monitor-session`, target process detection, PresentMon startup/stop/session cleanup, process/system sampler startup/stop, monitor status/summary writing, capture diagnostics.
- `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs`: stale-run report recovery, `FrameScopeReportGenerator.exe` invocation, report progress/status merge, report history entries, report auto-open/marker helpers.
- `src\core\FrameScopeCapturePlanner.cs`: target aliases, PUBG/TslGame planning, PresentMon argument planning.
- `src\core\FrameScopeConfigStore.cs`: config load/save/defaults/normalization.
- `src\core\FrameScopeReportProgress.cs`: report progress JSON helpers.
- `src\monitoring\FrameScopeProcessSampler.cs`: process sampling executable.
- `src\monitoring\FrameScopeSystemSampler.cs`: system/GPU sampling executable.
- `src\diagnostics\FrameScopeDiagnostics.cs`: diagnostic reports, retention, redaction, log append.
- `src\reporting\FrameScopeReportGenerator.cs`: report-generator entry point, shared data models, `Generate` orchestration, progress, argument parsing, and latest-run lookup.
- `src\reporting\FrameScopeReportGenerator.PresentMon.cs`: PresentMon CSV reading, frame validation, render-track selection, and capture diagnostics from frame rows.
- `src\reporting\FrameScopeReportGenerator.SystemData.cs`: system-sample CSV reading, CPU/GPU/memory/disk/network series projection, and effective CPU frequency.
- `src\reporting\FrameScopeReportGenerator.ProcessData.cs`: process-sample CSV reading, per-process matrix construction, CPU/memory stats, and target-process ordering.
- `src\reporting\FrameScopeReportGenerator.Analysis.cs`: time alignment, FPS buckets, low-FPS window math, percentile/stat helpers, rounding, and shared parsing helpers.
- `src\reporting\FrameScopeReportGenerator.Metadata.cs`: run metadata, capture diagnostics, hardware WMI metadata, and simple JSON string extraction.
- `src\reporting\FrameScopeReportGenerator.Csv.cs`: streaming CSV table/parser used by report data readers.
- `src\reporting\FrameScopeReportGenerator.Html.cs`: embedded report HTML/CSS/JavaScript template.

Do not put UI colors, card drawing, page layout, or button visual styling into backend monitoring files. UI can display backend state, but capture/session/report orchestration should stay here.

Parallel editing rule:

- Backend conversations can edit `Watcher`, `MonitorSession`, core, monitoring, diagnostics, and report orchestration files.
- `src\app\FrameScopeNativeMonitor.MonitorSession.cs` should be exclusive for PresentMon/sampler lifecycle changes.
- `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs` should be exclusive for report generation/auto-open/status merge changes.
- Report data conversations can edit `FrameScopeReportGenerator.PresentMon.cs`, `SystemData.cs`, `ProcessData.cs`, `Analysis.cs`, `Metadata.cs`, and `Csv.cs` according to the data source being changed.
- Report visual/interaction conversations should edit `FrameScopeReportGenerator.Html.cs` only.
- `src\reporting\FrameScopeReportGenerator.cs` should be exclusive when changing report CLI behavior, generated JSON shape, manifest/progress writing, or the main `Generate` orchestration.
- `src\reporting\FrameScopeReportGenerator.Html.cs` should be exclusive when changing embedded report UI/CSS/JavaScript because it is a large template string and conflicts easily.
- Do not modify `scripts\lightweight\` from backend FrameScope tasks unless the user explicitly asks for GameLite work.

## Stage 24-27 refined backend ownership

Monitor-session helpers are now split out of `FrameScopeNativeMonitor.MonitorSession.cs`:

- `src\app\FrameScopeNativeMonitor.MonitorSession.cs`: `RunNativeMonitorSession` orchestration only.
- `src\app\FrameScopeNativeMonitor.MonitorSession.Models.cs`: monitor-session data containers.
- `src\app\FrameScopeNativeMonitor.MonitorSession.Paths.cs`: run paths, argument parsing/quoting, CSV row count, tail text, dictionary merge, and event CSV header helpers.
- `src\app\FrameScopeNativeMonitor.MonitorSession.Targets.cs`: target process aliases, best-process selection, process snapshots, and wait-for-target logic.
- `src\app\FrameScopeNativeMonitor.MonitorSession.Tools.cs`: PresentMon/sampler/NVIDIA SMI path resolution.
- `src\app\FrameScopeNativeMonitor.MonitorSession.PresentMon.cs`: PresentMon stop/session cleanup, logman ETW session stop, PresentMon info, and capture diagnostics.
- `src\app\FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs`: child process launch, stdout/stderr pipe copy, stop, and exit checks.
- `src\app\FrameScopeNativeMonitor.MonitorSession.Status.cs`: native monitor status and summary JSON writers.

Report orchestration is now split:

- `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs`: stale-run recovery, completed-run report generation, report generator process invocation, manifest read, and report log writing.
- `src\app\FrameScopeNativeMonitor.ReportOrchestration.Models.cs`: history entry and report generation result models.
- `src\app\FrameScopeNativeMonitor.ReportStatus.cs`: status/progress merge, latest run lookup, status value helpers, history append, and report-open decision helpers.
- `src\app\FrameScopeNativeMonitor.ReportOpen.cs`: shell/browser report opening, browser candidate discovery, open marker, and status update after opening.

Diagnostics is now split:

- `src\diagnostics\FrameScopeDiagnostics.cs`: public diagnostics entry points, report generation orchestration, async log append, and retention policy entry.
- `src\diagnostics\FrameScopeDiagnostics.Models.cs`: diagnostics result models.
- `src\diagnostics\FrameScopeDiagnostics.Sections.cs`: diagnostic report section builders.
- `src\diagnostics\FrameScopeDiagnostics.Markdown.cs`: markdown renderer.
- `src\diagnostics\FrameScopeDiagnostics.Redaction.cs`: privacy redaction.
- `src\diagnostics\FrameScopeDiagnostics.Retention.cs`: diagnostic report cleanup and log trimming.
- `src\diagnostics\FrameScopeDiagnostics.IO.cs`: JSON/path/process/file helper readers.

Parallel backend rule:

- PresentMon lifecycle and capture diagnostics should be exclusive in `MonitorSession.PresentMon.cs`.
- Target discovery should be exclusive in `MonitorSession.Targets.cs` and `src\core\FrameScopeCapturePlanner.cs`.
- Status JSON shape should be exclusive in `MonitorSession.Status.cs`, `ReportStatus.cs`, and the report generator entry files.
- Report auto-open/browser fallback should be exclusive in `ReportOpen.cs`.
- Diagnostic redaction changes should be exclusive in `FrameScopeDiagnostics.Redaction.cs`.
- Diagnostic retention changes should be exclusive in `FrameScopeDiagnostics.Retention.cs`.

## Stage 29-32 final backend ownership

The report generator entry and sampler executables were split further:

- `src\reporting\FrameScopeReportGenerator.cs`: report-generator constants, `Main`, and `Generate` orchestration.
- `src\reporting\FrameScopeReportGenerator.Models.cs`: report generator in-memory models and `Fenwick` helper.
- `src\reporting\FrameScopeReportGenerator.Cli.cs`: command-line argument parsing and latest-run discovery.
- `src\reporting\FrameScopeReportGenerator.Progress.cs`: progress JSON write wrapper.
- `src\reporting\FrameScopeReportGenerator.Diagnostics.cs`: manifest diagnostic value lookup.
- Existing report data/template files remain: `.PresentMon.cs`, `.SystemData.cs`, `.ProcessData.cs`, `.Analysis.cs`, `.Metadata.cs`, `.Csv.cs`, and `.Html.cs`.
- `src\monitoring\FrameScopeProcessSampler.cs`: process sampler entry point, sampling loop, grouped process row output, and alert row output.
- `src\monitoring\FrameScopeProcessSampler.Models.cs`: process sampler row/group models and Win32 IO counter declaration.
- `src\monitoring\FrameScopeProcessSampler.Selection.cs`: process-running checks, IO counter reads, top CPU/IO selection, and dictionary pruning.
- `src\monitoring\FrameScopeProcessSampler.IO.cs`: argument parsing, process-name normalization, CSV writer, rounding, and nullable value helpers.
- `src\monitoring\FrameScopeSystemSampler.cs`: system sampler entry point and sampling loop.
- `src\monitoring\FrameScopeSystemSampler.Models.cs`: GPU snapshot and disposable performance-counter container.
- `src\monitoring\FrameScopeSystemSampler.PerfCounters.cs`: CPU/memory/disk/network performance-counter setup and reads.
- `src\monitoring\FrameScopeSystemSampler.Gpu.cs`: NVIDIA SMI query and GPU field parsing.
- `src\monitoring\FrameScopeSystemSampler.Processes.cs`: process count and process-running checks.
- `src\monitoring\FrameScopeSystemSampler.IO.cs`: argument parsing, CSV writer, parsing, and rounding helpers.

Parallel backend rule:

- Report generator JSON shape and `Generate` orchestration should stay exclusive in `FrameScopeReportGenerator.cs`.
- Report CLI/default-run behavior should be edited in `FrameScopeReportGenerator.Cli.cs`.
- Report progress behavior should be edited in `FrameScopeReportGenerator.Progress.cs` and coordinated with `src\core\FrameScopeReportProgress.cs`.
- Process sampler CSV schema and sampling loop should be exclusive in `FrameScopeProcessSampler.cs`; helper-only changes can target `Models`, `Selection`, or `IO`.
- System sampler CSV schema and sampling loop should be exclusive in `FrameScopeSystemSampler.cs`; GPU, perf-counter, process, and IO helper changes can be edited independently in their focused files.
- Any sampler change still requires build plus stable simulator validation because monitor-session launches these exes by name.

## Stage 33 residual report-open cleanup

The report open surface is now split into smaller backend/report partial files:

- `src\app\FrameScopeNativeMonitor.ReportOpen.cs`: report/path open entry points, including the report-open marker gate and shell/explorer fallback entry.
- `src\app\FrameScopeNativeMonitor.ReportOpen.Browser.cs`: default browser launch, explicit browser candidate discovery, registry browser command parsing, and per-browser open arguments.
- `src\app\FrameScopeNativeMonitor.ReportOpen.Status.cs`: `status.json` report-open marker updates.
- `src\app\FrameScopeNativeMonitor.ReportStatus.cs`: status/progress/history decisions only. This file was inspected and left unsplit because it does not own UI layout or browser launch logic.
- `src\reporting\FrameScopeReportGenerator.Html.cs`: embedded report HTML/CSS/JavaScript template. It remains intentionally unsplit in this stage and should stay exclusive for future report-template work.

Parallel backend rule:

- Browser fallback and report-open launch behavior should be exclusive in `ReportOpen.Browser.cs`.
- Report-open status JSON updates should be exclusive in `ReportOpen.Status.cs` and coordinated with `ReportStatus.cs` if status shape changes.
- Do not put UI colors, WinForms page layout, or report page button bindings into report-open/backend files.
- Do not split `FrameScopeReportGenerator.Html.cs` together with watcher/session/report-open changes; plan and validate it as a dedicated report UI/template task.

## Stage 34 report template split ownership

The embedded report template is now split into focused report-generator partial files:

- `src\reporting\FrameScopeReportGenerator.Html.cs`: `MakeHtml()` entry and template assembly order only.
- `src\reporting\FrameScopeReportGenerator.Html.Layout.cs`: report document opening, body wrapper opening, and document closing fragments.
- `src\reporting\FrameScopeReportGenerator.Html.Styles.cs`: embedded report CSS.
- `src\reporting\FrameScopeReportGenerator.Html.Sections.cs`: static report body fragments, including sidebar, topbar/header, toolbar, chart surface, and summary panels.
- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`: `framescope-interactive-data.js` include and embedded chart/interaction JavaScript.

Parallel backend/report rule:

- Report data behavior still belongs in `FrameScopeReportGenerator.PresentMon.cs`, `SystemData.cs`, `ProcessData.cs`, `Analysis.cs`, `Metadata.cs`, `Csv.cs`, and the report-generator entry/progress files.
- Report template layout, CSS, and JavaScript can now be maintained separately in the `Html.*.cs` files above.
- Chart sampling semantics remain guarded by `tests\chart-sampling-tests.js`; do not change sampling behavior as part of a layout or CSS-only task.
- `build.ps1` remains exclusive when adding or removing report-generator source files.
