# FrameScope P2 Logging Performance Optimization Report

Date: 2026-06-01

Final conclusion: PASS

## Scope

This round only handled P2 logging performance and log volume protection. It did not handle frontend pages, UI animation, large lists, report generation behavior, chart interaction, backend monitoring sampling, data-root recursive scanning, build artifact sync, installation/update validation, product packaging, real game launch, BF6 testing, GitHub push, or Release update.

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
| Frontend / animation / large-list implementation | Not changed in this round |
| Report generation / chart interaction implementation | Not changed in this round |
| Backend monitoring sampling implementation | Not changed in this round |
| Data-root recursive scan optimization | Not changed in this round |
| FPS data link | Not changed; regression tests passed |
| CPU Voltage / Vcore data link | Not changed; regression tests passed |
| CPU Core VID data link | Not changed; regression tests passed |

`tools\Run-Frontend.ps1 verify` ran package restore and regenerated frontend `dist` as normal verification-script behavior. This was validation only, not product packaging, installer generation, product install, or Release work.

## Evidence And Method

Measured path:

- Root watcher log path: `framescope-watcher.log` under the workspace/root resolved by the host.
- Main append path: `FrameScopeDiagnostics.AppendLog(...)`, called through `WriteFrameScopeLog(...)`.
- Settings / bridge log directory path: `logs.openDirectory`, host-resolved, rejects frontend-provided filesystem paths.
- Scenario: idle watcher with a missing fake target, no real game launch and no BF6 test.
- Each log line corresponds to one `File.AppendAllText(...)` append in the measured watcher log path, so line delta was used as write-count / write-frequency evidence.

Evidence files:

