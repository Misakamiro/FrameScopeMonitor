# FrameScope Monitor 全量安装版模拟验收复测报告

- 日期：2026-05-30
- 范围：安装版更新、安装版 WebView2 UI smoke、模拟目标监测、fake PresentMon 成功/失败分支、报告图表、CPU Core VID、旧 Vcore 兼容、轻量性能门禁、残留进程检查
- 源码路径：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
- 安装目录：`C:\Users\misakamiro\AppData\Local\FrameScopeMonitor`
- 用户数据目录：`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData`
- 证据目录：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\qa0530-full-installed`

## 总结状态

结论：`PARTIAL`

安装版主体可用，构建、单元/契约测试、WebView2 live/reduced-motion smoke、tray lifecycle smoke、fake target full report、PresentMon silent/access-denied/missing-csv 失败分支、CPU Core VID、旧 Vcore 兼容、轻量性能门禁和最终残留进程检查均有证据通过。

没有给 `PASS` 的原因有三点：

1. 6 个模拟目标均生成 full report，但 `qa-target-summary.json` 中 `htmlHasTargetName=false`、`targetTextOk=false`；报告页面显示的是进程名，例如 `cs2.exe`，没有显示配置目标名，例如 `Counter-Strike 2`。这不是串到其他游戏，但没有满足“报告文案不串 target / target 文案正确”的完整验收。
2. 报告图表截图证据显示 `1280x720` 下 `scrollWidth=1310`、`clientWidth=1280`，右侧概览卡片可见被裁切，未满足“1280x720 不横向溢出”的要求。`900x760` 证据没有横向溢出。
3. 本轮没有获得 Target 新增/编辑/删除 UI、目标编辑弹窗、设置保存后重启仍保持的独立截图级证据；已有证据覆盖了 Targets 列表、bridge `targets.get`、bridge path rejection、Settings 保存过程和配置重载，但没有覆盖完整手工级 CRUD。

## 安装更新与环境

本机安装更新：已完成。

- 构建产物：`dist\FrameScopeMonitor-Full-Setup.exe`
- 安装命令：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\dist\FrameScopeMonitor-Full-Setup.exe /quiet`
- 安装日志：`C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\install.log`
- 安装日志确认：`2026-05-30T13:53:06.9630493+08:00 install-start`，`2026-05-30T13:53:07.0435625+08:00 install-complete`
- 备注：quiet install wrapper 日志没有写入 `END/EXIT_CODE`，原因记录为安装后 FrameScopeMonitor 保持运行导致 wrapper 超时；安装目录文件时间和 install.log 可确认安装完成。

关键文件存在性：

| 文件 | 结果 | 时间戳 |
|---|---:|---|
| `FrameScopeMonitor.exe` | PASS | 2026-05-30 13:52:36 |
| `FrameScopeSystemSampler.exe` | PASS | 2026-05-30 13:52:36 |
| `FrameScopeProcessSampler.exe` | PASS | 2026-05-30 13:52:36 |
| `FrameScopeReportGenerator.exe` | PASS | 2026-05-30 13:52:36 |
| `tools\PresentMon-2.4.1-x64.exe` | PASS | 2026-05-03 01:15:16 |
| `LibreHardwareMonitorLib.dll` | PASS | 2026-02-14 19:16:28 |
| `Microsoft.Web.WebView2.Core.dll` | PASS | 2026-05-04 18:06:12 |
| `Microsoft.Web.WebView2.WinForms.dll` | PASS | 2026-05-04 18:06:24 |
| `WebView2Loader.dll` | PASS | 2026-05-04 18:06:34 |

用户数据未删除：`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData` 仍存在，`framescope-runs` 与 `diagnostic-reports` 均存在；复测时没有执行删除用户数据操作。

## 真实游戏边界

- 没有启动真实 Valorant / 无畏契约：PASS
- 没有启动真实 BF6：PASS
- 没有启动任何真实游戏：PASS
- 使用方式：所有目标均使用 `artifacts\qa0530-full-installed\runs\sim-targets\bin` 下的 fake target exe 和 `GenericFakePresentMon.exe`
- 最终残留检查：`realGameProcessCount=0`
- 证据：`artifacts\qa0530-full-installed\command-logs\16-final-residual-process-check.txt`

