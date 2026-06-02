# FrameScope Monitor WebView2 Tray And Window Lifecycle Implementation Report

Date: 2026-05-25

Source root:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Scope

This pass implemented only the requested C# host/window lifecycle and necessary bridge state:

- Added a single `NotifyIcon` and right-click tray menu to `FrameScopeWebHostForm`.
- Added tray menu actions:
  - Show window, rendered by the host as `\u663e\u793a\u7a97\u53e3`.
  - Exit, rendered by the host as `\u9000\u51fa`.
- Window X now follows persisted config:
  - `CloseWindowBehavior=minimize-to-tray` and `TrayEnabled=true` hides the WebView2 host window and leaves watcher/monitor processes untouched.
  - `CloseWindowBehavior=exit` uses the explicit exit path.
- Tray Show restores the window, returns it to the taskbar, and activates it.
- Tray Exit and window-X exit require confirmation when active FrameScope monitoring is detected.
- Smoke and harness close paths use an explicit close guard so automated close is not trapped by minimize-to-tray.
- `state.snapshot` now includes host state:
  - `windowVisible`
  - `trayAvailable`
  - `closeWindowBehavior`
- NotifyIcon is created once per host form and disposed with the form.

## Explicit Non-Goals

Not done in this pass:

- No UI visual redesign or styling changes.
- No CPU telemetry collection work.
- No performance optimization.
- No default WebView2 entry change.
- No restoration of the old WinForms UI.
- No install/update of the local installed app.
- No GitHub push or release publishing.

`build.ps1` was run as requested verification. Its existing side effect regenerated setup executables under `dist`; this was not treated as release or publishing work.

## Implementation Details

### Host Tray Lifecycle

Updated `src\app\FrameScopeNativeMonitor.WebHost.cs`.

Main behavior:

- `FrameScopeWebHostForm` creates one `NotifyIcon` and one `ContextMenuStrip` during form construction.
- Tray icon text is `FrameScope Monitor`.
- The tray icon remains available while the app is running when `TrayEnabled=true`.
- Window X with minimize-to-tray cancels `FormClosing`, hides the form, clears taskbar visibility, and publishes a host-window event.
- Tray Show calls `Show()`, restores `WindowState=Normal`, sets `ShowInTaskbar=true`, and calls `Activate()`.
- Tray Exit calls the same explicit exit path as window-X exit.
- `FinishAsync` sets the explicit close guard before `Close()` so smoke automation does not get hidden to tray.
- `Dispose(bool)` hides and disposes the tray icon and disposes the tray menu.

### Active Monitoring Exit Protection

Updated `src\app\FrameScopeNativeMonitor.ProcessCleanup.cs`.

Added active monitoring detection:

- Reads watcher state for `Phase=monitoring`.
- Checks `ActiveMonitors` in watcher state.
- Scans FrameScope-owned active capture processes:
  - `FrameScopeMonitor.exe --monitor-session`
  - `FrameScopeProcessSampler.exe`
  - `FrameScopeSystemSampler.exe`
  - bundled FrameScope `PresentMon`

If active monitoring is detected, explicit exit requires confirmation before stopping FrameScope background processes.

### Bridge Host State

Updated:

- `src\app\FrameScopeWebBridge.Contracts.cs`
- `src\app\FrameScopeWebBridge.cs`
- `src\app\FrameScopeWebBridge.State.cs`
- `src\frontend\src\bridge\contract.ts`
- `src\frontend\src\data\mockPreview.ts`

`FrameScopeWebBridgeOptions` now accepts a `HostStateProvider`. `state.snapshot` includes:

```json
{
  "host": {
    "windowVisible": true,
    "trayAvailable": true,
    "closeWindowBehavior": "minimize-to-tray"
  }
}
```

The WebView2 smoke harness now explicitly checks that `state.snapshot.host` exists and contains the expected fields.

### Tests And Harness

Added:

- `src\app\FrameScopeWebHostLifecycle.cs`
- `tests\FrameScopeWebHostLifecycleTests.cs`

Updated:

- `tests\FrameScopeWebBridgeTests.cs`
- `tests\Build-FrameScopeTests.ps1`
- `build.ps1`

Coverage added:

- Snapshot includes host window state.
- User close hides only when tray minimize is configured and tray is enabled.
- Explicit close and disposing are not trapped by minimize-to-tray.
- Active monitoring always requires confirmation.
- WebView2 tray smoke verifies:
  - X/hide-to-tray behavior through host lifecycle path
  - tray Show restore path
  - repeated hide/show does not create duplicate tray icon instances
  - active monitoring exit is blocked when confirmation is not granted
  - automation close/dispose guards do not get trapped in tray

## Verification

All commands below were run from the source root on 2026-05-25.

### Build

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Result: PASS

Observed output:

- `Build complete: ...\dist\FrameScopeMonitor-Setup.exe`
- `Full setup complete: ...\dist\FrameScopeMonitor-Full-Setup.exe`

