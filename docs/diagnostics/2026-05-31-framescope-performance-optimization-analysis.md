# FrameScope performance optimization analysis

Date: 2026-05-31
Workspace: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

This round is analysis only. Product source code was not changed. I created temporary measurement scripts and outputs under `artifacts\performance-optimization-analysis-20260531\` and wrote this report.

## 1. Summary

Overall result: PASS.

Optimization is recommended, but it should be split by area. The current evidence does not support a broad rewrite of the UI or report charts. The strongest hotspot is very large report generation.

| Area | Need optimization | Priority | Evidence |
| --- | --- | --- | --- |
| A. Frontend display pages | Only for large target/process lists | P2 | Normal pages load/render locally in 8-24 ms after initial load, 168-251 DOM nodes. A 250-row target process list reaches 1192 DOM nodes and 269 transitioned elements. |
| B. Frontend UI animation | No current optimization needed | Not recommended / P2 guard only | Normal pages have `animatedCount=0`; reduced-motion path keeps animations at 0 and load/nav remains normal. CSS avoids blur/backdrop filters in measured UI. |
| C. Report generation speed | Yes | P0 | Synthetic 20k rows: 311 ms, 51.83 MB WS. Historical 876,585-frame report: 7,844 ms wall, 7,750 ms CPU, 551.11 MB WS / 548.6 MB private. |
| D. Report chart performance | Yes, targeted process chart only | P1 | Large history report chart ready in 120 ms; FPS/system/perf tabs draw under 4.3 ms, but process tab peaks at 12.6 ms and process search at 14.6 ms. |
| E. Backend monitor overhead | Yes | P1 | Synthetic monitor sessions show `FrameScopeSystemSampler` at 53.26-53.78 MB WS, 62.69-64.12 MB private, 8.31-9.10% single-core estimate. |
| F. Logging overhead | No current optimization needed | Not recommended / P2 retention guard | Default run writes 0 watcher log bytes. Verbose/perf modes write only 632-698 bytes across about 11-12 s. |

Recommended next implementation entry: yes, start with the P0 report-generation window only. Do not mix frontend, chart interaction, sampler, and logging changes in the same implementation window.

## 2. Context reviewed before measuring

I read the latest implementation and retest context for the recent work so the optimization analysis would not accidentally break current semantics:

- `docs\implementation-reports\2026-05-30-framescope-fps-chart-gamepp-style-report.md`
- `docs\implementation-reports\2026-05-30-framescope-all-report-charts-gamepp-style-report.md`
- `docs\implementation-reports\2026-05-31-framescope-cpu-voltage-gamepp-alignment-report.md`
- `docs\implementation-reports\2026-05-31-framescope-cpu-core-vid-recording-chart-report.md`
- `docs\implementation-reports\2026-05-31-framescope-frontend-dead-component-cleanup-report.md`
- `docs\implementation-reports\2026-05-31-framescope-frontend-medium-component-cleanup-report.md`
- `docs\implementation-reports\2026-05-31-framescope-low-risk-cleanup-report.md`
- `docs\test-reports\2026-05-31-framescope-post-cleanup-verification.md`
- Earlier baselines for comparison: `docs\diagnostics\2026-05-29-framescope-full-performance-optimization-analysis.md` and `docs\diagnostics\2026-05-24-framescope-performance-baseline.md`

Current analysis still re-measured the state. Old conclusions were not reused as final evidence.

## 3. Hotspot ranking

### P0 must optimize

1. Very large report generation time and peak memory.
   - Current data: historical large copy, 876,585 frames / 876,603 raw PresentMon rows / 17,714 process samples / 427,061,403 bytes input, generated in 7,844 ms wall / 7,750 ms CPU, peak 551.11 MB working set and 548.6 MB private memory.
   - Main files: `src\reporting\FrameScopeReportGenerator.cs`, `FrameScopeReportGenerator.PresentMon.cs`, `FrameScopeReportGenerator.SystemData.cs`, `FrameScopeReportGenerator.ProcessData.cs`, `FrameScopeReportGenerator.Metadata.cs`, `FrameScopeReportGenerator.Csv.cs`.
   - Likely cause: the generator keeps large parsed structures in memory and serializes a full artifact payload after reading large CSV inputs. The current output payload is already compact enough for the browser, so the pressure is mostly in generator-side parsing/aggregation.

### P1 recommended

1. Backend monitor sampler overhead.
   - Current data: `FrameScopeSystemSampler` consistently measured around 53 MB working set / 63 MB private memory and 8.31-9.10% single-core estimate during 8 s synthetic capture windows. `FrameScopeMonitor` and `FrameScopeProcessSampler` CPU were 0% in the same measurement resolution.
   - Main files: `src\monitoring\FrameScopeSystemSampler.cs`, `FrameScopeSystemSampler.PerfCounters.cs`, `FrameScopeSystemSampler.CpuCoreTelemetry.cs`, `FrameScopeProcessSampler.cs`, `src\app\FrameScopeNativeMonitor.MonitorSession.cs`, `FrameScopeNativeMonitor.Watcher.cs`.

2. Large report process chart interaction.
   - Current data: large report `process` tab draw max 12.6 ms, process search max 14.6 ms, zoom max 7.1 ms. Other tabs are lower: FPS 0.9-1.0 ms, perf 2.7 ms, system 2.9 ms, IO 2.1 ms.
   - Main files: `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`, `src\reporting\FrameScopeReportGenerator.ProcessData.cs`.

### P2 optional

1. Frontend target/process list threshold optimization.
   - Current data: normal Overview/Reports/Settings/Targets are small. The synthetic 250-process target search case reached 1192 DOM nodes, 261 buttons, 269 transitioned elements, but still refreshed in 33 ms with no horizontal overflow.
   - Recommendation: only consider virtualization/windowing or memoized row components above a threshold such as 250-500 rows.

2. Recursive status/report discovery guard for very large data roots.
   - Static evidence: `FrameScopeReportProgress.cs` uses recursive `Directory.GetFiles(dataRoot, "status.json", SearchOption.AllDirectories)`, and report listing searches report HTML recursively. No current measurement showed this as the top bottleneck, so keep it P2 unless users have huge run directories.

3. Logging retention/rotation audit.
   - Current data: runtime logging volume is low. Keep only as a guard for long-running installations, not as a performance implementation priority.

### Not recommended

- Do not rewrite normal frontend pages for memoization/lazy loading now. Current normal page metrics do not justify it.
- Do not remove UI motion wholesale. Current animation data does not show meaningful pressure.
- Do not change FPS `bucketMs=1000`.
- Do not alter raw PresentMon statistics semantics.
- Do not merge CPU Voltage / Vcore and CPU Core VID semantics.
- Do not optimize by disabling CPU Voltage, CPU VID, or CPU core telemetry.
- Do not raise the global default telemetry interval as a blanket optimization.
- Do not drop raw rows, fake telemetry, or remove diagnostic data to make reports faster.

## 4. A. Frontend display page optimization

Current data:

- `Run-Frontend.ps1 verify`: exit 0, 9,609 ms total.
- Vitest: 5 files passed, 57 tests passed.
- Vite production build: 2,000 modules transformed, build 1.77 s.
- Bundle output:
  - `dist/index.html`: 0.53 kB, gzip 0.33 kB.
  - `dist/assets/index-C2p4fFQH.css`: 46.64 kB, gzip 8.00 kB.
  - `dist/assets/index-CPFZSQ60.js`: 366.62 kB, gzip 113.18 kB, sourcemap 1,339.99 kB.
- Page probe, normal motion:
  - Overview: load event 54 ms, navigation 0 ms, 168 DOM nodes, 8 buttons, overflowX false.
  - Reports: navigation 13 ms, 244 DOM nodes, 3 report rows, 24 buttons, overflowX false.
  - Settings: navigation 10 ms, 251 DOM nodes, 10 inputs, overflowX false.
  - Targets: navigation 24 ms, 246 DOM nodes, 4 target rows, overflowX false.
- Reduced motion:
  - Load event 16 ms, page navigation 8-12 ms after initial page, same DOM scale, overflowX false.
- Large target process fixture:
  - 250 process rows, process refresh to rows 33 ms, navigation 31 ms.
  - 1192 DOM nodes, 261 buttons, 269 transitioned elements, overflowX false.

Need optimization: not for normal pages. P2 only for large process search/list states.

Priority: P2.

Expected benefit:

- If large process lists become common, thresholded virtualization or row memoization can lower DOM count and style recalculation for 500+ process results.
- Normal flows will not gain enough to justify risk.

Risk:

- Virtualizing a selectable process list can break keyboard navigation, screen-reader table semantics, row focus, and visual QA fixtures if done too broadly.
- State splitting can cause stale operation status if bridge events are not carefully scoped.

Related files:

- `src\frontend\src\pages\TargetsPage.tsx`
- `src\frontend\src\pages\ReportsPage.tsx`
- `src\frontend\src\state\useFrameScopeBridgeState.ts`
- `src\frontend\src\pages\pages.css`
- `src\frontend\src\layout\*.tsx`

Suggested validation:

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
- Browser probe for Overview / Reports / Settings / Targets.
- Re-run many-results target process fixture with 250, 500, and 1000 process rows.
- Confirm no horizontal overflow and no lost selection/focus behavior.

Implementation window:

- One small frontend-only window: target process result list thresholding.
- Do not mix with report chart code or backend sampling changes.

## 5. B. Frontend UI animation performance

Current data:

- Normal page probe: Overview/Reports/Settings/Targets all reported `animatedCount=0`.
- Reduced-motion probe: animations remain 0; navigation stays 8-12 ms after initial load.
- Large target process fixture: `animatedCount=1`, tied to row update feedback; no horizontal overflow and refresh-to-250-rows was 33 ms.
- CSS/static checks:
  - `src\frontend\src\theme\tokens.css` sets material blur to `none`.
  - Motion contract tests check that page navigation does not use page fade/slide/scale/blur/exit animation.
  - `pages.css` includes `prefers-reduced-motion: reduce` paths that disable or reduce animations.

Need optimization: no current performance-driven change.

Priority: Not recommended now. P2 guard only if future QA shows animation jank on low-end machines.

Expected benefit:

- Minimal. Removing the remaining row/menu/spinner animations would likely save little while reducing UI feedback quality.

Risk:

- Over-optimizing animations can make process refresh, report menu open/close, and busy states feel broken or unresponsive.

Related files:

- `src\frontend\src\theme\motion.ts`
- `src\frontend\src\theme\tokens.css`
- `src\frontend\src\layout\PageTransition.tsx`
- `src\frontend\src\layout\layout.css`
- `src\frontend\src\pages\pages.css`
- `src\frontend\src\uiMotionContract.test.ts`

Suggested validation:

- Re-run frontend verify.
- Browser probe normal/reduced motion.
- Check `animatedCount`, `transitionedCount`, and overflow after any visual adjustment.

Implementation window:

- No implementation window recommended now.

## 6. C. Report generation speed

Current data:

| Dataset | Frames/raw rows | Input bytes | Wall ms | CPU ms | Peak WS MB | Peak private MB | `data.js` bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| synthetic-1k | 1,000 / 1,000 | 155,983 | 162 | 140.625 | 33.21 | 24.65 | 12,606 |
| synthetic-5k | 5,000 / 5,000 | 906,419 | 195 | 187.5 | 44.63 | 45.70 | 55,921 |
| synthetic-20k | 20,000 / 20,000 | 4,769,255 | 311 | n/a | 51.83 | 50.81 | 342,123 |
| history-large-copy | 876,585 / 876,603 | 427,061,403 | 7,844 | 7,750 | 551.11 | 548.60 | 1,266,013 |

The historical large data was copied to a short artifact path before measurement to avoid long-path noise. No real game was launched.

Need optimization: yes.

Priority: P0.

Expected benefit:

- Target a large-report generator peak memory reduction from about 550 MB to below 250-300 MB.
- Target large history generation from about 7.8 s toward 3-5 s, while keeping the same output semantics.
- Biggest likely win is generator-side streaming/downsampling/aggregation, not browser rendering.

Risk:

- High. The report generator owns sensitive semantics: FPS raw PresentMon statistics, FPS `bucketMs=1000`, CPU Voltage / Vcore, CPU Core VID, process RLE payload, and GamePP-style charts.
- Any optimization that changes statistics, bucket boundaries, or voltage/VID separation is not acceptable.

Related files:

- `src\reporting\FrameScopeReportGenerator.cs`
- `src\reporting\FrameScopeReportGenerator.PresentMon.cs`
- `src\reporting\FrameScopeReportGenerator.SystemData.cs`
- `src\reporting\FrameScopeReportGenerator.ProcessData.cs`
- `src\reporting\FrameScopeReportGenerator.Metadata.cs`
- `src\reporting\FrameScopeReportGenerator.Csv.cs`
- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`
- `tests\FrameScopeReportManifestTests.cs`
- `tests\chart-sampling-tests.js`

