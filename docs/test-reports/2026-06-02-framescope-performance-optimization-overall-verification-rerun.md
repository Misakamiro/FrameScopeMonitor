# FrameScope performance optimization overall verification rerun

Date: 2026-06-02
Workspace: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
Scope: validation only. No source fix, no optimization, no packaging, no installer/setup generation, no real game launch, no BF6 test, no GitHub push, no Release update.

## Final verdict

Result: PARTIAL.

The product verification itself is PASS: the previous blocker is cleared, all required commands/probes passed directly or after documented harness/path correction, and the corrected final results show no product failure, no source modification by this rerun inside the target workspace, no required performance regression, and no functional/semantic regression.

The overall task verdict is PARTIAL because one report artifact was accidentally created outside the requested FrameScope workspace before the correct report was copied into this workspace: `C:\Users\misakamiro\Documents\New project 4\docs\test-reports\2026-06-02-framescope-performance-optimization-overall-verification-rerun.md`. I did not delete or move that file because the user explicitly prohibited delete/move operations in this run.

Dirty worktree caveat: the target worktree is still the expected large dirty tree from prior P0/P1/P2 optimization rounds. Per the rerun rule, this is not a PARTIAL condition. The PARTIAL judgment is only for the accidental out-of-workspace report artifact, not for the known dirty diff and not for product behavior.

## Evidence roots

- Command logs: `docs\test-reports\2026-06-02-framescope-performance-optimization-overall-verification-rerun-evidence\command-logs`
- Short evidence root: `docs\test-reports\ov-rerun-0602-evidence`
- Final report: `docs\test-reports\2026-06-02-framescope-performance-optimization-overall-verification-rerun.md`

## Blocker status

Previous blocker: `FrameScopeNativeWatcherPolicyTests.exe` failed in the previous overall verification.

Rerun result:

- `.\tests\FrameScopeNativeWatcherPolicyTests.exe`: PASS, exit 0, elapsed 91 ms.
- Evidence: `command-logs\09-FrameScopeNativeWatcherPolicyTests.log`.

Conclusion: blocker cleared.

## P0 large report generation

Evidence:

- `docs\test-reports\ov-rerun-0602-evidence\p0\p0-report-generation-rerun-with-cpu.json`
- `docs\test-reports\ov-rerun-0602-evidence\p0\p0-frame-stats-raw-compare.json`
- Report HTML/data: `docs\test-reports\ov-rerun-0602-evidence\p0\history-large-rerun\charts`

Results:

| Run | Exit | Elapsed | CPU | Peak WS | Peak private | `data.js` | Frames | Raw rows | Valid rows | Process samples | Processes | System samples |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 0 | 4605 ms | 4562.5 ms | 198.54 MB | 207.35 MB | 1,266,157 bytes | 876,585 | 876,603 | 876,593 | 17,714 | 119 | 1,933 |
| 2 | 0 | 4627 ms | 4625 ms | 198.59 MB | 207.18 MB | 1,266,157 bytes | 876,585 | 876,603 | 876,593 | 17,714 | 119 | 1,933 |

Semantic checks:

- `reportKind=full`, `frameCaptureStatus=captured`.
- `frameStatsMatchRaw=true`.
- `bucketMs=1000`, `lowWindowMs=2000`.
- `mismatches=0`.
- FPS raw stats preserved: average `587.96`, 1% low `164.57`, 0.1% low `36.6`.

Harness caveat: earlier P0 attempts hit harness-only issues, including `Start-Process` CPU metric capture and redirect/pipe behavior. The final corrected no-redirect CPU run exited 0 and produced the metrics above. This is not a product failure.

## P1 backend monitoring

Evidence:

- `docs\test-reports\ov-rerun-0602-evidence\p1bm\after.json`
- Runtime: `docs\test-reports\ov-rerun-0602-evidence\p1bm\runtime`

Results:

| Run | Interval | Duration | Monitor CPU | Monitor WS | SystemSampler CPU | SystemSampler WS | Process rows | System rows | Core rows | Vcore rows | VID rows |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| rerun-01 | 1000 ms | 9108 ms | 0.0781 s / 0.88% single-core | 33.43 MB | 0.6875 s / 9.21% single-core | 51.02 MB | 734 | 7 | 112 | 7 | 56 |
| rerun-02 | 1000 ms | 9122 ms | 0.0938 s / 1.06% single-core | 33.07 MB | 0.7812 s / 10.41% single-core | 50.59 MB | 735 | 7 | 112 | 7 | 56 |

Semantic checks:

- `SampleIntervalMs=1000`, `ProcessSampleIntervalMs=1000`, `SlowSampleIntervalMs=1000`.
- `Phase=done`.
- CPU core telemetry available.
- `CpuVoltageStatus=vcore-available`; CPU Voltage / Vcore rows remain present.
- `CpuVidStatus=core-vid-available`; CPU Core VID rows remain present.

