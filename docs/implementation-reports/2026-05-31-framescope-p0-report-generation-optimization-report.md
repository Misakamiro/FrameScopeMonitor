# FrameScope P0 Report Generation Optimization Report

Date: 2026-05-31

Conclusion: PASS

## Scope

This round only handled the P0 large historical report generation performance issue. P1/P2 items were not handled.

Hard boundaries followed:

| Item | Result |
| --- | --- |
| Packaging | Not run |
| FrameScope install | Not run |
| Real game launch | Not run |
| BF6 testing | Not run |
| GitHub push | Not run |
| Release update | Not run |
| `build.ps1` | Not run |
| FPS statistic semantics | Preserved |
| FPS `bucketMs=1000` | Preserved |
| raw PresentMon source semantics | Preserved |
| CPU Voltage / Vcore vs CPU Core VID | Still separated |

## Dataset

Large history artifact:

`artifacts\poa31\history-large`

Input scale:

| File | Size |
| --- | ---: |
| `presentmon.csv` | 261,232,024 bytes |
| `process-samples.csv` | 165,503,651 bytes |
| `system-samples.csv` | 317,142 bytes |

`PathTooLongException` was not reproduced on this workspace path, so no short-path copy was required.

## Baseline

Baseline was measured with the pre-optimization root `FrameScopeReportGenerator.exe` on the same `history-large` dataset. Valid baseline runs are runs 3 and 4.

Evidence:

`artifacts\p0-report-generation-optimization-20260531\before\history-large-before-runs-3-4.json`

| Run | Exit | Elapsed ms | CPU ms | Peak WS MB | Peak private MB | `data.js` bytes | Frames | raw rows | valid rows | Process samples | Processes | System samples | Report kind | Capture status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| before 3 | 0 | 7,713 | 7,656.25 | 554.29 | 551.51 | 1,266,013 | 876,585 | 876,603 | 876,593 | 17,714 | 119 | 1,933 | full | captured |
| before 4 | 0 | 7,816 | 7,671.875 | 553.80 | 551.02 | 1,266,013 | 876,585 | 876,603 | 876,593 | 17,714 | 119 | 1,933 | full | captured |

Baseline average:

| Metric | Average |
| --- | ---: |
| Elapsed | 7,764.5 ms |
| CPU | 7,664.063 ms |
| Peak working set | 554.045 MB |
| Peak private memory | 551.265 MB |
| `data.js` size | 1,266,013 bytes |

Manifest/summary key fields were stable in baseline: `reportKind=full`, `frameCaptureStatus=captured`, `frames=876585`, `rawPresentMonRows=876603`, `validPresentMonRows=876593`, `processSamples=17714`, `processes=119`, `systemSamples=1933`, `cpuVoltageAvailable=false`, `cpuVidAvailable=false`.

## Hotspots Found

1. PresentMon CSV reading was the dominant memory hotspot. The old path parsed the full CSV row and kept a `PresentRecord` object per valid row with repeated strings for application, process id, swap chain, present mode, and tearing state.
2. FPS bucket generation allocated duplicate collections: bucket lists, seconds arrays, frame-ms arrays, plus later sorting for low and summary stats.
3. Process matrix generation parsed every column in the 165 MB `process-samples.csv` even though report generation only needed a small fixed column set.
4. Process RLE padding appended repeated null tokens one at a time, which increased string-builder churn on sparse process series.
5. `framescope-interactive-data.js`, JSON/JS serialization, manifest writing, and summary writing were not the main P0 bottleneck for this dataset. Output size stayed unchanged after optimization.
6. CPU/GPU/IO/temperature/voltage/VID system telemetry was not the P0 cost center for this dataset. `system-samples.csv` was small and CPU Voltage / CPU Core VID were unavailable in this artifact, so those semantics were left unchanged.

## Files Changed

P0-related source/test changes:

| File | Change |
| --- | --- |
| `src\reporting\FrameScopeReportGenerator.PresentMon.cs` | Streamed PresentMon track aggregation directly while reading selected CSV columns; kept selected-track diagnostics; added `ReadPresentMonForTests`. |
| `src\reporting\FrameScopeReportGenerator.Analysis.cs` | Reworked FPS bucket and frame-stat calculation to avoid duplicate per-frame arrays and repeated sorting. |
| `src\reporting\FrameScopeReportGenerator.cs` | Switched report summary stats to the new one-pass `CalculateFrameStats` result. |
| `src\reporting\FrameScopeReportGenerator.Csv.cs` | Added selected-field CSV reading with quoted-line fallback to the existing full parser. |
| `src\reporting\FrameScopeReportGenerator.ProcessData.cs` | Read only required process sample columns when building the process matrix. |
| `src\reporting\FrameScopeReportGenerator.Models.cs` | Added compact `PresentFrame`, `FrameStatsSummary`, and `FpsBucketAccumulator`; optimized `ProcessRleSeriesBuilder.PadTo`. |
| `tests\FrameScopeReportManifestTests.cs` | Added regression coverage for primary hardware track selection and raw diagnostics. |

