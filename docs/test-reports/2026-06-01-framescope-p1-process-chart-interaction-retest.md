# FrameScope P1 Process Chart Interaction Retest

Date: 2026-06-01

Final conclusion: PARTIAL

PARTIAL is only because the current overall working tree diff is not limited to the three P1 process interaction files. The P1 process interaction optimization itself is reproducible on the large historical report, and the required interaction, chart, layout, and test commands passed.

## Scope

This round was retest-only.

Not done:

| Area | Result |
| --- | --- |
| Source fixes | Not done |
| Backend monitoring optimization | Not done |
| Frontend animation optimization | Not done |
| Log optimization | Not done |
| `build.ps1` | Not run |
| Installer / setup packaging | Not run |
| FrameScope install/update | Not run |
| Real game launch | Not run |
| BF6 test | Not run |
| GitHub push | Not run |
| Release update | Not run |

Allowed writes produced by this retest:

| Artifact | Path |
| --- | --- |
| Process interaction probe JSON | `docs\test-reports\2026-06-01-framescope-p1-process-chart-interaction-retest-evidence\process-after-rerun\after-rerun-process-interaction.json` |
| Layout probe JSON and screenshots | `docs\test-reports\2026-06-01-framescope-p1-process-chart-interaction-retest-evidence\layout-probe\` |
| Chart nonblank probe JSON | `docs\test-reports\2026-06-01-framescope-p1-process-chart-interaction-retest-evidence\chart-nonblank-probe\chart-nonblank-probe.json` |

## Diff Scope Check

Implementation report claimed the P1 process interaction change set was centered on:

| File | Status from implementation report |
| --- | --- |
| `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs` | Process chart interaction optimization |
| `tests\chart-sampling-tests.js` | Regression coverage |
| `tools\Probe-ReportProcessInteraction.js` | Repeatable interaction probe |

Current working tree check:

| Check | Result |
| --- | --- |
| `git status --short` | Dirty working tree with many pre-existing tracked and untracked changes |
| `git diff --name-only` tracked count | 80 tracked paths |
| Tracked paths matching expected P1 files | `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`, `tests/chart-sampling-tests.js` |
| Tracked paths outside expected P1 files | 78 |
| `tools/Probe-ReportProcessInteraction.js` | Present as untracked file |

Scope conclusion: current overall git diff cannot be confirmed as only the P1 process interaction files. This retest did not revert, fix, or alter those existing changes.

## Dataset

Retest target:

`artifacts\p1-process-interaction-optimization-20260601\after-history-large\charts\framescope-interactive-report.html`

Baseline values came from:

`docs\implementation-reports\2026-06-01-framescope-p1-process-chart-interaction-optimization-report.md`

Large report scale:

| Metric | Value |
| --- | ---: |
| Process names | 119 |
| Process time samples | 17,714 |
| FPS bucket | 1000 ms |
| Process codec | `rle-v1` |

## Process Interaction Retest

Command:

`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tools\Probe-ReportProcessInteraction.js --report .\artifacts\p1-process-interaction-optimization-20260601\after-history-large\charts\framescope-interactive-report.html --out .\docs\test-reports\2026-06-01-framescope-p1-process-chart-interaction-retest-evidence\process-after-rerun --label after-rerun --runs 2`

Result: PASS, exit 0.

| Run | Process tab draw | Search max draw | Search input blocking | Hover | DOM nodes | Process names | Process samples | Max searched drawn points |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 9.6 ms | 6.7 ms | 0.1 ms | 0.4 ms | 319 | 119 | 17,714 | 9,424 |
| 2 | 8.1 ms | 7.4 ms | 0.1 ms | 0.2 ms | 319 | 119 | 17,714 | 9,424 |
| Average | 8.85 ms | 7.05 ms | 0.10 ms | 0.30 ms | 319 | 119 | 17,714 | 9,424 |

Baseline comparison:

| Metric | Baseline avg | Retest avg | Result |
| --- | ---: | ---: | --- |
| Process tab draw | 13.15 ms | 8.85 ms | 32.70% faster |
| Process search draw | 10.25 ms | 7.05 ms | 31.22% faster |
| Search input blocking | 10.35 ms | 0.10 ms | 99.03% lower |
| Tooltip / hover | 0.25 ms | 0.30 ms | Still sub-ms; run-to-run variance, functionally normal |
| DOM nodes | 319 | 319 | No growth |
| Max searched drawn points | 15,390 | 9,424 | 38.77% lower |
| Process names | 119 | 119 | Preserved |
| Process time samples | 17,714 | 17,714 | Preserved |

Reproducibility conclusion: P1 process search draw and input blocking improvements are reproducible. Process data counts did not drop, and DOM size did not grow.

## Interaction Correctness

| Check | Result | Evidence |
| --- | --- | --- |
| Process tab opens | PASS | Probe clicked `data-view="process"` and drew the chart |
| Top N default | PASS | Empty search returned the expected Top 10 |
| Search normal | PASS | `e` and `a` each returned 24 rendered series |
| Empty search | PASS | 10 Top N results |
| No-result search | PASS | `__NO_PROCESS_MATCH__` returned 0 series |
| Case-insensitive search | PASS | `edge` and `EDGE` returned the same 2 results |
| Uppercase search | PASS | `CODEX` returned 3 Codex-related results |
| Tooltip / hover | PASS | Tooltip visible, text length 156 |
| Process chart nonblank | PASS | `chartNonBlank=true` |

## Other Chart Regression Checks

`chart-sampling-tests.js` result: PASS.

Relevant preserved contracts:

| Check | Result |
| --- | --- |
| FPS chart remains GamePP style | PASS; title `FPS GamePP chart`, blue area/reference-line contract covered by chart sampling test |
| FPS `bucketMs=1000` | PASS |
| FPS raw PresentMon stats semantics | PASS via `FrameScopeReportManifestTests.exe`; raw frame stats and one-second display bucket checks passed |
| CPU Voltage / Vcore uses `DATA.cpuVoltage` | PASS via `chart-sampling-tests.js` |
| CPU Core VID uses `DATA.cpuVid` | PASS via `chart-sampling-tests.js` |
| VID/Vcore bidirectional isolation | PASS via `chart-sampling-tests.js` and `FrameScopeReportManifestTests.exe` |
| GPU chart nonblank | PASS; `performance-gpu-clock` drawn 1,023 points, `system-gpu-usage` drawn 1,553 points |
| IO chart nonblank | PASS; `io-disk-net` drawn 3,198 points |
| Temperature chart nonblank | PASS; `io-temperature` drawn 1,349 points |

Historical artifact note: this large report has no CPU Voltage or CPU Core VID samples, so those two chart tabs correctly show unavailable state in the runtime probe. Data-family separation is covered by the source-level chart sampling test and manifest tests.

## Layout Probe

Command:

`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tools\Probe-ReportHtmlLayout.js --report .\artifacts\p1-process-interaction-optimization-20260601\after-history-large\charts\framescope-interactive-report.html --diagnostic .\artifacts\p1-process-interaction-optimization-20260601\after-history-large\charts\framescope-interactive-report.html --out .\docs\test-reports\2026-06-01-framescope-p1-process-chart-interaction-retest-evidence\layout-probe`

Result: PASS, exit 0.

| Metric | Value |
| --- | ---: |
| Scenarios | 23 |
| `allNoOverflow` | `true` |
| Overflow scenarios | 0 |
| FPS dropdown options | `all`, `avg`, `low1`, `low01` |
| Raw/min/instant option exposed | No |

## Verification Commands

| Command | Result |
| --- | --- |
| `git status --short` | Ran; dirty working tree with many existing changes and this retest evidence directory |
| Bundled Node `tools\Probe-ReportProcessInteraction.js` after retest, 2 runs | PASS; output JSON listed above |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS; installed 110 npm packages as normal verify behavior; typecheck PASS; Vitest 5 files / 57 tests PASS; Vite build PASS |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS; `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS; output ended with `FrameScopeReportManifestTests: PASS` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS; `FrameScopeDiagnosticsTests: PASS` |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS; `FrameScopeSystemSamplerCpuCoreTests: PASS` |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS; `chart-sampling-tests: PASS` |
| Bundled Node `tools\Probe-ReportHtmlLayout.js` | PASS; `allNoOverflow=true`, 23 scenarios |
| Additional chart nonblank probe | PASS; FPS/GPU/IO/temperature charts had drawable data |
| `git diff --check` | PASS, exit 0; output contained existing LF-to-CRLF warnings only, no whitespace errors |
| Residual process check | PASS; `NO_MATCHING_RESIDUAL_PROCESSES` |