Harness caveat: the first long evidence path build hit a CSC temp/system-call path issue. The short path rerun built and measured successfully. This is a harness/path issue, not a product failure.

## P1 process chart interaction

Evidence: `docs\test-reports\ov-rerun-0602-evidence\process-interaction\rerun-process-interaction.json`

| Run | Tab draw | Tab elapsed | Max search draw | Max dispatch | Hover | DOM nodes | Process names | Process samples | JS heap |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 10.2 ms | 86.3 ms | 6.9 ms | 0.1 ms | 0.4 ms | 319 | 119 | 17,714 | 12.898 MB |
| 2 | 9.5 ms | 85.6 ms | 7.9 ms | 0.1 ms | 0.1 ms | 319 | 119 | 17,714 | 7.538 MB |

Checks:

- Search is case-insensitive and no-result path returns 0 without breaking chart state.
- Process names and samples are not dropped: 119 process names, 17,714 samples.
- Chart remains nonblank, tooltip visible, `fpsBucketMs=1000`.

## P2 large list display

Evidence: `docs\test-reports\ov-rerun-0602-evidence\large-lists\rerun-large-list-frontend-large-list-probe.json`

| Run | Initial rows | Windowed | Initial DOM | Initial CDP nodes | Scroll result | Filter result | Input frame |
| --- | ---: | --- | ---: | ---: | --- | --- | ---: |
| 1 | 19 / 250 | true | 331 | 1,691 | 10 rows, `FixtureProcess-241.exe` to `FixtureProcess-250.exe` | 51 / 51 | 31.3 ms |
| 2 | 19 / 250 | true | 275 | 3,909 | 10 rows, `FixtureProcess-241.exe` to `FixtureProcess-250.exe` | 51 / 51 | 27.7 ms |

Smoke:

- Target smoke: 4 rows retained.
- Settings smoke: saved successfully.
- Reports smoke: 3 rows retained.
- `smoke.success=true`.

Conclusion: 250-row initial windowing is preserved at about 19/250. Scroll, search/filter, input responsiveness, target/settings/report flows all pass.

## P2 UI animation

Evidence: `docs\test-reports\ov-rerun-0602-evidence\ui-animation\rerun-ui-animation-frontend-ui-animation-probe.json`

Static scan:

| Field | Count |
| --- | ---: |
| `framerMotionImports` | 0 |
| `motionElements` | 0 |
| `whileTap` | 0 |
| `motionConfig` | 0 |
| `transitionAll` | 0 |
| `boxShadowTransitions` | 0 |
| `filterDeclarations` / `backdropFilterDeclarations` / `blurReferences` | 0 / 0 / 0 |
| `prefersReducedMotionBlocks` | 3 |

Runtime inventory:

- ordinary max `transitionAll=0`.
- ordinary max box-shadow transitioned count `0`.
- ordinary max framer inline transform `0`.
- reduced-motion max animated count `0`.
- reduced-motion max `transitionAll=0`.
- `smoke.success=true`.

Conclusion: framer-motion imports, `motion.*`, `whileTap`, box-shadow transition, and reduced `transition-all` remain removed/controlled.

## P2 logging

Fresh rerun evidence:

- Idle watcher: `docs\test-reports\ov-rerun-0602-evidence\logging\idle-logs\rerun-log-metrics.json`
- Tail trim: `docs\test-reports\ov-rerun-0602-evidence\logging\diagnostics-tail-trim\diagnostics-tail-trim-smoke-result.json`
- Logging policy test: `command-logs\07-FrameScopeLoggingPolicyTests.log`

Fresh idle rerun:

| Case | Lines | Bytes | Duplicates | Lines/s | Key messages |
| --- | ---: | ---: | ---: | ---: | --- |
| default idle | 1 | 272 | 0 | 0.132 | `native-watcher-start` |
| verbose+perf idle | 3 | 500 | 0 | 0.395 | `native-watcher-start`, `target-scan`, `watcher-poll-ms` |

Tail trim rerun:

- `trimmed=true`.
- `beforeBytes=5,449,602`, `afterBytes=4,193,914`.
- `keptTailMarker=true`, `tailMarkerCount=65`.
- `removedOldHeadMarker=true`.
- `keptOldTailMarker=true`.
- `normalAppendOk=true`.

Rate limiter:

- Fresh `FrameScopeLoggingPolicyTests.exe`: PASS.
- Same-day detailed smoke evidence remains valid and was rechecked: `allPass=true`, same-key repeat suppressed, changed state writes, different keys do not suppress each other, heartbeat after interval writes, `target-scan` and `watcher-poll` use limiters, error paths use direct log.

