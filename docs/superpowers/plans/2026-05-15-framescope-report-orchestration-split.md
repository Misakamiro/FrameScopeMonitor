# FrameScope Report Orchestration Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `src/app/FrameScopeNativeMonitor.ReportOrchestration.cs` so report generation, status/history helpers, and report-opening browser fallback logic have separate files.

**Architecture:** Keep data models in `.Models.cs`, recovery and generator invocation in `.ReportOrchestration.cs`, status/history helpers in `.ReportStatus.cs`, and browser/open-marker helpers in `.ReportOpen.cs`. Preserve existing method bodies.

**Tech Stack:** C# WinForms app partial class, `FrameScopeReportGenerator.exe`, report progress JSON, history JSONL.

---

### Task 1: Split Report Orchestration

**Files:**
- Create: `src/app/FrameScopeNativeMonitor.ReportOrchestration.Models.cs`
- Create: `src/app/FrameScopeNativeMonitor.ReportStatus.cs`
- Create: `src/app/FrameScopeNativeMonitor.ReportOpen.cs`
- Modify: `src/app/FrameScopeNativeMonitor.ReportOrchestration.cs`

- [ ] **Step 1: Move models**

Move `FrameScopeHistoryEntry` and `ReportGenerationResult` unchanged into `.Models.cs`.

- [ ] **Step 2: Keep generator orchestration**

Keep `RecoverStaleMissingReports`, `EnsureReportForCompletedRun`, `HasAnyMonitorCsv`, `LatestMonitorCsvWriteTime`, `RunReportGeneration`, `ReadReportManifest`, and `WriteReportLog` in `.ReportOrchestration.cs`.

- [ ] **Step 3: Move status and history helpers**

Move `UpdateStatusAfterReportGeneration`, `UpdateStatusFromReportProgress`, `LatestRunDirectory`, `ReadStatusDictionary`, `StatusString`, `StatusInt`, `StatusBool`, `AddHistoryEntry`, `ShouldOpenReport`, `ShouldAutoOpenCompletedReport`, and `LatestHistory` into `.ReportStatus.cs`.

- [ ] **Step 4: Move report opening helpers**

Move `TryOpenPath`, `TryOpenHtmlWithBrowsers`, `TryOpenHtmlWithBrowser`, `GetBrowserOpenArguments`, `GetBrowserCandidates`, `IsSupportedBrowserCandidate`, registered-browser helpers, `ExtractExecutableFromCommand`, `TryOpenReport`, and `MarkReportOpened` into `.ReportOpen.cs`.

### Task 2: Update Build Inputs

**Files:**
- Modify: `build.ps1`

- [ ] **Step 1: Add new report orchestration partial files**

Compile `.Models.cs`, `.ReportStatus.cs`, and `.ReportOpen.cs` after `.ReportOrchestration.cs`.

### Task 3: Verify

Run `build.ps1` immediately, then include report tests and simulator in final verification.
