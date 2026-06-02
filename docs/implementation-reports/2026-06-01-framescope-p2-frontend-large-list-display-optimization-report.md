# FrameScope P2 Frontend Large List Display Optimization Report

Date: 2026-06-01

Final conclusion: PASS

## Scope

This round only handled the P2 frontend large target/process list display performance item. The optimized path is limited to the Targets page process lookup results when the list reaches the large-list threshold.

Explicitly not handled:

| Area | Result |
| --- | --- |
| UI animation optimization | Not changed |
| Chart generation | Not changed |
| Report process chart interaction | Not changed |
| Backend monitoring | Not changed |
| Logging optimization | Not changed |
| Data root recursive scan protection | Not changed |
| Build artifact sync | Not run |
| Install/update validation | Not run |

Hard boundaries:

| Boundary | Result |
| --- | --- |
| Packaging / installer build | Not run |
| FrameScope install | Not run |
| Real game launch | Not run |
| BF6 test | Not run |
| GitHub push | Not run |
| Release update | Not run |
| `build.ps1` | Not run |
| Large new frontend dependency | Not added |
| FPS raw statistics | Preserved; not touched |
| `bucketMs=1000` | Preserved; not touched |
| CPU Voltage / Vcore | Preserved; not touched |
| CPU Core VID | Preserved; not touched |
| Target add/edit/delete | Verified PASS |
| Settings save/read | Verified PASS |
| Reports refresh/open/regenerate basic flow | Verified PASS |

`tools\Run-Frontend.ps1 verify` ran its normal frontend verification workflow, including `npm ci` and a Vite build. This is recorded as validation script behavior, not product packaging, installer generation, or FrameScope installation.

## Evidence And Method

I added a dedicated frontend large-list probe under `tools\Probe-FrontendLargeLists.js`. It starts a local Vite preview/dev server, drives headless Edge through CDP, and records DOM nodes, CDP node counts, render/navigation time, input dispatch/frame timing, search refresh timing, scroll timing, JS heap, screenshots, and smoke-test results.

Evidence files:

| Evidence | Path |
| --- | --- |
| before probe, 2 runs | `artifacts\p2-frontend-large-list-optimization-20260601\before-fixed\before-frontend-large-list-probe.json` |
| after-final probe, 2 runs + smoke | `artifacts\p2-frontend-large-list-optimization-20260601\after-final\after-final-frontend-large-list-probe.json` |
| earlier after probe, 2 runs | `artifacts\p2-frontend-large-list-optimization-20260601\after\after-frontend-large-list-probe.json` |
| in-app Browser probe | `artifacts\p2-frontend-large-list-optimization-20260601\browser-probe\browser-top-probe.json` |

Probe coverage:

| Scenario | Row count | Purpose |
| --- | ---: | --- |
| Reports normal list | 3 reports | Normal Reports page list guardrail |
| Targets small process list | 0 initial / 1 filtered | Normal/small Targets process lookup guardrail |
| Targets large process list | 250 processes | Large process lookup list before/after comparison |

The stable before/after comparison covers 250 rows. I did not force a 500-row product fixture in this round because the existing visual fixture is stable at 250 rows and the requested hard floor was 250 rows; keeping the synthetic surface minimal reduced risk to product preview behavior.

Screenshots / probe evidence:

| View | Evidence |
| --- | --- |
| Reports normal list before | `artifacts\p2-frontend-large-list-optimization-20260601\before-fixed\reports-normal-run1.png` |
| Targets small process list before | `artifacts\p2-frontend-large-list-optimization-20260601\before-fixed\targets-small-process-run1.png` |
| Targets large process list before | `artifacts\p2-frontend-large-list-optimization-20260601\before-fixed\targets-large-process-250-run1.png` |
| Reports normal list after-final | `artifacts\p2-frontend-large-list-optimization-20260601\after-final\reports-normal-run1.png` |
| Targets small process list after-final | `artifacts\p2-frontend-large-list-optimization-20260601\after-final\targets-small-process-run1.png` |
| Targets large process list after-final | `artifacts\p2-frontend-large-list-optimization-20260601\after-final\targets-large-process-250-run1.png` |
| Target smoke screenshot | `artifacts\p2-frontend-large-list-optimization-20260601\after-final\smoke-targets.png` |
| Settings smoke screenshot | `artifacts\p2-frontend-large-list-optimization-20260601\after-final\smoke-settings.png` |
| Reports smoke screenshot | `artifacts\p2-frontend-large-list-optimization-20260601\after-final\smoke-reports.png` |