## UI 全量功能验收

| 区域 | 结果 | 证据 |
|---|---:|---|
| Overview 加载、页面 ready、状态刷新 | PASS | `smoke\installed-live-smoke.json`：`success=true`、`pageLoaded=true`、`pageReady=true`、`reactOverviewLoaded=true`、`processRefreshObserved=true` |
| 首次开始监测即时反馈 | PASS | `bridgeExtensionSmoke.monitorStartAccepted=true`、`monitorStarted=true`、`monitorStopAccepted=true`、`monitorStopped=true` |
| console error | PASS | live/reduced smoke 均未记录 console errors |
| light/dark/system 主题 | PASS | `themeSmoke.success=true`，截图：`installed-live-smoke-settings-light.png`、`installed-live-smoke-settings-dark.png`、`installed-live-smoke-settings-system.png` |
| Overview/Reports/Settings 主题生效 | PASS | `themeSmoke.overviewLight/dark/system=true`、`reportsLight/dark/system=true`、`settingsLight/dark/system=true` |
| Tray 隐藏/恢复/退出确认/重复图标 | PASS | `installed-tray-smoke.json`：`duplicateTrayIconsPrevented=true`、`blockedExit=true`、`exitAllowedWithoutActiveMonitoring=true` |
| Settings 全局采样间隔 | PASS | 截图：`smoke\installed-live-smoke-settings-sampling.png`，显示默认 `1000 ms`、范围 `500-5000 ms` |
| Settings 无真实 Vcore 设置 | PASS | 截图：`smoke\installed-live-smoke-settings-sampling.png`，仅显示 CPU Core VID 文案 |
| CPU Core VID 文案 | PASS | Settings 和报告图表均说明 VID 是请求/目标电压，不是真实 Vcore |
| 打开日志目录 | PASS | `bridgeExtensionSmoke.logsOpenDirectoryOk=true`，并且 `logsOpenPathRejected=true` |
| Targets 列表加载 | PASS | `reactTargetsLoaded=true`，截图：`smoke\installed-live-smoke-targets-result.png` |
| Targets 列表不显示 per-target sampling | PASS | `installed-live-smoke-targets-result.png` 未显示 per-target 采样字段 |
| Target 新增/编辑/删除 UI | PARTIAL | 没有独立截图级证据；bridge `targets.get` 和 path rejection 通过 |
| Target edit 弹窗无 per-target sampling | PARTIAL | 未捕获 edit modal 截图 |
| Reports 列表/打开报告/打开目录/重新生成 | PASS | `reportLiveActionSmoke.success=true`，open/openDirectory/regenerate 均成功 |
| Reports reduced-motion 收敛 | PASS | `installed-reduced-motion-smoke.json`：`success=true`、`reducedMotion=true` |
| FPS 下拉四项且无“最低瞬时 FPS” | PASS | `report-chart-screenshot-evidence.json`：`fpsOptionPass=true`、`noMinInstantOption=true` |

## 模拟目标覆盖

读取当前安装配置后，覆盖 6 个启用目标：

| 目标 | fake 进程 | PID | CSV 行数 | full report | hasFrameData | CPU Core | CPU VID | cpu-voltage-* | target 文案 |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Counter-Strike 2 | `cs2.exe` | 30436 | 240 | PASS | PASS | 128 | 64 | 未生成 | PARTIAL |
| PUBG: BATTLEGROUNDS | `TslGame.exe` | 3264 | 240 | PASS | PASS | 128 | 64 | 未生成 | PARTIAL |
| Delta Force | `DeltaForceClient-Win64-Shipping.exe` | 29616 | 240 | PASS | PASS | 128 | 64 | 未生成 | PARTIAL |
| Neverness To Everness | `HTGame.exe` | 9368 | 240 | PASS | PASS | 128 | 64 | 未生成 | PARTIAL |
| Valorant | `VALORANT-Win64-Shipping.exe` | 31368 | 240 | PASS | PASS | 128 | 64 | 未生成 | PARTIAL |
| Battlefield 6 | `bf6.exe` | 3160 | 240 | PASS | PASS | 128 | 64 | 未生成 | PARTIAL |

