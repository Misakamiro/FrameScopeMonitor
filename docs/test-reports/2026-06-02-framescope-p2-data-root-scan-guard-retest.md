# FrameScope P2 Data Root Scan Guard Retest

Date: 2026-06-02

Final conclusion: PARTIAL

Behavioral retest result: PASS. The P2 data-root scan guard optimization is reproducible on the synthetic large/noisy and small roots, valid reports were not lost, damaged JSON did not fail scans, the deep unrelated directory hit the depth guard, reparse junctions were skipped, Reports flow smoke passed, Settings save/read smoke passed, and the FPS / CPU Voltage / CPU Core VID guard tests passed.

Scope-isolation result: PARTIAL. The implementation report lists the P2 change scope as data-root / report/status scanning only, but the current `git diff` / `git status --short` worktree is already broadly dirty with many unrelated frontend, monitoring, reporting, packaging, and docs changes. Therefore this retest cannot honestly confirm that the current worktree diff is only the P2 file set. This retest did not modify source code; it only added this report and evidence files under `docs/test-reports/2026-06-02-framescope-p2-data-root-scan-guard-retest-evidence/`.

## Evidence

- Implementation report read: `docs/implementation-reports/2026-06-02-framescope-p2-data-root-scan-guard-optimization-report.md`
- Fresh comparison metrics: `docs/test-reports/2026-06-02-framescope-p2-data-root-scan-guard-retest-evidence/comparison/comparison-scan-metrics.json`
- Fresh guarded-only metrics: `docs/test-reports/2026-06-02-framescope-p2-data-root-scan-guard-retest-evidence/after/after-scan-metrics.json`
- Fresh edge-case smoke: `docs/test-reports/2026-06-02-framescope-p2-data-root-scan-guard-retest-evidence/edge-cases/edge-case-smoke.json`
- Command logs: `docs/test-reports/2026-06-02-framescope-p2-data-root-scan-guard-retest-evidence/command-logs/`

## 1. P2 Optimization Reproducibility

PASS for behavior. Fresh normalized comparison reproduced the large/noisy root improvement:

| Root | Before status scan avg | After status scan avg | Before Reports refresh avg | After Reports refresh avg |
| --- | ---: | ---: | ---: | ---: |
| Large/noisy | 108.453 ms | 11.524 ms | 108.812 ms | 17.715 ms |
| Small | 8.774 ms | 2.101 ms | 3.470 ms | 8.298 ms |

The small-root Reports refresh average is higher because run 1 was cold/JIT-sensitive; run 2 stayed low at 1.936 ms. No valid small-root report disappeared.

## 2. Large/Noisy Root Retest Data

Fresh normalized guarded results, 2 required runs:

| Run | Status scan elapsed | Reports refresh elapsed | Directories scanned | Files scanned | JSON parsed | Effective reports |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 11.656 ms | 17.099 ms | 132 | 256 | 71 | 46 |
| 2 | 11.392 ms | 18.330 ms | 132 | 256 | 71 | 46 |
| Average | 11.524 ms | 17.715 ms | 132 | 256 | 71 | 46 |

Large/noisy effective reports were not lost: expected 46, observed 46 in both runs.

## 3. Small Root Retest Data

Fresh normalized guarded results, 2 required runs:

| Run | Status scan elapsed | Reports refresh elapsed | Directories scanned | Files scanned | JSON parsed | Effective reports |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 3.168 ms | 14.660 ms | 9 | 19 | 4 | 3 |
| 2 | 1.034 ms | 1.936 ms | 9 | 19 | 4 | 3 |
| Average | 2.101 ms | 8.298 ms | 9 | 19 | 4 | 3 |

Small-root effective reports were not lost: expected 3, observed 3 in both runs. The guard did not incorrectly skip the normal small directory layout.

## 4. Exception Path Retest

Fresh edge smoke after adding a synthetic reparse junction inside the synthetic temp data root:

| Case | Result |
| --- | --- |
| Damaged JSON | PASS. `damagedJson=25`; scan completed. |
| Deep unrelated dir | PASS. `depthLimitHits=1`. |
| Reparse / junction skip | PASS. `reparseDirectoriesSkipped=2`, skip reason includes `reparse-point=2`. |
| Enumeration errors | PASS. `enumerationErrors=0`; no overall scan failure. |
| Status matches | PASS. `statusMatches=71`. |

## 5. Reports Flow Smoke

