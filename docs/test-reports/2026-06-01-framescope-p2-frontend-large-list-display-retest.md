# FrameScope P2 Frontend Large List Display Retest

Date: 2026-06-01

Final conclusion: PARTIAL

Why PARTIAL: the P2 large-list optimization itself is reproducible and the performance, interaction, visual, and required verification commands passed. The only caveat is scope hygiene: `git status --short` / aggregate `git diff` are not isolated to the P2 files in the current worktree. This retest did not modify source code; it only added this report and evidence artifacts under `docs/test-reports`.

## Scope Check

Implementation report read:

- `docs/implementation-reports/2026-06-01-framescope-p2-frontend-large-list-display-optimization-report.md`
- It states the P2 implementation touched only:
  - `src/frontend/src/pages/TargetsPage.tsx`
  - `src/frontend/src/pages/pages.css`
  - `src/frontend/src/utils/virtualListWindow.ts`
  - `src/frontend/src/utils/virtualListWindow.test.ts`
  - `src/frontend/src/uiInteractionContract.test.ts`
  - `tools/Probe-FrontendLargeLists.js`
  - the implementation report itself

Current git status / diff check:

- `git status --short` was run.
- Current worktree is broadly dirty: summary at retest time was 202 status lines, 80 tracked modified/deleted files, and 122 untracked entries.
- Targeted P2 status for the requested files was:
  - `M src/frontend/src/pages/TargetsPage.tsx`
  - `M src/frontend/src/pages/pages.css`
  - `M src/frontend/src/uiInteractionContract.test.ts`
  - `?? src/frontend/src/utils/virtualListWindow.ts`
  - `?? src/frontend/src/utils/virtualListWindow.test.ts`
  - `?? tools/Probe-FrontendLargeLists.js`
- Therefore the implementation report's P2 scope is narrow, but the current aggregate worktree diff is not limited to those files.
- This retest added only `docs/test-reports/2026-06-01-framescope-p2-frontend-large-list-display-retest.md` and `docs/test-reports/2026-06-01-framescope-p2-frontend-large-list-display-retest-evidence/`.

## Performance Retest

Method:

- Used bundled Node: `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`
- Ran `tools/Probe-FrontendLargeLists.js` twice as independent invocations.
- Each invocation used `--runs 2 --include-smoke`, so the performance summary below has 4 samples per scenario.
- Synthetic 250-row process list used `visualFixture=many-results`.

Evidence:

- `docs/test-reports/2026-06-01-framescope-p2-frontend-large-list-display-retest-evidence/perf-run-a/retest-a-frontend-large-list-probe.json`
- `docs/test-reports/2026-06-01-framescope-p2-frontend-large-list-display-retest-evidence/perf-run-b/retest-b-frontend-large-list-probe.json`

Average results:

| Scenario | Samples | Mounted process rows | Total rows | Windowed | DOM nodes | CDP nodes | Render/nav | Input dispatch | Search refresh | Scroll | JS heap |
| --- | ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Reports normal list | 4 | 0 | 0 | false | 250 | 1666 | 48.90 ms | n/a | n/a | n/a | 14.03 MB |
| Targets small process list | 4 | 0 initial / 1 filtered | 0 initial / 1 filtered | false | 252 | 2157 | 48.05 ms | 1.53 ms | 499.95 ms | n/a | 17.12 MB |
| Targets large process list, 250 rows | 4 | 19 | 250 | true | 275 | 2736 | 45.70 ms | 1.35 ms | 498.85 ms | 21.33 ms | 22.20 MB |

Baseline comparison from the implementation report:

| Metric | Baseline | Retest avg | Result |
| --- | ---: | ---: | --- |
| Initial mounted process rows | 250 | 19 | 92.40% lower; reproducible |
| DOM nodes | 1198 | 275 | 77.05% lower; reproducible |
| CDP nodes | 4601.5 | 2736 | 40.54% lower; reproducible |
| Render/nav | 46.45 ms | 45.70 ms | Slightly faster in this retest |
| Input dispatch | 4.50 ms | 1.35 ms | 70.00% lower; reproducible |
| JS heap | 17.84 MB | 22.20 MB | Still noisy/higher; no memory benefit claimed |

Direct answers:

1. P2 frontend large-list optimization is reproducible.
2. Large-list mounted rows are significantly lower: 250 to 19 initially.
3. DOM nodes and CDP nodes are lower than baseline: 1198 to 275 DOM, 4601.5 to 2736 CDP.
4. Retest render/nav average is 45.70 ms; input dispatch average is 1.35 ms.
5. JS heap remains a residual/noisy metric. It increased versus baseline and must not be treated as a benefit.
6. Small list remains non-windowed; after filter it rendered 1 of 1 result and `processWindowed=false`.
7. Reports normal list remains stable at 3 rows and 250 DOM nodes.

## Interaction Retest

Large-list interaction:

- The 250-row list is scrollable.
- After scrolling to the bottom, the probe reached `FixtureProcess-241.exe` through `FixtureProcess-250.exe`.
- After-scroll mounted rows were 10, with `data-windowed=true`, `data-process-total=250`, and no horizontal overflow.
- Search/filter worked: query `FixtureProcess-2` returned 51 rows and de-windowed because the filtered result is below the 250-row threshold.

