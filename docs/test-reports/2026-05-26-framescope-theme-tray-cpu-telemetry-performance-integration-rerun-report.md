# FrameScope theme/tray/CPU telemetry/performance integration rerun

Date: 2026-05-26 Asia/Hong_Kong

Source root:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Verdict

PASS.

The previous integration blocker is cleared. The mandatory frontend command:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

now exits `0`, with TypeScript typecheck, Vitest, and Vite build all passing. The earlier `mockPreview.ts` `ProcessSamplingMode` type gate is no longer blocking the integration chain.

No BF6 test was run, no real game was launched, no installer was run, nothing was pushed to GitHub, and no Release was updated.

## Reports Read First

- `docs\test-reports\2026-05-26-framescope-theme-tray-cpu-telemetry-performance-integration-retest-report.md`
- `docs\implementation-reports\2026-05-26-framescope-mock-preview-process-sampling-mode-fix-report.md`
- `docs\implementation-reports\2026-05-25-framescope-theme-settings-implementation-report.md`
- `docs\implementation-reports\2026-05-25-framescope-tray-window-lifecycle-implementation-report.md`
- `docs\implementation-reports\2026-05-25-framescope-cpu-core-frequency-telemetry-report.md`
- `docs\implementation-reports\2026-05-25-framescope-performance-optimization-pass1-report.md`

## Feature Area Status

| Area | Status | Rerun result |
|---|---:|---|
| Theme/Settings/config | PASS | Frontend verify passed; WebView2 live and reduced-motion smoke passed; light/dark/system Settings screenshots exist. |
| Tray/window lifecycle | PASS | Tray smoke passed X-to-tray, restore, duplicate prevention, active-monitoring blocked exit, automation/dispose guards. |
| CPU core telemetry | PASS | Enabled synthetic session wrote `cpu-core-samples.csv`; disabled synthetic session wrote no CPU core CSV or status sidecar; manifest contains metadata only. |
| Performance pass 1 | PASS | 250ms normal and 100ms high-precision focused gates passed against baseline; no remaining matrix child processes. |

## Command Results

| Command/check | Result | Evidence |
|---|---:|---|
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | `artifacts\integration-rerun-20260526\command-logs\frontend-verify.log`; Vitest `5 passed`, `49 passed`; Vite build completed. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | `artifacts\integration-rerun-20260526\command-logs\build.log`; build emitted existing `dist` setup artifacts as normal build side effect only. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | `artifacts\integration-rerun-20260526\command-logs\build-tests.log`; `FrameScope tests rebuilt.` |
| All C# test exe in `tests` | PASS | `artifacts\integration-rerun-20260526\csharp-test-results.json`; 15/15 passed, including all 14 requested test executables plus `FrameScopeUiStateTests.exe`. |
| WebView2 live smoke | PASS | `artifacts\integration-rerun-20260526\webview2-live\smoke.json`; `success=true`, `smokePayload.success=true`, `themeSmoke.success=true`. |
| WebView2 reduced-motion smoke | PASS | `artifacts\integration-rerun-20260526\webview2-reduced-motion\smoke.json`; `success=true`, `reducedMotion=true`. |
| WebView2 tray lifecycle smoke | PASS | `artifacts\integration-rerun-20260526\webview2-tray-lifecycle\smoke.json`; `success=true`. |
| CPU telemetry enabled/disabled synthetic sessions | PASS | `artifacts\integration-rerun-20260526\cpu-core-monitor-session-direct\cpu-core-monitor-session-summary.json`. |
| Performance focused matrix | PASS | `artifacts\integration-rerun-20260526\performance-focused-matrix\performance-focused-matrix-process-samples-cpu.json`. |
| PresentMon access-denied/missing-csv regression | PASS | `artifacts\integration-rerun-20260526\presentmon-regression-summary.json`. |
| UI snapshot spot check | PASS | `artifacts\integration-rerun-20260526\ui-fixtures\ui-fixture-verification.json`. |
| `git diff --check` | PASS | `artifacts\integration-rerun-20260526\command-logs\git-diff-check.log`; exit `0`, only LF-to-CRLF working-copy warnings. |
| Residual process check | PASS | `artifacts\integration-rerun-20260526\residual-process-check-final.json`; `matchingResidualCount=0`. |

## C# Test Executables

All of these passed with exit code `0`:

- `FrameScopeCapturePlannerTests.exe`
- `FrameScopeConfigStoreTests.exe`
- `FrameScopeDiagnosticsTests.exe`
- `FrameScopeNativeMonitorChildProcessTests.exe`
- `FrameScopePresentMonDiagnosticsTests.exe`
- `FrameScopeProcessCleanupTests.exe`
- `FrameScopeProcessSamplerTests.exe`
- `FrameScopePubgSimulatorTests.exe`
- `FrameScopeReportManifestTests.exe`
- `FrameScopeReportProgressTests.exe`
- `FrameScopeSystemSamplerCpuCoreTests.exe`
- `FrameScopeWebBridgeTests.exe`
- `FrameScopeWebHostLifecycleTests.exe`
- `FrameScopeWebView2RuntimeTests.exe`

Additional current test executable also passed:

- `FrameScopeUiStateTests.exe`

## WebView2 Smoke Evidence

Live smoke:

- `artifacts\integration-rerun-20260526\webview2-live\smoke.json`
- `artifacts\integration-rerun-20260526\webview2-live\smoke.png`

Reduced-motion smoke:

- `artifacts\integration-rerun-20260526\webview2-reduced-motion\smoke.json`
- `artifacts\integration-rerun-20260526\webview2-reduced-motion\smoke.png`

