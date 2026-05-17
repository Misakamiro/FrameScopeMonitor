# FrameScope Visual Helpers Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce UI design merge conflicts by splitting the remaining large shared visual helper files into focused partial files.

**Architecture:** Keep existing drawing behavior and control factories intact. Move methods by responsibility: cards/sections/list styling/buttons for app visual helpers, and compact/reference/logo/text drawing for the sidebar.

**Tech Stack:** C# WinForms/GDI+ partial class files compiled by `build.ps1`, screenshot harness for visual smoke checks.

---

### File Structure

- Modify: `src/app/FrameScopeNativeMonitor.UiVisualHelpers.cs`
  - Keep shared basics: `GlassCard`, `UiPurple`, `AppVersionText`, `IconBlock`, `MakeRounded`.
- Create: `src/app/FrameScopeNativeMonitor.UiVisualCards.cs`
  - Own status/metric/info/capture cards and metric blocks.
- Create: `src/app/FrameScopeNativeMonitor.UiVisualSections.cs`
  - Own `SectionPanel`, `FormLabel`, and `StyleDarkListView`.
- Create: `src/app/FrameScopeNativeMonitor.UiVisualButtons.cs`
  - Own dashboard/settings/button factories and button palette helpers.
- Modify: `src/ui/FrameScopeReferenceSidebar.Drawing.cs`
  - Keep `OnPaint` and `DrawReferenceSidebar`.
- Create: `src/ui/FrameScopeReferenceSidebar.CompactDrawing.cs`
  - Own compact sidebar, compact logo/nav/service drawing.
- Create: `src/ui/FrameScopeReferenceSidebar.ReferenceDrawing.cs`
  - Own main reference sidebar card, scrollbar, nav items, divider, service card.
- Create: `src/ui/FrameScopeReferenceSidebar.LogoDrawing.cs`
  - Own logo, FrameScope logo, status dot, and text drawing helpers.
- Modify: `build.ps1`
  - Add the new partial files to the main compile list.

### Task 1: Split App Visual Helpers

- [ ] Move card factories to `UiVisualCards.cs`.
- [ ] Move section/list helpers to `UiVisualSections.cs`.
- [ ] Move button factories/palette helpers to `UiVisualButtons.cs`.
- [ ] Update `build.ps1`.
- [ ] Run `build.ps1`.

### Task 2: Split Reference Sidebar Drawing

- [ ] Move compact drawing methods to `FrameScopeReferenceSidebar.CompactDrawing.cs`.
- [ ] Move full reference drawing methods to `FrameScopeReferenceSidebar.ReferenceDrawing.cs`.
- [ ] Move logo/status/text helpers to `FrameScopeReferenceSidebar.LogoDrawing.cs`.
- [ ] Update `build.ps1`.
- [ ] Run `build.ps1`.

### Task 3: Verification

- [ ] Run full verification commands listed in the main task.
- [ ] Generate overview/settings/live screenshots and record target screenshot harness status.

