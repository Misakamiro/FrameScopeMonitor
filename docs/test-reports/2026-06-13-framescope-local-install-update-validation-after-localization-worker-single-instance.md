# FrameScope Monitor 本地安装更新验证报告

日期：2026-06-13
结论：PASS

## 验证范围

本轮只做 FrameScope Monitor “图表中文化 + worker 说明 + 单实例修复”后的本地安装更新验证和报告。验证对象为本地 `dist\FrameScopeMonitor-Full-Setup.exe` quiet 更新安装后的真实安装版 payload。

明确未做：

- 未启动真实游戏。
- 未测试 BF6。
- 未推 GitHub。
- 未更新 GitHub Release。
- 未删除 `%LOCALAPPDATA%\FrameScopeMonitorData` 历史数据。

## 安装器与数据目录

安装器：`dist\FrameScopeMonitor-Full-Setup.exe`

安装结果：PASS

- Quiet installer exit code：`0`
- 安装器自动启动的 FrameScope UI 已正常关闭：`PostMessage WM_CLOSE`，`ForcedKill=false`
- 安装前数据目录：`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData`
  - Exists：`true`
  - FileCount：`923`
  - TotalBytes：`3060339891`
- 安装后即时数据目录：
  - Exists：`true`
  - FileCount：`923`
  - TotalBytes：`3060339891`
- 最终复核数据目录：
  - Exists：`true`
  - FileCount：`927`
  - TotalBytes：`3060350951`

判断：安装前后数据目录存在且未被清空；最终文件数增加来自本轮验证产生的报告/诊断类文件，不是安装器删除或重建历史数据。

主要证据：

- `docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-localization-worker-single-instance-evidence\01-pre-install-baseline.json`
- `docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-localization-worker-single-instance-evidence\02d-full-setup-dotnet-process-exit-and-close.json`
- `docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-localization-worker-single-instance-evidence\03-post-install-dir-summary.json`
- `docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-localization-worker-single-instance-evidence\post-test-artifact-payload-data-summary.json`

## 产物 SHA256

四个待验证产物 SHA256 均与目标值一致：

| 产物 | SHA256 |
| --- | --- |
| `dist\FrameScopeMonitor-Setup.exe` | `E9CE5D97C2673BA1ECE9DBF95073BEB32A4D33769B6C18B1F4639F6FEDD90C06` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | `D4BA6AABB83CC4F6C6BE89F0CFDA8EC35746054BABF60D4F82864DCC823D02B1` |
| `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe` | `790BFA2A303738F3FD3B7A1A03C71735ADA34260479C946E7F86B9351A3AE4A6` |
| `dist\FrameScopeMonitor-payload\frontend\assets\index-m2r1Gfgc.js` | `2DB69188D6FD4A6B2CA08379BFE38C89833C4188A427D3734B3719842BF302CE` |

## Installed Payload Hash Parity

结果：PASS

最终复核中，`%LOCALAPPDATA%\FrameScopeMonitor` 与 `dist\FrameScopeMonitor-payload` 的 dist payload 子集逐文件/hash 一致：

- Dist payload files：`30`
- Matched：`30`
- Missing：`0`
- Mismatch：`0`
- Installed total files：`197`
- Installed extras：`167`

说明：installed 目录中额外文件为历史配置、日志、smoke-temp、旧前端资源等持久化/验证残留；本轮判断标准是 dist payload 子集在 installed payload 中逐文件 hash 匹配，结果为全匹配。

证据：

- `docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-localization-worker-single-instance-evidence\04-installed-vs-dist-payload-hash-parity.json`
- `docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-localization-worker-single-instance-evidence\post-test-artifact-payload-data-summary.json`

## Installed 单实例 Smoke

结果：PASS

使用 installed payload `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe` 做 smoke：

- 首次普通 UI 启动正常：`firstUiWindowShown=true`
- 第二次普通启动没有打开第二个主窗口：`mainWindowCountDuringDuplicate=1`
- 第二次启动显示中文提示：`FrameScope Monitor 已在运行，请勿重复打开。`
- 第二进程退出：`secondExitedAfterPromptClose=true`，`secondExitCode=0`
- 第二进程未 force kill：`secondForcedKill=false`
- 第一窗口关闭后第三次普通启动正常：`thirdUiOpenedAfterFirstExit=true`
- 最终无残留：`postCleanupProcessCount=0`

证据：

- `liv-si-worker\installed-ui-single-instance-worker-smoke.json`

## Worker 说明与 Worker 绕过单实例锁

结果：PASS

Worker 说明检查：

- 当前监控页/bridge payload 快照中捕获到 `FrameScopeMonitor.exe`
- 捕获到 `监控 worker`
- 捕获到 `不是重复打开软件`
- `workerExplanationSeen=true`