The in-app Browser screenshot timed out twice, so the browser evidence is the DOM/probe JSON plus CDP screenshots captured by the dedicated probe. Browser probe top-of-list result: `processTotal=250`, `processRows=19`, `windowed=true`, `domNodes=332`, no console warnings/errors.

## Before Baseline

Before probe: `before-fixed`, 2 runs.

| Scenario | DOM nodes | CDP nodes | Render/nav | Input dispatch | Input frame | Search refresh | Scroll | JS heap | Rendered rows | Total rows |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Reports normal list | 250 | 1684.5 | 46.15 ms | n/a | n/a | n/a | n/a | 13.87 MB | 0 | 0 |
| Targets small process list | 252 | 2175.5 | 46.80 ms | 1.50 ms | 20.35 ms | 499.70 ms | n/a | 13.69 MB | 0 initial / 1 filtered | 0 initial / 1 filtered |
| Targets large process list | 1198 | 4601.5 | 46.45 ms | 4.50 ms | 30.55 ms | 499.45 ms | 22.15 ms | 17.84 MB | 250 | 250 |

Large-list baseline findings:

| Finding | Evidence |
| --- | --- |
| Too many DOM nodes were mounted for the 250-row process list | 1198 DOM nodes, 4601.5 CDP nodes |
| Every process row was rendered at once | 250 rendered rows for 250 total rows |
| The row count also inflated interactive elements | 261 buttons in the large-list baseline run |
| Input dispatch was higher on large lists than small lists | 4.50 ms large vs 1.50 ms small |
| Search refresh timing was dominated by the mock bridge delay | about 499 ms before/after, not a row rendering hotspot |
| No frontend dependency or backend call was required to improve the DOM cost | The bottleneck was local list rendering |

## Hotspots Found

1. The process lookup result panel rendered all 250 rows into DOM at once. This produced about 1198 DOM nodes on the Targets page and 250 process row buttons.
2. The large list made input dispatch heavier because React had more mounted row DOM to reconcile and maintain.
3. Search/filter refresh time was not primarily a DOM-rendering issue in the probe. The measured refresh stayed around 499 ms because the mock bridge intentionally waits before resolving.
4. No repeated full sort was found in the process result render path. The visible issue was the unbounded `map` from process results to rows.
5. Target configuration rows are small and editable, so memoizing or virtualizing target edit rows would add risk without measurable need in this scope.
6. A thresholded windowing path is appropriate: normal/small lists remain visually identical, while 250+ row lists reduce mounted rows and preserve scroll reach with spacer elements.

## Files Changed

| File | Change |
| --- | --- |
| `src\frontend\src\pages\TargetsPage.tsx` | Added thresholded process-result windowing for 250+ rows, stable scroll state, bounded result signature, and probe attributes. |
| `src\frontend\src\pages\pages.css` | Added windowed process-list scrollbar/spacer styles. |
| `src\frontend\src\utils\virtualListWindow.ts` | Added a small dependency-free virtual list window calculator. |
| `src\frontend\src\utils\virtualListWindow.test.ts` | Added unit coverage for small, middle, and end-of-list window calculations. |
| `src\frontend\src\uiInteractionContract.test.ts` | Added contract coverage for the thresholded windowing path and probe attributes. |
| `tools\Probe-FrontendLargeLists.js` | Added repeatable browser performance/smoke probe for Reports, Targets small process list, Targets large process list, and target/settings/report smoke. |
| `docs\implementation-reports\2026-06-01-framescope-p2-frontend-large-list-display-optimization-report.md` | This implementation report. |

