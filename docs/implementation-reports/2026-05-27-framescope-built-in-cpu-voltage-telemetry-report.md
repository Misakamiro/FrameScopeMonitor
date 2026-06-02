# FrameScope Built-in CPU Voltage Telemetry Report

Date: 2026-05-27

Conclusion: PASS

Implementation result: FrameScope now has a bundled CPU voltage sensor provider path based on `LibreHardwareMonitorLib.dll`. It does not require the user to install, open, or keep `LibreHardwareMonitor.exe` / `OpenHardwareMonitor.exe` running. The implementation is conservative: only rows identified as real `per-core` voltage are allowed to become per-core voltage chart series. VID, aggregate Vcore, Package, SOC, SVI2/TFN, and motherboard-style voltage rows are recorded only as `non-per-core` with a reason.

Host result: this machine does not expose real measured per-core CPU voltage. The refreshed host probe found only aggregate `Vcore`, `Vcore SoC`, `Vcore Misc`, and `Core #1..#8 VID` sensors, so status is `non-per-core-only` and the report renders a Chinese no-data reason instead of a fake curve.

## Scope

Completed in this pass:

- Added the built-in sensor provider as the main `auto` CPU voltage path.
- Preserved WMI LibreHardwareMonitor/OpenHardwareMonitor as fallback.
- Preserved synthetic/static provider paths for tests.
- Added unavailable, load-failed, and permission-failed status handling.
- Expanded `cpu-voltage-samples.csv` so every voltage row carries source, provider, sensor, core identity, value, status, reason, and sensor identifier.
- Updated status, summary, manifest, and `DATA.cpuVoltage` so they distinguish `per-core-available`, `non-per-core-only`, provider unavailable, permission failure, and load failure.
- Kept CPU core frequency collection on `Processor Information(*)\Actual Frequency`; the refreshed host probe still wrote `cpu-core-samples.csv`.
- Kept Settings CPU voltage sampling interval default at `1000ms` with hardware telemetry clamp behavior.

Explicit non-goals honored:

- Did not test BF6.
- Did not launch any real game.
- Did not run an installer.
- Did not push GitHub or update a Release.

## Provider Design

Provider interface:

- `ICpuVoltageTelemetryProvider` exposes `Available`, `UnavailableReason`, `SourceLabel`, `ProviderKind`, and `ReadSamples()`.
- Built-in provider: `BuiltInLibreHardwareMonitorCpuVoltageTelemetryProvider`, source `builtin-librehardwaremonitor`, provider kind `built-in`.
- WMI fallback provider: `WmiCpuVoltageTelemetryProvider`, source `wmi-librehardwaremonitor` or `wmi-openhardwaremonitor`, provider kind `wmi`.
- Synthetic/static provider: `StaticCpuVoltageTelemetryProvider`, used by tests and disabled/unavailable states.
- Unavailable provider state: static provider with provider kind `unavailable`, `disabled`, or failure-specific status in telemetry JSON.

`auto` resolution now tries:

1. bundled `LibreHardwareMonitorLib.dll`
2. WMI fallback
3. unavailable provider with combined reason

The built-in provider loads `LibreHardwareMonitorLib.dll` from the app base directory, enables CPU and motherboard sensors, updates the hardware tree, reads voltage sensors, and classifies each sample before it can be written.

## CSV Contract

`cpu-voltage-samples.csv` header:

```csv
Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreId,PhysicalCoreId,ThreadIndex,VoltageVolts,Status,Reason,SensorIdentifier
```

Rules:

- `Status=per-core` rows may include `ProcessorGroup`, `LogicalProcessor`, `CoreId`, `PhysicalCoreId`, and `ThreadIndex`.
- `Status=non-per-core` rows intentionally leave per-core identity fields blank.
- `VoltageVolts` is accepted only in the valid `0..5V` range.
- `Reason` records why a non-per-core sensor was not charted as per-core voltage.

## Report And Chart Behavior

The report generator reads `cpu-voltage-telemetry-status.json` first, then uses run status/summary only as lower-priority fallback. This prevents stale `status.json` values from overriding the dedicated voltage sidecar.

`DATA.cpuVoltage` now carries:

- `status`
- `source`
- `providerKind`
- `providerRequested`
- `totalSampleCount`
- `perCoreSampleCount`
- `nonPerCoreSampleCount`
- `sampleIntervalMs`
- `samplesCsv`