### Test Build

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
```

Result: PASS

Observed output:

- `FrameScope tests rebuilt.`

### Web Bridge Tests

Command:

```powershell
.\tests\FrameScopeWebBridgeTests.exe
```

Result: PASS

Observed output:

- `FrameScopeWebBridgeTests: PASS`

### Web Host Lifecycle Tests

Command:

```powershell
.\tests\FrameScopeWebHostLifecycleTests.exe
```

Result: PASS

Observed output:

- `FrameScopeWebHostLifecycleTests: PASS`

### WebView2 Runtime Tests

Command:

```powershell
.\tests\FrameScopeWebView2RuntimeTests.exe
```

Result: PASS

Observed output:

- `FrameScopeWebView2RuntimeTests: PASS`

### Frontend Verify

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result: PASS

Observed output:

- TypeScript typecheck passed.
- Vitest: `5 passed`, `48 passed`.
- Vite production build passed.

Reason for running: `StateSnapshotPayload` contract changed to include `host`, so TypeScript and mock preview had to be verified.

### WebView2 Live Smoke

Command:

```powershell
.\dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-tray-window-lifecycle-20260525\live-smoke-final\smoke.json --web-ui-screenshot artifacts\webview2-tray-window-lifecycle-20260525\live-smoke-final\smoke.png --web-ui-timeout-ms 120000
```

Result: PASS

Evidence:

`artifacts\webview2-tray-window-lifecycle-20260525\live-smoke-final\smoke.json`

Key fields:

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `smokePayload.success=true`
- `smokePayload.bridgeExtensionSmoke.success=true`
- `smokePayload.bridgeExtensionSmoke.stateSnapshotHostOk=true`
- `smokePayload.themeSmoke.success=true`

### Tray Window Lifecycle Harness

Command:

```powershell
.\dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe --web-ui-tray-smoke --web-ui-evidence artifacts\webview2-tray-window-lifecycle-20260525\tray-smoke-waited\smoke.json --web-ui-screenshot artifacts\webview2-tray-window-lifecycle-20260525\tray-smoke-waited\smoke.png --web-ui-timeout-ms 60000
```

Result: PASS

Evidence:

`artifacts\webview2-tray-window-lifecycle-20260525\tray-smoke-waited\smoke.json`

Key fields:

- `success=true`
- `initialVisible=true`
- `initialTrayVisible=true`
- `firstHide=true`
- `firstHidden=true`
- `shown=true`
- `secondHide=true`
- `trayInstanceAfterFirstHide=1`
- `trayInstanceAfterSecondHide=1`
- `duplicateTrayIconsPrevented=true`
- `blockedExit=true`
- `stillVisibleAfterBlockedExit=true`
- `exitAllowedWithoutActiveMonitoring=true`
- `automationCloseGuard=true`
- `disposeGuard=true`

This covers X/hide, tray show, tray exit path without active monitoring, repeated hide/show without duplicate icon creation, active monitoring blocked exit, and automation/dispose close guards.

### Git Diff Check

Command:

```powershell
git diff --check
```

Result: PASS

Observed output:

- Exit code `0`.
- Git printed LF/CRLF working-copy warnings only.
- No whitespace error was reported.

### Residual Process Check

Command:

```powershell
$repo = (Resolve-Path '.').Path; $selfPid = $PID; $patterns = @('FrameScopeMonitor.exe','FrameScopeProcessSampler.exe','FrameScopeSystemSampler.exe','FrameScopeReportGenerator.exe','PresentMon-2.4.1-x64.exe','FakeGame.exe','FakePresentMon.exe'); $matches = Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $selfPid } | Where-Object { $name = $_.Name; $cmd = [string]$_.CommandLine; $exe = [string]$_.ExecutablePath; ($patterns -contains $name) -or ($cmd -like "*$repo*" -and $cmd -match 'FrameScopeMonitor|FrameScopeProcessSampler|FrameScopeSystemSampler|PresentMon|web-ui-smoke|web-ui-tray-smoke') -or ($exe -like "*$repo*") } | Select-Object ProcessId,Name,ExecutablePath,CommandLine; if ($matches) { $matches | Format-List; exit 1 } else { 'NO_MATCHING_RESIDUAL_PROCESSES' }
```

Result: PASS

Observed output:

- `NO_MATCHING_RESIDUAL_PROCESSES`

## Files Changed For This Scope

Primary implementation:

- `src\app\FrameScopeNativeMonitor.WebHost.cs`
- `src\app\FrameScopeNativeMonitor.ProcessCleanup.cs`
- `src\app\FrameScopeWebBridge.Contracts.cs`
- `src\app\FrameScopeWebBridge.cs`
- `src\app\FrameScopeWebBridge.State.cs`
- `src\app\FrameScopeWebHostLifecycle.cs`

Contract/mock support:

- `src\frontend\src\bridge\contract.ts`
- `src\frontend\src\data\mockPreview.ts`

Tests/build:

- `tests\FrameScopeWebBridgeTests.cs`
- `tests\FrameScopeWebHostLifecycleTests.cs`
- `tests\Build-FrameScopeTests.ps1`
- `build.ps1`

Report:

- `docs\implementation-reports\2026-05-25-framescope-tray-window-lifecycle-implementation-report.md`

## Current Status

PASS for the requested implementation scope.

The WebView2 host now supports close-to-tray, tray Show, tray Exit, active-monitoring exit confirmation, explicit automation/dispose close guards, and host state in `state.snapshot`. Required build, tests, WebView2 live smoke, tray lifecycle harness, `git diff --check`, and residual process checks passed.
