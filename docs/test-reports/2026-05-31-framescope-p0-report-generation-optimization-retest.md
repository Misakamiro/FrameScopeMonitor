# FrameScope P0 Large History Report Generation Optimization Retest

Date: 2026-05-31

Retest type: P0 report generation optimization only. No source fix was made in this retest.

Final conclusion: PARTIAL.

Reason: the P0 performance/correctness/layout result is reproducible and passes, but the current tracked `git diff` is not P0-only. The worktree already contains broad non-P0 source changes outside report generation, so the requested "current git diff only around P0" scope check cannot be fully confirmed.

## Direct Answers

| Question | Answer |
| --- | --- |
| 1. P0 performance optimization reproducible? | Yes. P0 after performance is clearly reproduced and remains well ahead of the baseline. |
| 2. after elapsed / CPU / working set / private memory | Run 1: 4888 ms / 4812.5 ms / 200.97 MB / 208.54 MB. Run 2: 4956 ms / 4890.625 ms / 198.70 MB / 207.25 MB. |
| 3. Compared with baseline | Best elapsed improved 36.63%; best CPU reduced 37.14%; best working set reduced 64.12%; best private memory reduced 62.39%. |
| 4. `data.js` size abnormal? | No. Both retest runs produced `framescope-interactive-data.js` = 1,266,013 bytes. |
| 5. `allMatch` / `frameStatsMatchRaw` | `allMatch=true`; `frameStatsMatchRaw=true`. |
| 6. FPS raw statistic semantics preserved? | Yes. Raw PresentMon recomputation matched report output exactly. |
| 7. `bucketMs=1000` preserved? | Yes. `bucketMs=1000`, `lowWindowMs=2000`. |
| 8. CPU Voltage / Vcore and CPU Core VID still separate? | Yes. `DATA.cpuVoltage` and `DATA.cpuVid` remain separate; VID/Vcore checks passed. In this large artifact both are unavailable with separate empty series. |
| 9. Layout probe `allNoOverflow=true`? | Yes. 23 scenarios, 0 overflow. Key chart screenshots passed nonblank pixel sampling. |
| 10. Source modified by this retest? | No. This retest added only this report and evidence under `docs/test-reports`. Existing source modifications were already present. |
| 11. P1/P2 handled? | No. This retest did not handle frontend animation optimization, backend monitoring overhead, log performance, or process-tab interaction optimization. |
| 12. Packaging/install/game/BF6/GitHub/Release actions? | Not performed. No installer/package build, no FrameScope install, no real game launch, no BF6 test, no GitHub push, no Release update. |
| 13. Verification command results | Listed below. |
| 14. Final result | PARTIAL overall because current `git diff` is broader than P0; P0 reproducibility itself is PASS. |

## Dataset

Dataset used:

`artifacts\poa31\history-large`

Input scale from implementation report:

| File | Size |
| --- | ---: |
| `presentmon.csv` | 261,232,024 bytes |
| `process-samples.csv` | 165,503,651 bytes |
| `system-samples.csv` | 317,142 bytes |

Generator used for retest:

`artifacts\p0-report-generation-optimization-20260531\bin\FrameScopeReportGenerator.exe`

Evidence directory:

`docs\test-reports\2026-05-31-framescope-p0-report-generation-optimization-retest-evidence`

## Scope / Diff Check

Implementation report says the P0 optimization itself touched the report generation path:

| Area | Relevant files from implementation report |
| --- | --- |
| PresentMon read | `src\reporting\FrameScopeReportGenerator.PresentMon.cs` |
| report analysis / FPS stats | `src\reporting\FrameScopeReportGenerator.Analysis.cs`, `src\reporting\FrameScopeReportGenerator.cs` |
| CSV read | `src\reporting\FrameScopeReportGenerator.Csv.cs` |
| process data | `src\reporting\FrameScopeReportGenerator.ProcessData.cs` |
| report models/generator | `src\reporting\FrameScopeReportGenerator.Models.cs` |
| tests | `tests\FrameScopeReportManifestTests.cs` |

Current tracked `git diff` is broader than that. `git status --short` and `git diff --name-only` show existing modified/deleted tracked files in non-P0 areas, including:

- `build.ps1`
- `packaging\FrameScopeSetupNative.cs`
- `src\app\*`
- `src\core\FrameScopeConfigStore.cs`
- `src\diagnostics\FrameScopeDiagnostics.Sections.cs`
- `src\frontend\src\*`
- `src\monitoring\*`
- `tests\FrameScopeConfigStoreTests.cs`, `tests\FrameScopeDiagnosticsTests.cs`, `tests\FrameScopeWebBridgeTests.cs`, `tests\chart-sampling-tests.js`
- deleted `tools\WebView2Spike\*`

