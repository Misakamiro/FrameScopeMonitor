# FrameScope Monitor WebView2 React Live Actions Retest Report

Date: 2026-05-21 14:30 +08:00
Role: WebView2 React live actions retest tester
Scope: read-only source retest; generated evidence artifacts and this report only

## 1. Current Retest Conclusion

部分通过。

Core build, frontend verification, C# regression tests, WebView2 live smoke, reduced-motion smoke, and WinForms fallback screenshot all passed. The React UI is loading from `src\frontend\dist` under `--web-ui`, and the default no-argument / screenshot WinForms path remains the old WinForms UI.

The live bridge boundary is mostly healthy: `state.snapshot`, `config.get`, `config.save`, `processes.refresh`, `reports.list`, `targets.get`, `targets.save`, `monitor.start`, `monitor.stop`, and `diagnostics.generate` were observed through `window.chrome.webview` messages. The smoke also verified host-side rejection for forbidden path authority and missing report id cases, so I did not find mock success pretending to be live success.

Retest is not a full pass because two issues remain:

- FSM-WEB-LIVE-001: `reports.open`, `reports.openDirectory`, and successful `reports.regenerate` were not independently covered through the real WebView2 UI click path in the current smoke harness. Unit tests cover the bridge/adapter contract, and live smoke covers `reports.list` plus error rejection, but this is still a live-action coverage gap for candidate packaging.
- FSM-WEB-LIVE-002: Reports page card layout visibly wraps the `Size` value vertically in the narrow stats cell, making values hard to read.

## 2. Blocking Summary

| ID | Severity | Owner | Status |
| --- | --- | --- | --- |
| FSM-WEB-LIVE-001 | P1 verification blocker | UI interaction / backend bridge test harness | Real WebView2 UI success path for report open/open directory/regenerate still needs direct evidence. |
| FSM-WEB-LIVE-002 | P2 UI design | UI design | Reports `Size` value wraps vertically; not a bridge blocker, but visible polish regression. |

## 3. Read Documents And Source

- `docs\implementation-reports\2026-05-21-framescope-webview2-react-ui-live-actions-report.md`
- `docs\implementation-reports\2026-05-21-framescope-webview2-bridge-extension-report.md`
- `docs\test-reports\2026-05-21-framescope-webview2-react-merge-p1-retest-report.md`
- `src\frontend\src\state\useFrameScopeBridgeState.ts`
- `src\frontend\src\pages\OverviewPage.tsx`
- `src\frontend\src\pages\TargetsPage.tsx`
- `src\frontend\src\pages\ReportsPage.tsx`
- `src\frontend\src\pages\AboutPage.tsx`
- `src\frontend\src\data\mockPreview.ts`
- `src\frontend\src\bridge\contract.ts`
- `src\app\FrameScopeWebBridge*.cs`
- `src\app\FrameScopeNativeMonitor.WebHost.cs`

## 4. Command Verification

| Command | Result | Notes |
| --- | --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | Used bundled Node `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe` and project npm CLI cache. `npm ci`, typecheck, Vitest 2 files / 7 tests, and Vite build all passed. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | Built `dist\FrameScopeMonitor-Setup.exe`; no packaging handoff performed. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | Test executables rebuilt. |
| `.\tests\FrameScopeUiStateTests.exe` | PASS | `FrameScopeUiStateTests: PASS`. |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS | `FrameScopeReportProgressTests: PASS`. |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS | `FrameScopeWebBridgeTests: PASS`; includes report open/openDirectory/regenerate adapter contract tests. |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS | `chart-sampling-tests: PASS`. |
| `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo` | PASS | 0 warnings / 0 errors. |
| WebView2 live smoke | PASS | `webview2-live-smoke.json`: `success=true`, `bridgeExtensionSmoke.success=true`. |
| WebView2 reduced-motion smoke | PASS | `webview2-reduced-motion-smoke.json`: `success=true`, `reducedMotion=true`. |
| WinForms fallback screenshot | PASS | `winforms-fallback-overview.png` shows old WinForms overview. |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS | Exit 0; only existing LF/CRLF warnings. |

## 5. Real Bridge Behavior

Evidence:

- `artifacts\webview2-react-live-actions-retest-20260521\webview2-live-smoke.json`
- `artifacts\webview2-react-live-actions-retest-20260521\webview2-reduced-motion-smoke.json`