Tray lifecycle smoke:

- `artifacts\integration-rerun-20260526\webview2-tray-lifecycle\smoke.json`

Tray key fields:

```json
{
  "success": true,
  "duplicateTrayIconsPrevented": true,
  "blockedExit": true,
  "stillVisibleAfterBlockedExit": true,
  "exitAllowedWithoutActiveMonitoring": true,
  "automationCloseGuard": true,
  "disposeGuard": true
}
```

## UI Snapshot Spot Check

Theme screenshots from live WebView2 smoke:

- `artifacts\integration-rerun-20260526\webview2-live\smoke-settings-light.png`
- `artifacts\integration-rerun-20260526\webview2-live\smoke-settings-dark.png`
- `artifacts\integration-rerun-20260526\webview2-live\smoke-settings-system.png`
- `artifacts\integration-rerun-20260526\webview2-live\smoke-reports-light.png`
- `artifacts\integration-rerun-20260526\webview2-live\smoke-reports-dark.png`
- `artifacts\integration-rerun-20260526\webview2-live\smoke-reports-system.png`

Additional Edge CDP mock-preview spot checks:

- `artifacts\integration-rerun-20260526\ui-fixtures\sidebar-overview-first-load.png`
- `artifacts\integration-rerun-20260526\ui-fixtures\overview-first-click-feedback.png`
- `artifacts\integration-rerun-20260526\ui-fixtures\reports-more-menu-open.png`
- `artifacts\integration-rerun-20260526\ui-fixtures\ui-fixture-screenshots.json`
- `artifacts\integration-rerun-20260526\ui-fixtures\ui-fixture-verification.json`

Verification summary:

```json
{
  "overviewActive": true,
  "monitorStartFeedbackObserved": true,
  "reportsMenuOpen": true,
  "screenshotsPresent": true
}
```

## CPU Telemetry Gate

Acceptance evidence:

- `artifacts\integration-rerun-20260526\cpu-core-monitor-session-direct\cpu-core-monitor-session-summary.json`
- `artifacts\integration-rerun-20260526\cpu-core-monitor-session-direct\cpu-core-data-js-field-check.json`

Enabled session:

```json
{
  "monitorExit": 0,
  "reportExit": 0,
  "cpuCoreCsvExists": true,
  "cpuCoreCsvRows": 224,
  "uniqueLogicalProcessorCount": 16,
  "actualFrequencyMinMHz": 3775,
  "actualFrequencyMaxMHz": 4863,
  "manifestCpuCoreSampleCount": 224,
  "manifestCpuCoreTelemetryAvailable": true,
  "manifestCpuVoltageAvailable": false
}
```

Disabled session:

```json
{
  "monitorExit": 0,
  "reportExit": 0,
  "cpuCoreCsvExists": false,
  "cpuCoreStatusSidecarExists": false,
  "statusCpuCoreTelemetryEnabled": false,
  "statusCpuCoreSampleCount": 0,
  "manifestCpuCoreSampleCount": 0,
  "manifestCpuCoreTelemetryAvailable": false,
  "manifestCpuVoltageAvailable": false
}
```

The accepted CPU evidence uses the direct rerun directory above. An earlier wrapper attempt produced valid CSV/manifest files, but PowerShell `Start-Process` did not return a usable monitor exit code, so it was not used for acceptance.

## Performance Gate

Evidence:

- `artifacts\integration-rerun-20260526\performance-focused-matrix\performance-focused-matrix.json`
- `artifacts\integration-rerun-20260526\performance-focused-matrix\performance-focused-matrix-process-samples-cpu.json`

The performance gate was computed from monitor-session `process-samples.csv` CPU rows for the synthetic FrameScope chain.

| Gate | Result |
|---|---:|
| 250ms normal total avg CPU <= 3% | PASS, `0.2162%` |
| 100ms high precision total no regression vs baseline `4.77% + 10%` | PASS, `0.35%` |
| 100ms high precision ProcessSampler no regression vs baseline `3.57% + 10%` | PASS, `0.1995%` |
| PresentMon/FPS semantics preserved | PASS, both focused runs captured frame data. |
| Remaining performance child processes | PASS, no remaining child processes. |

WebView2 live and reduced-motion smoke both also exercised `monitor.stop`; their raw smoke JSON contains `remainingProcessCount:0` in the `monitor.stopped` event.

## PresentMon Regression Gate

Evidence:

- `artifacts\integration-rerun-20260526\presentmon-regression-summary.json`
- `artifacts\integration-rerun-20260526\command-logs\csharp-tests\FrameScopeReportManifestTests.log`

Observed:

```json
{
  "accessDeniedExpected": "presentmon-etw-access-denied",
  "accessDeniedStatusObserved": true,
  "accessDeniedFailureCategoryObserved": true,
  "accessDeniedEtwBooleanObserved": true,
  "missingCsvExpected": "no-presentmon-csv",
  "missingCsvStatusObserved": true
}
```

## Residual Process Check

Final residual process evidence:

- `artifacts\integration-rerun-20260526\residual-process-check-final.json`

Result:

```json
{
  "matchingResidualCount": 0,
  "matches": []
}
```

The final scan covered FrameScope app/sampler/report processes, PresentMon/FakePresentMon, synthetic target processes, WebView2 smoke processes, and the temporary Edge/Node fixture automation from this rerun.

## Recommendation

Yes. This source tree is ready to enter local installed-app update verification.

The recommendation is limited to the next local install-update validation step. This rerun did not run an installer, did not package a release, did not push GitHub, and did not update any Release asset.
