# FrameScope Monitor single-instance retest PARTIAL 澄清复核

复核日期：2026-06-13

工作区：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

背景报告：

- `docs\implementation-reports\2026-06-13-framescope-single-instance-launch-guard-report.md`
- `docs\test-reports\2026-06-13-framescope-single-instance-launch-guard-retest.md`

结论：**PASS**

## 复核范围

本次只复核专项复测中标为 PARTIAL 的 direct `--monitor-session --MonitorProcessRole monitor-session-worker` 补强 harness。未修改源码，未修 bug，未打包，未安装，未启动真实游戏，未测试 BF6，未推 GitHub，未更新 Release。

## 既有 PARTIAL 证据

复测报告中的 PARTIAL 来自额外 direct harness：

- 证据文件：`docs\test-reports\2026-06-13-framescope-single-instance-launch-guard-retest-evidence\monitor-session-direct-while-ui-running-result.json`
- UI 运行中：`uiRunningBeforeMonitor=true`、`uiRunningDuringMonitorStartup=true`、`uiRunningAfterMonitorExit=true`
- monitor-session 进程被创建：`monitorPid=10716`
- 参数包含 `--monitor-session`、`--MonitorProcessRole monitor-session-worker`、`--RunRoot`、`--RunNamePrefix`、`--TargetProcessName`、`--InitialTargetPid`
- 结果：`monitorExited=false`、`monitorSignal=TIMEOUT`、`newestRunDir=""`、`statusExists=false`、`summaryExists=false`

该结果能证明当时进程未被普通 UI 单实例提示拦截，但不能证明 monitor-session 生命周期已经进入并卡在真实业务逻辑里。

## 源码对照

真实 watcher 构造 monitor-session 的位置：

- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `StartMonitorProcess(...)` 使用当前 `Application.ExecutablePath`，工作目录为 `Root`
- 参数包含：
  - `--monitor-session`
  - `--TargetProcessName`
  - `--TargetProcessAliases`
  - `--TargetDisplayName`
  - `--InitialTargetPid`
  - `--WaitSeconds 15`
  - `--CaptureSeconds 0`
  - telemetry interval 参数
  - `--MonitorProcessRole monitor-session-worker`
  - `--ControlPollIntervalMs 3000`
  - `--RunRoot`
  - `--RunNamePrefix`

monitor-session 入口：

- `src\app\FrameScopeNativeMonitor.cs` 在 UI 初始化和单实例锁之前先处理 `--monitor-session`，直接 `Environment.Exit(RunNativeMonitorSession(args))`
- `src\app\FrameScopeNativeMonitor.SingleInstance.cs` 中 `IsInteractiveUiLaunch(...)` 对 `--monitor-session` 和 `--MonitorProcessRole` 均返回 false，因此 worker 模式不会进入普通 UI 单实例锁

monitor-session 生命周期：

- `src\app\FrameScopeNativeMonitor.MonitorSession.cs`
- `RunNativeMonitorSession(...)` 在解析参数后会先规范 `RunRoot`，`Directory.CreateDirectory(runRoot)`
- 随后立即创建本次 `paths.RunDir`
- 在等待目标进程之前即写入 `status.json` 的 `created` / `waiting-for-target` 状态

因此，如果进程已经正确进入 `RunNativeMonitorSession(...)`，即使后续目标等待、PresentMon、sampler 或停止逻辑失败，也应至少看到 run 目录或错误状态。旧 direct harness 60 秒后仍无 run 目录，更符合 harness 启动/观测未可靠进入 monitor-session 生命周期，而不是产品 monitor-session 正常等待 watcher 信号或真实链路卡死。

## 测试对照

`tests\FrameScopeNativeMonitorChildProcessTests.cs` 使用 fake target 和 fake PresentMon 路径启动真实 `FrameScopeMonitor.exe --monitor-session`，并断言：

- monitor process 可退出
- run 目录生成
- `status.json`、`summary.json` 生成
- PresentMon stderr / silent no-csv / CPU telemetry 等场景可被记录和分类

`tests\FrameScopeProcessCleanupTests.cs` 覆盖后台进程清理。

`tests\FrameScopeSingleInstanceLaunchGuardTests.cs` 覆盖：