模拟目标共同结论：

- `targetResolvedProcess` 与 `targetPid` 均正确。
- PresentMon args 使用 `--process_id <pid>` 和目标 run 下的 `presentmon.csv`。
- `presentmon.csv` 存在且有效行 `240`。
- `status.json`、`summary.json`、`framescope-interactive-manifest.json` 均一致记录 `captured/full/hasFrameData=true`。
- `DATA.presentMon.rawRows=240`、`DATA.presentMon.validRows=240`，图表使用 raw PresentMon 行。
- 每个目标没有生成 `cpu-voltage-samples.csv` 或 `cpu-voltage-telemetry-status.json`。
- 问题：`qa-target-summary.json` 中 `htmlHasTargetName=false`、`targetTextOk=false`。报告页没有串到其他游戏，但显示目标名不完整，实际显示进程名。

## PresentMon 失败分支回归

| 分支 | 结果 | 关键证据 |
|---|---:|---|
| silent no-csv | PASS | `silent-no-csv-20260530-143332\status.json`：`FrameCaptureStatus=presentmon-no-csv-silent`、`PresentMonFailureCategory=presentmon-no-csv-silent`、`PresentMonExitCode=0`、`stdoutTail=Started recording.` |
| access denied | PASS | `access-denied-20260530-143350\status.json`：`FrameCaptureStatus=presentmon-etw-access-denied`、`PresentMonFailureCategory=presentmon-etw-access-denied`、`PresentMonExitCode=6`、`stderrTail=failed to start trace session: access denied` |
| missing csv 普通分支 | PASS | `missing-csv-20260530-143408\status.json`：`FrameCaptureStatus=no-presentmon-csv`、`PresentMonFailureCategory=missing-presentmon-csv`、`PresentMonExitCode=3` |

所有三类失败分支均生成 diagnostic report，manifest 中 `reportKind=diagnostic`、`hasFrameData=false`。silent no-csv 的诊断字段包含 PresentMon args、CSV path、CSV last check time、runtime、stdout/stderr tail、target pid、resolved process。

备注：目录中还保留一个早期误启动证据 `silent-no-csv-20260530-142610`，其状态是 `target-not-found`，不作为最终 silent no-csv 结果；最终有效分支是 `silent-no-csv-20260530-143332`。

## 采样与硬件数据

| 项目 | 结果 | 证据 |
|---|---:|---|
| process-samples.csv | PASS | 6 个模拟目标均生成 |
| system-samples.csv | PASS | 6 个模拟目标均生成 |
| 默认 1000ms 采样 | PASS | 模拟目标命令均使用 `--SampleIntervalMs 1000`、`--ProcessSampleIntervalMs 1000`、`--CpuCoreSampleIntervalMs 1000`、`--CpuVidSampleIntervalMs 1000` |
| 1500ms 统一采样 | PASS | `sampling-1500-summary.json`：system avg `1504.6ms`、cpuCore avg `1504.4ms`、cpuVid avg `1503.3ms` |
| CPU core frequency | PASS | `cpu-core-samples.csv` 128 行，16 logical processor，Actual Frequency 有动态值 |
| CPU Core VID | PASS | `cpu-vid-samples.csv` 64 行，8 个 core，source=`builtin-librehardwaremonitor`，status=`core-vid-available` |
| 新 run 不生成 cpu-voltage-* | PASS | 模拟目标 run 目录没有 `cpu-voltage-*` 文件 |
| 旧 Vcore run 兼容 | PASS | `old-vcore-compat-summary.json`：`reportExit=0`、`pass=true`、旧 `cpu-voltage-samples.csv` 不导致崩溃 |

旧配置兼容说明：当前安装配置仍可看到历史字段，如 `CpuTelemetry.CollectCpuVoltage`、`PerCoreVoltageSampleIntervalMs`、`VoltageProvider` 和 target 内部 `SampleIntervalMs`。本轮实测结论是：这些字段没有让新 run 生成 `cpu-voltage-*`，UI 也没有重新暴露真实 Vcore 设置；但配置文件字段本身仍作为兼容残留存在。

## 报告图表验收

FPS 图表：

