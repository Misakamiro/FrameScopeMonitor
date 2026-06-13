# FrameScope Monitor build artifact sync after localization / worker / single-instance fixes

Date: 2026-06-13

Status: PASS

Scope: only build, artifact sync, read-only extraction/verification, built-payload smoke, and this report. Did not install FrameScope, did not run setup/full setup, did not start a real game, did not test BF6, did not push GitHub, did not update GitHub Release.

## Inputs checked

Workspace:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Required background reports all existed before build:

- `docs\implementation-reports\2026-06-13-framescope-monitor-worker-and-chart-chinese-localization-report.md`
- `docs\test-reports\2026-06-13-framescope-monitor-worker-and-chart-chinese-localization-retest.md`
- `docs\implementation-reports\2026-06-13-framescope-single-instance-launch-guard-report.md`
- `docs\test-reports\2026-06-13-framescope-single-instance-launch-guard-retest.md`
- `docs\test-reports\2026-06-13-framescope-single-instance-launch-guard-partial-clarification.md`

`git status --short` was recorded before changes. The worktree already contained modified source/test/build files and multiple untracked docs/evidence directories; no user changes were rolled back. Modified files included `build.ps1`, `src/app/FrameScopeNativeMonitor*.cs`, frontend bridge/page/state files, reporting generator files, and test files. Untracked files included the five 2026-06-13 background reports/evidence, older test evidence directories, `smoke-temp/`, `src/app/FrameScopeNativeMonitor.SingleInstance.cs`, and `tests/FrameScopeSingleInstanceLaunchGuardTests.cs`.

## Build-before verification

Commands run before final build:

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
  - PASS. TypeScript typecheck passed, Vitest passed `6` files / `64` tests, Vite production build passed.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
  - PASS. `FrameScope tests rebuilt.`
- `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe`
  - PASS. Covered ordinary UI guard, worker/diagnostic bypass, duplicate lock release, Chinese duplicate prompt.
- `.\tests\FrameScopeReportManifestTests.exe`
  - PASS. Manifest/report checks passed, including PresentMon source-of-truth behavior and CPU Voltage / CPU VID separation.
- `.\tests\FrameScopeDiagnosticsTests.exe`
  - PASS.
- `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`
  - PASS.
- `.\tests\FrameScopeNativeWatcherPolicyTests.exe`
  - PASS.
- `.\tests\FrameScopeNativeMonitorChildProcessTests.exe`
  - PASS.
- `.\tests\FrameScopeProcessCleanupTests.exe`
  - PASS.
- bundled Node `.\tests\chart-sampling-tests.js`
  - PASS.
- layout probe with current reporting source and synthetic reports
  - PASS. `allNoOverflow=true`, `scenarioCountByLabelScan=23`, `overflowTrueCount=0`, `chartScrollOverflowTrueCount=0`, `screenshotCount=23`, `allScreenshotsNonBlank=true`.

Notes on corrected verification harness issues:

- The temporary layout-probe report generator needed the same `Microsoft.VisualBasic.dll` reference used by `tests\Build-FrameScopeTests.ps1`.
- The temporary report path was moved to `%TEMP%` to avoid .NET Framework path length limits.
- The layout probe JSON still contains mojibake in localized strings, so the final summary used stable JSON key scanning plus PNG pixel sampling, matching prior retest practice.

## Build

Command:

`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`

Result: PASS.

Generated:

- `dist\FrameScopeMonitor-Setup.exe`
- `dist\FrameScopeMonitor-Full-Setup.exe`
- `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe`

No generated installer was executed.

## Artifact hashes

| Artifact | Size | SHA256 |
| --- | ---: | --- |
| `dist\FrameScopeMonitor-Setup.exe` | 2,706,432 | `E9CE5D97C2673BA1ECE9DBF95073BEB32A4D33769B6C18B1F4639F6FEDD90C06` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | 201,885,696 | `D4BA6AABB83CC4F6C6BE89F0CFDA8EC35746054BABF60D4F82864DCC823D02B1` |
| `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe` | 364,032 | `790BFA2A303738F3FD3B7A1A03C71735ADA34260479C946E7F86B9351A3AE4A6` |
| `dist\FrameScopeMonitor-payload\frontend\assets\index-m2r1Gfgc.js` | 243,808 | `2DB69188D6FD4A6B2CA08379BFE38C89833C4188A427D3734B3719842BF302CE` |

## Embedded payload verification

Read-only extraction method:

- Loaded setup/full setup assemblies with .NET reflection.
- Read manifest resource `FrameScopePayload`.
- Wrote the resource stream to a temporary `payload.zip`.
- Extracted the zip to `%TEMP%`.
- Compared every extracted file against `dist\FrameScopeMonitor-payload` by relative path, length, and SHA256.
- Did not execute either installer.