Observed live smoke values:

- `reportsListOk=true`
- `targetsGetOk=true`
- `reportOpenPathRejected=true`
- `targetsSavePathRejected=true`
- `reportRegenerateMissingRejected=true`
- `diagnosticsAccepted=true`
- `diagnosticsCompleted=true`
- `monitorStartAccepted=true`
- `monitorStarted=true`
- `monitorStopAccepted=true`
- `monitorStopped=true`
- `processRefreshObserved=true`
- `configDirtyObserved=true`
- `configSavingObserved=true`
- `configSaveSuccessObserved=true`

Message log confirms live WebView2 bridge traffic, including `js->host` requests and `host->js` responses/events. Examples observed:

- `reports.list` returned 16 validated report entries from real history/data root.
- `targets.get` returned 9 configured targets, 6 enabled.
- `reports.open` with a frontend path was rejected as `path_not_allowed`.
- `targets.save` with `path` was rejected as `path_not_allowed`.
- `reports.regenerate` with a missing report id was rejected as `report_not_found`.
- `diagnostics.generate` returned accepted, then `event.reportProgress` completed with real `markdownPath` and `jsonPath`.
- `monitor.start` returned accepted, then `event.status monitor.started` with a PID; `monitor.stop` returned accepted, then `event.status monitor.stopped`.

## 6. Button Matrix

| Area | Action | Result |
| --- | --- | --- |
| Overview | `monitor.start` | PASS. Live bridge accepted and emitted `monitor.started`; UI prevents duplicate start while busy/running. |
| Overview | `monitor.stop` | PASS. Live bridge accepted and emitted `monitor.stopped`; smoke cleaned up watcher process. |
| Overview | Open data directory | PASS disabled boundary. Button remains disabled / next-stage, no fake success. |
| Targets | `targets.get` | PASS. Real target config loaded into the form. |
| Targets | `targets.save` | PASS for success path through Settings smoke and bridge-level target save rejection; mock preview verified target dirty/saving/saved/failure UI states. |
| Targets | Add target | PASS disabled boundary. UI still marks direct add as not connected. |
| Reports | `reports.list` | PASS. Real report list loaded. |
| Reports | `reports.open` | PARTIAL. Unit test covers valid `reportId` adapter call and live smoke covers path rejection; real WebView2 UI open click was not executed as direct evidence. |
| Reports | `reports.openDirectory` | PARTIAL. Unit test covers valid `reportId` adapter call; real WebView2 UI open-directory click was not executed as direct evidence. |
| Reports | `reports.regenerate` | PARTIAL. Unit test covers valid adapter call and live smoke covers missing id rejection; real WebView2 UI successful regenerate was not directly observed. |
| Reports | `diagnostics.generate` | PASS. Live bridge accepted and completed with backend-returned paths. |
| About | Mock/live boundary text | PASS. About page states WebView2 live uses `window.chrome.webview`; browser preview is mock-labeled. |

## 7. Error Path Verification

Live bridge error paths:

- `reports.open` rejects arbitrary frontend path authority.
- `targets.save` rejects arbitrary frontend path/config authority.
- `reports.regenerate` rejects missing/invalid `reportId`.

Mock preview UI error paths:

- Evidence root: `artifacts\webview2-react-live-actions-retest-20260521`
- `mock-retest-monitor-start-failed.png`
- `mock-retest-targets-failed-kept-input.png`
- `mock-retest-diagnostics-failed.png`
- `mock-retest-failure-summary.json`

These mock screenshots are explicitly browser mock preview evidence only. They prove the React UI has visible failed-state handling and preserves edited target input on save failure, but they are not counted as real backend success.

## 8. Mock / Live Boundary

PASS.

WebView2 screenshots show `WebView2` and `Bridge ready`; pages label themselves as `WebView2 bridge live`, `real targets.get/save`, and `real reports bridge`. Browser-preview screenshots show `Mock preview` and mock adapter labels. I found no fallback from WebView2 live to browser mock success.

Source review also confirms:

- `contract.ts` includes the live action request types.
- `webviewBridge.ts` uses WebView2 messaging when `window.chrome.webview` is present.
- `mockPreview.ts` supports the same request names only in mock preview and has `?mockFailure=...` failure injection.
- C# bridge rejects frontend path authority for report/config/target-sensitive actions.

