# FrameScope chart dropout and CPU Core VID accuracy fix report

Date: 2026-06-13
Status: PASS

## Scope

Investigated and fixed report chart dropouts / bottom-spike rendering for invalid telemetry samples, plus inaccurate CPU Core VID display on the user's Valorant run.

User run used for evidence:

`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260613-134745`

Evidence folder:

`docs\test-reports\2026-06-13-framescope-chart-dropout-and-cpu-vid-fix-evidence`

## Root cause: chart vertical dropouts / bottom spikes

The report generator previously treated zero voltage, zero frequency, zero temperature, and zero power values as ordinary chart values. That means telemetry read failures or missing values written as `0` could be drawn as real samples. The chart sampling path then preserved those low points as spikes, producing vertical lines down to the bottom of the chart.

For this specific Valorant run, the raw CSV did not contain zero/invalid lows in the affected voltage/frequency files:

- `cpu-voltage-samples.csv`: 5300 Vcore samples, min 0.960V, max 1.104V, zero/sub-0.7 count 0.
- `system-samples.csv`: CPU/GPU/memory clocks, GPU power, and GPU temperature had zero count 0.
- GPU clock minimum was 225 MHz, which is a valid low GPU clock/P-state value rather than a failed zero read.

So the run proves the chart path needed invalid-sample defense, while this screenshot's VID value was a separate sensor-selection problem.

## Root cause: CPU Core VID 0.538 / 0.550V

The user's tooltip value came directly from `cpu-vid-samples.csv`, not from tooltip math or frontend rendering:

- Sensor names: `Core #1 VID` through `Core #8 VID`.
- Sensor identifiers: `/amdcpu/0/voltage/2` through `/amdcpu/0/voltage/9`.
- Values: every raw VID sample was in the 0.488V to 0.681V range; 42400 / 42400 samples were below 0.7V.

The same run recorded actual CPU Voltage / Vcore separately from SuperIO:

- Sensor name: `Vcore`.
- Sensor identifier: `/lpc/it8689e/0/voltage/0`.
- Values: 0.960V to 1.104V, matching the user's expected approximately 1.08V Vcore reading.

LibreHardwareMonitor's AMD Core VID readings for this machine are therefore implausibly low for the user's expected voltage口径. There was no reliable per-core requested VID sensor near 1.08V in the available evidence. The fix rejects this known-bad AMD LHM Core VID range; it does not multiply by 2 and does not substitute Vcore as VID.

## Fixes

Changed files:

- `src\reporting\FrameScopeReportGenerator.SystemData.cs`
  - Frequency, power, and temperature chart data now converts invalid zero/non-finite samples to `null` gaps.
  - CPU core frequency ignores invalid zero/non-finite values.
  - CPU Voltage / Vcore accepts only plausible voltage values above 0.2V and below 5V.
  - CPU Core VID uses the same plausible voltage bounds and rejects AMD LibreHardwareMonitor Core VID values below 0.7V when the sensor text identifies `amdcpu`, `core`, and `vid`.
  - If all VID points are rejected for that reason, `DATA.cpuVid.available=false` and the reason explains the AMD low-range rejection.

- `src\monitoring\FrameScopeSystemSampler.CpuCoreTelemetry.cs`
  - Future collection rejects implausibly low AMD LHM Core VID samples before writing `cpu-vid-samples.csv`.
  - `cpu-vid-telemetry-status.json` gains `CpuVidRejectedSampleCount`.
  - Rejected-only VID telemetry reports an explicit unavailable reason instead of publishing false low VID data.

- `tests\FrameScopeReportManifestTests.cs`
  - Added coverage that invalid zero voltage/frequency/power/temperature samples do not become plotted zero spikes.
  - Added coverage that low AMD LHM Core VID is rejected while Vcore remains available.

- `tests\FrameScopeSystemSamplerCpuCoreTests.cs`
  - Added coverage that the sampler rejects low AMD LHM Core VID and records the rejection count.

- `tests\chart-sampling-tests.js`
  - Added coverage that chart downsampling preserves `null` gaps and does not convert them into zero-value spikes.

## Before / after evidence

Before regenerating the report:

- `DATA.fps.bucketMs`: 1000.
- `DATA.cpuVoltage.available`: true; series `CPU 电压 / Vcore`, min 0.960V, max 1.104V.
- `DATA.cpuVid.available`: true; 8 per-core series, every point below 0.7V, examples 0.531V / 0.538V / 0.550V.
- `DATA.system.perf.cpuFreq/gpuClock/memClock`: no zero/sub-0.7 values in this run.

After regenerating the same Valorant run with the fixed generator:

- `DATA.fps.bucketMs`: 1000.
- `DATA.cpuVoltage.available`: true; series remains `CPU 电压 / Vcore`, min 0.960V, max 1.104V.
- `DATA.cpuVid.available`: false.
- `DATA.cpuVid.reason`: `AMD LibreHardwareMonitor Core VID samples were rejected because every value was in the implausible low 0.4-0.7V range; CPU Voltage / Vcore remains separate and is not used as VID.`
- `DATA.cpuVid.series`: empty.
- `DATA.system.perf.cpuFreq/gpuClock/memClock`: still no invalid zero lows; true GPU low P-state 225 MHz is preserved.

Evidence files:

- `before-report-stats.json`
- `after-report-stats.json`
- `raw-csv-stats.json`
- `layout-probe\report-overflow-probe.json`
- `layout-probe\chart-nonblank-summary.json`

## Separation guarantees

- Confirmed `DATA.cpuVoltage` remains the actual overall CPU Voltage / Vcore chart.
- Confirmed `DATA.cpuVid` remains the per-core requested/target VID chart and is not filled from Vcore.
- Confirmed no unconditional VID `* 2` correction was added.
- Confirmed `bucketMs=1000` remains unchanged.
- Confirmed raw PresentMon frame statistics remain source of truth.

## Verification

Commands run:

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`: PASS, 64 frontend tests passed, production build passed.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
- `.\tests\FrameScopeReportManifestTests.exe`: PASS.
- `.\tests\FrameScopeDiagnosticsTests.exe`: PASS.
- `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`: PASS.
- `.\tests\FrameScopeNativeWatcherPolicyTests.exe`: PASS.
- `.\tests\FrameScopeNativeMonitorChildProcessTests.exe`: PASS.
- `.\tests\FrameScopeProcessCleanupTests.exe`: PASS.
- `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe`: PASS after stopping a pre-existing installed `FrameScopeMonitor.exe` that held the UI mutex.
- Bundled Node `.\tests\chart-sampling-tests.js`: PASS.
- Layout probe at 1280x720 and 900x760: PASS, `allNoOverflow=true`.
- Chart nonblank pixel probe for FPS, CPU core frequency, CPU Voltage / Vcore, and performance frequency charts at both viewport sizes: PASS.
- Regenerated the user's Valorant run report with fixed generator: PASS.
- `git diff --check`: PASS, only CRLF warnings.
- Residual process check: `NO_MATCHING_RESIDUAL_PROCESSES`.

## Explicitly not done

- Did not install FrameScope.
- Did not run setup or full setup.
- Did not start a real game.
- Did not test BF6.
- Did not push GitHub.
- Did not update Release.
- Did not touch GameLite/lightweight files.
