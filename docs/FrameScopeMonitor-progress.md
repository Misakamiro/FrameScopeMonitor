# FrameScopeMonitor Progress

## Current Redesign Stage 5 - Reference UI reset planning

Status: completed on 2026-05-10; planning only, no source code changed.

### Goal

Use the five user-provided reference UI images as the new visual source of truth, replace the previous B-dashboard interpretation, and define page/component/function mapping before coding.

### Files / modules

- docs/FrameScopeMonitor-design-system.md
- docs/FrameScopeMonitor-reference-ui-plan.md
- docs/FrameScopeMonitor-progress.md
- docs/FrameScopeMonitor-next-prompt.md

### Reference images

- C:/Users/misakamiro/Downloads/ChatGPT Image 2026年5月10日 02_46_23 (2).png
- C:/Users/misakamiro/Downloads/ChatGPT Image 2026年5月10日 02_46_23 (5).png
- C:/Users/misakamiro/Downloads/ChatGPT Image 2026年5月10日 02_46_23 (4).png
- C:/Users/misakamiro/Downloads/ChatGPT Image 2026年5月10日 02_46_23 (3).png
- C:/Users/misakamiro/Downloads/ChatGPT Image 2026年5月10日 02_46_22 (1).png

### Changes

- Marked the reference images as the new design source of truth.
- Extracted visual system: left glass sidebar, black/deep-blue background, translucent glowing cards, cyan/blue/green/purple accents, Chinese functional text, bottom report progress card.
- Added reference primitive/semantic/component tokens.
- Added plan-design-review: current stage-4 UI scores 4/10 vs reference; main gaps are missing sidebar/router/page separation, English labels, and non-reference card/icon language.
- Added page/function mapping for shell, 概览, 设置, 报告, 实时监控, 监控目标, and report HTML/chart UI.
- Defined staged implementation order and per-page validation rules.

### Verification

- Reference images opened and visually inspected.
- Planning docs updated only; no code/build run in this planning step.

### Risks

- Pixel-perfect match is limited by current WinForms stack. Close reference match is feasible with custom drawing/panels; full CSS-like glass accuracy would require framework switch, which is not assumed.
- Real PUBG remains unavailable locally; simulator/mock remains validation path.

### Remaining

- Next stage: implement shell/page router with left navigation, top Chinese status cards, bottom report progress, service/version card, and Chinese-only text audit.

## Current Redesign Stage 4 - Professional dashboard main UI

Status: completed on 2026-05-10.

### Goal

Replace the patched/debug-like WinForms main UI with the selected B Professional Performance Dashboard direction while preserving monitoring, report, settings, and diagnostic behavior.

### Files / modules

- FrameScopeNativeMonitor.cs
- docs/FrameScopeMonitor-progress.md
- docs/FrameScopeMonitor-next-prompt.md
- artifacts/ui-stage4-dashboard.png
- dist/FrameScopeMonitor-Setup.exe

### Changes

- Rebuilt the main window into a dashboard layout: header status strip, left monitoring target workflow, right capture/report/settings column, and bottom report-generation band.
- Centralized UI color tokens for the selected dark professional design system.
- Restyled target grid, buttons, status badges, settings controls, diagnostics actions, and report actions.
- Kept existing event behavior for save config, start/stop watcher, process refresh/add, data root selection, latest report/history/data-folder actions, diagnostic report generation, and diagnostic folder opening.
- Added dedicated watcher, enabled-target, latest-report, and report-stage labels so capture and failure states are visible instead of buried in one status line.
- Replaced the native light WinForms progress bar with a dark custom progress track/fill that supports idle, active, complete, and failed report states.
- Ran offscreen visual review and fixed compressed report buttons, hidden capture status, clipped settings controls, and the light OS progress bar.

### Verification

- build.ps1: exit 0; regenerated dist/FrameScopeMonitor-Setup.exe.
- FrameScopeDiagnosticsTests.exe: PASS.
- FrameScopeConfigStoreTests.exe: PASS.
- FrameScopeCapturePlannerTests.exe: PASS.
- FrameScopeReportProgressTests.exe: PASS.
- FrameScopePubgSimulatorTests.exe: PASS.
- node tests/chart-sampling-tests.js: PASS.
- dotnet build tools/FrameScopeRenderProbe/FrameScopeRenderProbe.csproj -c Release: PASS; 0 warnings; 0 errors.
- Offscreen UI screenshot: artifacts/ui-stage4-dashboard.png, 56617 bytes.
- Visual review: no obvious text overlap; report buttons readable; settings controls fit; capture/report/status hierarchy visible.
- Residual process check: no FrameScope, PresentMon, TslGame, or FakePresentMon process output.
- git diff --check via C:\Program Files\Git\cmd\git.exe: exit 0; only LF/CRLF warnings.

### Risks

- This stage validated the UI offscreen only; real game monitoring behavior is covered by previous simulator tests, not live PUBG.
- Installed %LOCALAPPDATA%/FrameScopeMonitor was not synced in this stage; final ship stage must reinstall/sync and verify payload hashes.
- WinForms limits mean this is a mature native dashboard, not a full custom GPU-accelerated UI framework.

### Remaining

- Next stage: report HTML/chart UI final polish under the same B dashboard direction, including zoom/pan/tooltip/share-ready layout checks.
- Later stages: architecture/performance review, full final validation, install sync, package hash verification, and real PUBG manual validation steps.

## Redesign Stage 3 - Diagnostic logs and reports

Status: completed on 2026-05-10.

### Goal

Add user-triggered and optional automatic diagnostic report generation with privacy redaction, diagnostic settings, async/low-overhead logging, and retention cleanup.

### Files / modules

- FrameScopeDiagnostics.cs
- FrameScopeConfigStore.cs
- FrameScopeNativeMonitor.cs
- framescope-config.example.json
- build.ps1
- tests/FrameScopeDiagnosticsTests.cs
- tests/FrameScopeConfigStoreTests.cs
- artifacts/ui-stage3-diagnostics.png

### Changes

- Added FrameScopeDiagnostics module for Markdown/JSON diagnostic reports.
- Report includes software, system, redacted settings, target detection, latest session, FPS summary, report generation status, current process memory, recent errors, and capture-chain fields.
- Added privacy redaction for user profile, username, and token/password/secret-like values.
- Added config fields: EnableVerboseLogs, EnablePerformanceDiagnosticsLogs, AutoGenerateDiagnosticReport, LogRetentionDays, MaxLogDiskMb.
- Added retention cleanup for diagnostic artifacts and trimming for watcher log.
- Made watcher log append asynchronous through FrameScopeDiagnostics.AppendLogAsync.
- Added verbose/performance diagnostic log paths gated by config switches.
- Added UI controls for diagnostic switches, retention days, disk cap, manual report generation, and diagnostic folder opening.
- Added CLI mode: FrameScopeMonitor.exe --generate-diagnostic-report.
- Optional auto diagnostic report generation now queues after a completed monitor run when enabled.

### Verification

- RED: FrameScopeDiagnosticsTests failed before implementation because config fields and FrameScopeDiagnostics were missing.
- GREEN: FrameScopeDiagnosticsTests: PASS.
- FrameScopeConfigStoreTests: PASS.
- FrameScopeCapturePlannerTests: PASS.
- FrameScopeReportProgressTests: PASS.
- FrameScopePubgSimulatorTests: PASS.
- node tests/chart-sampling-tests.js: PASS.
- build.ps1: exit 0; regenerated dist/FrameScopeMonitor-Setup.exe.
- Manual diagnostic CLI: exit 0; generated Markdown and JSON under %LOCALAPPDATA%/FrameScopeMonitorData/diagnostic-reports/diagnostic-20260510-021838.
- Diagnostic report section check: Software/Settings/Target Detection/Capture Chain present; JSON contains fpsSummary; markdown did not contain the Windows username.
- Offscreen UI screenshot: artifacts/ui-stage3-diagnostics.png, 44511 bytes, no screenshot error.
- Residual process check: no FrameScope, PresentMon, TslGame, or FakePresentMon process output.
- git diff --check via C:\Program Files\Git\cmd\git.exe: exit 0; only LF/CRLF warnings.

### Risks

- Real PUBG still not installed/tested on this machine; simulator evidence remains separate from real-game validation.
- UI now exposes diagnostic controls, but the full professional dashboard redesign is still pending.
- WinForms native progress bar still has a light OS style; this is deferred to the UI redesign stage.
- Installed %LOCALAPPDATA%/FrameScopeMonitor was not synced in this stage; final ship stage must reinstall/sync and verify payload hashes.

### Remaining

- Next stage: implement the selected B Professional Performance Dashboard UI with the design system instead of continuing small patches on the old layout.
- Later stages: report UI final polish, performance/architecture review, full final validation, install sync, and packaging.

## Stage 1 - Health baseline

Status: completed on 2026-05-10.

### Goal

Confirm current build, test, dependency, artifact, installed-app, and residual-process state before feature work.

### Files / modules

- build.ps1
- FrameScopeConfigStore.cs
- tests/FrameScopeConfigStoreTests.cs
- tools/FrameScopeRenderProbe/
- dist/
- %LOCALAPPDATA%/FrameScopeMonitor

### Results

- FrameScopeConfigStoreTests.exe: PASS.
- build.ps1: exit 0; generated dist/FrameScopeMonitor-Setup.exe.
- dotnet build tools/FrameScopeRenderProbe/FrameScopeRenderProbe.csproj -c Release: exit 0; 0 warnings; 0 errors.
- git diff --check: only LF/CRLF warnings for existing tracked files.
- Residual process check: no FrameScope, PresentMon, PUBG, or TslGame process found.
- Installed app exists, but installed exe timestamps are older than source build outputs; final stage must sync or reinstall.

### Risks

- Build refreshes binary/dist artifacts.
- Worktree was already dirty before this staged execution; preserve existing user/previous-agent changes.

### Remaining

- Stage 2: diagnose PUBG FPS chain with synthetic TslGame/RenderProbe evidence.
- Stage 3: add chart mode regression tests and fix raw/spike separation if needed.
- Final stage: sync installed app and verify package payload hashes.

## Stage 2 - PUBG FPS capture chain

Status: completed on 2026-05-10 with synthetic evidence; real PUBG not tested.

### Goal

Reduce PUBG silent FPS-missing failures by hardening target detection, PresentMon capture planning, and no-data diagnostics.

### Files / modules

- FrameScopeCapturePlanner.cs
- FrameScopeNativeMonitor.cs
- build.ps1
- tests/FrameScopeCapturePlannerTests.cs

### Changes

- Added FrameScopeCapturePlanner as a testable seam for target aliases, PUBG process-name capture, PresentMon arguments, and target-not-found diagnostics.
- PUBG now expands to TslGame and TslGame-Win64-Shipping.
- PUBG uses PresentMon --process_name for all aliases instead of locking capture to a transient PID.
- Monitor now trusts a live InitialTargetPid immediately. This reduces missed capture during PUBG startup/window timing transitions.
- Target-not-found diagnostics now include WindowWaitStatus and an actionable PUBG message covering timing, elevation, borderless/windowed fullscreen, and permission mismatch.

### Verification

- RED: FrameScopeCapturePlannerTests initially failed to compile because FrameScopeCapturePlanner did not exist.
- GREEN: FrameScopeCapturePlannerTests: PASS.
- FrameScopeConfigStoreTests: PASS.
- build.ps1: exit 0.
- Synthetic background TslGame.exe + fake PresentMon monitor session: monitor exit 0, Phase=done, PresentMonCaptureMode=process_name, PresentMonCaptureTarget=TslGame.exe;TslGame-Win64-Shipping.exe, PresentMonCsvRows=1.
- Residual check after synthetic run: no expected FrameScope/TslGame/FakePresentMon process output.

### Risks

- Real PUBG was not launched here. Final validation still needs user environment with PUBG, account/session, anti-cheat, and real rendering path.
- Synthetic PresentMon returns an artificial exit path, so it proves argument/status chain, not real ETW capture.

### Remaining

- Stage 3: chart raw/spike sampling regression and fix.
- Final stage: provide PUBG manual validation steps and exact status fields to inspect.

## Stage 3 - Chart raw vs spike sampling

Status: completed on 2026-05-10 with regression coverage; fixture did not reproduce current-mode mixing.

### Goal

Ensure "raw dense" and "spike preserving" chart modes use different render data and have visibly different point counts while preserving FPS spikes/drops.

### Files / modules

- FrameScopeReportGenerator.cs
- tests/chart-sampling-tests.js

### Changes

- Added a Node regression test that extracts the embedded production chart sampling functions from FrameScopeReportGenerator.cs.
- Test verifies raw mode draws dense fixture data, spike mode sharply reduces ordinary dense points, spike mode preserves low FPS drops and high FPS peaks, and PNG export names include the selected mode.

### Verification

- node tests/chart-sampling-tests.js: PASS.
- Fixture result: raw mode draws 10000 points; spike mode stays under 700 render points and keeps 7 FPS / 240 FPS extremes.

### Risks

- This stage validates data-processing behavior, not visual screenshot quality. Visual report QA remains Stage 7.
- Current code already had separated raw/spike logic from prior work; this stage locks it with a regression test instead of changing working behavior.

### Remaining

- Stage 4: move more capture/report responsibilities behind smaller modules.
- Stage 7: visually inspect generated report, zoom/pan, tooltip, export PNG.

## Stage 4 - Architecture seams

Status: completed on 2026-05-10 for capture planner and report progress seams.

### Goal

Reduce large-file coupling without a risky full rewrite.

### Files / modules

- FrameScopeCapturePlanner.cs
- FrameScopeReportProgress.cs
- FrameScopeNativeMonitor.cs
- FrameScopeReportGenerator.cs
- build.ps1
- tests/FrameScopeCapturePlannerTests.cs
- tests/FrameScopeReportProgressTests.cs

### Changes

- Extracted PUBG target alias and PresentMon planning logic into FrameScopeCapturePlanner.
- Extracted report progress field creation, JSON write/read, ETA, and status merge helpers into FrameScopeReportProgress.
- Updated build.ps1 so monitor and report generator compile shared seams.

### Verification

- FrameScopeCapturePlannerTests: PASS.
- FrameScopeReportProgressTests: PASS.
- build.ps1: exit 0.

### Risks

- FrameScopeNativeMonitor.cs and FrameScopeReportGenerator.cs are still large. This stage deliberately avoided a broad rewrite to keep behavior stable.

### Remaining

- Stage 6/7: UI/report visual refactor and interaction work.

## Stage 5 - Report generation progress

Status: completed on 2026-05-10 for progress plumbing and UI control compile verification.

### Goal

Show report generation phase, percent, ETA, error, and retry state without freezing the app.

### Files / modules

- FrameScopeNativeMonitor.cs
- FrameScopeReportGenerator.cs
- FrameScopeReportProgress.cs
- tests/FrameScopeReportProgressTests.cs

### Changes

- Report generator accepts --progress and writes report-progress.json at stages: 读取数据, 处理数据, 处理进程, 降采样, 生成图表, 完成, 生成失败.
- Watcher passes progress path to report generator and polls it while generator runs.
- status.json receives ReportProgressPhase, ReportProgressPercent, ReportProgressMessage, ReportProgressEtaSeconds, ReportProgressError, ReportCanRetry, ReportProgressPath.
- Main UI now has a report progress label and progress bar.

### Verification

- FrameScopeReportProgressTests: PASS.
- build.ps1: exit 0.
- Direct report generation on synthetic run with --progress: exit 0; progress Phase=完成; Percent=100; manifest parsed as diagnostic report with UTF-8.

### Risks

- Visual placement of the progress bar still needs Stage 6 screenshot review.
- Very short reports may jump from 1% to 100% quickly; long reports will show intermediate stages.

### Remaining

- Stage 6: main UI design pass and screenshot verification.

## Stage 6 - Main UI design pass

Status: completed on 2026-05-10 with screenshot verification.

### Goal

Keep the main app dark, compact, and readable while exposing report generation state.

