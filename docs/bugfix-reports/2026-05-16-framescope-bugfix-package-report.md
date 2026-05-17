# 2026-05-16 FrameScope bugfix and package report

## Current conclusion

Fixed and repackaged. The local install directory was updated by copying the freshly built payload into `%LOCALAPPDATA%\FrameScopeMonitor`; this was not a full interactive installer run.

## Tester report sources

- `docs\test-reports\*.md`: not present in this workspace.
- Direct user report in this conversation: on the "监控目标" page, clicking the process input or "刷新进程" did not show selectable processes and the whole app froze. Desired behavior is a selectable running-process list like the supplied reference images.
- Implementation and orchestration context read:
  - `docs\implementation-reports\2026-05-16-framescope-ui-interaction-implementation-report.md`
  - `docs\implementation-reports\2026-05-16-framescope-ui-design-implementation-report.md`
  - `docs\implementation-reports\2026-05-16-framescope-backend-implementation-report.md`
  - `docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Worklog.md`
  - `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Worklog.md`
  - `docs\orchestration\FrameScopeMonitor-BackendPrompt-Worklog.md`

## Fixed issues

### FSM-BUG-2026-05-16-01: process picker freezes the UI

Severity: high. This blocks adding monitored processes from the UI.

Root cause:

- `RefreshProcessList()` ran synchronously on the WinForms UI thread.
- It called `FrameScopeProcessPicker.EnumerateRunningProcesses()` on every ComboBox click, DropDown event, and refresh button click.
- The enumeration read `process.MainModule.FileName` for every running process. On Windows this can be slow or block on protected/system processes, so the UI thread stopped pumping messages and looked frozen.
- The process dropdown also refreshed repeatedly on click/dropdown instead of using a cached list while refresh was in flight.

Fix:

- Moved process enumeration to a `ThreadPool` background task.
- Added `processRefreshInFlight` guard so repeated clicks do not stack multiple full enumerations.
- Cached the latest process picker list and opens the dropdown from the UI thread after the first refresh completes.
- Stopped reading `MainModule.FileName` by default; `EnumerateRunningProcesses()` now uses the lightweight path and only reads the module path if explicitly requested with `includeProcessPath=true`.
- Changed display text from PID-first debug text to user-facing picker text: `Window title (process.exe)`, with a process-name fallback.
- Added a regression test covering the selectable app-list display text.
- Kept target buttons connected to the real handlers: refresh process, add process, save config, start watcher, stop watcher.

## Modified files

- `src\app\FrameScopeNativeMonitor.UiProcessPicker.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`
- `src\app\FrameScopeNativeMonitor.UiFields.cs`
- `src\ui\FrameScopeUiState.cs`
- `tests\FrameScopeUiStateTests.cs`
- `docs\bugfix-reports\2026-05-16-framescope-bugfix-package-report.md`

No watcher/session/sampler/report-template/GameLite/WMI files were modified. SGuard behavior was not changed; existing GameLite-side SGuard throttling defaults remain as previously implemented, with `-DisableSGuardThrottle` as the explicit off switch.

## Impact scope

- UI interaction only: process picker refresh, dropdown open behavior, process display text, and selected process resolution.
- Backend capture, report generation, watcher lifecycle, GameLite, WMI triggers, and SGuard were not changed.
- The installed payload was refreshed from the rebuilt `dist\FrameScopeMonitor-payload`.

