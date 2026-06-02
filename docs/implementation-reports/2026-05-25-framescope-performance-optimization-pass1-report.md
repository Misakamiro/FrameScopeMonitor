# FrameScope performance optimization pass 1

Date verified: 2026-05-26 Asia/Hong_Kong
Source root: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
Baseline: `docs\diagnostics\2026-05-24-framescope-performance-baseline.md`

## Verdict

PASS for the first low-risk pass.

The pass stayed inside the baseline-supported scope:

- Added normal/high-precision process sampling profile semantics.
- Default normal process sampling is 250ms.
- Legacy 100ms targets remain high-precision and keep 100ms.
- Reduced `FrameScopeProcessSampler.exe` CSV row-writing allocation without changing CSV schema or dropping raw diagnostic data.
- Hardened WebView2 monitor stop cleanup so smoke start/stop waits for watcher/process cleanup and reports remaining count after the wait.
- Re-ran the 100/250/500/1000ms synthetic matrix with PID-level CPU evidence.

Explicit non-goals held:

- No large-report generation optimization.
- No BF6 behavior changes or BF6 test launch.
- No FPS or PresentMon capture semantic reduction.
- No UI visual redesign.
- No tray feature work in this pass.
- No CPU telemetry feature work in this pass.
- No GitHub push or release publishing.

Note: `build.ps1` was run as requested verification and it emits `dist\FrameScopeMonitor-Setup.exe` and `dist\FrameScopeMonitor-Full-Setup.exe` by design. Those files are build verification artifacts only; no packaging/release workflow or GitHub publish step was performed.

## Implementation

### Process sampling profile

Files:

- `src\core\FrameScopeConfigStore.cs`
- `framescope-config.example.json`
- `src\frontend\src\bridge\contract.ts`
- `src\frontend\src\pages\TargetsPage.tsx`
- `tests\FrameScopeConfigStoreTests.cs`

Behavior:

- New default targets use `ProcessSamplingMode: "normal"` and `ProcessSampleIntervalMs: 250`.
- `ProcessSamplingMode: "high-precision"` keeps `ProcessSampleIntervalMs: 100`.
- Old config files without `ProcessSamplingMode` remain compatible:
  - old explicit `ProcessSampleIntervalMs: 100` is inferred as high precision;
  - old non-100 or missing values normalize to normal mode.
- Editable target save/merge preserves hidden interval fields and sampling mode, so saving through the UI does not destroy existing target sampling settings.

### ProcessSampler write path

Files:

- `src\monitoring\FrameScopeProcessSampler.IO.cs`
- `tests\FrameScopeProcessSamplerTests.cs`

Behavior:

- Replaced `String.Join(",", values.Select(Csv))` with a direct `StringBuilder` CSV line builder.
- CSV escaping and output line formatting are covered by tests.
- Data shape is unchanged: `process-samples.csv`, `topcpu-samples.csv`, `topio-samples.csv`, and alerts are still written.
- This is intentionally a low-risk allocation/write optimization, not a data-reduction change.

### WebView2 watcher cleanup wait

Files:

- `src\app\FrameScopeNativeMonitor.ProcessCleanup.cs`
- `src\app\FrameScopeNativeMonitor.WebHost.cs`
- `tests\FrameScopeProcessCleanupTests.cs`

Behavior:

- `StopFrameScopeBackgroundProcesses()` now waits for cleanup through `StopFrameScopeBackgroundProcessesAndWait(3000)`.
- Added `WaitForFrameScopeBackgroundProcessesToExit(waitMs)` so callers can prove no watcher/sampler/session processes remain.
- WebView2 `monitor.stop` now waits up to 8000ms when background processes are found and reports `remainingProcessCount` after the wait.
- The live and reduced-motion WebView2 smoke runs both reported monitor stop completion with no remaining watcher.

## Synthetic matrix

Final valid matrix evidence:

- Summary copy: `artifacts\performance-optimization-pass1-20260525\matrix-after-verified\matrix-results.json`
- Raw PID-level samples and full run artifacts: `C:\Users\MISAKA~1\AppData\Local\Temp\framescope-opt1-matrix-20260526-130533`
- Measurement method: PID-level process CPU deltas from `Get-Process.CPU`, filtered to the synthetic monitor chain and expressed as single-core percent.

