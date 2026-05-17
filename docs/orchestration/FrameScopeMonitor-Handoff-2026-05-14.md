# FrameScope Monitor Handoff - 2026-05-14

This file is for the next coordinator conversation to load first.

## Project Root

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Current Project State

The project has been reorganized without rewriting business logic.

### Directory Layout

- `src\app\`: main program, main window, page composition, watcher, monitor-session orchestration.
- `src\ui\`: UI components, theme, page UI, live page data, report page.
- `src\core\`: configuration, capture planning, report progress.
- `src\monitoring\`: process sampler, system sampler.
- `src\diagnostics\`: diagnostic report, log cleanup, privacy redaction.
- `src\reporting\`: HTML report generator.
- `scripts\lightweight\`: automatic lightweight script core implementation.

### Key Moves

- `FrameScopeNativeMonitor.cs` -> `src\app\FrameScopeNativeMonitor.cs`
- `FrameScopeUiComponents.cs` -> `src\ui\FrameScopeUiComponents.cs`
- `FrameScopeUiState.cs` -> `src\ui\FrameScopeUiState.cs`
- `FrameScopeLiveData.cs` -> `src\ui\FrameScopeLiveData.cs`
- `FrameScopeReportPage.cs` -> `src\ui\FrameScopeReportPage.cs`
- `FrameScopeConfigStore.cs` -> `src\core\FrameScopeConfigStore.cs`
- `FrameScopeCapturePlanner.cs` -> `src\core\FrameScopeCapturePlanner.cs`
- `FrameScopeReportProgress.cs` -> `src\core\FrameScopeReportProgress.cs`
- `FrameScopeProcessSampler.cs` -> `src\monitoring\FrameScopeProcessSampler.cs`
- `FrameScopeSystemSampler.cs` -> `src\monitoring\FrameScopeSystemSampler.cs`
- `FrameScopeDiagnostics.cs` -> `src\diagnostics\FrameScopeDiagnostics.cs`
- `FrameScopeReportGenerator.cs` -> `src\reporting\FrameScopeReportGenerator.cs`

### Lightweight Script State

The automatic lightweight script core moved to `scripts\lightweight\`.

The root directory keeps thin `.ps1` wrappers so old WMI triggers, `.cmd` launchers, and manual runs keep working.

## Documentation

Load these first depending on the requested work:

- Project overview: `docs\FrameScopeMonitor-Project-Overview.md`
- UI: `docs\modules\software-ui.md`
- UI interaction: `docs\modules\ui-interactions.md`
- Backend monitoring: `docs\modules\backend-monitoring.md`
- Lightweight script: `docs\modules\lightweight-script.md`
- Progress log: `docs\FrameScopeMonitor-progress.md`
- Next prompt: `docs\FrameScopeMonitor-next-prompt.md`
- Cleanup plan: `docs\superpowers\plans\2026-05-13-project-structure-cleanup.md`

Also synchronized:

- `AGENTS.md`
- `README.md`

## Last Reported Validation

- `build.ps1`: PASS
- `tests\Build-FrameScopeTests.ps1`: PASS
- 6 C# test executables: PASS
- `node tests\chart-sampling-tests.js`: PASS
- `dotnet build FrameScopeRenderProbe`: PASS, 0 warnings, 0 errors
- PUBG stable simulation chain: PASS, `monitorExit=0`, `reportExit=0`, `hasFrameData=true`
- Lightweight root wrapper and `scripts\lightweight` core scripts: 7 syntax parse checks PASS
- `Check-GameLiteAutoTrigger.ps1` root wrapper and moved script direct run: PASS, existing WMI trigger visible
- Root `.cs` count: 0
- `git diff --check`: PASS, only LF/CRLF notices
- No leftover FrameScope, PresentMon, TslGame, Valorant, CS2, FakePresentMon, or GameLite processes

## Known Risk

Target page screenshot harness still has an old DataGridView issue:

`--ui-page targets --ui-screenshot`

It returns but does not write a PNG and can leave a screenshot process behind. The prior worker stopped the leftover process. Build, tests, and monitoring chain were reported unaffected.

Installing or removing lightweight WMI triggers requires administrator rights. The prior worker did not execute WMI write install/remove actions; only syntax and status checks were performed.

## Coordinator Department Map

Future coordinator prompts should route work as follows:

### UI Design Prompt And Skill Dialog

Purpose:
Write UI design prompts and skill choices. Use when the user provides reference images or wants a design direction.

Read first:
- `docs\modules\software-ui.md`

Skills:
- `ui-ux-pro-max`
- `design-system`
- `plan-design-review`
- `design-review`
- `writing-plans`
- `verification-before-completion`

### UI Frontend Implementation Dialog

Purpose:
Implement visual UI, layout, theme, cards, buttons, sidebars, tables, chart containers, and page visual structure.

Read first:
- `docs\modules\software-ui.md`
- `docs\modules\ui-interactions.md` if any control behavior is touched

Skills:
- `ui-ux-pro-max`
- `design-system`
- `design-review`
- `review`
- `health`
- `verification-before-completion`

### UI Interaction Prompt And Skill Dialog

Purpose:
Write prompts for page switching, button behavior, settings persistence, table editing, process selection, real-time monitor entry/exit, logs, and graph refresh rules.

Read first:
- `docs\modules\ui-interactions.md`

Skills:
- `diagnose`
- `review`
- `improve-codebase-architecture`
- `tdd`
- `verification-before-completion`

### UI Interaction Frontend Implementation Dialog

Purpose:
Implement interaction logic and connect UI controls to real state.

Read first:
- `docs\modules\ui-interactions.md`
- `docs\modules\software-ui.md`

Skills:
- `diagnose`
- `review`
- `tdd`
- `health`
- `verification-before-completion`

### Backend Prompt And Skill Dialog

Purpose:
Write prompts for process detection, FPS capture, frame-time logic, CPU/GPU/memory sampling, reports, logs, and diagnostics.

Read first:
- `docs\modules\backend-monitoring.md`

Skills:
- `diagnose`
- `review`
- `improve-codebase-architecture`
- `tdd`
- `health`
- `verification-before-completion`

### Backend Implementation Dialog

Purpose:
Implement backend monitoring and reporting changes.

Read first:
- `docs\modules\backend-monitoring.md`
- `docs\modules\lightweight-script.md` if lightweight scripts are touched

Skills:
- `diagnose`
- `review`
- `tdd`
- `health`
- `verification-before-completion`

### Tester Prompt And Skill Dialog

Purpose:
Write test plans and skill lists for visual, functional, backend, simulator, and packaging verification.

Read first:
- `docs\FrameScopeMonitor-Project-Overview.md`
- relevant module docs

Skills:
- `health`
- `verification-before-completion`
- `diagnose`
- `review`

### Tester Dialog

Purpose:
Execute tests, screenshots, build checks, simulator checks, process cleanup, and manual verification instructions.

Read first:
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\FrameScopeMonitor-progress.md`

Skills:
- `health`
- `verification-before-completion`
- `diagnose`

### Bugfix Skill-Design Dialog

Purpose:
Create targeted bugfix prompts and select skills for downstream implementation.

Read first:
- module doc for the affected area
- `docs\FrameScopeMonitor-progress.md`

Skills:
- `diagnose`
- `review`
- `tdd`
- `verification-before-completion`

### Bugfix And Final Packaging Dialog

Purpose:
Apply fixes, run full validation, package final deliverables.

Read first:
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\FrameScopeMonitor-progress.md`
- `docs\FrameScopeMonitor-next-prompt.md`

Skills:
- `diagnose`
- `review`
- `health`
- `verification-before-completion`
- `ship`

## Coordinator Reminder

When the next conversation starts, do not restart from zero. Use this file plus the module docs. If a fact is critical and cheap to verify, verify it in the project before writing a downstream prompt.
