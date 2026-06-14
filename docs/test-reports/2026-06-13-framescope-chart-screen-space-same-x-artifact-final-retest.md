# FrameScope Chart Screen-Space Same-X Artifact Final Retest

Date: 2026-06-13

Status: PASS

## Scope

- Workspace: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
- CS2 run: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260613-152103`
- Fresh report: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260613-152103\charts\framescope-interactive-report.html`
- Fresh evidence directory: `docs\test-reports\2026-06-13-framescope-chart-screen-space-same-x-artifact-final-retest-evidence`
- This retest only regenerated/read the CS2 report, generated test evidence, ran the requested validation commands, and wrote this report.
- No source code was modified in this retest. The worktree already contained unrelated modified/untracked files before this final retest.

## Result Summary

Overall result is `PASS`.

The previous blocker was the layout probe JSON failing Windows PowerShell default parsing. This final retest generated a fresh `report-overflow-probe.json`, parsed it with default `Get-Content -Raw | ConvertFrom-Json`, and confirmed `allNoOverflow=true`.

The chart artifact checks also pass:

- Vcore dark vertical cuts/slices: not reproduced in the fresh 1280x720 screenshot.
- Vcore duplicate screen x: `0`.
- Vcore same-x vertical jump: `0`.
- Vcore finite range: `0.948-1.116V`.
- Vcore zero regression: `0` finite zero values in `DATA.cpuVoltage`; invalid null/gap handling is also covered by `chart-sampling-tests: PASS`.
- Background process chart: line-only appearance retained; no filled-area pollution is visible.
- Real process spikes and Top process peaks are retained.

## CS2 Report Regeneration

Fresh command:

```powershell
.\FrameScopeReportGenerator.exe "C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260613-152103"
```

Fresh generator output confirmed:

- `frames=454316`
- `rawPresentMonRows=454316`
- `validPresentMonRows=454316`
- `presentMonSelectionMode=all`
- `cpuVoltageAvailable=true`
- `cpuVoltageSampleCount=946`
- `cpuVoltageSampleIntervalMs=1000`

Note: generator stdout reported low-level `cpuVidAvailable=true`, but the final report page `DATA.cpuVid` is the authoritative UI data and remains `available=false` after AMD low VID rejection.

## Vcore Evidence

Fresh screen-space stats:

- Evidence: `docs\test-reports\2026-06-13-framescope-chart-screen-space-same-x-artifact-final-retest-evidence\screen-space-render-stats.json`
- `DATA.cpuVoltage.available=true`
- `DATA.cpuVoltage.sampleCount=946`
- `DATA.cpuVoltage.seriesCount=1`
- Vcore series key: `cpu-voltage:vcore`
- Raw points: `946`
- Rendered points: `946`
- Null rendered points: `0`
- Duplicate screen x: `0`
- Same-x vertical jump: `0`
- Finite range: `0.948-1.116V`
- Source finite range: `0.948-1.116V`
- Finite zero values in `DATA.cpuVoltage`: `0`

Visual review:

- `layout-probe\cpu-voltage-1280x720.png` shows a stable Vcore band around roughly `1.0-1.1V`.
- No dense dark vertical cuts/slices are visible.
- The filled Vcore area does not collapse to `0`, so `Number(null)`/gap-to-zero behavior is not reproduced.

## Background Process Evidence

Fresh visual review:

- `layout-probe\background-process-1280x720.png` shows thin line spikes only.
- The previous heavy filled-area pollution is absent.
- The chart keeps real short spikes instead of flattening them away.

Fresh process screen-space stats for Top 10:

- Max duplicate screen x across Top 10: `0`
- Max same-x vertical jump across Top 10: `0`
- Retained Top process CPU peaks: `7.88%`, `5.31%`, `4.45%`, `3.18%`, `2.70%`, `2.24%`, `2.14%`, `1.95%`, `1.83%`, `1.83%`

Fresh `DATA.process.stats` also retained the Top process table peaks, starting with:

- `完美世界竞技平台`: `7.88%`
- `System`: `5.31%`
- `svchost`: `4.45%`
- `dwm`: `3.18%`
- `nvcontainer`: `2.70%`

## Layout Probe Evidence

Fresh command:

```powershell
C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tools\Probe-ReportHtmlLayout.js --report "C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260613-152103\charts\framescope-interactive-report.html" --diagnostic "C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260613-152103\charts\framescope-interactive-report.html" --out docs\test-reports\2026-06-13-framescope-chart-screen-space-same-x-artifact-final-retest-evidence\layout-probe
```

Result:

