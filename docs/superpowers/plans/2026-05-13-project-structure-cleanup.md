# FrameScope Monitor Project Structure Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize FrameScope Monitor into clear source, UI, interaction, backend monitoring, reporting, packaging, tool and lightweight-script areas without changing runtime behavior.

**Architecture:** This is a low-risk directory/module cleanup. Existing C# types keep their current names and default namespace; build/test scripts are updated to compile the moved files by path. Lightweight GameLite scripts move to an isolated `scripts/lightweight/` module, with thin root wrappers kept for compatibility.

**Tech Stack:** Windows PowerShell, .NET Framework `csc.exe`, WinForms C#, existing command-line tests.

---

## Phase 1: Read-Only Analysis

Status: complete before file movement.

### Current Module Map

- UI visuals:
  - `FrameScopeUiComponents.cs`
  - UI helper sections in `FrameScopeNativeMonitor.cs`
  - `FrameScopeReportPage.cs`
  - design docs under `docs/FrameScopeMonitor-design-system.md`
- UI interactions:
  - page switch, buttons, target grid, settings, live refresh timers in `FrameScopeNativeMonitor.cs`
  - live/report UI partials in `FrameScopeLiveData.cs`, `FrameScopeReportPage.cs`, `FrameScopeUiState.cs`
- Backend monitoring:
  - watcher and monitor-session orchestration in `FrameScopeNativeMonitor.cs`
  - process matching/PUBG aliases in `FrameScopeCapturePlanner.cs`
  - config in `FrameScopeConfigStore.cs`
  - diagnostics/log retention in `FrameScopeDiagnostics.cs`
  - process sampler in `FrameScopeProcessSampler.cs`
  - system sampler in `FrameScopeSystemSampler.cs`
  - report generator in `FrameScopeReportGenerator.cs`
  - report progress in `FrameScopeReportProgress.cs`
- Lightweight scripts:
  - `Install-GameLiteAutoTrigger.ps1`
  - `Check-GameLiteAutoTrigger.ps1`
  - `Remove-GameLiteAutoTrigger.ps1`
  - `GameLiteSession.ps1`
  - `Enter-GameLite.ps1`
  - `Exit-GameLite.ps1`
  - `Invoke-GameLiteSGuardThrottle.ps1`
  - root `.cmd` launchers
  - `game-lite-auto-trigger-backup.json`
- Keep in place:
  - root `build.ps1`
  - root exe outputs
  - `packaging/`
  - `tools/`
  - `tests/`
  - config/history/log runtime files

### Target Structure

- `src/app/`
  - `FrameScopeNativeMonitor.cs`
- `src/ui/`
  - `FrameScopeUiComponents.cs`
  - `FrameScopeUiState.cs`
  - `FrameScopeLiveData.cs`
  - `FrameScopeReportPage.cs`
- `src/core/`
  - `FrameScopeConfigStore.cs`
  - `FrameScopeCapturePlanner.cs`
  - `FrameScopeReportProgress.cs`
- `src/monitoring/`
  - `FrameScopeProcessSampler.cs`
  - `FrameScopeSystemSampler.cs`
- `src/diagnostics/`
  - `FrameScopeDiagnostics.cs`
- `src/reporting/`
  - `FrameScopeReportGenerator.cs`
- `scripts/lightweight/`
  - core GameLite scripts and state/backup data
- root compatibility wrappers:
  - `Install-GameLiteAutoTrigger.ps1`
  - `Check-GameLiteAutoTrigger.ps1`
  - `Remove-GameLiteAutoTrigger.ps1`
  - `GameLiteSession.ps1`
  - `Enter-GameLite.ps1`
  - `Exit-GameLite.ps1`
  - `Invoke-GameLiteSGuardThrottle.ps1`
  - root `.cmd` launchers call wrappers

## Phase 2: Move Code Structure

- [ ] Create module directories under `src/` and `scripts/lightweight/`.
- [ ] Move C# source files to target `src/` directories.
- [ ] Move GameLite core scripts to `scripts/lightweight/`.
- [ ] Replace root GameLite `.ps1` files with thin wrappers that forward all args to moved scripts.
- [ ] Keep root `.cmd` files as compatibility launchers.
- [ ] Update `build.ps1` source paths.
- [ ] Update `tools/FrameScopePubgSimulator/Run-PubgSimulation.ps1` source paths.
- [ ] Add/update a test compile helper so tests can be rebuilt against `src/`.

## Phase 3: Documentation

- [ ] Create `docs/FrameScopeMonitor-Project-Overview.md`.
- [ ] Create `docs/modules/software-ui.md`.
- [ ] Create `docs/modules/ui-interactions.md`.
- [ ] Create `docs/modules/backend-monitoring.md`.
- [ ] Create `docs/modules/lightweight-script.md`.
- [ ] Update `AGENTS.md`.
- [ ] Update `README.md`.
- [ ] Append progress to `docs/FrameScopeMonitor-progress.md`.
- [ ] Update `docs/FrameScopeMonitor-next-prompt.md`.

## Phase 4: Verification

- [ ] Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`.
- [ ] Rebuild tests against moved source paths.
- [ ] Run all C# test exes and `node tests\chart-sampling-tests.js`.
- [ ] Run `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`.
- [ ] Run UI screenshot/smoke command if current harness supports it.
- [ ] Run lightweight script checks:
  - wrapper syntax parse for all root wrappers.
  - moved core script syntax parse.
  - `Check-GameLiteAutoTrigger.ps1` execution if WMI access is available.
- [ ] Run `rg` checks for stale root source references.
- [ ] Run `git diff --check`.
- [ ] Check no FrameScope/PresentMon/GameLite residual process.

## Risk Controls

- Do not change C# namespaces or public class names.
- Do not alter monitoring behavior, sampling intervals, report data, or UI event logic.
- Root wrappers preserve old GameLite entry paths for WMI triggers and user shortcuts.
- If a moved file breaks build, fix script paths first before touching business logic.
