# FrameScope P1 Process Chart Interaction Optimization Report

Date: 2026-06-01

Final conclusion: PASS

## Scope

This round only optimized the large report HTML background process chart interaction path.

Explicitly not handled:

| Area | Result |
| --- | --- |
| Backend monitoring overhead | Not changed |
| Frontend main UI pages | Not changed |
| UI animation | Not changed |
| Log retention / size guard | Not changed |
| P0 report generation logic | Not changed |
| Build artifact sync / installer / setup | Not run |
| Release / GitHub | Not run |

Hard boundaries:

| Boundary | Result |
| --- | --- |
| Packaging | Not run |
| FrameScope install | Not run |
| Real game launch | Not run |
| BF6 test | Not run |
| GitHub push | Not run |
| Release update | Not run |
| `build.ps1` | Not run |
| FPS raw statistics | Preserved |
| `bucketMs=1000` | Preserved |
| CPU Voltage / Vcore vs CPU Core VID | Preserved as separate data families |
| Process data removal | Not done |
| Process statistic semantics | Preserved |

## Dataset And Evidence

Baseline and after both used the same historical large report dataset:

`artifacts\poa31\history-large`

Scale:

| Metric | Value |
| --- | ---: |
| PresentMon rows | 876,603 raw / 876,593 valid / 876,585 selected |
| Process names | 119 |
| Process time samples | 17,714 |
| System samples | 1,933 |
| Process codec | `rle-v1` |
| FPS bucket | `bucketMs=1000` |

Evidence:

| Evidence | Path |
| --- | --- |
| before process probe | `artifacts\p1-process-interaction-optimization-20260601\before-dispatch\before-dispatch-process-interaction.json` |
| after generated report | `artifacts\p1-process-interaction-optimization-20260601\after-history-large\charts\framescope-interactive-report.html` |
| after process probe | `artifacts\p1-process-interaction-optimization-20260601\after-final4\after-final4-process-interaction.json` |
| layout probe | `artifacts\p1-process-interaction-optimization-20260601\layout-probe\report-overflow-probe.json` |

The after report was generated into an isolated artifact run directory using NTFS hardlinks for the large CSV inputs. The original `artifacts\poa31\history-large` report was not overwritten.

## Before Baseline

Fresh page runs, large historical report:

| Run | Process tab draw | Process tab elapsed | Search max draw | Search max input blocking | Hover | DOM nodes | JS heap | Process names | Process samples |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| before 1 | 12.4 ms | 90.8 ms | 11.9 ms | 12.1 ms | 0.3 ms | 319 | 15.182 MB | 119 | 17,714 |
| before 2 | 13.9 ms | 92.1 ms | 8.6 ms | 8.6 ms | 0.2 ms | 319 | 8.944 MB | 119 | 17,714 |
| average | 13.15 ms | 91.45 ms | 10.25 ms | 10.35 ms | 0.25 ms | 319 | 12.063 MB | 119 | 17,714 |

Search coverage:

| Query | Behavior |
| --- | --- |
| empty | Top 10 process list |
| `e` | multi-match search, 24 rendered series |
| `a` | multi-match search, 24 rendered series |
| `edge` | narrow search, 2 rendered series |
| `CODEX` | uppercase search input |
| `__NO_PROCESS_MATCH__` | no result |

## Hotspots Found

1. Process tab initial switch still had to decode and render Top N process series on first interaction.
2. Process search input was synchronous: the input event cleared caches and called `draw()` immediately, so large multi-match search blocked the input event for up to 12.1 ms in the measured baseline.
3. Process search repeatedly lowercased process names while scanning. The data size is not huge, but it is avoidable work on every keystroke.
4. RLE decoding used `encoded.split(';')`, allocating a token array before expanding values. That is extra allocation on large process reports.
5. Multi-match process search rendered up to 24 lines with the same per-series sampling budget as smaller line sets. This inflated canvas work: max searched drawn points averaged 15,390.
6. Tooltip/hover was not a hotspot in this dataset: before average was 0.25 ms and tooltip was visible.
7. Top N order was already precomputed by report data order; no repeated client-side full sort was found.
8. DOM churn was not the bottleneck. The process chart is canvas-based and DOM nodes stayed at 319.

## Files Changed

| File | Change |
| --- | --- |
| `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs` | Added process search index, bounded prewarm cache, streaming RLE decoder, rAF search scheduling, and lower multi-match process sampling. |
| `tests\chart-sampling-tests.js` | Added regression coverage for streaming RLE, search index, prewarm function, process sampling bound, and preserved chart contracts. |
| `tools\Probe-ReportProcessInteraction.js` | Added repeatable large-report process tab/search/hover performance and correctness probe. |
| `docs\implementation-reports\2026-06-01-framescope-p1-process-chart-interaction-optimization-report.md` | This implementation report. |

