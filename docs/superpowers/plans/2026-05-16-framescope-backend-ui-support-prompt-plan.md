# FrameScope Backend UI Support Prompt Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `diagnose`, `review`, `improve-codebase-architecture`, `tdd`, `health`, and `verification-before-completion` before implementation completion. Use `ship` only if the user explicitly expands this task to final packaging or release.

**Goal:** Produce a downstream backend implementation prompt that fills the real backend support needed by the current reference-image UI without changing visual UI files or faking data.

**Architecture:** The UI already wires most buttons to real handlers. The remaining backend-risk area is the scattered status/report/history/readiness contract behind Overview, Targets, Reports, Settings, and the shared bottom report-progress card. The downstream worker should inspect current code, then add or harden backend/core/report status seams only where real UI state needs a reliable source.

**Tech Stack:** WinForms C# on .NET Framework compiler via `build.ps1`; JSON status/config/history files; watcher/session/report generator executables; simulator validation through `tools\FrameScopePubgSimulator`.

---

## Evidence Read

- Reference images:
  - `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (1).png`
  - `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (2).png`
  - `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (3).png`
  - `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (4).png`
  - `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_38_46.png`
- Docs:
  - `AGENTS.md`
  - `docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md`
  - `docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md`
  - `docs\FrameScopeMonitor-Project-Overview.md`
  - `docs\modules\backend-monitoring.md`
  - `docs\modules\ui-interactions.md`
  - `docs\modules\software-ui.md`
  - `docs\FrameScopeMonitor-progress.md`
  - `docs\FrameScopeMonitor-next-prompt.md`
  - `docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Role.md`
  - `docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Worklog.md`
  - `docs\implementation-reports\2026-05-16-framescope-ui-design-implementation-report.md`
  - `docs\implementation-reports\2026-05-16-framescope-ui-interaction-implementation-report.md`
- Code inspected:
  - `src\app\FrameScopeNativeMonitor.PageOverview.cs`
  - `src\app\FrameScopeNativeMonitor.PageSettings.cs`
  - `src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs`
  - `src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`
  - `src\ui\FrameScopeReportPage.Layout.cs`
  - `src\app\FrameScopeNativeMonitor.UiHelpers.cs`
  - `src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs`
  - `src\app\FrameScopeNativeMonitor.UiConfigActions.cs`
  - `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`
  - `src\ui\FrameScopeReportPage.Actions.cs`
  - `src\app\FrameScopeNativeMonitor.Watcher.cs`
  - `src\app\FrameScopeNativeMonitor.ReportStatus.cs`
  - `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs`
  - `src\app\FrameScopeNativeMonitor.ReportOpen.Status.cs`
  - `src\core\FrameScopeReportProgress.cs`
  - `src\core\FrameScopeConfigStore.cs`
  - `src\reporting\FrameScopeReportGenerator.cs`
  - `tests\FrameScopeUiStateTests.cs`
  - `tests\FrameScopePubgSimulatorTests.cs`

## UI To Backend Mapping

| UI surface | Current observed source | Backend contract required |
| --- | --- | --- |
| Top monitor status card | `IsWatcherRunningQuiet()` / `StatePath` | Real watcher pid, phase, active monitor count, stale-state handling, no fake ready/running state. |
| Enabled target card | `FrameScopeConfigStore` config targets | Config normalization must preserve enabled target count, aliases, sample intervals, auto-open. |
| Software status card | UI status pill from watcher/config actions | Backend errors must surface through status/log fields instead of always showing ready. |
| Overview capture-chain status | watcher running plus latest report path | A reliable summary should distinguish idle, monitoring, done, diagnostic report, error, missing data. |
| Overview latest report/output cards | history and latest report path | History must point to real HTML/run dir; missing files must be explicit. |
| Targets table | `framescope-config.json` | Save/read must normalize targets and avoid losing `ProcessSampleIntervalMs` / `SlowSampleIntervalMs`. |
| Target page start/stop | `StartWatcher()` / `StopWatcher()` | Watcher lifecycle must write state/logs and clean monitor/session/sampler children. |
| Settings controls | `FrameScopeConfigStore` | Diagnostic/log/retention/data-root fields must persist and affect diagnostics/retention. |
| Report center metrics | `RecentHistoryEntries()` and `LatestReportPath()` | Report count/status/type must be based on real history/status/manifest, not static examples. |
| Report detail/quick actions | selected history entry, run dir, report HTML | Missing HTML/run dir/CSV must disable or fail with real errors; regenerate uses report generator. |
| Bottom report generation card | latest `status.json` with progress fields | Progress must be current, phase-aware, error-aware, and not stale from an unrelated old run. |