### Files / modules

- FrameScopeNativeMonitor.cs
- artifacts/ui-stage6.png

### Changes

- Added bottom report generation label and progress bar.
- Kept current compact esports-style dark UI: target table, status pill, start/stop/report actions, data root, auto-open setting.

### Verification

- Offscreen UI screenshot command: exit 0.
- Screenshot written to artifacts/ui-stage6.png.
- Visual check: no obvious overlap, target table readable, progress UI visible, no foreground game interruption.

### Risks

- WinForms native ProgressBar uses OS styling, so the empty bar is lighter than the rest of the dark UI.
- More aggressive UI redesign would be higher risk and is deferred unless requested.

### Remaining

- Stage 7: report UI interaction, zoom/pan, tooltip, export screenshot validation.

## Stage 7 - Report UI and chart interaction

Status: completed on 2026-05-10 with browser interaction verification.

### Goal

Improve generated report readability and add local chart navigation without dropping full source data.

### Files / modules

- FrameScopeReportGenerator.cs
- tests/chart-sampling-tests.js
- artifacts/report-stage7.png
- artifacts/report-stage7-interaction.png

### Changes

- Added visible time-window state to the report chart renderer.
- Chart X axis now uses the visible time range instead of always mapping against full duration.
- Wheel over the plot zooms around cursor time.
- Left-drag pans the visible time window.
- Reset view returns to full run duration.
- Render stats now show current mode, visible window, drawn points, source points, and bucket count.
- Viewport drawing clips to chart area and rescales Y axis for the visible window.
- Raw/spike/trend sampling still reads from full data.js and only changes render points.

### Verification

- node tests/chart-sampling-tests.js: PASS.
- build.ps1: exit 0.
- Generated report for real local Valorant run: exit 0; report exists; manifest parsed with Node as UTF-8.
- Manifest evidence: hasFrameData=true; frames=848891; processes=114; frameCaptureStatus=captured; reportKind=full.
- Edge headless screenshot: artifacts/report-stage7.png, PNG valid, 199102 bytes.
- Playwright headless interaction test:
  - Initial: 保留尖峰 / 全时段.
  - Wheel zoom changed window to 2:43-21:54.
  - Drag pan changed window to 5:25-24:35.
  - Reset returned to 全时段.
  - Screenshot: artifacts/report-stage7-interaction.png.

### Risks

- Real browser export PNG click was not saved to disk automatically because browser download plumbing is separate; export handler remains wired and mode-aware.
- PUBG real gameplay was not tested in this stage.

### Remaining

- Stage 8: full regression, package verification, installed app sync, residual process check, final delivery notes.

## Stage 8 - Final verification and package delivery

Status: completed on 2026-05-10; installed app synced and packages rebuilt.

### Goal

Run final regression, verify generated packages, sync installed app, and record remaining real-environment validation gaps.

### Files / modules

- Full project build output
- dist/FrameScopeMonitor-Setup.exe
- dist/FrameScopeMonitor-Installer.zip
- dist/FrameScopeMonitor-LegacyCleanup.exe
- %LOCALAPPDATA%/FrameScopeMonitor
- artifacts/ui-final.png
- artifacts/ui-installed-final.png
- artifacts/synthetic-pubg-final-healthy/

### Verification

- FrameScopeConfigStoreTests: PASS.
- FrameScopeCapturePlannerTests: PASS.
- FrameScopeReportProgressTests: PASS.
- node tests/chart-sampling-tests.js: PASS.
- dotnet build tools/FrameScopeRenderProbe/FrameScopeRenderProbe.csproj -c Release: PASS, 0 warnings, 0 errors.
- build.ps1: exit 0; rebuilt dist/FrameScopeMonitor-Setup.exe.
- git diff --check: exit 0; only LF/CRLF warnings.
- Source GUI offscreen screenshot: artifacts/ui-final.png, 39828 bytes.
- Installed GUI offscreen screenshot: artifacts/ui-installed-final.png, 39828 bytes.
- Installed report generator regenerated local Valorant report: exit 0; hasFrameData=true; frames=848891; processes=114; frameCaptureStatus=captured; reportKind=full.
- Synthetic PUBG healthy chain with hidden TslGame.exe and fake PresentMon:
  - monitorExit=0.
  - phase=done.
  - PresentMonCaptureMode=process_name.
  - PresentMonCaptureTarget=TslGame.exe;TslGame-Win64-Shipping.exe.
  - PresentMonCsvRows=1.
  - FrameCaptureStatus=captured.
  - PresentMonExitedEarly=false.
  - PresentMonForcedStop=false.
- Residual process check: no FrameScope, PresentMon, TslGame, PUBG, or FakePresentMon process remained.

### Installed app sync

- Synced latest binaries into %LOCALAPPDATA%/FrameScopeMonitor.
- Preserved framescope-config.json, framescope-history.jsonl, framescope-watcher.log, and data root.
- Hash check: source build, dist payload, and installed binaries all match.

### Package output

- dist/FrameScopeMonitor-Installer.zip
  - Size: 529882 bytes.
  - SHA256: 41C970021957D3798DB3D54FCEAD2472AE5A45D38C51757646142D1A714574D3.
- dist/FrameScopeMonitor-Setup.exe
  - Size: 528896 bytes.
  - SHA256: A464EF51B9727BBDB74ACA13338D597645CC9F46AEF5C67E0EC2C7FC01816BC9.
- dist/FrameScopeMonitor-LegacyCleanup.exe
  - Size: 29184 bytes.
  - SHA256: FD062904BA72A2A04ABB5952F8C403CC0CBF7485594011E83712C496DE620F39.

### Verified games / data

- Valorant historical run: report regeneration and manifest verified.
- PUBG: synthetic TslGame/PresentMon chain verified only. Real PUBG gameplay was not launched here.

### Remaining risk

- Real PUBG still needs user-side gameplay validation with actual game, account/session, anti-cheat, privilege level, and fullscreen/borderless mode.
- Export PNG click handler is code-wired and mode-aware, but browser download file save was not separately captured during headless tests.

### Manual PUBG validation steps

1. Start FrameScope installed app from %LOCALAPPDATA%/FrameScopeMonitor/FrameScopeMonitor.exe.
2. Keep PUBG target enabled.
3. Start monitoring before or after PUBG reaches the final game window.
4. Run one short match/training session.
5. After exit, open the latest PUBG run folder.
6. Check status.json fields:
   - Phase should be done.
   - PresentMonCaptureMode should be process_name.
   - PresentMonCaptureTarget should include TslGame.exe and TslGame-Win64-Shipping.exe.
   - PresentMonCsvRows should be greater than 0.
   - FrameCaptureStatus should be captured.
7. If FrameCaptureStatus is not captured, send status.json, summary.json, presentmon.stderr.log, and presentmon.csv size.

## Redesign run - Stage 1 - Direction, health, and design system

Status: completed on 2026-05-10.

### Goal

Lock the selected modern UI direction, capture current health baseline, and create a design system before source implementation.

### Files / modules

- docs/FrameScopeMonitor-design-system.md
- docs/FrameScopeMonitor-progress.md
- docs/FrameScopeMonitor-next-prompt.md
- dist/
- %LOCALAPPDATA%/FrameScopeMonitor

### User decision

- Selected UI direction: B - Professional Performance Dashboard.

### Changes

- Created design system document for the selected direction.
- Recorded plan-design-review outcome: existing UI is useful but still ad hoc; target design is dark, data-first, compact, and mature.
- Locked color tokens, typography, spacing, button/status/table/chart rules, diagnostics placement, and animation limits.

### Health verification

- FrameScopeConfigStoreTests.exe: PASS.
- FrameScopeCapturePlannerTests.exe: PASS.
- FrameScopeReportProgressTests.exe: PASS.
- node tests/chart-sampling-tests.js: PASS.
- build.ps1: PASS; rebuilt dist/FrameScopeMonitor-Setup.exe.
- dotnet build tools/FrameScopeRenderProbe/FrameScopeRenderProbe.csproj -c Release: PASS, 0 warnings, 0 errors.
- git diff --check: only LF/CRLF warnings.
- Residual process check: no FrameScope, PresentMon, PUBG, TslGame, or FakePresentMon processes.

### Package state after health build

- dist/FrameScopeMonitor-Installer.zip SHA256: DFE7DFC1BEDAB22D50F42FCAB198BA147A359012316837A8FCE60383279090D7.
- dist/FrameScopeMonitor-Setup.exe SHA256: 8AAB71296AC9FD0EEE4BC5620FC70C5E5C818C48E55499F6B28B8D7ED22C8F33.
- dist payload hashes match the newly built source binaries.
- Installed binaries under %LOCALAPPDATA%/FrameScopeMonitor do not match the newly built source binaries after this health build. Final stage must sync or reinstall before delivery.

### Risks / blockers

- 修改报告.txt is empty, so active requirements come from chat plus this progress file.
- Real PUBG is not installed on this machine. PUBG validation must use simulator/mock until user performs real game validation.
- Current worktree is dirty from previous staged work; preserve existing changes.

### Verification-before-completion check

- Evidence exists for this stage: tests/build/hash/residual process checks plus design system document.
- No source code was modified in this stage.

### Remaining

- Stage 2: implement PUBG simulator/demo validation for process, window, fake FPS data, report generation, and UI/status chain.

## Redesign run - Stage 2 - PUBG simulator and mock FPS chain

Status: completed on 2026-05-10 with simulator evidence; real PUBG not tested.

### Goal

Build a reusable PUBG simulator/demo so this machine can validate PUBG process recognition, window recognition, fake FPS data capture, no-data diagnostics, status chain, and report generation without real PUBG installed.

### Files / modules

- tools/FrameScopePubgSimulator/FrameScopePubgSimulationCommon.cs
- tools/FrameScopePubgSimulator/PubgGameSimulator.cs
- tools/FrameScopePubgSimulator/FakePresentMon.cs
- tools/FrameScopePubgSimulator/Run-PubgSimulation.ps1
- tools/FrameScopePubgSimulator/README.md
- tests/FrameScopePubgSimulatorTests.cs
- docs/FrameScopeMonitor-progress.md
- docs/FrameScopeMonitor-next-prompt.md

### Changes

- Added offscreen `TslGame.exe` simulator with a real main window handle and PUBG-like title.
- Added fake PresentMon that accepts FrameScope's normal PresentMon args and writes controlled `presentmon.csv` data.
- Added scenarios: stable, fluctuating, spikes, no-data, missing-csv.
- Added one-command PowerShell runner that builds simulator binaries into an isolated artifact bin, launches the game simulator, runs `FrameScopeMonitor.exe --monitor-session`, runs `FrameScopeReportGenerator.exe`, and emits JSON evidence.
- Added simulator behavior tests for aliases, stable FPS rows, spike/drop rows, header-only no-data, and monitor-session args.
- Fixed fake PresentMon simulator bug: `--stop_existing_session` is a normal startup arg, not a termination request. Only `--terminate_existing_session` now stops fake capture.
- Fixed simulator runner bug: manifest is parsed by safe field extraction instead of PowerShell `ConvertFrom-Json`, because current report manifest can contain legacy mojibake text that PowerShell rejects.
- Fixed simulator runner bug: each run now compiles to its own artifact `bin`, avoiding locked `TslGame.exe` when multiple scenarios run close together.

### TDD / diagnostic evidence

- RED: `FrameScopePubgSimulatorTests.cs` failed to compile because `FrameScopePubgSimulationCommon` did not exist.
- GREEN: `FrameScopePubgSimulatorTests.exe`: PASS.
- First simulator run reproduced a real fake-chain failure: no `presentmon.csv`; root cause was fake PresentMon treating startup `--stop_existing_session` as stop.
- After fix, simulator produces captured FPS data and generated reports.

### Simulator verification

- `spikes` with InitialPid:
  - monitorExit=0.
  - reportExit=0.
  - phase=done.
  - presentMonCaptureMode=process_name.
  - presentMonCaptureTarget=TslGame.exe;TslGame-Win64-Shipping.exe.
  - presentMonCsvRows=240.
  - frameCaptureStatus=captured.
  - hasFrameData=true.
  - frames=240.
  - reportKind=full.
  - targetHasMainWindow=true.
- `stable` with InitialPid:
  - monitorExit=0.
  - reportExit=0.
  - frameCaptureStatus=captured.
  - targetWindowTitle=PLAYERUNKNOWN'S BATTLEGROUNDS - PUBG: BATTLEGROUNDS.
  - targetHasMainWindow=true.
- `fluctuating -NoInitialPid`:
  - monitorExit=0.
  - reportExit=0.
  - presentMonCaptureMode=process_name.
  - frameCaptureStatus=captured.
  - hasFrameData=true.
  - usedInitialPid=false.
  - targetHasMainWindow=true.
- `no-data`:
  - monitorExit=0.
  - reportExit=0.
  - presentMonCsvRows=0.
  - frameCaptureStatus=no-presentmon-rows.
  - hasFrameData=false.
  - reportKind=diagnostic.
  - targetHasMainWindow=true.

### Risks / blockers

- This validates FrameScope's PUBG-like process/window/status/report chain, not real PUBG ETW capture under anti-cheat.
- Real PUBG still needs user-side gameplay validation.
- Simulator creates temporary artifact runs under `artifacts/pubg-simulator/`.

### Verification-before-completion check

- Evidence exists for process recognition, window recognition, FPS CSV data path, no-data diagnostic path, report generation, and no residual process cleanup.

### Remaining

- Stage 3: add UI "generate logs/report" diagnostic package, log settings, retention policy, and privacy redaction.

## Redesign run - Stage 6 - Reference UI shell, page router, rounded system

Status: completed on 2026-05-10.

### Goal

Replace old one-screen WinForms visual shell with the reference-image direction: dark tech dashboard, left navigation, top status cards, page router, persistent report progress, Chinese UI text, rounded cards/buttons/inputs.

### Files / modules

- FrameScopeNativeMonitor.cs
- docs/FrameScopeMonitor-progress.md
- docs/FrameScopeMonitor-next-prompt.md
- artifacts/ui-overview-rounded-final.png
- artifacts/ui-targets-rounded-final.png
- artifacts/ui-settings-rounded-final.png
- artifacts/ui-reports-rounded-final-v2.png
- artifacts/ui-live-rounded-final.png

### Changes

- Added reference-style shell:
  - left sidebar navigation: 概览、实时监控、监控目标、报告、设置、关于我们.
  - top title/status area with Chinese monitoring/software status cards.
  - central page router using `ShowPage(...)`.
  - persistent bottom report generation card with percent/phase/ETA/status and open report directory action.
- Added initial pages:
  - 概览: target count, capture chain, recent capture/report/output state, quick actions.
  - 监控目标: editable target grid, process input, refresh/add/save/start/stop, PUBG/TslGame hint.
  - 设置: monitoring/report/diagnostic/data-root controls with save wiring.
  - 报告: report list/detail/actions/support package/open report/open output folder.
  - 实时监控: demo-labeled chart placeholders, live metrics, capture chain, log panel.
- Added unified rounded UI system:
  - `FrameScopeRoundedDrawing`.
  - `FrameScopeCardPanel` rounded glass cards.
  - `FrameScopeRoundedTableLayoutPanel` rounded section panels.
  - `FrameScopeRoundedButton` self-painted rounded buttons.
  - rounded status cards, inputs, list/table surfaces, progress track/fill.
- Fixed page-router compatibility:
  - `RefreshProcessList`, `AddSelectedProcess`, `BrowseDataRoot`, `OpenDataRoot`, and `SaveConfigFromGrid` now handle pages where old controls are not mounted.
  - target status card text is Chinese.
- Added offscreen page screenshot support:
  - `FrameScopeMonitor.exe --ui-page <overview|targets|settings|reports|live> --ui-screenshot <path>`.

### Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `tests\FrameScopeDiagnosticsTests.exe`: PASS.
- `tests\FrameScopeConfigStoreTests.exe`: PASS.
- `tests\FrameScopeCapturePlannerTests.exe`: PASS.
- `tests\FrameScopeReportProgressTests.exe`: PASS.
- `tests\FrameScopePubgSimulatorTests.exe`: PASS.
- `node tests\chart-sampling-tests.js`: PASS.
- `dotnet build tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- `C:\Program Files\Git\cmd\git.exe diff --check`: only LF/CRLF warnings.
- Residual process check: `NO_RESIDUAL_PROCESSES`.
- Offscreen screenshots generated for overview, targets, settings, reports, and live pages.

### Package state

- Rebuilt `dist\FrameScopeMonitor-Setup.exe`.
- SHA256: `EAF83F98BBCB04C1F93ED6CE4FC1E2EA787CC426EA5827ED56B5036B0A0F39F9`.
- Installed app under `%LOCALAPPDATA%\FrameScopeMonitor` is not yet synced in this stage. Final ship stage must reinstall or sync.

### Design review notes

- Reference direction is now visible at first glance: dark blue-black shell, cyan/blue/green/purple accents, glowing rounded cards, left nav, bottom report progress.
- User requested all UI rounded; this stage replaced rectangular cards/buttons with unified rounded drawing.
- Native OS window chrome and some system controls such as grid scrollbars are still Windows-native. Main application surfaces/buttons/cards/inputs are rounded.
- Real-time charts are still demo-labeled placeholders; real data chart wiring remains next-stage work.

### Risks / blockers

- Real PUBG still not tested because this machine has no PUBG.
- Report page actions currently use latest-report/history/support-package logic; selected-row-specific regenerate remains future work.
- Settings reset-default button still reports unavailable and needs real reset implementation in settings deepening stage.

### Verification-before-completion check

- Stage has fresh build/test/screenshot evidence.
- No claim made that real PUBG works; only simulator-backed PUBG chain has been validated in earlier stage.

### Remaining

- Next stage: deepen real page behavior:
  - settings reset defaults + save/reload proof.
  - report center selected report detail/actions/regenerate.
  - live page real/latest data stream and explicit demo mode.
  - continue UI rounding/polish where native controls still look too system-like.

## Redesign run - Stage 7 - Page behavior deepening and rounded UI verification

Status: completed on 2026-05-10.

### Goal

Make the new reference-style UI behave like a real tool instead of a visual shell: settings save/reset, report row details/actions, live page latest-run data, Chinese status text, rounded visual system verification.

### Files / modules

- FrameScopeNativeMonitor.cs
- docs/FrameScopeMonitor-progress.md
- docs/FrameScopeMonitor-next-prompt.md
- artifacts/ui-overview-stage7-verified-rounded.png
- artifacts/ui-settings-stage7-verified-rounded.png
- artifacts/ui-reports-stage7-verified-rounded.png
- artifacts/ui-live-stage7-verified-rounded.png

### Changes

- Settings page:
  - `恢复默认` now creates real default config through `FrameScopeConfigStore.CreateDefaultConfig()`.
  - `保存设置` saves config, reloads it, refreshes target status, and updates status text.
  - settings inputs are hosted in rounded dark input surfaces.
- Reports page:
  - report list rows are tied to real `FrameScopeHistoryEntry` records.
  - row selection updates a real details panel.
  - `打开报告`, `打开输出目录`, `导出支持包`, `重新生成`, `刷新列表` are wired to selected report logic.
  - missing file/run cases now show Chinese status instead of silent no-op.
- Live page:
  - reads latest run from history or latest run directories.
  - parses tail of `presentmon.csv` for FPS/frame-time preview.
  - parses tail of `system-samples.csv` for CPU/GPU/memory preview.
  - if latest run has no FPS rows, UI shows no-data state and log warning instead of fake FPS.
  - demo data is only used when no real run exists.
- UI polish:
  - verified rounded dark-tech cards/buttons/inputs/status/progress across overview/settings/reports/live screenshots.
  - localized visible progress message for PresentMon no-frame-data path.

### Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `tests\FrameScopeDiagnosticsTests.exe`: PASS.
- `tests\FrameScopeConfigStoreTests.exe`: PASS.
- `tests\FrameScopeCapturePlannerTests.exe`: PASS.
- `tests\FrameScopeReportProgressTests.exe`: PASS.
- `tests\FrameScopePubgSimulatorTests.exe`: PASS.
- `node tests\chart-sampling-tests.js`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- `C:\Program Files\Git\cmd\git.exe diff --check`: only LF/CRLF warnings.
- Residual process check: `NO_RESIDUAL_PROCESSES`.
- Offscreen screenshots generated and visually checked:
  - `artifacts\ui-overview-stage7-verified-rounded.png` (89740 bytes)
  - `artifacts\ui-settings-stage7-verified-rounded.png` (75621 bytes)
  - `artifacts\ui-reports-stage7-verified-rounded.png` (87538 bytes)
  - `artifacts\ui-live-stage7-verified-rounded.png` (86454 bytes)

### Package state

- Rebuilt `dist\FrameScopeMonitor-Setup.exe`.
- SHA256: `A7B62083229C6B4C5D37E6641AE5CA40853CAFD886A4CBDCB80DC765E46AA303`.
- Installed app under `%LOCALAPPDATA%\FrameScopeMonitor` is not yet synced in this stage. Final ship stage must reinstall or sync.

### Design review notes

- The first viewport now matches the requested reference direction much more closely: dark blue-black shell, cyan/blue/green/purple accents, glowing rounded cards, left navigation, top status cards, bottom report progress.
- User requested all UI rounded; main app surfaces, cards, buttons, input hosts, status cards, and progress surfaces are rounded.
- Remaining native Windows pieces: outer OS title bar, standard checkbox glyphs, some list/grid internals and scrollbars. These are not custom-painted yet.
- Live page correctly shows a no-FPS state for the latest PUBG-like run because `presentmon.csv` has no FPS data; no fake FPS was inserted.

### Risks / blockers

- Real PUBG still cannot be tested on this machine because PUBG is not installed.
- Simulator-backed PUBG chain remains the verified substitute; real ETW/anti-cheat/fullscreen behavior needs user-side validation.
- `FrameScopeNativeMonitor.cs` is now large; later architecture cleanup should split theme/components/page builders/data readers.
- Full report HTML/chart UI still needs the next stage: zoom/pan polish, shareable visual style, mode differences, tooltip/readability.

### Verification-before-completion check

- Stage 7 has fresh build, tests, screenshot, diff-check, and residual-process evidence.
- No claim made that real PUBG works. Only simulator/local-run UI behavior verified.

### Remaining

- Next stage: report HTML/chart UI deepening:
  - make generated report visual style match the dark rounded dashboard.
  - verify raw dense vs spike-preserving modes still use distinct arrays/point counts.
  - test wheel zoom, drag pan, tooltip readability, local time-window inspection.
  - keep full data sidecar; optimize rendering without deleting data.

## Redesign run - Stage 8 - Report HTML UI and chart interaction

Status: completed on 2026-05-10.

### Goal

Modernize the generated HTML report to match the reference dark rounded dashboard style, verify chart zoom/pan/tooltip/export behavior, and prove raw dense vs spike-preserving modes are truly different.

### Files / modules

- FrameScopeReportGenerator.cs
- tests/chart-sampling-tests.js
- docs/FrameScopeMonitor-progress.md
- docs/FrameScopeMonitor-next-prompt.md
- artifacts/report-stage8-after.png
- artifacts/report-stage8-export.png
- artifacts/report-stage8-nodata.png

### Changes

- Report visual system:
  - replaced older square/flat report surfaces with rounded dark blue-black dashboard surfaces.
  - increased main report/card/chart/button/input radii: main shell 22px, chart surface 18px, tool buttons 13px.
  - added reference-style gradients, cyan borders, soft glow, rounded top report card, rounded toolbar, rounded chart frame.
  - turned metric rings into card-like rounded stat tiles so report matches the app UI direction.
- Chart rendering:
  - added `drawSpikeMarkers()` for spike mode. It marks visible FPS peaks/drops without changing full data storage.
  - chart background now uses a dark blue-black gradient and brighter cyan frame/grid.
  - exported PNG now uses a rounded chart frame and dark dashboard background.
- Tests:
  - extended `tests/chart-sampling-tests.js` to assert:
    - spike mode does not reuse raw data.
    - wheel zoom and drag pan wiring exists.
    - report has rounded chart shell.
    - spike markers exist.
    - exported PNG uses rounded framing.

### Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `tests\FrameScopeDiagnosticsTests.exe`: PASS.
- `tests\FrameScopeConfigStoreTests.exe`: PASS.
- `tests\FrameScopeCapturePlannerTests.exe`: PASS.
- `tests\FrameScopeReportProgressTests.exe`: PASS.
- `tests\FrameScopePubgSimulatorTests.exe`: PASS.
- `node tests\chart-sampling-tests.js`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- `C:\Program Files\Git\cmd\git.exe diff --check`: only LF/CRLF warnings.
- Residual FrameScope/PresentMon/TslGame/FakePresentMon process check: `NO_RESIDUAL_PROCESSES`.
- Playwright headless report screenshot:
  - `artifacts\report-stage8-after.png`
  - browser console/page errors: none.
  - title: `FrameScope - VALORANT-Win64-Shipping.exe 性能报告`.
  - tabs: `帧率`, `性能图表`, `系统占用`, `后台进程`, `IO/温度`.
  - computed radii: main 22px, chart 18px, button 13px.
- Chart interaction browser verification:
  - spike mode: drawn 5,334 points / raw 58,384 points.
  - raw dense mode: drawn 58,384 points / raw 58,384 points.
  - wheel zoom changed visible window from full range to `2:53-22:03`.
  - drag pan changed visible window to `5:25-24:35`.
  - tooltip became visible on hover.
  - PNG export generated `artifacts\report-stage8-export.png`, 2,639,508 bytes.
- No-FPS diagnostic report browser verification:
  - `artifacts\report-stage8-nodata.png`.
  - browser console/page errors: none.
  - alert text clearly states PresentMon no-data status, CSV bytes, and target `TslGame.exe`.

### Package state

- Rebuilt `dist\FrameScopeMonitor-Setup.exe`.
- SHA256: `5538291057474169CC6359FCABC4483CB2ECA4F815342E0907625C4CE864F82F`.
- Installed app under `%LOCALAPPDATA%\FrameScopeMonitor` is not yet synced in this stage. Final ship stage must reinstall or sync.

### Design review notes

- Generated reports now visually align with the app shell: dark dashboard, translucent rounded panels, cyan/green/yellow/red/purple data colors, rounded chart and action controls.
- The report is still dense when FPS data is very noisy, but raw vs spike is now measurably distinct and spike mode marks visible peaks/drops.
- Browser validation used headless Playwright Chromium so it did not steal foreground focus.

### Risks / blockers

- Real PUBG still cannot be tested because PUBG is not installed.
- Playwright Chromium headless shell was installed into `%LOCALAPPDATA%\ms-playwright` to enable screenshot/interaction verification.
- `FrameScopeNativeMonitor.cs` remains large and should be split in the next architecture cleanup stage.
- Installed app is not yet updated; current installer contains the changes but has not been applied to `%LOCALAPPDATA%\FrameScopeMonitor`.

### Verification-before-completion check

- Stage 8 has fresh build, full tests, report generation, browser screenshot, chart interaction, PNG export, no-data report, diff-check, and residual-process evidence.
- No claim made that real PUBG works.

### Remaining

- Next stage: architecture cleanup and final ship prep:
  - split `FrameScopeNativeMonitor.cs` UI/theme/page/data helper modules where safe.
  - preserve existing behavior and tests while reducing giant-file risk.
  - final stage after cleanup: rebuild, test, sync/install local app, verify installer payload/hash, and report manual PUBG validation steps.

## Redesign run - Stage 9 - Architecture cleanup and installed ship verification

Status: completed on 2026-05-10.

### Goal

Reduce giant-file risk with a low-risk module extraction, rebuild/package, sync the installed app, and verify the installed runtime without disturbing foreground use.

### Files / modules

- FrameScopeNativeMonitor.cs
- FrameScopeUiComponents.cs
- build.ps1
- docs/FrameScopeMonitor-progress.md
- docs/FrameScopeMonitor-next-prompt.md
- install-backups/stage9-20260510-052436/
- artifacts/installed-overview-stage9.png
- artifacts/report-stage9-installed-smoke.png

### Changes

- Added `FrameScopeUiComponents.cs`.
- Moved these standalone UI helpers out of `FrameScopeNativeMonitor.cs`:
  - `FrameScopeRoundedDrawing`
  - `FrameScopeCardPanel`
  - `FrameScopeRoundedTableLayoutPanel`
  - `FrameScopeRoundedButton`
  - `FrameScopeLiveSnapshot`
  - `FrameScopeMiniChartPanel`
- Updated `build.ps1` to compile `FrameScopeUiComponents.cs` into `FrameScopeMonitor.exe`.
- Reduced `FrameScopeNativeMonitor.cs` from 4591 lines after extraction; UI helper module is now 258 lines.
- Kept behavior and UI direction unchanged.

### Installed app sync

- Confirmed no FrameScope / PresentMon / TslGame / FakePresentMon process was running before sync.
- Backed up current install files to:
  - `install-backups\stage9-20260510-052436`
- Synced built files into:
  - `%LOCALAPPDATA%\FrameScopeMonitor`
- Preserved installed `framescope-config.json`, history, and logs.
- Verified installed core exe hashes match source build:
  - `FrameScopeMonitor.exe`: match
  - `FrameScopeProcessSampler.exe`: match
  - `FrameScopeSystemSampler.exe`: match
  - `FrameScopeReportGenerator.exe`: match
  - `FrameScopeUninstaller.exe`: match
  - `FrameScopeLegacyCleanup.exe`: match
  - `tools\PresentMon-2.4.1-x64.exe`: match
- Verified installer payload hashes match built source exe files.

### Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `tests\FrameScopeDiagnosticsTests.exe`: PASS.
- `tests\FrameScopeConfigStoreTests.exe`: PASS.
- `tests\FrameScopeCapturePlannerTests.exe`: PASS.
- `tests\FrameScopeReportProgressTests.exe`: PASS.
- `tests\FrameScopePubgSimulatorTests.exe`: PASS.
- `node tests\chart-sampling-tests.js`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- Offscreen UI screenshots from rebuilt source:
  - overview 89740 bytes
  - targets 89988 bytes
  - settings 75621 bytes
  - reports 87538 bytes
  - live 86454 bytes
- Installed app offscreen screenshot:
  - `artifacts\installed-overview-stage9.png`, 89740 bytes.
- Installed report generator on large Valorant run:
  - frames 848891
  - rawPresentMonRows 848898
  - hasFrameData true
  - reportKind full
  - processes 114
  - processSamples 13466
  - systemSamples 1473
- Installed report browser smoke:
  - `artifacts\report-stage9-installed-smoke.png`, 723823 bytes.
  - browser console/page errors: none.
  - wheel zoom changed visible range.
- `C:\Program Files\Git\cmd\git.exe diff --check`: only LF/CRLF warnings.
- Final residual process check: `NO_RESIDUAL_PROCESSES`.

### Package state

- Rebuilt `dist\FrameScopeMonitor-Setup.exe`.
- Setup SHA256: `3115494B2100444A50EAC38814FAF09D0CCFD4821BC8095B34C76F422FAEFB68`.
- `dist\FrameScopeMonitor-Installer.zip` SHA256: `ADF20112748A42CC91939AEA85C4257A52C65F2C4D16410C1D2CFF785DEED822`.
- Package files:
  - `dist\FrameScopeMonitor-Setup.exe`, 556544 bytes.
  - `dist\FrameScopeMonitor-Installer.zip`, 557864 bytes.
  - `dist\FrameScopeMonitor-LegacyCleanup.exe`, 29184 bytes.

### Real PUBG status

- Real PUBG was not tested because PUBG is not installed on this machine.
- PUBG simulator/mock chain is tested through `FrameScopePubgSimulatorTests.exe`.
- Manual real PUBG validation still required:
  1. Install/sync this build.
  2. Start FrameScope Monitor normally.
  3. Confirm PUBG/TslGame target is enabled in `监控目标`.
  4. Start monitoring before launching PUBG.
  5. Launch PUBG in borderless, fullscreen, and windowed modes if possible.
  6. Play 3-5 minutes in each mode.
  7. Confirm UI shows capture state instead of silently blank.
  8. After exit, open latest report and check `presentmon.csv`, `status.json`, and `charts\framescope-interactive-manifest.json`.
  9. If FPS is missing, export support package from `报告` page and inspect `FrameCaptureStatus`, `FrameCaptureMessage`, PresentMon stdout/stderr tails, target window fields, and permission hints.

### Risks / blockers

- `FrameScopeNativeMonitor.cs` still contains page builders and monitor logic. It is smaller, but a future cleanup should split page/data readers further.
- Installer itself still opens a visible installer UI and auto-starts app; this stage used manual sync to avoid stealing foreground focus.
- Real PUBG ETW/anti-cheat/fullscreen behavior remains user-environment validation.

### Verification-before-completion check

- Stage 9 has fresh build, full tests, source UI screenshots, installed UI screenshot, installed report generation, installed report browser smoke, hash checks, payload checks, and residual-process evidence.
- No real PUBG success claim made.

## Final verification refresh - 2026-05-10 05:45

Status: completed.

### Goal

Refresh verification after context handoff, make sure the installed app and latest package match the final source, and fix any small polish issue found during visual review.

### Changes in this refresh

- `FrameScopeNativeMonitor.cs`
  - Fixed the UI version label fallback from `v0.0.0.0` to `v1.1.1`.
- `FrameScopeDiagnostics.cs`
  - Fixed diagnostic report software version fallback from assembly `0.0.0.0` to `1.1.1`.
- Rebuilt and re-synced installed binaries into:
  - `%LOCALAPPDATA%\FrameScopeMonitor`
- Backed up the previous installed files to:
  - `install-backups\final-versionfix-20260510-054315`

### Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `tests\FrameScopeDiagnosticsTests.exe`: PASS.
- `tests\FrameScopeConfigStoreTests.exe`: PASS.
- `tests\FrameScopeCapturePlannerTests.exe`: PASS.
- `tests\FrameScopeReportProgressTests.exe`: PASS.
- `tests\FrameScopePubgSimulatorTests.exe`: PASS.
- `node tests\chart-sampling-tests.js`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- Installed UI offscreen screenshots:
  - `artifacts\final-installed-ui-overview.png`
  - `artifacts\final-installed-ui-targets.png`
  - `artifacts\final-installed-ui-settings.png`
  - `artifacts\final-installed-ui-reports.png`
  - `artifacts\final-installed-ui-live.png`
  - `artifacts\final-installed-ui-overview-version.png`
- Visual check:
  - Main UI uses dark rounded dashboard panels/buttons/status cards.
  - Overview version now shows `v1.1.1`.
  - Report UI uses rounded dark chart layout and keeps chart controls readable.
- Diagnostic report CLI smoke:
  - Exit code 0.
  - Latest JSON report version: `1.1.1`.
- Installed report generator smoke on large Valorant run:
  - elapsed: 7335 ms
  - frames: 848891
  - rawPresentMonRows: 848898
  - hasFrameData: true
  - reportKind: full
  - processes: 114
  - processSamples: 13466
  - systemSamples: 1473
  - progress phase: 完成
  - progress percent: 100
- Browser report smoke with bundled Playwright:
  - console/page errors: none.
  - `原始密集`: 58384 drawn / 58384 raw.
  - `保留尖峰`: 5334 drawn / 58384 raw.
  - wheel zoom changed range to `2:35-21:46`.
  - screenshot: `artifacts\final-report-browser-smoke-after-versionfix.png`.
- Source vs installed hashes: all core exe files and PresentMon matched.
- Source vs package payload hashes: all installer payload exe files and PresentMon matched.
- Legacy cleanup source vs `dist\FrameScopeMonitor-LegacyCleanup.exe`: matched.
- `git diff --check`: only LF/CRLF warnings.
- Residual process check: no FrameScope / PresentMon / TslGame / FakePresentMon processes.

### Package state

- Setup:
  - `dist\FrameScopeMonitor-Setup.exe`
  - SHA256: `5AC23934F92BE9FF061205A0FFD3567BB446732BB9A6D4D02241980D9ECD573C`
- Zip:
  - `dist\FrameScopeMonitor-Installer.zip`
  - SHA256: `0F452BCA010353AF72E745F8D496D8F4C4D0F3BDCD16B5B7A0F65AD7E52517F0`
- Legacy cleanup:
  - `dist\FrameScopeMonitor-LegacyCleanup.exe`

### Real PUBG status

- Real PUBG was not tested because PUBG is not installed on this machine.
- PUBG simulator/mock regression test passed through `FrameScopePubgSimulatorTests.exe`.
- Real PUBG ETW, anti-cheat, fullscreen, borderless, and windowed behavior still needs manual validation on a PUBG machine.

### Remaining risk

- `FrameScopeNativeMonitor.cs` still contains page builders and monitor logic. Further splitting is optional and should be done in small verified steps.
- Installer UI is still visible if run manually; this refresh used direct installed-file sync to avoid stealing foreground focus.

## Stage 10 - Live data architecture split

Status: completed on 2026-05-10.

### Goal

Continue architecture cleanup with a small, low-risk split: move real-time monitoring data loading out of the main WinForms file without changing UI behavior or visual style.

### Files / modules

- `FrameScopeNativeMonitor.cs`
- `FrameScopeLiveData.cs`
- `build.ps1`
- `docs/FrameScopeMonitor-progress.md`
- `docs/FrameScopeMonitor-next-prompt.md`
- `install-backups/stage10-livedata-20260510-055406/`
- `artifacts/stage10-live-data-split-live.png`
- `artifacts/stage10-live-data-split-overview.png`

### Changes

- Changed `FrameScopeNativeMonitor` to a partial static class.
- Added `FrameScopeLiveData.cs`.
- Moved live data loading methods into `FrameScopeLiveData.cs`:
  - `LoadLiveSnapshot`
  - `CreateDemoLiveSnapshot`
  - `FindLatestRunForLive`
  - `TryPopulatePresentMonTail`
  - `TryPopulateSystemTail`
  - `ApplyFpsStats`
- Updated `build.ps1` to compile `FrameScopeLiveData.cs`.
- Kept UI layout, colors, rounded styling, and behavior unchanged.
- `FrameScopeNativeMonitor.cs` is now 4103 lines.
- `FrameScopeLiveData.cs` is 160 lines.

### Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `tests\FrameScopeDiagnosticsTests.exe`: PASS.
- `tests\FrameScopeConfigStoreTests.exe`: PASS.
- `tests\FrameScopeCapturePlannerTests.exe`: PASS.
- `tests\FrameScopeReportProgressTests.exe`: PASS.
- `tests\FrameScopePubgSimulatorTests.exe`: PASS.
- `node tests\chart-sampling-tests.js`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- UI smoke screenshots:
  - `artifacts\stage10-live-data-split-live.png`, 86433 bytes.
  - `artifacts\stage10-live-data-split-overview.png`, 89720 bytes.
- Live page behavior check:
  - The page still reads the latest real session.
  - If the latest real session has no FPS data, the UI shows the no-FPS state instead of silently switching to fake data.
- Installed app sync:
  - Backed up previous installed files to `install-backups\stage10-livedata-20260510-055406`.
  - Synced binaries into `%LOCALAPPDATA%\FrameScopeMonitor`.
  - Source/install hashes matched for core binaries and PresentMon.
  - Source/package payload hashes matched for installer payload and legacy cleanup artifact.
- `git diff --check`: only LF/CRLF warnings.
- Residual process check: no FrameScope / PresentMon / TslGame / FakePresentMon processes.

### Package state

- Setup:
  - `dist\FrameScopeMonitor-Setup.exe`
  - SHA256: `8029C8BF99C3F02FE9D0F8DAC312229B1BFD8CD46DDC3589650A1A43D04F6D5C`
- Zip:
  - `dist\FrameScopeMonitor-Installer.zip`
  - SHA256: `716862246E07B5BE8ADCDF8BE644B06C3E8EEA3CE880D5F56F9D1C1767103F94`
- Legacy cleanup:
  - `dist\FrameScopeMonitor-LegacyCleanup.exe`
  - SHA256: `4BE7D6C3FDB33E15360749F25804A35FD99D74B67E7D91924319D69F8B3B293A`

### Real PUBG status

- Real PUBG still was not tested because PUBG is not installed on this machine.
- PUBG simulator/mock test remains passing.

## Stage 11 - Report page architecture split

Status: completed on 2026-05-10.

### Goal

Continue architecture cleanup with another small split: move report page UI, report details, latest report helpers, and report-specific actions out of the main WinForms file without changing UI behavior or style.

### Files / modules

- `FrameScopeNativeMonitor.cs`
- `FrameScopeReportPage.cs`
- `build.ps1`
- `docs/FrameScopeMonitor-progress.md`
- `docs/FrameScopeMonitor-next-prompt.md`
- `install-backups/stage11-reportpage-20260510-060424/`
- `artifacts/stage11-report-page-split-reports.png`
- `artifacts/stage11-report-page-split-targets.png`
- `artifacts/stage11-report-page-split-overview.png`

### Changes

- Added `FrameScopeReportPage.cs`.
- Moved report-related methods into the report partial module:
  - `BuildReportsPage`
  - `ReportActionsCard`
  - `ReportListCard`
  - `ReportDetailCard`
  - `BuildReportDetailText`
  - `UpdateReportDetailUi`
  - `LatestReportPath`
  - `OpenLatestReport`
  - `OpenHistory`
  - `OpenSelectedReport`
  - `OpenSelectedReportFolder`
  - `GenerateSelectedDiagnosticReport`
  - `RegenerateSelectedReport`
- Updated `build.ps1` to compile `FrameScopeReportPage.cs`.
- Kept UI layout, Chinese text, rounded styling, and button behavior unchanged.
- Current line counts:
  - `FrameScopeNativeMonitor.cs`: 3806 lines.
  - `FrameScopeReportPage.cs`: 328 lines.
  - `FrameScopeLiveData.cs`: 160 lines.
  - `FrameScopeUiComponents.cs`: 233 lines.

### Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `tests\FrameScopeDiagnosticsTests.exe`: PASS.
- `tests\FrameScopeConfigStoreTests.exe`: PASS.
- `tests\FrameScopeCapturePlannerTests.exe`: PASS.
- `tests\FrameScopeReportProgressTests.exe`: PASS.
- `tests\FrameScopePubgSimulatorTests.exe`: PASS.
- `node tests\chart-sampling-tests.js`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- UI smoke screenshots:
  - `artifacts\stage11-report-page-split-reports.png`, 87517 bytes.
  - `artifacts\stage11-report-page-split-targets.png`, 89969 bytes.
  - `artifacts\stage11-report-page-split-overview.png`, 89720 bytes.
- Visual check:
  - Reports page still shows report list, capture chain, report detail, and real action buttons.
  - Targets page still shows report quick actions.
  - Overview still shows latest report state.
- Installed app sync:
  - Backed up previous installed files to `install-backups\stage11-reportpage-20260510-060424`.
  - Synced binaries into `%LOCALAPPDATA%\FrameScopeMonitor`.
  - Source/install hashes matched for core binaries and PresentMon.
  - Source/package payload hashes matched for installer payload and legacy cleanup artifact.
- `git diff --check`: only LF/CRLF warnings.
- Residual process check: no FrameScope / PresentMon / TslGame / FakePresentMon processes.

### Package state

- Setup:
  - `dist\FrameScopeMonitor-Setup.exe`
  - SHA256: `A88C86F75F0B110F985DD9AF8D5AB5F6412A48AC13DCAD280DABA599632CC526`
- Zip:
  - `dist\FrameScopeMonitor-Installer.zip`
  - SHA256: `552C9B6D9E7DDF6465760B251E50E46A425AD5AE0300FAB9D5173E533F385611`
- Legacy cleanup:
  - `dist\FrameScopeMonitor-LegacyCleanup.exe`
  - SHA256: `3445D22007FE7DEBEA5D95B041D18F9E99192C24A0D61BDF938600EFCB9FF966`

### Real PUBG status

- Real PUBG still was not tested because PUBG is not installed on this machine.
- PUBG simulator/mock test remains passing.
## 2026-05-10 Stage 16.1 - Right UI Reference Mapping

Scope: user accepted current left sidebar direction. Do not redesign sidebar. Current work targets right-side main UI only.

### Skills used

- `ui-ux-pro-max`: reference-image UI extraction for dashboard/card/table/chart/forms.
- `design-system`: shared token model for right pages.
- `plan-design-review`: pre-code layout risk check.
- `diagnose`: existing UI logic entrypoint mapping.
- `review`: fake-button/state-sync risk mapping.
- `improve-codebase-architecture`: keep real state seams, avoid static mock pages.
- `caveman`: concise stage notes.

### Design tokens extracted from four reference images

- App bg: deep blue-black gradient, center slightly brighter, edges darker.
- Workspace bg: `#030B18` to `#071A30`, no flat black.
- Card bg: translucent dark blue, approx `rgba(8,24,42,0.88)`.
- Card border: low-glow cyan/blue, approx `rgba(38,145,210,0.55)`.
- Active border: cyan `#29E6FF`, not harsh white.
- Primary text: near white `#F1F7FF`.
- Secondary text: grey-blue `#AFC3D9`.
- Muted text: `#6F879F`.
- Primary accent: cyan-blue.
- Secondary accent: blue.
- Success: green.
- Diagnostics: purple.
- Stop/error: red-pink.
- Card radius: 18-22px in WinForms logical units.
- Button radius: 12-16px.
- Progress bar radius: 10-12px.
- Header title: 28-32pt Segoe UI bold.
- Page section title: 14-16pt Microsoft YaHei UI bold.
- Metric value: 24-32pt bold, color-coded.

