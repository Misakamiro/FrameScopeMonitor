# FrameScope P2 UI Animation Performance Retest

Date: 2026-06-01

Final conclusion: PASS

Scope:专项复测 P2 frontend UI animation / transition / repaint cleanup only. This retest did not modify source code, did not fix bugs, did not handle log/backend monitoring/report generation/process chart interaction/large-list implementation, did not run product packaging, did not install FrameScope, did not launch a real game, did not test BF6, did not push GitHub, and did not update Release.

Note on current worktree scope: `git status --short` and `git diff --name-status` show the repository already has a broad dirty worktree with many pre-existing changes outside this P2 UI animation slice. The P2 implementation report itself declares the UI-motion scoped files as `App.tsx`, `main.tsx`, `PageTransition.tsx`, `Button.tsx`, `ToolbarButton.tsx`, `motion.ts`, `components.css`, `pages.css`, `uiMotionContract.test.ts`, and `Probe-FrontendUiAnimation.js`. This retest only added this report plus evidence artifacts under `docs/test-reports/2026-06-01-framescope-p2-ui-animation-performance-retest-evidence/`.

## Evidence

| Evidence | Path |
| --- | --- |
| UI animation probe JSON | `docs/test-reports/2026-06-01-framescope-p2-ui-animation-performance-retest-evidence/ui-animation/retest-frontend-ui-animation-probe.json` |
| Large-list guard JSON | `docs/test-reports/2026-06-01-framescope-p2-ui-animation-performance-retest-evidence/large-list-guard/p2-ui-animation-retest-guard-frontend-large-list-probe.json` |
| Ordinary screenshots | `ui-animation/ordinary-overview.png`, `ordinary-targets.png`, `ordinary-reports.png`, `ordinary-settings.png` |
| Reduced-motion screenshots | `ui-animation/reduced-overview.png`, `reduced-targets.png`, `reduced-reports.png`, `reduced-settings.png` |
| Smoke screenshots | `ui-animation/smoke-targets.png`, `smoke-settings.png`, `smoke-reports.png` |

`Probe-ReportHtmlLayout.js` was not run because this retest did not modify report HTML/layout and the goal explicitly excluded report generation/layout work.

## Direct Answers

| Question | Answer |
| --- | --- |
| 1. P2 UI animation optimization reproducible? | PASS. Static/runtime cleanup remains visible; performance deltas are small and noisy, consistent with a P2 cleanup rather than a major hotspot fix. |
| 2. `framer-motion` imports / `<motion.*>` / `whileTap` keep 0? | PASS. Static scan: `framerMotionImports=0`, `motionElements=0`, `whileTap=0`, `motionConfig=0`. |
| 3. box-shadow transition keeps 0? | PASS. Static `boxShadowTransitions=0`; runtime ordinary/reduced page average `boxShadowTransitioned=0`; interaction average also `0`. |
| 4. reduced-motion `transition-all` keeps 0? | PASS. Static `transitionAll=0`; reduced page average `transitionAll=0`; reduced interaction average `transitionAll=0`. |
| 5. ordinary / reduced page Task retest data? | Ordinary average `64.09 ms`; reduced average `61.60 ms`. See tables below. |
| 6. Ordinary motion visual normal? | PASS. Overview / Targets / Reports / Settings screenshots were not blank, did not show obvious overlap, and kept visible state feedback. |
| 7. Reduced-motion visual normal? | PASS. Targets / Settings screenshots were not blank or uncontrolled; focus/color/border/status feedback remained visible. |
| 8. target add/edit/delete normal? | PASS. Smoke: start rows `4`, final rows `4`, added target visible, edited target visible, save disabled after save. |
| 9. Settings save/read normal? | PASS. Smoke saved config and read back `global-telemetry-sample-interval=1375`. |
| 10. Reports basic flow normal? | PASS. Smoke refreshed/opened/opened directory/regenerated report; report rows stayed `3`; operation status visible. |
| 11. Large-list windowing not regressed? | PASS. 250-row fixture initially rendered `19/250` rows with `windowed=true`; after scroll rendered `10/250`; filtered 51-row list de-windowed to `51/51`, `windowed=false`. |
| 12. FPS / CPU Voltage / CPU Core VID unaffected? | PASS. `FrameScopeReportManifestTests.exe`, `FrameScopeSystemSamplerCpuCoreTests.exe`, and `chart-sampling-tests.js` passed; FPS raw semantics, `bucketMs=1000`, CPU Voltage/Vcore, CPU Core VID, and VID/Vcore isolation remain covered. |
| 13. Source modified in this retest? | NO. Only this report and evidence artifacts were added. Existing dirty source files were already present before the retest. |
| 14. Other optimization areas handled? | NO. No backend/log/report-generation/process-chart/large-list implementation work was done. |
| 15. Packaging/install/game/BF6/GitHub/Release? | NO. `Run-Frontend.ps1 verify` ran `npm ci` and Vite build as normal verification-script behavior only; no product package/install/release action was performed. |
| 16. All verification command results? | See command table below. |
| 17. Final conclusion? | PASS, with the worktree-scope caveat above. |

## Static Retest

From `tools/Probe-FrontendUiAnimation.js --runs 2 --include-smoke`:

| Metric | Retest |
| --- | ---: |
| scanned frontend runtime files | 11 |
| `framer-motion` imports | 0 |
| `<motion.*>` elements | 0 |
| `whileTap` | 0 |
| `MotionConfig` | 0 |
| static `transition: all` / `transition-property: all` | 0 |
| static transition declarations | 14 |
| static animation declarations | 8 |
| `@keyframes` | 4 |
| static box-shadow transition candidates | 0 |
| filter / backdrop-filter / blur | 0 / 0 / 0 |
| `prefers-reduced-motion` blocks | 3 |