## 9. Motion And Reduced Motion

Evidence:

- `motion-contact-sheet.png`
- `reduced-motion-contact-sheet.png`
- `webview2-live-smoke-transition-*.png`
- `webview2-reduced-motion-smoke-transition-*.png`

Result: PASS with note.

I did not see old page body mixed with new page header/nav, whole-page spinner, or empty skeleton frames. First transition frames still show a very low-opacity target page in some transitions, including reduced-motion capture, but the page content is already the target page rather than old/new mixed content. This is acceptable for the current live-action retest, but UI interaction may still choose to make reduced-motion page commits fully opacity-stable.

## 10. Screenshots And Evidence Paths

Primary evidence directory:

- `artifacts\webview2-react-live-actions-retest-20260521`

Key files:

- WinForms fallback: `winforms-fallback-overview.png`
- WebView2 pages: `webview2-live-smoke-overview.png`, `webview2-live-smoke-targets-result.png`, `webview2-live-smoke-reports.png`, `webview2-live-smoke-settings-clean.png`, `webview2-live-smoke-about.png`
- Settings dirty/saving/saved: `webview2-live-smoke-settings-dirty.png`, `webview2-live-smoke-settings-saving.png`, `webview2-live-smoke-settings-saved.png`
- Process refresh: `webview2-live-smoke-targets-loading.png`, `webview2-live-smoke-targets-result.png`
- Motion: `motion-contact-sheet.png`
- Reduced motion: `reduced-motion-contact-sheet.png`
- Bridge JSON/event log: `webview2-live-smoke.json`, `webview2-reduced-motion-smoke.json`
- Mock preview failure UI: `mock-retest-*.png`, `mock-retest-summary.json`, `mock-retest-failure-summary.json`

## 11. Residual Process Check

Final process check found no test-started residual processes for:

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`
- project-local Vite preview / esbuild from this retest

Processes still present and not killed:

- Existing `msedgewebview2.exe` groups started on 2026-05-17, unrelated to this retest.
- `node.exe` PID 1912 under `C:\Users\misakamiro\AppData\Local\OpenAI\Codex\...`, which belongs to the Codex app runtime, not this project preview.

## 12. Issues

### FSM-WEB-LIVE-001

Severity: P1 verification blocker

Repro steps:

1. Run the current WebView2 live smoke.
2. Inspect `webview2-live-smoke.json` and `FrameScopeNativeMonitor.WebHost.cs`.
3. Compare with requested coverage for Reports open/openDirectory/regenerate.

Actual result:

The live smoke verifies `reports.list`, invalid path rejection for `reports.open`, and missing report id rejection for `reports.regenerate`. It does not click the real React Reports buttons for successful `reports.open`, `reports.openDirectory`, or successful `reports.regenerate` through WebView2.

Expected result:

Candidate packaging should have direct evidence that each enabled Reports button works through the real WebView2 UI and does not fake success.

Possible files:

- `src\app\FrameScopeNativeMonitor.WebHost.cs`
- `src\frontend\src\pages\ReportsPage.tsx`
- `src\frontend\src\state\useFrameScopeBridgeState.ts`
- `tests\FrameScopeWebBridgeTests.cs`

Recommended owner:

UI interaction + backend bridge test harness. Add or run a smoke path that selects a backend-returned report row and exercises open/openDirectory/regenerate success without relying only on unit-level adapter tests.

### FSM-WEB-LIVE-002

Severity: P2 UI design

Repro steps:

1. Open `webview2-live-smoke-reports.png`.
2. Inspect the first report card stat cells.

Actual result:

The `Size` value wraps vertically in the narrow cell, splitting digits and units into a column, which is hard to read.

Expected result:

Size should display on one readable line or use a responsive stat layout that prevents vertical character wrapping.

Possible files:

- `src\frontend\src\pages\ReportsPage.tsx`
- `src\frontend\src\pages\pages.css`

Recommended owner:

UI design.

## 13. Packaging Recommendation

Do not move directly to candidate packaging from this retest alone.

Recommended next step: fix or explicitly cover FSM-WEB-LIVE-001 first. After direct live evidence exists for Reports open/openDirectory/regenerate, packaging validation can proceed with the same frontend verify, C# tests, WebView2 live smoke, WinForms fallback screenshot, and residual process check.
