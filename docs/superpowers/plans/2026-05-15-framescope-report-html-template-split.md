# FrameScope Report HTML Template Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the embedded report HTML template into focused `FrameScopeReportGenerator.Html.*.cs` partial files without changing generated report behavior.

**Architecture:** Keep `MakeHtml()` as the single report-template entry point and move static string fragments behind private helper methods on the same partial `FrameScopeReportGenerator` class. The split is mechanical: page layout, CSS, JavaScript, and static report sections move to separate files while report data generation, chart sampling semantics, manifest shape, progress writing, and report-open behavior remain untouched.

**Tech Stack:** C# partial classes compiled by `build.ps1`, native HTML/CSS/JavaScript emitted by `FrameScopeReportGenerator.exe`, existing Node chart sampling regression tests, existing simulator/report generation validation.

---

## Current `FrameScopeReportGenerator.Html.cs` Responsibilities

- Contains the private `MakeHtml()` method used by `FrameScopeReportGenerator.Generate()` when writing `charts\framescope-interactive-report.html`.
- Embeds the entire report document as one verbatim C# string.
- Owns the HTML document shell: doctype, `<html>`, `<head>`, body shell, sidebar, main report area, toolbar, chart canvas, summary panels, and closing tags.
- Owns all embedded CSS in the `<style>` block.
- Owns all embedded JavaScript after `framescope-interactive-data.js`, including chart sampling, render modes, zoom/pan, hover tooltip, PNG export, static data binding, tab switching, and event wiring.
- Owns static HTML section containers for hardware, run metadata, chart toolbar, process peak rows, and FPS summary rows.
- Does not currently own C# HTML escaping helpers. Escaping is JavaScript-side `esc(v)` inside the embedded script.

## Proposed Files

- `src\reporting\FrameScopeReportGenerator.Html.cs`
  - Keep `MakeHtml()` only.
  - Concatenate fragment helpers in the same order as the current single template.
- `src\reporting\FrameScopeReportGenerator.Html.Layout.cs`
  - New helper methods for the doctype/head opening, body layout wrapper, and document closing.
- `src\reporting\FrameScopeReportGenerator.Html.Styles.cs`
  - New helper method for the unchanged `<style>` block.
- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`
  - New helper method for the unchanged `<script src='framescope-interactive-data.js'></script>` and embedded chart/interaction JavaScript block.
- `src\reporting\FrameScopeReportGenerator.Html.Sections.cs`
  - New helper methods for static report body fragments: sidebar, topbar/tabs, chart controls, chart surface, and summary panels.

No `Html.Escape.cs` file is planned because there are no C# escaping helpers in the current file.

## Methods Moved Without Behavior Changes

- Move only literal string content from the original `MakeHtml()` into new private helper methods.
- Preserve HTML/CSS/JavaScript text exactly except for fragment boundaries and string concatenation.
- Keep all JavaScript function names and event wiring unchanged, including `samplingProfile`, `getRenderablePoints`, `drawSpikeMarkers`, `zoomChart`, `startPan`, `endPan`, `exportChartPng`, and `initStatic`.
- Keep `MakeHtml()` private and keep all new helpers private so no external caller surface changes.

## Build List Changes

Add these files to the `FrameScopeReportGenerator.exe` compile block in `build.ps1`:

- `.\src\reporting\FrameScopeReportGenerator.Html.Layout.cs`
- `.\src\reporting\FrameScopeReportGenerator.Html.Styles.cs`
- `.\src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`
- `.\src\reporting\FrameScopeReportGenerator.Html.Sections.cs`

## Task Steps

### Task 1: Split Template Fragments

**Files:**
- Modify: `src\reporting\FrameScopeReportGenerator.Html.cs`
- Create: `src\reporting\FrameScopeReportGenerator.Html.Layout.cs`
- Create: `src\reporting\FrameScopeReportGenerator.Html.Styles.cs`
- Create: `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`
- Create: `src\reporting\FrameScopeReportGenerator.Html.Sections.cs`

- [x] Extract the current `MakeHtml()` verbatim string into exact fragments.
- [x] Rewrite `MakeHtml()` to concatenate fragment helpers.
- [x] Verify the assembled template text matches the original template text before continuing.

### Task 2: Update Build Input

**Files:**
- Modify: `build.ps1`

- [x] Add the four new report HTML partial files immediately before `FrameScopeReportGenerator.Html.cs` or next to it in the report-generator compile block.
- [x] Do not touch main app, sampler, GameLite, watcher, monitor-session, ReportOpen, or ReportStatus compile behavior.

### Task 3: Update Docs

**Files:**
- Modify: `docs\FrameScopeMonitor-progress.md`
- Modify: `docs\FrameScopeMonitor-next-prompt.md`
- Modify: `docs\modules\backend-monitoring.md`
- Modify: `docs\modules\software-ui.md`

- [x] Record the report-template split and the new ownership rules.
- [x] Mark `FrameScopeReportGenerator.Html.cs` as the report-template entry file rather than the large exclusive all-in-one template.
- [x] State that CSS, JavaScript, layout shell, and static sections can be edited in separate files.
- [x] Keep `build.ps1` exclusive for source list changes.

## Regression Test Commands

Run all of these after the split:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
node .\tests\chart-sampling-tests.js
.\tests\FrameScopeUiStateTests.exe
.\tests\FrameScopeReportProgressTests.exe
dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario stable -DurationSeconds 4
& "C:\Program Files\Git\cmd\git.exe" diff --check
```

Also check generated simulator output for:

- `monitorExit=0`
- `reportExit=0`
- `presentMonCsvRows` has a valid positive row count
- `hasFrameData=true`
- `reportKind=full`
- generated HTML report exists and opens as a file
- chart, summary, diagnostics/status note, process rows, and system/hardware sections are not blank or obviously broken

Finally run a residual process check for `FrameScopeMonitor`, `PresentMon`, `TslGame`, `Valorant`, `CS2`, `FakePresentMon`, `GameLite`, and abnormal leftover PowerShell processes.

## Risks And Rollback Points

- Main risk: accidentally changing the emitted HTML/CSS/JavaScript text while moving fragments. Mitigation: compare the assembled template with the original text before running the full build.
- Secondary risk: missing one new partial file from `build.ps1`. Mitigation: `build.ps1` must compile `FrameScopeReportGenerator.exe` after the source-list update.
- Chart risk: `tests\chart-sampling-tests.js` extracts JavaScript across all report generator files. If split boundaries hide `function samplingProfile` or `function updateLegend`, the Node test fails.
- Rollback point: revert only the new `FrameScopeReportGenerator.Html.*.cs` files, the reduced `FrameScopeReportGenerator.Html.cs`, the `build.ps1` source-list update, and docs updates from this plan. Do not revert unrelated prior project-structure changes.

## Scope Guard

- Do not modify GameLite, lightweight scripts, WMI triggers, watcher behavior, monitor-session lifecycle, ReportOpen, ReportStatus, chart sampling semantics, generated JSON shape, or report data analysis.
- Do not introduce a frontend framework, WebView, external JavaScript dependency, fake report feature, fake chart, or visual redesign.
