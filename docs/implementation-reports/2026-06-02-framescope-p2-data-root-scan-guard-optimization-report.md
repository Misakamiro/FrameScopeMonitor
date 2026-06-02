# FrameScope P2 Data Root Scan Guard Optimization Report

Date: 2026-06-02

Final conclusion: PASS

## 1. Scope

This round only optimized data root / report / status recursive scan protection. It did not handle frontend large lists, UI animation, logging rate limiter / tail trim, report generation, process chart interaction, backend monitoring sampling, build artifact sync, installation/update validation, FPS semantics, CPU Voltage / Vcore semantics, or CPU Core VID semantics.

Hard boundary result:

| Boundary | Result |
| --- | --- |
| Packaging / installer build | Not run |
| FrameScope install | Not run |
| Real game launch | Not run |
| BF6 test | Not run |
| GitHub push | Not run |
| Release update | Not run |
| `build.ps1` | Not run |
| User reports/runs deletion | Not done |

`tools\Run-Frontend.ps1 verify` restored frontend packages and regenerated frontend `dist` as normal verification-script behavior. This was validation only, not product packaging, installer generation, product install, or Release work.

## 2. Before Baseline

Evidence:

- Raw baseline: `artifacts\p2-data-root-scan-guard-20260602\baseline\baseline-scan-metrics.json`
- Normalized before/after comparison: `artifacts\p2-data-root-scan-guard-20260602\comparison\comparison-scan-metrics.json`

Synthetic roots:

- Small data root: normal runs/reports, old status/report, damaged `status.json`, shallow noise.
- Large/noisy data root: 46 report HTML files, 71 `status.json` files, 25 damaged JSON files, `node_modules`, `dist`, `cache`, `bin`, `obj`, `tmp`, and a deep unrelated directory.

Raw pre-change recursive shape:

| Root | Directories scanned | Files scanned | Status files | Report HTML files | Damaged JSON |
| --- | ---: | ---: | ---: | ---: | ---: |
| Small | 19 | 58 | 4 | 3 | 1 |
| Large/noisy | 2374 | 25308 | 71 | 46 | 25 |

Normalized baseline averages, 2 runs:

| Root | Status scan elapsed | Reports refresh elapsed | JSON parsed | Working set |
| --- | ---: | ---: | ---: | ---: |
| Small | 8.076 ms | 2.771 ms | 4 | 28.34 MB |
| Large/noisy | 100.713 ms | 103.450 ms | 71 | 34.34 MB |

The first raw baseline also measured a conservative Reports refresh path at about 207-209 ms on the large/noisy root because the harness generated synthetic history inside the timed section. The normalized comparison moved history preparation outside the timed refresh to better match the actual Reports page request.

## 3. Hotspots Found

| Hotspot | Evidence | Risk |
| --- | --- | --- |
| Full recursive status scan | `FrameScopeReportProgress.FindLatestEffectiveStatus` used `Directory.GetFiles(dataRoot, "status.json", SearchOption.AllDirectories)` | Large data roots force deep traversal and parse damaged/unrelated status files. |
| Full recursive report fallback | `FrameScopeWebBridge.LoadReports` recursively searched `framescope-interactive-report.html` | Reports refresh can walk dependency/build/cache trees before returning the list. |
| Diagnostics latest run scan | `FrameScopeDiagnostics.FindLatestRun` recursively searched all `status.json` | Diagnostics generation can pick or scan through unrelated deep paths. |
| No skip rules | Synthetic `node_modules/dist/cache/bin/obj/tmp` dominated file and directory count | High IO and UI refresh latency when data root grows or points at a noisy parent. |
| Duplicate status reads | Reports history + fallback could validate the same run twice | Avoidable JSON parse and file IO on refresh. |
| Damaged JSON handled late | Damaged JSON did not crash, but was still reached by unbounded recursion | Safe behavior existed, but traversal was too broad. |

## 4. Changes

Modified files:

| File | Change |
| --- | --- |
| `src\core\FrameScopeReportProgress.cs` | Added `FrameScopeDataRootScanner`, scan options/stats, expected run-layout probing, guarded fallback recursion, skip rules, depth/count/time guards, reparse-point skip, and used it for latest progress status. |
| `src\app\FrameScopeWebBridge.Reports.cs` | Replaced unbounded report HTML fallback recursion with guarded scanner; added per-refresh status JSON cache; kept history-first behavior. |
| `src\diagnostics\FrameScopeDiagnostics.IO.cs` | Replaced latest-run full recursive status discovery with guarded scanner. |
| `tests\FrameScopeReportProgressTests.cs` | Added coverage for noisy directory skip, valid progress selection, damaged/noisy path resilience, and scanner stats. |
| `tests\FrameScopeWebBridgeTests.cs` | Added Reports flow coverage that keeps history and expected-layout reports while skipping `node_modules` fallback noise. Existing open/open directory/regenerate smoke remains passing. |
| `tests\FrameScopeDiagnosticsTests.cs` | Added diagnostics latest-run coverage to ensure `node_modules` status does not win over a valid run. |
| `tests\Build-FrameScopeTests.ps1` | Added `FrameScopeReportProgress.cs` to test builds that now compile scanner consumers. |
| `docs\implementation-reports\2026-06-02-framescope-p2-data-root-scan-guard-optimization-report.md` | This report. |