- `presentMon.rawRows=240`
- `presentMon.validRows=240`
- `frameStats.average=64.21`
- `frameStats.low1=8.93`
- `frameStats.low01=8.93`
- `chart-sampling-tests: PASS`
- `report-chart-screenshot-evidence.json`：`fpsOptionPass=true`、`noMinInstantOption=true`
- 红色异常帧点：`redAnomalyLegend=true`、截图像素检查 `redPixels=437`
- tooltip：`tooltipVisible=true`，示例包含 raw point 时间和值
- 未发现 `bucketMs` / `lowWindowMs` 回归证据

其他图表：

- process CPU / memory / IO：报告页可渲染，process data 使用 `rle-v1`
- system CPU / memory：报告页可渲染
- CPU core frequency：截图 `cpu-core-frequency-1280x720.png`、`cpu-core-frequency-900x760.png`
- CPU Core VID：截图 `cpu-core-vid-1280x720.png`、`cpu-core-vid-900x760.png`

图表布局问题：

- `fps-default-1280x720` 证据：`scrollWidth=1310`、`clientWidth=1280`，右侧摘要卡片可见被裁切。结论：`1280x720` 存在横向溢出。
- `fps-default-900x760` 证据：`scrollWidth=900`、`clientWidth=900`，未见横向溢出。

## 日志与诊断

默认日志：

- `framescope-watcher.log` 尾部 156 行中主要是生命周期、report、monitor 和 PresentMon stop 事件。
- 未见持续刷 verbose/perf/debug 噪声。
- `EnableVerboseLogs=false`、`EnablePerformanceDiagnosticsLogs=false`。

诊断：

- 失败分支 diagnostic report 正常生成。
- 健康 full report 未产生无意义 diagnostic 自动报告证据。
- “打开日志目录”按钮通过 host 解析目录打开，不允许前端传任意路径：`logsOpenDirectoryOk=true`、`logsOpenPathRejected=true`。

## 性能与资源占用

轻量性能门禁结果：

| 项目 | 结果 |
|---|---:|
| 安装版 idle 总 CPU | `0%` |
| 安装版 idle 总 working set | `398.4 MB` |
| synthetic monitor 运行中总 CPU | `0.1%` |
| synthetic monitor 运行中总 working set | `122.5 MB` |
| FrameScopeMonitor synthetic CPU | `0%` |
| FrameScopeProcessSampler synthetic CPU | `0%` |
| 报告生成耗时 | `1019 ms` |
| performance-gate data.js | `28487 bytes` |

结论：未见明显性能回退。Reports 切换和 reduced-motion smoke 都成功；但报告页 1280 宽度横向溢出需要修复后再做一次视觉性能复测。

## 必须运行命令结果

| 命令 | 结果 | 证据 |
|---|---:|---|
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | `command-logs\01-run-frontend-verify.log`，`EXIT_CODE: 0` |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | `command-logs\02-build.log`，`EXIT_CODE: 0` |
| full setup quiet install | PARTIAL | `install.log` 有 `install-complete`，wrapper 日志无 `END/EXIT_CODE` |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | `command-logs\04-build-tests.log`，`EXIT_CODE: 0` |
| 所有 `tests\FrameScope*Tests.exe` | PASS | `command-logs\05-tests-summary.json`，18 个 exe exitCode 全为 0 |
| `node .\tests\chart-sampling-tests.js` 默认 node | PARTIAL | `command-logs\06-chart-sampling-tests.log`，WindowsApps/Codex node shim 未给出正常 exit code |
| bundled Node 跑 chart-sampling | PASS | `command-logs\06b-chart-sampling-tests-bundled-node.log`，`chart-sampling-tests: PASS`，`EXIT_CODE: 0` |
| WebView2 live smoke | PASS | `smoke\installed-live-smoke.json`，`success=true` |
| WebView2 reduced-motion smoke | PASS | `smoke\installed-reduced-motion-smoke.json`，`success=true` |
| tray lifecycle smoke | PASS | `smoke\installed-tray-smoke.json`，`success=true` |
| `git diff --check` | PASS | `command-logs\15-git-diff-check.log`，`EXIT_CODE: 0`；仅 LF/CRLF warning |
| 最终残留进程检查 | PASS | `command-logs\16-final-residual-process-check.txt`，`NO_MATCHING_RESIDUAL_PROCESSES` |

