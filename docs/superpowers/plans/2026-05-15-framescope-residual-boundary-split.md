# FrameScope Residual Boundary Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clean up the remaining report-page, live-page, and report-open responsibility overlaps without changing FrameScope Monitor behavior.

**Architecture:** Keep the existing C# partial-class split. Move only whole helper methods or event-binding groups into focused files so UI layout, UI interaction, and backend/report status responsibilities are easier to edit independently.

**Tech Stack:** C# WinForms on .NET Framework compiler via `build.ps1`, PowerShell verification scripts, existing UI screenshot harness, Node chart sampling tests.

---

## File Structure

- `src\ui\FrameScopeReportPage.Layout.cs`: report page control creation, arrangement, visual structure, and calls into binding helpers.
- `src\ui\FrameScopeReportPage.Actions.cs`: report page button binding and report actions.
- `src\app\FrameScopeNativeMonitor.PageLive.cs`: live-page ownership note only.
- `src\app\FrameScopeNativeMonitor.PageLive.Layout.cs`: live page visual structure, chart cards, and metric cards.
- `src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs`: live page enter/leave refresh timer and page refresh lifecycle.
- `src\app\FrameScopeNativeMonitor.PageLive.Log.cs`: live log pause, clear, and display behavior.
- `src\ui\FrameScopeLiveData.Csv.cs`: CSV helper methods used by live data loading.
- `src\app\FrameScopeNativeMonitor.ReportOpen.cs`: report open entry points.
- `src\app\FrameScopeNativeMonitor.ReportOpen.Browser.cs`: HTML browser fallback discovery and launch helpers.
- `src\app\FrameScopeNativeMonitor.ReportOpen.Status.cs`: report-open marker/status update helper.
- `src\app\FrameScopeNativeMonitor.ReportStatus.cs`: status/progress/history decisions only; no forced split unless mixed responsibilities are found.
- `src\reporting\FrameScopeReportGenerator.Html.cs`: inspect and document only; no template rewrite in this stage.
- `build.ps1`: main executable source list for newly added partial files.

## Tasks

- [ ] Verify the current residual files and grep for event bindings, timers, report open calls, styling, and process launch points.
- [ ] Move report page button bindings from layout into report actions while preserving the existing button targets.
- [ ] Split live page layout, lifecycle, log, and CSV helpers into focused partial files.
- [ ] Split report-open browser fallback and status update helpers only if the existing file still mixes those responsibilities.
- [ ] Add any new C# source files to `build.ps1`; update test build script only if a test target compiles those sources.
- [ ] Update module docs and next prompt with the new ownership rules.
- [ ] Run the required build, tests, chart sampling, RenderProbe build, UI screenshots, report-button wiring checks, and residual process check.

## Non-Goals

- Do not change UI text, button behavior, report data shape, watcher/session logic, GameLite scripts, WMI triggers, or the report HTML template internals.
- Do not split `FrameScopeReportGenerator.Html.cs` in this stage; record it as a future exclusive report-template task.
