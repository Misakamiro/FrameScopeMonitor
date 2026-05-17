# FrameScope Report And Target Pages Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the remaining large report and target page files into clearer UI layout, UI detail/grid, and interaction action files.

**Architecture:** Keep page entry methods in the existing page files and move focused helper groups into C# partial files under the same class. This keeps behavior stable while giving future UI design and UI interaction conversations separate files to edit.

**Tech Stack:** C# WinForms partial class files compiled by `build.ps1`, existing UI screenshot harness and regression tests.

---

### File Structure

- Modify: `src/ui/FrameScopeReportPage.cs`
  - Keep `BuildReportsPage` only.
- Create: `src/ui/FrameScopeReportPage.Layout.cs`
  - Own `ReportActionsCard`, `ReportListCard`, and `ReportDetailCard`.
- Create: `src/ui/FrameScopeReportPage.Detail.cs`
  - Own `BuildReportDetailText`, `UpdateReportDetailUi`, and `LatestReportPath`.
- Create: `src/ui/FrameScopeReportPage.Actions.cs`
  - Own report opening, history, selected report folder, selected diagnostics, and selected report regeneration actions.
- Modify: `src/app/FrameScopeNativeMonitor.PageTargets.cs`
  - Keep `BuildTargetsPage` only.
- Create: `src/app/FrameScopeNativeMonitor.PageTargets.Layout.cs`
  - Own `TargetListCard` and `TargetSettingsCard`.
- Create: `src/app/FrameScopeNativeMonitor.PageTargets.Grid.cs`
  - Own `CreateTargetGrid` and `DrawTargetGridCheckboxCell`.
- Create: `src/app/FrameScopeNativeMonitor.PageTargets.Actions.cs`
  - Own `BuildTargetActionRow`, `RoundedComboHost`, and `DrawDarkComboItem`.
- Modify: `build.ps1`
  - Add all new page partial files to the main `FrameScopeMonitor.exe` compile list.

### Task 1: Split Report Page

- [ ] **Step 1: Move report layout helpers**

Move report action/list/detail card construction into `FrameScopeReportPage.Layout.cs`.

- [ ] **Step 2: Move report detail helpers**

Move detail text, detail label refresh, and latest report lookup into `FrameScopeReportPage.Detail.cs`.

- [ ] **Step 3: Move report actions**

Move report open/history/folder/diagnostic/regenerate actions into `FrameScopeReportPage.Actions.cs`.

### Task 2: Split Target Page

- [ ] **Step 1: Move target layout helpers**

Move `TargetListCard` and `TargetSettingsCard` into `FrameScopeNativeMonitor.PageTargets.Layout.cs`.

- [ ] **Step 2: Move target grid helpers**

Move grid creation and checkbox cell drawing into `FrameScopeNativeMonitor.PageTargets.Grid.cs`.

- [ ] **Step 3: Move target action helpers**

Move action row, process combo host, and combo item drawing into `FrameScopeNativeMonitor.PageTargets.Actions.cs`.

### Task 3: Build And Validate

- [ ] **Step 1: Update `build.ps1`**

Add new `.cs` files to the existing compile list.

- [ ] **Step 2: Run build**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Expected: exit 0.

- [ ] **Step 3: Run required UI checks**

Generate overview/settings/live screenshots. Attempt targets only with timeout handling because the existing DataGridView screenshot harness can hang.

