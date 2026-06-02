# FrameScope CPU Core VID Recording And Chart Report

Date: 2026-05-31

Final conclusion: PASS

## Direct Answers

- CPU Voltage / Vcore chart retained: YES. The existing `DATA.cpuVoltage` path and `CPU Voltage / Vcore` chart remain separate and still represent overall real CPU Voltage / Vcore.
- CPU Core VID added or corrected as extra data and extra chart: YES. VID stays in `cpu-vid-samples.csv`, `cpu-vid-telemetry-status.json`, `DATA.cpuVid`, and the independent `CPU Core VID` report tab/chart.
- `Core 0 VID` through `Core 7 VID` records correctly: YES. Zero-based names keep their numeric core id, so `Core 0 VID` and `Core 1 VID` are not merged.
- `Core #1 VID` through `Core #8 VID` records correctly: YES. Hash-prefixed names remain one-based and map to zero-based core indexes 0 through 7.
- Same-value VID remains separate by core: YES. Synthetic 8-core VID at `0.975 V` produces 8 independent series and preserves the original `SensorName`.
- VID does not enter CPU Voltage / Vcore: YES. VID rows are rejected by the CPU Voltage / Vcore classifier and report tests assert `DATA.cpuVoltage.series` stays empty for VID-only runs.
- CPU Voltage / Vcore is not replaced by VID: YES. The Vcore chart is still generated from explicit Vcore/CPU Voltage CSV rows only.
- FPS changed: NO. This round did not change PresentMon frame parsing or FPS bucketing. `chart-sampling-tests.js` still verifies raw PresentMon stats and `bucketMs=1000`.
- Packaging/install/game/release actions: NO packaging, no FrameScope install, no real game launch, no BF6 test, no GitHub push, no Release update.

## Implementation

- Added shared CPU Core VID parsing helpers in `FrameScopeSystemSampler.CpuCoreTelemetry.cs`.
  - `Core 0 VID` to `Core 7 VID` are treated as zero-based and are not decremented.
  - `Core #1 VID` to `Core #8 VID` are treated as one-based and are converted to zero-based core ids.
  - SOC VID, Package VID, aggregate Core VID without a core number, GPU/non-CPU VID, and other non-CPU rails are rejected.
- Updated the built-in LibreHardwareMonitor VID provider to use the shared parser/filter.
- Enabled the `CPU Core VID` report tab and kept `CPU Voltage / Vcore` as its own tab.
- Added regression coverage for:
  - zero-based and one-based VID parsing;
  - same-value 8-core VID series preservation;
  - raw `SensorName` preservation;
  - VID-only runs not populating CPU Voltage / Vcore;
  - CPU Core VID tab not being disabled.

## Evidence Paths

- Synthetic VID/Vcore report: `artifacts\cpu-core-vid-20260531\synthetic-run\charts\framescope-interactive-report.html`
- Synthetic report data: `artifacts\cpu-core-vid-20260531\synthetic-run\charts\framescope-interactive-data.js`
- Synthetic VID CSV: `artifacts\cpu-core-vid-20260531\synthetic-run\cpu-vid-samples.csv`
- Synthetic Vcore CSV: `artifacts\cpu-core-vid-20260531\synthetic-run\cpu-voltage-samples.csv`
- Layout probe summary: `artifacts\cpu-core-vid-20260531\layout-probe\report-overflow-probe.json`
- Layout probe screenshots: `artifacts\cpu-core-vid-20260531\layout-probe\*.png`

## Verification

All required commands were run from:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

| Command | Result |
| --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS, `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS, typecheck passed, Vitest `57 passed`, Vite build passed |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS |
| bundled Node `.\tools\Probe-ReportHtmlLayout.js ...` | PASS, `allNoOverflow=true` |
| `git diff --check` | PASS, exit code 0; only existing LF-to-CRLF warnings |
| residual process check | PASS, `NO_MATCHING_RESIDUAL_PROCESSES` |

## Notes

- The working tree already contained many unrelated modified/untracked files before this round. I did not revert or delete them.
- The persistent layout artifact was generated synthetically only. No real game, BF6 run, installer, package, push, or release flow was executed.
