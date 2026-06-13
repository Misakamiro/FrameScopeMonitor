# FrameScope Monitor 普通 UI 单实例启动修复专项复测

复测日期：2026-06-13  
工作区：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`  
背景实现报告：`docs\implementation-reports\2026-06-13-framescope-single-instance-launch-guard-report.md`  
证据目录：`docs\test-reports\2026-06-13-framescope-single-instance-launch-guard-retest-evidence`

## 结论

**PARTIAL**

普通 UI 单实例真实行为通过复测：第一次 UI 可打开，第二次普通启动只出现中文重复启动提示，不出现第二个主窗口，关闭提示后第二个启动进程退出；测试启动的 UI 进程退出后，第三次普通启动可重新打开，说明 Mutex 随 UI 进程释放。

worker 绕过单实例锁的核心路径通过自动化与观察证据：`--watcher` 在 UI 持锁时可启动并清理；`--monitor-session` / `--MonitorProcessRole monitor-session-worker` 的单实例绕过由 `FrameScopeSingleInstanceLaunchGuardTests.exe` 覆盖，monitor-session worker 生命周期由 `FrameScopeNativeMonitorChildProcessTests.exe` 覆盖。保守点：我额外做的 direct `--monitor-session --MonitorProcessRole monitor-session-worker` 手工 harness 在 UI 运行期间确实创建了 monitor-session 进程，但 60 秒内未完整退出且未生成 run 目录，因此“UI 持锁 + direct monitor-session 完整生命周期”这条补强证据未闭环。最终残留进程检查为 `NO_MATCHING_RESIDUAL_PROCESSES`。

## A. 普通 UI 单实例真实行为

证据：`ui-single-instance-result.json`、`ui-single-instance-prompt-confirm.json`

| 检查项 | 结果 | 证据 |
| --- | --- | --- |
| 第一次普通 UI 启动 | PASS | `firstUiWindowShown=true`，窗口标题为 `FrameScope Monitor Web UI`，`firstMainWindowCount=1` |
| 第二次普通 UI 启动不出现第二主窗口 | PASS | `mainWindowCountDuringDuplicate=1` |
| 第二次普通 UI 启动出现中文提示 | PASS | 对话框文本包含 `FrameScope Monitor 已在运行，请勿重复打开。`；Node BOM 兼容复核 `exactPromptFound=true` |
| 第二个启动进程退出 | PASS | 关闭提示后 `secondExitedAfterPromptClose=true`，`secondExitCode=0` |
| 无常驻第二 UI 进程 | PASS | 提示框显示期间可见 transient 第二进程，关闭提示后退出；最终清理后 `finalProcessCountAfterCleanup=0` |
| 恢复/置前已有窗口 | 未可靠观察 | 该项只在可观察时确认；本轮 foreground 仍停在 Edge/其他窗口，没有把此项计为失败 |
| 关闭 UI 后再次普通启动 | PASS | 当前配置为关闭窗口最小化到托盘，因此测试用 `Stop-Process` 结束测试启动的 UI 进程；随后 `thirdUiOpenedAfterUiProcessExit=true`，`thirdMainWindowCount=1` |

## B. worker 不受普通 UI 单实例锁影响

证据：`ui-single-instance-result.json`、`monitor-session-test-while-ui-running-result.json`、`monitor-session-direct-while-ui-running-result.json`、`08-FrameScopeNativeMonitorChildProcessTests.txt`

| 检查项 | 结果 | 证据 |
| --- | --- | --- |
| UI 运行时 `--watcher` 可启动 | PASS | `watcherRunningWhileUiHeld=true`，`watcherProcessCountWhileUiHeld=1` |
| `--watcher` 停止后清理 | PASS | `watcherExitedAfterStop=true`，`workerResidualCountAfterStop=0` |
| `--monitor-session` / `--MonitorProcessRole` 未被单实例锁误判为普通 UI | PASS for guard | `FrameScopeSingleInstanceLaunchGuardTests.exe` 通过；测试覆盖 `--monitor-session`、`--MonitorProcessRole` 绕过 UI 单实例锁 |
| monitor-session worker 生命周期 | PASS for automated lifecycle | `FrameScopeNativeMonitorChildProcessTests.exe` 通过，覆盖 fake target、monitor-session worker、采样器与输出文件路径；未启动真实游戏 |
| UI 运行期间执行 monitor-session 相关测试 | PARTIAL | `FrameScopeNativeMonitorChildProcessTests.exe` 在普通 UI 进程运行后启动并通过；但该长测试结束时 UI 已不再存活，不能证明 UI 锁全程持有 |
| direct `--monitor-session --MonitorProcessRole monitor-session-worker` | PARTIAL | UI 运行时 monitor-session 进程已创建，未被重复启动提示拦截；但 60 秒内未退出，未生成 run 目录，作为补强证据未闭环 |
| 最终残留进程 | PASS | 最终检查输出 `NO_MATCHING_RESIDUAL_PROCESSES` |

## C. 回归检查

| 回归项 | 结果 | 证据 |
| --- | --- | --- |
| worker 说明仍存在 | PASS | `FrameScopeWebBridge.State.cs`、`FrameScopeNativeMonitor.WebHost.cs`、`OverviewPage.tsx`、`mockPreview.ts` 均保留“FrameScopeMonitor.exe 子进程是监控 worker，不是重复打开软件”的说明 |
| 图表中文化未回退 | PASS | `chart-sampling-tests.js` 通过；layout probe 覆盖 FPS、CPU 核心频率、CPU 电压 / Vcore、CPU 核心 VID、性能频率、系统占用、后台进程、IO / 温度 |
| `DATA.cpuVoltage` 未改名 | PASS | `chart-sampling-tests.js` 断言 `DATA.cpuVoltage`；测试通过 |
| `DATA.cpuVid` 未改名 | PASS | `chart-sampling-tests.js` 断言 `DATA.cpuVid`；测试通过 |
| `bucketMs=1000` 保持 | PASS | `chart-sampling-tests.js` 断言 `bucketMs=Number(fps.bucketMs)||1000`；layout tooltip 也显示 `bucketMs=1000 ms` |
| FPS raw PresentMon 统计语义保持 | PASS | `FrameScopeReportManifestTests.exe` 通过，覆盖 raw PresentMon row count；报告脚本说明仍保留 raw PresentMon 作为源数据 |
| Vcore / VID 双向隔离保持 | PASS | `FrameScopeReportManifestTests.exe`、`FrameScopeSystemSamplerCpuCoreTests.exe`、`chart-sampling-tests.js` 均通过；VID-only 不进入 CPU Voltage / Vcore，Vcore/SOC/package 不生成 VID |

## 必跑命令结果

| # | 命令 | 结果 |
| --- | --- | --- |
| 1 | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS：typecheck PASS，Vitest 6 files / 64 tests PASS，Vite build PASS |
| 2 | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS：`FrameScope tests rebuilt.` |
| 3 | `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe` | PASS |
| 4 | `.\tests\FrameScopeReportManifestTests.exe` | PASS |
| 5 | `.\tests\FrameScopeDiagnosticsTests.exe` | PASS |
| 6 | `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS |
| 7 | `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS |
| 8 | `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS |
| 9 | `.\tests\FrameScopeProcessCleanupTests.exe` | PASS |
| 10 | bundled Node 运行 `.\tests\chart-sampling-tests.js` | PASS |
| 11 | layout probe | PASS：`allNoOverflow=true`，23 个场景 overflow 为 0，23 张截图像素抽样均非空白 |
| 12 | `git diff --check` | PASS：退出码 0；仅有 LF/CRLF 工作区换行警告 |
| 13 | 残留进程检查 | PASS：`NO_MATCHING_RESIDUAL_PROCESSES` |

## 未做事项

- 未改源码。
- 未修 bug。
- 未打包。
- 未安装。
- 未启动真实游戏。
- 未测试 BF6。
- 未推 GitHub。
- 未更新 Release。

## 注意事项

- `FrameScopeReportManifestTests.exe` 输出中出现 BF6、Valorant 等名称属于合成测试数据，不是真实游戏启动。
- 当前配置下关闭窗口是最小化到托盘；本轮 Mutex 释放检查使用的是结束测试启动的 UI 进程后再启动，而不是把窗口 X 关闭当作进程退出。
- direct monitor-session 补强 harness 超时后已清理进程；最终残留检查确认没有 FrameScope/PresentMon 相关残留。