The chart code filters out `non-per-core`, `non_per_core`, and `aggregate` rows before building series. If there are no real per-core rows, `DATA.cpuVoltage.series` stays empty and the report displays the Chinese reason from metadata. Non-per-core values are never drawn as per-core voltage curves.

## Package And License

NuGet package:

- Package: `LibreHardwareMonitorLib`
- Version: `0.9.6`
- Source: NuGet restore in `build.ps1`
- License: `MPL-2.0`
- Project: `https://github.com/LibreHardwareMonitor/LibreHardwareMonitor`
- Repository commit from nuspec: `3d331e3370efb858411f19511373eff65a218701`

Packaged files verified in `dist\FrameScopeMonitor-payload`:

- `LibreHardwareMonitorLib.dll`
- `HidSharp.dll`
- `DiskInfoToolkit.dll`
- `RAMSPDToolkit-NDD.dll`
- `BlackSharp.Core.dll`
- `System.Buffers.dll`
- `System.CodeDom.dll`
- `System.Memory.dll`
- `System.Numerics.Vectors.dll`
- `System.Runtime.CompilerServices.Unsafe.dll`
- `System.Security.AccessControl.dll`
- `System.Security.Principal.Windows.dll`
- `System.Threading.AccessControl.dll`

Admin/permission model:

- FrameScope does not require an external monitoring app process.
- The code does not hard-require administrator mode to start the provider.
- If a sensor path needs elevated access or driver access, the provider catches permission failures and writes `permission-failed` with the concrete reason.
- Missing DLL or reflection/load failures become `load-failed`.

Performance impact:

- Voltage sampling uses the existing hardware telemetry interval path.
- Default CPU voltage interval remains `1000ms`.
- Hardware telemetry clamp remains `500..5000ms`.
- The provider updates the LibreHardwareMonitor hardware tree only when the voltage sample is due.

## Host Evidence

Fresh host probe, no real game:

`artifacts\cpu-voltage-built-in-20260527\host-provider-probe-rerun2-final\host-provider-probe-summary.json`

Key results:

```json
{
  "samplerExitCode": 0,
  "requestedProvider": "auto",
  "cpuCoreCsvExists": true,
  "cpuCoreCsvRows": 208,
  "cpuCoreTelemetryAvailable": true,
  "cpuCoreLogicalProcessorCount": 16,
  "cpuVoltageCsvExists": true,
  "cpuVoltageCsvRows": 143,
  "cpuVoltagePerCoreCsvRows": 0,
  "cpuVoltageNonPerCoreCsvRows": 143,
  "cpuVoltageStatus": "non-per-core-only",
  "cpuVoltageSource": "builtin-librehardwaremonitor",
  "cpuVoltageProviderKind": "built-in",
  "cpuVoltageProviderRequested": "auto",
  "cpuVoltageUnavailableReason": "仅检测到 non-per-core CPU 电压传感器；图表只显示真实 per-core voltage。"
}
```

Sensors found:

- `Vcore`: `non-per-core`, reason `Aggregate Vcore 不是实测 per-core voltage。`
- `Vcore SoC`: `non-per-core`, reason `Aggregate Vcore 不是实测 per-core voltage。`
- `Vcore Misc`: `non-per-core`, reason `Aggregate Vcore 不是实测 per-core voltage。`
- `Core #1 VID` through `Core #8 VID`: `non-per-core`, reason `VID 是请求电压，不是实测 per-core voltage。`

Interpretation: the bundled provider is working on this host, but the host hardware/firmware/library exposure does not provide real measured per-core voltage sensors. No VID/Vcore/SOC/package value was used as a per-core voltage curve.

## Synthetic And Report Evidence

Synthetic monitor-session with per-core voltage:

`artifacts\cvbi\synthsession\synthetic-monitor-session-summary-final.json`

- `statusCpuVoltageStatus=per-core-available`
- `statusCpuVoltageProviderKind=synthetic`
- `voltageCsvRows=1`
- `voltageCsvPerCoreRows=1`
- `dataCpuVoltageSeriesCount=1`
- `dataCpuVoltagePointCount=1`

Host non-per-core report:

`artifacts\cvbi\host-non-per-core-report-summary-final.json`

