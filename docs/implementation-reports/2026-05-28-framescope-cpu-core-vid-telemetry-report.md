# FrameScope CPU Core VID Telemetry Implementation Report

Date: 2026-05-28

Conclusion: PASS

## Scope

Implemented CPU Core VID collection, persistence, manifest export, report data export, and chart display as a separate telemetry lane from real CPU voltage. VID is consistently labeled as request/target voltage and is not represented as real per-core Vcore.

Explicitly not performed: BF6 testing, real game launch, installer execution, GitHub push, Release update.

## What changed

- Added dedicated CPU Core VID telemetry plumbing:
  - `cpu-vid-samples.csv`
  - `cpu-vid-telemetry-status.json`
  - sampler options for `--enable-cpu-vid-telemetry`, `--cpu-vid-csv`, `--cpu-vid-status`, `--cpu-vid-interval`, and `--cpu-vid-provider`
  - dedicated `CpuVidTelemetrySession`, `CpuVidSample`, `ICpuVidTelemetryProvider`, and test provider types
- Extended the built-in LibreHardwareMonitor provider to read only real `Core #n VID` voltage sensors into the VID lane.
- Kept CPU voltage semantics separate:
  - real CPU voltage chart still requires true per-core voltage
  - package/SOC/aggregate `Vcore` remains non-per-core voltage
  - VID never becomes CPU voltage series data
- Added report manifest fields:
  - `cpuVidAvailable`
  - `cpuVidSampleCount`
  - `cpuVidCoreCount`
  - `cpuVidSource`
  - `cpuVidStatus`
  - `cpuVidReason`
  - `cpuVidNote`
  - `cpuVidSamplesCsv`
- Added `DATA.cpuVid` in `framescope-interactive-data.js`. VID is not inserted into `DATA.cpuVoltage.series`.
- Added `CPU Core VID` chart support:
  - unit `V`
  - one curve per `Core #n VID`
  - title and note clarify that VID is CPU request/target voltage, not real per-core Vcore
  - unavailable state uses Chinese no-data reason and creates no fake series
- Preserved CPU core frequency report data and chart path.

## Host provider probe

Evidence root:

`artifacts\cpu-vid-provider-probe-20260528-024521`

Probe result: PASS

- Host Core VID available: yes
- Provider: `builtin-librehardwaremonitor`
- VID sensors detected:
  - `Core #1 VID`
  - `Core #2 VID`
  - `Core #3 VID`
  - `Core #4 VID`
  - `Core #5 VID`
  - `Core #6 VID`
  - `Core #7 VID`
  - `Core #8 VID`
- VID rows: 72
- VID core count: 8
- VID status: `core-vid-available`
- VID is separately persisted:
  - `artifacts\cpu-vid-provider-probe-20260528-024521\cpu-vid-samples.csv`
  - `artifacts\cpu-vid-provider-probe-20260528-024521\cpu-vid-telemetry-status.json`
- Real CPU voltage status on this host:
  - `cpuVoltageAvailable=false`
  - `cpuVoltagePerCoreAvailable=false`
  - `cpuVoltageNonPerCoreAvailable=true`
  - detected non-per-core sensors: `Vcore`, `Vcore SoC`, `Vcore Misc`
  - reason: `仅检测到 non-per-core CPU 电压传感器；图表只显示真实 per-core voltage。`
- CPU core frequency status:
  - rows: 144
  - logical processors: 16
  - `cpuFrequencyCaptured=true`

## Report evidence

Generated report:

`artifacts\cpu-vid-provider-probe-20260528-024521\charts\framescope-interactive-report.html`

Generated data:

`artifacts\cpu-vid-provider-probe-20260528-024521\charts\framescope-interactive-data.js`

Generated manifest:

`artifacts\cpu-vid-provider-probe-20260528-024521\charts\framescope-interactive-manifest.json`

Manifest/data checks:

- `cpuVidAvailable=true`
- `cpuVidReason=""`
- `cpuVidSampleCount=72`
- `cpuVidCoreCount=8`
- `cpuVidSource=builtin-librehardwaremonitor`
- `DATA.cpuVid` exists with 8 VID series.
- `DATA.cpuVoltage` exists with 0 voltage series for this run.
- VID-in-voltage-series check: false.
- CPU voltage chart remains disabled/no real per-core voltage for this host run.

## Screenshot evidence

Screenshot/layout evidence root:

`artifacts\cpu-vid-provider-probe-20260528-024521\screenshots`

Fresh final screenshots:

- `report-cpu-core-vid-chart-1280x720-final.png`
- `report-cpu-voltage-no-real-per-core-chart-1280x720-final.png`
- `report-cpu-core-frequency-chart-1280x720-final.png`
- `report-cpu-core-vid-chart-900x760-final.png`
- `report-cpu-vid-dropdown-expanded-900x760-final.png`

