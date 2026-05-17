# FrameScope Monitor Session Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `src/app/FrameScopeNativeMonitor.MonitorSession.cs` into focused partial-class files so backend monitoring conversations can work on target discovery, PresentMon lifecycle, child process IO, and status writing independently.

**Architecture:** Keep `RunNativeMonitorSession` in the existing file as the session orchestration entry point. Move helper methods mechanically into new partial files and keep `MonitorSessionPaths` plus `TargetProcessSnapshot` data containers in a dedicated model file.

**Tech Stack:** C# WinForms app partial class compiled by `build.ps1`, existing FrameScope monitor simulator, report generator, and unit tests.

---

### Task 1: Split Monitor Session Helpers

**Files:**
- Create: `src/app/FrameScopeNativeMonitor.MonitorSession.Models.cs`
- Create: `src/app/FrameScopeNativeMonitor.MonitorSession.Paths.cs`
- Create: `src/app/FrameScopeNativeMonitor.MonitorSession.Targets.cs`
- Create: `src/app/FrameScopeNativeMonitor.MonitorSession.Tools.cs`
- Create: `src/app/FrameScopeNativeMonitor.MonitorSession.PresentMon.cs`
- Create: `src/app/FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs`
- Create: `src/app/FrameScopeNativeMonitor.MonitorSession.Status.cs`
- Modify: `src/app/FrameScopeNativeMonitor.MonitorSession.cs`

- [ ] **Step 1: Move model classes**

Move `MonitorSessionPaths` and `TargetProcessSnapshot` unchanged into `.Models.cs`.

- [ ] **Step 2: Move path and argument helpers**

Move `CreateMonitorSessionPaths`, `ParseIntArgument`, `JoinArguments`, `QuoteCommandArgument`, `CountCsvDataRows`, `TailText`, `AddDictionary`, and `WriteEventCsvHeader` unchanged into `.Paths.cs`.

- [ ] **Step 3: Move target process helpers**

Move `BuildTargetProcessBaseNames`, `ShouldUseProcessNameCapture`, `FindBestTargetProcess`, `SnapshotProcess`, and `WaitForTargetProcess` unchanged into `.Targets.cs`.

- [ ] **Step 4: Move tool discovery helpers**

Move `ResolvePresentMonPath`, `ResolveProcessSamplerPath`, `ResolveSystemSamplerPath`, `ResolveNvidiaSmiPath`, and `FirstExistingPath` unchanged into `.Tools.cs`.

- [ ] **Step 5: Move PresentMon lifecycle helpers**

Move `RequestPresentMonStop`, `CleanupFrameScopePresentMonSessions`, `QueryFrameScopePresentMonSessions`, `StopEtwSessionWithLogman`, `WritePresentMonInfo`, and `BuildPresentMonCaptureDiagnostics` unchanged into `.PresentMon.cs`.

- [ ] **Step 6: Move child-process helpers**

Move `StartNativeMonitorChild`, `BeginCopyPipe`, `StopMonitorChild`, and `ProcessExited` unchanged into `.ChildProcesses.cs`.

- [ ] **Step 7: Move status writers**

Move `WriteNativeMonitorStatus` and `WriteNativeMonitorSummary` unchanged into `.Status.cs`.

### Task 2: Update Build Inputs

**Files:**
- Modify: `build.ps1`

- [ ] **Step 1: Compile new partial files**

Add all new monitor-session partial files immediately after `FrameScopeNativeMonitor.MonitorSession.cs` in the main executable source list.

### Task 3: Verify Stage 24

**Files:**
- Check: `FrameScopeMonitor.exe`
- Check: tests and simulator

- [ ] **Step 1: Build**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`

- [ ] **Step 2: Run minimum backend verification**

Run the test rebuild, test executables, chart sampling test, RenderProbe build, and stable simulator after the split.