## 5. Skip And Guard Rules

| Rule | Why safe |
| --- | --- |
| Expected run-layout scan first | Directly checks normal FrameScope layout (`dataRoot\game\run\status.json` and `dataRoot\game\run\charts\framescope-interactive-report.html`) before fallback recursion, so normal valid reports remain discoverable. |
| History remains first | Reports already recorded in `framescope-history.jsonl` are validated and listed before fallback discovery, so previously visible valid reports do not disappear because of fallback skip rules. |
| Skip names: `.git`, `.hg`, `.svn`, `.vs`, `.vscode`, `.cache`, `.pytest_cache`, `__pycache__`, `node_modules`, `dist`, `build`, `out`, `coverage`, `test-results`, `playwright-report`, `cache`, `tmp`, `temp`, `bin`, `obj` | These are dependency, build, cache, VCS, or test-output directories, not FrameScope run storage. They were the synthetic high-IO sources. |
| Skip reparse points | Avoids junction/symlink loops and inaccessible external targets. Edge smoke confirmed reparse paths are skipped without failing the scan. |
| Max depth = 6 | Covers normal `dataRoot\game\run\charts\report` and limited migration variants, while preventing very deep unrelated trees from dominating scan time. |
| Max directories = 4096, max files = 20000, max matches = 5000, max elapsed = 1500 ms | Prevents long UI-blocking scans on pathological roots. Limits are high enough for ordinary FrameScope histories and still bounded. |
| Per-directory exception handling | Damaged or inaccessible paths are skipped and counted; they do not fail the whole scan. |
| Status JSON cache in Reports refresh | Same run status is parsed once per refresh even if found via history and fallback. It does not change status contents or report metadata. |

Risk note: a report stored only under an explicitly skipped directory and not present in history is treated as fallback noise. Normal FrameScope generated reports under the expected data root layout are still found, and history entries remain honored.

## 6. After Results

After evidence:

- `artifacts\p2-data-root-scan-guard-20260602\comparison\comparison-scan-metrics.json`
- Edge cases: `artifacts\p2-data-root-scan-guard-20260602\edge-cases\edge-case-smoke.json`

After averages, 2 runs:

| Root | Status scan elapsed | Reports refresh elapsed | Directories scanned | Files scanned | JSON parsed | Working set | Reports found |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Small | 1.757 ms | 7.307 ms | 9 | 19 | 4 | 28.92 MB | 3 |
| Large/noisy | 11.161 ms | 16.465 ms | 132 | 256 | 71 | 34.36 MB | 46 |

Skip evidence on large/noisy root:

| Skip reason | Count |
| --- | ---: |
| `name:node_modules` | 2 |
| `name:dist` | 2 |
| `name:cache` | 2 |
| `name:bin` | 2 |
| `name:obj` | 2 |
| `name:tmp` | 2 |

Deep unrelated directory protection: `depthLimitHits=1` on the large/noisy root.

Edge-case smoke after adding a reparse/inaccessible junction:

| Metric | Result |
| --- | ---: |
| Status matches | 71 |
| Damaged JSON skipped | 25 |
| Reparse directories skipped | 2 |
| Enumeration errors | 0 |
| Depth limit hits | 1 |

## 7. Before / After Comparison

| Root | Metric | Before avg | After avg | Change |
| --- | --- | ---: | ---: | ---: |
| Small | Status scan elapsed | 8.076 ms | 1.757 ms | -78.2% |
| Small | Reports refresh elapsed | 2.771 ms | 7.307 ms | +4.536 ms cold/JIT-sensitive; run 2 stayed 1.254 -> 1.618 ms |
| Small | Directories scanned | 19 | 9 | -52.6% |
| Small | Files scanned | 58 | 19 | -67.2% |
| Small | JSON parsed | 4 | 4 | unchanged |
| Small | Working set | 28.34 MB | 28.92 MB | +0.58 MB |
| Small | Reports found | 3 | 3 | unchanged |
| Large/noisy | Status scan elapsed | 100.713 ms | 11.161 ms | -88.9% |
| Large/noisy | Reports refresh elapsed | 103.450 ms | 16.465 ms | -84.1% |
| Large/noisy | Directories scanned | 2374 | 132 | -94.4% |
| Large/noisy | Files scanned | 25308 | 256 | -99.0% |
| Large/noisy | JSON parsed | 71 | 71 | unchanged in this fixture; all status files were in run-like paths or damaged JSON fixtures |
| Large/noisy | Working set | 34.34 MB | 34.36 MB | effectively unchanged |
| Large/noisy | Reports found | 46 | 46 | unchanged |

