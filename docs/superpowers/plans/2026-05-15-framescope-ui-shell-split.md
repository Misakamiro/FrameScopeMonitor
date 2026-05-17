# FrameScope UI Shell Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `src/app/FrameScopeNativeMonitor.UiShell.cs` into focused partial-class files so UI shell, page routing, visual helper controls, report progress UI, screenshot harness, and status display can be edited independently.

**Architecture:** Keep behavior unchanged and preserve all existing method bodies. Keep `BuildUi`, `BuildSidebar`, `EnableDarkTitleBar`, and `BuildHeader` in `.UiShell.cs`; move fields and helper groups into nearby partial files.

**Tech Stack:** C# WinForms partial class compiled by `build.ps1`, existing UI screenshot harness and UI state tests.

---

### Task 1: Split UI Shell Partial

**Files:**
- Create: `src/app/FrameScopeNativeMonitor.UiFields.cs`
- Create: `src/app/FrameScopeNativeMonitor.UiRouting.cs`
- Create: `src/app/FrameScopeNativeMonitor.UiVisualHelpers.cs`
- Create: `src/app/FrameScopeNativeMonitor.UiReportProgress.cs`
- Create: `src/app/FrameScopeNativeMonitor.UiScreenshots.cs`
- Create: `src/app/FrameScopeNativeMonitor.UiStatusDisplay.cs`
- Modify: `src/app/FrameScopeNativeMonitor.UiShell.cs`

- [ ] **Step 1: Move UI fields**

Move form/page/control/timer/report-progress fields into `.UiFields.cs`.

- [ ] **Step 2: Move page routing**

Move `ShowPage`, `ResetPageControls`, `NavButton`, and `SetActiveNavButton` into `.UiRouting.cs`.

- [ ] **Step 3: Move visual helper factories**

Move card/button/list/label helper methods into `.UiVisualHelpers.cs`.

- [ ] **Step 4: Move report progress UI**

Move `BuildReportProgressCard`, `SetReportProgress`, and `ApplyReportProgressWidth` into `.UiReportProgress.cs`.

- [ ] **Step 5: Move screenshots**

Move `PrintWindow`, `CaptureUiScreenshot`, `TryPrintWindow`, and `CaptureSidebarScreenshot` into `.UiScreenshots.cs`.

- [ ] **Step 6: Move status display**

Move `FadeIn`, `SetStatus`, and `SetStatusPill` into `.UiStatusDisplay.cs`.

### Task 2: Update Build Inputs

**Files:**
- Modify: `build.ps1`

- [ ] **Step 1: Compile new UI partial files**

Add the new app UI partial files immediately after `FrameScopeNativeMonitor.UiShell.cs`.

### Task 3: Verify Stage 25

Run `build.ps1` immediately after the split, then include the full test and screenshot set in final verification.
