# FrameScope Report Generator Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `src\reporting\FrameScopeReportGenerator.cs` into focused partial class files so report data parsing, analysis, metadata, and HTML template work can happen independently.

**Architecture:** Keep the same executable, class name, method names, JSON shape, HTML output, and CLI behavior. Use `internal static partial class FrameScopeReportGenerator` only; this is a mechanical move, not a report behavior rewrite.

**Tech Stack:** C#/.NET Framework compiler via `build.ps1`, `System.Web.Script.Serialization`, WMI hardware metadata, existing Node chart sampling regression, existing simulator and RenderProbe checks.

---

## File Structure

- Keep: `src\reporting\FrameScopeReportGenerator.cs`
  - Entry point, constants, shared model classes, `Generate`, progress helper, argument helper, latest-run lookup.
- Create: `src\reporting\FrameScopeReportGenerator.PresentMon.cs`
  - `ReadPresentMon`, track selection, PresentMon frame filtering.
- Create: `src\reporting\FrameScopeReportGenerator.SystemData.cs`
  - `ReadSystem`, system series projection, effective CPU frequency.
- Create: `src\reporting\FrameScopeReportGenerator.ProcessData.cs`
  - `ReadProcessMatrix`, per-process stats, matrix allocation helper.
- Create: `src\reporting\FrameScopeReportGenerator.Analysis.cs`
  - time alignment, FPS bucket generation, Fenwick-based low-window calculations, frame/stat math helpers.
- Create: `src\reporting\FrameScopeReportGenerator.Metadata.cs`
  - run metadata, capture diagnostics, hardware WMI metadata, JSON string extraction.
- Create: `src\reporting\FrameScopeReportGenerator.Csv.cs`
  - `CsvTable` parser.
- Create: `src\reporting\FrameScopeReportGenerator.Html.cs`
  - `MakeHtml` only. This keeps embedded report HTML/CSS/JS isolated from data parsing.
- Modify: `build.ps1`
  - Add all new `src\reporting\FrameScopeReportGenerator.*.cs` files to the `FrameScopeReportGenerator.exe` compile list.
- Modify docs:
  - `docs\modules\backend-monitoring.md`
  - `docs\FrameScopeMonitor-progress.md`
  - `docs\FrameScopeMonitor-next-prompt.md`

## Tasks

### Task 1: Baseline And Split

**Files:**
- Modify: `src\reporting\FrameScopeReportGenerator.cs`
- Create: `src\reporting\FrameScopeReportGenerator.PresentMon.cs`
- Create: `src\reporting\FrameScopeReportGenerator.SystemData.cs`
- Create: `src\reporting\FrameScopeReportGenerator.ProcessData.cs`
- Create: `src\reporting\FrameScopeReportGenerator.Analysis.cs`
- Create: `src\reporting\FrameScopeReportGenerator.Metadata.cs`
- Create: `src\reporting\FrameScopeReportGenerator.Csv.cs`
- Create: `src\reporting\FrameScopeReportGenerator.Html.cs`

- [ ] Record the original method map from `FrameScopeReportGenerator.cs`.
- [ ] Change the main class declaration to `internal static partial class FrameScopeReportGenerator`.
- [ ] Move method blocks mechanically by responsibility. Do not change method bodies except imports/class wrapper.
- [ ] Keep nested model/helper classes accessible to all partial files by leaving them in the main file.

### Task 2: Compile Wiring

**Files:**
- Modify: `build.ps1`

- [ ] Add the new report generator partial files to the `FrameScopeReportGenerator.exe` compiler invocation.
- [ ] Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`.
- [ ] Fix only compile errors caused by the mechanical split.

### Task 3: Docs

**Files:**
- Modify: `docs\modules\backend-monitoring.md`
- Modify: `docs\FrameScopeMonitor-progress.md`
- Modify: `docs\FrameScopeMonitor-next-prompt.md`

- [ ] Document the new report-generator file ownership.
- [ ] Mark `FrameScopeReportGenerator.Html.cs` as exclusive for embedded report UI/CSS/JS work.
- [ ] Mark data-parser/analysis files as backend/report-data ownership.

### Task 4: Full Verification

Run:

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

- [ ] Generate or confirm source UI screenshots for overview/settings/live/targets because build output changed.
- [ ] Check residual processes for FrameScopeMonitor, PresentMon, TslGame, Valorant, CS2, FakePresentMon, GameLite.

## Safety Rules

- Do not change report JSON keys, manifest keys, file names, or embedded chart JavaScript behavior.
- Do not move GameLite/lightweight scripts.
- Do not install/remove WMI triggers.
- Do not rewrite the report generator into new abstractions in this stage.
- Do not treat a no-frame diagnostic report as a full successful FPS capture.