## 8. Correctness Results

| Requirement | Result |
| --- | --- |
| Effective reports still discovered | PASS. Small stayed 3/3, large/noisy stayed 46/46. |
| Effective run/status still read | PASS. `FrameScopeReportProgressTests` and diagnostics latest-run test pass. |
| Damaged JSON does not fail scan | PASS. Synthetic damaged JSON count was 25 and scans completed. |
| Deep unrelated dirs do not drag scan | PASS. `depthLimitHits=1`, large/noisy files scanned dropped from 25308 to 256. |
| Inaccessible/reparse path handling | PASS. Reparse smoke skipped external junctions and reported `enumerationErrors=0`. |
| Reports refresh/list response | PASS. `reports.list` smoke and WebBridge tests pass; large/noisy refresh average 16.465 ms after. |
| Report open | PASS. `FrameScopeWebBridgeTests.exe` covers `reports.open` by validated reportId. |
| Open report directory | PASS. `FrameScopeWebBridgeTests.exe` covers `reports.openDirectory`. |
| Regenerate report | PASS. `FrameScopeWebBridgeTests.exe` covers `reports.regenerate` accepted + completed event. |
| Settings save/read | PASS. `FrameScopeWebBridgeTests.exe` covers config save/get and targets save preserving theme/window/CPU telemetry fields. |

## 9. FPS / CPU Voltage / CPU Core VID Guard

This round did not touch report generation, chart data, sampling, FPS, CPU Voltage / Vcore, or CPU Core VID code paths.

Guard evidence:

- `FrameScopeReportManifestTests.exe`: PASS.
- `FrameScopeDiagnosticsTests.exe`: PASS.
- `FrameScopeSystemSamplerCpuCoreTests.exe`: PASS.
- Bundled Node `tests\chart-sampling-tests.js`: PASS.

Result: FPS, CPU Voltage / Vcore, and CPU Core VID data links were not changed by this data-root scan round.

## 10. Verification Commands

| Command / check | Result | Evidence |
| --- | --- | --- |
| Baseline/after data-root scan comparison, small and large, 2 runs each | PASS | `comparison\comparison-scan-metrics.json` |
| Raw before baseline synthetic generation and scan metrics | PASS | `baseline\baseline-scan-metrics.json` |
| Synthetic small data root correctness | PASS | 3 reports before/after, valid status found |
| Synthetic large/noisy data root correctness | PASS | 46 reports before/after, valid status found |
| Damaged JSON / inaccessible path / deep unrelated dir | PASS | `edge-cases\edge-case-smoke.json` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS. Typecheck PASS; Vitest 6 files / 62 tests PASS; Vite build PASS | `verification\command-logs\01-run-frontend-verify.log` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS. `FrameScope tests rebuilt.` | `verification\command-logs\02-build-framescope-tests.log` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | `verification\command-logs\03-FrameScopeReportManifestTests.log` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS | `verification\command-logs\04-FrameScopeDiagnosticsTests.log` |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS | `verification\command-logs\05-FrameScopeSystemSamplerCpuCoreTests.log` |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS | `verification\command-logs\06-FrameScopeWebBridgeTests.log` |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS | `verification\command-logs\07-FrameScopeReportProgressTests.log` |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS. `chart-sampling-tests: PASS` | `verification\command-logs\08-chart-sampling-tests-bundled-node.log` |
| Reports flow smoke: refresh/open/open directory/regenerate | PASS via `FrameScopeWebBridgeTests.exe` | `verification\command-logs\06-FrameScopeWebBridgeTests.log` |
| Settings save/read smoke | PASS via `FrameScopeWebBridgeTests.exe` | `verification\command-logs\06-FrameScopeWebBridgeTests.log` |
| `git diff --check` | PASS, exit 0. Only existing LF/CRLF warnings, no whitespace errors | `verification\command-logs\09-git-diff-check.log` |
| Residual process check | PASS. `NO_MATCHING_RESIDUAL_PROCESSES` | `verification\command-logs\10-residual-process-check.log` |

Bundled Node path used:

`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`

## 11. TDD Note

RED was verified first:

`tests\FrameScopeReportProgressTests.cs` failed to compile because `FrameScopeDataRootScanStats` / `FrameScopeDataRootScanner` did not exist.

After implementation, `Build-FrameScopeTests.ps1`, `FrameScopeReportProgressTests.exe`, `FrameScopeWebBridgeTests.exe`, and `FrameScopeDiagnosticsTests.exe` passed.

## 12. Final Conclusion

PASS.

The large/noisy synthetic data root now avoids deep dependency/build/cache recursion, preserves valid report discovery, keeps Reports open/open-directory/regenerate behavior working, keeps Settings save/read working, and does not affect FPS, CPU Voltage / Vcore, or CPU Core VID data semantics.