Suggested validation:

- Re-run synthetic 1k / 5k / 20k and history-large-copy measurements.
- Add generator-side assertions that `fps.bucketMs` stays 1000, raw PresentMon row counts stay unchanged, and VID/Vcore fields remain isolated.
- Compare manifest counts, `data.js` schema, and chart probe results before/after.

Implementation window:

- One P0 report-generation-only window.
- Suggested split inside that window:
  1. Add measurement harness/checks as test artifacts.
  2. Optimize PresentMon/system/process CSV parsing and aggregation memory behavior.
  3. Preserve output schema and run manifest/chart contract tests.
  4. Re-measure all four data scales.

## 7. D. Report chart performance

Current data:

Synthetic 20k report:

- Ready: 150 ms.
- Initial FPS draw: 0.6 ms.
- Tab draw max: 2.9 ms.
- Process tab draw: 2.8 ms.
- Process search max: 2.9 ms.
- Zoom max: 2.6 ms.
- Tooltip settle: 2 ms.
- Final heap estimate: about 2.45 MB.
- `fpsBucketMs=1000`, `processCodec=rle-v1`, CPU voltage series 1, CPU VID series 8.

History large report:

- Ready: 120 ms.
- Initial FPS draw: 1.0 ms.
- FPS tab: 0.9 ms.
- CPU Voltage / Vcore tab: 2.4 ms, no data in this historical run.
- CPU Core VID tab: 4.3 ms, no data in this historical run.
- Performance tab: 2.7 ms.
- System tab: 2.9 ms.
- Process tab: 12.6 ms.
- IO tab: 2.1 ms.
- Process search: 14.6 ms.
- Zoom: 7.1 ms.
- Tooltip settle: 3 ms, visible.
- Final heap estimate: about 12.26 MB.
- `fpsBucketMs=1000`, `processCodec=rle-v1`, 119 processes, 17,714 process samples.

