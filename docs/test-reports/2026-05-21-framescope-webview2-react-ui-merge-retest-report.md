# FrameScope Monitor WebView2 React UI merge retest report

Date: 2026-05-21
Tester role: WebView2 React UI merge retest
Scope: UI design pass + UI interaction pass + bridge smoke regression
Source edits: none

## 1. Current conclusion

通过。

Frontend verification, main build, C# bridge/report tests, chart sampling, WebView2 live smoke, reduced-motion smoke, layout screenshots, motion frames, and residual-process checks all passed.

The earlier PowerShell display of Chinese text looked like mojibake, so I ran a strict UTF-8 byte-level check against the implementation report and representative React source files. The files are valid UTF-8 without replacement characters or mojibake byte patterns; rendered screenshots also show readable Chinese. This is therefore not a release blocker.

## 2. Documents and files read

- `docs\implementation-reports\2026-05-21-framescope-webview2-react-ui-design-pass-report.md`
- `artifacts\webview2-react-ui-interaction-review-20260521-afterfix\*`
- `src\frontend\src\**`
- `build.ps1`
- `tests\Build-FrameScopeTests.ps1`

## 3. Command validation

| Command | Result | Notes |
| --- | --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | Used bundled Node and project npm CLI. `npm ci`, `tsc --noEmit`, Vitest `4` files / `12` tests, and Vite build passed. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | Built `dist\FrameScopeMonitor-Setup.exe`. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | Test executables rebuilt. |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS | `FrameScopeReportProgressTests: PASS`. |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS | `FrameScopeWebBridgeTests: PASS`. |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS | `chart-sampling-tests: PASS`. |
| WebView2 live smoke | PASS | `webview2-live-smoke.json`: `success=true`, page ready, Reports live actions passed. |
| WebView2 reduced-motion smoke | PASS | `webview2-reduced-motion-smoke.json`: `success=true`, `reducedMotion=true`. |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS | Exit code `0`; only LF-to-CRLF warnings were printed. |

## 4. User original feedback acceptance

| Feedback item | Result | Evidence |
| --- | --- | --- |
| No fake macOS window embedded in Windows window | PASS | Normal WebView2 screenshots show full client-area app shell, no red/yellow/green controls or rounded outer mini-window shell. |
| No non-functional red/yellow/green buttons | PASS | No red/yellow/green fake controls in screenshots or shell source. |
| No meaningless Search / Notification fake entries in main visual area | PASS | Topbar contains page title and status only. No search/notification controls occupy primary UI. |
| Chinese copy readable and user-facing | PASS | Rendered screenshots show readable Chinese; strict UTF-8 byte-level checks on source/report files passed. |
| User can understand page core function and next action | PASS | Overview prioritizes monitoring state/start/stop/refresh/recent report; Targets/Reports/Settings show direct actions and status. |

## 5. UI visual acceptance

Normal-width WebView2 screenshots:

- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-overview.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-targets-result.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-reports.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-settings-clean.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-about.png`

Current-build 900x760 CDP screenshots:

- `artifacts\webview2-react-ui-merge-retest-20260521\cdp-900x760-targets.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\cdp-900x760-reports.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\cdp-900x760-settings.png`

Afterfix interaction-window layout evidence also read:

- `artifacts\webview2-react-ui-interaction-review-20260521-afterfix\browser-targets-900x760.png`
- `artifacts\webview2-react-ui-interaction-review-20260521-afterfix\browser-reports-900x760.png`
- `artifacts\webview2-react-ui-interaction-review-20260521-afterfix\browser-settings-900x760.png`
- `artifacts\webview2-react-ui-interaction-review-20260521-afterfix\browser-interaction-audit.json`

Visual result:

- Normal width: overview / targets / reports / settings / about are readable.
- 900x760: targets / reports / settings do not show horizontal overflow, vertical character splitting, or button overlap in inspected screenshots.
- Technical details are concentrated in About; they do not dominate Overview / Targets / Reports / Settings first screen.
- Encoding note: source/report text passed strict UTF-8 decoding without replacement characters or mojibake byte patterns. The earlier mojibake-looking shell output was a console display artifact, not file corruption.

## 6. UI interaction acceptance

Result: PASS.

Evidence:

- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-transition-overview-targets-01.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-transition-overview-targets-02.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-transition-overview-targets-03.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-transition-targets-reports-01.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-transition-targets-reports-02.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-transition-targets-reports-03.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-transition-reports-settings-01.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-transition-reports-settings-02.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke-transition-reports-settings-03.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\cdp-transition-reports-about-00.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\cdp-transition-reports-about-01.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\cdp-transition-reports-about-02.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\cdp-transition-reports-about-03.png`
- `artifacts\webview2-react-ui-merge-retest-20260521\cdp-transition-reports-about-04.png`