## Direct Answers

| Question | Answer |
| --- | --- |
| 1. P1 process interaction optimization reproducible? | Yes. Search draw and input blocking remain clearly better than baseline. |
| 2. After retest data? | Avg tab draw 8.85 ms, search draw 7.05 ms, input blocking 0.10 ms, hover 0.30 ms. |
| 3. Compared with baseline? | Search draw 31.22% faster; input blocking 99.03% lower; tab draw 32.70% faster. |
| 4. Process names kept 119? | Yes, 119 in both runs. |
| 5. Process time samples kept 17,714? | Yes, 17,714 in both runs. |
| 6. Search correctness passed? | Yes. Normal, empty, no-result, uppercase, and case-insensitive checks passed. |
| 7. Tooltip / hover normal? | Yes. Tooltip visible and populated; sub-ms hover. |
| 8. FPS / CPU Voltage / CPU Core VID unaffected? | Yes by tests. FPS GamePP/bucket/raw stats passed; `DATA.cpuVoltage` and `DATA.cpuVid` separation passed. |
| 9. Layout `allNoOverflow=true`? | Yes. |
| 10. Any source modification by this retest? | No source edits were made in this retest. Current working tree already contains many source changes. |
| 11. Other optimization areas handled? | No. Backend monitoring, frontend animation, and log optimization were not handled. |
| 12. Packaging/install/game/BF6/GitHub/Release? | No packaging, no install, no real game launch, no BF6 test, no GitHub push, no Release update. |
| 13. All verification command results? | Listed in Verification Commands. |
| 14. Final conclusion? | PARTIAL overall because current git diff scope is broader than the requested P1 process files; P1 process retest itself PASS. |