Layout probe:

- Synthetic 20k: `allNoOverflow=true`, 23 scenarios, covering FPS, CPU Voltage / Vcore, CPU Core VID, performance clocks, system usage, background process, IO, temperature, diagnostic report.
- History large: `allNoOverflow=true`, 23 scenarios, same chart coverage.
- FPS dropdown retained Average/1% Low/0.1% Low options and did not expose Min Instant as a metric.

Need optimization: targeted process chart only.

Priority: P1.

Expected benefit:

- Reduce process search/redraw spikes from about 14.6 ms toward under 8 ms on large reports.
- Keep report chart memory stable for longer interaction sessions.

Risk:

- Medium. Process chart already uses `PROCESS_TOP_N=10`, RLE decoding, bounded visible indexes, render cache, hover cache, and requestAnimationFrame hover scheduling. Further optimization must not break search correctness, Top N semantics, tooltip values, or process CPU/memory switching.

Related files:

- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`
- `src\reporting\FrameScopeReportGenerator.ProcessData.cs`
- `src\reporting\FrameScopeReportGenerator.Html.Styles.cs`
- `tests\chart-sampling-tests.js`
- `tools\Probe-ReportHtmlLayout.js`

Suggested validation:

- `node tests\chart-sampling-tests.js`
- `node tools\Probe-ReportHtmlLayout.js --report <synthetic-20k-report>`
- `node tools\Probe-ReportHtmlLayout.js --report <history-large-report>`
- Re-run report HTML performance probe for FPS, CPU Voltage / Vcore, CPU Core VID, GPU/perf, IO, temperature, and process views.

Implementation window:

- One chart-only window after the P0 generator window.
- Keep it limited to process chart decoding/rendering/search behavior.

## 8. E. Backend monitoring overhead

Current data:

Synthetic monitor sessions used `TslGame.exe` as a fake target and `FakePresentMon.exe`. No real game was launched.

| Mode | Interval | Duration ms | Monitor CPU % | ProcessSampler CPU % | SystemSampler CPU % | SystemSampler WS/private MB | Process rows/bytes | Log bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| default-1000ms | 1000 | 11,038 | 0 | 0 | 9.06 | 53.61 / 62.69 | 749 / 65,999 | 0 |
| default-1500ms | 1500 | 11,241 | 0 | 0 | 8.90 | 53.76 / 62.84 | 535 / 46,771 | 0 |
| verbose-1000ms | 1000 | 12,032 | 0 | 0 | 8.31 | 53.78 / 64.12 | 856 / 75,377 | 632 |
| perf-1000ms | 1000 | 10,990 | 0 | 0 | 9.10 | 53.26 / 63.14 | 856 / 75,238 | 698 |

Static evidence:

- `FrameScopeConfigStore.cs` defines default global telemetry interval 1000 ms, min 500 ms, max 5000 ms.
- `FrameScopeNativeMonitor.Watcher.cs` passes the same normalized telemetry interval into sample, process, slow, CPU core, CPU voltage, and CPU VID paths.
- `FrameScopeProcessSampler.cs` enumerates `Process.GetProcesses()` each sample, writes grouped process rows, Top CPU, Top IO, alerts, and flushes every 10 samples.
- `FrameScopeSystemSampler.cs` checks target process, samples performance counters, calls CPU core/voltage/VID `TryWriteSample`, flushes system CSV every 5 samples, and sleeps by loop elapsed time.

Need optimization: yes, but targeted.

Priority: P1.

Expected benefit:

- Lower `FrameScopeSystemSampler` working set/private memory and single-core overhead.
- Reduce CSV growth for process samples when interval increases or when process count is high.
- Preserve global interval behavior and telemetry fidelity.

Risk:

- High if the optimization disables or merges telemetry. CPU Voltage / Vcore and CPU Core VID must stay separate and enabled.
- Medium for process sampler changes: grouping/top-N changes can affect report process charts and diagnostics.

Related files:

- `src\monitoring\FrameScopeSystemSampler.cs`
- `src\monitoring\FrameScopeSystemSampler.PerfCounters.cs`
- `src\monitoring\FrameScopeSystemSampler.CpuCoreTelemetry.cs`
- `src\monitoring\FrameScopeProcessSampler.cs`
- `src\monitoring\FrameScopeProcessSampler.IO.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession.cs`
- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `src\core\FrameScopeConfigStore.cs`

Suggested validation:

- Re-run `Measure-BackendMonitorAndLogs.ps1` with 1000 ms and 1500 ms.
- Add longer synthetic run, for example 60 s, to reduce CPU-time quantization noise.
- Run `FrameScopeSystemSamplerCpuCoreTests.exe`, `FrameScopeDiagnosticsTests.exe`, and report manifest tests.
- Confirm sample interval fields remain 1000 by default and VID/Vcore remain isolated.

Implementation window:

- One backend-sampler-only window.
- Do not include frontend or report generator changes.
- Suggested work:
  1. Add stronger sampler measurement harness.
  2. Investigate SystemSampler memory/CPU source.
  3. Optimize counter/provider lifecycle and avoid unnecessary repeated expensive reads.
  4. Validate CSV row counts, status JSON, and report ingestion.

## 9. F. Logging performance

Current data:

- Default 1000 ms run: watcher log delta 0 bytes / 0 lines.
- Default 1500 ms run: watcher log delta 0 bytes / 0 lines.
- Verbose 1000 ms run: 632 bytes / 3 lines / 52.53 bytes per second.
- Performance diagnostics 1000 ms run: 698 bytes / 5 lines / 63.51 bytes per second.
- stdout/stderr output in synthetic monitor runs: 0 bytes.

Static evidence:

- `FrameScopeConfigStore.cs` defaults `EnableVerboseLogs=false` and `EnablePerformanceDiagnosticsLogs=false`.
- `FrameScopeLoggingPolicy.cs` gates verbose/performance logging on config flags.
- Watcher and monitor-session performance logs are sparse phase logs, not per-frame logs.
- CSV samplers flush in batches: process sampler every 10 samples, system sampler every 5 samples.

Need optimization: no current runtime-log performance change.

Priority: Not recommended now. P2 retention/rotation audit only.

Expected benefit:

- Small. The current measured log volume is negligible compared with report generation and sampler overhead.

Risk:

- Reducing logs further can remove useful diagnostics for PresentMon/ETW/no-CSV failures.

Related files:

- `src\core\FrameScopeLoggingPolicy.cs`
- `src\core\FrameScopeConfigStore.cs`
- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession.cs`
- `src\monitoring\FrameScopeProcessSampler.cs`
- `src\monitoring\FrameScopeSystemSampler.cs`

