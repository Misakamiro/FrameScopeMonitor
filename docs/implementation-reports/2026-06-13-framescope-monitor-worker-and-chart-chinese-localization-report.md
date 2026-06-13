# FrameScope Monitor worker 与图表中文化修正报告

日期：2026-06-13

## 范围

本次处理两个用户反馈：

1. 启动监控后任务管理器里出现两个 `FrameScopeMonitor.exe`，用户不理解原因。
2. 报告/图表里仍有较多英文可见文案。

本次没有触碰 GameLite/lightweight 文件，没有安装 FrameScope，没有运行 setup/full setup，没有启动真实游戏，没有测试 BF6，没有推 GitHub，也没有更新 GitHub Release。

## 双进程诊断结论

两个 `FrameScopeMonitor.exe` 是预期 worker 架构，不是重复打开软件。

确认到的代码路径：

- UI 进程启动同一个可执行文件并传入 `--watcher`。
- watcher 进程再启动同一个可执行文件并传入 `--monitor-session`。
- 这样可以把 UI/WebView 编排和 native monitor session 隔离开。
- 本次保持这个隔离架构，没有为了任务管理器只显示一个进程而合并 UI 和 worker。

停止/清理结论：

- 停止监控的返回文案现在会说明 watcher / monitor-session 子进程已清理。
- cleanup 策略覆盖 `--watcher` 和 `--monitor-session`。
- `FrameScopeProcessCleanupTests.exe`、`FrameScopeNativeWatcherPolicyTests.exe`、`FrameScopeNativeMonitorChildProcessTests.exe` 均通过。
- 最终残留进程检查输出：`NO_MATCHING_RESIDUAL_PROCESSES`。

由于任务边界要求不启动真实游戏、不测试 BF6，本轮没有做真实游戏采集计数；结论来自源码路径核对、watcher/child-process smoke、cleanup 测试和残留进程检查。

## 体验修正

已在这些位置补充 worker 说明和状态字段：

- `src/app/FrameScopeNativeMonitor.WebHost.cs`
- `src/app/FrameScopeNativeMonitor.Watcher.cs`
- `src/app/FrameScopeWebBridge.State.cs`
- `src/app/FrameScopeWebBridge.Monitoring.cs`
- `src/frontend/src/bridge/contract.ts`
- `src/frontend/src/state/useFrameScopeBridgeState.ts`
- `src/frontend/src/data/mockPreview.ts`
- `src/frontend/src/pages/OverviewPage.tsx`
- `src/frontend/src/pages/pages.css`

UI/状态区现在会提示：

> 任务管理器中可能显示一个 FrameScopeMonitor.exe 子进程；这是监控 worker，不是重复打开软件。

诊断和日志现在可以区分角色：

- `processRole = watcher-worker`
- `WorkerRole = monitor-session-worker`
- `workerProcessName = FrameScopeMonitor.exe`
- 日志包含 `monitor-worker-start role=watcher-worker`
- 日志包含 `monitor-worker-start role=monitor-session-worker`
- monitor-session 启动参数包含 `--MonitorProcessRole monitor-session-worker`

## 报告/图表中文化

已中文化这些报告生成代码里的用户可见文案：

- `src/reporting/FrameScopeReportGenerator.Html.Sections.cs`
- `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`
- `src/reporting/FrameScopeReportGenerator.SystemData.cs`

覆盖范围：

- 图表 tab：帧率、CPU 核心频率、CPU 电压 / Vcore、CPU 核心 VID（请求电压）、性能图表、系统占用、后台进程、IO/温度
- 图表标题和说明
- FPS 图例/参考线：最小值、最大值、平均值
- FPS tooltip：样本数
- FPS、CPU 核心、CPU VID、系统占用、后台进程、IO 的 metric 下拉项
- 空状态：无可绘制数据、无 CPU 电压 / Vcore 数据、无 CPU 核心 VID 数据
- 仪表盘和 summary：平均、占用、温度、长帧、最大帧时间
- 后台进程 Top N 说明：前 10 个进程
- CPU Voltage / Vcore 显示：CPU 电压 / Vcore
- CPU Core VID 显示：CPU 核心 VID（请求电压），并明确 VID 是每核心请求/目标电压，不是真实 Vcore
- IO/系统标签：磁盘吞吐、网络吞吐、磁盘延迟、GPU 功耗、GPU 温度、CPU/GPU/内存/VRAM 占用

## 未改机器 key 和语义

以下机器 key、schema 和语义没有改名：

- `DATA.cpuVoltage`
- `DATA.cpuVid`
- `bucketMs`
- `bucketMs=1000` 的 FPS 显示 bucket 语义
- raw PresentMon 统计仍是 source of truth
- CSV header 和 manifest 字段名未改

Vcore / VID 隔离仍保持：

- `DATA.cpuVoltage` 仍代表真实整体 CPU Voltage / Vcore。
- `DATA.cpuVid` 仍代表每核心请求/目标 VID。
- VID 不会伪装成 Vcore。

## 测试更新

已更新测试锁定新行为：

- `tests/FrameScopeNativeWatcherPolicyTests.cs`
  - 覆盖 watcher-worker / monitor-session-worker 标签
  - 覆盖任务管理器子进程说明
  - 覆盖 cleanup policy 包含 `--watcher` 和 `--monitor-session`
- `src/frontend/src/uiInteractionContract.test.ts`
  - 覆盖 Overview 页 worker 说明和 monitor worker 状态文案
- `tests/chart-sampling-tests.js`
  - 覆盖中文图表文案
  - 覆盖旧英文可见文案不会回退
  - 覆盖 `DATA.cpuVoltage`、`DATA.cpuVid`、`bucketMs=Number(fps.bucketMs)||1000`
- `tests/FrameScopeReportManifestTests.cs`
  - 覆盖 VID 可见曲线名中文化，同时保持稳定 `cpu-vid:<core>` key

## 验证结果

已运行并通过：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
  - typecheck PASS
  - Vitest：6 files / 64 tests PASS
  - Vite build PASS
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` PASS
- `.\tests\FrameScopeReportManifestTests.exe` PASS
- `.\tests\FrameScopeDiagnosticsTests.exe` PASS
- `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` PASS
- `.\tests\FrameScopeNativeWatcherPolicyTests.exe` PASS
- `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` PASS
- `.\tests\FrameScopeProcessCleanupTests.exe` PASS
- bundled Node：`tests\chart-sampling-tests.js` PASS
- 报告 layout probe PASS
  - 23 个场景
  - `allNoOverflow=true`
  - 覆盖 1280x720 和 900x760
  - `CHART_SCREENSHOTS_NONBLANK screenshotCount=23 nonblankFailures=0`
- `git diff --check` PASS
- GameLite/lightweight 边界检查：`NO_GAMELITE_OR_LIGHTWEIGHT_FILES_TOUCHED`
- 残留进程检查：`NO_MATCHING_RESIDUAL_PROCESSES`

最新 layout probe JSON：

`C:\Users\MISAKA~1\AppData\Local\Temp\framescope-report-layout-current-0d6510b319e040eaae42772c9326dc1a\layout-probe\report-overflow-probe.json`

## 未做事项

- 未安装 FrameScope。
- 未运行 setup 或 full setup。
- 未启动真实游戏。
- 未测试 BF6。
- 未推 GitHub。
- 未更新 GitHub Release。