- 普通 UI 会被 guard
- `--watcher`、`--monitor-session`、`--MonitorProcessRole monitor-session-worker`、诊断入口均绕过 UI guard
- 重复 UI 锁释放正常
- 中文提示保持

## 本次最小验证

本次没有重新大范围复测，只运行和 PARTIAL 澄清直接相关的最小验证。

### 自动化测试

`.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe`

结果：PASS

关键输出：

- `[PASS] ordinary UI launches are guarded`
- `[PASS] worker and diagnostic launches bypass the UI guard`
- `[PASS] duplicate UI lock is rejected and releases cleanly`
- `[PASS] duplicate UI prompt stays Chinese`
- `FrameScopeSingleInstanceLaunchGuardTests: PASS`

`.\tests\FrameScopeNativeMonitorChildProcessTests.exe`

结果：PASS

关键输出：

- `FrameScopeNativeMonitorChildProcessTests: PASS`

`.\tests\FrameScopeProcessCleanupTests.exe`

结果：PASS

关键输出：

- `FrameScopeProcessCleanupTests: PASS`

### direct monitor-session 对照 smoke

不启动 UI，使用同类 fake target / fake PresentMon 参数直接启动 `FrameScopeMonitor.exe --monitor-session --MonitorProcessRole monitor-session-worker`。

结果：

```json
{"monitorExited":true,"monitorExitCode":0,"runDirCreated":true,"statusExists":true,"summaryExists":true,"finalPhase":"done","frameCaptureStatus":"presentmon-etw-access-denied"}
```

结论：direct 参数形态和 monitor-session 产品入口本身可以闭环。

### UI 持锁 direct monitor-session smoke

先启动普通 UI 进程持有单实例锁，再使用同类 fake target / fake PresentMon 参数启动 `FrameScopeMonitor.exe --monitor-session --MonitorProcessRole monitor-session-worker`。

结果：

```json
{"uiPid":18952,"uiRunningBeforeMonitor":true,"monitorPid":16940,"monitorCommandContainsMonitorSession":true,"monitorExited":true,"monitorExitCode":0,"runDirCreated":true,"statusExists":true,"summaryExists":true,"finalPhase":"done","frameCaptureStatus":"presentmon-etw-access-denied"}
```

结论：UI 持有普通单实例锁时，monitor-session worker 仍可启动、生成 run 目录、写入 status/summary 并退出。worker 模式未被单实例锁误挡。

## Root Cause 澄清

旧 direct monitor-session harness 未闭环的 root cause 是 **补强 harness limitation / 观测链路不可靠**，不是已确认的产品 bug。

具体依据：

- 正确进入 `RunNativeMonitorSession(...)` 后应早期创建 run 目录并写状态。
- 旧 direct harness 进程存在但 60 秒内无 run 目录、无 `status.json`、无 `summary.json`，不符合 monitor-session 已进入后的正常失败形态。
- 本次不带 UI 的 direct 对照 smoke 可以闭环。
- 本次 UI 持锁 direct smoke 也可以闭环。
- 自动化 guard、monitor child-process、cleanup 测试均通过。

旧 harness 的失败不能作为“真实 watcher 链路会卡住”的证据。它只说明那条手写补强观测没有闭环。

## 产品影响判断

- 普通 UI 单实例修复：维持 PASS。
- worker 模式绕过 UI 单实例锁：确认 PASS。
- `--watcher` / `--monitor-session` 真实链路清理：由专项测试和本次最小 smoke 确认 PASS。
- direct monitor-session 作为产品入口：本次同类 direct smoke 在 UI 未持锁和 UI 持锁两种情况下均可闭环。
- 旧 direct harness 未闭环：不代表产品 bug。

## 残留进程检查

最终残留进程检查输出：

```text
NO_MATCHING_RESIDUAL_PROCESSES
```

## git diff --check

`git diff --check` 退出码 0。输出仅有既有工作区 LF/CRLF 换行提示，无 whitespace error。

说明：目标工作区在本轮复核开始前已有背景实现/复测留下的源码 diff；本轮只新增本澄清报告，未修改源码，未回滚任何既有改动。

## 明确未做

- 未改源码。
- 未修 bug。
- 未打包。
- 未安装。
- 未启动真实游戏。
- 未测试 BF6。
- 未推 GitHub。
- 未更新 Release。