Generated evidence was written under `artifacts\p1-process-interaction-optimization-20260601\`.

## Optimizations And Safety

| Optimization | Why it does not affect functionality |
| --- | --- |
| Precomputed `processSearchIndex` | Uses the same `String(name).toLowerCase().includes(query)` matching semantics as before; it only moves lowercase conversion out of each search. |
| `processTopIndexes` / `processPrewarmIndexes` | Uses existing process order from `DATA.process.names`; Top N remains the first 10 entries and statistics are unchanged. |
| Streaming RLE decoder | Expands the same `rle-v1` tokens, preserves null padding, truncation, `NaN` handling, and expected length behavior. |
| Top/high-rank decoded series prewarm | Only fills `processSeriesCache`; it does not mutate `DATA.process`, remove data, change process ordering, or change chart values. |
| rAF search scheduling | Coalesces input redraw onto the browser frame queue. The final query value is still used, and correctness probes cover empty/no-result/case-insensitive search. |
| Multi-match process sampling bound | Still uses the existing spike/envelope path that keeps min/max per bucket. It reduces drawn canvas points for 24-line search views without dropping process data or changing statistics. |

## After Results

Fresh page runs, regenerated after report:

| Run | Process tab draw | Process tab elapsed | Search max draw | Search max input blocking | Hover | DOM nodes | JS heap | Process names | Process samples |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| after 1 | 9.6 ms | 88.3 ms | 6.9 ms | 0.1 ms | 0.3 ms | 319 | 7.541 MB | 119 | 17,714 |
| after 2 | 10.8 ms | 88.1 ms | 6.6 ms | 0.1 ms | 0.1 ms | 319 | 19.009 MB | 119 | 17,714 |
| average | 10.20 ms | 88.20 ms | 6.75 ms | 0.10 ms | 0.20 ms | 319 | 13.275 MB | 119 | 17,714 |

## Before / After Comparison

| Metric | Before avg | After avg | Change |
| --- | ---: | ---: | ---: |
| Process tab draw | 13.15 ms | 10.20 ms | 22.43% faster |
| Process tab elapsed | 91.45 ms | 88.20 ms | 3.55% faster |
| Process search draw | 10.25 ms | 6.75 ms | 34.15% faster |
| Process search input blocking | 10.35 ms | 0.10 ms | 99.03% lower |
| Tooltip / hover | 0.25 ms | 0.20 ms | 20.00% faster |
| DOM nodes | 319 | 319 | unchanged |
| Max searched drawn points | 15,390 | 9,424 | 38.77% lower |
| Process names | 119 | 119 | unchanged |
| Process time samples | 17,714 | 17,714 | unchanged |
| JS heap used | 12.063 MB | 13.275 MB | +1.212 MB |

Memory note: the small average heap increase is expected from the bounded process cache prewarm. It is limited to 24 high-rank CPU series and 10 Top N memory series, and process data itself is unchanged.

## Correctness Checks

Process search:

| Check | Result |
| --- | --- |
| Empty search returns Top 10 | PASS |
| `edge` search only returns names containing `edge` | PASS |
| `edge` vs `EDGE` case-insensitive result equality | PASS |
| `CODEX` uppercase search exercised | PASS |
| no-result query returns 0 series | PASS |
| process chart nonblank | PASS |
| tooltip visible and populated | PASS |

Process Top N:

Before and after empty search both returned the same Top 10:

`msedge`, `Codex`, `无畏契约登录器`, `QQ`, `System`, `dwm`, `powershell`, `Memory Compression`, `O+Connect`, `svchost`

Process data integrity:

| Check | Result |
| --- | --- |
| `DATA.process.names.length` | 119 |
| `DATA.process.stats.length` | 119 |
| `DATA.process.cpu.length` | 119 |
| `DATA.process.mem.length` | 119 |
| `DATA.process.t.length` | 17,714 |
| codec | `rle-v1` |

FPS / voltage / VID:

| Check | Result |
| --- | --- |
| `DATA.fps.bucketMs` | 1000 |
| FPS raw-stat path | Not changed; chart sampling and manifest tests PASS |
| CPU Voltage / Vcore data family | Still `DATA.cpuVoltage`; historical artifact has 0 series |
| CPU Core VID data family | Still `DATA.cpuVid`; historical artifact has 0 series |
| VID/Vcore separation | PASS via chart sampling and manifest tests |

Layout:

`tools\Probe-ReportHtmlLayout.js` result: `allNoOverflow=true`, 23 scenarios, 0 overflow.

## Verification Commands

| Command | Result |
| --- | --- |
| before process interaction probe, 2 fresh runs | PASS; data in `before-dispatch-process-interaction.json`. |
| after process interaction probe, 2 fresh runs | PASS; data in `after-final4-process-interaction.json`. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS; script added 110 npm packages as normal verify behavior; typecheck PASS; Vitest 5 files / 57 tests PASS; Vite build PASS. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS; `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS; `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS; `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS; `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS; `chart-sampling-tests: PASS`. |
| bundled Node `tools\Probe-ReportHtmlLayout.js --report .\artifacts\p1-process-interaction-optimization-20260601\after-history-large\charts\framescope-interactive-report.html --diagnostic same --out .\artifacts\p1-process-interaction-optimization-20260601\layout-probe` | PASS; `allNoOverflow=true`, 23 scenarios, 0 overflow. |
| test-only `csc.exe` report generator compile to `artifacts\p1-process-interaction-optimization-20260601\bin` | PASS. This was not packaging and did not run `build.ps1`. |
| isolated after report generation | PASS; generated after report with 876,585 frames, 119 processes, 17,714 process samples. |
| `git diff --check` | PASS exit 0; output contained existing LF-to-CRLF warnings only, no whitespace errors. |
| residual process check | PASS; `NO_MATCHING_RESIDUAL_PROCESSES`. |

## Risk

Risk is low to medium and limited to report HTML interaction:

- Search and Top N semantics are covered by the process probe.
- RLE behavior is covered by Node regression tests.
- Process data count and process statistic meaning are unchanged.
- Multi-match search draws fewer canvas points, but still uses the same spike/envelope min/max sampling approach to preserve chart readability.
- The bounded prewarm cache trades about +1.2 MB average JS heap in this probe for lower first-interaction latency.

Final result: PASS.
