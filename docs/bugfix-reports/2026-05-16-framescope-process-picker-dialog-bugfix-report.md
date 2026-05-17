# 2026-05-16 FrameScope process picker dialog bugfix report

## Current conclusion

Fixed, verified, repackaged, and locally updated. The local install directory was updated by copying the freshly built payload into `%LOCALAPPDATA%\FrameScopeMonitor`; this was not a full interactive installer run.

## Why the previous fix was insufficient

The previous fix treated the issue as a slow ComboBox refresh. It made enumeration lighter and asynchronous, but the user-facing interaction still depended on a normal ComboBox dropdown. That did not satisfy the requested Windows-style program picker behavior, and the verification evidence only proved static page rendering plus build/tests, not the real click path that was failing for the user.

## Real root cause

- `FrameScopeNativeMonitor.PageTargets.Actions.cs` still wired the process input through `ComboBox.DropDown` and click events.
- `OpenProcessPickerDropdown()` still forced `DroppedDown = true`, so the UI could re-enter ComboBox dropdown handling while the user clicked the same control.
- `RefreshProcessList()` filled a hidden/dropdown-backed list but did not present a clear selectable program picker.
- The test seam only covered display text and did not verify search, sort, manual input normalization, or selected-row behavior.

## New process picker behavior

- Clicking the process input field opens an independent modal dialog titled `添加 / 选择一个程序`.
- Clicking the arrow area opens the same dialog.
- Clicking `刷新进程` opens the same dialog.
- The dialog shows:
  - title/instruction: `选择一个程序`
  - sort dropdown: `最近使用`, `按名称`, `按进程名`
  - search box
  - selectable process list with icon, program/window title, process name, and PID as secondary detail
  - `浏览...`, `添加选定的程序`, and `取消`
- Process enumeration runs on a background thread with in-flight protection and a 5 second UI timeout.
- Protected processes and unavailable metadata are skipped or downgraded to default display/icon behavior.
- The default process enumeration path does not read `MainModule.FileName`.
- Selecting a process fills the process input, adds a row to the target table, saves config, scrolls to the new row, and selects it.
- Cancelling the dialog leaves the current input unchanged.
- Manual input remains supported through `添加进程`; `TslGame` still normalizes to `TslGame.exe`, and aliases separated by `;` are preserved.

## Modified files

- `src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`
- `src\app\FrameScopeNativeMonitor.UiProcessPicker.cs`
- `src\ui\FrameScopeUiState.cs`
- `tests\FrameScopeUiStateTests.cs`
- `build.ps1`

## New files

- `src\app\FrameScopeNativeMonitor.ProcessPickerDialog.cs`
- `docs\bugfix-reports\2026-05-16-framescope-process-picker-dialog-bugfix-report.md`

## Tests added or updated

Updated `tests\FrameScopeUiStateTests.cs` with coverage for:

- process picker display text: `Window title (process.exe)`
- search by window title and process name
- sort by display name
- sort by process name
- recent sort prioritizing visible/windowed processes
- manual process input normalization

## Real interaction verification evidence

Evidence directories:

- `artifacts\process-picker-dialog-20260516-interaction`
- `artifacts\process-picker-dialog-20260516-interaction-add-final`

Screenshots:

- `01-targets-initial-real.png`: real launched app on the `监控目标` page.
- `02-picker-open-refreshing.png`: process picker opened from the UI and showing refresh/list state.
- `03-picker-list-populated.png`: picker list populated with running processes.
- `04-cancel-returned-no-add.png`: cancel returned to the main window without adding.
- `02-picker-list-populated-from-input-click.png`: clicking the input field opened the dialog.
- `03-picker-notepad3-selected.png`: selected a real running Notepad3 process row.
- `04-targets-added-visible-row.png`: selected process added to the target table and visible as a selected row.

Recorded interaction result:

- Repeated refresh while the picker was open left only one picker dialog: `DialogCountDuringRepeatedRefresh = 1`.
- Adding the selected process wrote `Notepad3.exe` into config during the test.
- Test config was restored after the interaction run.

## UI screenshots

Generated screenshots:

- `artifacts\process-picker-dialog-20260516-ui-pages\ui-overview.png`
- `artifacts\process-picker-dialog-20260516-ui-pages\ui-settings.png`
- `artifacts\process-picker-dialog-20260516-ui-pages\ui-live.png`
- `artifacts\process-picker-dialog-20260516-ui-pages\ui-reports.png`
- `artifacts\process-picker-dialog-20260516-ui-pages\ui-targets.png`

The changed target-page area was checked for visible text overlap/overflow. Target action buttons remain wired to real handlers: input/arrow/refresh open the picker, `添加进程` still adds manual input, `保存配置` saves, watcher buttons still call the existing watcher handlers.

## Automatic verification

Commands run:

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

Results:

- `build.ps1`: PASS.
- `Build-FrameScopeTests.ps1`: PASS.
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
- `git diff --check`: PASS with LF/CRLF warnings only.

Simulator output files checked:

- `presentmon.csv`: exists.
- `process-samples.csv`: exists.
- `system-samples.csv`: exists.
- `summary.json`: exists.
- `status.json`: exists.

## Package outputs

Evidence file:

- `artifacts\process-picker-dialog-20260516-package\package-before-install.json`

Package artifacts:

- `dist\FrameScopeMonitor-Setup.exe`
  - size: `586240`
  - SHA256: `9D58A3CAB2670ED01A4BFF66ADE448EF23EA42FCD50A6863C93E8096F1E10984`
- `dist\FrameScopeMonitor-Installer.zip`
  - size: `587277`
  - SHA256: `B379E75BF6CAEF9108ECF4E1C8AC42AEC1D7062C66919CEBB06494E17B96CB69`

Payload freshness:

- `FrameScopeMonitor.exe`: root build output hash matched payload hash.
- `FrameScopeProcessSampler.exe`: root build output hash matched payload hash.
- `FrameScopeSystemSampler.exe`: root build output hash matched payload hash.
- `FrameScopeReportGenerator.exe`: root build output hash matched payload hash.
- `FrameScopeUninstaller.exe`: root build output hash matched payload hash.

## Local install directory update

Install directory:

- `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor`

Update method:

- Copied children of `dist\FrameScopeMonitor-payload` into the install directory.
- This was not a full installer run and did not rewrite installer registry metadata, uninstall metadata, or shortcuts.

Install evidence:

- `artifacts\process-picker-dialog-20260516-package\install-update.json`
- `artifacts\process-picker-dialog-20260516-installed-health\installed-targets.png`

Installed files matched payload by SHA256:

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `FrameScopeUninstaller.exe`

Installed health check:

- Ran installed `FrameScopeMonitor.exe --ui-screenshot ... --ui-page targets`.
- Screenshot was generated successfully.

## Residual process check

After tests, simulator, screenshots, packaging, and install update, no residual process was found for:

- `FrameScopeMonitor`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `PresentMon`
- `FakePresentMon`
- `TslGame`
- `GameLite`
- temporary `notepad` / `Notepad3` test process

## Scope notes

- Watcher/session/sampler/report generation logic was not modified.
- `src\core`, `src\monitoring`, `src\diagnostics`, `src\reporting`, `scripts\lightweight`, WMI trigger, GameLite, and SGuard behavior were not changed.
- SGuard was not involved in this fix.

## Uncovered items and real PUBG manual validation

Real PUBG was not launched on this machine. Simulator validation passed, but anti-cheat, real ETW capture, exclusive fullscreen, and true game lifecycle behavior still require manual validation:

1. Start installed `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe`.
2. Open `监控目标`.
3. Click the process input; confirm the new picker opens.
4. Click `刷新进程`; confirm the same picker opens and the app stays responsive.
5. Select `TslGame.exe` or `TslGame-Win64-Shipping.exe`, or manually type the process name and click `添加进程`.
6. Confirm the target row is added and enabled.
7. Start monitoring.
8. Launch PUBG and enter a rendered scene.
9. Exit PUBG.
10. Confirm the run directory contains `presentmon.csv`, `process-samples.csv`, `system-samples.csv`, `summary.json`, and `status.json`.
11. Confirm the HTML report is generated and charts/summary rows contain real PUBG frame data.
12. Confirm no residual `FrameScopeMonitor --monitor-session`, sampler, `PresentMon`, or report-generator process remains.