### Page layout mapping

- Global shell:
  - Keep accepted `FrameScopeReferenceSidebar`.
  - Right workspace gets unified gradient/custom painted background.
  - Header remains shared: large `FrameScope Monitor` + subtitle + 3 status cards.
  - Bottom `报告生成` card remains shared and must keep real progress/open-folder logic.

- Overview:
  - Existing `BuildOverviewPage` has real target count, watcher state, latest report, output dir, quick action buttons.
  - Needs visual rebuild: match five status cards, chain card, monitored games card, three info cards, quick actions.

- Settings:
  - Existing `SettingsEditor`, `SaveConfigFromGrid`, `ResetConfigToDefaultsFromUi`, `BrowseDataRoot` are real.
  - Needs visual rebuild: reference-like left settings card + right summary/target/chain cards.

- Live:
  - Existing `ShowPage` starts live timer only for `live`; leaving page stops timer.
  - Existing `LoadLiveSnapshot` and `FrameScopeMiniChartPanel` are real-data based with empty state.
  - Needs visual rebuild: reference chart grid, right capture/log cards, log buttons keep real pause/clear.

- Targets:
  - Existing `CreateTargetGrid`, `AddSelectedProcess`, `RefreshProcessList`, `StartWatcher`, `StopWatcher`, `SaveConfigFromGrid` are real.
  - Needs visual rebuild: reference table/controls; avoid white native scrollbars where possible.

### Plan design review

- Primary risk: trying to match screenshots with many default WinForms controls will preserve default-control feel.
- Decision: add/extend reusable self-painted primitives for card backgrounds, buttons, status cards, and chart panels; keep data/event logic in existing methods.
- Primary constraint: do not rewrite accepted sidebar.
- Verification route: per-page screenshot CLI, main `csc` compile for stage checks, final health/build only at final stage.

### Verification

