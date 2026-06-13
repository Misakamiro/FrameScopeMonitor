# FrameScope chart dropout and CPU Core VID accuracy fix retest

Date: 2026-06-13
Status: PASS

## Scope

This was a retest-only pass for the "chart bottom spike / dropout + CPU Core VID accuracy" fix.

I did not modify source code, fix bugs, package, install, start a real game, test BF6, push GitHub, or update Release.

Workspace:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Implementation report checked:

`docs\implementation-reports\2026-06-13-framescope-chart-dropout-and-cpu-vid-accuracy-fix-report.md`

User Valorant run checked:

`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260613-134745`

Report used for after validation:

`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260613-134745\charts\framescope-interactive-report.html`

Retest evidence folder:

`docs\test-reports\2026-06-13-framescope-chart-dropout-and-cpu-vid-accuracy-fix-retest-evidence`

## Result

PASS.

The regenerated/after Valorant report no longer plots the bad AMD Core VID 0.4-0.7V samples as a CPU VID chart. `DATA.cpuVid.available=false`, `DATA.cpuVid.series` is empty, and the reason explicitly says AMD LibreHardwareMonitor Core VID samples were rejected because every value was in the implausible low 0.4-0.7V range; CPU Voltage / Vcore remains separate and is not used as VID.

CPU Voltage / Vcore remains available and displays the real Vcore range from the run: 0.960-1.104V.

No evidence of invalid 0 / extremely low failed telemetry being drawn as bottom spikes was found in the after chart DATA for CPU Voltage, CPU frequency, GPU clock, memory clock, GPU power, or GPU temperature. The real GPU low P-state 225 MHz is preserved.

## Valorant run evidence

### CPU Voltage / Vcore

Raw CSV:

- File: `cpu-voltage-samples.csv`
- Header remains English/schema-compatible: `Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,...,VoltageVolts,...,SensorIdentifier`
- Samples: 5300
- Sensor: `Vcore`
- Identifier: `/lpc/it8689e/0/voltage/0`
- Source: `builtin-librehardwaremonitor`
- Range: 0.960-1.104V
- Zero/sub-0.7 count: 0
- Example values include 1.068V and 1.080V.

After DATA:

- `DATA.cpuVoltage.available=true`
- Series name/key: `CPU 电压 / Vcore`, `cpu-voltage:vcore`
- Count: 5300
- Range: 0.960-1.104V
- Zero/sub-0.7 count: 0

Conclusion: the user's expected around 1.08V reading is Vcore, not CPU Core VID.

### CPU Core VID

Raw CSV:

- File: `cpu-vid-samples.csv`
- Header remains English/schema-compatible: `Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,...,VidVolts,...,SensorIdentifier`
- Samples: 42400
- Sensors: `Core #1 VID` through `Core #8 VID`
- Identifiers: `/amdcpu/0/voltage/2` through `/amdcpu/0/voltage/9`
- Source: `builtin-librehardwaremonitor`
- Range: 0.488-0.681V
- Values below 0.7V: 42400 / 42400
- The screenshot-style low values such as 0.538V / 0.550V are consistent with these raw AMD LHM Core VID samples, not tooltip math.

After DATA:

- `DATA.cpuVid.available=false`
- `DATA.cpuVid.seriesCount=0`
- `DATA.cpuVid.reason`: AMD LibreHardwareMonitor Core VID samples were rejected because every value was in the implausible low 0.4-0.7V range; CPU Voltage / Vcore remains separate and is not used as VID.
- `DATA.cpuVid.note`: VID is CPU per-core request/target voltage, not real Vcore; it is separated from CPU Voltage / Vcore.

Conclusion: Vcore was not inserted into `DATA.cpuVid`; the VID/Vcore separation is preserved.

### CPU/GPU/memory frequency and low P-state

Raw `system-samples.csv`:

- Header remains English/schema-compatible.
- `CpuFrequencyMHz`: 5300 samples, min/max 4201 MHz, zero count 0.
- `GpuClockMHz`: 5300 samples, min 225 MHz, max 1995 MHz, zero count 0.
- `MemClockMHz`: 5300 samples, min 9251 MHz, max 9501 MHz, zero count 0.
- `GpuTempC`: 5300 samples, min 49C, max 69C, zero count 0.
- `PowerW`: 5300 samples, min 88.37W, max 344.65W, zero/sub-0.7 count 0.

After DATA:

- `DATA.system.perf.cpuFreq`: count 5300, min 4396 MHz, max 5037 MHz, zero/sub-0.7 count 0.
- `DATA.system.perf.gpuClock`: count 5300, min 225 MHz, max 1995 MHz, zero/sub-0.7 count 0.
- `DATA.system.perf.memClock`: count 5300, min 9251 MHz, max 9501 MHz, zero/sub-0.7 count 0.
- `DATA.system.io.power`: count 5300, min 88.37W, max 344.65W, zero/sub-0.7 count 0.
- `DATA.system.io.temp`: count 5300, min 49C, max 69C, zero/sub-0.7 count 0.

Conclusion: the real GPU 225 MHz low P-state is still present and was not wrongly filtered.

## Chart and layout probe

Command:

`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tools\Probe-ReportHtmlLayout.js --report "...\framescope-interactive-report.html" --out .\docs\test-reports\2026-06-13-framescope-chart-dropout-and-cpu-vid-accuracy-fix-retest-evidence\layout-probe`

Result:

- Exit code: 0
- `allNoOverflow=true`
- Scenarios: 23
- Viewports covered: 1280x720 and 900x760
- Failing overflow scenarios: none

Key chart views checked:

- CPU core frequency: nonblank at 1280x720 and 900x760.
- CPU Voltage / Vcore: nonblank at 1280x720 and 900x760.
- CPU Core VID unavailable state: nonblank at 1280x720 and 900x760.
- Performance frequency chart: nonblank at 1280x720 and 900x760.

Pixel probe result:

- `allNonblank=true`
- CPU Voltage screenshots sampled 94-140 unique colors.
- CPU core frequency screenshots sampled 45-55 unique colors.
- Performance chart screenshots sampled 395-508 unique colors.
- CPU VID unavailable screenshots sampled 38-44 unique colors.

Visual conclusion: the after report does not show obvious bottom-spike vertical lines caused by invalid 0 / extremely low samples in the checked CPU Voltage and frequency charts. CPU VID correctly renders unavailable instead of drawing the bad low VID series.

## Regression checks

- Chart localization did not regress: report tabs and chart controls remain Chinese; `tests\chart-sampling-tests.js` also checks `CPU 电压 / Vcore`, `CPU 核心 VID（请求电压）`, localized process dropdown text, and `bucketMs`.
- Worker explanation did not regress: frontend/source still contains `任务管理器中可能显示一个 FrameScopeMonitor.exe 子进程，这是监控 worker，不是重复打开软件。`; frontend verify and watcher policy tests passed.
- Ordinary UI single-instance behavior did not regress: `FrameScopeSingleInstanceLaunchGuardTests.exe` passed all checks, including ordinary UI guard, worker/diagnostic bypass, clean duplicate lock release, and Chinese duplicate prompt.
- `DATA.cpuVoltage` remains present and unchanged as the CPU Voltage / Vcore key.
- `DATA.cpuVid` remains present and unchanged as the CPU VID key.
- `DATA.fps.bucketMs=1000`.
- FPS raw PresentMon semantics remain intact: manifest/data report `rawRows=3516227`, `validRows=3516226`, `selectionMode=all`, and the HTML tooltip still reads `bucketMs` from the machine key.
- CSV headers remain English/schema-compatible and were not localized.
- `framescope-interactive-manifest.json` schema keys remain English-compatible and report/data links remain valid.

## Verification commands

All commands were run fresh in this retest window.

| Check | Result |
| --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS: typecheck passed, 6 Vitest files passed, 64 tests passed, production build passed. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS: `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS |
| `.\tests\FrameScopeProcessCleanupTests.exe` | PASS |
| `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe` | PASS |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS |
| Layout probe at 1280x720 and 900x760 | PASS, `allNoOverflow=true` |
| Valorant DATA/CSV evidence probe | PASS |
| `git diff --check` | PASS, only LF-to-CRLF warnings from Git; no whitespace errors. |
| Final residual process check | PASS: `NO_MATCHING_RESIDUAL_PROCESSES` |

## Final decision

PASS.

The chart dropout/bottom-spike defense and CPU VID/Vcore separation are validated against the provided Valorant run and the required regression suite. The low AMD LHM Core VID values are confirmed as raw `/amdcpu/.../VID` sensor data, the expected around 1.08V reading is confirmed as SuperIO Vcore, and the after report keeps Vcore visible while refusing to present that Vcore as CPU VID.

## Explicitly not done

- Did not modify source code.
- Did not fix bugs.
- Did not package.
- Did not install.
- Did not start a real game.
- Did not test BF6.
- Did not push GitHub.
- Did not update Release.
