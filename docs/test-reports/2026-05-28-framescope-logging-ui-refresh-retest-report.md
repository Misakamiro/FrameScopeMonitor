# FrameScope Monitor logging diagnostics and UI refresh removal retest

Date: 2026-05-28
Conclusion: PASS

Source root:
`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Evidence root:
`artifacts\logging-ui-refresh-retest-20260528`

## Scope boundary

- Retest only; no source implementation changes were made in this retest.
- Did not test BF6.
- Did not launch a real game.
- Did not install or run an installer.
- Did not push GitHub or update Release.
- `build.ps1` generated setup artifacts as its normal build side effect, but no installer was executed.
- The worktree already had many implementation changes before this retest. This report records only the retest commands, evidence, and conclusion.

## Summary

PASS. The combined source-tree retest confirms:

- Default logging stays on lifecycle/failure paths and does not continuously write verbose/perf/debug noise.
- `EnableVerboseLogs` and `EnablePerformanceDiagnosticsLogs` both gate their respective log families.
- Automatic diagnostics are gated to failure/diagnostic paths; healthy full-report paths do not auto-generate diagnostic folders.
- Settings no longer exposes the old monitor/status refresh interval control, while real sampling interval controls remain visible and saveable.
- Legacy `PollIntervalMs` is compatibility-only and normalizes internally to `1000`.
- The native watcher loop uses the internal `1000 ms` cadence.
- Frontend state refresh constants are `1000 ms` visible, `3000 ms` hidden/tray, and `200 ms` coalesced refresh after user operations.
- `PollIntervalMs` does not control PresentMon raw frames or sampler intervals.
- CPU core frequency, CPU Core VID, and real CPU voltage remain separated; VID is not presented as real per-core Vcore, and missing real per-core voltage does not fall back to fake data.

Recommendation: enter a separate local install update validation window. Source-tree build/tests/smoke are clean, but installed-app validation was intentionally not run in this retest.

## Required command results

| Check | Result | Evidence |
| --- | --- | --- |
| `tools\Run-Frontend.ps1 verify` | PASS | `frontend-verify.log`; TypeScript, 5 Vitest files / 53 tests, Vite build all passed. |
| `build.ps1` | PASS | `build.log`; 0 warnings, 0 errors; installer artifacts generated but not run. |
| `tests\Build-FrameScopeTests.ps1` | PASS | `test-build.log`; `FrameScope tests rebuilt.` |
| Full `tests\FrameScope*Tests.exe` sweep | PASS | `csharp-tests-summary.json`; 17 / 17 exit code 0. |
| `tests\chart-sampling-tests.js` | PASS | `chart-sampling-tests.log`; `chart-sampling-tests: PASS`. |
| Logging diagnostics construction | PASS | `Run-LoggingDiagnosticsVerification.ps1`, `logging-diagnostics-verification.json`; `overallPass=true`. |
| Synthetic monitor-session interval separation | PASS | `Run-SyntheticIntervalEvidence.ps1`, `synthetic-monitor-session-interval-evidence.json`; `overallPass=true`. |
| WebView2 live smoke | PASS | `webview2-live-smoke.json`; `success=true`, `pageReady=true`, `errors=0`, `console=0`. |
| WebView2 reduced-motion smoke | PASS | `webview2-reduced-motion-smoke.json`; `success=true`, `pageReady=true`, `reducedMotion=true`, `errors=0`, `console=0`. |
| CPU Core VID chart screenshot | PASS | `cpu-vid-screenshot-focused-summary.json`; `report-cpuVid-1280x720.png`, `report-cpuVid-900x760.png`. |
| `git diff --check` before report | PASS | `git-diff-check-before-report.log`; exit 0, LF/CRLF warnings only. |
| Residual process check before report | PASS | `residual-process-check-before-report.json`; `NO_MATCHING_RESIDUAL_PROCESSES`. |
| `git diff --check` after report | PASS | `git-diff-check-after-report.log`; exit 0, LF/CRLF warnings only. |
| Residual process check after report | PASS | `residual-process-check-after-report.json`; `NO_MATCHING_RESIDUAL_PROCESSES`. |

## Retest checklist

| Requirement | Result | Evidence |
| --- | --- | --- |
| 1. Default logs only key lifecycle/failure events, no continuous verbose/perf/debug noise | PASS | `logging-diagnostics-verification.json`: `defaultNoVerbosePerfOrDebugNoise=true`; default watcher log had one `native-watcher-start` line and no verbose/perf/debug noise. |
| 2. Detailed log switch is effective | PASS | `verboseOffQuiet=true`, `verboseOnDetailed=true`; verbose-on logged `monitor-session-created` and `monitor-children-started`. |
| 3. Performance diagnostics log switch is effective | PASS | `perfOffQuiet=true`, `perfOnDetailed=true`; perf-on logged target wait, child start, capture loop, and total session timings. |
| 4. Auto diagnostics only on failure/diagnostic path | PASS | `autoNormalDoesNotGenerate=true`, `autoFailureGenerates=true`; failure diagnostic copied under `diagnostic-evidence-failure`. |
| 5. Settings normal UI no longer shows monitor/status refresh interval | PASS | `uiDesignContract.test.ts` passed in frontend verify; Settings screenshots `webview2-live-smoke-settings-clean.png` and reduced-motion equivalent captured current UI. |
| 6. Legacy config with `PollIntervalMs` loads and normalizes to internal 1000 ms | PASS | `FrameScopeConfigStoreTests.exe` passed; tests cover legacy `PollIntervalMs=333` normalization and sampler interval preservation. |
| 7. Native watcher loop uses internal 1000 ms | PASS | `FrameScopeNativeWatcherPolicyTests.exe` passed; source contract verifies `Thread.Sleep(FrameScopeConfigStore.InternalPollIntervalMs)` and `InternalPollIntervalMs == 1000`. |
| 8. Frontend status refresh visible/hidden/immediate cadence | PASS | `uiInteractionContract.test.ts` passed; constants verified as 1000, 3000, and 200 ms. WebView2 smoke also exercised monitor start/stop and save-triggered refresh paths. |
| 9. `PollIntervalMs` does not affect real sampling data | PASS | Synthetic session wrote 3 PresentMon raw frame rows, 2511 process sample rows, 5 system sample rows, and status intervals: `SampleIntervalMs=64`, `ProcessSampleIntervalMs=250`, `SlowSampleIntervalMs=1250`, `CpuCoreSampleIntervalMs=750`, `CpuVoltageSampleIntervalMs=1750`, `CpuVidSampleIntervalMs=1750`. |
| 10. Settings real sampling interval area remains and saves | PASS | WebView2 smoke edited `process-sample-interval`; both live and reduced-motion JSON recorded `configDirtyObserved=true`, `configSavingObserved=true`, `configSaveSuccessObserved=true`. |
| 11. CPU frequency / Core VID / real voltage do not fall back incorrectly | PASS | `FrameScopeSystemSamplerCpuCoreTests.exe`, `FrameScopeReportManifestTests.exe`, and CPU VID focused screenshots passed. CPU VID chart note says it is request/target voltage, not real per-core Vcore. |
| 12. Do not test BF6 / real game / install / GitHub / Release | PASS | Only synthetic target, fake PresentMon, source-tree build/tests, WebView2 smoke, and report HTML screenshot were used. |

## Screenshot evidence

- Settings without monitor refresh item:
  - `artifacts\logging-ui-refresh-retest-20260528\webview2-live-smoke-settings-clean.png`
  - `artifacts\logging-ui-refresh-retest-20260528\webview2-reduced-motion-smoke-settings-clean.png`
- Settings sampling interval area:
  - `artifacts\logging-ui-refresh-retest-20260528\webview2-live-smoke-settings-dirty.png`
  - `artifacts\logging-ui-refresh-retest-20260528\webview2-live-smoke-settings-saved.png`
- Logging and diagnostics area:
  - `artifacts\logging-ui-refresh-retest-20260528\webview2-live-smoke-settings-clean.png`
  - `artifacts\logging-ui-refresh-retest-20260528\webview2-reduced-motion-smoke-settings-clean.png`
- CPU Core VID chart:
  - `artifacts\logging-ui-refresh-retest-20260528\report-ui-cpu-vid\report-cpuVid-1280x720.png`
  - `artifacts\logging-ui-refresh-retest-20260528\report-ui-cpu-vid\report-cpuVid-900x760.png`

Note: the reused CDP report UI script returned nonzero for the full report audit because the selected historical CPU VID report had no FPS frame data. The CPU VID focused summary filtered the relevant `cpuVid` records only, and both CPU VID screenshots passed with title `CPU Core VID`, note text present, no section overlaps, and non-empty chart canvas.

## Artifacts created in this retest

- `artifacts\logging-ui-refresh-retest-20260528\frontend-verify.log`
- `artifacts\logging-ui-refresh-retest-20260528\build.log`
- `artifacts\logging-ui-refresh-retest-20260528\test-build.log`
- `artifacts\logging-ui-refresh-retest-20260528\csharp-tests.log`
- `artifacts\logging-ui-refresh-retest-20260528\csharp-tests-summary.json`
- `artifacts\logging-ui-refresh-retest-20260528\chart-sampling-tests.log`
- `artifacts\logging-ui-refresh-retest-20260528\Run-LoggingDiagnosticsVerification.ps1`
- `artifacts\logging-ui-refresh-retest-20260528\logging-diagnostics-verification.json`
- `artifacts\logging-ui-refresh-retest-20260528\Run-SyntheticIntervalEvidence.ps1`
- `artifacts\logging-ui-refresh-retest-20260528\synthetic-monitor-session-interval-evidence.json`
- `artifacts\logging-ui-refresh-retest-20260528\webview2-live-smoke.json`
- `artifacts\logging-ui-refresh-retest-20260528\webview2-reduced-motion-smoke.json`
- `artifacts\logging-ui-refresh-retest-20260528\cpu-vid-screenshot-focused-summary.json`
- `artifacts\logging-ui-refresh-retest-20260528\git-diff-check-before-report.log`
- `artifacts\logging-ui-refresh-retest-20260528\residual-process-check-before-report.json`
- `artifacts\logging-ui-refresh-retest-20260528\git-diff-check-after-report.log`
- `artifacts\logging-ui-refresh-retest-20260528\residual-process-check-after-report.json`

## Final decision

PASS.

The source-tree retest is strong enough to proceed to a separate local install update validation step, if that scope is authorized next. This retest did not perform installation or release work.