- Manifest: `cpuVoltageStatus=non-per-core-only`
- Manifest: `cpuVoltagePerCoreAvailable=false`
- Manifest: `cpuVoltageNonPerCoreAvailable=true`
- DATA: `dataCpuVoltageSeriesCount=0`
- DATA: `dataCpuVoltagePointCount=0`
- CPU core frequency remained available: `dataCpuCoreFrequencySeriesCount=16`, `dataCpuCoreFrequencyPointCount=6`

Screenshots:

- CPU voltage with data: `artifacts\cpu-voltage-built-in-20260527\screenshots\final-cpu-voltage-data-1280.png`
- CPU voltage no-data reason: `artifacts\cpu-voltage-built-in-20260527\screenshots\final-cpu-voltage-no-data-1280.png`
- CPU core frequency chart: `artifacts\cpu-voltage-built-in-20260527\screenshots\final-cpu-core-frequency-1280.png`
- Settings CPU voltage interval area: `artifacts\cpu-voltage-built-in-20260527\screenshots\final-settings-cpu-voltage-interval-1280.png`

WebView2 smoke evidence:

`artifacts\cpu-voltage-built-in-20260527\webview2-smoke-final\webview2-smoke-summary-final.json`

- live smoke: `success=true`
- reduced-motion smoke: `success=true`
- Settings interval screenshot: CPU core interval `1000`, CPU voltage interval `1000`

## Verification

Fresh commands run after implementation:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
PASS: TypeScript, 5 Vitest files, 50 tests, Vite production build

powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
PASS: dependency restore and build completed; installers were generated but not executed

powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
PASS

.\tests\FrameScopeConfigStoreTests.exe
PASS

.\tests\FrameScopeCapturePlannerTests.exe
PASS

.\tests\FrameScopeReportProgressTests.exe
PASS

.\tests\FrameScopePresentMonDiagnosticsTests.exe
PASS

.\tests\FrameScopeSystemSamplerCpuCoreTests.exe
PASS

.\tests\FrameScopeProcessSamplerTests.exe
PASS

.\tests\FrameScopeNativeMonitorChildProcessTests.exe
PASS

.\tests\FrameScopeReportManifestTests.exe
PASS

.\tests\FrameScopeDiagnosticsTests.exe
PASS

.\tests\FrameScopePubgSimulatorTests.exe
PASS

.\tests\FrameScopeWebBridgeTests.exe
PASS

.\tests\FrameScopeWebHostLifecycleTests.exe
PASS

.\tests\FrameScopeProcessCleanupTests.exe
PASS

.\tests\FrameScopeWebView2RuntimeTests.exe
PASS

.\tests\FrameScopeUiStateTests.exe
PASS

C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tests\chart-sampling-tests.js
PASS

.\FrameScopeMonitor.exe --web-ui-smoke
PASS: exit 0

.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion
PASS: exit 0
```

Final checks run after writing this report:

- `git diff --check`: PASS, exit 0. Git reported LF-to-CRLF normalization warnings only.
- residual process check: PASS, `NO_MATCHING_RESIDUAL_PROCESSES` for this source tree's FrameScope, sampler, PresentMon, WebView2, and Node processes.

## Requirement Checklist

- Software-bundled voltage collection: PASS. The main `auto` path uses bundled `LibreHardwareMonitorLib.dll`.
- External LibreHardwareMonitor/OpenHardwareMonitor process not required: PASS.
- Real-only policy: PASS. Non-real or non-per-core voltage stays non-per-core/unavailable.
- VID/Vcore/Package/SOC misuse: PASS. These are not charted as per-core voltage.
- Real per-core chart only: PASS. The chart layer filters non-per-core rows.
- Host real per-core voltage availability: unavailable on this machine. Host status is `non-per-core-only`.
- Host unavailable reason: only aggregate Vcore/Vcore SoC/Vcore Misc and VID sensors were exposed.
- CPU core frequency preserved: PASS. Refreshed host probe wrote `cpu-core-samples.csv` with 208 rows and 16 logical processors.
- Settings CPU voltage interval default/clamp: PASS. Default remains `1000ms`; clamp remains in the hardware telemetry path.
- Retest recommendation: YES. Open a retest window on hardware that exposes true measured per-core voltage sensors; this host can only prove built-in provider load/read/classification and safe no-data behavior.