- Read current UI entrypoints: `BuildHeader`, `BuildReportProgressCard`, `BuildOverviewPage`, `BuildSettingsPage`, `BuildLivePage`, `BuildTargetsPage`, `ShowPage`, live timer functions, config save, process refresh.
- No code implementation in this stage besides documentation.

### Next

- Stage 16.2: implement unified right-side workspace background, reference header status cards, and bottom report generation card while preserving real events.
## 2026-05-10 Stage 16.2 - Shared Right UI Shell

### Goal

Implement shared right-side visual primitives without changing accepted sidebar or page logic.

### Files changed

- `FrameScopeUiComponents.cs`
- `FrameScopeNativeMonitor.cs`
- `docs/FrameScopeMonitor-progress.md`
- `docs/FrameScopeMonitor-next-prompt.md`

### Changes

- Added `FrameScopeWorkspacePanel` for deep blue-black gradient workspace background.
- Added `FrameScopeStatusLabel` for self-painted top status cards with icon, glow border, title/value text.
- Upgraded `FrameScopeCardPanel` with gradient fill, soft border, inner glow.
- Upgraded rounded buttons with gradient fill and glow border.
- Rebuilt shared header spacing:
  - `FrameScope Monitor` title preserved.
  - subtitle preserved.
  - top right three status cards wired to existing labels/status update path.
- Rebuilt shared bottom `报告生成` card:
  - larger title.
  - centered real progress text.
  - green progress fill.
  - real `打开报告目录` button still calls `OpenDataRoot`.
- Increased default window width to avoid accepted wide sidebar compressing right-side reference layout.

### Verification

- Main exe compiled with direct `csc`: PASS.
- No `build.ps1` run.
- No package run.
- Screenshots generated:
  - `artifacts/stage16.2-overview-v4.png`
  - `artifacts/stage16.2-settings-v2.png`
  - `artifacts/stage16.2-live-v2.png`
  - `artifacts/stage16.2-targets-v2.png`
- Visual check:
  - Header title no longer clipped.
  - Status cards no longer show old flat label style.
  - Bottom report generation card visually closer to reference.
  - Sidebar style unchanged.

### Remaining gaps

- Overview page card content still too text/list based; needs Stage 16.3.
- Settings page layout still sparse and not fully reference-matched; needs Stage 16.4.
- Live chart grid exists but needs reference-specific chart polish; Stage 16.5.
- Targets table still uses native DataGridView with native scrollbar risk; Stage 16.6.

### Next

- Stage 16.3: rebuild Overview content layout against reference image 1 while keeping real target/report/output/watcher logic.

## Stage 16.3 - 概览页复刻
- 时间：2026-05-10 14:29:10
- 范围：右侧概览页，不改左侧导航栏。
- 修改：修复 FrameScopeCaptureChainVisual 透明背景初始化顺序；概览页使用真实配置目标数、监测状态、最近报告、输出目录状态；捕获链改为自绘视觉；受监控游戏改为真实配置表式列表；快速操作保留真实“启动监测 / 打开输出目录”。
- 修改文件：FrameScopeUiComponents.cs, FrameScopeNativeMonitor.cs
- 验证：直接 csc 编译通过；生成截图 artifacts/stage16.3-overview.png；截图检查无启动异常、无明显白色默认控件、按钮为中文。
- 风险：真实参考图未能像素级读取，只按用户规格和现有参考方向对齐；窗口标题栏仍是 WinForms 原生外壳。
- 下一阶段：Stage 16.4 设置页复刻。

## Stage 16.4 - 设置页复刻
- 时间：2026-05-10 14:36:50
- 范围：右侧设置页，不改左侧导航栏。
- 修改：新增自绘设置行 FrameScopeSettingRowPanel；设置页改为监测/报告/诊断分组卡片行；原生复选框改为可点击状态按钮并绑定隐藏 Checked 值；采样间隔保存时同步到当前目标采样配置；保留天数、最大 MB、数据目录继续使用真实配置字段。
- 修改文件：FrameScopeUiComponents.cs, FrameScopeNativeMonitor.cs
- 验证：直接 csc 编译通过；生成截图 artifacts/stage16.4-settings-v2.png；截图检查无原生白色复选框、按钮中文、保存/恢复默认/选择目录仍绑定原 click 逻辑。
- 风险：设置页没有做像素级对齐；路径输入在默认宽度下会截断显示，但真实值仍保留在 TextBox。
- 下一阶段：Stage 16.5 实时监控页复刻。

## Stage 16.5 - 实时监控页复刻/验收
- 时间：2026-05-10 14:38:39
- 范围：右侧实时监控页，不改左侧导航栏。
- 修改：本阶段未追加代码修改；沿用已有真实数据链路和空状态逻辑。
- 验证：直接 csc 编译已在当前代码后通过；生成截图 artifacts/stage16.5-live-before.png；运行 tests/FrameScopeUiStateTests.exe 通过。
- 功能证据：实时页空状态显示“暂无/等待”，日志说明未捕获原因；正常 UI 没有固定假数据；状态逻辑测试覆盖非实时页不刷新、无目标清空、进程退出清空等路径。
- 风险：本机无真实游戏运行，本阶段未验证活动 PresentMon 数据流；最终阶段需用 simulator/mock 或真实 run 再查。
- 下一阶段：Stage 16.6 监控目标页复刻。

## Stage 16.6 - 监控目标页复刻
- 时间：2026-05-10 14:46:24
- 范围：右侧监控目标页，不改左侧导航栏。
- 修改：右侧设置卡片改为圆角状态按钮并绑定真实配置 Checked 值；目标表格启用/自动打开报告列改为自绘深色复选框，去除原生白块；保留刷新进程、添加进程、保存配置、启动/停止监测原事件。
- 修改文件：FrameScopeNativeMonitor.cs
- 验证：直接 csc 编译通过；生成截图 artifacts/stage16.6-targets-v3.png；运行 tests/FrameScopeUiStateTests.exe 通过。
- 功能证据：表格仍使用真实 config.Targets；双击编辑和采样率校验事件保留；添加进程仍走 FrameScopeProcessPicker + watcher 运行时先暂停逻辑。
- 风险：进程选择 ComboBox 仍有 WinForms 原生下拉箭头；本阶段未做真实点击添加进程的人工 GUI 验证。
- 下一阶段：Stage 16.7 四页截图 design-review。

## Stage 16.7 - 四页 design-review
- 时间：2026-05-10 14:49:29
- 截图：artifacts/stage16.7-overview.png, artifacts/stage16.7-settings.png, artifacts/stage16.7-live.png, artifacts/stage16.7-targets.png
- 检查结果：四页均可切换并截图；右侧主内容统一深蓝黑背景、圆角玻璃卡片、中文按钮、顶部三状态卡、底部报告生成区；未发现页面内容裁切或白色滚动条。
- 已修正：目标页表格复选框自绘，右侧设置卡片去原生复选框。
- 仍有差距：WinForms 原生窗口标题栏存在；目标页进程选择 ComboBox 的下拉箭头仍是原生区域；路径过长处会视觉截断但不影响真实值。
- 下一阶段：Stage 16.8 health / verification。

## Stage 16.8 - Health / verification / installed sync
- 时间：2026-05-10 14:55:08
- 构建：直接 csc 编译 FrameScopeMonitor.exe 通过；未运行 build.ps1；未重新打包。
- 测试：FrameScopeConfigStoreTests、FrameScopeCapturePlannerTests、FrameScopeReportProgressTests、FrameScopeDiagnosticsTests、FrameScopePubgSimulatorTests、FrameScopeUiStateTests 全部 PASS；chart-sampling-tests.js PASS；RenderProbe dotnet build exit 0。
- 截图：artifacts/stage16.8-overview.png, stage16.8-settings.png, stage16.8-live.png, stage16.8-targets.png。
- 安装目录同步：已备份旧 exe 到 %LOCALAPPDATA%\FrameScopeMonitor\FrameScopeMonitor.exe.bak-20260510-145307；已复制新 FrameScopeMonitor.exe；source/installed SHA256 均为 99CEED26F863FCC8B29A9C57741B603B2631EB2A982CEECCCEA33C17C4318B83；installed screenshot artifacts/stage16.8-installed-overview.png 已生成。
- 残留进程：截图命令刚结束时曾短暂看到 FrameScopeMonitor 进程；等待 2 秒复查后 FrameScope / PresentMon / PUBG / TslGame 均无残留。
- 限制：未真实运行 PUBG；本阶段未重打包安装器；git 命令当前环境不可用，未做 git diff 检查。

## Stage 17 - final reference UI delivery

- Time: 2026-05-10 19:18
- Scope: final visual pass, build/package, installed-app sync, verification summary.
- Files changed:
  - `FrameScopeNativeMonitor.cs`
  - `FrameScopeUiComponents.cs`
  - `docs/FrameScopeMonitor-progress.md`
  - `docs/FrameScopeMonitor-next-prompt.md`
- UI changes:
  - Main window default size changed to 1600x1000 and minimum size to 1280x800.
  - Left sidebar width reduced to 300 and redrawn as a compact dark reference-style sidebar.
  - Removed the fake light sidebar scrollbar.
  - Added dark title-bar request through DWM.
  - Adjusted settings rows to reduce text crowding.
  - Capture-chain visual now uses compact sizing in narrow cards to avoid clipping.
- Real-function checks:
  - Overview uses real config targets, watcher status, report directory and output directory state.
  - Settings save/default/directory selection remain wired to config logic.
  - Live page keeps strict empty-state behavior when no configured target process exists.
  - Targets page still reads and saves real `config.Targets`, including checkbox state and sample interval validation.
  - Report progress path verified with a synthetic run and real report generator.
- Build:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- Tests:
  - `tests\FrameScopeConfigStoreTests.exe`: PASS.
  - `tests\FrameScopeCapturePlannerTests.exe`: PASS.
  - `tests\FrameScopeReportProgressTests.exe`: PASS.
  - `tests\FrameScopeDiagnosticsTests.exe`: PASS.
  - `tests\FrameScopePubgSimulatorTests.exe`: PASS.
  - `tests\FrameScopeUiStateTests.exe`: PASS.
  - `node tests\chart-sampling-tests.js`: PASS.
  - `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS.
  - `C:\Program Files\Git\cmd\git.exe diff --check`: PASS except line-ending warnings only.
- Screenshots:
  - `artifacts\stage17-final-overview.png`
  - `artifacts\stage17-final-settings.png`
  - `artifacts\stage17-final-live.png`
  - `artifacts\stage17-targets.png`
  - `artifacts\stage17-installed-overview.png`
- Package artifacts:
  - `dist\FrameScopeMonitor-Setup.exe`
  - `dist\FrameScopeMonitor-Installer.zip`
  - `dist\FrameScopeMonitor-LegacyCleanup.exe`
- Installed sync:
  - Installed directory: `%LOCALAPPDATA%\FrameScopeMonitor`
  - Backup directory: `install-backups\stage17-final-20260510-185309`
  - Source/installed/payload hashes matched for monitor, samplers, report generator and uninstaller exes.
- Limitations:
  - Latest screenshot automation for the targets page still hangs around the DataGridView path; existing `artifacts\stage17-targets.png` is the retained target-page screenshot.
  - Real fullscreen game and anti-cheat ETW capture still require manual validation with PUBG/Valorant/CS2 on the user's machine.
  - No fake FPS/demo curve was introduced; live page shows empty state when there is no configured active target process.

## Stage 17.1 - postbuild installed sync

- Time: 2026-05-10 19:27
- Reason: final `build.ps1` regenerated binaries and package artifacts, so installed app needed a fresh sync.
- Build:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- Installed sync:
  - Backup directory: `install-backups\stage17-postbuild-20260510-192743`
  - Synced `FrameScopeMonitor.exe`, `FrameScopeProcessSampler.exe`, `FrameScopeSystemSampler.exe`, `FrameScopeReportGenerator.exe`, `FrameScopeUninstaller.exe` to `%LOCALAPPDATA%\FrameScopeMonitor`.
  - Source, installed directory and `dist\FrameScopeMonitor-payload` hashes match for all five files.
- Final package hashes:
  - `dist\FrameScopeMonitor-Setup.exe`: `285CDA1C052E84A38C26E9119D7D9F80528499E843DDAE69ED59DF095C6CC68C`
  - `dist\FrameScopeMonitor-Installer.zip`: `17DCAADA5C942091FD91E6BC6C45331B55E918BE1763608C7695743E08AEB576`
  - `dist\FrameScopeMonitor-LegacyCleanup.exe`: `485DD4D979BD264F285B2CEBF21B5918B0ED04F75C1122DA71DC2BBA846F9B3F`
- Final residual-process check:
  - No FrameScope, PresentMon, TslGame, Valorant or CS2 process matched after sync.

## Stage 18 - project structure cleanup

- Time: 2026-05-13
- Goal: reorganize project into clearer modules without rewriting business logic.
- Plan:
  - `docs\superpowers\plans\2026-05-13-project-structure-cleanup.md`
- Source moved:
  - `FrameScopeNativeMonitor.cs` -> `src\app\FrameScopeNativeMonitor.cs`
  - `FrameScopeUiComponents.cs`, `FrameScopeUiState.cs`, `FrameScopeLiveData.cs`, `FrameScopeReportPage.cs` -> `src\ui\`
  - `FrameScopeConfigStore.cs`, `FrameScopeCapturePlanner.cs`, `FrameScopeReportProgress.cs` -> `src\core\`
  - `FrameScopeProcessSampler.cs`, `FrameScopeSystemSampler.cs` -> `src\monitoring\`
  - `FrameScopeDiagnostics.cs` -> `src\diagnostics\`
  - `FrameScopeReportGenerator.cs` -> `src\reporting\`
- Lightweight scripts moved:
  - Core scripts moved to `scripts\lightweight\`.
  - Root `.ps1` files now thin wrappers for compatibility.
  - Root `.cmd` launchers still call root wrappers.
- Build/test path updates:
  - `build.ps1`
  - `tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1`
  - `tests\chart-sampling-tests.js`
  - added `tests\Build-FrameScopeTests.ps1`
- Docs added/updated:
  - `docs\FrameScopeMonitor-Project-Overview.md`
  - `docs\modules\software-ui.md`
  - `docs\modules\ui-interactions.md`
  - `docs\modules\backend-monitoring.md`
  - `docs\modules\lightweight-script.md`
  - `AGENTS.md`
  - `README.md`
  - `docs\FrameScopeMonitor-next-prompt.md`
- Verification snapshot:
  - `build.ps1`: PASS after path update.
  - `tests\Build-FrameScopeTests.ps1`: PASS after path update.
- Remaining verification for final pass:
  - run rebuilt test exes.
  - run chart sampling test.
  - run RenderProbe build.
  - run lightweight wrapper/script parse and status check.
  - run stale reference scan and `git diff --check`.

## Stage 18.1 - verification after structure cleanup

- Time: 2026-05-13
- Build:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
- Tests:
  - `tests\FrameScopeConfigStoreTests.exe`: PASS.
  - `tests\FrameScopeCapturePlannerTests.exe`: PASS.
  - `tests\FrameScopeReportProgressTests.exe`: PASS.
  - `tests\FrameScopeDiagnosticsTests.exe`: PASS.
  - `tests\FrameScopePubgSimulatorTests.exe`: PASS.
  - `tests\FrameScopeUiStateTests.exe`: PASS.
  - `node tests\chart-sampling-tests.js`: PASS.
  - `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- Runtime checks:
  - UI screenshots generated:
    - `artifacts\stage18-overview.png`
    - `artifacts\stage18-settings.png`
    - `artifacts\stage18-live.png`
  - `FrameScopeMonitor.exe --ui-page targets --ui-screenshot artifacts\stage18-targets.png` returned but did not write PNG and left screenshot process; process was stopped manually. This is the known target/DataGridView screenshot harness limitation, not a build/path reference failure.
  - PUBG simulator stable scenario PASS: monitorExit 0, reportExit 0, phase done, `presentMonCsvRows=240`, `hasFrameData=true`, `reportKind=full`.
  - `no-data -DurationSeconds 1` simulator run was too short and stopped with missing `summary.json`; stable scenario was used as the end-to-end monitor validation.