Layout check:

`report-chart-focused-checks-final.json`

Result:

- `problemCount=0`
- 1280x720 VID chart: no dropdown, legend, or tooltip overlap
- 900x760 VID chart: no dropdown, legend, or tooltip overlap
- 900x760 dropdown-expanded chart: no dropdown, legend, or tooltip overlap
- screenshots are nonblank and have expected dimensions

## Verification

All verification below was run on 2026-05-28 from the source tree.

| Check | Result | Notes |
| --- | --- | --- |
| `.\tools\Run-Frontend.ps1 verify` | PASS after rerun with `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | Direct script invocation was blocked by local PowerShell ExecutionPolicy. Bypass run passed typecheck, 50 frontend tests, and Vite build. |
| `.\build.ps1` | PASS | Built `dist\FrameScopeMonitor-Setup.exe` and `dist\FrameScopeMonitor-Full-Setup.exe`; installers were not executed. |
| `.\tests\Build-FrameScopeTests.ps1` | PASS | Test binaries rebuilt. |
| `tests\FrameScopeConfigStoreTests.exe` | PASS |  |
| `tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS | Includes VID schema, interval clamp, unavailable reason, synthetic VID CSV/status, and voltage/VID separation checks. |
| `tests\FrameScopeReportManifestTests.exe` | PASS | Includes fake Core #1..#8 VID, VID-only, aggregate Vcore rejection, no-VID Chinese reason, and CPU frequency report checks. |
| `tests\FrameScopeWebBridgeTests.exe` | PASS |  |
| `tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS | Includes child-process VID CSV/status path checks. |
| `tests\FrameScopeDiagnosticsTests.exe` | PASS |  |
| `tests\FrameScopeProcessCleanupTests.exe` | PASS |  |
| `tests\FrameScopeProcessSamplerTests.exe` | PASS |  |
| `tests\FrameScopePresentMonDiagnosticsTests.exe` | PASS |  |
| `tests\FrameScopeWebHostLifecycleTests.exe` | PASS |  |
| `tests\FrameScopeWebView2RuntimeTests.exe` | PASS |  |
| `tests\FrameScopeCapturePlannerTests.exe` | PASS |  |
| `tests\FrameScopeReportProgressTests.exe` | PASS |  |
| `tests\FrameScopeUiStateTests.exe` | PASS |  |
| `tests\FrameScopePubgSimulatorTests.exe` | PASS | Extra local test binary coverage; no real game launch. |
| `tests\chart-sampling-tests.js` | PASS | Confirms `data-view='cpuVid'`, `DATA.cpuVid`, independent VID metric state, and request/target wording. |
| WebView2 live smoke | PASS | `artifacts\cpu-vid-webview2-final-20260528\webview2-live-smoke.json`, `success=true`. |
| WebView2 reduced-motion smoke | PASS | `artifacts\cpu-vid-webview2-final-20260528\webview2-reduced-motion-smoke.json`, `success=true`, `reducedMotion=true`. |
| Host provider probe | PASS | `provider-probe-summary.json`, `conclusion=host-core-vid-available`. |
| Report screenshot/layout check | PASS | `problemCount=0`. |
| `git diff --check` | PASS | Exit code 0. Git printed LF/CRLF normalization warnings only. |
| Residual process check | PASS | No residual FrameScope/SystemSampler/ReportGenerator/ProcessSampler/PresentMon or smoke-started report Edge process found. |

## Requirement checklist

| Requirement | Result |
| --- | --- |
| New CPU per-core VID collection and recording | PASS |
| Uses built-in provider to read `Core #n VID` sensors | PASS |
| VID modeled separately from real CPU voltage/per-core Vcore | PASS |
| Chart name uses `CPU Core VID` / `CPU 核心 VID` language | PASS |
| Tooltip, legend, manifest, and data distinguish VID from real voltage and non-per-core voltage | PASS |
| VID-only run keeps CPU voltage chart unavailable while VID chart shows VID | PASS |
| Package/SOC/Vcore-only run does not enter CPU VID chart | PASS |
| No VID sensor produces Chinese no-data reason and no fake data | PASS |
| CPU core frequency path remains normal | PASS |
| 900x760 and 1280x720 chart/dropdown/legend/tooltip layout | PASS |

## Final judgment

PASS.

This host did capture real `Core #1..#8 VID` via the built-in LibreHardwareMonitor provider. VID lands in dedicated files and enters the independent `DATA.cpuVid` / `CPU Core VID` chart. CPU voltage remains strict: this run still has no real per-core voltage chart because the host exposed only aggregate/package/SOC Vcore-style sensors outside VID.

Recommendation: enter a focused retest window. A real-game or BF6 run is not required for this VID lane retest unless the next owner explicitly wants game-runtime acceptance coverage.
