# FrameScope Monitor 本地安装更新验证报告

日期：2026-06-13
结论：PARTIAL

## 范围

本轮只做 FrameScope Monitor “图表断层毛刺 + CPU VID 修复”后的本地安装更新验证和报告。

明确未做：
- 未启动真实游戏。
- 未测试 BF6。
- 未推 GitHub。
- 未更新 GitHub Release。
- 未删除用户历史数据。

工作区：
`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

用户 Valorant run：
`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260613-134745`

证据目录：
`docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-chart-dropout-and-vid-fix-evidence`

## 总结

本轮 `dist\FrameScopeMonitor-Full-Setup.exe /quiet` 本地 quiet 更新安装成功，安装器 exit code 为 `0`。安装器自动启动的 FrameScope UI 已正常关闭，未对自动启动的普通 UI 做 force kill。

`%LOCALAPPDATA%\FrameScopeMonitorData` 安装前后均存在且未被清空。安装前基线文件数 `923`、总字节 `3060339891`；安装后复核文件数 `949`、总字节 `4222023465`。增长来自本轮报告重生成、诊断/验证副产物，不是安装器清空历史数据目录。

对用户 Valorant run 用 installed `FrameScopeReportGenerator.exe` 重生成报告后，关键验收项通过：
- `bucketMs=1000`
- `DATA.cpuVoltage` 存在且可用
- Vcore 范围保持 `0.960-1.104V`
- `DATA.cpuVid` 存在但 `available=false`
- `DATA.cpuVid.reason` 明确说明 AMD LibreHardwareMonitor Core VID `0.4-0.7V` 全部被拒绝，且 `CPU Voltage / Vcore remains separate and is not used as VID`
- `DATA.cpuVid.series` 为空
- 未把 Vcore 写进 `DATA.cpuVid`
- GPU 最低时钟 `225 MHz` 真实低 P-state 保留
- CPU 电压 / Vcore、CPU 频率、GPU 时钟、内存时钟在数据层未出现无效 `0` / `0<x<0.7` 扎底值
- 中文图表文案存在

但本轮最终仍是 `PARTIAL`，原因已经由 fresh post-install smoke 证据重新坐实：
- fresh installed `--web-ui-smoke` live / reduced 都稳定复现 .NET 早崩退出码 `-532462766`，且在写出任何 smoke JSON / screenshot 之前就退出。
- fresh installed target/settings smoke 未在预期时间内产出 smoke JSON / screenshot；两个隐藏 helper 分别卡在 `--web-ui-target-settings-evidence-smoke` 和 `--web-ui-settings-persistence-read-smoke`，因无主窗口、无法 `WM_CLOSE`，只能在记录证据后强制清理。
- 因此，安装后的真实 installed app smoke 仍不能整体判为 PASS。

## 安装器与数据目录

安装命令：
`dist\FrameScopeMonitor-Full-Setup.exe /quiet`

结果：
- Installer exit code：`0`
- 自动启动 UI 已正常关闭
- 自动启动 UI 未强杀

数据目录保留：
- 安装前：`Exists=true`
- 安装后：`Exists=true`
- 结论：保留，未被清空

主要证据：
- `01-pre-install-baseline.json`
- `02d-full-setup-dotnet-process-exit-and-close.json`
- `03-post-install-dir-summary.json`

## 待验证产物 SHA256

本轮核对结果：
- `dist\FrameScopeMonitor-Setup.exe`：`8E3A301D7D2C4AC18FD2EA1F83BDDDE5FCFFB96985F303DAD09A25785B9CD5A3`
- `dist\FrameScopeMonitor-Full-Setup.exe`：`0C724E50BE1DC133BC39F188199810F4400340AD5540B656A8DAE2855ACC0901`
- `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe`：`EEA59166F2FEAB7A89DD3580A62481B520976BCD9D5FD0445A7D0B744FB3165C`
- `dist\FrameScopeMonitor-payload\frontend\assets\index-m2r1Gfgc.js`：`2DB69188D6FD4A6B2CA08379BFE38C89833C4188A427D3734B3719842BF302CE`

结论：与任务给定 SHA256 一致。

## Installed payload hash parity

直接拿 `dist\FrameScopeMonitor-payload` 全目录对比 installed 目录会被 `smoke-temp` 等非安装内容污染；因此本轮用 `FrameScopeMonitor-Full-Setup.exe` 内嵌资源 `FrameScopePayload` 抽出 shipped payload，再与 `%LOCALAPPDATA%\FrameScopeMonitor` 对比。

结果：PASS
- Embedded payload files：`30`
- Matched：`30`
- Missing：`0`
- Mismatch：`0`
- Match：`true`

结论：按安装器真正 shipped 的 payload 子集口径，installed payload 与 installer payload file-level/hash 一致。

证据：
- `04b-installed-vs-embedded-payload-hash-parity.json`

## Installed Valorant run report smoke

installed `FrameScopeReportGenerator.exe` 对用户 Valorant run 重生成报告结果：PASS

关键结果：
- report generator exit code：`0`
- `bucketMs=1000`
- `DATA.cpuVoltage.available=true`
- `DATA.cpuVoltage.series[0].name = CPU 电压 / Vcore`
- Vcore 最小值：`0.960V`
- Vcore 最大值：`1.104V`
- `DATA.cpuVid.available=false`
- `DATA.cpuVid.seriesCount=0`
- `DATA.cpuVid.reason`：AMD LibreHardwareMonitor Core VID 全部落在不可信的 `0.4-0.7V`，已拒绝；并明确 `CPU Voltage / Vcore remains separate and is not used as VID`
- `cpuVidContainsVcoreNamedSeries=false`
- `gpuClockMin=225`
- `cpuFreqZeroLikeCount=0`
- `gpuClockZeroLikeCount=0`
- `memClockZeroLikeCount=0`
- HTML 中文文案存在：`帧率`、`CPU 电压 / Vcore`、`CPU 核心 VID`、`无可绘制数据`
- 布局探针：`allNoOverflow=true`

说明：
- `DATA.cpuVid.available=false` 在这轮是预期行为。
- 已确认没有把 Vcore 冒充成 VID。
- 已确认 GPU 真实 `225 MHz` 低 P-state 保留。
- 已确认中文图表文案存在。

额外观察：
- `DATA.cpuVid.available=false` 与用户验收项一致。
- 但本轮发现 `manifest.cpuVidAvailable` 仍为 `true`、`manifest.cpuVidReason` 为空，和 DATA 层状态不一致。这不影响本任务明确要求的 DATA 层验收项，但它是本轮验证中发现的附带问题。

证据：
- `05-report-generator-run.json`
- `06-valorant-report-stats.json`
- `06b-valorant-report-verify.json`
- `layout-probe\report-overflow-probe.json`

## Installed app smoke

### 1. Fresh post-install WebView2 live / reduced

结果：FAIL

本轮在最终安装（`install.log` 记录 `2026-06-13T17:46:57+08:00 install-start` / `17:46:57+08:00 install-complete`）之后重新对 installed app 做 fresh 重放：
- `live-fresh.exitCode = -532462766`
- `reduced-fresh.exitCode = -532462766`
- `live-fresh.json` / `reduced-fresh.json` 均未生成
- `live-fresh.png` / `reduced-fresh.png` 均未生成

结论：这不是“只差超时没等到证据”，而是 installed WebView2 smoke 在更早阶段就崩溃退出。

证据：
- `installed-webview2-smoke-fresh\summary.json`

### 2. Fresh post-install target add/edit/delete + Settings

结果：FAIL

本轮用 installed app 再次执行 target/settings smoke：
- fresh `target-settings-crud-smoke.json` 未生成
- fresh `settings-restart-persistence-smoke.json` 未生成
- fresh screenshot 未生成
- 第一个隐藏 helper 卡在：
  - `--web-ui-target-settings-evidence-smoke`
- 第二个隐藏 helper 卡在：
  - `--web-ui-settings-persistence-read-smoke`
- 两个 helper 都没有主窗口，无法 `WM_CLOSE`，在记录证据后做了强制清理

说明：这里的 force kill 仅用于“无窗口、无响应、已超时的测试 helper”，不是安装器自动启动 UI，也不是普通用户可见 UI 的异常处理方式。

证据：
- `liv-ts-fresh\target-settings-evidence-summary.json`
- `liv-ts-fresh\hung-process-summary.json`
- `liv-ts-fresh\hung-process-summary-settings-restart.json`

### 3. 单实例、worker 说明、worker 绕锁

结果：部分确认

本轮没有补到“最终 17:46 quiet 安装之后”的 fresh installed single-instance smoke JSON；因此这一项不能按 fresh installed smoke PASS 记。

但同日 earlier installed smoke 与 fresh 自动化回归仍显示这些功能没有逻辑回退：
- `2026-06-13 11:49:35 +08:00` 的 installed single-instance / worker smoke 显示：
  - 首次普通 UI 启动正常
  - 第二次普通启动不产生第二窗口
  - 中文提示 `FrameScope Monitor 已在运行，请勿重复打开。`
  - watcher / monitor-session 不被单实例锁误挡
  - 清理后 `postCleanupProcessCount=0`
- 本轮 fresh 回归测试 `FrameScopeSingleInstanceLaunchGuardTests.exe` 通过
- target/settings earlier installed smoke（同日 `11:32:54+08:00`）里也捕获到 worker 说明文本

因此：
- 中文化：在报告层明确确认
- worker 说明：有 installed 证据命中
- 单实例与 worker 绕锁：有同日 installed smoke + 本轮 fresh regression tests 支撑
- 但因为缺少“最终这次 quiet 安装后”的 fresh installed single-instance smoke 产物，总体仍不能把 installed app smoke 判成 PASS

可复用证据：
- `liv-si-worker\installed-ui-single-instance-worker-smoke.json`
- `liv-ts\smoke\target-settings-crud-smoke.json`
- `liv-ts\smoke\settings-restart-persistence-smoke.json`

## 指定验证命令结果

以下命令已 fresh 重跑：
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`：PASS
  - `6` 个 Vitest 文件通过，`64` 个测试通过，production build 通过
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`：PASS
- `.\tests\FrameScopeReportManifestTests.exe`：PASS
- `.\tests\FrameScopeDiagnosticsTests.exe`：PASS
- `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`：PASS
- `.\tests\FrameScopeNativeWatcherPolicyTests.exe`：PASS
- `.\tests\FrameScopeNativeMonitorChildProcessTests.exe`：PASS
- `.\tests\FrameScopeProcessCleanupTests.exe`：PASS
- `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe`：PASS
- bundled Node `.	ests\chart-sampling-tests.js`：PASS
- `git diff --check`：PASS（exit `0`；未发现 whitespace error）

证据：
- `command-logs-final-rerun\01-frontend-verify.txt`
- `command-logs-final-rerun\02-build-framescope-tests.txt`
- `command-logs-final-rerun\03-FrameScopeReportManifestTests.txt`
- `command-logs-final-rerun\04-FrameScopeDiagnosticsTests.txt`
- `command-logs-final-rerun\05-FrameScopeSystemSamplerCpuCoreTests.txt`
- `command-logs-final-rerun\06-FrameScopeNativeWatcherPolicyTests.txt`
- `command-logs-final-rerun\07-FrameScopeNativeMonitorChildProcessTests.txt`
- `command-logs-final-rerun\08-FrameScopeProcessCleanupTests.txt`
- `command-logs-final-rerun\09-FrameScopeSingleInstanceLaunchGuardTests.txt`
- `command-logs-final-rerun\10-chart-sampling-tests.txt`
- `command-logs-final-rerun\11-git-diff-check.txt`

## 残留进程检查

结果：PASS

最终输出：
`NO_MATCHING_RESIDUAL_PROCESSES`

说明：在 fresh target/settings smoke 超时后，现场出现两个无窗口隐藏 helper。它们都已在记录证据后清理完毕；清理后重新复核，最终残留进程检查为干净状态。

证据：
- `final-residual-process-check.txt`
- fresh 终检输出：`NO_MATCHING_RESIDUAL_PROCESSES`

## 最终结论

PARTIAL

原因不是安装失败，也不是图表 / CPU VID 修复无效；相反，下列核心目标都已完成并有证据证明：
- quiet 更新安装成功，installer exit code `0`
- 用户数据目录保留，未清空
- installed payload 与 installer payload hash parity 通过
- installed report generator 对用户 Valorant run 重生成结果符合预期：
  - `bucketMs=1000`
  - `DATA.cpuVid.available=false`
  - Vcore 保持在 `0.960-1.104V`
  - 未把 Vcore 冒充 VID
  - GPU `225 MHz` 保留
  - 图表数据层无无效 `0` 扎底
  - 中文图表文案存在
- 指定自动化测试与 `git diff --check` 全部通过
- 最终残留进程检查输出 `NO_MATCHING_RESIDUAL_PROCESSES`

但以下 installed app smoke 项，本轮仍没有被“最终这次 quiet 安装之后的 fresh installed 证据”证明通过：
- WebView2 live/reduced smoke
- target add/edit/delete 到 `finalTargetCount=0`
- Settings 保存/读取
- fresh installed app 级单实例 smoke 完整产物

因此，本轮不能给 PASS，只能给 PARTIAL。
