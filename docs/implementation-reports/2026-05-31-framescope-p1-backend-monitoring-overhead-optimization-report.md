# FrameScope P1 Backend Monitoring Overhead Optimization Report

Date: 2026-05-31

Conclusion: PASS

## Scope

This round only handled the P1 backend monitoring overhead item. It did not handle large-report process chart interaction, frontend page performance, UI animation, logging retention/size guards, build artifact sync, install/update validation, GitHub push, or Release updates.

Hard boundaries followed:

| Boundary | Result |
| --- | --- |
| Packaging / installer build | Not run |
| FrameScope install | Not run |
| Real game launch | Not run |
| BF6 test | Not run |
| GitHub push | Not run |
| Release update | Not run |
| `build.ps1` | Not run |
| TelemetrySampleIntervalMs | Preserved at 1000 ms in before/after evidence |
| CPU Voltage / Vcore | Still independent, still recorded to `cpu-voltage-samples.csv` |
| CPU Core VID | Still independent, still recorded to `cpu-vid-samples.csv` |
| Vcore/VID mixing | Not introduced |
| FPS/report chart/P0 report generation output | Not changed |

## Baseline Method

Main before/after evidence uses the same synthetic-monitoring shape as the performance analysis: a fake `TslGame.exe` target and `FakePresentMon.exe`, with `FrameScopeMonitor.exe` starting `FrameScopeProcessSampler.exe` and `FrameScopeSystemSampler.exe`.

For stable row counts, I used a test-only synthetic target built under `artifacts\p1bm\bin\TslGame.exe` that lives for 15 seconds. Capture duration remained 12 seconds, interval remained 1000 ms, and the target exits naturally during the monitor stop window so sampler buffers flush. No real game was launched.

Main evidence files:

- Before: `artifacts\p1bm\before-final-cpu-total.json`
- After: `artifacts\p1bm\after-final-cpu-total.json`
- Measurement/runtime scripts: `artifacts\p1-backend-monitoring-overhead-optimization-20260531\tools\`

Earlier exploratory runs with longer artifact paths or a 25-second synthetic target were not used as final evidence because they either hit path-length/timing noise or forced sampler shutdown.

## Before Baseline

| Run | SystemSampler CPU | CPU seconds | WS MB | Private MB | process rows | system rows | cpu-core rows | cpu-voltage rows | cpu-vid rows | interval |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| before 1 | 7.42% | 1.0938 | 53.23 | 62.97 | 1442 | 14 | 224 | 14 | 112 | 1000 ms |
| before 2 | 6.56% | 0.9688 | 53.37 | 63.10 | 1456 | 14 | 224 | 14 | 112 | 1000 ms |
| average | 6.99% | 1.0313 | 53.30 | 63.04 | 1449 | 14 | 224 | 14 | 112 | 1000 ms |

Status JSON before: `Phase=done`, `CpuVoltageStatus=vcore-available`, `CpuVidStatus=core-vid-available`, `CpuCoreSampleIntervalMs=1000`, `CpuVoltageSampleIntervalMs=1000`, `CpuVidSampleIntervalMs=1000`.

## Hotspots Found

1. `FrameScopeSystemSampler` created separate built-in LibreHardwareMonitor providers for CPU Voltage / Vcore and CPU Core VID even when both requested `auto`. That opened duplicate hardware provider state and walked the same sensor tree twice.
2. Built-in sensor reading fetched `SensorType`, `Value`, `Name`, and `Identifier` separately for Vcore and VID classification. The two telemetry families are semantically separate, but the raw sensor property read can be shared.
3. CPU core telemetry allocated new `CpuCoreCounterSample` objects and reparsed processor identities on every sample.
4. CPU core / Vcore / VID status sidecars were rewritten on every successful sample. Final status needs to be exact, but repeated identical sidecar writes during capture were unnecessary.
5. System sampling enumerated target process presence, `cs2` presence, and process count separately. One process snapshot can provide all three values without changing fields.

Process sampling was inspected and left functionally unchanged; it remains an all-process sampler and still emits grouped process rows/top CPU/top IO/alerts. Watcher/monitor lifecycle and launch arguments still pass the same telemetry intervals and keep CPU core, CPU Voltage / Vcore, and CPU Core VID enabled.

## Files Changed

Product/test changes:

| File | Change |
| --- | --- |
| `src\monitoring\FrameScopeSystemSampler.cs` | Creates CPU hardware telemetry providers once and passes shared instances into Vcore/VID sessions; uses one process snapshot for system row process fields. |
| `src\monitoring\FrameScopeSystemSampler.PerfCounters.cs` | Shares built-in LibreHardwareMonitor provider between Vcore and VID when both use built-in/auto; caches same-round sensor tree reads; reuses CPU core sample objects. |
| `src\monitoring\FrameScopeSystemSampler.CpuCoreTelemetry.cs` | Reuses per-batch timestamp strings and throttles sidecar status writes while preserving final status on dispose. |
| `src\monitoring\FrameScopeSystemSampler.Models.cs` | Adds cached CPU core identity fields to `CpuCoreCounterSample`. |
| `src\monitoring\FrameScopeSystemSampler.Processes.cs` | Adds one-pass system process snapshot. |
| `tests\FrameScopeSystemSamplerCpuCoreTests.cs` | Adds regression coverage for sharing built-in Vcore/VID hardware provider requests. |

Evidence-only artifacts:

| Path | Purpose |
| --- | --- |
| `artifacts\p1-backend-monitoring-overhead-optimization-20260531\tools\Build-MeasurementRuntime.ps1` | Compiles measurement-only runtimes under artifacts; no packaging/installer. |
| `artifacts\p1-backend-monitoring-overhead-optimization-20260531\tools\Measure-BackendMonitorOverhead.ps1` | Runs synthetic monitor measurements and records CPU/memory/rows/schema/status fields. |
| `artifacts\p1bm\SyntheticTslGame.cs` and `artifacts\p1bm\bin\TslGame.exe` | Test-only fake target. |

## Safety Notes

| Optimization | Why it does not affect function |
| --- | --- |
| Shared built-in provider | Only shares the LibreHardwareMonitor `Computer` instance for provider requests that already resolve to built-in/auto. Vcore and VID still use separate session classes, CSV files, columns, statuses, and classifications. |
| Same-round sensor cache | Reads the hardware tree once and then classifies the same raw sensor snapshot into Vcore and VID. It does not synthesize unavailable data and does not accept VID as Vcore. |
| Single sensor property read | Reads `SensorType`, `Value`, `Name`, and `Identifier` once per sensor, then runs the existing Vcore and VID classifiers separately. |
| CPU core sample reuse | Reuses containers and pre-parsed logical processor identity; counter values are still read every sample and written to the same schema. |
| Status write throttling | First status is written, periodic status is written every 5 samples, and final dispose writes the authoritative final status. Final fields/counts/reasons stay unchanged. |
| One process snapshot | `Cs2Running`, `TargetRunning`, and `ProcessCount` still come from live process enumeration; it avoids repeated enumerations in the same sample. |

## After Results

| Run | SystemSampler CPU | CPU seconds | WS MB | Private MB | process rows | system rows | cpu-core rows | cpu-voltage rows | cpu-vid rows | interval |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| after 1 | 4.63% | 0.6719 | 51.04 | 60.64 | 1442 | 14 | 224 | 14 | 112 | 1000 ms |
| after 2 | 4.55% | 0.6562 | 50.89 | 60.41 | 1444 | 14 | 224 | 14 | 112 | 1000 ms |
| average | 4.59% | 0.6640 | 50.97 | 60.53 | 1443 | 14 | 224 | 14 | 112 | 1000 ms |

Status JSON after: `Phase=done`, `CpuVoltageStatus=vcore-available`, `CpuVidStatus=core-vid-available`, `CpuCoreSampleIntervalMs=1000`, `CpuVoltageSampleIntervalMs=1000`, `CpuVidSampleIntervalMs=1000`.

## Before / After Comparison

| Metric | Before avg | After avg | Change |
| --- | ---: | ---: | ---: |
| SystemSampler single-core CPU | 6.99% | 4.59% | 34.33% lower |
| SystemSampler CPU seconds | 1.0313 | 0.6640 | 35.62% lower |
| SystemSampler working set | 53.30 MB | 50.97 MB | 4.38% lower |
| SystemSampler private memory | 63.04 MB | 60.53 MB | 3.98% lower |
| ProcessSampler single-core CPU | 1.12% | 0.84% | 25.00% lower |
| ProcessSampler working set | 27.18 MB | 27.22 MB | effectively unchanged |
| ProcessSampler private memory | 49.65 MB | 49.75 MB | effectively unchanged |
| Process sample rows | 1449 avg | 1443 avg | equivalent; background process count varied |
| System sample rows | 14 | 14 | unchanged |
| CPU core rows | 224 | 224 | unchanged |
| CPU Voltage / Vcore rows | 14 | 14 | unchanged |
| CPU Core VID rows | 112 | 112 | unchanged |
| Sample interval | 1000 ms | 1000 ms | unchanged |
| Status JSON | `done`, Vcore available, VID available | same | unchanged status/reason口径 |
| CSV schema | baseline | same | unchanged |

CSV schema comparison:

| CSV | Fields |
| --- | --- |
| system | `Time, SampleIndex, Cs2Running, TargetRunning, TotalCpuPct, CpuFrequencyMHz, CpuPerformancePct, AvailableMB, DiskAvgSecPerTransfer, DiskBytesPerSec, NetBytesPerSec, GpuUtilPct, GpuMemUtilPct, GpuTempC, GpuPState, GpuClockMHz, MemClockMHz, PowerW, VramUsedMiB, VramTotalMiB, ProcessCount` |
| cpu-core | `Time, SampleIndex, ElapsedMs, Source, ProcessorGroup, LogicalProcessor, PhysicalCoreId, ThreadIndex, ActualFrequencyMHz, ProcessorFrequencyMHz, ProcessorPerformancePct, PercentOfMaximumFrequency, ProcessorUtilityPct, PerformanceLimitFlags` |
| cpu-voltage | `Time, SampleIndex, ElapsedMs, Source, Provider, SensorName, ProcessorGroup, LogicalProcessor, CoreId, PhysicalCoreId, ThreadIndex, VoltageVolts, Status, Reason, SensorIdentifier` |
| cpu-vid | `Time, SampleIndex, ElapsedMs, Source, Provider, SensorName, ProcessorGroup, LogicalProcessor, CoreIndex, PhysicalCoreId, ThreadIndex, VidVolts, Status, Reason, SensorIdentifier` |

## Explicit Semantic Answers

| Requirement | Answer |
| --- | --- |
| TelemetrySampleIntervalMs preserved? | Yes. Before and after use 1000 ms for system/process/cpu-core/cpu-voltage/cpu-vid. No interval was raised. |
| CPU Voltage / Vcore still independent? | Yes. It remains `cpu-voltage-samples.csv`, `VoltageVolts`, `Status=vcore`, `CpuVoltageStatus=vcore-available`. |
| CPU Core VID still independent? | Yes. It remains `cpu-vid-samples.csv`, `VidVolts`, `Status=core-vid`, `CpuVidStatus=core-vid-available`. |
| VID used as Vcore? | No. VID still goes only through VID classification and CSV. |
| Vcore used as VID? | No. Vcore still goes only through Vcore classification and CSV. |
| Required samples reduced? | No. system/cpu-core/cpu-voltage/cpu-vid row counts are unchanged in the final evidence. |
| Fake unavailable data introduced? | No. Unavailable providers still write unavailable status/reason; no synthetic value is emitted. |
| Report fields missing? | No report generator/schema changes were made. Chart sampling tests still pass. |

## Verification

| Command / check | Result |
| --- | --- |
| Before backend monitor overhead, 2 runs | PASS. `artifacts\p1bm\before-final-cpu-total.json`; rows/status/schema recorded. |
| After backend monitor overhead, 2 runs | PASS. `artifacts\p1bm\after-final-cpu-total.json`; rows/status/schema recorded. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS. Script added 110 packages as normal verify behavior; typecheck PASS; Vitest 5 files / 57 tests PASS; Vite build PASS. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS. `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS. |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS. |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS. |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS. `chart-sampling-tests: PASS`. |
| `tools\Probe-ReportHtmlLayout.js` | Not run; report HTML/layout output was not changed in this backend sampler-only round. |
| `git diff --check` | PASS after report creation; existing LF-to-CRLF warnings only, no whitespace errors. |
| Residual process check | PASS after report creation: `NO_MATCHING_RESIDUAL_PROCESSES`. |

## Risk

Residual risk is low to medium. The optimization changes provider lifetime and status write cadence, so I specifically verified CPU Voltage / Vcore and CPU Core VID remain independently available with unchanged CSV schemas and row counts. CPU savings depend on hardware sensor/provider behavior; memory savings should be more stable because duplicate built-in provider state is avoided.

## Final Result

PASS.

Backend monitoring overhead was reduced without changing sample intervals, telemetry families, CSV schemas, final status semantics, report generator output, FPS behavior, or P0 report-generation optimizations. No packaging, install, real game launch, BF6 test, GitHub push, Release update, frontend animation work, large-report process chart work, or log-retention work was performed.