说明：`liv-bridge\bridge-summary.json` 中通用 bridge 扩展流程自身 `success=false`、`exitCode=2`，但本轮要求的 worker 说明文本检查已经在其快照证据中命中，因此 worker 说明项判定为 PASS。

Worker 不被单实例锁误挡：

- UI 持锁时 watcher 可启动并被枚举：`watcherRunningWhileUiHeld=true`，`watcherListedWhileUiHeld=true`
- UI 持锁时 monitor-session 可启动、产生状态/summary，并自然退出：`monitorSessionSeenWhileUiHeld=true`，`monitorSessionExitCode=0`，`monitorSessionForcedKill=false`
- watcher 是测试主动启动的后台进程，收尾时由测试清理：`forced=true`，这是清理测试 helper/background process，不是安装器或应用 UI 的异常 force kill。

证据：

- `liv-bridge\bridge-summary.json`
- `liv-bridge\bridge-smoke.json`
- `liv-si-worker\installed-ui-single-instance-worker-smoke.json`

## Installed 图表中文化与报告语义

结果：PASS

使用 installed `FrameScopeReportGenerator.exe` 生成 synthetic report 并检查报告 HTML/JS：

- Report generator exit code：`0`
- 中文图表文案存在：
  - `帧率`
  - `CPU 电压 / Vcore`
  - `CPU 核心 VID`
  - `后台进程`
  - `平均 FPS`
  - `样本数`
  - `无可绘制数据`
- `bucketMs=1000`：`fpsBucketMs=1000`
- `DATA.cpuVoltage` 存在：`hasDataCpuVoltage=true`
- `DATA.cpuVid` 存在：`hasDataCpuVid=true`
- Vcore / VID 双向隔离保持：
  - `cpuVoltageSeries=["cpu-voltage:vcore:CPU 电压 / Vcore"]`
  - `cpuVidSeries=["cpu-vid:0:核心 #1 VID","cpu-vid:1:核心 #2 VID"]`
  - `vcoreVidSeparated=true`
- FPS raw PresentMon 统计语义保持：
  - `rawPresentMonRows=20`
  - `validPresentMonRows=20`
  - `selectedPresentMonRows=20`
  - `frameCount=20`
  - `fpsDisplayPointCount=2`
  - `frameStatsMaxInstant=4000`
  - `rawPresentMonSemanticsKept=true`

证据：

- `liv-report\installed-report-smoke-summary.json`

## Target / Settings Smoke

结果：PASS

Seed target 场景：

- Target add：`targetAddSaved=true`
- Target edit：`targetEditSaved=true`
- Target delete：`targetDeleteSaved=true`
- Settings 保存：`settingsSaved=true`
- Settings 重启后读取：`restartTelemetrySampleIntervalMs=1375`
- 未触碰用户真实 config：`userConfigTouched=false`

空 target 场景：

- Target add/edit/delete：全部为 `true`
- 删除最后一个 target 后：`finalTargetCount=0`
- Settings 保存：`settingsSaved=true`
- 最终 telemetry interval：`1375`

证据：

- `liv-ts\target-settings-evidence-summary.json`
- `liv-ts-empty\target-settings-empty-summary.json`

## 必要测试结果

结果：PASS

本轮重新运行了用户指定的必要验证命令，所有 exit code 均为 `0`：

| 命令 | 结果 |
| --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS，Vitest `6 files / 64 tests` passed，Vite build passed |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS，`FrameScope tests rebuilt.` |
| `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe` | PASS |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS |
| `.\tests\FrameScopeProcessCleanupTests.exe` | PASS |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS |

证据：

- `docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-localization-worker-single-instance-evidence\command-logs-final\summary.json`
- `docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-localization-worker-single-instance-evidence\command-logs-final\*.txt`

## 收尾检查

`git diff --check`：PASS

- Exit code：`0`
- 输出只有 LF/CRLF 转换 warning，没有 whitespace error。
- 证据：`docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-localization-worker-single-instance-evidence\final-git-diff-check.txt`

最终残留进程检查：PASS

- 输出：`NO_MATCHING_RESIDUAL_PROCESSES`
- 证据：`docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-localization-worker-single-instance-evidence\final-residual-process-check.txt`

## 最终结论

PASS。

本地 quiet 更新安装成功，安装前后数据目录保留；installed payload 与 dist payload 子集 hash 完全一致；installed 单实例 smoke、worker 说明、worker 绕过单实例锁、图表中文化、`bucketMs=1000`、`DATA.cpuVoltage`、`DATA.cpuVid`、FPS raw PresentMon 统计语义、Vcore/VID 隔离、target/settings/report smoke 以及必要自动化测试均通过。未启动真实游戏、未测试 BF6、未推 GitHub、未更新 GitHub Release。