Suggested validation:

- Re-run default/verbose/perf synthetic monitor sessions.
- Check log delta bytes/lines and file growth over a longer synthetic run.
- Confirm PresentMon stdout/stderr and diagnostic markdown still preserve failure evidence.

Implementation window:

- No immediate implementation window.
- If needed later, make it a logging-only window: retention limits, lazy formatting, and size caps without reducing required diagnostics.

## 10. Areas that must not be changed

Do not change these while optimizing:

- FPS GamePP chart visual/interaction contract.
- FPS `bucketMs=1000`.
- Raw PresentMon statistics and row-count semantics.
- CPU Voltage / Vcore independent definition.
- CPU Core VID independent definition.
- VID/Vcore bidirectional isolation.
- Report chart GamePP style.
- Existing Overview / Targets / Settings / Reports flow.
- Existing target/settings/report bridge contracts.
- Diagnostic behavior for no PresentMon CSV, ETW access denied, and no frame data.
- Default global telemetry interval semantics unless a user explicitly changes settings.

## 11. Suggested implementation windows

1. Window 1, P0 report generation only:
   - Optimize generator parsing/aggregation memory and time.
   - Verify synthetic 1k/5k/20k and historical large copy.
   - Preserve manifest, `data.js`, FPS bucket, raw PresentMon, Vcore, VID, process chart contracts.

