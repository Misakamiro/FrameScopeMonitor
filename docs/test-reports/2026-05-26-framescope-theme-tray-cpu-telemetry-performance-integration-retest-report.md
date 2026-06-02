# FrameScope Monitor theme/tray/CPU telemetry/performance integration retest

Date: 2026-05-26 Asia/Hong_Kong

Source root:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Verdict

FAIL for the integration/update gate.

Most runtime and C# validation passed, but the required frontend verification command failed at TypeScript typecheck:

```text
src/data/mockPreview.ts(289,3): error TS2322:
Property 'ProcessSamplingMode' is missing in type ...
```

The failing block is the `targetPreview.map(...)` target config construction in `src\frontend\src\data\mockPreview.ts:289-297`. Because `tools\Run-Frontend.ps1 verify` is mandatory and failed, this source tree should not move to local install update validation yet.

No code was changed, no installer was run, no real game was launched, and nothing was pushed to GitHub.

## Reports Read First

- `docs\implementation-reports\2026-05-25-framescope-theme-settings-implementation-report.md`
- `docs\implementation-reports\2026-05-25-framescope-tray-window-lifecycle-implementation-report.md`
- `docs\implementation-reports\2026-05-25-framescope-cpu-core-frequency-telemetry-report.md`
- `docs\implementation-reports\2026-05-25-framescope-performance-optimization-pass1-report.md`

## Feature Area Status

| Area | Status | Evidence |
|---|---|---|
| Theme/Settings/config | PARTIAL | WebView2 live/reduced smoke captured light/dark/system Settings, Overview, Reports. C# config/web bridge tests passed. Blocked by frontend typecheck failure in mock preview config. |
| Tray/window lifecycle | PASS | Tray smoke passed: X hide, tray show, repeated hide/show, active monitoring blocked exit, no duplicate tray icon, automation/dispose close guards. |
| CPU core telemetry | PASS | Enabled synthetic session wrote `cpu-core-samples.csv`; 128 rows, 16 logical processors, Actual Frequency 3979-4864 MHz. Disabled session wrote no CPU core CSV/sidecar. Voltage stayed `unavailable`. |
| Performance pass 1 | PASS with measurement note | 250ms/100ms focused matrix passed acceptance using `process-samples.csv` CPU rows. External PID CPU sampling in this short run captured zero CPU delta, so it is recorded only as low-resolution supplemental evidence. |
| Regression smoke | PASS where executable smoke was run | WebView2 live, reduced-motion, Reports live actions, monitor.stop `remainingProcessCount=0`, PresentMon access-denied classification tests, missing CSV tests, and UI fixture screenshots passed. |

## Command Results

| Command/check | Result | Notes |
|---|---:|---|
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | FAIL | `tsc --noEmit` failed because mock target configs miss required `ProcessSamplingMode`. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | Regenerated normal `dist` artifacts as existing build behavior only; not treated as release packaging. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | `FrameScope tests rebuilt.` |
| All 14 requested C# test exe | PASS | `FrameScopeCapturePlannerTests`, `ConfigStore`, `Diagnostics`, `NativeMonitorChildProcess`, `PresentMonDiagnostics`, `ProcessCleanup`, `ProcessSampler`, `PubgSimulator`, `ReportManifest`, `ReportProgress`, `SystemSamplerCpuCore`, `WebBridge`, `WebHostLifecycle`, `WebView2Runtime`. |
| WebView2 live smoke | PASS | `success=true`, `themeSmoke.success=true`, `reportLiveActionSmoke.success=true`, monitor stop `remainingProcessCount=0`. |
| WebView2 reduced-motion smoke | PASS | `success=true`, `reducedMotion=true`, monitor stop `remainingProcessCount=0`. |
| WebView2 tray lifecycle smoke | PASS | `duplicateTrayIconsPrevented=true`, `blockedExit=true`, `automationCloseGuard=true`, `disposeGuard=true`. |
| Synthetic monitor-session CPU telemetry enabled/disabled | PASS | Enabled generated CPU core CSV; disabled generated no noise files. |
| Performance 250ms/100ms focused matrix | PASS | Acceptance gates passed; see performance section. |
| `git diff --check` | PASS | Exit 0; only LF-to-CRLF working-copy warnings. |
| Residual process check | PASS | Final `matchingResidualCount=0`. |

