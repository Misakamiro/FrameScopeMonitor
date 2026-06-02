# FrameScope CPU Core VID Recording And Chart Retest

Date: 2026-05-31

Workspace:
`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Retest boundary: only retest and evidence/report generation. I did not modify source code, did not fix bugs, did not package, did not install FrameScope, did not launch a real game, did not test BF6, did not push GitHub, and did not update Release.

Final conclusion: PARTIAL

Reason: all required functional, chart, probe, formatting, and residual-process checks passed. The only partial item is scope audit against the raw current `git diff`: the worktree already contains broad pre-existing modifications across UI, monitoring, reporting, packaging, and tests, so the raw diff is not limited only to CPU Core VID/Vcore/FPS files. This retest did not add or change source files.

## Direct Answers

- CPU Voltage / Vcore chart retained: YES. Probe shows the independent `CPU Voltage / Vcore` tab/chart, and data inspection shows `DATA.cpuVoltage.series` contains `CPU Voltage / Vcore`.
- CPU Core VID exists as an extra independent chart: YES. Probe shows the independent `CPU Core VID` tab/chart, and data inspection shows VID is under `DATA.cpuVid`.
- `Core 0 VID` through `Core 7 VID` correctly record as 8 series: YES. Synthetic `cpu-vid-samples.csv` preserves `Core 0 VID` through `Core 7 VID`, and `framescope-interactive-data.js` contains 8 `DATA.cpuVid.series`.
- `Core #1 VID` through `Core #8 VID` correctly record as 8 series: YES. `FrameScopeReportManifestTests.exe` passed the one-based scenario, and source assertions cover `Core #1 VID` to `Core #8 VID` preserving 8 series and raw `SensorName`.
- Same-value VID is not merged: YES. Synthetic data has 8 cores all at `0.975 V`; `DATA.cpuVid.series.length` is still 8 and each series keeps its core name.
- VID does not enter CPU Voltage / Vcore: YES. VID-only report tests passed; source assertions require `cpuVoltage.available=false` and `cpuVoltage.series.Count=0` for VID-only runs.
- CPU Voltage / Vcore does not enter VID: YES. Vcore/SOC/package voltage rejection tests passed; source assertions require Vcore/SOC/package voltage not to create `cpuVid` series.
- FPS remains `bucketMs=1000` and raw PresentMon semantics are unchanged: YES. `DATA.fps.bucketMs=1000`, FPS keys remain `bucketMs`, `lowWindowMs`, `t`, `avg`, `low1`, `low01`, `samples`; `chart-sampling-tests.js` passed and continues to assert raw PresentMon/FPS display semantics.
- Source modifications in this retest: NO. This retest added only this report, layout probe evidence under `docs/test-reports`, and `residual-process-check.txt`. Existing source changes were already present in the dirty worktree.
- Packaging/install/real game/BF6/GitHub/Release actions: NO. No FrameScope install, no package build, no real game launch, no BF6 test, no GitHub push, no Release update.

## Scope Audit

Implementation report read:
`docs\implementation-reports\2026-05-31-framescope-cpu-core-vid-recording-chart-report.md`

The implementation report states the intended scope as CPU Core VID parsing/numbering, `cpu-vid-samples.csv`, `cpu-vid-telemetry-status.json`, `DATA.cpuVid`, an independent `CPU Core VID` report chart, retained `DATA.cpuVoltage`/`CPU Voltage / Vcore`, and related tests/probes.

Current raw `git diff` scope: PARTIAL. `git status --short`, `git diff --stat`, and `git diff --name-only` show many pre-existing modified/untracked files outside this VID-specific scope, including broad frontend UI, monitor session, diagnostics, packaging, config, and other tests. I treated those as existing worktree state and did not revert or edit them.

VID-specific and adjacent verified areas include:

- `src\monitoring\FrameScopeSystemSampler.CpuCoreTelemetry.cs`
- `src\monitoring\FrameScopeSystemSampler*.cs`
- `src\reporting\FrameScopeReportGenerator.SystemData.cs`
- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`
- `tests\FrameScopeSystemSamplerCpuCoreTests.cs`
- `tests\FrameScopeReportManifestTests.cs`
- `tests\chart-sampling-tests.js`
- `tools\Probe-ReportHtmlLayout.js`

## Data And Chart Evidence

Synthetic VID CSV:
`artifacts\cpu-core-vid-20260531\synthetic-run\cpu-vid-samples.csv`

Synthetic VID status:
`artifacts\cpu-core-vid-20260531\synthetic-run\cpu-vid-telemetry-status.json`

Synthetic report data:
`artifacts\cpu-core-vid-20260531\synthetic-run\charts\framescope-interactive-data.js`

Synthetic report HTML:
`artifacts\cpu-core-vid-20260531\synthetic-run\charts\framescope-interactive-report.html`

This run's layout probe summary:
`docs\test-reports\2026-05-31-framescope-cpu-core-vid-recording-chart-retest-evidence\layout-probe\report-overflow-probe.json`

CPU Core VID chart screenshot/probe:
`docs\test-reports\2026-05-31-framescope-cpu-core-vid-recording-chart-retest-evidence\layout-probe\cpu-core-vid-1280x720.png`

CPU Voltage / Vcore chart screenshot/probe:
`docs\test-reports\2026-05-31-framescope-cpu-core-vid-recording-chart-retest-evidence\layout-probe\cpu-voltage-1280x720.png`

FPS non-regression screenshot/probe:
`docs\test-reports\2026-05-31-framescope-cpu-core-vid-recording-chart-retest-evidence\layout-probe\fps-default-1280x720.png`

Residual process evidence:
`docs\test-reports\2026-05-31-framescope-cpu-core-vid-recording-chart-retest-evidence\residual-process-check.txt`

Key parsed evidence from `framescope-interactive-data.js`:

```json
{
  "cpuVidSeries": 8,
  "cpuVidNames": [
    "Core 0 VID",
    "Core 1 VID",
    "Core 2 VID",
    "Core 3 VID",
    "Core 4 VID",
    "Core 5 VID",
    "Core 6 VID",
    "Core 7 VID"
  ],
  "cpuVidValues": [0.975, 0.975, 0.975, 0.975, 0.975, 0.975, 0.975, 0.975],
  "cpuVoltageSeries": 1,
  "cpuVoltageNames": ["CPU Voltage / Vcore"],
  "fpsBucketMs": 1000
}
```

Key parsed evidence from layout probe:

```json
{
  "allNoOverflow": true,
  "cpuVoltageTitle": "CPU Voltage / Vcore",
  "cpuVoltageNote": "Overall CPU Voltage / Vcore in V, aligned with GamePP CPU Voltage. VID, SOC, Package, VBAT, and VIN are not used here.",
  "cpuVidTitle": "CPU Core VID",
  "cpuVidNote": "VID is CPU request/target voltage, not real Vcore.",
  "fpsTitle": "FPS GamePP chart",
  "fpsNote": "Bucketed FPS display keeps raw PresentMon statistics intact. Blue area, Min/Max/Average references, and sample-count tooltip remain unchanged."
}
```

## Verification Commands

| Command | Result |
| --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS: `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS: typecheck passed, Vitest `57 passed`, Vite build passed |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS: `chart-sampling-tests: PASS` |
| bundled Node `.\tools\Probe-ReportHtmlLayout.js --report ... --out docs\test-reports\...\layout-probe` | PASS: output JSON generated and `allNoOverflow=true` |
| `git diff --check` | PASS exit code 0; output contains only existing LF-to-CRLF warnings |
| residual process check | PASS: `NO_MATCHING_RESIDUAL_PROCESSES` |

## Filter Rule Coverage

- SOC VID, Package VID, aggregate VID, GPU VID, and non-CPU VID are covered by `FrameScopeSystemSamplerCpuCoreTests.exe` and `FrameScopeReportManifestTests.exe`.
- VID rows are rejected by CPU Voltage / Vcore chart tests; `DATA.cpuVoltage` is not populated by VID-only runs.
- Vcore/SOC/package voltage rows are rejected by CPU Core VID chart tests; `DATA.cpuVid` is not populated by real Vcore rows.
- `SensorName` is preserved in the generated VID series, allowing direct comparison with names such as `Core 0 VID`.

## Notes

- `tools\Run-Frontend.ps1 verify` printed `added 110 packages in 3s`; this was npm dependency setup inside the required frontend verification script, not a FrameScope install and not packaging/release work.
- No source fix was attempted after any check. There were no functional failures to fix.