- Lightweight script checks:
  - Root wrappers and moved scripts parsed successfully for all 7 GameLite scripts.
  - Root wrapper `Check-GameLiteAutoTrigger.ps1`: PASS, WMI trigger objects visible.
  - Direct moved script `scripts\lightweight\Check-GameLiteAutoTrigger.ps1`: PASS, WMI trigger objects visible.
  - Existing WMI consumers still point to root wrappers, which is expected and preserved for compatibility.
- Reference checks:
  - root C# source count is 0; source lives under `src\`.
  - `scripts\lightweight` contains 7 core `.ps1` scripts.
  - module docs exist under `docs\modules`.
  - stale root source path scan over build/tests/tools/src/scripts active docs found no old build-breaking source path.
  - `git diff --check`: PASS with LF/CRLF warnings only.
- Residual process check:
  - no FrameScope, PresentMon, TslGame, Valorant, CS2, FakePresentMon or GameLite process remained after verification.

## Stage 19 - lightweight project boundary separation

- Time: 2026-05-14
- Goal: make GameLite automatic lightweight scripts a clear independent project boundary, without wiring them into FrameScope Monitor.
- Stage A inventory:
  - Read `docs\modules\lightweight-script.md`, `docs\FrameScopeMonitor-Project-Overview.md`, `docs\FrameScopeMonitor-progress.md`, `docs\FrameScopeMonitor-next-prompt.md`, `AGENTS.md`, and `README.md`.
  - Audited root GameLite `.ps1` wrappers, root `.cmd` launchers, `scripts\lightweight\*.ps1`, `scripts\lightweight\game-lite-auto-trigger-backup.json`, build/test scripts, docs and references using `rg`.
  - Coupling result: FrameScope C# source, build, test rebuild, reporting and monitoring do not need GameLite/WMI. GameLite scripts do not call `FrameScopeMonitor.exe`, PresentMon, FrameScope report generator or FrameScope data root.
- Stage B separation fixes:
  - Root `.cmd` launchers now forward `%*`.
  - Root `.ps1` wrappers remain as compatibility bridges and forward `@args` to `scripts\lightweight`.
  - SGuard throttling is disabled by default and requires explicit `-AllowSGuardThrottle`.
  - `Install-GameLiteAutoTrigger.ps1` installs only the normal game trigger by default; SGuard WMI trigger is not installed by default.
  - `Exit-GameLite.ps1` now restores only from the saved snapshot; broad fallback restore is skipped to avoid touching unrelated processes.
  - Added `tests\lightweight-separation-tests.ps1` for parse, wrapper forwarding, `.cmd` forwarding, build/test independence, FrameScope runtime reference checks, SGuard default-disabled checks, and snapshot-only restore checks.
- Stage C lightweight verification:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\lightweight-separation-tests.ps1`: PASS.
  - Root `Check-GameLiteAutoTrigger.ps1`: PASS.
  - Direct `scripts\lightweight\Check-GameLiteAutoTrigger.ps1`: PASS.
  - Wrapper from external cwd with spaces: PASS.
  - Root wrapper argument forwarding with `Enter-GameLite.ps1 -OnlyProcessNames NoSuchProcessForWrapperArgTest` then `Exit-GameLite.ps1`: PASS.
  - SGuard no-opt-in run `Invoke-GameLiteSGuardThrottle.ps1 -RequireActiveGame -ThrottlePagePriority`: PASS no-op.
- Stage D FrameScope verification:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
  - Rebuilt test exes PASS: `FrameScopeConfigStoreTests`, `FrameScopeCapturePlannerTests`, `FrameScopeReportProgressTests`, `FrameScopeDiagnosticsTests`, `FrameScopePubgSimulatorTests`, `FrameScopeUiStateTests`.
  - `node .\tests\chart-sampling-tests.js`: PASS.
  - `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- WMI status:
  - Existing normal GameLite trigger is installed.
  - Existing legacy SGuard trigger/consumer is still present, but `SGuardHighRiskOptInInstalled=false`; Stage 20 supersedes the old SGuard opt-in behavior with default SGuard throttling plus the explicit `-DisableSGuardThrottle` off switch.
  - No install/remove WMI operation was executed in this stage.
- Docs updated:
  - `docs\modules\lightweight-script.md`
  - `docs\FrameScopeMonitor-Project-Overview.md`
  - `docs\FrameScopeMonitor-progress.md`
  - `docs\FrameScopeMonitor-next-prompt.md`
  - `AGENTS.md`
  - `README.md`
- Remaining manual validation:
  - Administrator install/remove WMI lifecycle should be tested only with explicit user approval.
  - Real game lifecycle still needs manual validation: Check trigger, start target game, confirm Enter, exit game, confirm Exit restore, then check residual GameLite/PowerShell/FrameScope/PresentMon processes.

## Stage 19.1 - final verification after documentation update

- Time: 2026-05-14
- Lightweight:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\lightweight-separation-tests.ps1`: PASS, parsed 7 root wrappers and 7 core scripts; `.cmd` `%*` forwarding confirmed; build/test independence confirmed; historical Stage 19 behavior still had SGuard default disabled; Stage 20 now supersedes this with SGuard default enabled; Exit snapshot-only restore confirmed.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\Check-GameLiteAutoTrigger.ps1`: PASS.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\lightweight\Check-GameLiteAutoTrigger.ps1`: PASS.
  - External cwd call from a temp directory with spaces to root `Check-GameLiteAutoTrigger.ps1`: PASS.
  - Root wrapper argument forwarding via `Enter-GameLite.ps1 -OnlyProcessNames NoSuchProcessForWrapperArgTest` followed by `Exit-GameLite.ps1`: PASS; snapshot count 0, changed count 0, fallback restore skipped.
  - `Invoke-GameLiteSGuardThrottle.ps1 -RequireActiveGame -ThrottlePagePriority` without `-AllowSGuardThrottle`: historical Stage 19 check was PASS no-op; Stage 20 now supersedes this by enabling SGuard throttle by default and using `-DisableSGuardThrottle` as the explicit off switch.
- FrameScope:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
  - Rebuilt test exes PASS: `FrameScopeConfigStoreTests`, `FrameScopeCapturePlannerTests`, `FrameScopeReportProgressTests`, `FrameScopeDiagnosticsTests`, `FrameScopePubgSimulatorTests`, `FrameScopeUiStateTests`.
  - `node .\tests\chart-sampling-tests.js`: PASS.
  - `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- WMI read-only check:
  - `GameTriggerInstalled=true`.
  - `SGuardLegacyTriggerInstalled=true`.
  - `SGuardLegacyConsumerInstalled=true`.
  - `SGuardHighRiskOptInInstalled=false`.
  - Existing WMI consumers still point at root wrappers; root wrappers resolve to `scripts\lightweight`.
  - No WMI install/remove was executed.
- Residual process check:
  - Final standalone check after all verification commands finished returned `NO_MATCHING_RESIDUAL_PROCESSES`.
- Git/diff:
  - `C:\Program Files\Git\cmd\git.exe diff --check`: PASS with LF/CRLF warnings only.
  - `git status` remains dirty from the broader structure cleanup; unrelated pre-existing moves/untracked directories were not reverted.

## Stage 20 - GameLite event-trigger SGuard-default update

- Time: 2026-05-14
- Goal: change GameLite so SGuard is throttled by default during game mode, and default game start/exit automation uses WMI events instead of a long-running PowerShell polling session.
- Plan:
  - `docs\superpowers\plans\2026-05-14-gamelite-event-trigger-sguard-default.md`
- Script changes:
  - `Enter-GameLite.ps1` now includes SGuard targets by default unless `-DisableSGuardThrottle` is passed. `-AllowSGuardThrottle` remains accepted for compatibility only.
  - Default SGuard throttling uses Idle priority, IO priority 0, page priority 1 and affinity on the last two logical cores. `-StrictSGuard` uses the last one logical core.
  - Steam main client and Weixin are now hard-protected from default background throttling, alongside Steam overlay/network helpers, OPPO Connect, Everything, QQ/QQEX, WeChat/WeChatAppEx and system-sensitive chains.
  - `Invoke-GameLiteSGuardThrottle.ps1` now defaults to enabled unless `-DisableSGuardThrottle` is passed, and supports `-StrictSGuard`.
  - `Install-GameLiteAutoTrigger.ps1` now installs separate WMI filters/consumers for `Win32_ProcessStartTrace` and `Win32_ProcessStopTrace`.
  - The game start consumer runs short-lived `Enter-GameLite.ps1`; the game stop consumer runs `Exit-GameLite.ps1 -RequireNoActiveGame -ExitGraceSeconds 8`.
  - The SGuard late-start consumer is retained but gated with `-RequireActiveGame -ThrottlePagePriority`, so it no-ops without an active GameLite state/game.
  - `GameLiteSession.ps1` remains as a compatibility/manual entry but no longer contains the default infinite game-exit polling loop. If a legacy start-only WMI consumer calls it before the new stop trigger exists, it no-ops and logs the migration requirement.
  - `Exit-GameLite.ps1` now supports `-RequireNoActiveGame` and skips restore while any configured game is still running.
  - `Remove-GameLiteAutoTrigger.ps1` covers new and legacy filters/consumers.
  - `Check-GameLiteAutoTrigger.ps1` reports game start trigger, game stop trigger, SGuard late-start trigger, legacy trigger state, SGuard default policy, state file status and running GameLite PowerShell.
- Tests/docs:
  - `tests\lightweight-separation-tests.ps1` now asserts SGuard default enabled, `-DisableSGuardThrottle`, start/stop WMI event queries, no default `GameLiteSession.ps1`, no `-ForceSnapshot` default trigger, and no `while ($true)` polling path in `GameLiteSession.ps1`.
  - Updated `docs\modules\lightweight-script.md`, `docs\FrameScopeMonitor-Project-Overview.md`, `docs\FrameScopeMonitor-next-prompt.md`, `AGENTS.md` and `README.md` to describe the current SGuard default/event-trigger rules.
- Verification in progress:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\lightweight-separation-tests.ps1`: PASS after the new assertions.
  - PowerShell parser check over 7 root wrappers and 7 `scripts\lightweight` scripts: PASS.
  - Root `Check-GameLiteAutoTrigger.ps1`: PASS. Current machine still has legacy `GameLiteAutoTriggerFilter` and `GameLiteSGuardTriggerFilter`; new `GameLiteGameStartTriggerFilter` / `GameLiteGameStopTriggerFilter` are not installed because no admin install was executed.
  - Direct `scripts\lightweight\Check-GameLiteAutoTrigger.ps1`: PASS with the same read-only WMI state.
  - Root wrapper forwarding via `Enter-GameLite.ps1 -OnlyProcessNames NoSuchProcessForWrapperArgTest`: PASS, output reported `SGuardThrottleDefault=enabled-last-two-cores`, `ChangedCount=0`; `Exit-GameLite.ps1` removed the empty state file.
  - SGuard mock process named `SGuard64.exe`: PASS. Default `Enter-GameLite.ps1 -OnlyProcessNames SGuard64` changed priority to Idle, IO priority to 0, page priority to 1 and affinity to last two logical cores; `Exit-GameLite.ps1` restored the snapshot. `-DisableSGuardThrottle` run reported `SGuardThrottleDefault=disabled` and `ChangedCount=0`.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
  - Rebuilt test exes PASS: `FrameScopeConfigStoreTests`, `FrameScopeCapturePlannerTests`, `FrameScopeReportProgressTests`, `FrameScopeDiagnosticsTests`, `FrameScopePubgSimulatorTests`, `FrameScopeUiStateTests`.
  - `node .\tests\chart-sampling-tests.js`: PASS.
  - `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
  - `C:\Program Files\Git\cmd\git.exe diff --check`: PASS with LF/CRLF warnings only.
- WMI caution:
  - No install/remove WMI operation has been executed in this stage. Existing legacy WMI objects are read-only until the user authorizes admin migration.

## Stage 20.1 - continued GameLite verification

- Time: 2026-05-14
- Scope:
  - Continued verification only. No WMI install/remove was executed, even though the current shell is elevated.
  - Fixed stale documentation statements found during verification: Stage 19 SGuard opt-in/no-op wording is now explicitly marked historical, and the Stage 20 plan now matches the implemented `LastTwo` default affinity and no-`-ForceSnapshot` WMI start consumer.
- Fresh lightweight verification:
  - PowerShell parser check over 7 root wrappers and 7 `scripts\lightweight` core scripts: PASS (`PARSER_OK`).
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\lightweight-separation-tests.ps1`: PASS (`SGuardDefault=enabled`, `GameStartStopTriggers=required`, `ExitRestoresOnlySnapshot=true`).
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\Check-GameLiteAutoTrigger.ps1`: PASS.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\lightweight\Check-GameLiteAutoTrigger.ps1`: PASS.
  - Current WMI read-only state remains legacy-only: `GameStartTriggerInstalled=false`, `GameStopTriggerInstalled=false`, `SGuardLateStartTriggerInstalled=false`, `LegacyGameTriggerInstalled=true`, `SGuardLegacyTriggerInstalled=true`.
  - Root wrapper forwarding from project root with `Enter-GameLite.ps1 -OnlyProcessNames NoSuchProcessForWrapperArgTest` then `Exit-GameLite.ps1`: PASS, no state file left.
  - Root wrapper forwarding from external cwd `C:\Users\misakamiro\AppData\Local\Temp\GameLite External Cwd With Spaces`: PASS, `EnterChangedCount=0`, `EnterSGuardThrottleDefault=enabled-last-two-cores`, no state file left.
  - External cwd direct Check from the same path: PASS, `SGuardThrottleDefault=enabled`, `StateFileExists=false`, `RunningGameLiteScriptCount=0`.
  - Legacy `GameLiteSession.ps1 -StartupWaitSeconds 1 -ExitGraceSeconds 1`: PASS, exited quickly with no state file and no GameLite PowerShell residual after a short wait. This confirms the legacy start-only WMI consumer path does not enter a non-restorable session before migration.
- SGuard mock verification:
  - A temporary mock `SGuard64.exe` process was launched from a copied `powershell.exe` under `%TEMP%\GameLiteSGuardMock`.
  - Default `Enter-GameLite.ps1 -OnlyProcessNames SGuard64`: PASS. Mock priority changed `Normal -> Idle`, IO priority changed to `0`, page priority changed to `1`, affinity preset was `LastTwo`, and `SGuardThrottleDefault=enabled-last-two-cores`.
  - `Exit-GameLite.ps1`: PASS. Snapshot restore returned the mock priority to `Normal`, `RestoredCount=1`, and removed the state file.
  - `Enter-GameLite.ps1 -OnlyProcessNames SGuard64 -DisableSGuardThrottle`: PASS. `ChangedCount=0`, `SGuardThrottleDefault=disabled`.
- FrameScope isolation regression:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS, rebuilt `dist\FrameScopeMonitor-Setup.exe`.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
  - Rebuilt test exes PASS with exit code 0: `FrameScopeCapturePlannerTests.exe`, `FrameScopeConfigStoreTests.exe`, `FrameScopeDiagnosticsTests.exe`, `FrameScopePubgSimulatorTests.exe`, `FrameScopeReportProgressTests.exe`, `FrameScopeUiStateTests.exe`.
  - `node .\tests\chart-sampling-tests.js`: PASS.
  - `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- Residual and diff checks:
  - Final GameLite script residual process check: PASS (`NO_GAMELITE_SCRIPT_RESIDUALS`).
  - Final state file check: PASS (`NO_GAMELITE_STATE_FILE`).
  - Final FrameScope/PresentMon/SGuard mock/game process check: PASS, no matching process output.
  - `C:\Program Files\Git\cmd\git.exe diff --check`: PASS with line-ending warnings only for `README.md`, `build.ps1`, and `framescope-config.example.json`.