2. Window 2, P1 backend sampler only:
   - Reduce SystemSampler overhead and process sampler CSV/data volume without disabling telemetry.
   - Verify with synthetic target, not a real game.
   - Run sampler/report/diagnostic tests.

3. Window 3, P1 report process chart only:
   - Optimize process chart search/redraw/decoded-series cache.
   - Verify layout probe and chart interaction probe on synthetic 20k plus history-large.

4. Window 4, P2 frontend list threshold only:
   - Optimize target process result list only if real usage shows frequent 250+ result sets.
   - Keep normal page code unchanged unless evidence changes.

5. Window 5, P2 logging/data-root guard only:
   - Only if long-running installations show data-root scan or log retention problems.
   - Do not combine with sampler or generator changes.

## 12. Commands and results

Read-only / measurement commands:

- `git status --short`
  - Result: dirty worktree with many pre-existing modified/untracked files. This analysis did not stage or commit anything.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
  - Exit 0, duration 9,609 ms.
  - 5 Vitest files passed, 57 tests passed.
  - Vite build output: HTML 0.53 kB gzip 0.33 kB, CSS 46.64 kB gzip 8.00 kB, JS 366.62 kB gzip 113.18 kB.
  - Note: this verify script may refresh `node_modules` and `src\frontend\dist`; that is verification behavior, not FrameScope installation or product packaging.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
  - Exit 0, duration 1,960 ms.
