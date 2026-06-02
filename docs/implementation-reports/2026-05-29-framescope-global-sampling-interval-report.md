# FrameScope Global Sampling Interval Implementation Report

Date: 2026-05-29
Result: PASS

## Scope

FrameScope Monitor now exposes one user-facing telemetry sampling interval instead of separate Settings and per-target intervals. The setting controls FrameScope-owned telemetry only. PresentMon raw frame rows remain unmodified so FPS average, 1% low, and 0.1% low continue to be computed from raw frame data.

No BF6 or real game was launched. No installer was run. No GitHub push or release update was performed.

## Implementation

- Global field: `TelemetrySampleIntervalMs`.
- Default: `1000 ms`.
- User-visible and config clamp range: `500-5000 ms`.
- Legacy target fields remain readable for compatibility, but normalization migrates `SampleIntervalMs`, `ProcessSampleIntervalMs`, and `SlowSampleIntervalMs` to the global interval.
- CPU telemetry config is normalized to the same global interval for per-core frequency and voltage/VID sampling.
- Watcher launch arguments now pass the global interval into:
  - `--SampleIntervalMs`
  - `--ProcessSampleIntervalMs`
  - `--SlowSampleIntervalMs`
  - `--CpuCoreSampleIntervalMs`
  - `--CpuVoltageSampleIntervalMs`
  - CPU VID follows the voltage interval path.
- Settings UI now shows a single `全局采样间隔` field with default/range guidance and explains that lower values refresh more frequently and cost more resources.
- Settings no longer exposes a separate backend/process sampling interval.
- Target list and target editing no longer display or edit per-target sampling time.
- Existing `PollIntervalMs` remains an internal watcher loop setting and was not made user-facing.

## Telemetry Covered

The unified interval applies to FrameScope-owned telemetry, including:

- `process-samples.csv`
- `system-samples.csv`
- `cpu-core-samples.csv`
- `cpu-voltage-samples.csv`
- `cpu-vid-samples.csv`
- other FrameScope-owned sampler telemetry routed through the same monitor-session interval arguments

PresentMon raw frame data is not downsampled by this setting. FPS charts may still bucket visually at 1 second, but FPS statistics remain based on raw frame rows.

## Compatibility

- Old configs without `TelemetrySampleIntervalMs` load with `1000 ms`.
- Old targets with per-target `100 ms` or other legacy `SampleIntervalMs` / `ProcessSampleIntervalMs` values load without crashing.
- Legacy per-target values do not continue to pollute the UI.
- Saving normalizes legacy target sampling fields to the global interval.

## Evidence

Screenshots and generated evidence are under:

`artifacts/global-sampling-20260529/`

Key screenshots:

- `webview2-live-smoke-settings-sampling.png`: Settings shows one global sampling interval and the `500-5000 ms` range.
- `webview2-live-smoke-targets-result.png`: Target list has no per-target sampling column.
- `cpu-core-vid-report.png`: CPU Core VID report tab still opens and keeps the unavailable/no-fake-data state where the run has no VID sensor data.

Synthetic monitor-session evidence:

`artifacts/global-sampling-20260529/synthetic-monitor-session/synthetic-monitor-session-results.txt`

Observed:

- `1000 ms`: sample/process/system/cpuCore/cpuVoltage/cpuVid all `1000`.
- `1500 ms`: sample/process/system/cpuCore/cpuVoltage/cpuVid all `1500`.

## Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Run-Frontend.ps1 verify`: PASS
  - TypeScript typecheck passed.
  - Vitest: 5 files, 55 tests passed.
  - Production frontend build passed.
- `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1`: PASS
- `powershell -NoProfile -ExecutionPolicy Bypass -File tests\Build-FrameScopeTests.ps1`: PASS
- C# test executables: PASS
  - `FrameScopeConfigStoreTests.exe`
  - `FrameScopeWebBridgeTests.exe`
  - `FrameScopeNativeWatcherPolicyTests.exe`
  - `FrameScopeNativeMonitorChildProcessTests.exe`
  - `FrameScopeSystemSamplerCpuCoreTests.exe`
  - `FrameScopeReportManifestTests.exe`
  - `FrameScopeProcessSamplerTests.exe`
  - `FrameScopeDiagnosticsTests.exe`
  - `FrameScopePresentMonDiagnosticsTests.exe`
  - `FrameScopeProcessCleanupTests.exe`
  - `FrameScopeLoggingPolicyTests.exe`
  - `FrameScopeWebHostLifecycleTests.exe`
  - `FrameScopeCapturePlannerTests.exe`
  - `FrameScopeReportProgressTests.exe`
  - `FrameScopePubgSimulatorTests.exe`
  - `FrameScopeWebView2RuntimeTests.exe`
  - `FrameScopeUiStateTests.exe`
- `node tests\chart-sampling-tests.js`: PASS
- Synthetic monitor-session at `1000 ms` and `1500 ms`: PASS
- WebView2 live smoke: PASS
- WebView2 reduced-motion smoke: PASS

## Retest Recommendation

Recommend entering the retest window. The implementation and synthetic/WebView2 checks pass, but the next validation should be a normal non-BF6 update-validation pass that confirms the packaged app preserves the same global interval behavior after install/update.
