# FrameScope build artifact sync after chart, VID source, and VID unavailable message fixes

Date: 2026-06-14

Status: PASS

Scope: build, artifact sync, read-only validation, and report only. I did not install FrameScope, did not run `FrameScopeMonitor-Setup.exe`, did not run `FrameScopeMonitor-Full-Setup.exe`, did not start a real game, did not test BF6, did not push GitHub, and did not update Release.

## Workspace

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Initial git status was dirty before this window. Existing user changes and existing untracked reports/evidence directories were not reverted.

## Background reports

All required background reports were present:

- `docs\implementation-reports\2026-06-13-framescope-chart-screen-space-same-x-artifact-fix-report.md`
- `docs\implementation-reports\2026-06-13-framescope-layout-probe-json-parse-fix-report.md`
- `docs\test-reports\2026-06-13-framescope-chart-screen-space-same-x-artifact-final-retest.md`
- `docs\test-reports\2026-06-14-framescope-amd-lhm-core-vid-source-rejection-retest.md`
- `docs\implementation-reports\2026-06-14-framescope-cpu-vid-unavailable-message-report.md`

## Pre-build validation

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`: PASS. TypeScript check passed, 64 Vitest tests passed, Vite production build passed.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
- `.\tests\FrameScopeReportManifestTests.exe`: PASS.
- `.\tests\FrameScopeDiagnosticsTests.exe`: PASS.
- `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`: PASS.
- `.\tests\FrameScopeNativeWatcherPolicyTests.exe`: PASS.
- `.\tests\FrameScopeNativeMonitorChildProcessTests.exe`: PASS.
- `.\tests\FrameScopeProcessCleanupTests.exe`: PASS.
- `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe`: PASS.
- Bundled Node `.\tests\chart-sampling-tests.js`: PASS.
- Bundled Node `.\tools\Probe-ReportHtmlLayout.js --report ... --diagnostic ... --out .\smoke-temp\artifact-sync-chart-vid-message-20260614\prebuild-layout-probe`: PASS. PowerShell `ConvertFrom-Json` parsed the JSON, `allNoOverflow=true`, `scenarioCount=23`, `overflowCount=0`.

Old AMD VID threshold strategy scan:

```powershell
rg -n --pcre2 "ImplausibleLowAmd|0\.4-0\.7|0\.7V|value\s*>=\s*0\.7" src tests tools packaging build.ps1
```

Result: no matches in active source/test/tool/build paths. Full-repo search still finds historical docs and old smoke artifacts, but no active strategy code.

## Build

Official build command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Result: PASS.

No generated installer was executed.

Required artifacts exist and are non-empty:

- `dist\FrameScopeMonitor-Setup.exe`: 2,710,016 bytes.
- `dist\FrameScopeMonitor-Full-Setup.exe`: 201,888,768 bytes.
- `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe`: 364,032 bytes.

## SHA256

| Artifact | Size | SHA256 |
| --- | ---: | --- |
| `dist\FrameScopeMonitor-Setup.exe` | 2,710,016 bytes | `BB1C023317BAF16F7CFD9584F3DF598BE3288BA11398BC1947A1897AF8B3BAB8` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | 201,888,768 bytes | `26C106925D4C2A5031A7E17DD3BA5FA520D727F16117622F7A72EA8E2D0DEBA9` |
| `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe` | 364,032 bytes | `0919ABE1125F430CCD9B15F039745E3733B0334F04313497DE483C43F5122A09` |
| `dist\FrameScopeMonitor-payload\frontend\assets\index-m2r1Gfgc.js` | 243,808 bytes | `2DB69188D6FD4A6B2CA08379BFE38C89833C4188A427D3734B3719842BF302CE` |

## Embedded payload sync

Read-only extraction loaded the installer assemblies as resources and extracted only the embedded `FrameScopePayload` zip stream. The installers were not executed.

Final payload sync result:

- `dist\FrameScopeMonitor-payload`: 30 files.
- setup embedded payload: 30 files, matches dist payload, no missing/extra/changed files.
- full setup embedded payload: 30 files, matches dist payload, no missing/extra/changed files.
- `dist\FrameScopeMonitor-payload\smoke-temp`: absent after cleanup.

Note: one intermediate UI smoke attempt wrote a temporary profile under `dist\FrameScopeMonitor-payload\smoke-temp`. That was my generated smoke data, it was removed after verifying the resolved path was inside the payload root, and the payload sync comparison above was rerun afterward.

## Built report smoke

Evidence:

- `smoke-temp\cvm1240\built-smoke-summary.json`
- `smoke-temp\cvm1240\layout-probe\report-overflow-probe.json`

Built generator:

`dist\FrameScopeMonitor-payload\FrameScopeReportGenerator.exe`

Generated report inputs were copied before regeneration, so AppData source runs were not modified.

### CS2 chart smoke

Generated CS2 report:

`smoke-temp\cvm1240\cs2\Counter-Strike-2-20260613-152103\charts\framescope-interactive-report.html`

Result: PASS.

- Vcore duplicate screen x: `0`.
- Vcore same-x vertical jump: `0`.
- Vcore rendered finite zero count: `0`.
- Vcore source finite zero count: `0`.
- Vcore finite range: `0.972-1.116V`, so `null`/gap data was not collapsed to fake `0`.
- Background process chart line-only mode: PASS.
- Process same-x needle artifacts: PASS, top process series had no duplicate screen x/same-x vertical artifacts.
- Real spike retained: PASS, process peak list includes values at or above `7.8`.
- PresentMon raw semantics preserved: `rawRows=454316`, `validRows=454316`, `selectedRows=454316`, `selectionMode=all`.

### Valorant VID unavailable message smoke

Latest source run copied from:

`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260614-114551`

Generated Valorant report:

`smoke-temp\cvm1240\val\Valorant-20260614-114551\charts\framescope-interactive-report.html`

Result: PASS.

- CPU Core VID tab exists: `data-view='cpuVid'`.
- CPU Core VID tab is not disabled.
- `DATA.cpuVid.available=false`.
- `DATA.cpuVid.series.length=0`.
- The unavailable reason is not blank and contains the required meaning:
  - not a missing chart bug,
  - AMD LibreHardwareMonitor Core VID source is untrusted,
  - incorrect about `0.5V` VID is stopped,
  - CPU Voltage / Vcore remains visible under CPU voltage / Vcore,
  - Vcore does not impersonate VID,
  - future legal VID sources will display normally.
- `DATA.cpuVoltage.available=true`.
- `DATA.cpuVoltage.series[0].key="cpu-voltage:vcore"`.
- Vcore was not written into `DATA.cpuVid`.
- `bucketMs=1000`.

### AMD LHM VID source rejection smoke

Generated AMD LHM fixture report:

`smoke-temp\cvm1240\fixtures\amd-lhm\charts\framescope-interactive-report.html`

Result: PASS.

- Fixture used AMD LHM Core VID identifiers under `/amdcpu/0/voltage/2..9`.
- VID values were `0.762V`, intentionally above the old `0.7V` threshold, and were still rejected by source semantics.
- `DATA.cpuVid.available=false`.
- `DATA.cpuVid.series.length=0`.
- `DATA.cpuVid.sampleCount=0`.
- Reason keeps the AMD LibreHardwareMonitor Core VID untrusted-source explanation.
- `DATA.cpuVoltage.available=true`.
- `bucketMs=1000`.

### Legal VID fixture smoke

Generated legal VID fixture report:

`smoke-temp\cvm1240\fixtures\legal-vid\charts\framescope-interactive-report.html`

Result: PASS.

- `DATA.cpuVid.available=true`.
- `DATA.cpuVid.series.length=8`.
- `DATA.cpuVid.sampleCount=8`.
- `bucketMs=1000`.
- Legal VID data remains displayable.

### Layout and localization

Bundled Node layout probe on the newly generated CS2 report:

- JSON parsed with PowerShell `ConvertFrom-Json`.
- `allNoOverflow=true`.
- `resultCount=23`.
- `overflowCount=0`.

Built smoke also confirmed chart localization markers for CPU voltage / Vcore, CPU Core VID, and background process charts, and confirmed the worker explanation source text remains present.

## Built payload UI smoke

Payload smoke copy:

`C:\Users\misakamiro\AppData\Local\Temp\fspay-cvm-202606141240\payload`

Evidence:

- `smoke-temp\cvm1240\payload-ui\payload-copy-meta.json`
- `smoke-temp\cvm1240\payload-ui\webview2-runtime-self-test-copy.txt`
- `smoke-temp\cvm1240\payload-ui\target-settings-empty-rerun\target-settings-empty-evidence-summary.json`
- `smoke-temp\cvm1240\payload-ui\single-instance-worker\single-instance-worker-smoke-summary.json`

Result: PASS.

- WebView2 self-test: `available:149.0.4022.69`.
- Target add/edit/delete from zero initial targets: PASS.
- `targetAddSaved=true`.
- `targetEditSaved=true`.
- `targetDeleteSaved=true`.
- `finalTargetCount=0`.
- Settings save/read: PASS.
- Saved telemetry sample interval: `1375`.
- Restart/read telemetry sample interval: `1375`.
- Ordinary UI single-instance smoke: PASS. A duplicate ordinary UI launch showed `FrameScope Monitor 已在运行，请勿重复打开。` instead of opening a second main UI.
- Worker bypass smoke: PASS. `--watcher` stayed alive while the ordinary UI lock was held.
- Final owned temp payload process count after cleanup: `0`.

## Final checks

- Final active-path old threshold scan: no matches.
- `git diff --check`: exit code `0`; Git printed LF-to-CRLF working-copy warnings only, with no whitespace errors.
- Final residual process check: `NO_MATCHING_RESIDUAL_PROCESSES`.

## Explicitly not done

- Did not install FrameScope.
- Did not run setup.
- Did not run full setup.
- Did not start a real game.
- Did not test BF6.
- Did not push GitHub.
- Did not update Release.
