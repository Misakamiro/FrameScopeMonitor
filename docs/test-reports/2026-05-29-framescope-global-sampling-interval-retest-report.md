# FrameScope Global Sampling Interval Retest Report

Date: 2026-05-29
Result: PASS

## Scope

Retested the global telemetry sampling interval change without source edits, BF6, real-game launch, install/update, GitHub push, or release work.

The retest covered ConfigStore compatibility, Settings UI, target list/edit UI, watcher and monitor-session interval propagation, FPS raw-frame statistics, CPU core frequency, CPU Core VID, CPU voltage no-real-per-core-Vcore behavior, logging diagnostics, fixed internal watcher loop behavior, theme/tray settings, WebView2 smoke, `git diff --check`, and residual process cleanup.

## Evidence Root

- `artifacts\global-sampling-retest-20260529\`
- Short-path synthetic run root used to avoid Windows MAX_PATH during report generation:
  - `C:\Users\misakamiro\AppData\Local\Temp\fsgs-retest-20260529b`

## Verification Commands

| Check | Result | Evidence |
| --- | --- | --- |
| `tools\Run-Frontend.ps1 verify` | PASS | TypeScript passed; Vitest 5 files / 55 tests passed; production frontend build passed. |
| `build.ps1` | PASS | Built `dist\FrameScopeMonitor-Setup.exe` and `dist\FrameScopeMonitor-Full-Setup.exe`; no install was run. |
| `tests\Build-FrameScopeTests.ps1` | PASS | `FrameScope tests rebuilt.` |
| C# tests | PASS | Required and affected exe tests passed. |
| `tests\chart-sampling-tests.js` | PASS | `chart-sampling-tests: PASS`. |
| Synthetic monitor-session 1000ms | PASS | `synthetic-monitor-session-retest-evidence.json`; all telemetry intervals are `1000`. |
| Synthetic monitor-session 1500ms | PASS | `synthetic-monitor-session-retest-evidence.json`; all telemetry intervals are `1500`. |
| FPS raw statistics | PASS | `fps-raw-stat-retest-evidence.json`; raw-frame recompute matches report stats. |
| WebView2 live smoke | PASS | `webview2-live-smoke.json`: `success=true`, `reducedMotion=false`. |
| WebView2 reduced-motion smoke | PASS | `webview2-reduced-motion-smoke-retry.json`: `success=true`, `reducedMotion=true`. |
| Screenshot evidence | PASS | Settings, target list, target edit, CPU Core VID screenshots captured. |
| `git diff --check` | PASS | Exit 0; only existing LF/CRLF warnings were printed. |
| Residual process check | PASS | No remaining `FrameScopeMonitor`, sampler, `PresentMon`, `FakePresentMon`, `TslGame`, `msedge`, or Vite process after cleanup. |

## C# Test Executables

All selected tests exited `0`:

- `FrameScopeConfigStoreTests.exe`
- `FrameScopeWebBridgeTests.exe`
- `FrameScopeNativeMonitorChildProcessTests.exe`
- `FrameScopeSystemSamplerCpuCoreTests.exe`
- `FrameScopeReportManifestTests.exe`
- `FrameScopeNativeWatcherPolicyTests.exe`
- `FrameScopeProcessSamplerTests.exe`
- `FrameScopeDiagnosticsTests.exe`
- `FrameScopePresentMonDiagnosticsTests.exe`
- `FrameScopeProcessCleanupTests.exe`
- `FrameScopeLoggingPolicyTests.exe`
- `FrameScopeWebHostLifecycleTests.exe`
- `FrameScopeCapturePlannerTests.exe`
- `FrameScopeReportProgressTests.exe`
- `FrameScopePubgSimulatorTests.exe`
- `FrameScopeWebView2RuntimeTests.exe`
- `FrameScopeUiStateTests.exe`

## ConfigStore

Result: PASS

- `TelemetrySampleIntervalMs` defaults to `1000 ms`.
- Clamp range is `500-5000 ms`.
- Low and high out-of-range values normalize to `500` and `5000`.
- Legacy `SampleIntervalMs`, `ProcessSampleIntervalMs`, and `SlowSampleIntervalMs` values load and normalize to the global interval.
- `PollIntervalMs` is compatibility/internal only and normalizes to `FrameScopeConfigStore.InternalPollIntervalMs`.

Evidence:

- `FrameScopeConfigStoreTests.exe`: PASS.
- Static Settings/Targets source check:
  - `SettingsTelemetrySampleIntervalRefs=3`
  - `SettingsPollIntervalRefs=0`
  - `TargetsContainsSample100Text=false`
  - target edit row contains no `ProcessSampleIntervalMs` / `SlowSampleIntervalMs` input.

## Settings UI

Result: PASS

- Settings exposes one global sampling interval field.
- It no longer exposes separate backend process sampling, monitor refresh, or status refresh interval controls.
- The field text and tests cover the `500-5000 ms` range and that the setting controls backend process, system, and hardware telemetry.
- Settings text states FPS raw frame statistics are not downsampled by this interval.

Evidence:

- `webview2-live-smoke-settings-sampling.png`
- `webview2-reduced-motion-smoke-retry-settings-sampling.png`
- `FrameScopeWebBridgeTests.exe`: PASS.
- `src\frontend\src\uiDesignContract.test.ts` and `src\frontend\src\uiInteractionContract.test.ts` were included by `tools\Run-Frontend.ps1 verify`.

## Targets UI

Result: PASS

- Target list no longer displays `采样 100 毫秒` or any per-target sampling column.
- Target edit row no longer exposes per-target sampling interval editing.
- Legacy target `100ms` values do not pollute the UI.

Evidence:

- `webview2-live-smoke-targets-result.png`
- `webview2-reduced-motion-smoke-retry-targets-result.png`
- `browser-target-edit-no-per-target-sampling.png`
- `browser-target-edit-no-per-target-sampling.json`:
  - `hasEditingRow=true`
  - `hasTargetNameField=true`
  - `hasTargetProcessField=true`
  - `containsSample100=false`
  - `containsPerTargetInterval=false`

## Monitor Session / Watcher

Result: PASS

Synthetic monitor-session runs used a fake target process and fake PresentMon. No real game was launched.

For `1000 ms`:

- `SampleIntervalMs=1000`
- `ProcessSampleIntervalMs=1000`
- `SlowSampleIntervalMs=1000`
- `CpuCoreSampleIntervalMs=1000`
- `CpuVoltageSampleIntervalMs=1000`
- `CpuVidSampleIntervalMs=1000`
- `ControlPollIntervalMs=1000`

For `1500 ms`:

- `SampleIntervalMs=1500`
- `ProcessSampleIntervalMs=1500`
- `SlowSampleIntervalMs=1500`
- `CpuCoreSampleIntervalMs=1500`
- `CpuVoltageSampleIntervalMs=1500`
- `CpuVidSampleIntervalMs=1500`
- `ControlPollIntervalMs=1000`

Evidence:

- `synthetic-monitor-session-retest-evidence.json`
- `FrameScopeNativeWatcherPolicyTests.exe`: PASS.
- `FrameScopeNativeMonitorChildProcessTests.exe`: PASS.
- `FrameScopeSystemSamplerCpuCoreTests.exe`: PASS.

## FPS Raw Statistics

Result: PASS

PresentMon raw frame data was not downsampled by either global interval:

- `1000 ms`: `presentmon=240`, `rawPresentMonRows=240`, `validPresentMonRows=240`, `frames=240`.
- `1500 ms`: `presentmon=240`, `rawPresentMonRows=240`, `validPresentMonRows=240`, `frames=240`.

Raw-frame recomputation matched report `frameStats`:

- `1000 ms`: average `60`, 1% Low `60`, 0.1% Low `60`.
- `1500 ms`: average `60`, 1% Low `60`, 0.1% Low `60`.

Evidence:

- `fps-raw-stat-retest-evidence.json`
- `FrameScopeReportManifestTests.exe`: PASS, including raw-frame average / 1% Low / 0.1% Low assertions.
- `tests\chart-sampling-tests.js`: PASS.

## Non-Regression Coverage

Result: PASS

- CPU core frequency chart remains available in generated reports.
- CPU Core VID chart remains available and labels VID as request/target voltage, not real Vcore.
- CPU voltage no-real-per-core-Vcore state remains explicit: synthetic run reported `CpuVoltageStatus=non-per-core-only`, with no fake per-core Vcore chart.
- Logging diagnostics tests passed.
- `PollIntervalMs` remains the internal watcher loop path and did not replace telemetry intervals.
- Theme/tray settings remained covered by WebView2 smoke theme passes and WebBridge tests.

Evidence:

- `cpu-core-vid-report.png`
- `cpu-core-vid-report.json`: title `CPU Core VID`; note states VID is request/target voltage and not real per-core Vcore.
- `FrameScopeLoggingPolicyTests.exe`: PASS.
- `FrameScopeWebBridgeTests.exe`: PASS.
- `FrameScopeNativeWatcherPolicyTests.exe`: PASS.

## Retest Notes

- A first synthetic run under the long repo artifact path hit `PathTooLongException` during report generation. I reran under `C:\Users\misakamiro\AppData\Local\Temp\fsgs-retest-20260529b`, and both 1000ms and 1500ms synthetic sessions passed.
- An initial reduced-motion smoke run with the normal 45s timeout produced screenshots but timed out waiting for the bridge smoke. A retry with `--web-ui-timeout-ms 90000` passed and is the evidence used for the final result.
- The built-in WebView2 smoke does not open the target edit row, so the target-edit screenshot was captured from the local frontend mock bridge using the same compiled frontend state. The screenshot is evidence for the edit UI surface only; WebView2 smoke covers the real bridge workflow.

## Required Conclusions

1. Global sampling interval completely replaces user-facing per-target sampling: YES.
2. Default and range are correct: YES, default `1000 ms`, clamp/UI range `500-5000 ms`.
3. Telemetry files use the same interval: YES for `process-samples.csv`, `system-samples.csv`, `cpu-core-samples.csv`, `cpu-voltage-samples.csv`, and `cpu-vid-samples.csv` in both 1000ms and 1500ms synthetic runs.
4. FPS raw statistics are not broken: YES, raw PresentMon rows remain intact and average / 1% Low / 0.1% Low recompute matches report stats.
5. Recommendation: proceed to local install/update validation next, if install-scope is opened. This retest did not install or update the local app by request.