- Probe exit code: `0`
- JSON path: `docs\test-reports\2026-06-13-framescope-chart-screen-space-same-x-artifact-final-retest-evidence\layout-probe\report-overflow-probe.json`
- Windows PowerShell default `Get-Content -Raw | ConvertFrom-Json`: `PASS`
- `allNoOverflow=true`
- Scenario count: `23`
- Overflow count: `0`
- Focus scenarios found: `cpu-voltage-1280x720`, `background-process-1280x720`, `cpu-voltage-900x760`, `background-process-900x760`
- All four focus scenarios had `overflow=false` and `chartScrollOverflowX=false`

Focus screenshot nonblank pixel checks:

| Screenshot | Bytes | Size | Sampled unique colors | Nonblank |
| --- | ---: | --- | ---: | --- |
| `cpu-voltage-1280x720.png` | `328005` | `1280x720` | `746` | PASS |
| `background-process-1280x720.png` | `453488` | `1280x720` | `724` | PASS |
| `cpu-voltage-900x760.png` | `267633` | `900x760` | `670` | PASS |
| `background-process-900x760.png` | `354159` | `900x760` | `805` | PASS |

## Semantic Regression Evidence

Fresh report `DATA` checks:

- `DATA.cpuVoltage.available=true`
- `DATA.cpuVoltage.sampleCount=946`
- `DATA.cpuVoltage.seriesCount=1`
- `DATA.cpuVoltage` range is `0.948-1.116V`
- `DATA.cpuVid` exists.
- `DATA.cpuVid.available=false`
- `DATA.cpuVid.sampleCount=7568`
- `DATA.cpuVid.seriesCount=0`
- `DATA.cpuVid.reason` includes the AMD low VID rejection: `0.4-0.7V` / `implausible low`
- `DATA.fps.bucketMs=1000`
- PresentMon raw semantics remain: `rawRows=454316`, `validRows=454316`, `selectedRows=454316`, `selectionMode=all`
- Vcore did not impersonate VID: Vcore is in `DATA.cpuVoltage` as `cpu-voltage:vcore`; `DATA.cpuVid.seriesCount=0`

UI and behavior regression checks:

- Chart Chinese localization is present in the fresh report HTML: `FrameScope 性能报告`, `CPU 电压 / Vcore`, `CPU 核心 VID（请求电压）`, `后台进程`, `FPS 按 1000 ms bucket 显示`.
- Worker/diagnostic launch behavior is covered by `FrameScopeNativeWatcherPolicyTests.exe: PASS` and `FrameScopeNativeMonitorChildProcessTests.exe: PASS`.
- Ordinary UI single-instance behavior is covered by `FrameScopeSingleInstanceLaunchGuardTests.exe: PASS`, including:
  - ordinary UI launches are guarded
  - worker and diagnostic launches bypass the UI guard
  - duplicate UI lock is rejected and releases cleanly
  - duplicate UI prompt stays Chinese

## Required Verification Commands

Passed:

1. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
   - TypeScript typecheck passed.
   - Vitest: `6` files, `64` tests passed.
   - Vite build passed as part of the required frontend verification command.
2. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
   - `FrameScope tests rebuilt.`
3. `.\tests\FrameScopeReportManifestTests.exe`
   - `FrameScopeReportManifestTests: PASS`
4. `.\tests\FrameScopeDiagnosticsTests.exe`
   - `FrameScopeDiagnosticsTests: PASS`
5. `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`
   - `FrameScopeSystemSamplerCpuCoreTests: PASS`
6. `.\tests\FrameScopeNativeWatcherPolicyTests.exe`
   - `FrameScopeNativeWatcherPolicyTests: PASS`
7. `.\tests\FrameScopeNativeMonitorChildProcessTests.exe`
   - `FrameScopeNativeMonitorChildProcessTests: PASS`
8. `.\tests\FrameScopeProcessCleanupTests.exe`
   - `FrameScopeProcessCleanupTests: PASS`
9. `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe`
   - `FrameScopeSingleInstanceLaunchGuardTests: PASS`
10. Bundled Node `.\tests\chart-sampling-tests.js`
    - `chart-sampling-tests: PASS`
11. Bundled Node `.\tools\Probe-ReportHtmlLayout.js`
    - Exit code `0`
    - Fresh `report-overflow-probe.json` generated.
12. Windows PowerShell default `Get-Content -Raw | ConvertFrom-Json`
    - `PASS`
    - `allNoOverflow=true`
13. `git diff --check`
    - Exit code `0`
    - Git printed LF/CRLF working-copy warnings only; no whitespace errors.
14. Final residual process check
    - `NO_MATCHING_RESIDUAL_PROCESSES`

## Not Done

- Did not modify source code.
- Did not fix bugs.
- Did not package the product or build an installer. The Vite build was only the required `Run-Frontend.ps1 verify` validation step.
- Did not install FrameScope Monitor.
- Did not launch a real game.
- Did not test BF6. Some automated tests use process-name fixtures such as `bf6.exe`, but no real BF6 game was started or tested.
- Did not push GitHub.
- Did not update Release.
