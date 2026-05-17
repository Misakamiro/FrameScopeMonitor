# FrameScope UI Interactions Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `src/app/FrameScopeNativeMonitor.UiInteractions.cs` into smaller partial files for UI helpers, config editing, process picker actions, watcher controls, background process cleanup, status refresh, and diagnostic actions.

**Architecture:** Preserve existing UI behavior and method bodies. Keep no static fake controls; all buttons continue to call the same real handlers.

**Tech Stack:** C# WinForms, WMI process reads, existing config store, diagnostics, watcher process launch.

---

### Task 1: Split UI Interaction Methods

**Files:**
- Create: `src/app/FrameScopeNativeMonitor.UiHelpers.cs`
- Create: `src/app/FrameScopeNativeMonitor.UiConfigActions.cs`
- Create: `src/app/FrameScopeNativeMonitor.UiProcessPicker.cs`
- Create: `src/app/FrameScopeNativeMonitor.UiWatcherControls.cs`
- Create: `src/app/FrameScopeNativeMonitor.UiProcessCleanup.cs`
- Create: `src/app/FrameScopeNativeMonitor.UiStatusRefresh.cs`
- Create: `src/app/FrameScopeNativeMonitor.UiDiagnosticActions.cs`
- Modify: `src/app/FrameScopeNativeMonitor.UiInteractions.cs`

- [ ] **Step 1: Move generic helpers**

Move `RecentHistoryEntries`, `FormatBytes`, `DefaultSampleIntervalText`, `ResolveCurrentDataRoot`, `EnabledTargetCount`, and `IsWatcherRunningQuiet` into `.UiHelpers.cs`.

- [ ] **Step 2: Move config actions**

Move `ReadGridConfig`, `SaveConfigFromGrid`, `ResetConfigToDefaultsFromUi`, and `BrowseDataRoot` into `.UiConfigActions.cs`.

- [ ] **Step 3: Move process picker actions**

Move `RefreshProcessList`, `AddSelectedProcess`, and `SelectedProcessNameFromPicker` into `.UiProcessPicker.cs`.

- [ ] **Step 4: Move watcher controls**

Move `IsWatcherRunning`, `StartWatcher`, and `StopWatcher` into `.UiWatcherControls.cs`.

- [ ] **Step 5: Move cleanup helpers**

Move `HasFrameScopeBackgroundProcesses`, `StopFrameScopeBackgroundProcesses`, `EnumerateFrameScopeBackgroundPids`, `ProcessInfo`, process map helpers, and `TryKillProcess` into `.UiProcessCleanup.cs`.

- [ ] **Step 6: Move status refresh**

Move `UpdateWatcherStatus`, `UpdateReportProgressUi`, `LocalizeProgressMessage`, and `LatestReportProgress` into `.UiStatusRefresh.cs`.

- [ ] **Step 7: Move diagnostics**

Move `OpenDataRoot`, `GenerateDiagnosticReportFromUi`, `OpenDiagnosticFolder`, and `SetStatusFromAnyThread` into `.UiDiagnosticActions.cs`.

### Task 2: Update Build Inputs

**Files:**
- Modify: `build.ps1`

- [ ] **Step 1: Compile new UI interaction partial files**

Add all new interaction partial files after `FrameScopeNativeMonitor.UiInteractions.cs`.

### Task 3: Verify

Run `build.ps1` immediately, then include full UI/state verification in final checks.
