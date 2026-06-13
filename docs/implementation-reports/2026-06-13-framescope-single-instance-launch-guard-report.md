# FrameScope Monitor 主程序单实例启动修复报告

日期：2026-06-13

状态：PASS

## 目标

修复普通用户重复点击桌面/开始菜单快捷方式时，会再次打开一个 FrameScope Monitor 主窗口并留下额外 UI 进程的问题。修复范围只限制普通 UI 启动，不能影响 `--watcher`、`--monitor-session`、`--MonitorProcessRole monitor-session-worker` 等内部 worker/诊断入口。

## Root Cause

入口在 `src/app/FrameScopeNativeMonitor.cs` 的 `Main(string[] args)`。

原始启动分流为：

1. `--monitor-session` 进入 `RunNativeMonitorSession(args)`。
2. `--generate-diagnostic-report` 进入诊断报告 CLI。
3. `--watcher` 进入 `RunNativeWatcher(args)`。
4. 其他参数继续初始化 WinForms/WebView2，并调用 `RunWebUi(args)`。

问题是第 4 步普通 UI 路径没有任何跨进程单实例保护。重复执行 `FrameScopeMonitor.exe` 时，每个进程都会继续初始化 WebView2 主窗口，所以会出现两个普通 UI 进程和两个主窗口。由于同一个 `FrameScopeMonitor.exe` 也承载 watcher / monitor-session worker，不能按进程名粗暴 kill 或阻止所有同名进程。

修复前复现证据：两次普通启动后可看到两个命令行均为普通 `FrameScopeMonitor.exe` 的 UI 进程，两个进程都带主窗口，`NORMAL_UI_PROCESS_COUNT=2`。

## 实现

新增 `src/app/FrameScopeNativeMonitor.SingleInstance.cs`：

- 使用命名 Mutex：`Local\FrameScopeMonitor.InteractiveUi.SingleInstance`。
- 只给普通交互式 UI 启动加锁。
- 第二次普通 UI 启动拿锁失败后，不进入 WebView2 初始化，不创建第二个主窗口。
- 第二次启动会尝试用已有窗口句柄执行低风险置前：
  - `IsIconic`
  - `ShowWindow(SW_RESTORE)`
  - `SetForegroundWindow`
- 然后弹出中文提示并以退出码 0 退出。

`src/app/FrameScopeNativeMonitor.cs` 的入口调整为：

1. 先处理 `--monitor-session`、`--generate-diagnostic-report`、`--watcher`。
2. 再判断 `IsInteractiveUiLaunch(args)`。
3. 仅普通 UI 路径调用 `TryAcquireInteractiveUiSingleInstanceLock(...)`。
4. `RunWebUi(args)` 返回或异常退出路径都会在 `finally` 中释放 Mutex。

提示文案：

> FrameScope Monitor 已在运行，请勿重复打开。

该文案在源码中用 Unicode 转义保存，避免 PowerShell/编译代码页导致中文乱码。

## 普通 UI 与 Worker 区分

`IsInteractiveUiLaunch(args)` 对以下参数直接返回 false，因此绕过 UI 单实例锁：

- `--watcher`
- `--monitor-session`
- `--MonitorProcessRole`
- `--generate-diagnostic-report`
- `--webview2-runtime-self-test`
- `--web-ui-smoke`
- `--web-ui-target-settings-evidence-smoke`
- `--web-ui-settings-persistence-read-smoke`
- `--web-ui-tray-smoke`

无参数启动、快捷方式启动、仅带普通配置参数的启动仍视为普通 UI 启动，需要参与单实例保护。

## 测试覆盖

新增 `tests/FrameScopeSingleInstanceLaunchGuardTests.cs`，并更新 `tests/Build-FrameScopeTests.ps1`：

- 普通 UI 启动会被识别为需要单实例保护。
- `--watcher`、`--monitor-session`、`--MonitorProcessRole monitor-session-worker`、诊断 CLI 和 Web UI smoke 参数不会被 UI 单实例锁挡住。
- 第一次 Mutex 获取成功，第二次获取失败且不会泄漏 Mutex handle。
- UI 退出释放锁后可再次获取。
- 重复启动提示文案保持中文。

更新 `build.ps1`，把 `FrameScopeNativeMonitor.SingleInstance.cs` 纳入 `FrameScopeMonitor.exe` 编译源文件列表。

## Smoke 结果

已用当前源码手动只重编 `FrameScopeMonitor.exe` 主程序本体；没有运行 setup/full setup，也没有生成或安装安装包。

普通 UI 重复启动 smoke：

- 第一次普通启动：主窗口 `FrameScope Monitor Web UI` 正常显示。
- UI 已运行时第二次普通启动：只出现提示窗口 `FrameScope Monitor`，没有第二个 `FrameScope Monitor Web UI` 主窗口。
- 第二次进程在提示窗口关闭后退出。
- 第二次启动期间主 UI 窗口数量保持 1。
- 关闭第一个主 UI 后，第三次普通启动能正常重新打开，说明 Mutex 已释放。
- smoke 结束后普通 UI 残留数为 0。

worker bypass smoke：

- UI 单实例锁已被主 UI 持有时，`--watcher` 仍能启动并保持运行。
- UI 单实例锁已被主 UI 持有时，`--monitor-session --MonitorProcessRole monitor-session-worker` 仍能启动；使用不存在的测试目标时按预期退出码 2 结束，证明不是被 Mutex 阻止。

worker 清理：

- 停止/清理后 watcher worker 数量回到 0。
- 最终残留进程检查输出 `NO_MATCHING_RESIDUAL_PROCESSES`。

## 验证结果

已运行并通过：

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
  - typecheck PASS
  - Vitest：6 files / 64 tests PASS
  - Vite build PASS
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
- `.\tests\FrameScopeReportManifestTests.exe`
- `.\tests\FrameScopeDiagnosticsTests.exe`
- `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`
- `.\tests\FrameScopeNativeWatcherPolicyTests.exe`
- `.\tests\FrameScopeNativeMonitorChildProcessTests.exe`
- `.\tests\FrameScopeProcessCleanupTests.exe`
- `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe`
- bundled Node：`.\tests\chart-sampling-tests.js`
- layout probe：
  - `allNoOverflow=true`
  - 场景数 23
  - 溢出数 0
  - 报告 HTML 保留中文图表文案，例如“CPU 电压 / Vcore”“CPU 核心 VID（请求电压）”“后台进程”“系统占用”
- `git diff --check`
- 最终残留进程检查：`NO_MATCHING_RESIDUAL_PROCESSES`

说明：报告 manifest 测试输出中出现的 BF6 等名称来自既有合成测试数据，不是启动真实游戏，也不是 BF6 测试。

## 影响确认

- 普通 UI 单实例：已限制。
- 第二次普通启动：有中文提示，不创建第二个主 UI 窗口，不留下第二个常驻 UI 进程。
- 已有窗口激活：已实现低风险尝试，不引入跨进程 IPC。
- watcher：未受影响。
- monitor-session：未受影响。
- `--MonitorProcessRole monitor-session-worker`：未受影响。
- 停止监控/关闭 UI 后：无目标残留进程。
- 之前 worker 说明和图表中文化：未回退；chart sampling 和 layout probe 已验证。
- GameLite/lightweight 文件：未触碰。

## 明确未做

- 未安装 FrameScope。
- 未运行 setup/full setup。
- 未启动真实游戏。
- 未测试 BF6。
- 未推 GitHub。
- 未更新 GitHub Release。
