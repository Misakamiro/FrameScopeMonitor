# FrameScope Reference Sidebar Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `src/ui/FrameScopeReferenceSidebar.cs` so navigation event args, control state/events, and sidebar drawing helpers are in separate UI design files.

**Architecture:** Convert `FrameScopeReferenceSidebar` to a partial class. Keep public control API and mouse events in the main file; move drawing methods unchanged to `.Drawing.cs`; move `FrameScopeNavigationEventArgs` to `.Navigation.cs`.

**Tech Stack:** C# WinForms custom control, existing screenshot harness.

---

### Task 1: Split Sidebar Control

**Files:**
- Create: `src/ui/FrameScopeReferenceSidebar.Navigation.cs`
- Create: `src/ui/FrameScopeReferenceSidebar.Drawing.cs`
- Modify: `src/ui/FrameScopeReferenceSidebar.cs`

- [ ] **Step 1: Move navigation event args**

Move `FrameScopeNavigationEventArgs` unchanged into `.Navigation.cs`.

- [ ] **Step 2: Make sidebar partial**

Change `FrameScopeReferenceSidebar` to `internal sealed partial class`.

- [ ] **Step 3: Move drawing methods**

Move compact and reference drawing methods, logo drawing, nav-item drawing, service card drawing, status dot, and text helpers into `.Drawing.cs`.

### Task 2: Update Build Inputs

**Files:**
- Modify: `build.ps1`

- [ ] **Step 1: Compile new sidebar files**

Add `.Navigation.cs` and `.Drawing.cs` after `FrameScopeReferenceSidebar.cs`.

### Task 3: Verify

Run `build.ps1`, then include overview/settings/live screenshots in final UI verification.