Conclusion: default idle keeps necessary log, verbose+perf repeated logs are controlled, rate limiter and diagnostics tail trim are normal.

## P2 data root scan

Evidence:

- `docs\test-reports\ov-rerun-0602-evidence\data-root\comparison\comparison-scan-metrics.json`
- `docs\test-reports\ov-rerun-0602-evidence\data-root\edge-cases\edge-case-smoke.json`

Results:

- Small root reports: 3 / 3.
- Large noisy root reports: 46 / 46.
- Large status matches: 71.
- Damaged JSON counted safely: 25.
- Edge scan: `directoriesVisited=132`, `filesVisited=256`, `reparseDirectoriesSkipped=2`, `depthLimitHits=1`, `enumerationErrors=0`.

Conclusion: large noisy root, small root, damaged JSON, deep directory, and reparse junction safety checks all pass.

## Report HTML and core semantics

Evidence: `docs\test-reports\ov-rerun-0602-evidence\layout\report-overflow-probe.json`

Results:

- `allNoOverflow=true` across 23 viewport/view checks.
- FPS dropdown options remain `Average FPS / 1% Low / 0.1% Low`, `Average FPS only`, `1% Low only`, `0.1% Low only`.
- `noMinInstantOption=true` for all checked views.
- CPU Voltage / Vcore view note keeps GamePP alignment: overall CPU Voltage / Vcore in V; VID/SOC/package/VBAT/VIN not used there.
- CPU Core VID view remains separate and explicitly identifies VID as requested/target voltage, not real per-core Vcore.
- GamePP-style chart and bucketed FPS behavior remain intact.

Additional semantic coverage:

- `FrameScopeReportManifestTests.exe`: PASS, covering FPS raw/stat semantics, bucket and manifest behavior, CPU Voltage/Vcore, CPU Core VID, and VID/Vcore isolation.
- `chart-sampling-tests.js`: PASS.
- `FrameScopeWebBridgeTests.exe`: PASS, covering target/settings/report bridge paths.
- `FrameScopeDiagnosticsTests.exe`: PASS.
- GameLite/lightweight diff check: `NO_DIFF_PATH_MATCHES`.

Conclusion: FPS raw, GamePP chart, `bucketMs=1000`, CPU Voltage/Vcore, CPU Core VID, VID/Vcore isolation, target/settings/report flow, and GameLite/lightweight untouched status are preserved.

## WebView2 smoke

Evidence:

- `docs\test-reports\ov-rerun-0602-evidence\webview2\live\live.json`
- `docs\test-reports\ov-rerun-0602-evidence\webview2\reduced\reduced.json`
- `docs\test-reports\ov-rerun-0602-evidence\webview2\webview2-smoke-summary.json`

| Mode | Success | Page loaded | Page ready | Reduced motion | Elapsed | Process count | Bridge |
| --- | --- | --- | --- | --- | ---: | ---: | --- |
| live | true | true | true | false | 614 ms | 250 | ready |
| reduced | true | true | true | true | 639 ms | 250 | ready |

Conclusion: WebView2 live and reduced-motion smoke both pass. This used smoke mode only and did not start a real game.

## Required command results

