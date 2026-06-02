# FrameScope CPU Logical Processor Frequency Telemetry Implementation Report

Date: 2026-05-25

Source root:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Scope

This pass implemented the first CPU frequency telemetry stage only:

- Collect Windows `Processor Information(*)\Actual Frequency` per logical processor.
- Write the new `cpu-core-samples.csv` artifact.
- Keep `process-samples.csv` semantics unchanged.
- Keep `system-samples.csv` as the existing fixed-width summary table.
- Leave `PhysicalCoreId` and `ThreadIndex` empty in this phase.
- Record CPU voltage as unavailable only.
- Keep report UI/charts unchanged.
- Add report manifest metadata only:
  - `cpuCoreSampleCount`
  - `cpuCoreTelemetryAvailable`
  - `cpuVoltageAvailable`

Explicitly not done:

- No LibreHardwareMonitor integration.
- No OpenHardwareMonitor integration.
- No Vcore/VID/package-voltage fabrication or promise.
- No CPU core frequency chart UI.
- No GitHub push or release publishing.
- No local install/update.

`build.ps1` was run because it was part of the requested verification list. The existing script regenerates local `dist` installer artifacts as a side effect; no installer was run and no release was published.

## Implementation

### System Sampler

Updated:

- `src\monitoring\FrameScopeSystemSampler.cs`
- `src\monitoring\FrameScopeSystemSampler.Models.cs`
- `src\monitoring\FrameScopeSystemSampler.PerfCounters.cs`
- `src\monitoring\FrameScopeSystemSampler.IO.cs`
- `src\monitoring\FrameScopeSystemSampler.CpuCoreTelemetry.cs`

Added command-line options:

- `--enable-cpu-core-telemetry true|false`
- `--cpu-core-csv <path>`
- `--cpu-core-status <path>`
- `--cpu-core-interval <ms>`

CSV schema:

```text
Time,SampleIndex,ElapsedMs,Source,ProcessorGroup,LogicalProcessor,PhysicalCoreId,ThreadIndex,ActualFrequencyMHz,ProcessorFrequencyMHz,ProcessorPerformancePct,PercentOfMaximumFrequency,ProcessorUtilityPct,PerformanceLimitFlags
```

Behavior:

- `Source` is `windows-perfcounter`.
- `ProcessorGroup` / `LogicalProcessor` are parsed from Windows counter instances such as `0,15`.
- `PhysicalCoreId` and `ThreadIndex` stay empty.
- Sample interval defaults to `1000 ms` and is clamped to at least `500 ms`.
- If `Actual Frequency` is unavailable, sampler writes `cpu-core-telemetry-status.json` with a clear unavailable reason and does not create noisy data CSV rows.
- If telemetry is disabled, it creates neither `cpu-core-samples.csv` nor `cpu-core-telemetry-status.json`.
- CPU voltage status is always:
  - `CpuVoltageAvailable=false`
  - `CpuVoltageStatus=unavailable`

### Monitor Session

Updated:

- `src\app\FrameScopeNativeMonitor.MonitorSession.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession.Models.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession.Paths.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession.Status.cs`
- `src\app\FrameScopeNativeMonitor.Watcher.cs`

Behavior:

- Monitor-session now owns paths for:
  - `cpu-core-samples.csv`
  - `cpu-core-telemetry-status.json`
- Watcher passes config-derived CPU telemetry options into monitor-session.
- Monitor-session passes CPU telemetry options into `FrameScopeSystemSampler.exe`.
- `status.json` and `summary.json` include CPU telemetry availability, sample count, and voltage unavailable state.
- A missing/unavailable `Actual Frequency` counter does not fail monitor-session.

### Config And Mock Data

Updated:

- `src\core\FrameScopeConfigStore.cs`
- `framescope-config.example.json`
- `tests\FrameScopeConfigStoreTests.cs`
- `src\frontend\src\data\mockPreview.ts`
- `src\frontend\src\data\mockPreview.test.ts`

Config behavior:

- `CpuTelemetry.CollectPerCoreFrequency` now defaults to `true`.
- `CpuTelemetry.CollectCpuVoltage` remains `false`.
- `CpuTelemetry.PerCoreSampleIntervalMs` defaults to `1000` and is clamped to `500..5000`.
- Explicit disabled telemetry is preserved and tested.

### Report Generator

Updated:

- `src\reporting\FrameScopeReportGenerator.Metadata.cs`
- `src\reporting\FrameScopeReportGenerator.cs`
- `tests\FrameScopeReportManifestTests.cs`

Behavior:

- Counts `cpu-core-samples.csv` rows.
- Reads CPU telemetry availability/status metadata.
- Writes only manifest fields:
  - `cpuCoreSampleCount`
  - `cpuCoreTelemetryAvailable`
  - `cpuVoltageAvailable`
- Does not add CPU core raw rows, metadata, chart data, chart tabs, or visible UI controls to `framescope-interactive-data.js` or HTML.

## Verification

### Build

Passed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Output:

```text
Build complete: ...\dist\FrameScopeMonitor-Setup.exe
Full setup complete: ...\dist\FrameScopeMonitor-Full-Setup.exe
```