## Key Evidence Paths

Root evidence directory:

`artifacts\integration-retest-20260526`

WebView2 live/reduced smoke:

- `artifacts\integration-retest-20260526\webview2-live\smoke.json`
- `artifacts\integration-retest-20260526\webview2-live\smoke.png`
- `artifacts\integration-retest-20260526\webview2-reduced-motion\smoke.json`
- `artifacts\integration-retest-20260526\webview2-reduced-motion\smoke.png`

Theme screenshots:

- `artifacts\integration-retest-20260526\webview2-live\smoke-settings-light.png`
- `artifacts\integration-retest-20260526\webview2-live\smoke-settings-dark.png`
- `artifacts\integration-retest-20260526\webview2-live\smoke-settings-system.png`
- `artifacts\integration-retest-20260526\webview2-live\smoke-overview-light.png`
- `artifacts\integration-retest-20260526\webview2-live\smoke-overview-dark.png`
- `artifacts\integration-retest-20260526\webview2-live\smoke-overview-system.png`
- `artifacts\integration-retest-20260526\webview2-live\smoke-reports-light.png`
- `artifacts\integration-retest-20260526\webview2-live\smoke-reports-dark.png`
- `artifacts\integration-retest-20260526\webview2-live\smoke-reports-system.png`

Tray lifecycle:

- `artifacts\integration-retest-20260526\webview2-tray-lifecycle\smoke.json`

CPU telemetry:

- `artifacts\integration-retest-20260526\cpu-core-monitor-session\cpu-core-monitor-session-summary.json`
- `artifacts\integration-retest-20260526\cpu-core-monitor-session\cpu-core-data-js-field-check.json`
- Enabled CSV: `artifacts\integration-retest-20260526\cpu-core-monitor-session\enabled\runs\enabled-20260526-133926\cpu-core-samples.csv`
- Enabled manifest: `artifacts\integration-retest-20260526\cpu-core-monitor-session\enabled\runs\enabled-20260526-133926\charts\framescope-interactive-manifest.json`

Performance:

- `artifacts\integration-retest-20260526\performance-focused-matrix\performance-focused-matrix.json`
- `artifacts\integration-retest-20260526\performance-focused-matrix\performance-focused-matrix-process-samples-cpu.json`

UI regression screenshots:

- `artifacts\integration-retest-20260526\ui-fixtures\sidebar-overview-first-load.png`
- `artifacts\integration-retest-20260526\ui-fixtures\overview-first-click-feedback.png`
- `artifacts\integration-retest-20260526\ui-fixtures\targets-lookup-empty.png`
- `artifacts\integration-retest-20260526\ui-fixtures\targets-lookup-failure.png`
- `artifacts\integration-retest-20260526\ui-fixtures\targets-lookup-results-many.png`
- `artifacts\integration-retest-20260526\ui-fixtures\reports-more-menu-open.png`
- Index: `artifacts\integration-retest-20260526\ui-fixtures\ui-fixture-screenshots.json`

Residual process:

- `artifacts\integration-retest-20260526\residual-process-check-final.json`

## Acceptance Details

### Theme/Settings/config

- Light/dark/system screenshots were captured for Settings, Overview, and Reports.
- `FrameScopeConfigStoreTests.exe` passed, covering defaults, old config normalization, explicit `TrayEnabled=false`, CPU telemetry normalization, and process sampling mode compatibility.
- `FrameScopeWebBridgeTests.exe` passed, covering `config.save` and `targets.save` preservation of new global fields.
- Settings dirty/saving/saved screenshots were captured by WebView2 smoke.
- Save-failure dirty draft retention is covered by frontend contract source/tests, but the full frontend verify command is blocked by the current type error.

### Tray/window lifecycle

Tray smoke JSON key fields:

```json
{
  "success": true,
  "firstHide": true,
  "shown": true,
  "secondHide": true,
  "trayInstanceAfterFirstHide": 1,
  "trayInstanceAfterSecondHide": 1,
  "duplicateTrayIconsPrevented": true,
  "blockedExit": true,
  "stillVisibleAfterBlockedExit": true,
  "exitAllowedWithoutActiveMonitoring": true,
  "automationCloseGuard": true,
  "disposeGuard": true
}
```

### CPU core telemetry

Enabled session summary:

```json
{
  "cpuCoreCsvExists": true,
  "cpuCoreCsvRows": 128,
  "uniqueLogicalProcessorCount": 16,
  "actualFrequencyMinMHz": 3979,
  "actualFrequencyMaxMHz": 4864,
  "manifestCpuCoreSampleCount": 128,
  "manifestCpuCoreTelemetryAvailable": true,
  "manifestCpuVoltageAvailable": false
}
```

Disabled session summary:

```json
{
  "cpuCoreCsvExists": false,
  "cpuCoreStatusSidecarExists": false,
  "statusCpuCoreTelemetryEnabled": false,
  "statusCpuCoreSampleCount": 0,
  "statusCpuVoltageAvailable": false,
  "statusCpuVoltageStatus": "unavailable"
}
```

`cpu-core-data-js-field-check.json` confirms `framescope-interactive-data.js` does not contain CPU core raw row/chart fields such as `cpuCoreSamples`, `ActualFrequencyMHz`, `LogicalProcessor`, `cpuCoreChart`, `cpuCoreSampleCount`, or `cpuVoltageAvailable`.

### Performance gate

Focused matrix results from `process-samples.csv` CPU rows:

| Gate | Result |
|---|---:|
| 250ms normal total avg CPU <= 3% | PASS, 0.2688% FrameScope chain average |
| 100ms high precision no total regression vs 4.77% baseline +10% | PASS, 0.3886% FrameScope chain average |
| 100ms high precision ProcessSampler no regression vs 3.57% baseline +10% | PASS, 0.2333% ProcessSampler average |
| PresentMon/FPS semantics preserved | PASS, 360 rows and `hasFrameData=true` in both focused runs |
| Remaining child processes | PASS, none reported in matrix summary |

Measurement note: the external PID sampler JSON recorded `0` CPU delta for the short run, so the acceptance table above uses monitor-session `process-samples.csv` CPU rows. The external PID result is retained as supplemental evidence, not as the primary CPU gate.

## Regression Notes

- PresentMon stderr access denied classification remains covered by `FrameScopePresentMonDiagnosticsTests.exe`, `FrameScopeNativeMonitorChildProcessTests.exe`, and `FrameScopeReportManifestTests.exe`; observed status: `presentmon-etw-access-denied`.
- True missing CSV remains covered by `FrameScopeReportManifestTests.exe`; observed diagnostic path: `no-presentmon-csv`.
- Reports live actions passed in WebView2 live smoke: list, open report, open directory, regenerate accepted, regenerate in-flight, regenerate completed.
- UI first-click/sidebar/Targets/Reports/Overview evidence was captured through WebView2 smoke plus Edge CDP mock fixture screenshots. The CDP fixture used the existing `dist\FrameScopeMonitor-payload\frontend` build, did not edit source, and did not launch real games.

## Residual Process Check

Final residual process check:

```json
{
  "matchingResidualCount": 0,
  "matches": []
}
```

The scan included FrameScope app/sampler/report processes, PresentMon, fake PresentMon, fake target processes, and the short-lived Edge/Node fixture automation used for screenshots.

## Recommendation

Do not enter local installed-app update verification yet.

Fix the frontend typecheck blocker first by making the mock preview target config satisfy the current `FrameScopeTargetConfig` contract, then rerun at minimum:

- `tools\Run-Frontend.ps1 verify`
- WebView2 live smoke
- WebView2 reduced-motion smoke
- `git diff --check`
- residual process check

After that passes, this source tree can be considered for local install update validation.