## Downstream Complete Copy Prompt v2

```text
/goal 补齐 FrameScope Monitor 当前 UI 按钮、状态卡、报告入口和监测流程所需的真实后端支撑：只修改本提示词允许的后端/core/report/status 文件，禁止伪造数据、禁止修改 UI 视觉文件、禁止接回 GameLite/WMI/SGuard，完成前必须通过 build、测试、stable simulator、报告产物、manifest 和残留进程验证。

你现在是 FrameScope Monitor 的后端实现对话框。你的任务不是重新设计 UI，而是检查当前 UI 参考图实现后的按钮、状态卡、报告入口和底部报告进度卡，补齐或加固它们背后的真实后端状态、历史、报告、日志和错误输出。

项目路径：
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d

如果当前环境没有可调用的 goal 工具，也必须在首条回复顶部写出同等目标，并持续按目标清单推进。

必须使用 / 调用的 skills：
- diagnose
- review
- improve-codebase-architecture
- tdd
- health
- verification-before-completion

如果用户临时把任务扩大成 bug 修复后的最终打包或发布，再额外使用：
- ship

必须先读取：
1. AGENTS.md
2. docs\orchestration\FrameScopeMonitor-BackendPrompt-Role.md
3. docs\orchestration\FrameScopeMonitor-BackendPrompt-Worklog.md
4. docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md
5. docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md
6. docs\FrameScopeMonitor-Project-Overview.md
7. docs\modules\backend-monitoring.md
8. docs\modules\ui-interactions.md
9. docs\modules\software-ui.md
10. docs\implementation-reports\2026-05-16-framescope-ui-design-implementation-report.md
11. docs\implementation-reports\2026-05-16-framescope-ui-interaction-implementation-report.md
12. docs\FrameScopeMonitor-progress.md 和 docs\FrameScopeMonitor-next-prompt.md 只读参考，不要修改。

执行前必须实际查看这些 UI/后端代码，不要只看文档：
- src\app\FrameScopeNativeMonitor.PageOverview.cs
- src\app\FrameScopeNativeMonitor.PageSettings.cs
- src\app\FrameScopeNativeMonitor.PageTargets*.cs
- src\ui\FrameScopeReportPage*.cs
- src\app\FrameScopeNativeMonitor.UiHelpers.cs
- src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs
- src\app\FrameScopeNativeMonitor.UiConfigActions.cs
- src\app\FrameScopeNativeMonitor.UiWatcherControls.cs
- src\app\FrameScopeNativeMonitor.UiDiagnosticActions.cs
- src\app\FrameScopeNativeMonitor.Watcher.cs
- src\app\FrameScopeNativeMonitor.MonitorSession*.cs
- src\app\FrameScopeNativeMonitor.ReportOrchestration*.cs
- src\app\FrameScopeNativeMonitor.ReportStatus.cs
- src\app\FrameScopeNativeMonitor.ReportOpen*.cs
- src\core\FrameScopeConfigStore.cs
- src\core\FrameScopeReportProgress.cs
- src\reporting\FrameScopeReportGenerator*.cs
- src\diagnostics\FrameScopeDiagnostics*.cs
- tests\FrameScopeUiStateTests.cs
- tests\FrameScopePubgSimulatorTests.cs

本轮 UI 按钮/状态与后端能力映射：
- 概览“启动监测” -> 保存真实配置并启动 watcher；需要 watcher state/log 支撑。
- 概览/目标/报告/设置“打开输出/报告目录” -> 打开真实 DataRoot 或报告目录；目录不存在时应明确创建或报错。
- 顶部“监测器/已启用目标/软件状态” -> watcher state、config targets、最近错误/就绪状态。
- 概览“捕获链状态/最近捕获/最近报告/输出目录/诊断模式” -> config、StatePath、history、status.json、manifest、目录可写性。
- 目标表格 -> framescope-config.json；必须保留别名、采样间隔、auto-open 和慢采样字段。
- 目标“刷新进程/添加进程/保存配置/启动/停止” -> 真实进程列表、配置写入、watcher/session 进程生命周期。
- 设置“采样间隔/自动打开报告/详细日志/自动诊断/性能诊断/保留天数/最大 MB/数据目录” -> FrameScopeConfigStore、diagnostics retention、watcher verbose/perf logging。
- 报告中心“最近报告状态/已生成报告/导出格式/报告摘要/打开 HTML/详细报告/重新生成” -> history、selected run dir、ReportHtml、status.json、manifest、diagnostic report、report generator。
- 底部“报告生成”卡 -> 最新有效 report progress/status；必须避免显示陈旧或不相关的旧 run 进度。

你必须先回答这 8 个问题：
1. 每个 UI 按钮或状态背后对应哪个真实后端动作？
2. 需要读取或写入哪个 config、status、summary、manifest、history、log？
3. 是否影响 watcher/session/report generation/report open？
4. 是否影响采样器 CSV schema？
5. 是否影响报告 HTML/data.js/manifest？
6. 是否影响真实游戏捕获，还是 simulator 可验证？
7. 是否需要安装目录同步或最终安装器打包？
8. 是否触碰 GameLite/WMI/SGuard 边界？

建议实现范围：
先诊断，不要预设一定要改代码。若确认存在后端缺口，优先补齐“UI 可读的真实状态/报告摘要 seam”，例如在现有后端/core/status 文件中集中产出或读取：
- watcher phase、pid、active monitors、completed runs、last report、last error/log path；
- latest run 的 status/summary/manifest；
- latest report 是否存在、是否 hasFrameData、reportKind、frames、process/system sample count；
- DataRoot 是否存在/可写；
- config summary，包括 enabled/total targets、sample interval、auto-open、diagnostic/log/retention fields；
- report progress 的新鲜度和所属 run，避免底部进度卡误读旧 status.json。

允许修改文件：
- src\app\FrameScopeNativeMonitor.Watcher.cs
- src\app\FrameScopeNativeMonitor.MonitorSession*.cs
- src\app\FrameScopeNativeMonitor.ReportOrchestration*.cs
- src\app\FrameScopeNativeMonitor.ReportStatus.cs
- src\app\FrameScopeNativeMonitor.ReportOpen*.cs
- src\core\FrameScopeConfigStore.cs
- src\core\FrameScopeCapturePlanner.cs
- src\core\FrameScopeReportProgress.cs
- src\diagnostics\FrameScopeDiagnostics*.cs
- src\reporting\FrameScopeReportGenerator.cs
- src\reporting\FrameScopeReportGenerator.Models.cs
- src\reporting\FrameScopeReportGenerator.Cli.cs
- src\reporting\FrameScopeReportGenerator.Progress.cs
- src\reporting\FrameScopeReportGenerator.Diagnostics.cs
- src\reporting\FrameScopeReportGenerator.PresentMon.cs
- src\reporting\FrameScopeReportGenerator.SystemData.cs
- src\reporting\FrameScopeReportGenerator.ProcessData.cs
- src\reporting\FrameScopeReportGenerator.Analysis.cs
- src\reporting\FrameScopeReportGenerator.Metadata.cs
- src\reporting\FrameScopeReportGenerator.Csv.cs
- tests\FrameScopeConfigStoreTests.cs
- tests\FrameScopeCapturePlannerTests.cs
- tests\FrameScopeReportProgressTests.cs
- tests\FrameScopeDiagnosticsTests.cs
- tests\FrameScopePubgSimulatorTests.cs
- tests\FrameScopeUiStateTests.cs

只有当你证明 UI 交互文件必须读取新增后端状态 seam，且不改视觉布局时，才允许最小联动修改：
- src\app\FrameScopeNativeMonitor.UiHelpers.cs
- src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs
- src\ui\FrameScopeReportPage.Detail.cs
这三项联动必须在最终报告里单独说明原因。不要修改 UI 视觉/layout 文件。

禁止修改文件：
- src\ui\FrameScopeUiTheme.cs
- src\ui\FrameScopeRoundedDrawing.cs
- src\ui\FrameScopePanels.cs
- src\ui\FrameScopeButtons.cs
- src\ui\FrameScopeStatusControls.cs
- src\ui\FrameScopeReferenceSidebar*.cs
- src\app\FrameScopeNativeMonitor.UiVisual*.cs
- src\app\FrameScopeNativeMonitor.PageLive.Layout.cs
- src\app\FrameScopeNativeMonitor.PageOverview.cs
- src\app\FrameScopeNativeMonitor.PageSettings.cs
- src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs
- src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs
- src\ui\FrameScopeReportPage.Layout.cs
- scripts\lightweight\
- 根目录 GameLite wrapper `.ps1` / `.cmd`
- WMI trigger 安装/删除/迁移
- SGuard 策略
- packaging\
- docs\FrameScopeMonitor-progress.md
- docs\FrameScopeMonitor-next-prompt.md

高冲突文件必须独占并在最终报告说明：
- build.ps1
- src\app\FrameScopeNativeMonitor.cs
- src\app\FrameScopeNativeMonitor.MonitorSession.PresentMon.cs
- src\app\FrameScopeNativeMonitor.MonitorSession.Status.cs
- src\app\FrameScopeNativeMonitor.ReportStatus.cs
- src\app\FrameScopeNativeMonitor.ReportOpen*.cs
- src\reporting\FrameScopeReportGenerator.cs
- src\diagnostics\FrameScopeDiagnostics.Redaction.cs
- src\diagnostics\FrameScopeDiagnostics.Retention.cs

明确不允许做什么：
- 不允许为了 UI 卡片好看写死“可用、就绪、12、HTML + 详细”等假状态。
- 不允许把没有帧数据的空报告当成 full 成功。
- 不允许删除、跳过或缩水原始 CSV/data.js/manifest 数据来让 UI 更快。
- 不允许把 GameLite 接回 FrameScope C# app、build、tests、report chain。
- 不允许安装、删除或迁移 WMI trigger。
- 不允许隐藏 SGuard 默认行为或关闭开关。
- 不允许只跑部分验证就声称完成。

分阶段执行：
1. 用 diagnose 建立反馈回路：先用现有 tests、status/history fixture、stable simulator 或直接报告生成证明当前 UI 状态来源是否有缺口。
2. 列出 3-5 个可证伪假设，例如“底部进度卡可能读取旧 status.json”“报告中心计数可能只来自 history 而没有校验文件存在”“捕获链状态不能区分 diagnostic/full/error”。
3. 用 tdd 选择最小行为测试。优先扩展现有 C# 测试；如果没有合适 seam，先在最终报告说明测试 seam 缺口，再做最小实现。
4. 做最小后端实现，优先集中在 status/report/core seam，不把逻辑塞进 UI 视觉文件。
5. 用 review 检查 diff：真实数据来源、错误路径、status 字段兼容、manifest 字段、残留进程、文件边界。
6. 用 health 跑完整项目验证。
7. 用 verification-before-completion 汇总每条命令的实际结果。

最低验证命令：
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeConfigStoreTests.exe
.\tests\FrameScopeCapturePlannerTests.exe
.\tests\FrameScopeReportProgressTests.exe
.\tests\FrameScopeDiagnosticsTests.exe
.\tests\FrameScopePubgSimulatorTests.exe
.\tests\FrameScopeUiStateTests.exe
node .\tests\chart-sampling-tests.js
dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario stable -DurationSeconds 4
"C:\Program Files\Git\cmd\git.exe" diff --check

如果 `node .\tests\chart-sampling-tests.js` 命中 WindowsApps `Access is denied`，把下面路径放到 PATH 前面后重跑：
C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin

如果改了报告生成或报告数据，还必须：
- 直接报告生成或 stable simulator 生成报告。
- 检查 HTML/data.js/manifest 存在。
- 检查 manifest 中 hasFrameData、reportKind、frame count、process sample count、system sample count。
- Edge headless 打开报告并截图。
- 截图非空。
- 不破坏 chart canvas、gauges、process rows、summary rows、data include、chart sampling script。

如果改了 watcher/session/采样器，还必须：
- simulator 证明 monitorExit=0、reportExit=0。
- 检查 presentmon.csv、process-samples.csv、system-samples.csv、summary.json、status.json。
- 检查游戏退出后 monitor/session/sampler/PresentMon 没有残留。

真实 PUBG 无法测试时，最终报告必须写出手动验证步骤：
1. 打开 FrameScope Monitor，确认 PUBG/TslGame 目标启用。
2. 启动监测。
3. 启动 PUBG 并进入一段真实渲染场景。
4. 退出 PUBG。
5. 确认 run 目录生成 presentmon.csv、process-samples.csv、system-samples.csv、summary.json、status.json。
6. 确认 reportExit=0、hasFrameData=true、reportKind=full。
7. 打开 HTML 报告，检查 FPS、1% Low、0.1% Low、process rows、system charts。
8. 确认退出后没有 FrameScopeMonitor --monitor-session、FrameScopeProcessSampler、FrameScopeSystemSampler、PresentMon 残留。

最终输出格式：
1. 当前结论。
2. 修改了哪些文件。
3. 每个 UI 按钮/状态现在由哪个真实后端能力支撑。
4. 修复或补齐了哪些 status/config/history/manifest/log 字段。
5. 测试和验证命令及结果。
6. simulator 结果和真实 PUBG 手动验证步骤。
7. 是否触碰 GameLite/WMI/SGuard，默认应为未触碰。
8. 未解决风险或无法验证项。
```

## Self-Review Checklist

- [x] Prompt starts with goal-mode instruction.
- [x] Prompt keeps backend implementation separate from UI visual files.
- [x] Prompt does not authorize GameLite/WMI/SGuard work.
- [x] Prompt requires simulator and manual PUBG validation path.
- [x] Prompt includes explicit allow/deny file lists and high-conflict files.
- [x] Prompt forbids fake UI data and stale report-progress claims.