### Test Build

Passed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
```

Output:

```text
FrameScope tests rebuilt.
```

### Sampler And Report Tests

Passed:

```powershell
.\tests\FrameScopeSystemSamplerCpuCoreTests.exe
.\tests\FrameScopeReportManifestTests.exe
.\tests\FrameScopeNativeMonitorChildProcessTests.exe
```

Coverage added:

- CPU core CSV header/schema.
- Windows counter instance parsing.
- Disabled telemetry creates no noise files.
- Unavailable `Actual Frequency` records unavailable reason and keeps session successful.
- CPU voltage remains unavailable.
- Report manifest receives CPU telemetry metadata only.
- Report data does not receive raw CPU core samples or metadata.

### Full Local C# Test Set

Passed:

```powershell
.\tests\FrameScopeConfigStoreTests.exe
.\tests\FrameScopeCapturePlannerTests.exe
.\tests\FrameScopeReportProgressTests.exe
.\tests\FrameScopePresentMonDiagnosticsTests.exe
.\tests\FrameScopeSystemSamplerCpuCoreTests.exe
.\tests\FrameScopeReportManifestTests.exe
.\tests\FrameScopeDiagnosticsTests.exe
.\tests\FrameScopePubgSimulatorTests.exe
.\tests\FrameScopeWebBridgeTests.exe
.\tests\FrameScopeWebHostLifecycleTests.exe
.\tests\FrameScopeWebView2RuntimeTests.exe
.\tests\FrameScopeNativeMonitorChildProcessTests.exe
```

Result:

```text
All C# tests passed.
```

### Frontend Verify

Passed because config/mock defaults changed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result:

```text
5 test files passed
48 tests passed
vite build completed
```

### Synthetic Monitor Session

Evidence files:

- `artifacts\cpu-core-telemetry-20260525\synthetic-monitor-session-summary.json`
- `artifacts\cpu-core-telemetry-20260525\runs\CpuCoreSynthetic-20260525-233216\cpu-core-samples.csv`
- `artifacts\cpu-core-telemetry-20260525\runs\CpuCoreSynthetic-20260525-233216\charts\framescope-interactive-manifest.json`

Result:

```json
{
  "rowCount": 128,
  "uniqueLogicalProcessorCount": 16,
  "actualFrequencyMinMHz": 3670,
  "actualFrequencyMaxMHz": 5019,
  "actualFrequencyUniqueCount": 123,
  "allActualFrequencyEquals4200": false,
  "manifestCpuCoreSampleCount": 128,
  "manifestCpuCoreTelemetryAvailable": true,
  "manifestCpuVoltageAvailable": false,
  "dataContainsCpuCoreChartOrRawData": false,
  "dataContainsCpuCoreMetadata": false
}
```

### Local 7800X3D Counter Verification

Evidence files:

- `artifacts\cpu-core-telemetry-20260525\local-processor-information-summary.json`
- `artifacts\cpu-core-telemetry-20260525\local-processor-information-counter-samples.csv`

Result:

```json
{
  "cpuName": "AMD Ryzen 7 7800X3D 8-Core Processor",
  "cpuCores": 8,
  "cpuLogicalProcessors": 16,
  "cpuMaxClockSpeedMHz": 4200,
  "actualFrequencyLogicalProcessorCount": 16,
  "actualFrequencySampleCount": 64,
  "actualFrequencyMinMHz": 3928,
  "actualFrequencyMaxMHz": 4888,
  "actualFrequencyUniqueCount": 64,
  "actualFrequencyAll4200MHz": false,
  "processorFrequencyUniqueValues": [4200]
}
```

This proves the implemented source is the dynamic Windows `Actual Frequency` counter, not the fixed `Processor Frequency` value.

### Disabled Telemetry

Evidence file:

- `artifacts\cpu-core-telemetry-20260525\disabled-monitor-session-summary.json`

Result:

```json
{
  "monitorExitCode": 0,
  "cpuCoreCsvExists": false,
  "cpuCoreStatusSidecarExists": false,
  "statusCpuCoreTelemetryEnabled": false,
  "statusCpuCoreSampleCount": 0,
  "statusCpuVoltageAvailable": false,
  "statusCpuVoltageStatus": "unavailable"
}
```

### Git Diff Check

Passed:

```powershell
git diff --check
```

Only line-ending warnings were printed by Git for existing Windows CRLF normalization; no whitespace errors were reported.

### Residual Process Check

Passed after tests and synthetic sessions completed:

```powershell
Get-Process | Where-Object { $_.ProcessName -match 'FrameScope|PresentMon|FrameScopeNativeMonitorChildProcessTests' }
```

Result:

```text
No matching residual processes.
```

## Notes

- The workspace already had many unrelated modified/untracked files before this CPU telemetry pass. This implementation did not revert or overwrite those changes.
- `build.ps1` and frontend verify regenerated normal build artifacts such as `dist` and `src\frontend\dist`.
- `tools\Run-Frontend.ps1 verify` also restored `src\frontend\node_modules` for frontend verification.
