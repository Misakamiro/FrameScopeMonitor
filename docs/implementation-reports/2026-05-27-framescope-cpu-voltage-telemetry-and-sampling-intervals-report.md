# FrameScope CPU Voltage Telemetry and Sampling Intervals Report

Date: 2026-05-27

## Scope

This pass adds CPU per-core voltage telemetry plumbing, keeps CPU per-logical-processor frequency telemetry on `ActualFrequencyMHz`, and exposes telemetry sampling intervals in Settings.

Explicit non-goals honored:

- Did not launch BF6 or any real game.
- Did not run installers.
- Did not push GitHub or update Release.
- PresentMon raw frame data was not downsampled; FPS average, 1% Low, and 0.1% Low still use raw frame rows.

## Data Contract

The report layer already exposed `DATA.cpuVoltage` and accepted legacy voltage fields in `cpu-core-samples.csv`. This implementation keeps that fallback and adds a dedicated real voltage file:

`cpu-voltage-samples.csv`

```csv
Time,SampleIndex,ElapsedMs,Source,ProcessorGroup,LogicalProcessor,PhysicalCoreId,ThreadIndex,CoreVoltageV,SensorName,SensorIdentifier
```

Status is written to:

`cpu-voltage-telemetry-status.json`

Key status fields:

- `CpuVoltageTelemetryEnabled`
- `CpuVoltageAvailable`
- `CpuVoltageStatus`
- `CpuVoltageSource`
- `CpuVoltageUnavailableReason`
- `CpuVoltageSampleIntervalMs`
- `CpuVoltageSampleCount`
- `CpuVoltageLogicalProcessorCount`

Report manifest now includes:

- `cpuVoltageAvailable`
- `cpuVoltageStatus`
- `cpuVoltageSource`
- `cpuVoltageSampleCount`
- `cpuVoltageSampleIntervalMs`
- `cpuVoltageSamplesCsv`

`DATA.cpuVoltage` reads `cpu-voltage-samples.csv` first, then falls back to real voltage fields in legacy `cpu-core-samples.csv`. Old runs without voltage data remain readable and show a Chinese no-data reason instead of crashing.

## Provider Behavior

The real provider is source-labeled and conservative:

- `wmi-librehardwaremonitor` for `root\LibreHardwareMonitor`
- `wmi-openhardwaremonitor` for `root\OpenHardwareMonitor`
- `wmi-sensor` unavailable fallback

It only accepts sensor rows that look like real per-core voltage sensors:

- `SensorType` contains voltage.
- Sensor name or identifier contains `core` plus a core index.
- Rejects `VID`, `Vcore`, `package`, and `SOC`.
- Rejects invalid voltage values outside `0..5V`.

Current host probe result:

```json
{
  "CpuVoltageTelemetryEnabled": true,
  "CpuVoltageAvailable": false,
  "CpuVoltageStatus": "unavailable",
  "CpuVoltageSource": "wmi-sensor",
  "CpuVoltageUnavailableReason": "root\\LibreHardwareMonitor unavailable: 无效命名空间  | root\\OpenHardwareMonitor unavailable: 无效命名空间 ",
  "CpuVoltageSampleIntervalMs": 1000,
  "CpuVoltageSampleCount": 0
}
```

This means the code path is real, but this machine currently does not expose LibreHardwareMonitor/OpenHardwareMonitor per-core voltage WMI namespaces.

## Sampling Configuration

Defaults are now 1000ms for:

- `ProcessSampleIntervalMs`
- `SlowSampleIntervalMs`
- `CpuTelemetry.PerCoreSampleIntervalMs`
- `CpuTelemetry.PerCoreVoltageSampleIntervalMs`

Hardware telemetry is clamped to `500..5000ms`. If a saved value is lower than 500ms, the normalized config/status reflects the clamped value. Legacy explicit process intervals such as 100ms/250ms are preserved for existing configs, but new defaults and mock Settings preview use 1000ms.

Settings UI now includes a `采样间隔` group with:

- Background process interval
- System slow interval
- CPU core frequency interval
- CPU core voltage interval
- CPU core frequency toggle
- CPU core voltage toggle

Save-failure draft retention remains covered by the Settings interaction contract test.

## Evidence Artifacts

Synthetic voltage run:

`artifacts\cpu-voltage-20260527\synthetic-run`

- `cpu-voltage-samples.csv`: 24 real-shaped per-core voltage rows.
- Manifest: `cpuVoltageAvailable=true`, `cpuVoltageSource=synthetic-sensor`, `cpuVoltageSampleCount=24`.
- Report data: `DATA.cpuVoltage.available=true`, 4 voltage series, 6 chart points.

Unavailable run:

`artifacts\cpu-voltage-20260527\unavailable-run`

- No `cpu-voltage-samples.csv`.
- Manifest: `cpuVoltageAvailable=false`, `cpuVoltageStatus=unavailable`.
- Report data shows Chinese no-data reason and no fake VID/Vcore series.

Screenshots:

- Settings sampling interval area: `artifacts\cpu-voltage-20260527\screenshots\settings-sampling-intervals.png`
- CPU voltage synthetic chart: `artifacts\cpu-voltage-20260527\screenshots\report-cpu-voltage-synthetic.png`
- CPU voltage unavailable chart: `artifacts\cpu-voltage-20260527\screenshots\report-cpu-voltage-unavailable.png`
- CPU core frequency chart: `artifacts\cpu-voltage-20260527\screenshots\report-cpu-core-frequency.png`

WebView2 smoke:

- Live: `artifacts\cpu-voltage-20260527\webview2-live-smoke.json`
- Reduced motion: `artifacts\cpu-voltage-20260527\webview2-reduced-motion-smoke.json`

Both report `success=true`.

## Verification

Read before implementation:

- `docs\implementation-reports\2026-05-27-framescope-report-chart-data-implementation-report.md`
- `docs\implementation-reports\2026-05-25-framescope-cpu-core-frequency-telemetry-report.md`

Fresh verification run:

```text
tools\Run-Frontend.ps1 verify
PASS: TypeScript, 50 Vitest tests, Vite build

build.ps1
PASS

tests\Build-FrameScopeTests.ps1
PASS

tests\FrameScopeConfigStoreTests.exe
PASS

tests\FrameScopeSystemSamplerCpuCoreTests.exe
PASS

tests\FrameScopeReportManifestTests.exe
PASS

tests\FrameScopeWebBridgeTests.exe
PASS

tests\FrameScopeNativeMonitorChildProcessTests.exe
PASS

node tests\chart-sampling-tests.js
PASS

tests\FrameScopeDiagnosticsTests.exe
PASS

tests\FrameScopeProcessSamplerTests.exe
PASS

tests\FrameScopeProcessCleanupTests.exe
PASS

tests\FrameScopePresentMonDiagnosticsTests.exe
PASS

FrameScopeMonitor.exe --web-ui-smoke
PASS

FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion
PASS

git diff --check
PASS: exit 0; only CRLF normalization warnings

Residual process check
PASS: FrameScope/PresentMon process count 0; temporary Vite node count 0; task Edge profile process count 0
```