The earlier attempt at `artifacts\performance-optimization-pass1-20260525\matrix-after\matrix-results.json` is not used for acceptance. It captured valid rows/bytes but did not attribute `FrameScopeProcessSampler.exe` CPU correctly because it only saw a partial process set. A second deep-path attempt was also discarded because .NET Framework hit `PathTooLongException`; the final valid matrix used a short temp root.

### Before baseline

| Process interval | Total avg CPU | ProcessSampler avg CPU | `process-samples.csv` rows | `process-samples.csv` bytes | Top CPU rows | Top IO rows |
|---:|---:|---:|---:|---:|---:|---:|
| 100ms | 4.77% | 3.57% | 9,011 | 774,785 | 1,961 | 497 |
| 250ms | 2.49% | 1.29% | 3,918 | 336,803 | 841 | 237 |
| 500ms | 2.20% | 1.00% | 1,913 | 163,854 | 401 | 140 |
| 1000ms | 2.05% | 0.71% | 1,002 | 85,560 | 201 | 90 |

### After pass 1

| Process interval | Total avg CPU | ProcessSampler avg CPU | ProcessSampler max CPU | `process-samples.csv` rows | `process-samples.csv` bytes | Top CPU rows | Top IO rows | Capture |
|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 100ms | 3.42% | 2.37% | 10.35% | 8,272 | 708,852 | 1,860 | 284 | captured |
| 250ms | 1.98% | 1.19% | 5.19% | 3,608 | 309,273 | 800 | 157 | captured |
| 500ms | 0.79% | 0.40% | 5.19% | 1,848 | 157,980 | 400 | 113 | captured |
| 1000ms | 1.46% | 0.66% | 5.21% | 968 | 82,445 | 200 | 63 | captured |

### Acceptance checks

| Gate | Result | Evidence |
|---|---|---|
| 250ms normal mode total avg CPU <= 3% | PASS | 1.98% total avg CPU |
| 100ms high precision does not regress >10% from old baseline | PASS | ProcessSampler avg CPU 2.37% vs old 3.57%; total avg CPU 3.42% vs old 4.77% |
| FPS/PresentMon semantics preserved | PASS | all four synthetic runs captured 360 PresentMon rows |
| Raw data not deleted | PASS | process, top CPU, top IO, raw PID samples, and run artifacts retained |

## Verification

Commands run from the source root:

| Check | Result |
|---|---|
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS |
| `.\tests\FrameScopeConfigStoreTests.exe` | PASS |
| `.\tests\FrameScopeProcessSamplerTests.exe` | PASS |
| `.\tests\FrameScopeProcessCleanupTests.exe` | PASS |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS |
| `.\FrameScopeMonitor.exe --web-ui-smoke ...` | PASS, `success=true`, elapsed 7209ms |
| `.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion ...` | PASS, `success=true`, elapsed 7097ms |
| `git diff --check` | PASS, exit 0; only CRLF conversion warnings |
| residual process check | PASS, `matchingResidualCount=0` |

Smoke evidence:

- Live: `artifacts\performance-optimization-pass1-20260525\webview2-live-smoke.json`
- Live screenshot: `artifacts\performance-optimization-pass1-20260525\webview2-live-smoke.png`
- Reduced motion: `artifacts\performance-optimization-pass1-20260525\webview2-reduced-motion-smoke.json`
- Reduced motion screenshot: `artifacts\performance-optimization-pass1-20260525\webview2-reduced-motion-smoke.png`
- Residual check: `artifacts\performance-optimization-pass1-20260525\residual-process-check.json`

Both smoke runs exercised monitor start/stop. The stop event reported `remainingProcessCount: 0`, and the final independent process query found no matching `FrameScopeMonitor`, `FrameScopeProcessSampler`, `FrameScopeSystemSampler`, `FrameScopeReportGenerator`, PresentMon, `FakePresentMon`, or `TslGame` processes.

## Worktree note

This source tree already contained many unrelated uncommitted changes before this pass, including WebView2 visual/theme/tray, CPU telemetry, PresentMon diagnostics, system sampler, report metadata, and packaging/build changes. I did not revert or normalize those unrelated changes. This report describes only the performance pass work and the verification performed against the current tree.

## Next safe item

The baseline still shows large report generation as the biggest proven bottleneck, but it was intentionally not optimized in this pass. That should be handled as the next separate round with its own before/after data.