Target / Settings / Reports smoke:

| Area | Result | Evidence |
| --- | --- | --- |
| Target add/edit/delete | PASS | Both smoke runs: start rows 4, final rows 4, add visible, edit visible, save disabled after save |
| Settings save/read | PASS | Both smoke runs saved and read back `1375` |
| Reports basic flow | PASS | Both smoke runs: start rows 3, final rows 3, operation status visible |

Smoke evidence:

- `docs/test-reports/2026-06-01-framescope-p2-frontend-large-list-display-retest-evidence/perf-run-a/smoke-targets.png`
- `docs/test-reports/2026-06-01-framescope-p2-frontend-large-list-display-retest-evidence/perf-run-a/smoke-settings.png`
- `docs/test-reports/2026-06-01-framescope-p2-frontend-large-list-display-retest-evidence/perf-run-a/smoke-reports.png`
- Matching smoke screenshots also exist under `perf-run-b/`.

Direct answers:

8. Large-list scroll/search/filter are normal.
9. Target add/edit/delete is normal.
10. Settings save/read is normal.
11. Reports basic flow is normal.

## Visual Retest

Screenshots inspected:

| View | Evidence |
| --- | --- |
| Ordinary Reports list | `docs/test-reports/2026-06-01-framescope-p2-frontend-large-list-display-retest-evidence/perf-run-a/reports-normal-run1.png` |
| Large process list, top | `docs/test-reports/2026-06-01-framescope-p2-frontend-large-list-display-retest-evidence/visual-scroll/targets-large-process-list-top.png` |
| Large process list, after internal scroll | `docs/test-reports/2026-06-01-framescope-p2-frontend-large-list-display-retest-evidence/visual-scroll/targets-large-process-list-after-scroll.png` |

Visual result:

- No obvious text overlap.
- No button misalignment.
- No row-height jump observed.
- No blank list.
- No abnormal large-list scroll area.
- No horizontal overflow in probe metrics.

Direct answer:

12. Screenshot/visual check shows no obvious issue in the scoped views.

## Regression Guardrails

FPS / CPU Voltage / CPU Core VID:

- FPS raw statistics semantics were not modified in this retest.
- `chart-sampling-tests.js` passed, covering chart sampling guardrails including `bucketMs=1000`.
- `FrameScopeReportManifestTests.exe`, `FrameScopeDiagnosticsTests.exe`, and `FrameScopeSystemSamplerCpuCoreTests.exe` passed.
- CPU Voltage / Vcore and CPU Core VID separation remain covered by the passing manifest/diagnostics/cpu-core tests.
- VID/Vcore bidirectional isolation remains covered by the same passing test set.

Direct answer:

13. FPS / CPU Voltage / CPU Core VID guardrails passed and were not affected by this retest.

## Commands Run

| Command | Result |
| --- | --- |
| `git status --short` | Ran. Worktree is broadly dirty and not isolated to P2; see Scope Check. |
| Bundled Node `.\tools\Probe-FrontendLargeLists.js --label retest-a --runs 2 --include-smoke --out .\docs\test-reports\2026-06-01-framescope-p2-frontend-large-list-display-retest-evidence\perf-run-a` | PASS; 6 performance results, smoke success true. |
| Bundled Node `.\tools\Probe-FrontendLargeLists.js --label retest-b --runs 2 --include-smoke --out .\docs\test-reports\2026-06-01-framescope-p2-frontend-large-list-display-retest-evidence\perf-run-b` | PASS; 6 performance results, smoke success true. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS; `npm ci` added 110 packages as normal verify behavior; typecheck PASS; Vitest 6 files / 61 tests PASS; Vite build PASS. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS; `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS; final line `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS; `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS; `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS; `chart-sampling-tests: PASS`. |
| Target/settings/report smoke | PASS; covered by both `Probe-FrontendLargeLists.js --include-smoke` runs. |
| Focused visual scroll probe | PASS; produced before/after internal-scroll screenshots and metrics. |
| `git diff --check` | PASS with LF-to-CRLF warnings only; no whitespace error and exit code 0. |
| Residual process check | PASS; refined probe/vite/userData check returned `NO_MATCHING_RESIDUAL_PROCESSES`. |

`Probe-ReportHtmlLayout.js` was not run because this P2 retest did not change report HTML/layout, and the request allowed skipping it when report HTML/layout was not changed.

## Boundary Confirmation

Direct answers:

14. Source modifications in this retest: no. Only docs/test-reports report/evidence artifacts were added.
15. Other optimization boards handled: no. UI animation, logging, backend monitoring, report generation, and process chart interaction were not handled.
16. Packaging/install/game/GitHub/Release boundaries: no product packaging, no FrameScope install, no real game launch, no BF6 test, no GitHub push, no Release update. `Run-Frontend.ps1 verify` did run `npm ci` and Vite build as normal verification script behavior only.
17. All required validation command results are recorded above.
18. Final conclusion: PARTIAL because current aggregate git diff is not isolated; scoped P2 performance, interaction, visual, and guardrail validation are PASS.