## Runtime Page Data

Page samples: ordinary 2 runs x Overview / Targets / Reports / Settings = 8 samples; reduced-motion 2 runs x same pages = 8 samples.

| Mode | Samples | Avg nav ms | Avg Task ms | Avg box-shadow transitioned | Avg transition-all | Avg transitioned elems | Avg animated elems |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| ordinary | 8 | 43.45 | 64.09 | 0.00 | 0.00 | 23.25 | 0.63 |
| reduced | 8 | 38.57 | 61.60 | 0.00 | 0.00 | 20.25 | 0.00 |

Per-page Task samples:

| Mode | Run | Overview | Targets | Reports | Settings |
| --- | ---: | ---: | ---: | ---: | ---: |
| ordinary | 1 | 79.260 | 63.579 | 31.732 | 92.052 |
| ordinary | 2 | 113.019 | 34.843 | 35.827 | 62.395 |
| reduced | 1 | 110.118 | 39.870 | 33.530 | 61.561 |
| reduced | 2 | 99.256 | 39.751 | 36.874 | 71.861 |

Interpretation: retest averages are close to the implementation report after-values (`ordinary 62.38 ms`, `reduced 60.09 ms`) but show expected browser/CDP measurement noise. The durable reproduction signal is the zeroed runtime/static cleanup metrics: no Framer runtime path, no box-shadow transitions, and no reduced-motion transition-all.

## Runtime Interaction Data

Interactions covered Targets refresh, Reports menu open, and Settings save in both motion modes.

| Mode | Samples | Avg interaction ms | Avg Task delta ms | Avg box-shadow transitioned | Avg transition-all |
| --- | ---: | ---: | ---: | ---: | ---: |
| ordinary | 6 | 26.10 | 4.40 | 0.00 | 0.00 |
| reduced | 6 | 22.77 | 4.72 | 0.00 | 0.00 |

Button / toolbar feedback check: Vitest `uiMotionContract.test.ts` passed, including the CSS button active/reduced-motion contract. Runtime smoke clicked ordinary buttons and toolbar-style controls for Targets refresh, Reports menu/open-directory/regenerate, and Settings save. Manual screenshot inspection confirmed visible button states and focus/selection affordances remained present.

## Functional Smoke

| Area | Result | Evidence |
| --- | --- | --- |
| Target add/edit/delete | PASS | `startRows=4`, `finalRows=4`, `addVisible=true`, `editVisible=true`, `saveDisabled=true` |
| Settings save/read | PASS | `saved=true`, read-back value `1375` |
| Reports basic flow | PASS | `startRows=3`, `finalRows=3`, `operationStatusVisible=true` |
| Large-list 250-row guard | PASS | initial `19/250`, `windowed=true`; after scroll `10/250`; filtered `51/51`, `windowed=false` |

## Visual Check

| Mode | Pages inspected | Result |
| --- | --- | --- |
| ordinary | Overview, Targets, Reports, Settings | PASS. Pages rendered with stable layout, visible controls, no blank screen, no obvious text overlap. |
| reduced-motion | Targets, Settings plus captured Overview/Reports | PASS. Pages rendered without runaway animation, blank state, or missing interaction feedback. |

## Verification Commands

| Command | Result |
| --- | --- |
| `git status --short` | Ran. Existing broad dirty worktree recorded; retest added evidence/report only. |
| Bundled Node `.\tools\Probe-FrontendUiAnimation.js --label retest --runs 2 --include-smoke --out .\docs\test-reports\2026-06-01-framescope-p2-ui-animation-performance-retest-evidence\ui-animation` | PASS. Output JSON written, `results=16`, `smokeSuccess=true`. |
| Bundled Node `.\tools\Probe-FrontendLargeLists.js --label p2-ui-animation-retest-guard --runs 2 --include-smoke --out .\docs\test-reports\2026-06-01-framescope-p2-ui-animation-performance-retest-evidence\large-list-guard` | PASS. Output JSON written, `results=6`, `smokeSuccess=true`. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS. `npm ci` added 110 packages as normal verify behavior; typecheck PASS; Vitest 6 files / 62 tests PASS; Vite build PASS. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS. `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS. Final line `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS. `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS. `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS. `chart-sampling-tests: PASS`. |
| target/settings/report smoke | PASS. Covered by UI animation probe and large-list guard probe. |
| large-list guard | PASS. 250-row windowing remained active. |
| `git diff --check` | PASS. Exit code 0; only existing LF-to-CRLF warnings were printed, no whitespace errors. |
| residual process check | PASS. `NO_MATCHING_RESIDUAL_PROCESSES`. |

## Guardrail Summary

FPS raw statistics and `bucketMs=1000`: covered by report manifest/chart sampling tests. CPU Voltage / Vcore and CPU Core VID remain separate: covered by manifest, diagnostics, sampler CPU-core tests, and chart sampling tests. VID/Vcore bidirectional isolation remains covered: VID-only data does not populate CPU Voltage/Vcore, and Vcore/SOC/package voltage does not populate CPU Core VID.

## Retest Boundaries

No source bug fixes were made. No report HTML/layout changes were made. No process graph interaction work was done. No backend/logging/monitoring changes were done. No new package installation for FrameScope, product packaging, installer generation, real game launch, BF6 validation, GitHub push, or Release update was performed.
