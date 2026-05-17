# FrameScopeMonitor Reference UI Implementation Plan

Status: planning only. No source code changes in this step.

Reference images are the only visual target. Earlier stage-4 dashboard is rejected as insufficient.

## Design System Summary

- Style: dark tech / gaming performance dashboard.
- Shell: fixed left navigation, top title/status, card-based page content, persistent bottom report generation bar.
- Language: all UI function text Chinese; product name may remain `FrameScope Monitor`.
- Surfaces: translucent deep blue cards with thin blue border and subtle cyan glow.
- Accents: cyan for active/nav/FPS, blue for primary action, green for ready/captured, purple for analysis/diagnostics, amber for warning, red for stop/error.
- Data policy: cards and buttons must be wired to actual config/status/report/diagnostic logic. Demo data allowed only on real-time page when no live run exists, and must be labeled.

## Plan Design Review

Score against reference before new implementation: 4/10.

Main gaps:

- Current UI has no left nav and no page router.
- Many labels/section titles remain English.
- Visual cards lack glassy glow/reference spacing/icon language.
- Overview, report center, settings page, live monitor page are not separate real pages.
- Real-time charts are absent.

Adjusted implementation rules:

- First create UI shell + page router; then implement pages one by one.
- Keep old event logic behind new buttons; do not create dead buttons.
- Add small helper modules/seams only if they reduce the current giant UI method.
- For real-time monitor, read active/latest run/status/logs where available; fallback demo stream must be explicit.
- For report center, derive list from `framescope-history.jsonl` and run folders; actions use existing open/report/diagnostic helpers.

## Page / Component / Function Mapping

### 1. Shell

- Files: `FrameScopeNativeMonitor.cs`; possibly new `FrameScopeUiState.cs` / `FrameScopeUiTheme.cs` if split is needed.
- Components:
  - left sidebar: 概览, 实时监控, 监控目标, 报告, 设置, 关于我们.
  - top title: `FrameScope Monitor` + Chinese subtitle.
  - top cards: 监测器状态, 已启用目标, 软件状态.
  - bottom card: 报告生成 progress.
  - left bottom: 服务状态 + 版本号.
- Real wiring:
  - watcher state from `framescope-watcher-state.json`.
  - target count from config/grid.
  - software status from watcher/report error state.
  - version from assembly.
  - progress from `LatestReportProgress()`.
- Completion: page switching works; all nav labels Chinese; no old one-screen layout remains.

### 2. 概览

- Components:
  - metric cards: 已启用目标, 捕获链状态, 最近报告状态, 输出目录状态, 诊断模式.
  - 捕获链流程图: 游戏/进程 -> 采样器 -> 分析引擎 -> 数据存储 -> 报告输出.
  - 受监控游戏列表.
  - 最近捕获状态, 最近报告, 输出目录状态.
  - 快速操作: 启动监测, 打开输出目录, 刷新进程, 保存配置.
- Real wiring:
  - target list/config.
  - report/status/history/latest report path.
  - output directory exists/writable check.
  - diagnostic toggles from config.
  - buttons call existing start/open/refresh/save functions.
- Empty state: no report -> `暂无报告`.

### 3. 设置

- Components:
  - 监测设置: 采样间隔, 自动打开报告, 详细日志.
  - 报告设置: 自动生成诊断报告, 报告输出处理方式.
  - 诊断设置: 性能诊断, 保留天数, 最大占用.
  - 数据目录: current path + 选择.
  - 配置摘要, 目标状态, 输出目录状态, 捕获链状态.
  - buttons: 恢复默认, 保存设置.
- Real wiring:
  - Save through `FrameScopeConfigStore.BuildConfigFromEditableTargets()` and `SaveConfig()`.
  - Need add/reset helper for defaults if missing.
  - Existing config supports most fields; report output handling can initially map to existing auto-open/retain behavior unless new field is added.
- Completion: changes persist after reload; summary matches saved config.

### 4. 报告

- Components:
  - 报告中心 list/table.
  - search box, status filter, refresh.
  - table columns: 报告名称, 目标进程, 生成时间, 状态, 大小.
  - 最近报告 list.
  - Capture Chain card.
  - 报告详情: target game/process/sample interval/duration/time/size/export path/modules.
  - buttons: 打开输出目录, 打开报告, 导出支持包, 重新生成, 启动生成.
- Real wiring:
  - reports from `framescope-history.jsonl` + run folders.
  - open report/path via existing `TryOpenPath()` / latest path helpers.
  - export support package via `FrameScopeDiagnostics.GenerateReport()`.
  - regenerate via existing `EnsureReportForCompletedRun()` when selected run has monitor data.
  - start generation disabled/clear status if no selected completed run.
- Completion: no dead buttons; unavailable actions show Chinese reason.

### 5. 实时监控

- Components:
  - FPS chart.
  - Frame Time chart.
  - current FPS, avg FPS, 1% Low, 0.1% Low.
  - CPU/GPU/memory/current process cards.
  - Capture Chain card.
  - real-time log panel: 暂停, 清空, colored log levels.
- Real/demo wiring:
  - Real source: active/latest run files when monitor session exists (`status.json`, `presentmon.csv`, sampler CSVs, watcher log).
  - Demo source: simulator-generated stream or deterministic in-memory demo when no active data. Must show `演示数据`.
  - Logs from watcher/report logs; pause/clear affect UI buffer only unless user chooses file cleanup later.
- Completion: charts update without blocking UI; demo mode obvious; no fixed fake-only graph.

### 6. 监控目标

- Components:
  - target table with enable checkbox, game/software, process name, sampling interval, auto-open switch, group selection.
  - refresh/add/save/start/stop.
  - right Capture Chain, Reports, Settings.
  - PUBG/TslGame special hint.
- Real wiring:
  - current grid config read/write.
  - process list from `Process.GetProcesses()`.
  - save/start/stop existing functions.
  - PUBG alias logic remains in `FrameScopeCapturePlanner`.
- Completion: table edits save and reload; start/stop watcher still work.

### 7. Report HTML / Chart UI

- Files: `FrameScopeReportGenerator.cs`.
- Components:
  - match same dark reference system.
  - zoom/pan/reset/tooltip.
  - point counts for raw/spike/trend.
  - share-ready title/stats/grid/legend.
- Real wiring:
  - no raw data deletion.
  - raw dense vs spike preserve arrays remain distinct.
- Completion: screenshot and interaction smoke test pass.

## Implementation Stages

1. Reference design planning: update design system and mapping docs. No code.
2. UI shell/page router: sidebar, top cards, bottom progress, Chinese-only shell.
3. 概览 + 监控目标 pages: wire existing config/start/stop/process/report state.
4. 设置 page: real save/reset/summary/status.
5. 报告 page: real report list/detail/actions/support package/regenerate.
6. 实时监控 page: real/latest data + explicit demo stream, logs, live cards.
7. Report HTML/chart UI polish.
8. Design review + review + health + final verification + context-save/handoff.

## Validation Per Page

- Screenshot compared to reference images.
- Chinese UI text audit.
- Dead-button audit.
- Config persistence or action verification.
- Residual process check when launching GUI/offscreen validation.
- `build.ps1` and tests after each code stage.

## Known Constraints

- Current app is WinForms. Visual match can be close with custom drawing/panels, but not pixel-perfect web/CSS glass unless switching framework. No framework switch unless user explicitly approves.
- Real PUBG unavailable locally. PUBG validation uses existing simulator; real anti-cheat/fullscreen validation remains manual.