Result:

- No old/new mixed page body observed in inspected live smoke and CDP frames.
- No blank content region or full-page spinner observed.
- Reduced motion smoke produced identical stable page captures for transition groups, with no crossfade blanking.
- `GlassCard` and `MetricCard` no longer have mount opacity/slide animation in source; `uiInteractionContract.test.ts` covers this.

## 7. Real bridge behavior acceptance

Result: PASS for the smoke-covered bridge actions.

WebView2 live smoke evidence:

- `artifacts\webview2-react-ui-merge-retest-20260521\webview2-live-smoke.json`

Observed:

- Overview monitor start/stop: `monitorStartAccepted=true`, `monitorStarted=true`, `monitorStopAccepted=true`, `monitorStopped=true`.
- Targets: `targetsGetOk=true`; path-authority rejection for unsafe target save payload passed.
- Process refresh: `processRefreshObserved=true`; target page loading/result screenshots generated.
- Reports:
  - `reportsListClickOk=true`
  - selected real `reportId`: `JqhZoVeT4vxbHef3n2-AGsUqR2i-0KUbppVi1cGYh5E`
  - `reportOpenClickOk=true`
  - `reportOpenDirectoryClickOk=true`
  - `reportRegenerateClickAccepted=true`
  - `reportRegenerateClickInFlight=true`
  - `reportRegenerateClickCompleted=true`
- Diagnostics: `diagnosticsAccepted=true`, `diagnosticsCompleted=true`.
- Settings: `configDirtyObserved=true`, `configSavingObserved=true`, `configSaveSuccessObserved=true`.

Failure-kept-input evidence from the afterfix interaction run:

- `artifacts\webview2-react-ui-interaction-review-20260521-afterfix\browser-targets-save-failure-kept-input.png`
- `artifacts\webview2-react-ui-interaction-review-20260521-afterfix\browser-settings-save-failure-kept-input.png`
- `browser-interaction-audit.json`: `targetsSaveKeepsInput=true`, `settingsSaveKeepsInput=true`.

## 8. Mock/live boundary

Result: PASS.

- Browser preview screenshots show `预览模式`, making mock mode visible.
- WebView2 screenshots show `本机连接`; live smoke uses `window.chrome.webview` and real bridge responses.
- WebView2 live smoke did not fallback to browser mock success.
- Unsafe path-style actions were rejected:
  - `reportOpenPathRejected=true`
  - `targetsSavePathRejected=true`
  - `reportRegenerateMissingRejected=true`
- About page owns the boundary explanation and does not present fake operations as available workflow controls.

## 9. Issues found

No blocking or release-polish issues were found in this merge retest.

Encoding follow-up that was checked and closed:

- Symptom: PowerShell output rendered some Chinese text as mojibake.
- Verification: strict UTF-8 decoding of the implementation report and representative React source files passed; no replacement characters or mojibake byte patterns were found.
- Result: treated as a console display artifact, not a product/source issue.

## 10. Residual process check

Required process list checked:

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`
- Vite / esbuild / node / test WebView2 user-data related processes

Result: PASS.

No required FrameScope residual processes were found. No Vite/esbuild/node/test WebView2 user-data related process remained from this retest. Existing `msedgewebview2.exe` processes were present, but command lines show they belong to Windows `SearchHost.exe` and Clash Verge (`clash-verge.exe`) WebView2 user-data directories, not this FrameScope retest.

## 11. Packaging recommendation

Enter final packaging candidate verification.

This retest found no WebView2 React UI merge blocker. The next step can be candidate packaging, with normal installer/package smoke verification after packaging.
