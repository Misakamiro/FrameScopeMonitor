# FrameScope performance baseline diagnosis

Date run: 2026-05-25 01:03-01:23 Asia/Hong_Kong
Report filename follows the requested 2026-05-24 diagnostics name.
Source root: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Verdict

PARTIAL.

Status scale used in this report: PASS means the requirement was measured and satisfied; PARTIAL means the requirement was measured but has an environment or coverage limit; FAIL means the requirement was attempted and contradicted by evidence. Current overall status is PARTIAL, with no requirement classified as FAIL.

The pre-optimization baseline is usable for UI idle/workload cost, synthetic monitor-session cost, sampler interval cost, and report-generation cost. It is not a full PASS because real `PresentMon-2.4.1-x64.exe` did not produce `presentmon.csv` against the synthetic target in this environment, even though it exited `0` after "Started recording" / "Stopped recording" and stderr reported `warning: 51927 ETW events were lost.` No BF6 or real game was launched.

No source code was changed, no build was run, no package was created, and nothing was pushed to GitHub. The only source-tree artifact written by this diagnosis is this markdown report.

## Test Method

Temporary runtime root:

`C:\Users\misakamiro\AppData\Local\Temp\framescope-perf-baseline-20260525-010341`

Evidence JSON and screenshots:

`C:\Users\misakamiro\AppData\Local\Temp\framescope-perf-baseline-20260525-010341\evidence`

The temporary root copied existing built binaries and existing `src\frontend\dist`. It also used a copied synthetic run and a copied existing Valorant run for large-report generation. This kept smoke evidence, screenshots, config writes, generated reports, and test runs out of the source tree.

CPU numbers are process delta CPU as single-core percent. For example, `100%` means roughly one CPU core fully occupied during the sample window.

## Baseline Data

### 1. WebView2 React UI idle

Command shape: existing `FrameScopeMonitor.exe` launched from the temporary root with the React frontend available, then sampled for about 30 seconds.

| Process | Avg CPU | P95 CPU | Max CPU | Max Working Set | Max Private |
|---|---:|---:|---:|---:|---:|
| `FrameScopeMonitor.exe` | 0.00% | 0.00% | 0.00% | 45.85 MB | 26.31 MB |
| `msedgewebview2.exe` | 0.02% | 0.00% | 1.00% | 128.63 MB | 114.89 MB |
| Total tree | 0.02% | n/a | 1.00% | 174.48 MB | 141.20 MB |

Result: PASS for idle. The React/WebView2 shell is effectively idle after load.

### 2. Frontend workload

Command shape: existing `FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion` from the temporary root. This covered page transitions, Reports list, Targets process lookup, Settings config save, report regenerate, open report, open directory, diagnostics request, and monitor start/stop bridge actions.

Smoke success: `true`
Smoke elapsed: `5407 ms`

| Process | Avg CPU | P95 CPU | Max CPU | Max Working Set | Max Private | Avg Read | Avg Write |
|---|---:|---:|---:|---:|---:|---:|---:|
| `FrameScopeMonitor.exe` | 11.00% | 32.00% | 32.00% | 74.71 MB | 38.28 MB | 398.42 KB/s | 274.46 KB/s |
| `msedgewebview2.exe` | 3.27% | 20.00% | 22.00% | 133.41 MB | 229.60 MB | 28.98 KB/s | 130.41 KB/s |
| `msedge.exe` opened by report-open smoke | 0.00% | 0.00% | 0.00% | 184.71 MB | 74.59 MB | 0.00 KB/s | 0.00 KB/s |
| Total tree | 14.27% | n/a | 54.00% | 392.83 MB | 342.47 MB | n/a | n/a |

Result: PASS for covered frontend actions, with one cleanup finding. The smoke action opened Edge for report viewing and cleaned those Edge processes. It also left one temporary-root watcher after monitor start/stop smoke; I stopped that temp watcher manually and final residual check was clean.

### 3. Monitor session process cost

Synthetic target: existing `TslGame.exe` from prior simulator artifacts.
Fake PresentMon: existing `FakePresentMon.exe` from prior simulator artifacts.
Capture length: 6 seconds.
No BF6 and no real game were launched.

| PresentMon | Process Interval | Frame Status | PresentMon Rows | Avg Total CPU | Peak Working Set |
|---|---:|---|---:|---:|---:|
| FakePresentMon | 100 ms | captured | 360 | 4.77% | 115.61 MB |
| FakePresentMon | 250 ms | captured | 360 | 2.49% | 114.36 MB |
| FakePresentMon | 500 ms | captured | 360 | 2.20% | 113.02 MB |
| FakePresentMon | 1000 ms | captured | 360 | 2.05% | 112.74 MB |
| Real PresentMon | 100 ms | no-presentmon-csv | 0 | 5.44% | 113.43 MB |

100ms synthetic per-process breakdown:

| Process | Avg CPU | P95 CPU | Max CPU | Max Working Set | Max Private | Avg Write |
|---|---:|---:|---:|---:|---:|---:|
| `FrameScopeProcessSampler.exe` | 3.57% | 12.00% | 12.00% | 28.61 MB | 50.21 MB | 78.20 KB/s |
| `FrameScopeSystemSampler.exe` | 0.93% | 2.00% | 2.00% | 38.11 MB | 39.91 MB | 0.13 KB/s |
| `FakePresentMon.exe` | 0.13% | 2.00% | 2.00% | 13.67 MB | 9.09 MB | 0.00 KB/s |
| `FrameScopeMonitor.exe` | 0.13% | 2.00% | 2.00% | 28.39 MB | 23.78 MB | 1.21 KB/s |

Result: PASS for synthetic monitor chain. PARTIAL for real PresentMon in this environment because real PresentMon did not write CSV for the synthetic target.

### 4. Sampler interval cost

CSV output over the 6 second synthetic capture:

| Process interval | `process-samples.csv` rows | `process-samples.csv` bytes | `topcpu-samples.csv` rows | `topio-samples.csv` rows | ProcessSampler avg CPU |
|---:|---:|---:|---:|---:|---:|
| 100 ms | 9011 | 774,785 | 1961 | 497 | 3.57% |
| 250 ms | 3918 | 336,803 | 841 | 237 | 1.29% |
| 500 ms | 1913 | 163,854 | 401 | 140 | 1.00% |
| 1000 ms | 1002 | 85,560 | 201 | 90 | 0.71% |

Result: PASS. Process sampler cost and file volume scale strongly with sample interval. 100ms is the main runtime overhead knob.

### 5. Report generation

Small synthetic run:

| Input | Value |
|---|---:|
| Process rows | about 9011 rows |
| Report generator elapsed | 250.89 ms |
| Output HTML | 40,277 bytes |
| Output data JS | 80,441 bytes |

Large existing non-BF6 run copied to temp:

Source: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260510-151512`

| Metric | Value |
|---|---:|
| `presentmon.csv` input | 261,232,024 bytes |
| `process-samples.csv` input | 165,503,651 bytes |
| `system-samples.csv` input | 317,142 bytes |
| Frames in manifest | 876,585 |
| Raw PresentMon rows | 876,603 |
| Processes | 119 |
| Process samples | 17,714 |
| System samples | 1,933 |
| Generator elapsed | 8418.10 ms |
| Generator avg CPU | 99.12% |
| Generator P95 CPU | 115.00% |
| Generator max CPU | 116.00% |
| Generator max working set | 566.88 MB |
| Generator max private memory | 569.06 MB |
| Avg read throughput | 69,560.31 KB/s |
| Max read throughput | 119,832.00 KB/s |
| Output `framescope-interactive-data.js` | 16,777,674 bytes |
| Output HTML | 40,277 bytes |

Result: PASS for measurement, but this is the biggest proven bottleneck.

## Bottleneck Ranking

1. Report generation on large runs: about 8.4s, about 569 MB private memory, and roughly one full CPU core for a 427 MB input run. The generator reads full CSV inputs and serializes a large `framescope-interactive-data.js` in one pass.
2. `FrameScopeProcessSampler.exe` at 100ms: about 3.57% average single-core CPU by itself, p95 12%, and about 78 KB/s writes in the short synthetic run. It performs full `Process.GetProcesses()` enumeration each loop and writes grouped process CSV plus top CPU/IO CSV.
3. Frontend workload during bridge actions: smoke total averaged 14.27% with peaks up to 54%, but this includes report regeneration, report-open browser launch, screenshots, and bridge action churn. Idle UI is not the problem.
4. Reports list scanning: `reports.list` reads up to 100 history lines, searches `dataRoot` recursively for report HTML, takes 50, then validates files. This is acceptable on current temp data but can become visible when dataRoot grows large.
5. WebView2 idle: not a bottleneck. Idle total average was about 0.02% CPU.

## Polling, Rendering, JSON, DOM, and Process Release

### Polling

No unnecessary React-side constant polling was found. `src\frontend\src\state\useFrameScopeBridgeState.ts` performs initial one-shot requests for snapshot/config/reports/targets, then uses one-shot `setTimeout` guards only while waiting for async bridge events.

Expected polling exists in native code:

- `FrameScopeNativeMonitor.Watcher.cs`: watcher loop sleeps by `PollIntervalMs`.
- `FrameScopeProcessSampler.cs`: monitor-time loop sleeps by `ProcessSampleIntervalMs`, minimum 100ms.
- `FrameScopeSystemSampler.cs`: monitor-time loop sleeps by slow interval, minimum 500ms, default 1000ms.

The costly loop is ProcessSampler, not WebView2 idle.

### Repeated rendering

The React UI does not show a permanent render loop. The report HTML uses canvas rendering and caches render buckets/hover rows. It throttles hover with `requestAnimationFrame`, and the smoke/idle CPU data supports that idle rendering is stable.

### JSON and DOM size

The HTML report does not create a huge DOM for all points. It loads `framescope-interactive-data.js` into memory and draws with canvas. DOM size is not the primary issue.

The large-data issue is still real: a large Valorant run produced a 16.8 MB data JS file and generator memory peaked near 569 MB. The risk is full in-memory CSV-to-object-to-JS serialization and browser data load, not DOM nodes.

### Process release

Final residual check for the temporary runtime root found:

`matchingResidualCount = 0`

The monitor-session matrix also reported 0 remaining child processes per run. The only transient issue was the UI smoke leaving one temp-root `FrameScopeMonitor.exe --watcher`, which was stopped manually before final residual verification.

## Safe Optimization Items

1. Make `ProcessSampleIntervalMs` less aggressive by default for normal targets, for example 250ms, while preserving 100ms as an explicit high-resolution option.
2. Reduce ProcessSampler write volume without dropping diagnostic meaning: keep per-sample top CPU/IO, but consider lower-frequency full grouped process matrix or write only changed/non-idle process rows.
3. Optimize report generator memory: stream or chunk large CSV reads where possible, avoid duplicate passes over `process-samples.csv`, and avoid holding more intermediate collections than needed.
4. Keep report data complete but split output data into logical chunks or lazy-loaded sections so loading Reports does not require one large JS blob for every view.
5. Cache report-list discovery metadata per run, then invalidate on new report generation, instead of recursively scanning the full data root on every reports refresh.
6. Harden monitor stop from WebView2 smoke path: the smoke observed a temporary watcher left after start/stop. The backend cleanup path should prove watcher termination before emitting success.

## Boundaries That Should Not Be Changed

- Do not reduce or discard original frame/process/system data to make reports look fast.
- Do not remove FPS, 1% Low, 0.1% Low, hover inspection, process search, process timelines, system charts, IO/temperature charts, or report regenerate/open actions.
- Do not replace the native C# monitor path with PowerShell/Python during game capture.
- Do not weaken PresentMon failure diagnostics. Empty reports without frame data must stay diagnostic, not success.
- Do not change BF6-specific behavior based on this baseline. BF6 was intentionally not tested.
- Do not make UI idle fast by disabling real bridge refresh, Reports list, Targets lookup, or Settings save behavior.

## Recommended Implementation Order

1. Add a first-class performance profile switch or target setting for process sampling: normal mode around 250ms, high-resolution mode at 100ms.
2. Optimize `FrameScopeProcessSampler.exe` write volume and full process enumeration cost, then remeasure the 100/250/500/1000ms matrix.
3. Optimize report generation memory and duplicate reads, using the large Valorant copy as the regression fixture.
4. Split or lazy-load large report data while preserving complete raw data in artifacts.
5. Cache Reports list metadata and avoid repeated recursive scans when no run changed.
6. Fix or verify the monitor stop cleanup path observed by WebView2 smoke.
7. Only after the above, run a real non-BF6 game capture if authorized separately.

## Acceptance Metrics For Future Optimization

Use these as concrete before/after gates:

- WebView2 idle remains <= 0.10% average CPU and <= 200 MB total working set for the app + WebView2 process tree.
- Frontend smoke remains successful and should not leave a watcher or report generator process.
- 250ms ProcessSampler mode should keep monitor-session total average CPU <= 3.0% on the same synthetic matrix.
- 100ms high-resolution mode should not exceed the current baseline by more than 10% unless the feature explicitly needs more telemetry.
- Large 427 MB report generation should improve from 8.4s / 569 MB private memory, with a target of <= 5s and <= 350 MB private memory as a first pass.
- `framescope-interactive-data.js` should not grow for the same input unless new fields are intentionally added.
- Final residual check must remain 0 for `FrameScopeMonitor`, `FrameScopeProcessSampler`, `FrameScopeSystemSampler`, `FrameScopeReportGenerator`, PresentMon, FakePresentMon, and synthetic target processes started by the test.

## Residual Process Check

Final scoped residual query:

- Temporary root: `C:\Users\misakamiro\AppData\Local\Temp\framescope-perf-baseline-20260525-010341`
- Matching residual FrameScope/PresentMon/Fake target processes: `0`
- Global same-name check for `FrameScopeMonitor`, `FrameScopeProcessSampler`, `FrameScopeSystemSampler`, `FrameScopeReportGenerator`, `PresentMon`, `FakePresentMon`, and `TslGame`: no active results at final check.

## Requirement Coverage

| Requirement | Status | Evidence |
|---|---|---|
| WebView2 React UI idle CPU/memory | PASS | `ui-idle-metrics.json` |
| Page switch / Reports / Targets lookup / Settings save frontend workload | PASS | `ui-smoke-metrics.json`, `webui-smoke-evidence.json`, screenshots |
| Monitor session CPU/memory/IO for FrameScopeMonitor, ProcessSampler, SystemSampler, PresentMon, ReportGenerator | PARTIAL | monitor/session measured; report generator measured separately; real PresentMon did not produce CSV |
| Different sampler interval overhead | PASS | 100/250/500/1000ms synthetic matrix |
| Report generation time and memory | PASS | small synthetic and large Valorant copied run |
| Polling/re-render/JSON/DOM/process release review | PASS | static review plus runtime residual checks |
| Do not test BF6 / do not launch real game | PASS | synthetic target and copied non-BF6 Valorant run only |
| Do not change source/build/package/GitHub | PASS | no build/package/push; only this report written |