- `.\tests\FrameScopeReportManifestTests.exe`
  - PASS, exit 0, duration 595 ms.
- `.\tests\FrameScopeDiagnosticsTests.exe`
  - PASS, exit 0, duration 493 ms.
- `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`
  - PASS, exit 0, duration 121 ms.
- Bundled Node: `.\tests\chart-sampling-tests.js`
  - PASS, exit 0, duration 83 ms.
- Bundled Node: `tools\Probe-ReportHtmlLayout.js` on synthetic 20k report.
  - Exit 0, duration 22,116 ms, `allNoOverflow=true`.
- Bundled Node: `tools\Probe-ReportHtmlLayout.js` on history-large report.
  - Exit 0, duration 21,900 ms, `allNoOverflow=true`.
- Temporary measurement: `artifacts\performance-optimization-analysis-20260531\measurements\Measure-FrontendPages.js`
  - Output: `frontend-pages-current-metrics.json`.
  - Normal pages: 168-251 DOM nodes, navigation 0-24 ms, overflowX false.
  - Many-results target list: 250 rows, 1192 DOM nodes, refresh-to-rows 33 ms, overflowX false.
- Temporary measurement: `artifacts\performance-optimization-analysis-20260531\measurements\Measure-ReportGeneration.ps1`
  - Output: `report-generation-current-metrics.json`.
  - Synthetic 1k/5k/20k: 162/195/311 ms wall.
  - History large: 7,844 ms wall, 551.11 MB peak working set.