- Remaining manual validation:
  - Admin WMI migration is still not executed. To validate the new permanent event lifecycle, explicitly authorize and run `Install-GameLiteAutoTrigger.ps1`, then run Check and a real game lifecycle.
  - Real anti-cheat/game behavior still needs PUBG/Valorant/CS2 or user-specified game validation: enter, in-game behavior, overlay/chat/OPPO Connect unaffected, exit, snapshot restore, residual process check.

## Stage 35 - GameLite standalone project migration

- Time: 2026-05-15
- Scope:
  - Migration only. No WMI install/remove was executed.
  - GameLite implementation was moved out of the FrameScope Monitor source tree into sibling standalone project:
    `C:\Users\misakamiro\Documents\Codex\2026-05-02\gamelite-auto-lightweight`
  - FrameScope root `.ps1` files remain as compatibility wrappers for old WMI consumers, `.cmd` launchers, and manual entry habits.
  - FrameScope root `.cmd` launchers still forward `%*`.
  - Old `scripts\lightweight` core `.ps1` files were removed from the FrameScope tree to avoid two active implementations.
  - Legacy runtime files from old `scripts\lightweight` were archived under the new project at `legacy-from-framescope`.
- New standalone files:
  - `AGENTS.md`
  - `README.md`
  - `docs\lightweight-script.md`
  - `tests\gamelite-standalone-tests.ps1`
  - root GameLite `.ps1` scripts and `.cmd` launchers.
- Updated FrameScope docs:
  - `AGENTS.md`
  - `README.md`
  - `docs\FrameScopeMonitor-Project-Overview.md`
  - `docs\modules\lightweight-script.md`
  - `docs\FrameScopeMonitor-next-prompt.md`
  - `docs\FrameScopeMonitor-progress.md`
- Known runtime state:
  - Current host WMI consumers still point at old FrameScope root wrappers until administrator WMI migration is explicitly authorized.
  - This is expected; the old wrappers now bridge to the new standalone project.
- Handoff:
  - Further GameLite behavior testing should happen in a separate tester conversation using the standalone project path and without changing WMI unless explicitly authorized.
## Stage 21 - FrameScopeNativeMonitor partial split

- Time: 2026-05-14
- Goal: separate UI design, UI interaction, watcher, monitor-session, and report orchestration ownership without changing behavior.
- Plan:
  - `docs\superpowers\plans\2026-05-14-framescope-ui-interaction-backend-split.md`
- Main split:
  - `src\app\FrameScopeNativeMonitor.cs` is now the app entry/shared helper file instead of the 4000+ line mixed implementation.
  - `src\ui\FrameScopeUiTheme.cs` holds shared UI colors and radius constants.
  - `src\app\FrameScopeNativeMonitor.UiShell.cs` holds app shell, sidebar, header, report-progress card, page routing, status display helpers, screenshot helpers, and common visual helper methods.
  - `src\app\FrameScopeNativeMonitor.UiInteractions.cs` holds UI action handlers, config save/reset, process picker actions, watcher start/stop UI flow, report progress UI refresh, and diagnostic-report UI generation.
  - `src\app\FrameScopeNativeMonitor.PageOverview.cs`, `PageSettings.cs`, `PageLive.cs`, `PageTargets.cs`, and `PageAbout.cs` hold page-specific builders.
  - `src\app\FrameScopeNativeMonitor.Watcher.cs` holds native watcher loop, active monitor tracking, watcher state, and watcher logging.
  - `src\app\FrameScopeNativeMonitor.MonitorSession.cs` holds monitor-session, PresentMon, sampler process, status/summary, and capture diagnostics logic.
  - `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs` holds report recovery, report generator invocation, status/progress merge, history, and report auto-open helpers.
- Build/test script updates:
  - `build.ps1` now compiles the new partial `.cs` files.
  - `tests\Build-FrameScopeTests.ps1` did not need source-list changes because it rebuilds independent core/UI-state tests, not the WinForms app partial set.
- Docs updated:
  - `docs\modules\software-ui.md`
  - `docs\modules\ui-interactions.md`
  - `docs\modules\backend-monitoring.md`
  - `docs\FrameScopeMonitor-next-prompt.md`
  - `docs\FrameScopeMonitor-progress.md`
- Parallel ownership rules:
  - UI design conversations should use `src\ui\FrameScopeUiTheme.cs`, `src\ui\FrameScopeUiComponents.cs`, `FrameScopeNativeMonitor.UiShell.cs`, page partial files, and `src\ui\FrameScopeReportPage.cs`.
  - UI interaction conversations should use `FrameScopeNativeMonitor.UiShell.cs`, `FrameScopeNativeMonitor.UiInteractions.cs`, page partial files, `FrameScopeUiState.cs`, and `FrameScopeLiveData.cs`.
  - Backend conversations should use `FrameScopeNativeMonitor.Watcher.cs`, `FrameScopeNativeMonitor.MonitorSession.cs`, `FrameScopeNativeMonitor.ReportOrchestration.cs`, `src\core\`, `src\monitoring\`, and `src\diagnostics\`.
  - Exclusive/high-conflict files: `build.ps1`, `src\app\FrameScopeNativeMonitor.cs`, `FrameScopeNativeMonitor.UiShell.cs`, `FrameScopeNativeMonitor.UiInteractions.cs`, `FrameScopeNativeMonitor.MonitorSession.cs`, `FrameScopeNativeMonitor.ReportOrchestration.cs`, `src\ui\FrameScopeUiTheme.cs`, and `src\reporting\FrameScopeReportGenerator.cs`.
- Verification status:
  - Initial `build.ps1`: PASS after partial split and compile-list update.
  - Initial `tests\Build-FrameScopeTests.ps1`: PASS.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
  - Rebuilt test exes PASS with exit code 0: `FrameScopeConfigStoreTests.exe`, `FrameScopeCapturePlannerTests.exe`, `FrameScopeReportProgressTests.exe`, `FrameScopeDiagnosticsTests.exe`, `FrameScopePubgSimulatorTests.exe`, `FrameScopeUiStateTests.exe`.
  - `node .\tests\chart-sampling-tests.js`: PASS.
  - `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
  - UI screenshots generated successfully: `artifacts\stage21-overview.png`, `artifacts\stage21-settings.png`, `artifacts\stage21-live.png`, `artifacts\stage21-targets.png`.
  - Stable PUBG simulator scenario: PASS, `monitorExit=0`, `reportExit=0`, `presentMonCaptureMode=process_name`, `presentMonCsvRows=240`, `hasFrameData=true`, `reportKind=full`.
  - Final residual process check: PASS, no matching FrameScope/PresentMon/TslGame/Valorant/CS2/FakePresentMon/GameLite processes.
  - `C:\Program Files\Git\cmd\git.exe diff --check`: PASS with line-ending warnings only.
- Remaining:
  - `src\reporting\FrameScopeReportGenerator.cs` remains large and should get a dedicated future split plan before any broad refactor.
  - Target page screenshot harness may still hit the known DataGridView screenshot issue; treat that separately from main app behavior.

## Stage 22 - FrameScopeReportGenerator partial split

- Time: 2026-05-14
- Goal: split the native HTML report generator into focused partial files without changing report behavior, JSON shape, manifest/progress writing, or embedded chart JavaScript.
- Plan:
  - `docs\superpowers\plans\2026-05-14-framescope-report-generator-split.md`
- Main split:
  - `src\reporting\FrameScopeReportGenerator.cs` now holds report generator constants, shared nested models, entry point, `Generate`, progress helper, argument parsing, and latest-run lookup.
  - `src\reporting\FrameScopeReportGenerator.PresentMon.cs` holds PresentMon CSV reading, frame validation, and render-track selection.
  - `src\reporting\FrameScopeReportGenerator.SystemData.cs` holds system-sample CSV reading and system/performance/IO series projection.
  - `src\reporting\FrameScopeReportGenerator.ProcessData.cs` holds process-sample CSV reading, per-process CPU/memory matrices, and process stats.
  - `src\reporting\FrameScopeReportGenerator.Analysis.cs` holds time alignment, FPS buckets, low-FPS/stat math, rounding, and shared parsing helpers.
  - `src\reporting\FrameScopeReportGenerator.Metadata.cs` holds run metadata, capture diagnostics, hardware WMI metadata, and simple JSON extraction.
  - `src\reporting\FrameScopeReportGenerator.Csv.cs` holds the streaming CSV parser.
  - `src\reporting\FrameScopeReportGenerator.Html.cs` holds the embedded report HTML/CSS/JavaScript template.
- Build/test script updates:
  - `build.ps1` now compiles all report generator partial `.cs` files into `FrameScopeReportGenerator.exe`.
- Docs updated:
  - `docs\modules\backend-monitoring.md`
  - `docs\FrameScopeMonitor-next-prompt.md`
  - `docs\FrameScopeMonitor-progress.md`
- Parallel ownership rules:
  - Report data conversations can edit `FrameScopeReportGenerator.PresentMon.cs`, `SystemData.cs`, `ProcessData.cs`, `Analysis.cs`, `Metadata.cs`, and `Csv.cs`.
  - Report UI/interaction conversations should edit `FrameScopeReportGenerator.Html.cs`.
  - `FrameScopeReportGenerator.cs` remains exclusive for CLI, manifest/progress, generated data shape, and main generation orchestration changes.
  - `FrameScopeReportGenerator.Html.cs` should be exclusive for embedded report template work because it is a large string and conflicts easily.