Evidence/artifact additions:

| Path | Purpose |
| --- | --- |
| `artifacts\p0-report-generation-optimization-20260531\before` | Baseline run data and copied report outputs. |
| `artifacts\p0-report-generation-optimization-20260531\after-final` | Optimized run data, copied report outputs, raw/stat comparisons. |
| `artifacts\p0-report-generation-optimization-20260531\bin\FrameScopeReportGenerator.exe` | Test-only optimized generator used for after measurements; root executable was not overwritten. |
| `artifacts\p0-report-generation-optimization-20260531\layout-probe` | Layout probe JSON and screenshots. |

The worktree had many pre-existing unrelated modified/untracked files. They were not reverted and were not part of this P0 change.

## Safety Notes

| Optimization | Why it is safe |
| --- | --- |
| Selected CSV field reading | Column indexes still come from the CSV header names. Lines containing quotes fall back to the existing full parser. Missing columns still produce empty field values instead of changing call sites to positional assumptions. |
| Compact PresentMon track frames | The report does not expose per-row repeated string objects. Track identity and diagnostics still use the same process id, swap chain, application, present mode, and tearing inputs. raw row, valid row, selected row, and frame stats were compared against the previous output and raw CSV. |
| FPS bucket accumulator | `bucketMs` remains 1000 and `lowWindowMs` remains 2000. Average FPS still comes from raw frame times in the same bucket. 1% Low and 0.1% Low still use the raw frame stream through the same rolling-window/Fenwick approach. |
| Single frame-stat sort | Average, min, max, frames-over thresholds, 1% Low, and 0.1% Low are computed from the same selected raw frame milliseconds. The implementation sorts once instead of repeatedly sorting/enumerating. |
| Process selected-column read | The process matrix uses the same required columns: `ProcessName`, `Time`, `SampleIndex`, `CpuPct`, and `WorkingSetMB`. The output process codec and counts remained identical. |
| RLE bulk padding | The encoded `rle-v1` semantics are unchanged. The change only appends repeated `n` runs in one operation instead of repeatedly appending single null samples. |

## After Results

After measurements used the optimized artifact executable:

`artifacts\p0-report-generation-optimization-20260531\bin\FrameScopeReportGenerator.exe`

Evidence:

`artifacts\p0-report-generation-optimization-20260531\after-final\history-large-after-final-runs.json`

| Run | Exit | Elapsed ms | CPU ms | Peak WS MB | Peak private MB | `data.js` bytes | Frames | raw rows | valid rows | Process samples | Processes | System samples | Report kind | Capture status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| after 1 | 0 | 5,081 | 5,062.5 | 198.71 | 207.27 | 1,266,013 | 876,585 | 876,603 | 876,593 | 17,714 | 119 | 1,933 | full | captured |
| after 2 | 0 | 5,010 | 4,953.125 | 199.66 | 208.18 | 1,266,013 | 876,585 | 876,603 | 876,593 | 17,714 | 119 | 1,933 | full | captured |

After average:

| Metric | Average |
| --- | ---: |
| Elapsed | 5,045.5 ms |
| CPU | 5,007.813 ms |
| Peak working set | 199.185 MB |
| Peak private memory | 207.725 MB |
| `data.js` size | 1,266,013 bytes |

## Before / After Comparison

Primary comparison uses best-of-two because the user requested at least two runs and best run is less affected by transient OS scheduling. Average is also reported.

| Metric | Before best | After best | Best improvement | Before avg | After avg | Avg improvement |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Elapsed ms | 7,713 | 5,010 | 35.04% faster | 7,764.5 | 5,045.5 | 35.02% faster |
| CPU ms | 7,656.25 | 4,953.125 | 35.31% lower | 7,664.063 | 5,007.813 | 34.66% lower |
| Peak working set | 553.80 MB | 198.71 MB | 64.12% lower | 554.045 MB | 199.185 MB | 64.05% lower |
| Private memory | 551.02 MB | 207.27 MB | 62.38% lower | 551.265 MB | 207.725 MB | 62.32% lower |
| `data.js` size | 1,266,013 bytes | 1,266,013 bytes | unchanged | 1,266,013 bytes | 1,266,013 bytes | unchanged |
| Report output correctness | baseline reference | `allMatch=true`, `frameStatsMatchRaw=true` | no semantic delta | baseline reference | same | no semantic delta |

## Correctness Lock

Before/after key data comparison:

`artifacts\p0-report-generation-optimization-20260531\after-final\before-after-key-data-compare.json`

Result: `allMatch=true`, `mismatches=[]`.

Matched fields:

| Field | Before | After |
| --- | ---: | ---: |
| `fps.bucketMs` | 1000 | 1000 |
| `fps.lowWindowMs` | 2000 | 2000 |
| frames | 876,585 | 876,585 |
| average FPS | 587.96 | 587.96 |
| 1% Low | 164.57 | 164.57 |
| 0.1% Low | 36.6 | 36.6 |
| min instant FPS | 1.104 | 1.104 |
| max instant FPS | 3544.842 | 3544.842 |
| process codec | `rle-v1` | `rle-v1` |
| process names | 119 | 119 |
| process times | 17,714 | 17,714 |
| CPU Voltage available | false | false |
| CPU Voltage series | 0 | 0 |
| CPU Core VID available | false | false |
| CPU Core VID series | 0 | 0 |
| PresentMon raw rows | 876,603 | 876,603 |
| PresentMon valid rows | 876,593 | 876,593 |
| PresentMon selected rows | 876,585 | 876,585 |

Raw PresentMon recomputation:

`artifacts\p0-report-generation-optimization-20260531\after-final\frame-stats-raw-compare.json`

Result: `frameStatsMatchRaw=true`, `mismatches=[]`.

Raw recomputed frame stats matched the report exactly:

| Field | Value |
| --- | ---: |
| raw rows | 876,603 |
| valid rows | 876,593 |
| out-of-range rows | 10 |
| selected rows | 876,585 |
| average FPS | 587.96 |
| 1% Low | 164.57 |
| 0.1% Low | 36.6 |
| min instant FPS | 1.104 |
| max instant FPS | 3544.842 |
| max frame ms | 905.976 |
| frames over 20 ms | 129 |
| frames over 33.3 ms | 89 |
| frames over 100 ms | 48 |

Explicit semantic statements:

| Requirement | Status |
| --- | --- |
| FPS chart remains GamePP style | Preserved; layout probe still reports `viewTitle="FPS GamePP chart"`. |
| `bucketMs=1000` | Preserved; before and after both 1000. |
| raw PresentMon statistics | Preserved; raw recomputation matched report output exactly. |
| Average / 1% Low / 0.1% Low / Min / Max | Preserved; before/after and raw comparison all match. |
| CPU Voltage / Vcore uses `DATA.cpuVoltage` | Preserved; no mixing introduced. |
| CPU Core VID uses `DATA.cpuVid` | Preserved; no mixing introduced. |
| VID/Vcore isolation | Preserved; both availability and series counts stayed separated. |
| Process chart data | Preserved; `rle-v1`, process count, and process time count all match. |

Note: PowerShell `ConvertFrom-Json` is not reliable on the generated JS/JSON artifacts that include localized text in this workspace. The correctness comparisons used bundled Node to evaluate/parse the same browser data path, which matches how the report HTML consumes `window.FRAMESCOPE_DATA`.

## Verification

Fresh verification commands run during this round:

| Command | Result |
| --- | --- |
| Baseline large history generation, before runs 3 and 4 | exit 0 both; data recorded in `history-large-before-runs-3-4.json`. |
| Optimized large history generation, after runs 1 and 2 | exit 0 both; data recorded in `history-large-after-final-runs.json`. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | exit 0; verify script added 110 packages as normal behavior; typecheck PASS; Vitest 5 files / 57 tests PASS; Vite build PASS in 1.66s. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | exit 0; `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | exit 0; `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | exit 0; `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | exit 0; `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| Bundled Node `.\tests\chart-sampling-tests.js` | exit 0; `chart-sampling-tests: PASS`. |
| Bundled Node `tools\Probe-ReportHtmlLayout.js --report artifacts\poa31\history-large\charts\framescope-interactive-report.html --diagnostic artifacts\poa31\history-large\charts\framescope-interactive-report.html --out artifacts\p0-report-generation-optimization-20260531\layout-probe` | exit 0; output `report-overflow-probe.json`; Node summary `allNoOverflow=true`, `scenarioCount=23`, `overflowCount=0`. |
| Bundled Node before/after key data comparison | `allMatch=true`, `mismatches=[]`. |
| Bundled Node raw PresentMon frame stats comparison | `frameStatsMatchRaw=true`, `mismatches=[]`. |
| `git diff --check` | exit 0 after report creation; output contains existing LF-to-CRLF warnings only, no whitespace errors. |
| Residual process check | `NO_MATCHING_RESIDUAL_PROCESSES`. |

Post-report final checks:

| Command | Result |
| --- | --- |
| `git diff --check` | exit 0; existing LF-to-CRLF warnings only. |
| Residual process check | `NO_MATCHING_RESIDUAL_PROCESSES`. |

## Final Result

PASS.

The P0 large historical report generation path improved by about 35% elapsed time and reduced peak memory by more than 60% on the 876,585-frame history dataset. Report output data size and key semantics stayed unchanged, raw PresentMon statistics matched the regenerated report, `bucketMs=1000` stayed fixed, CPU Voltage / Vcore and CPU Core VID remained isolated, and no P1/P2 work or prohibited packaging/install/game/BF6/GitHub/Release actions were performed.
