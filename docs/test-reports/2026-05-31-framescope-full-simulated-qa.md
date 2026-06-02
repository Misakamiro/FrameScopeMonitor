# FrameScope Full Simulated QA

- Date: 2026-05-31
- Scope: simulated QA only; no real game launch and no source fix
- Evidence root: `docs\test-reports\2026-05-31-framescope-full-simulated-qa-evidence`
- Short-path synthetic run root: `C:\fsqa0531`
- Final verdict: **PARTIAL**

## Direct Answers

| Question | Result |
|---|---|
| 是否修改源码 | No source files were edited in this QA pass. The existing source diff was already large before this run. |
| 是否打包 | PARTIAL boundary exception: the required `build.ps1` command was run and that script generated setup/full-setup/dist artifacts. No installer was run and no release artifact was published. |
| 是否安装 FrameScope | No. |
| 是否启动真实游戏 | No. |
| 是否测试 BF6 | No real BF6 test was run. Some existing unit-test fixtures contain `bf6.exe` text only. |
| 是否推 GitHub | No. |
| 是否更新 Release | No. |
| 主 UI smoke 是否通过 | PASS: WebView2 live smoke rerun exit code 0. |
| target 新增/编辑/删除是否通过 | PASS: add/edit/delete all saved in target/settings smoke. |
| Settings 保存/读取是否通过 | PASS: saved and restart-read telemetry sample interval `1375`. |
| 日志目录打开动作是否通过 | PASS: WebView2 bridge smoke returned `logs.directoryOpened`. |
| 报告列表刷新是否通过 | PASS: `reports.list` loaded the synthetic report. |
| 报告打开/重新生成/打开目录是否通过 | PASS: WebView2 live/reduced smoke completed open, regenerate, and open-directory flows. |
| FPS 图表是否保持 `bucketMs=1000` 和 raw 统计语义 | PASS: `DATA.fps.bucketMs=1000`; manifest `frames/rawPresentMonRows/validPresentMonRows=120`. |
| CPU Voltage / Vcore 是否仍独立存在 | PASS: `DATA.cpuVoltage.available=true`, unit `V`, one Vcore series. |
| CPU Core VID 是否额外独立存在 | PASS: `DATA.cpuVid.available=true`, unit `V`, four VID series. |
| VID/Vcore 是否双向隔离 | PASS: VID did not populate Vcore; Vcore did not populate VID. |
| 1280x720 和 900x760 probe 是否 `allNoOverflow=true` | PASS: layout probe `allNoOverflow=true`, 23 scenarios, overflow count 0. |
| 残留进程检查结果 | PASS: `NO_MATCHING_RESIDUAL_PROCESSES`. |
| 最终结论 | PARTIAL: product/function checks passed; boundary is partial because required `build.ps1` generated setup artifacts and smoke harness created temporary config/profile outside `docs\test-reports`. |

## Verification Commands

| Command | Result | Evidence |
|---|---:|---|
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS, exit 0 | `logs\01-frontend-verify.log` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS, exit 0; generated setup artifacts | `logs\02-build-ps1.log` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS, exit 0 | `logs\03-build-tests.log` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS, exit 0 | `logs\04-report-manifest-tests.log` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS, exit 0 | `logs\05-diagnostics-tests.log` |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS, exit 0 | `logs\06-system-sampler-cpu-core-tests.log` |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS, exit 0 | `logs\07-native-watcher-policy-tests.log` |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS, exit 0 | `logs\08-native-monitor-child-process-tests.log` |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS, exit 0 | `logs\09-chart-sampling-tests.log` |
| bundled Node `tools\Probe-ReportHtmlLayout.js` | PASS, exit 0 | `logs\11-layout-probe.log` |
| WebView2 live smoke | PASS after rerun with config under app root | `ui-smoke\webview2-live-smoke-rerun.json` |
| WebView2 reduced-motion smoke | PASS after rerun with config under app root | `ui-smoke\webview2-reduced-motion-smoke-rerun.json` |
| target/settings smoke | PASS | `target-settings-smoke\target-settings-evidence-summary.json` |
| `git diff --check` | PASS, exit 0; LF/CRLF warnings only | `logs\18-git-diff-check.log` |
| residual process check | PASS | `residual-process-check.json` |

## Notes And Exceptions

- The first synthetic report generation under the long `docs\test-reports` path hit `PathTooLongException`. Per instruction, the synthetic run was regenerated under `C:\fsqa0531`.
- The first WebView2 smoke attempt used `C:\fsqa0531\profile\framescope-config.json`; the app rejected config paths outside the application root. Reruns used `smoke-temp\qa0531-full-sim` for the temporary config/history and passed.
- Some smoke screenshot suffix captures hit path-length errors, but the primary screenshots and JSON evidence were still produced. The layout probe screenshots are complete under `layout-probe`.
- WebView2 smoke generated diagnostic report output under `%LOCALAPPDATA%\FrameScopeMonitorData\diagnostic-reports` as part of the existing diagnostics smoke path. It did not install or launch a real game.

## Key Evidence Paths

- QA logs: `docs\test-reports\2026-05-31-framescope-full-simulated-qa-evidence\logs`
- UI smoke JSON: `docs\test-reports\2026-05-31-framescope-full-simulated-qa-evidence\ui-smoke\webview2-live-smoke-rerun.json`
- target/settings/report smoke JSON: `docs\test-reports\2026-05-31-framescope-full-simulated-qa-evidence\target-settings-smoke\target-settings-evidence-summary.json`
- layout probe JSON: `docs\test-reports\2026-05-31-framescope-full-simulated-qa-evidence\layout-probe\report-overflow-probe.json`
- chart evidence summary: `docs\test-reports\2026-05-31-framescope-full-simulated-qa-evidence\chart-evidence-summary.json`
- residual process evidence: `docs\test-reports\2026-05-31-framescope-full-simulated-qa-evidence\residual-process-check.json`
- synthetic report: `C:\fsqa0531\profile\runs\SyntheticQA\run1\charts\framescope-interactive-report.html`
- synthetic data: `C:\fsqa0531\profile\runs\SyntheticQA\run1\charts\framescope-interactive-data.js`

## Screenshot Evidence

| Area | Screenshot |
|---|---|
| FPS | `layout-probe\fps-default-1280x720.png` |
| FPS tooltip | `layout-probe\fps-tooltip-1280x720.png` |
| CPU Voltage / Vcore | `layout-probe\cpu-voltage-1280x720.png` |
| CPU Core VID | `layout-probe\cpu-core-vid-1280x720.png` |
| GPU/performance | `layout-probe\performance-chart-1280x720.png` |
| CPU/system usage | `layout-probe\system-usage-1280x720.png` |
| IO | `layout-probe\io-disk-net-1280x720.png` |
| Temperature | `layout-probe\io-temperature-1280x720.png` |
| Process chart | `layout-probe\background-process-1280x720.png` |
| UI live smoke | `ui-screenshots\webview2-live-smoke-rerun.png` |
| Reduced motion smoke | `ui-screenshots\webview2-reduced-motion-smoke-rerun.png` |
| Target CRUD | `target-settings-screenshots\target-settings-crud.png` |
| Settings restart persistence | `target-settings-screenshots\settings-restart-persistence.png` |

## Worktree Boundary

The current git diff remains large, matching the known pre-existing state. This QA pass added evidence and this report under `docs\test-reports`, plus temporary smoke profiles under `smoke-temp` and short-path synthetic artifacts under `C:\fsqa0531`. No source fixes were made.