## 截图和 artifact 路径

核心证据：

- Settings light：`artifacts\qa0530-full-installed\smoke\installed-live-smoke-settings-light.png`
- Settings dark：`artifacts\qa0530-full-installed\smoke\installed-live-smoke-settings-dark.png`
- Settings system：`artifacts\qa0530-full-installed\smoke\installed-live-smoke-settings-system.png`
- Settings 全局采样间隔 / VID 文案 / 无真实 Vcore 设置：`artifacts\qa0530-full-installed\smoke\installed-live-smoke-settings-sampling.png`
- Settings 日志与诊断：`artifacts\qa0530-full-installed\smoke\installed-live-smoke-settings-clean.png`
- Targets 列表无 per-target sampling：`artifacts\qa0530-full-installed\smoke\installed-live-smoke-targets-result.png`
- Reports 列表：`artifacts\qa0530-full-installed\smoke\installed-live-smoke-reports.png`
- Reports 操作：`installed-live-smoke-reports-open-success.png`、`installed-live-smoke-reports-open-directory-success.png`、`installed-live-smoke-reports-regenerate-success.png`
- FPS 四个下拉选项：`artifacts\qa0530-full-installed\screenshots\report-charts\fps-dropdown-control-1280x720.png`
- FPS 红色异常点：`artifacts\qa0530-full-installed\screenshots\report-charts\fps-default-1280x720.png`
- FPS tooltip：`artifacts\qa0530-full-installed\screenshots\report-charts\fps-tooltip-visible-1280x720.png`
- CPU Core Frequency 图：`artifacts\qa0530-full-installed\screenshots\report-charts\cpu-core-frequency-1280x720.png`
- CPU Core VID 图：`artifacts\qa0530-full-installed\screenshots\report-charts\cpu-core-vid-1280x720.png`
- Tray lifecycle：`artifacts\qa0530-full-installed\smoke\installed-tray-smoke.json`
- WebView2 live smoke JSON：`artifacts\qa0530-full-installed\smoke\installed-live-smoke.json`
- WebView2 reduced-motion smoke JSON：`artifacts\qa0530-full-installed\smoke\installed-reduced-motion-smoke.json`
- 每个模拟 target run summary：`artifacts\qa0530-full-installed\runs\sim-targets\runs\*\qa-target-summary.json`
- silent no-csv 诊断：`artifacts\qa0530-full-installed\runs\failure-branches\silent-no-csv-20260530-143332\status.json`
- access denied 诊断：`artifacts\qa0530-full-installed\runs\failure-branches\access-denied-20260530-143350\status.json`

缺口：

- 没有 target edit modal 截图。
- 没有 Target 新增/删除 UI 截图。
- 没有设置保存后“退出应用再重新启动”的独立截图证据。

## 残留进程检查

最终检查时间：`2026-05-30T14:56:25.3424862+08:00`

结果：

- `residualCount=0`
- `realGameProcessCount=0`
- `NO_MATCHING_RESIDUAL_PROCESSES`

检查覆盖：

- `FrameScopeMonitor.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `FrameScopeProcessSampler.exe`
- `PresentMon-2.4.1-x64.exe`
- `GenericFakePresentMon.exe`
- fake target processes：`cs2.exe`、`TslGame.exe`、`DeltaForceClient-Win64-Shipping.exe`、`HTGame.exe`、`VALORANT-Win64-Shipping.exe`、`bf6.exe`
- Vite / Playwright / FrameScope WebView2 test profile

## 后续建议

建议后续做真实游戏人工验收：是，但建议先修复两个自动验收中已经发现的问题：

1. full report 应显示配置目标名，并保证不会串到其他 target；当前只显示进程名，自动 target 文案检查为失败。
2. 报告图表页在 `1280x720` 需要消除横向溢出或明确做可控横向滚动布局。

修复后建议再跑一次本报告同款模拟验收，然后选择 1 个真实游戏做人工短流程验收：启动游戏、开始采集、停止采集、打开报告、确认 PresentMon 权限/反作弊环境下行为和用户可见提示。
