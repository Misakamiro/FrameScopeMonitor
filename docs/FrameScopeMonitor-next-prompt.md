# FrameScopeMonitor next prompt

Current state: project structure cleanup completed after Stage 17 UI delivery, Stage 19 separated GameLite automatic lightweight scripts from FrameScope Monitor boundaries, Stage 20 changed GameLite to SGuard-default/event-trigger start-stop automation, Stage 20.1 completed continued non-destructive verification, Stage 21 split `src\app\FrameScopeNativeMonitor.cs` into smaller partial-class files for UI design, UI interaction, watcher, monitor-session, and report orchestration ownership, Stage 22 split the report generator into focused partial-class files, Stage 23-28 further split UI components, UI shell, UI interactions, monitor-session helpers, report orchestration helpers, diagnostics helpers, and reference-sidebar drawing into focused files, Stage 29-32 finished another mechanical split of report-generator entry helpers, report/target pages, shared visual helpers, reference-sidebar drawing, and sampler helper files, Stage 33 cleaned up residual report/live/report-open cross-file overlaps, and Stage 34 split the embedded report HTML template into layout, styles, sections, and scripts partial files.

Read first:

1. `AGENTS.md`
2. `docs\FrameScopeMonitor-Project-Overview.md`
3. Matching module doc under `docs\modules\`

Current source layout:

- UI visuals: `src\ui\FrameScopeUiTheme.cs`, `src\ui\FrameScopeRoundedDrawing.cs`, `src\ui\FrameScopePanels.cs`, `src\ui\FrameScopeButtons.cs`, `src\ui\FrameScopeStatusControls.cs`, `src\ui\FrameScopeLiveChart.cs`, `src\ui\FrameScopeReferenceSidebar*.cs`, `src\app\FrameScopeNativeMonitor.UiShell.cs`, `src\app\FrameScopeNativeMonitor.UiVisualHelpers.cs`, `src\app\FrameScopeNativeMonitor.UiVisualCards.cs`, `src\app\FrameScopeNativeMonitor.UiVisualSections.cs`, `src\app\FrameScopeNativeMonitor.UiVisualButtons.cs`, `src\app\FrameScopeNativeMonitor.UiReportProgress.cs`, `src\app\FrameScopeNativeMonitor.UiScreenshots.cs`, `src\app\FrameScopeNativeMonitor.UiStatusDisplay.cs`, page partial files, and report-page layout files.
- UI state/interactions: `src\ui\FrameScopeUiState.cs`, `src\ui\FrameScopeLiveData.cs`, `src\ui\FrameScopeReportPage.Actions.cs`, `src\ui\FrameScopeReportPage.Detail.cs`, `src\app\FrameScopeNativeMonitor.UiFields.cs`, `src\app\FrameScopeNativeMonitor.UiRouting.cs`, `src\app\FrameScopeNativeMonitor.UiHelpers.cs`, `src\app\FrameScopeNativeMonitor.UiConfigActions.cs`, `src\app\FrameScopeNativeMonitor.UiProcessPicker.cs`, `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`, `src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs`, `src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs`, `src\app\FrameScopeNativeMonitor.UiDiagnosticActions.cs`, `src\app\FrameScopeNativeMonitor.PageTargets.Grid.cs`, and `src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`.
- App entry/shared helpers: `src\app\FrameScopeNativeMonitor.cs`
- App watcher/session/report orchestration: `src\app\FrameScopeNativeMonitor.Watcher.cs`, `src\app\FrameScopeNativeMonitor.MonitorSession*.cs`, `src\app\FrameScopeNativeMonitor.ReportOrchestration*.cs`, `src\app\FrameScopeNativeMonitor.ReportStatus.cs`, and `src\app\FrameScopeNativeMonitor.ReportOpen.cs`
- Core config/planning/progress: `src\core\`
- Monitoring samplers: `src\monitoring\FrameScopeProcessSampler*.cs` and `src\monitoring\FrameScopeSystemSampler*.cs`
- Diagnostics: `src\diagnostics\FrameScopeDiagnostics*.cs`
- Report generator: `src\reporting\FrameScopeReportGenerator.cs` for entry/generation orchestration; `FrameScopeReportGenerator.Models.cs`, `Cli.cs`, `Progress.cs`, `Diagnostics.cs`, `PresentMon.cs`, `SystemData.cs`, `ProcessData.cs`, `Analysis.cs`, `Metadata.cs`, and `Csv.cs` for focused report data work; `FrameScopeReportGenerator.Html.cs`, `Html.Layout.cs`, `Html.Styles.cs`, `Html.Sections.cs`, and `Html.Scripts.cs` for focused embedded report template work.
- Lightweight scripts: sibling standalone project `..\gamelite-auto-lightweight\`.

Compatibility:

- Root GameLite `.ps1` files are thin wrappers that forward to `..\gamelite-auto-lightweight\`.
- Root `.cmd` launchers still call root wrappers and forward `%*`.
- `build.ps1` compiles moved source files from `src\`.
- `tests\Build-FrameScopeTests.ps1` rebuilds test exes against moved source files.
- `tests\chart-sampling-tests.js` now reads all `src\reporting\FrameScopeReportGenerator*.cs` files because embedded report chart JavaScript can live in report template partial files such as `FrameScopeReportGenerator.Html.Scripts.cs`.
- Stage 18 verification passed for build, rebuilt tests, chart test, RenderProbe build, stable PUBG simulator, lightweight wrapper parse and lightweight check script.
- Stage 19 historical note: the previous separation pass validated a SGuard opt-in design; Stage 20 supersedes that rule with SGuard throttling enabled by default.
- Stage 20 changed the current GameLite rules: SGuard is throttled by default, disable with `-DisableSGuardThrottle`, and new auto-trigger installs separate WMI start and stop events instead of a persistent polling session.
- Stage 20.1 verification passed parser checks, lightweight separation tests, root/direct Check scripts, external cwd wrapper invocation, legacy `GameLiteSession.ps1` no-op behavior, SGuard mock default throttle/restore/disable paths, FrameScope build/test isolation, chart test, RenderProbe build, and final residual process/state-file checks.
- Stage 35 extracted GameLite into sibling standalone project `..\gamelite-auto-lightweight\`; FrameScope now keeps root `.ps1` and `.cmd` compatibility bridges only.

Lightweight/GameLite boundary rules:

- Do not wire GameLite logic into FrameScope C# app, samplers, report generator, build, or tests.
- Do not make FrameScope build/test require WMI triggers.
- Do not make GameLite depend on `FrameScopeMonitor.exe`, PresentMon, FrameScope report generator, FrameScope run directories, or monitoring data directories.
- Root wrappers are compatibility bridges only for old WMI consumers, `.cmd` launchers and manual habits.
- SGuard throttling is enabled by default. `-AllowSGuardThrottle` is compatibility-only; use `-DisableSGuardThrottle` to turn it off. Default SGuard strategy is Idle priority, IO priority 0, page priority 1, affinity last two logical cores; `-StrictSGuard` uses last one logical core.
- GameLite auto trigger should use `Win32_ProcessStartTrace` for entering lightweight mode and `Win32_ProcessStopTrace` for restore. Do not reintroduce a long-running `GameLiteSession.ps1` game-exit polling loop as the default automation path.
- Existing legacy GameLite/SGuard WMI consumers may remain on the machine. Treat them as read-only status unless the user explicitly authorizes install/remove. Legacy game-start consumers that still point at `GameLiteSession.ps1` should no-op unless the new stop trigger exists.
- Current host state after Stage 20.1 is still legacy-only WMI: new game start, game stop, and SGuard late-start triggers are not installed because WMI migration was not authorized or executed.
- `Exit-GameLite.ps1` restores only from its saved snapshot; do not add broad fallback restores.
- `Enter-GameLite.ps1` writes an empty JSON snapshot marker `[]` when no eligible process is changed. This marker is intentional: it lets SGuard late-start throttling recognize the active GameLite session and is removed by `Exit-GameLite.ps1`.

Known caution:

- `src\app\FrameScopeNativeMonitor.cs` is now the app entry/shared helper file. Do not move new feature logic back into it.
- `FrameScopeNativeMonitor.UiInteractions.cs`, `FrameScopeNativeMonitor.UiShell.cs`, `FrameScopeNativeMonitor.MonitorSession.cs`, and `FrameScopeNativeMonitor.ReportOrchestration.cs` are now small entry/placeholder/orchestration files. New feature logic should go into the focused partial file listed in the matching module doc.
- `src\ui\FrameScopeUiTheme.cs`, `src\ui\FrameScopeReferenceSidebar.LogoDrawing.cs`, `src\app\FrameScopeNativeMonitor.UiVisualButtons.cs`, `src\app\FrameScopeNativeMonitor.UiRouting.cs`, `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`, `src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs`, `src\app\FrameScopeNativeMonitor.MonitorSession.PresentMon.cs`, `src\app\FrameScopeNativeMonitor.MonitorSession.Status.cs`, `src\app\FrameScopeNativeMonitor.ReportStatus.cs`, `src\app\FrameScopeNativeMonitor.ReportOpen.cs`, `src\diagnostics\FrameScopeDiagnostics.Redaction.cs`, `src\diagnostics\FrameScopeDiagnostics.Retention.cs`, and `build.ps1` should be exclusive when changing their shared behavior.
- `src\reporting\FrameScopeReportGenerator.cs` is now a report-generator entry/orchestration file. Keep generated JSON shape, manifest/progress writing, and main `Generate` orchestration exclusive when changing it. CLI-only edits should go to `FrameScopeReportGenerator.Cli.cs`; progress-only edits should go to `FrameScopeReportGenerator.Progress.cs`.
- `src\reporting\FrameScopeReportGenerator.Html.cs` holds only `MakeHtml()` and template assembly order. Report template work should target `Html.Layout.cs`, `Html.Styles.cs`, `Html.Sections.cs`, or `Html.Scripts.cs` according to responsibility.
- Report data changes should target the focused report partial file: `Models.cs`, `PresentMon.cs`, `SystemData.cs`, `ProcessData.cs`, `Analysis.cs`, `Metadata.cs`, `Csv.cs`, or `Diagnostics.cs`.
- Process sampler changes should target `FrameScopeProcessSampler.cs` for loop/CSV schema, `FrameScopeProcessSampler.Models.cs` for row/counter models, `Selection.cs` for top CPU/IO and process-running checks, or `IO.cs` for args/CSV helpers.
- System sampler changes should target `FrameScopeSystemSampler.cs` for loop/CSV schema, `Models.cs` for snapshots/counter containers, `PerfCounters.cs` for CPU/memory/disk/network counters, `Gpu.cs` for NVIDIA SMI, `Processes.cs` for process status, or `IO.cs` for args/CSV helpers.
- Target page screenshot harness still may fail to write PNG around the DataGridView path. Stage 22 confirmed `--ui-page targets --ui-screenshot` can hang without writing `stage22-targets.png`; identify the process by that exact command line and stop only that screenshot process. Overview, settings, and live screenshots still generate normally.
- Very short `Run-PubgSimulation.ps1 -Scenario no-data -DurationSeconds 1` can stop before `summary.json`; use stable 4 second scenario for end-to-end monitor validation unless specifically testing no-data behavior.
- Real fullscreen game capture and anti-cheat ETW behavior still require manual validation with PUBG/Valorant/CS2.
- Do not add fake FPS curves. Live page should show empty state when no enabled configured target process exists.
- Real GameLite validation still requires a real game or simulator lifecycle: run Check, start target game, confirm Enter, exit game, confirm Exit restore, then check residual GameLite/PowerShell/FrameScope/PresentMon processes.

Next useful prompts:

For UI design work: Act as the FrameScope Monitor UI design owner. Read `AGENTS.md`, `docs\FrameScopeMonitor-Project-Overview.md`, and `docs\modules\software-ui.md`. Work mainly in `src\ui\FrameScopeUiTheme.cs`, `src\ui\FrameScopeRoundedDrawing.cs`, `src\ui\FrameScopePanels.cs`, `src\ui\FrameScopeButtons.cs`, `src\ui\FrameScopeStatusControls.cs`, `src\ui\FrameScopeLiveChart.cs`, `src\ui\FrameScopeReferenceSidebar*.cs`, `src\app\FrameScopeNativeMonitor.UiVisualHelpers.cs`, `UiVisualCards.cs`, `UiVisualSections.cs`, `UiVisualButtons.cs`, `UiReportProgress.cs`, `UiScreenshots.cs`, `UiStatusDisplay.cs`, page layout files, and `src\ui\FrameScopeReportPage.Layout.cs`. Do not change watcher/session/report orchestration. Verify with build, UI screenshots, and relevant UI tests.

For UI interaction work: Act as the FrameScope Monitor UI interaction owner. Read `AGENTS.md`, `docs\FrameScopeMonitor-Project-Overview.md`, and `docs\modules\ui-interactions.md`. Work mainly in `FrameScopeNativeMonitor.UiRouting.cs`, `UiConfigActions.cs`, `UiProcessPicker.cs`, `UiWatcherControls.cs`, `UiProcessCleanup.cs`, `UiStatusRefresh.cs`, `UiDiagnosticActions.cs`, `PageTargets.Grid.cs`, `PageTargets.Actions.cs`, `src\ui\FrameScopeReportPage.Actions.cs`, `src\ui\FrameScopeReportPage.Detail.cs`, `src\ui\FrameScopeUiState.cs`, and `src\ui\FrameScopeLiveData.cs`. Keep buttons wired to real logic. Verify build, `FrameScopeUiStateTests.exe`, screenshots, and page switching.

For backend monitoring work: Act as the FrameScope Monitor backend monitoring owner. Read `AGENTS.md`, `docs\FrameScopeMonitor-Project-Overview.md`, and `docs\modules\backend-monitoring.md`. Work mainly in `src\app\FrameScopeNativeMonitor.Watcher.cs`, `src\app\FrameScopeNativeMonitor.MonitorSession*.cs`, `src\app\FrameScopeNativeMonitor.ReportOrchestration*.cs`, `src\app\FrameScopeNativeMonitor.ReportStatus.cs`, `src\app\FrameScopeNativeMonitor.ReportOpen.cs`, `src\core\`, `src\monitoring\FrameScopeProcessSampler*.cs`, `src\monitoring\FrameScopeSystemSampler*.cs`, and `src\diagnostics\FrameScopeDiagnostics*.cs`. Do not change UI styling or GameLite. Verify build, rebuilt tests, chart test, RenderProbe build, and stable simulator scenario.

For report generator work: Act as the FrameScope Monitor report generator owner. Read `AGENTS.md`, `docs\FrameScopeMonitor-Project-Overview.md`, and `docs\modules\backend-monitoring.md`. Work in `src\reporting\FrameScopeReportGenerator*.cs`: orchestration in `.cs`, models in `.Models.cs`, CLI in `.Cli.cs`, progress in `.Progress.cs`, diagnostics lookup in `.Diagnostics.cs`, PresentMon parsing in `.PresentMon.cs`, system samples in `.SystemData.cs`, process matrix in `.ProcessData.cs`, FPS/stat math in `.Analysis.cs`, metadata/hardware/status in `.Metadata.cs`, CSV parsing in `.Csv.cs`, report template assembly in `.Html.cs`, report document/layout fragments in `.Html.Layout.cs`, report CSS in `.Html.Styles.cs`, static report body fragments in `.Html.Sections.cs`, and chart/interaction JavaScript in `.Html.Scripts.cs`. Do not change watcher/session/GameLite logic. Verify build, chart sampling test, direct report generation or stable simulator, RenderProbe build, and `git diff --check`.

Stage 33 residual split notes:

- Report page layout/action split: `src\ui\FrameScopeReportPage.Layout.cs` creates controls and layout; `src\ui\FrameScopeReportPage.Actions.cs` owns click binding and report actions.
- Live page split: `src\app\FrameScopeNativeMonitor.PageLive.Layout.cs` owns visual structure, `PageLive.Lifecycle.cs` owns the timer and refresh lifecycle, `PageLive.Log.cs` owns pause/clear/display behavior, and `src\ui\FrameScopeLiveData.Csv.cs` owns CSV helpers used by live data loading.
- Report open split: `src\app\FrameScopeNativeMonitor.ReportOpen.cs` owns report/path open entry points, `ReportOpen.Browser.cs` owns browser fallback/candidate discovery, and `ReportOpen.Status.cs` owns report-open status writes.
- `src\app\FrameScopeNativeMonitor.ReportStatus.cs` was inspected and should remain status/progress/history-only unless status shape changes.
- Stage 34 report-template split: `src\reporting\FrameScopeReportGenerator.Html.cs` is now a small assembly entry; `Html.Layout.cs`, `Html.Styles.cs`, `Html.Sections.cs`, and `Html.Scripts.cs` own the report template fragments. Keep `build.ps1` exclusive for source-list changes.

Act as the GameLite lightweight-script owner. Read `AGENTS.md`, `docs\FrameScopeMonitor-Project-Overview.md`, and `docs\modules\lightweight-script.md`. Verify the current GameLite boundary without installing/removing WMI unless explicitly authorized. Run `tests\lightweight-separation-tests.ps1`, root/direct `Check-GameLiteAutoTrigger.ps1`, wrapper parameter forwarding, external cwd invocation, SGuard default-enabled dry-run, `-DisableSGuardThrottle` dry-run, and residual process checks. If making changes, keep root wrappers compatible, keep FrameScope independent, and keep game start/exit automation event-triggered rather than long-running polling.

For GameLite implementation work, switch to `C:\Users\misakamiro\Documents\Codex\2026-05-02\gamelite-auto-lightweight`, read its `AGENTS.md`, `README.md`, and `docs\lightweight-script.md`, then run `tests\gamelite-standalone-tests.ps1`.