PASS via `FrameScopeWebBridgeTests.exe`.

Covered request types in `tests/FrameScopeWebBridgeTests.cs`: `reports.list`, `reports.open`, `reports.openDirectory`, and `reports.regenerate`. The test executable passed after the current build, and large/noisy scan metrics still showed 46 valid reports.

## 6. Settings Save/Read Smoke

PASS via `FrameScopeWebBridgeTests.exe`.

Covered flows include `config.save`, config round-trip of theme/window/CPU telemetry fields, `targets.save`, and target/config preservation.

## 7. FPS / CPU Voltage / CPU Core VID Guard

PASS.

| Guard | Evidence |
| --- | --- |
| FPS raw statistics semantics preserved | `FrameScopeReportManifestTests.exe` PASS. It checks raw frame count, raw average/low values, and no fake FPS. |
| `bucketMs=1000` | `FrameScopeReportManifestTests.exe` PASS. |
| CPU Voltage / Vcore remains independent | `FrameScopeReportManifestTests.exe`, `FrameScopeSystemSamplerCpuCoreTests.exe`, and bundled Node `chart-sampling-tests.js` PASS. |
| CPU Core VID remains independent | `FrameScopeReportManifestTests.exe`, `FrameScopeSystemSamplerCpuCoreTests.exe`, and bundled Node `chart-sampling-tests.js` PASS. |
| VID/Vcore bidirectional isolation | PASS. VID-only does not create CPU Voltage / Vcore data; aggregate Vcore/SOC/package voltage does not create CPU Core VID data. |

## 8. Source And Scope Answers

| Question | Answer |
| --- | --- |
| Was source modified by this retest? | No. This retest only added report/evidence artifacts under `docs/test-reports`. |
| Does the current git diff only contain the P2 target file set? | No. Current worktree is already broadly dirty beyond the P2 file set. |
| Implementation report P2 target files | `FrameScopeReportProgress.cs`, `FrameScopeWebBridge.Reports.cs`, `FrameScopeDiagnostics.IO.cs`, `FrameScopeReportProgressTests.cs`, `FrameScopeWebBridgeTests.cs`, `FrameScopeDiagnosticsTests.cs`, `Build-FrameScopeTests.ps1`. |
| Did this retest handle other optimization areas? | No. No frontend, animation, logging, backend monitoring, report generation, or process chart work was performed. |
| Did this retest package/install/start a real game/test BF6/push/update Release? | No. `Run-Frontend.ps1 verify` restored frontend packages and ran a Vite build as verification-script behavior only; no product packaging, FrameScope install, real game launch, BF6 test, GitHub push, or Release update was performed. |
| Was `Probe-ReportHtmlLayout.js` run? | No. This retest did not change report HTML/layout and the requested scope explicitly excluded report HTML/layout work. |

## 9. Verification Commands

| Command / check | Result |
| --- | --- |
| `git status --short` | Captured. Worktree is broadly dirty with many existing modified/untracked files; new retest evidence dir appears under `docs/test-reports/2026-06-02-framescope-p2-data-root-scan-guard-retest-evidence/`. |
| Large/noisy root scan retest, 2 runs | PASS. 46 reports retained; 132 dirs, 256 files, 71 JSON parsed. |
| Small root scan retest, 2 runs | PASS. 3 reports retained; 9 dirs, 19 files, 4 JSON parsed. |
| Damaged JSON / deep unrelated dir / reparse junction retest | PASS. Damaged JSON safe, `depthLimitHits=1`, `reparseDirectoriesSkipped=2`, `enumerationErrors=0`. |
| Reports flow smoke | PASS via `FrameScopeWebBridgeTests.exe`. |
| Settings save/read smoke | PASS via `FrameScopeWebBridgeTests.exe`. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS. Typecheck PASS; Vitest 6 files / 62 tests PASS; Vite build PASS. Script restored frontend packages and regenerated dist as normal verification behavior. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS. `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS. |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS. |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS. |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS. Used `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`. |
| `git diff --check` | PASS, exit 0. Existing LF/CRLF warnings only; no whitespace errors. |
| Residual process check | PASS. `NO_MATCHING_RESIDUAL_PROCESSES`. |

## 10. Final Conclusion

PARTIAL overall because the behavioral P2 retest passed, but the current worktree diff is not isolated to the P2 data-root scan guard files. No product correctness failure was found in this retest.
