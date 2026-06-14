# FrameScope Chart Screen-Space Same-X Artifact Fix Report

Date: 2026-06-13

Status: PASS

## Scope

- Target run: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260613-152103`
- Evidence directory: `docs\test-reports\2026-06-13-framescope-chart-screen-space-same-x-artifact-fix-evidence`
- No install, no real game launch, no BF6 test, no GitHub push, no Release update.
- Note: one early `build.ps1` run generated setup artifacts in `dist` because that script always does so. They were not installed, published, pushed, or used for validation. Later generator rebuilds used a direct `csc` command only.

## Root Cause

The previous fix removed same-time min/max pairs, but the render path still allowed different timestamps to land on the same canvas pixel column. The line and area paths then connected multiple finite values inside one screen x bin, producing visible vertical cuts.

Investigation found two additional contributors:

- `Number(null)` treated invalid telemetry gaps as finite `0`, so Vcore could be pulled toward the chart baseline. This matches the failed retest evidence where Vcore had `finiteMin=0`.
- Full-range rendering still used `lowerBoundTime` / `upperBoundTime` slicing. Process timestamps can include duplicate or boundary-sensitive values, so the full view could drop some real process peak samples when a range object was passed.

## Fix

- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`
  - Added final screen-space compaction before drawing. Adjacent points that round to the same screen x are merged into one render point.
  - Continuous metrics such as Vcore use a stable average representative inside the pixel bin.
  - Process charts keep the peak value inside the pixel bin, preserving short real spikes while preventing same-x vertical line segments.
  - `null`, `undefined`, and empty values are no longer treated as numeric `0`; real numeric `0` remains valid for process CPU and other true zero metrics.
  - Spike mode keeps all original samples when `len <= chart pixel width`, then lets screen compaction remove only true pixel duplicates.
  - Full-range views skip binary-search slicing; zoomed/panned ranges still slice.
  - Background process area fill remains disabled; process chart is line-only.
- `tests\chart-sampling-tests.js`
  - Added screen-space duplicate x and same-x vertical jump checks.
  - Added dense Vcore finite-width regression coverage.
  - Added process sparse/dense spike coverage proving peaks stay visible while same-screen-x jumps are zero.
  - Kept `DATA.cpuVoltage`, `DATA.cpuVid`, and `bucketMs=1000` source/semantic assertions.

## CS2 Evidence

Key files:

- `after-vcore-chart.png`
- `after-process-chart.png`
- `after-render-probe.json`
- `screen-space-render-probe.json`
- `layout-probe\report-overflow-probe.json`

Screen-space render probe:

- `DATA.cpuVoltage.available=true`
- `DATA.cpuVoltage.sampleCount=946`
- `DATA.cpuVid.available=false`
- `DATA.cpuVid.sampleCount=0`
- `DATA.fps.bucketMs=1000`
- PresentMon raw/valid rows: `454316 / 454316`, selection mode `all`
- Vcore: raw `946`, rendered `946`, duplicate screen x `0`, same-x vertical `0`, finite range `0.948-1.116V`
- Process Top 10: every series has duplicate screen x `0` and same-x vertical `0`
- Process peaks retained: `7.88%`, `5.31%`, `4.45%`, `3.18%`, `2.70%`, `2.24%`, `2.14%`, `1.95%`, `1.83%`, `1.83%`

Visual review:

- Vcore after screenshot shows a continuous Vcore area/line without dense dark vertical cuts down to `0`.
- Background process after screenshot remains line-only. Short spike lines are still visible and the heavy filled-area pollution is absent.

## Boundary Checks

- `DATA.cpuVoltage` remains the Vcore source.
- `DATA.cpuVid.available=false` remains the report-page result for the AMD low VID rejection case.
- Vcore is not used as VID; VID is not used as Vcore.
- `bucketMs=1000` remains unchanged.
- FPS remains based on raw PresentMon bucket statistics.
- No low P-state filtering was added; true numeric zeros and low values are still accepted where semantically valid.
- Chinese report text, worker/single-instance behavior, invalid low VID rejection, and process tooltip/table data were preserved.

## Verification

Passed:

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
  - TypeScript typecheck passed.
  - Vitest: `6` files, `64` tests passed.
  - Vite production build passed.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
  - `FrameScope tests rebuilt.`
- `.\tests\FrameScopeReportManifestTests.exe`
  - `FrameScopeReportManifestTests: PASS`
- `.\tests\FrameScopeDiagnosticsTests.exe`
  - `FrameScopeDiagnosticsTests: PASS`
- `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`
  - `FrameScopeSystemSamplerCpuCoreTests: PASS`
- `.\tests\FrameScopeNativeWatcherPolicyTests.exe`
  - `FrameScopeNativeWatcherPolicyTests: PASS`
- `.\tests\FrameScopeNativeMonitorChildProcessTests.exe`
  - `FrameScopeNativeMonitorChildProcessTests: PASS`
- `.\tests\FrameScopeProcessCleanupTests.exe`
  - `FrameScopeProcessCleanupTests: PASS`
- `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe`
  - all four subchecks passed; final `FrameScopeSingleInstanceLaunchGuardTests: PASS`
- Bundled Node `.\tests\chart-sampling-tests.js`
  - `chart-sampling-tests: PASS`
- Layout probe:
  - `allNoOverflow=true`
  - `cpu-voltage-1280x720`, `background-process-1280x720`, `cpu-voltage-900x760`, and `background-process-900x760` show no page or chart horizontal overflow in probe output.
  - Probe screenshots were generated for both target views. The JSON file contains mojibake strings that prevent PowerShell `ConvertFrom-Json` parsing, so the result was read from the raw output.
- CS2 after report regenerated with local `FrameScopeReportGenerator.exe`.
- Screenshot review completed for Vcore and background process charts.
- `git diff --check`
  - exit code `0`; Git printed LF/CRLF working-copy warnings only.
- Residual process check:
  - `NO_MATCHING_RESIDUAL_PROCESSES`

## Not Done

- Did not install.
- Did not launch a real game.
- Did not test BF6.
- Did not push GitHub.
- Did not update Release.
- Did not intentionally package for delivery; an early full `build.ps1` run generated setup artifacts as a side effect and they were not used.