Final post-smoke comparison:

| Installer | Embedded zip SHA256 | Files | Missing | Extra | Mismatch | Matches dist payload |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| setup | `35CC58985A15649479E7ED4B418EFC302B36C84C8A95ACE56ABEB3BD2DFCD6AC` | 30 | 0 | 0 | 0 | true |
| full setup | `35CC58985A15649479E7ED4B418EFC302B36C84C8A95ACE56ABEB3BD2DFCD6AC` | 30 | 0 | 0 | 0 | true |

During smoke, temporary files were created under `dist\FrameScopeMonitor-payload`; those verification-only files were removed and payload consistency was rechecked afterward.

## Built payload smoke

All smoke tests used `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe` or sibling payload executables, not the setup installers.

- WebView2 runtime self-test:
  - PASS. Exit code `0`, evidence `available:149.0.4022.62`.
- Ordinary UI single-instance smoke:
  - PASS. First normal UI launch created one main process/window.
  - Second normal launch showed `FrameScope Monitor 已在运行，请勿重复打开。`.
  - Second process exited with code `0` after confirming the prompt.
  - No second main UI remained: process count stayed at `1` while first UI was open.
  - After closing the first UI, process count became `0`.
  - A third normal launch opened normally again, proving the lock was released.
- Worker smoke while UI held the single-instance lock:
  - PASS. `--watcher --config ...` started and stayed running while the UI lock was held.
  - PASS. `--monitor-session --MonitorProcessRole monitor-session-worker ... --WaitSeconds 1` was not blocked by the UI single-instance lock and exited through the worker path with code `1` for the intentionally missing synthetic target.
  - PASS. After stopping/cleaning smoke processes, residual count for the built payload path was `0`.
- Target add/edit/delete and settings persistence:
  - PASS. Built payload WebView smoke with temporary config under payload root.
  - `targetAddSaved=true`, `targetEditSaved=true`, `targetDeleteSaved=true`.
  - Deleting the last target ended with `finalTargetCount=0`.
  - `settingsSaved=true`; saved telemetry sample interval `1375`; restart-read telemetry sample interval `1375`.
- Report resource smoke:
  - PASS. Built payload `FrameScopeReportGenerator.exe` generated a synthetic report.
  - Confirmed `"bucketMs":1000` in generated data.
  - Confirmed report HTML still references `DATA.cpuVoltage` and `DATA.cpuVid`.
  - Confirmed generated data keeps English machine keys including `cpuVoltage`, `cpuVid`, and `fps`.
  - Confirmed manifest fields remain English, including `cpuVoltageAvailable`, `cpuVidAvailable`, `targetProcessName`.
  - Confirmed Chinese chart/report text exists, including `帧率`, `CPU 电压 / Vcore`, `CPU 核心 VID`, `后台进程`, `IO/温度`, `平均 FPS`, `样本数`, `无可绘制数据`.
  - Confirmed `cpu-voltage:vcore` and `cpu-vid:0`/`核心 #1 VID` remain separate, so VID is not presented as Vcore.
  - Confirmed PresentMon CSV header remains English: `TimeInDateTime,MsBetweenPresents,Application,ProcessID,SwapChainAddress,PresentMode,AllowsTearing`.
- Worker explanation in built payload:
  - PASS. Frontend JS contains `监控 worker 已启动`, `监控 worker 已停止`, and `任务管理器中可能显示一个 FrameScopeMonitor.exe 子进程，这是监控 worker，不是重复打开软件。`.
  - PASS. Bridge smoke evidence contained `workerExplanation` and `watcher-worker`.
  - PASS. Built `FrameScopeMonitor.exe` contains the Chinese duplicate prompt and worker explanation strings.

## Required semantics

Confirmed in built payload/resource checks:

- `bucketMs=1000` unchanged.
- `DATA.cpuVoltage` unchanged.
- `DATA.cpuVid` unchanged.
- FPS raw PresentMon data remains the source of truth; report smoke used English PresentMon headers and generated FPS buckets from `presentmon.csv`.
- CPU Voltage / Vcore and CPU Core VID remain separate; VID does not populate the Vcore series.
- CSV headers and manifest field names remain English for compatibility.

## Cleanup and final checks

- Removed only this run's temporary payload smoke artifacts from `dist\FrameScopeMonitor-payload`, then revalidated embedded payload equality.
- No setup installer was run.
- No full setup installer was run.
- No real game was started.
- BF6 was not tested.
- GitHub was not pushed.
- GitHub Release was not updated.

Final commands after writing this report:

- `git diff --check`
  - PASS, exit code `0`.
  - Output only contained LF-to-CRLF working-copy warnings for existing modified files; no whitespace error was reported.
- final residual process check
  - PASS, exact output: `NO_MATCHING_RESIDUAL_PROCESSES`.