| Command / check | Result | Evidence |
| --- | --- | --- |
| `git status --short` | PASS, ran at start and end; large dirty tree is expected | `00-git-status-short.log`, `28-final-git-status-short.log` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS; added 110 packages as verify behavior, typecheck PASS, Vitest 6 files / 62 tests PASS, Vite build PASS | `01-run-frontend-verify.log` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS, tests rebuilt | `02-build-framescope-tests.log` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS, exit 0 | `03-FrameScopeReportManifestTests.log` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS, exit 0 | `04-FrameScopeDiagnosticsTests.log` |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS, exit 0 | `05-FrameScopeSystemSamplerCpuCoreTests.log` |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS, exit 0 | `06-FrameScopeReportProgressTests.log` |
| `.\tests\FrameScopeLoggingPolicyTests.exe` | PASS, exit 0 | `07-FrameScopeLoggingPolicyTests.log` |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS, exit 0 | `08-FrameScopeWebBridgeTests.log` |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS, exit 0, previous blocker cleared | `09-FrameScopeNativeWatcherPolicyTests.log` |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS, exit 0 | `10-FrameScopeNativeMonitorChildProcessTests.log` |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS | `11-chart-sampling-tests-bundled-node.log` |
| Bundled Node raw P0 compare | PASS after corrected harness; `frameStatsMatchRaw=true`, `bucketMs=1000` | `12g-p0-raw-compare-bundled-node.log` |
| Bundled Node `tools\Probe-ReportHtmlLayout.js` | PASS | `13-probe-report-html-layout.log` |
| Bundled Node `tools\Probe-ReportProcessInteraction.js` | PASS | `14-probe-report-process-interaction.log` |
| Bundled Node `tools\Probe-FrontendLargeLists.js` | PASS | `15-probe-frontend-large-lists.log` |
| Bundled Node `tools\Probe-FrontendUiAnimation.js` | PASS | `16-probe-frontend-ui-animation.log` |
| P0 report generation performance probe | PASS after corrected harness; final no-redirect CPU run exit 0 | `12d-p0-report-generation-rerun-with-cpu-no-redirect.log` |
| P1 backend measurement runtime build | PASS on short path | `17b-p1-build-measurement-runtime-shortpath.log` |
| P1 backend overhead measurement | PASS on short path | `18b-p1-measure-backend-monitor-overhead-shortpath.log` |
| P2 logging idle rerun | PASS | `24b-p2-logging-idle-rerun.log` |
| P2 logging tail trim rerun | PASS | `24-p2-logging-tail-trim-rerun.log` |
| P2 data root scan rerun | PASS | `23-p2-data-root-scan-rerun.log` |
| Target/settings/report smoke | PASS via large-list and UI-animation smoke plus WebBridge/WebView2 | large-list/UI-animation JSON, `08-FrameScopeWebBridgeTests.log` |
| WebView2 live smoke | PASS, exit 0 | `22-webview2-live-smoke.log` |
| WebView2 reduced smoke | PASS, exit 0 | `23-webview2-reduced-smoke.log` |
| `git diff --check` | PASS, exit 0; only LF/CRLF warnings from current dirty worktree | `25-final-git-diff-check.log` |
| GameLite/lightweight diff check | PASS, `NO_DIFF_PATH_MATCHES` | `26-final-gamelite-lightweight-diff-check.log` |
| Residual process check | PASS, `NO_MATCHING_RESIDUAL_PROCESSES` after corrected self-match filter and after fresh idle rerun | `27b-final-residual-process-check-corrected.log`, `27c-final-residual-process-check-after-idle-rerun.log` |

Harness caveats:

- `12e` / `12f` raw compare attempts failed due PowerShell quoting / JS spread stack shape; final `12g` passed.
- P0 generator attempts before `12d` had process CPU capture / redirect harness issues; final `12d` passed.
- P1 long output path build hit a CSC path/temp issue; final short path `17b` and `18b` passed.
- First residual process check matched its own PowerShell command line; corrected name-only checks passed, including after the fresh idle logging rerun.

These caveats do not indicate source/product failures.

## Boundary audit

- Source code changed by this rerun in the target workspace: no.
- Files added by this rerun in the target workspace: `docs\test-reports` report/evidence only.
- Boundary issue: yes. A duplicate report file was accidentally created outside the requested workspace at `C:\Users\misakamiro\Documents\New project 4\docs\test-reports\2026-06-02-framescope-performance-optimization-overall-verification-rerun.md`. No source file was changed there, but this is still an out-of-workspace artifact.
- `build.ps1` run: no.
- Installer/setup generated: no.
- Packaging run: no.
- Real game started: no.
- BF6 tested: no.
- GitHub push: no.
- Release updated: no.
- `Run-Frontend.ps1 verify` package restore / dist build: recorded as required verification script behavior and not counted as install/package work.
- Synthetic P1 target used: yes, existing synthetic `TslGame.exe` and `FakePresentMon.exe` only.

## Summary answer

1. Blocker解除: yes, `FrameScopeNativeWatcherPolicyTests.exe` PASS.
2. P0: PASS, large report generation stable, CPU/memory/data.js captured, raw FPS stats match, `bucketMs=1000`.
3. P1 backend: PASS, CPU/CPU seconds/memory measured, 1000 ms intervals preserved, system/core/Vcore/VID rows present.
4. P1 process interaction: PASS, draw/search/input responsive, names/samples not dropped.
5. P2 large list: PASS, 250-row initial windowing 19/250, scroll/search/input and target/settings/report smoke pass.
6. P2 UI animation: PASS, framer-motion/motion/whileTap/transition-all/box-shadow transition checks remain 0 where required.
7. P2 logging: PASS, fresh idle/tail rerun pass, rate limiter policy pass.
8. P2 data root scan: PASS, large 46/46, small 3/3, damaged/deep/reparse safety pass.
9. Core semantics: PASS, FPS raw, GamePP chart, bucket, CPU Voltage/Vcore, CPU Core VID, VID/Vcore isolation, target/settings/report flow, GameLite/lightweight untouched.
10. Product verification verdict: PASS.
11. Overall task verdict: PARTIAL, solely because of the accidental out-of-workspace report artifact described above.

Report write-time usage snapshot: `tokensUsed=722598`, `elapsed=1562s`.