- Temporary measurement: `artifacts\performance-optimization-analysis-20260531\measurements\Measure-ReportHtmlPerf.js`
  - Outputs: `report-html-perf-synthetic-20k.json`, `report-html-perf-history-large.json`.
  - Synthetic 20k max tab draw 2.9 ms.
  - History large max chart action 14.6 ms on process search.
- Temporary measurement: `artifacts\performance-optimization-analysis-20260531\measurements\Measure-BackendMonitorAndLogs.ps1`
  - Output: `backend-monitor-log-current-metrics.json`.
  - Used synthetic `TslGame.exe` and `FakePresentMon.exe`, not a real game.
  - Default logs 0 bytes. Verbose/perf logs 632-698 bytes.
  - SystemSampler measured 53.26-53.78 MB WS and 8.31-9.10% single-core estimate.

Final verification after writing this report:

- `git diff --check`
  - Exit 0.
  - Output included existing LF/CRLF working-copy warnings across many dirty files; no whitespace error was reported.
- Residual process check for `FrameScopeMonitor`, `FrameScopeProcessSampler`, `FrameScopeSystemSampler`, `FrameScopeReportGenerator`, `PresentMon`, `FakePresentMon`, `TslGame`, and headless Edge/Playwright processes tied to this artifact.
  - Result: `NO_MATCHING_RESIDUAL_PROCESSES`.

## 13. Boundary answers

- Modified source code: No.
- Fixed bugs: No product bug fix. A temporary artifact measurement script variable was corrected after a PowerShell `$PID` naming conflict.
- Deleted or moved product files: No.
- Packaged installer/setup: No.
- Installed FrameScope: No.
- Started real game: No.
- Tested BF6: No.
- Pushed GitHub: No.
- Updated Release: No.
- Ran `build.ps1`: No.

## 14. Final conclusion

PASS.

FrameScope currently does not need broad frontend or animation optimization. It does need a focused P0 optimization pass for very large report generation, followed by separate P1 windows for backend sampler overhead and large-report process chart interaction. Logging should not be optimized now beyond future retention/size guards, because the measured runtime log volume is already negligible.
