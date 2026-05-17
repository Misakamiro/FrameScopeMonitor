# FrameScope UI Components Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the large `src/ui/FrameScopeUiComponents.cs` visual-control file into focused UI design files without changing behavior.

**Architecture:** Keep all controls in the global namespace and preserve existing class names, constructors, properties, event names, drawing code, and access modifiers. Move classes mechanically by visual responsibility so UI design conversations can edit buttons, panels, reference sidebar, and live charts independently.

**Tech Stack:** C# WinForms, .NET Framework compiler through `build.ps1`, existing FrameScope test scripts and screenshot harness.

---

### Task 1: Split Pure UI Controls

**Files:**
- Create: `src/ui/FrameScopeRoundedDrawing.cs`
- Create: `src/ui/FrameScopePanels.cs`
- Create: `src/ui/FrameScopeButtons.cs`
- Create: `src/ui/FrameScopeStatusControls.cs`
- Create: `src/ui/FrameScopeReferenceSidebar.cs`
- Create: `src/ui/FrameScopeLiveChart.cs`
- Modify: `src/ui/FrameScopeUiComponents.cs`

- [ ] **Step 1: Move drawing helpers**

Move `FrameScopeRoundedDrawing` unchanged into `FrameScopeRoundedDrawing.cs`.

- [ ] **Step 2: Move panel controls**

Move `FrameScopeCardPanel`, `FrameScopeWorkspacePanel`, `FrameScopeSettingRowPanel`, `FrameScopeSidebarPanel`, and `FrameScopeRoundedTableLayoutPanel` unchanged into `FrameScopePanels.cs`.

- [ ] **Step 3: Move button controls**

Move `FrameScopeRoundedButton` and `FrameScopeNavButton` unchanged into `FrameScopeButtons.cs`.

- [ ] **Step 4: Move status and small visual controls**

Move `FrameScopeStatusLabel`, `FrameScopeCaptureChainVisual`, `FrameScopeToggleCheckBox`, `FrameScopeSidebarLogo`, and `FrameScopeGlowDot` unchanged into `FrameScopeStatusControls.cs`.

- [ ] **Step 5: Move reference sidebar controls**

Move `FrameScopeNavigationEventArgs` and `FrameScopeReferenceSidebar` unchanged into `FrameScopeReferenceSidebar.cs`.

- [ ] **Step 6: Move live chart controls**

Move `FrameScopeLiveSnapshot` and `FrameScopeMiniChartPanel` unchanged into `FrameScopeLiveChart.cs`.

### Task 2: Update Build Inputs

**Files:**
- Modify: `build.ps1`
- Check: `tests/Build-FrameScopeTests.ps1`

- [ ] **Step 1: Compile new UI files**

Replace the single `src/ui/FrameScopeUiComponents.cs` compile input with the new focused UI component files.

- [ ] **Step 2: Confirm test compile inputs**

Verify `tests/Build-FrameScopeTests.ps1` does not compile the moved visual controls directly. If it does, update its source list.

### Task 3: Verify Stage 23

**Files:**
- Check: `FrameScopeMonitor.exe`
- Check: `tests/*`
- Check: `artifacts/stage23-*.png`

- [ ] **Step 1: Build**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`

- [ ] **Step 2: Rebuild tests**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`

- [ ] **Step 3: Run existing tests**

Run the existing FrameScope test executables plus `node .\tests\chart-sampling-tests.js`.

- [ ] **Step 4: UI screenshots**

Generate overview, settings, and live screenshots. Treat the historical targets DataGridView screenshot harness failure as a harness issue only if the app build and other screenshots pass.

- [ ] **Step 5: Diff hygiene**

Run: `git diff --check`