Therefore, I cannot honestly confirm that the current worktree diff is only P0. I did not modify or revert those files in this retest.

## Performance Retest

Command type: two fresh runs of the optimized report generator against the same `history-large` run directory, with elapsed time, process CPU time, peak working set, peak private memory, and output sizes recorded.

Evidence:

`docs\test-reports\2026-05-31-framescope-p0-report-generation-optimization-retest-evidence\history-large-after-retest-runs.json`

| Run | Exit | Elapsed ms | CPU ms | Peak WS MB | Peak private MB | `data.js` bytes | Frames | Raw rows | Valid rows | Process samples | Processes | System samples | Report kind | Capture status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| after retest 1 | 0 | 4,888 | 4,812.5 | 200.97 | 208.54 | 1,266,013 | 876,585 | 876,603 | 876,593 | 17,714 | 119 | 1,933 | full | captured |
| after retest 2 | 0 | 4,956 | 4,890.625 | 198.70 | 207.25 | 1,266,013 | 876,585 | 876,603 | 876,593 | 17,714 | 119 | 1,933 | full | captured |

Average after retest:

| Metric | Average |
| --- | ---: |
| Elapsed | 4,922 ms |
| CPU | 4,851.563 ms |
| Peak working set | 199.835 MB |
| Peak private memory | 207.895 MB |
| `data.js` size | 1,266,013 bytes |

Baseline from implementation report:

| Metric | Baseline best | Baseline avg |
| --- | ---: | ---: |
| Elapsed | 7,713 ms | 7,764.5 ms |
| CPU | 7,656.25 ms | 7,664.063 ms |
| Peak working set | 553.80 MB | 554.045 MB |
| Peak private memory | 551.02 MB | 551.265 MB |
| `data.js` size | 1,266,013 bytes | 1,266,013 bytes |

Comparison:

| Metric | Baseline best | Retest best | Best improvement | Baseline avg | Retest avg | Avg improvement |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Elapsed | 7,713 ms | 4,888 ms | 36.63% faster | 7,764.5 ms | 4,922 ms | 36.61% faster |
| CPU | 7,656.25 ms | 4,812.5 ms | 37.14% lower | 7,664.063 ms | 4,851.563 ms | 36.70% lower |
| Peak working set | 553.80 MB | 198.70 MB | 64.12% lower | 554.045 MB | 199.835 MB | 63.93% lower |
| Peak private memory | 551.02 MB | 207.25 MB | 62.39% lower | 551.265 MB | 207.895 MB | 62.29% lower |
| `data.js` size | 1,266,013 bytes | 1,266,013 bytes | unchanged | 1,266,013 bytes | 1,266,013 bytes | unchanged |

Variation note: retest elapsed varied by 68 ms between two runs, and peak memory varied by about 2.27 MB working set / 1.29 MB private memory. This is small and consistent with OS scheduling, filesystem cache state, and sampling interval granularity. No run approached the baseline.

## Correctness Retest

Evidence:

- `docs\test-reports\2026-05-31-framescope-p0-report-generation-optimization-retest-evidence\retest-key-data-compare.json`
- `docs\test-reports\2026-05-31-framescope-p0-report-generation-optimization-retest-evidence\retest-frame-stats-raw-compare.json`

Result:

| Check | Result |
| --- | --- |
| Compare current retest `data.js` with implementation after-final reference | `allMatch=true` |
| Raw PresentMon recomputation vs report frame stats | `frameStatsMatchRaw=true` |
| Mismatches | 0 |
| Raw mismatches | 0 |
| `fps.bucketMs` | 1000 |
| `fps.lowWindowMs` | 2000 |
| `frames` | 876,585 |
| `presentMon.rawRows` | 876,603 |
| `presentMon.validRows` | 876,593 |
| `presentMon.selectedRows` | 876,585 |
| `frameStats.average` | 587.96 |
| `frameStats.low1` | 164.57 |
| `frameStats.low01` | 36.6 |
| `frameStats.minInstant` | 1.104 |
| `frameStats.maxInstant` | 3544.842 |
| `frameStats.maxFrameMs` | 905.976 |
| `frameStats.framesOver20` | 129 |
| `frameStats.framesOver33` | 89 |
| `frameStats.framesOver100` | 48 |

Raw PresentMon semantics verified:

- `rawRows` counts all CSV data rows.
- `validRows` requires parseable `TimeInDateTime` and `0 < MsBetweenPresents < 10000`.
- `selectedRows` uses the selected PresentMon track.
- For multi-track runs, hardware mode is preferred when present.
- Resume artifacts over 1000 ms are dropped from selected FPS stats.

Data structure checks:

| Structure | Result |
| --- | --- |
| `brand`, `run`, `target`, `hardware`, `counts` | present |
| `presentMon`, `frameStats`, `fps` | present |
| `system`, `cpuCore`, `cpuVoltage`, `cpuVid`, `process` | present |
| `capture`, `notes` | present |
| process codec | `rle-v1` |
| process names | 119 |
| process time samples | 17,714 |

VID/Vcore isolation:

| Check | Result |
| --- | --- |
| CPU Voltage tab exists | PASS |
| CPU Core VID tab exists | PASS |
| CPU Voltage / Vcore chart reads `DATA.cpuVoltage` | PASS |
| CPU Core VID chart reads `DATA.cpuVid` | PASS |
| CPU VID keeps separate metric state | PASS |
| `cpuVoltage.available` | false |
| `cpuVoltage.series.length` | 0 |
| `cpuVid.available` | false |
| `cpuVid.series.length` | 0 |

The artifact does not contain voltage/VID samples, so both chart families are unavailable in data, but they remain structurally separated and are not mixed.

## Layout Retest

Command:

`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tools\Probe-ReportHtmlLayout.js --report .\artifacts\poa31\history-large\charts\framescope-interactive-report.html --diagnostic .\artifacts\poa31\history-large\charts\framescope-interactive-report.html --out .\docs\test-reports\2026-05-31-framescope-p0-report-generation-optimization-retest-evidence\layout-probe`

Evidence:

- `docs\test-reports\2026-05-31-framescope-p0-report-generation-optimization-retest-evidence\layout-probe\report-overflow-probe.json`
- `docs\test-reports\2026-05-31-framescope-p0-report-generation-optimization-retest-evidence\layout-chart-nonblank-check.json`

Result:

| Check | Result |
| --- | --- |
| Scenario count | 23 |
| Overflow count | 0 |
| `allNoOverflow` | true |
| Key charts nonblank | true |

Nonblank chart pixel sampling:

| Scenario | Unique sampled colors | PNG bytes | Result |
| --- | ---: | ---: | --- |
| FPS | 944 | 427,975 | PASS |
| CPU Voltage / Vcore | 59 | 253,021 | PASS |
| CPU Core VID | 55 | 250,561 | PASS |
| GPU / performance chart | 1,021 | 365,545 | PASS |
| System usage | 1,238 | 478,010 | PASS |
| IO disk/net | 386 | 294,296 | PASS |
| Temperature | 657 | 418,717 | PASS |
| Background process | 396 | 295,944 | PASS |

## Required Commands

| Command | Result |
| --- | --- |
| `git status --short` | Ran before/after. Worktree has many pre-existing modified/untracked files; this retest added `docs\test-reports\2026-05-31-framescope-p0-report-generation-optimization-retest-evidence\` and this report. |
| P0 large history after performance retest, at least 2 runs | PASS. Run 1 exit 0, run 2 exit 0. Metrics recorded above. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS. Script added 110 packages as normal verify behavior; typecheck PASS; Vitest 5 files / 57 tests PASS; Vite build PASS. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS. `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS. `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS. `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS. `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS. `chart-sampling-tests: PASS`. |
| Bundled Node `tools\Probe-ReportHtmlLayout.js` | PASS. Output `report-overflow-probe.json`; `allNoOverflow=true`. |
| `git diff --check` | PASS exit 0. Output only existing LF-to-CRLF warnings, no whitespace errors. |
| Residual process check | PASS. `NO_MATCHING_RESIDUAL_PROCESSES`. |

## Boundaries Confirmed

| Boundary | Result |
| --- | --- |
| No source fixes in this retest | Confirmed |
| No P1/P2 handling in this retest | Confirmed |
| No frontend animation optimization | Confirmed |
| No backend monitoring overhead optimization | Confirmed |
| No log performance optimization | Confirmed |
| No large report process-tab interaction optimization | Confirmed |
| No `build.ps1` | Confirmed |
| No installer/package build | Confirmed |
| No FrameScope install | Confirmed |
| No real game launch | Confirmed |
| No BF6 test | Confirmed |
| No GitHub push | Confirmed |
| No Release update | Confirmed |

`Run-Frontend.ps1 verify` regenerated frontend verification artifacts and `dist` as part of its normal verify behavior. This is not counted as product packaging or installer generation.

## Final Result

P0 reproducibility: PASS.

Overall requested checklist: PARTIAL.

The optimized large historical report generation path remains reproducible: elapsed and CPU are about 36-37% better than baseline, peak memory remains about 62-64% lower, `data.js` size is unchanged, raw PresentMon frame statistics match, `bucketMs=1000` is preserved, VID/Vcore remain isolated, and layout probe passes. The only blocker to a full PASS is scope isolation of the current worktree: the current tracked diff contains broad pre-existing non-P0 changes, so the current diff cannot be truthfully confirmed as P0-only.
