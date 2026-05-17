# FrameScope Report Generator Entry Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Continue the FrameScope Monitor architecture cleanup by shrinking the report generator entry file without changing generated report behavior.

**Architecture:** Keep `FrameScopeReportGenerator.cs` as the entry and `Generate` orchestration module. Move passive models, CLI helpers, progress writing, and manifest diagnostic lookup into focused partial files so later report-data and report-UI conversations can avoid touching the same entry file.

**Tech Stack:** C# partial classes compiled by `build.ps1`, existing WinForms/report generator build pipeline, existing regression tests.

---

### File Structure

- Modify: `src/reporting/FrameScopeReportGenerator.cs`
  - Keep `Brand`, `Colors`, `Main`, and `Generate`.
  - Remove passive nested model/helper blocks that can live in partial files.
- Create: `src/reporting/FrameScopeReportGenerator.Models.cs`
  - Own `PresentRecord`, `PresentTrack`, `PresentReadResult`, `SystemRow`, `ProcessMatrixResult`, `ProcessStat`, and `Fenwick`.
- Create: `src/reporting/FrameScopeReportGenerator.Cli.cs`
  - Own `GetArgValue` and `FindLatestRun`.
- Create: `src/reporting/FrameScopeReportGenerator.Progress.cs`
  - Own `WriteProgress`.
- Create: `src/reporting/FrameScopeReportGenerator.Diagnostics.cs`
  - Own `GetDiagnostic`.
- Modify: `build.ps1`
  - Add the new report generator partial files to the `FrameScopeReportGenerator.exe` compile list.
- Modify: docs
  - Update `docs/modules/backend-monitoring.md`, `docs/FrameScopeMonitor-progress.md`, and `docs/FrameScopeMonitor-next-prompt.md` after implementation and verification.

### Task 1: Create Report Generator Entry Partials

- [ ] **Step 1: Move passive nested models**

Create `src/reporting/FrameScopeReportGenerator.Models.cs` with the exact model/helper classes formerly in `FrameScopeReportGenerator.cs`.

- [ ] **Step 2: Move CLI helpers**

Create `src/reporting/FrameScopeReportGenerator.Cli.cs` containing `GetArgValue` and `FindLatestRun`.

- [ ] **Step 3: Move progress helper**

Create `src/reporting/FrameScopeReportGenerator.Progress.cs` containing `WriteProgress`.

- [ ] **Step 4: Move diagnostic helper**

Create `src/reporting/FrameScopeReportGenerator.Diagnostics.cs` containing `GetDiagnostic`.

- [ ] **Step 5: Trim entry file**

Remove the moved members from `FrameScopeReportGenerator.cs`, leaving entry/generation orchestration intact.

### Task 2: Build Script Update

- [ ] **Step 1: Update report generator compile list**

Add the four new partial files to the `FrameScopeReportGenerator.exe` source list in `build.ps1`.

- [ ] **Step 2: Run compile feedback loop**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Expected: exit 0. If compile fails, fix only mechanical missing-source/duplicate-member issues.

### Task 3: Documentation And Verification

- [ ] **Step 1: Update module ownership docs**

Record the new report generator ownership in backend monitoring docs and the next prompt.

- [ ] **Step 2: Run full verification**

Run the project-required verification set:

```powershell
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
C:\Program Files\Git\cmd\git.exe diff --check
```

Expected: all pass. `git diff --check` may report existing LF/CRLF warnings only.

- [ ] **Step 3: UI and residual checks**

Generate overview/settings/live screenshots. Attempt targets only if safe; if the known DataGridView screenshot harness hangs, stop only that exact screenshot process and record it as a harness issue. Check no FrameScope/PresentMon/game/GameLite residual processes remain.