## Retest commands

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeConfigStoreTests.exe
.\tests\FrameScopeCapturePlannerTests.exe
.\tests\FrameScopeReportProgressTests.exe
.\tests\FrameScopeDiagnosticsTests.exe
.\tests\FrameScopePubgSimulatorTests.exe
.\tests\FrameScopeUiStateTests.exe
node .\tests\chart-sampling-tests.js
dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario stable -DurationSeconds 4
"C:\Program Files\Git\cmd\git.exe" diff --check
```

Node note: `chart-sampling-tests.js` was run with `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin` prepended to `PATH`.

## Verification result

Verification artifact directory:

- `artifacts\bugfix-process-picker-20260516-230827`

Command results:

- `build.ps1`: PASS.
- `tests\Build-FrameScopeTests.ps1`: PASS.
- `FrameScopeConfigStoreTests.exe`: PASS.
- `FrameScopeCapturePlannerTests.exe`: PASS.
- `FrameScopeReportProgressTests.exe`: PASS.
- `FrameScopeDiagnosticsTests.exe`: PASS.
- `FrameScopePubgSimulatorTests.exe`: PASS.
- `FrameScopeUiStateTests.exe`: PASS.
- `chart-sampling-tests.js`: PASS.
- `FrameScopeRenderProbe` Release build: PASS, 0 warnings, 0 errors.
- Stable PUBG simulator: PASS.
  - `monitorExit=0`
  - `reportExit=0`
  - `presentMonCsvRows=240`
  - `hasFrameData=true`
  - `frames=240`
  - `reportKind=full`
- `git diff --check`: PASS.

UI screenshots:

- `artifacts\bugfix-process-picker-ui-20260516-230827\ui-overview.png`
- `artifacts\bugfix-process-picker-ui-20260516-230827\ui-settings.png`
- `artifacts\bugfix-process-picker-ui-20260516-230827\ui-targets.png`
- `artifacts\bugfix-process-picker-ui-20260516-230827\ui-live.png`
- `artifacts\bugfix-process-picker-ui-20260516-230827\ui-reports.png`

Targets screenshot was reviewed for the changed area: no obvious text overlap or overflow in the process input/action row, and target action buttons remain wired to real handlers.

## Package status

- Repackaged: yes.
- `dist\FrameScopeMonitor-Setup.exe`: exists, 583168 bytes, timestamp `2026-05-16 23:08:29`, SHA256 `2E4227DB81BFFFA4C0D0E15923CD3DCAA8314EB86BAEFACBCE726B86CCEDA620`.
- `dist\FrameScopeMonitor-Installer.zip`: exists, 583916 bytes, timestamp `2026-05-16 23:08:29`, SHA256 `77A8F25BA1FA9809EE9FD62C5EFBAFC4A99C6B1E94CAF5EBF0BEEFDD8196C8A3`.
- Payload freshness: PASS. Root build outputs matched `dist\FrameScopeMonitor-payload` by SHA256 for:
  - `FrameScopeMonitor.exe`
  - `FrameScopeProcessSampler.exe`
  - `FrameScopeSystemSampler.exe`
  - `FrameScopeReportGenerator.exe`
  - `FrameScopeUninstaller.exe`

Package evidence:

- `artifacts\bugfix-process-picker-package-20260516-final.json`

## Local install directory update

- Install directory: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor`
- Updated locally: yes.
- Update method: copied children of `dist\FrameScopeMonitor-payload` into the install directory.
- This was not a full installer run and did not rewrite installer registry metadata, uninstall metadata, or shortcuts.
- Install hash verification: PASS. Installed files matched final payload by SHA256 for:
  - `FrameScopeMonitor.exe`
  - `FrameScopeProcessSampler.exe`
  - `FrameScopeSystemSampler.exe`
  - `FrameScopeReportGenerator.exe`
  - `FrameScopeUninstaller.exe`

Installation note:

- A first copy attempt used a literal wildcard path and did not update the installed binaries; the mismatch was detected by SHA256 and corrected by enumerating payload children explicitly before copying.

Installed health check:

- Installed `FrameScopeMonitor.exe --ui-screenshot ... --ui-page overview`: PASS.
- Screenshot: `artifacts\bugfix-process-picker-installed-health-20260516-final\installed-overview.png`

## Residual process check

After simulator, screenshot, package, and install-update checks, no residual process was found for:

- `FrameScopeMonitor`
- `FrameScopeReportGenerator`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `PresentMon`
- `FakePresentMon`
- `TslGame`
- `GameLite`

## Unfixed or user-confirmation items

- Real PUBG was not launched on this machine. Simulator validation passed, but anti-cheat, ETW, exclusive fullscreen, and real game lifecycle behavior still need manual PUBG validation.
- Because local update used payload copy rather than the interactive installer, installer UI, shortcut rewrite, registry metadata, and uninstall metadata were not re-tested.

## Real PUBG manual validation

1. Open the installed `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe`.
2. Open `监控目标`.
3. Click the process input and click `刷新进程`; confirm the app stays responsive and a selectable running-process list appears.
4. Select or type `TslGame.exe` / `TslGame-Win64-Shipping.exe` and confirm `添加进程` writes the target row.
5. Confirm the PUBG/TslGame target is enabled.
6. Start monitoring.
7. Launch PUBG and enter a real rendered scene.
8. Exit PUBG.
9. Confirm the run directory contains `presentmon.csv`, `process-samples.csv`, `system-samples.csv`, `summary.json`, and `status.json`.
10. Confirm `reportExit=0`, `hasFrameData=true`, and `reportKind=full`.
11. Open the HTML report and check FPS, 1% Low, 0.1% Low, process rows, and system charts.
12. Confirm no residual `FrameScopeMonitor --monitor-session`, sampler, `PresentMon`, or report-generator process remains.