| Evidence | Path |
| --- | --- |
| Before log metrics | `artifacts\p2-logging-performance-20260601\baseline\baseline-log-metrics.json` |
| After log metrics | `artifacts\p2-logging-performance-20260601\after\after-log-metrics.json` |
| Verification logs | `artifacts\p2-logging-performance-20260601\verification\` |
| WebView2 live smoke | `artifacts\p2-logging-performance-20260601\verification\webview2-live\webview2-live-smoke.json` |
| WebView2 reduced-motion smoke | `artifacts\p2-logging-performance-20260601\verification\webview2-reduced\webview2-reduced-smoke.json` |
| Residual process check | `artifacts\p2-logging-performance-20260601\verification\10-residual-process-check.json` |

## Before Baseline

Default idle runs were already small, matching the 2026-05-31 analysis that logging was not the current immediate hotspot. Verbose/performance diagnostics mode exposed the long-running risk: the same missing-target scan and watcher-poll state was written once per poll.

| Run | Mode | Log lines | Log bytes | Duplicate count | Write frequency, lines/sec | Elapsed sec |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `default-idle-01` | default | 1 | 264 | 0 | 0.144 | 6.943 |
| `default-idle-02` | default | 1 | 261 | 0 | 0.144 | 6.935 |
| `verbose-perf-idle-01` | verbose + perf | 13 | 1599 | 10 | 1.879 | 6.919 |
| `verbose-perf-idle-02` | verbose + perf | 13 | 1599 | 10 | 1.877 | 6.927 |

Baseline repeated messages:

- `verbose target-scan ... detected=none` repeated every watcher poll while the target was missing.
- `perf watcher-poll-ms=... active=0 completed=0` repeated every watcher poll while state was unchanged.

No per-frame default logging was found in this idle watcher path. The risk was diagnostic-mode noise during long-running watch sessions, not ordinary default logging.

## Risks Found

| Risk | Evidence | Why it matters |
| --- | --- | --- |
| Per-poll verbose target scan logs | 10 duplicate lines in each verbose/perf baseline run | Long-running idle watcher sessions can fill logs with unchanged "target not found" state. |
| Per-poll performance watcher logs | `watcher-poll-ms` emitted every poll in perf mode | Useful for diagnosis, but unchanged active/completed state does not need a line every poll. |
| Eager string construction behind verbose/perf gates | Existing call sites built aliases, paths, and process args before checking whether the log would be written | Disabled diagnostic logs should not pay formatting cost. |
| Synchronous append per line | `File.AppendAllText(...)` remains the append mechanism | Low volume is acceptable; duplicate suppression reduces append frequency without changing the storage model. |
| Long-run root log guard was append-side weak | Existing trimming depended on retention/diagnostic paths | A continuously running watcher can grow the root log before the next external cleanup. |
| Settings log directory path could be sensitive to changes | `logs.openDirectory` must remain host-owned and reject frontend paths | Needed explicit bridge smoke coverage after touching logging paths. |

## Changes

Modified files for this logging slice:

| File | Change |
| --- | --- |
| `src\core\FrameScopeLoggingPolicy.cs` | Added `FrameScopeLogRateLimiter`, keyed by log channel/state with state-change immediate write and interval heartbeat. |
| `tests\FrameScopeLoggingPolicyTests.cs` | Added coverage that repeated state is suppressed, state changes are preserved, and same-state heartbeat writes after the interval. |
| `src\app\FrameScopeNativeMonitor.Watcher.cs` | Added watcher verbose/perf rate limiters, rate-limited `target-scan` and `watcher-poll-ms`, and added lazy verbose/perf formatting overloads. |
| `src\app\FrameScopeNativeMonitor.MonitorSession.cs` | Converted verbose/perf session logs to lazy delegates so disabled diagnostic modes do not construct detailed strings. |
| `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs` | Converted the performance `report-generation-ms` log path to lazy formatting only; report generation behavior was not changed in this round. |
| `src\diagnostics\FrameScopeDiagnostics.cs` | Added append-side long-run guard: every 64 append writes, trim the current log to the existing tail-retention cap of 16 MB. |
| `docs\implementation-reports\2026-06-01-framescope-p2-logging-performance-optimization-report.md` | This report. |

TDD note: the new rate-limiter test was added first and failed because `FrameScopeLogRateLimiter` did not exist. After implementation, `FrameScopeLoggingPolicyTests.exe` passed.

## Diagnostic Safety

| Optimization | Why it does not reduce diagnostic ability |
| --- | --- |
| Rate-limit `target-scan` verbose logs | First observation still writes; changed detected state writes immediately; unchanged state writes again after 15 seconds as a heartbeat. Missing-target diagnosis remains visible without per-poll duplication. |
| Rate-limit `watcher-poll-ms` perf logs | First poll state writes; active/completed/slow-state changes write immediately; unchanged perf state writes after 15 seconds. Perf mode still proves the watcher is alive and captures state transitions. |
| Lazy verbose/perf formatting | Only diagnostic string construction moved behind the existing enable checks. Error, warning, status JSON, and non-verbose logs still use the existing paths. |
| Append-side trim guard | Uses existing tail trim behavior and only runs every 64 append writes. Current errors and recent diagnostics remain available; very old log tail beyond the cap can be discarded to protect disk usage. |
| Keep synchronous append model | This avoids introducing queue/thread shutdown risk. The optimization reduces unnecessary writes instead of changing ordering semantics. |

Required diagnostics remained intact:

- Critical errors still call the existing non-verbose log paths.
- Exceptions were not swallowed by the new policy; existing catch behavior was not broadened for monitored errors.
- `diagnostics/status` payloads kept required fields in smoke snapshots: `bridgeStatus`, `bridgeVersion`, `generatedAt`, `root`, `watcher.running`, `watcher.pid`, `watcher.statePath`, `watcher.completedRuns`, `watcher.lastReport`, `watcher.lastError`, `config.exists`, `config.path`, `config.enabledTargetCount`, `config.targetCount`, `config.dataRoot`, `host.windowVisible`, `host.trayAvailable`, `host.closeWindowBehavior`, `reports.historyPath`, and `reports.historyExists`.
- Verbose/performance diagnostics remain available when enabled; they are controlled by rate limit and lazy formatting, not removed.

## After Results

| Run | Mode | Log lines | Log bytes | Duplicate count | Write frequency, lines/sec | Elapsed sec |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `default-idle-01` | default | 1 | 258 | 0 | 0.144 | 6.947 |
| `default-idle-02` | default | 1 | 258 | 0 | 0.145 | 6.914 |
| `verbose-perf-idle-01` | verbose + perf | 3 | 486 | 0 | 0.433 | 6.921 |
| `verbose-perf-idle-02` | verbose + perf | 3 | 486 | 0 | 0.432 | 6.939 |

Before/after averages:

| Mode | Metric | Before avg | After avg | Change |
| --- | --- | ---: | ---: | ---: |
| Default | Log lines | 1.0 | 1.0 | 0.0 |
| Default | Log bytes | 262.5 | 258.0 | -4.5 |
| Default | Duplicate count | 0.0 | 0.0 | 0.0 |
| Default | Write frequency, lines/sec | 0.144 | 0.145 | +0.001 |
| Default | Elapsed sec | 6.939 | 6.931 | -0.008 |
| Verbose + perf | Log lines | 13.0 | 3.0 | -10.0 |
| Verbose + perf | Log bytes | 1599.0 | 486.0 | -1113.0 |
| Verbose + perf | Duplicate count | 10.0 | 0.0 | -10.0 |
| Verbose + perf | Write frequency, lines/sec | 1.878 | 0.433 | -1.445 |
| Verbose + perf | Elapsed sec | 6.923 | 6.930 | +0.007 |

Interpretation:

- Default mode was already controlled, and remained controlled.
- Verbose/perf idle logs were reduced from 13 lines to 3 lines per run.
- Duplicate logs were reduced from 10 to 0 in the measured verbose/perf idle runs.
- Log bytes were reduced from 1599 to 486 in the measured verbose/perf idle runs.
- CPU was not measured with enough precision to claim a CPU win. Elapsed time was effectively unchanged, which is expected because this is a long-run log-volume guard rather than the current top CPU hotspot.

## Settings And Bridge

`FrameScopeWebBridgeTests.exe` passed and covered the log-directory behavior:

- `logs.openDirectory` rejects frontend-provided paths.
- `logs.openDirectory` resolves the host-owned log directory.
- Missing log directory creation remains supported.

WebView2 smoke also passed in both modes:

| Smoke | Result | Evidence |
| --- | --- | --- |
| Live motion | PASS | `success=true`, `pageReady=true`, `reducedMotion=false`, `logsOpenPathRejected=true`, `logsOpenDirectoryOk=true`, `diagnosticsCompleted=true`, `monitorStarted=true`, `monitorStopped=true` |
| Reduced motion | PASS | `success=true`, `pageReady=true`, `reducedMotion=true`, `logsOpenPathRejected=true`, `logsOpenDirectoryOk=true`, `diagnosticsCompleted=true`, `monitorStarted=true`, `monitorStopped=true` |

Bridge evidence still contained host-to-JS and JS-to-host messages for state snapshots, report progress, diagnostics generation, monitor start/stop, and log directory opening. No bridge logging evidence field needed by the smoke was removed.

## FPS / CPU Voltage / CPU Core VID Guard

The logging changes do not touch sampling payload parsing or report chart data. Guard tests passed:

- `FrameScopeReportManifestTests.exe`: PASS, including FPS/report metadata and CPU voltage/VID report fields.
- `FrameScopeDiagnosticsTests.exe`: PASS.
- `FrameScopeSystemSamplerCpuCoreTests.exe`: PASS.
- Bundled Node `tests\chart-sampling-tests.js`: PASS.

Result: FPS, CPU Voltage / Vcore, and CPU Core VID data links were not changed by this logging round.

## Verification Commands

| Command | Result |
| --- | --- |
| Baseline log metrics, 2 default + 2 verbose/perf idle runs | PASS. Evidence in `artifacts\p2-logging-performance-20260601\baseline\baseline-log-metrics.json`. |
| After log metrics, 2 default + 2 verbose/perf idle runs | PASS. Evidence in `artifacts\p2-logging-performance-20260601\after\after-log-metrics.json`. |
| App-only direct compile of `FrameScopeMonitor.exe` for smoke validation | PASS. Executable refreshed; no installer/package build was run. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS. Typecheck PASS; Vitest 6 files / 62 tests PASS; Vite build PASS. Package restore/dist regeneration was normal verify behavior. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS. `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS. Final line `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS. `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS. `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| `.\tests\FrameScopeLoggingPolicyTests.exe` | PASS. `FrameScopeLoggingPolicyTests: PASS`. |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS. `FrameScopeWebBridgeTests: PASS`; used as Settings/log-directory smoke. |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS. `chart-sampling-tests: PASS`. |
| WebView2 live smoke | PASS. `success=true`, bridge smoke and logs open-directory checks passed. |
| WebView2 reduced-motion smoke | PASS. `success=true`, bridge smoke and logs open-directory checks passed. |
| `git diff --check` | PASS. Exit code 0; only existing LF-to-CRLF warnings were printed, no whitespace errors. |
| Residual process check | PASS. `NO_MATCHING_RESIDUAL_PROCESSES`. |

## Final Notes

The measurable benefit is log volume control in verbose/performance diagnostic mode, not a broad runtime speedup. That matches the original analysis: logging was not the current immediate bottleneck, but it was worth guarding for long-running sessions. The optimized paths preserve first-write, state-change, heartbeat, and error diagnostics while suppressing unchanged per-poll noise.

Final conclusion: PASS.