- Verification status:
  - Initial `build.ps1`: PASS after partial split and compile-list update.
  - `tests\chart-sampling-tests.js` was updated to read all `src\reporting\FrameScopeReportGenerator*.cs` files after the embedded chart JavaScript moved to `FrameScopeReportGenerator.Html.cs`.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
  - Rebuilt test exes PASS with exit code 0: `FrameScopeConfigStoreTests.exe`, `FrameScopeCapturePlannerTests.exe`, `FrameScopeReportProgressTests.exe`, `FrameScopeDiagnosticsTests.exe`, `FrameScopePubgSimulatorTests.exe`, `FrameScopeUiStateTests.exe`.
  - `node .\tests\chart-sampling-tests.js`: PASS.
  - `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
  - Stable PUBG simulator scenario: PASS, `monitorExit=0`, `reportExit=0`, `presentMonCaptureMode=process_name`, `presentMonCsvRows=240`, `hasFrameData=true`, `reportKind=full`.
  - UI screenshots generated successfully for `artifacts\stage22-overview.png`, `artifacts\stage22-settings.png`, and `artifacts\stage22-live.png`; all are 1600x1000 PNG files with nonblank sampled pixels.
  - `artifacts\stage22-targets.png`: FAIL/known harness issue. The `--ui-page targets --ui-screenshot` process did not exit within 30 seconds and did not write a PNG, matching the existing DataGridView screenshot harness risk. The screenshot process was identified by its `--ui-page targets --ui-screenshot ...stage22-targets.png` command line and stopped; this is not treated as a main app build or monitoring-chain failure.
  - UI interaction spot check: page routing still goes through `ShowPage`; live page entry/leave still starts/stops live refresh; buttons remain wired to real handlers such as `StartWatcher`, `StopWatcher`, `SaveConfigFromGrid`, `OpenDataRoot`, `GenerateDiagnosticReportFromUi`, report open/regenerate actions, and process refresh/add.

## Stage 23-28 - Continued UI, interaction, backend, and diagnostics split

Goal:

- Continue the Stage 21/22 architecture cleanup until the remaining high-conflict mixed files are split into clearer UI design, UI interaction, backend monitoring, report orchestration, and diagnostics ownership files.
- Keep the split mechanical: no feature rewrite, no UI text change, no fake controls, no GameLite/WMI boundary changes.

Plans added:

- `docs\superpowers\plans\2026-05-15-framescope-ui-components-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-monitor-session-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-ui-shell-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-report-orchestration-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-ui-interactions-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-diagnostics-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-reference-sidebar-split.md`

Split results:

- `src\ui\FrameScopeUiComponents.cs` is now only an umbrella note. Its former controls moved into `FrameScopeRoundedDrawing.cs`, `FrameScopePanels.cs`, `FrameScopeButtons.cs`, `FrameScopeStatusControls.cs`, `FrameScopeLiveChart.cs`, and `FrameScopeReferenceSidebar*.cs`.
- `src\ui\FrameScopeReferenceSidebar.cs` now holds sidebar state, constructor, and mouse navigation events. Drawing moved to `FrameScopeReferenceSidebar.Drawing.cs`; navigation args moved to `FrameScopeReferenceSidebar.Navigation.cs`.
- `src\app\FrameScopeNativeMonitor.UiShell.cs` now holds shell construction, sidebar creation, dark title-bar setup, and header construction. Fields, routing, visual helpers, report progress card, screenshots, and status display moved into focused `Ui*.cs` partial files.
- `src\app\FrameScopeNativeMonitor.UiInteractions.cs` is now a small placeholder. Config actions, process picker actions, watcher controls, process cleanup, status refresh, diagnostics UI actions, and generic UI helpers moved into focused partial files.
- `src\app\FrameScopeNativeMonitor.MonitorSession.cs` now keeps the `RunNativeMonitorSession` orchestration. Models, path/argument helpers, target discovery, tool resolution, PresentMon lifecycle, child process helpers, and status writers moved into focused `MonitorSession.*.cs` files.
- `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs` now keeps stale-run recovery and report-generator invocation. Models, status/history helpers, and report open/browser fallback moved into focused report partial files.
- `src\diagnostics\FrameScopeDiagnostics.cs` is now a partial static class with public entry points. Models, section builders, markdown rendering, redaction, retention, and IO helpers moved into focused diagnostics files.
- `build.ps1` was updated to compile all new source files.
- `tests\Build-FrameScopeTests.ps1` was updated so `FrameScopeDiagnosticsTests.exe` compiles all diagnostics partial files.

Current largest files after split:

- `src\monitoring\FrameScopeProcessSampler.cs`: 388 lines, single-purpose sampler executable.
- `src\monitoring\FrameScopeSystemSampler.cs`: 366 lines, single-purpose system sampler executable.
- `src\app\FrameScopeNativeMonitor.MonitorSession.cs`: 338 lines, monitor-session orchestration.
- `src\reporting\FrameScopeReportGenerator.cs`: 334 lines, report-generator entry and orchestration.
- `src\ui\FrameScopeReportPage.cs`: 328 lines, reports page UI/action surface.
- `src\app\FrameScopeNativeMonitor.UiVisualHelpers.cs`: 321 lines, shared UI visual factories.
- `src\app\FrameScopeNativeMonitor.PageTargets.cs`: 316 lines, target page layout and table visuals.

Parallel editing rules updated:

- UI design conversations should target `src\ui\FrameScope*.cs`, `UiVisualHelpers.cs`, `UiReportProgress.cs`, `UiScreenshots.cs`, `UiStatusDisplay.cs`, and page builder files.
- UI interaction conversations should target `UiRouting.cs`, `UiConfigActions.cs`, `UiProcessPicker.cs`, `UiWatcherControls.cs`, `UiProcessCleanup.cs`, `UiStatusRefresh.cs`, `UiDiagnosticActions.cs`, page partial files, `FrameScopeUiState.cs`, and `FrameScopeLiveData.cs`.
- Backend conversations should target `Watcher.cs`, `MonitorSession*.cs`, `ReportOrchestration*.cs`, `ReportStatus.cs`, `ReportOpen.cs`, `src\core\`, `src\monitoring\`, `src\diagnostics\FrameScopeDiagnostics*.cs`, and `src\reporting\FrameScopeReportGenerator*.cs`.
- Exclusive files by behavior: `build.ps1`; `FrameScopeNativeMonitor.cs`; `UiRouting.cs` for page routing; `UiWatcherControls.cs` for watcher start/stop; `UiProcessCleanup.cs` for killing monitor process trees; `MonitorSession.PresentMon.cs` for PresentMon lifecycle; `MonitorSession.Status.cs` and `ReportStatus.cs` for status JSON shape; `ReportOpen.cs` for browser/open-marker behavior; `FrameScopeDiagnostics.Redaction.cs` for privacy; `FrameScopeDiagnostics.Retention.cs` for cleanup policy; `FrameScopeReportGenerator.cs` for report CLI/data shape; `FrameScopeReportGenerator.Html.cs` for embedded report UI.

Verification notes:

- Intermediate `build.ps1` checks were run after each split stage and passed after fixing mechanical duplicate-model and P/Invoke attribute placement issues.
- Full final verification for this stage is recorded in the assistant response for this run.
  - Final residual process check: PASS, no matching FrameScopeMonitor/PresentMon/TslGame/Valorant/CS2/FakePresentMon/GameLite processes after excluding the check command itself.
  - `C:\Program Files\Git\cmd\git.exe diff --check`: PASS with line-ending warnings only for `README.md`, `build.ps1`, and `framescope-config.example.json`.

## Stage 29-32 - Final architecture split continuation

Goal:

- Continue the mechanical split until the remaining high-conflict UI, report, and sampler files have clear ownership.
- Preserve behavior, UI text, CSV/report JSON shape, monitoring semantics, and GameLite/WMI boundaries.

Plans added:

- `docs\superpowers\plans\2026-05-15-framescope-report-generator-entry-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-report-and-target-pages-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-visual-helpers-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-sampler-helpers-split.md`

Split results:

- `src\reporting\FrameScopeReportGenerator.cs` now keeps report constants, `Main`, and `Generate` orchestration. Models moved to `.Models.cs`, CLI helpers to `.Cli.cs`, progress wrapper to `.Progress.cs`, and manifest diagnostic lookup to `.Diagnostics.cs`.
- `src\ui\FrameScopeReportPage.cs` now keeps only the report page entry. Report layout moved to `.Layout.cs`, detail text/latest-report lookup to `.Detail.cs`, and report actions to `.Actions.cs`.
- `src\app\FrameScopeNativeMonitor.PageTargets.cs` now keeps only target page entry. Target list/settings layout moved to `.PageTargets.Layout.cs`, grid creation/validation/checkbox painting to `.PageTargets.Grid.cs`, and process/action-row controls to `.PageTargets.Actions.cs`.
- `src\app\FrameScopeNativeMonitor.UiVisualHelpers.cs` now keeps only basic shared helpers. Cards moved to `.UiVisualCards.cs`, section/list helpers to `.UiVisualSections.cs`, and button factories/palettes to `.UiVisualButtons.cs`.
- `src\ui\FrameScopeReferenceSidebar.Drawing.cs` now keeps sidebar paint entry points. Compact drawing moved to `.CompactDrawing.cs`, full reference drawing to `.ReferenceDrawing.cs`, and logo/text helpers to `.LogoDrawing.cs`.
- `src\monitoring\FrameScopeProcessSampler.cs` now keeps process sampler entry, loop, grouped row output, and alert output. Models/Win32 counters moved to `.Models.cs`, process selection helpers to `.Selection.cs`, and args/CSV helpers to `.IO.cs`.
- `src\monitoring\FrameScopeSystemSampler.cs` now keeps system sampler entry and loop. GPU/perf models moved to `.Models.cs`, performance counters to `.PerfCounters.cs`, NVIDIA SMI parsing to `.Gpu.cs`, process checks to `.Processes.cs`, and args/CSV helpers to `.IO.cs`.
- `build.ps1` was updated for every new source file. `tests\Build-FrameScopeTests.ps1` did not need sampler/report UI source-list changes because those test executables do not compile the main app or sampler exes.

Latest ownership rules:

- UI design conversations should edit `FrameScopeUiTheme.cs`, shared `src\ui\FrameScope*.cs` controls, `UiVisualCards.cs`, `UiVisualSections.cs`, `UiVisualButtons.cs`, `UiReportProgress.cs`, `UiScreenshots.cs`, `UiStatusDisplay.cs`, page layout files, and `FrameScopeReportPage.Layout.cs`.
- UI interaction conversations should edit `UiRouting.cs`, `UiConfigActions.cs`, `UiProcessPicker.cs`, `UiWatcherControls.cs`, `UiProcessCleanup.cs`, `UiStatusRefresh.cs`, `UiDiagnosticActions.cs`, `PageTargets.Grid.cs`, `PageTargets.Actions.cs`, `FrameScopeReportPage.Actions.cs`, `FrameScopeReportPage.Detail.cs`, `FrameScopeUiState.cs`, and `FrameScopeLiveData.cs`.
- Backend monitoring conversations should edit `Watcher.cs`, `MonitorSession*.cs`, `ReportOrchestration*.cs`, `ReportStatus.cs`, `ReportOpen.cs`, `src\core\`, `src\monitoring\FrameScopeProcessSampler*.cs`, `src\monitoring\FrameScopeSystemSampler*.cs`, and `src\diagnostics\FrameScopeDiagnostics*.cs`.
- Report generator conversations should edit `src\reporting\FrameScopeReportGenerator*.cs` according to responsibility: orchestration, models, CLI, progress, diagnostics lookup, PresentMon, system data, process data, analysis, metadata, CSV, or HTML template.
- Exclusive/high-conflict files: `build.ps1`, `FrameScopeNativeMonitor.cs`, `UiRouting.cs`, `UiWatcherControls.cs`, `UiProcessCleanup.cs`, `MonitorSession.PresentMon.cs`, `MonitorSession.Status.cs`, `ReportStatus.cs`, `ReportOpen.cs`, `FrameScopeDiagnostics.Redaction.cs`, `FrameScopeDiagnostics.Retention.cs`, `FrameScopeReportGenerator.cs`, `FrameScopeReportGenerator.Html.cs`, `FrameScopeProcessSampler.cs`, `FrameScopeSystemSampler.cs`, `FrameScopeReferenceSidebar.LogoDrawing.cs`, and `FrameScopeUiTheme.cs`.

Current largest files after Stage 29-32:

- `src\app\FrameScopeNativeMonitor.MonitorSession.cs`: 338 lines, monitor-session orchestration.
- `src\app\FrameScopeNativeMonitor.PageLive.cs`: 271 lines, live page layout/lifecycle.
- `src\app\FrameScopeNativeMonitor.Watcher.cs`: 268 lines, watcher loop/orchestration.
- `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs`: 243 lines, report recovery/generator invocation.
- `src\app\FrameScopeNativeMonitor.ReportOpen.cs`: 239 lines, report open/browser fallback.
- Remaining large files are now mostly single-responsibility orchestration modules rather than mixed UI/design/backend files.

Verification notes:

- Intermediate `build.ps1` after report entry split: PASS.
- Intermediate `build.ps1` after report/target page split: PASS.
- Intermediate `build.ps1` after visual helper split: PASS.
- Intermediate `build.ps1` after process sampler split: PASS.
- Intermediate `build.ps1` after system sampler split: PASS.
- Full final verification:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
  - `.\tests\FrameScopeConfigStoreTests.exe`: PASS.
  - `.\tests\FrameScopeCapturePlannerTests.exe`: PASS.
  - `.\tests\FrameScopeReportProgressTests.exe`: PASS.
  - `.\tests\FrameScopeDiagnosticsTests.exe`: PASS.
  - `.\tests\FrameScopePubgSimulatorTests.exe`: PASS.
  - `.\tests\FrameScopeUiStateTests.exe`: PASS.
  - `node .\tests\chart-sampling-tests.js`: PASS.
  - `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario stable -DurationSeconds 4`: PASS, `monitorExit=0`, `reportExit=0`, `presentMonCsvRows=240`, `hasFrameData=true`, `reportKind=full`.
  - UI screenshots: overview/settings/live PASS, generated as `artifacts\stage32-overview.png`, `artifacts\stage32-settings.png`, and `artifacts\stage32-live.png`.
  - Target page screenshot: known DataGridView screenshot harness issue reproduced; exact `FrameScopeMonitor.exe --ui-page targets --ui-screenshot ...stage32-targets.png` process was stopped after timeout and no PNG was written. This is not treated as main app behavior failure.
  - Residual process check: PASS, no matching FrameScopeMonitor/PresentMon/TslGame/Valorant/CS2/FakePresentMon/GameLite processes.
  - `C:\Program Files\Git\cmd\git.exe diff --check`: PASS with LF/CRLF warnings only for `README.md`, `build.ps1`, and `framescope-config.example.json`.

## Stage 33 - Residual UI/interaction/backend boundary cleanup

Goal:

- Finish the small remaining mixed-responsibility seams after the Stage 29-32 split.
- Keep behavior unchanged while making report page layout/actions, live page layout/lifecycle/log, and report-open browser/status responsibilities independently editable.

Plan added:

- `docs\superpowers\plans\2026-05-15-framescope-residual-boundary-split.md`

Split results:

- `src\ui\FrameScopeReportPage.Layout.cs` now creates report-page controls and delegates button wiring to action binding helpers.
- `src\ui\FrameScopeReportPage.Actions.cs` owns report-page `Click +=` binding and report actions: latest report, data root, diagnostic report, history, selected report folder, selected report open, support bundle, selected report regeneration, and refresh.
- `src\app\FrameScopeNativeMonitor.PageLive.cs` is now an ownership note only.
- `src\app\FrameScopeNativeMonitor.PageLive.Layout.cs` owns the live page visual structure, charts, and metric cards.
- `src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs` owns the live refresh timer and `RefreshLivePage` lifecycle.
- `src\app\FrameScopeNativeMonitor.PageLive.Log.cs` owns live log pause, clear, and display behavior.
- `src\ui\FrameScopeLiveData.Csv.cs` owns CSV helpers used by live snapshot loading.
- `src\app\FrameScopeNativeMonitor.ReportOpen.cs` now keeps report/path open entry points.
- `src\app\FrameScopeNativeMonitor.ReportOpen.Browser.cs` owns default browser launch, explicit browser fallback discovery, and registry browser command parsing.
- `src\app\FrameScopeNativeMonitor.ReportOpen.Status.cs` owns report-open status writes.
- `src\app\FrameScopeNativeMonitor.ReportStatus.cs` was inspected and left status/progress/history-only.
- `src\reporting\FrameScopeReportGenerator.Html.cs` was inspected and left for a dedicated future report-template plan.
- `build.ps1` was updated to compile the new live-page, live-data CSV, and report-open partial files. `tests\Build-FrameScopeTests.ps1` did not need changes because current test targets do not compile those main-app partial files.

Parallel ownership rules:

- UI design conversations can edit `FrameScopeReportPage.Layout.cs` and `PageLive.Layout.cs` for visual-only changes.
- UI interaction conversations should edit `FrameScopeReportPage.Actions.cs`, `PageLive.Lifecycle.cs`, and `PageLive.Log.cs` for button, timer, page refresh, pause, or clear behavior.
- Backend/report-open conversations should edit `ReportOpen.cs`, `ReportOpen.Browser.cs`, `ReportOpen.Status.cs`, and `ReportStatus.cs` according to responsibility.
- Exclusive/high-conflict files remain: `build.ps1`, `FrameScopeReportPage.Actions.cs`, `PageLive.Lifecycle.cs`, `PageLive.Log.cs`, `ReportOpen.Browser.cs`, `ReportOpen.Status.cs`, `ReportStatus.cs`, and `FrameScopeReportGenerator.Html.cs`.

Verification notes:

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
- `.\tests\FrameScopeUiStateTests.exe`: PASS.
- `.\tests\FrameScopeReportProgressTests.exe`: PASS.
- `node .\tests\chart-sampling-tests.js`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- UI screenshots generated successfully for `artifacts\stage33-overview.png`, `artifacts\stage33-settings.png`, `artifacts\stage33-live.png`, and `artifacts\stage33-reports.png`; all are 1600x1000 PNG files with nonblank sampled pixels.
- Report page button wiring check: PASS. Layout delegates to action binding helpers; all report buttons still call real handlers instead of static placeholders.
- Stable PUBG simulator scenario: PASS, `monitorExit=0`, `reportExit=0`, `presentMonCaptureMode=process_name`, `presentMonCsvRows=240`, `hasFrameData=true`, `reportKind=full`.

Remaining:

- `src\reporting\FrameScopeReportGenerator.Html.cs` remains the main large exclusive file and should be handled in a dedicated report-template split only after a separate plan.
- `ReportOpen` is now focused enough for normal maintenance; only split further if browser candidate discovery grows new platform-specific logic.

## Stage 34 - Report HTML template split

Goal:

- Split the single embedded report HTML template into focused partial files.
- Keep generated HTML, CSS, JavaScript, chart sampling semantics, report data shape, manifest writing, progress writing, and report-open behavior unchanged.

Plan added:

- `docs\superpowers\plans\2026-05-15-framescope-report-html-template-split.md`

Split results:

- `src\reporting\FrameScopeReportGenerator.Html.cs` now keeps only `MakeHtml()` and the high-level template assembly order.
- `src\reporting\FrameScopeReportGenerator.Html.Layout.cs` owns document opening, body wrapper opening, and document closing fragments.
- `src\reporting\FrameScopeReportGenerator.Html.Styles.cs` owns the unchanged embedded `<style>` block.
- `src\reporting\FrameScopeReportGenerator.Html.Sections.cs` owns the static report body fragments: sidebar, main header, toolbar, chart surface, and summary panels.
- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs` owns the unchanged `framescope-interactive-data.js` include and embedded chart/interaction JavaScript.
- `build.ps1` now compiles the new report HTML partial files into `FrameScopeReportGenerator.exe`.

Parallel ownership rules:

- Report template layout work can edit `FrameScopeReportGenerator.Html.Layout.cs` and `FrameScopeReportGenerator.Html.Sections.cs`.
- Report template styling work can edit `FrameScopeReportGenerator.Html.Styles.cs`.
- Report chart interaction work can edit `FrameScopeReportGenerator.Html.Scripts.cs`, but chart sampling semantics still require `tests\chart-sampling-tests.js`.
- `FrameScopeReportGenerator.Html.cs` should stay exclusive only when changing template assembly order.
- `build.ps1` remains exclusive when adding or removing C# source files.

Verification notes:

- Initial mechanical split compared the original template string with the reassembled fragments before writing files: PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
- `node .\tests\chart-sampling-tests.js`: PASS.
- `.\tests\FrameScopeUiStateTests.exe`: PASS.
- `.\tests\FrameScopeReportProgressTests.exe`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings, 0 errors.
- Stable PUBG simulator: PASS, `monitorExit=0`, `reportExit=0`, `presentMonCsvRows=240`, `hasFrameData=true`, `reportKind=full`.
- Generated report smoke: PASS, `framescope-interactive-report.html` and `framescope-interactive-data.js` exist; HTML contains chart canvas, gauges, process rows, summary rows, data include, and chart sampling script.
- Edge headless report screenshot: PASS, `artifacts\stage34-report-template-split.png`, 1600x1000, nonblank sampled pixels.
- `C:\Program Files\Git\cmd\git.exe diff --check`: PASS with existing LF/CRLF warnings for `README.md`, `build.ps1`, and `framescope-config.example.json`.
- Residual process check: PASS, no matching FrameScopeMonitor/PresentMon/TslGame/Valorant/CS2/FakePresentMon/GameLite processes.

## Stage 35 - GameLite standalone empty snapshot marker fix

Goal:

- Keep the standalone GameLite active-session marker reliable even when game start finds no eligible background process to throttle.
- Preserve snapshot-only restore and avoid reintroducing any FrameScope dependency.

Changes:

- Updated `..\gamelite-auto-lightweight\Enter-GameLite.ps1` so an empty snapshot is written as `[]` instead of relying on PowerShell pipeline JSON output for an empty array.
- Updated `..\gamelite-auto-lightweight\tests\gamelite-standalone-tests.ps1` to run a no-target Enter/Exit behavior check: Enter must create the empty state marker, and Exit must remove it.
- Documented the empty snapshot marker rule in the standalone GameLite docs and this FrameScope handoff.

Verification:

- Full verification for this stage is recorded in the assistant response for this run.