## Optimizations And Safety

| Optimization | Why it does not affect functionality |
| --- | --- |
| Threshold `PROCESS_RESULT_WINDOW_THRESHOLD = 250` | Lists below 250 rows still render through the existing full-list path, so ordinary and small process lists keep the same DOM shape and visual behavior. |
| Lightweight windowing via `getVirtualListWindow` | It only selects which visible process rows to mount. The full `visibleProcesses` array remains intact and scroll spacers preserve total scroll height. |
| Overscan rows | Adds rows above and below the viewport so mouse/keyboard scrolling does not reveal blank gaps during normal use. |
| Top/bottom spacer elements | Maintain the scrollbar range and let users reach the last process rows; the after scroll probe reached `FixtureProcess-241.exe` through `FixtureProcess-250.exe`. |
| Scroll reset on process-result signature changes | Search/filter result changes start at the top of the new result set, avoiding stale scroll offsets from a previous large list. |
| Bounded process-result signature | Tracks count, first row, last row, and refresh timestamp instead of depending on every row value, reducing avoidable signature churn without changing row data. |
| Probe-only data attributes | `data-windowed`, `data-process-total`, and `data-rendered-row-count` expose state for tests/probes only; they do not change UI behavior. |
| No new frontend dependency | The window calculator is local TypeScript and covered by Vitest. |

Target add/edit/delete, Settings save/read, and Reports page operations use separate state and bridge calls. The optimization only changes the rendering of process lookup results in the Targets page result panel.

## After Results

After probe: `after-final`, 2 runs.

| Scenario | DOM nodes | CDP nodes | Render/nav | Input dispatch | Input frame | Search refresh | Scroll | JS heap | Rendered rows | Total rows |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Reports normal list | 250 | 1666.0 | 46.20 ms | n/a | n/a | n/a | n/a | 13.00 MB | 0 | 0 |
| Targets small process list | 252 | 2157.0 | 44.45 ms | 1.55 ms | 19.10 ms | 491.80 ms | n/a | 17.04 MB | 0 initial / 1 filtered | 0 initial / 1 filtered |
| Targets large process list | 275 | 2736.0 | 42.45 ms | 1.50 ms | 32.05 ms | 499.70 ms | 21.30 ms | 24.07 MB | 19 | 250 |

Large-list scroll and search correctness:

| Check | Result |
| --- | --- |
| Initial large list is windowed | PASS; `processWindowed=true`, 19 rows rendered out of 250 |
| Scroll reaches end of large list | PASS; after scroll showed `FixtureProcess-241.exe` through `FixtureProcess-250.exe` |
| Large-list horizontal overflow | PASS; `overflowX=false` |
| Large-list search/filter | PASS; query `FixtureProcess-2` returned 51 rows and de-windowed because 51 is below the 250 threshold |
| Normal/small list path | PASS; small list remained `processWindowed=false` |

## Before / After Comparison

Primary comparison uses the 250-row Targets process result list.

| Metric | Before avg | After avg | Change |
| --- | ---: | ---: | ---: |
| DOM nodes | 1198 | 275 | 77.05% lower |
| Rendered process rows | 250 | 19 | 92.40% lower |
| CDP nodes | 4601.5 | 2736.0 | 40.54% lower |
| Render/nav time | 46.45 ms | 42.45 ms | 8.61% faster |
| Input dispatch | 4.50 ms | 1.50 ms | 66.67% lower |
| Input frame | 30.55 ms | 32.05 ms | 4.91% higher, within noisy frame timing |
| Scroll timing | 22.15 ms | 21.30 ms | 3.84% faster |
| Search refresh | 499.45 ms | 499.70 ms | unchanged; mock bridge delay dominates |
| JS heap used | 17.84 MB | 24.07 MB | higher/noisy CDP heap metric |
| Total process rows | 250 | 250 | unchanged |

Guardrail comparison:

| Scenario | Before DOM | After DOM | Before render/nav | After render/nav | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| Reports normal list | 250 | 250 | 46.15 ms | 46.20 ms | No DOM regression |
| Targets small process list | 252 | 252 | 46.80 ms | 44.45 ms | No DOM regression; still not windowed |

Memory note: the JS heap metric is noisy across CDP runs and increased in the after 250-row measurement. The clear win is mounted DOM/row count and input dispatch. I am not claiming a memory improvement for this round.

## Smoke Results

Smoke probe: `after-final`, 1 included smoke pass with target/settings/report workflows.

| Area | Result | Evidence |
| --- | --- | --- |
| Target add/edit/delete | PASS | start rows 4, final rows 4 after add/delete flow, add visible, edit visible, save button disabled after save |
| Settings save/read | PASS | saved value `1375`, read back from the UI |
| Reports page basic flow | PASS | start rows 3, final rows 3, operation status visible |
| Smoke overall | PASS | `smoke.success=true` |

Reports page basic flow included list rendering and report operation controls in the mock preview path. No backend report generation code was changed in this round.

## FPS / Voltage / VID

| Requirement | Result |
| --- | --- |
| FPS raw statistic semantics | Not changed; no reporting/statistics code modified |
| `bucketMs=1000` | Not changed |
| CPU Voltage / Vcore口径 | Not changed |
| CPU Core VID口径 | Not changed |
| Vcore/VID separation | Not touched; existing manifest/diagnostics/cpu-core/chart tests still pass |

## Verification Commands

| Command / check | Result |
| --- | --- |
| `node tools\Probe-FrontendLargeLists.js --label before --runs 2 --out artifacts\p2-frontend-large-list-optimization-20260601\before-fixed` | PASS; before baseline recorded for normal report list, small process list, and 250-row process list. |
| Bundled Node `tools\Probe-FrontendLargeLists.js --label after-final --runs 2 --include-smoke --out artifacts\p2-frontend-large-list-optimization-20260601\after-final` | PASS; after-final data recorded for normal report list, small process list, 250-row process list, and target/settings/reports smoke. |
| `node tools\Probe-FrontendLargeLists.js --label after --runs 2 --out artifacts\p2-frontend-large-list-optimization-20260601\after` | PASS; earlier after data recorded for normal report list, small process list, and 250-row process list. |
| In-app Browser probe | PASS; `processTotal=250`, `processRows=19`, `windowed=true`, `domNodes=332`, no console warnings/errors. Screenshot API timed out, CDP screenshots are present. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS; `npm ci` added 110 packages as normal verify behavior; typecheck PASS; Vitest 6 files / 61 tests PASS; Vite build PASS. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS; `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS; `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS; `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS; `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS; `chart-sampling-tests: PASS`. |
| `git diff --check` | PASS after report creation/update; existing LF-to-CRLF warnings only, no whitespace errors. |
| Residual process check | PASS; `NO_MATCHING_RESIDUAL_PROCESSES`. |

## Risk

Residual risk is low and limited to the process lookup result panel on the Targets page:

1. Large-list DOM is now virtualized, so any bug would most likely affect row visibility while scrolling. The scroll probe covered top and end-of-list visibility.
2. The row height is fixed at 62 px for window calculations. Current row styling matches the probe; future row-height changes should update the constant or add measurement.
3. JS heap did not improve in CDP metrics, so this round should be treated as a DOM/render/input optimization, not a memory optimization.
4. Search refresh latency is still governed by the mock bridge delay and was not optimized in this scope.

## Final Result

PASS.

The 250-row process lookup list now mounts 19 rows instead of 250 on initial large-list render, reducing DOM nodes by 77.05% and input dispatch by 66.67%, while normal report lists and small process lists keep the same DOM behavior. Target add/edit/delete, Settings save/read, and Reports page smoke checks passed. No packaging, install, real game launch, BF6 test, GitHub push, Release update, UI animation work, logging work, backend monitoring work, report generation work, or process chart interaction work was performed.
