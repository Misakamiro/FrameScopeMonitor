# FrameScope 报告 raw chart data / FPS 下拉复测报告

结论：PASS

复测时间：2026-05-29
复测范围：只测试，不改源码；未测试 BF6，未启动真实游戏，未安装，未推 GitHub/Release。

## 1. 复测结论

本轮复测确认：

- 报告 `DATA.fps` 不再输出 `bucketMs` / `lowWindowMs`，`t/avg/low1/low01/min` 均按原始 PresentMon 帧行输出。
- FPS 下拉只有 4 项：
  - 平均 FPS / 1% Low / 0.1% Low
  - 只看平均 FPS
  - 只看 1% Low
  - 只看 0.1% Low
- 下拉未出现“只看最低瞬时 FPS”，也未出现 `value=min`。
- 红色异常帧点不作为单独下拉选项，而是在 4 个 FPS 视图中默认叠加显示。
- tooltip 从当前原始时间点读取并显示曲线值；FPS tooltip 额外显示红色异常帧点值。
- average FPS / 1% Low / 0.1% Low 与原始 PresentMon 帧数据重新计算结果一致。
- CPU 核心频率、CPU Core VID、CPU 电压、system、process 图表均使用原始采样点。
- 20000 帧 synthetic 大样本报告可打开、非空、未崩溃，交互截图和像素检查通过。

建议：可以进入本机安装更新验证；本轮不建议直接进入 GitHub/Release 发布，因为本轮边界是源码树复测，不包含安装目录同步、安装后 hash/parity、卸载/安装器验证。

## 2. 命令验证

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| `tools\Run-Frontend.ps1 verify` | PASS | `artifacts\report-raw-data-retest-20260529\run-frontend-verify.log`，55 个 Vitest 用例通过，typecheck/build 通过 |
| `build.ps1` | PASS | `artifacts\report-raw-data-retest-20260529\build.log`，normal/full setup 构建完成；未运行安装器 |
| `tests\Build-FrameScopeTests.ps1` | PASS | `artifacts\report-raw-data-retest-20260529\build-tests.log` |
| C# tests | PASS | `artifacts\report-raw-data-retest-20260529\csharp-tests.log`，15 个相关测试 exe exit 0 |
| `tests\chart-sampling-tests.js` | PASS | `artifacts\report-raw-data-retest-20260529\chart-sampling-tests.log` |
| synthetic raw data 检查 | PASS | `artifacts\report-raw-data-retest-20260529\synthetic-raw-data-check.json` |
| report 交互截图 / 像素检查 | PASS | `artifacts\report-raw-data-retest-20260529\playwright-report-screenshots.json` |
| WebView2 live smoke | PASS | `artifacts\report-raw-data-retest-20260529\webview2-live-smoke.json`，`success=true`、`pageLoaded=true`、`pageReady=true` |
| WebView2 reduced-motion smoke | PASS | `artifacts\report-raw-data-retest-20260529\webview2-reduced-motion-smoke.json`，`success=true`、`pageLoaded=true`、`pageReady=true`、`reducedMotion=true` |
| `git diff --check` | PASS | `artifacts\report-raw-data-retest-20260529\git-diff-check-final.log`，无 whitespace error；仅有既有 LF/CRLF 提示 |
| 残留进程检查 | PASS | `artifacts\report-raw-data-retest-20260529\residual-process-check-final.log`，`NO_MATCHING_RESIDUAL_PROCESSES` |

已运行的 C# 测试：

- `FrameScopeReportManifestTests.exe`
- `FrameScopeSystemSamplerCpuCoreTests.exe`
- `FrameScopeConfigStoreTests.exe`
- `FrameScopeWebBridgeTests.exe`
- `FrameScopeNativeMonitorChildProcessTests.exe`
- `FrameScopeProcessSamplerTests.exe`
- `FrameScopeDiagnosticsTests.exe`
- `FrameScopeLoggingPolicyTests.exe`
- `FrameScopePresentMonDiagnosticsTests.exe`
- `FrameScopeProcessCleanupTests.exe`
- `FrameScopeNativeWatcherPolicyTests.exe`
- `FrameScopeWebHostLifecycleTests.exe`
- `FrameScopeWebView2RuntimeTests.exe`
- `FrameScopeReportProgressTests.exe`
- `FrameScopeCapturePlannerTests.exe`

## 3. raw data 检查结果

240 帧 synthetic 报告：

- `DATA.fps.t/avg/low1/low01/min`：均为 240 点。
- `DATA.fps.bucketMs` / `DATA.fps.lowWindowMs`：不存在。
- 原始 spike 点：`t=0.5s`，`min=25 FPS`。
- `frameStats.average=109.97`、`low1=16.82`、`low01=12.50`，与原始帧数组重算一致。
- `DATA.system.t=6`、`DATA.process.t=6`、`DATA.cpuCore.t=6`、`DATA.cpuVoltage.t=6`、`DATA.cpuVid.t=6`。
- `DATA.cpuCore.displayBucketMs` / `DATA.cpuVoltage.displayBucketMs` / `DATA.cpuVid.displayBucketMs`：不存在。

20000 帧 synthetic 大样本报告：

- `DATA.fps.t/avg/low1/low01/min`：均为 20000 点。
- `framescope-interactive-data.js`：567492 bytes。
- `frameStats.average=118.30`、`low1=75.73`、`low01=51.17`，与原始帧数组重算一致。
- Headless Edge 打开并截图成功，页面非空，`modeStats` 显示“绘制 2,983 点 / 原始源 60,000 点”，说明画布层仍可抽样绘制，但数据源保持原始序列。

## 4. FPS 下拉和红点截图

截图目录：`artifacts\report-raw-data-retest-20260529\screenshots`

- FPS 下拉：`fps-dropdown.png`
- 平均 FPS / 1% Low / 0.1% Low：`fps-all.png`
- 只看平均 FPS：`fps-avg.png`
- 只看 1% Low：`fps-low1.png`
- 只看 0.1% Low：`fps-low01.png`
- CPU Core VID：`cpu-vid.png`
- 大样本 FPS：`large-report-fps.png`

红色异常点像素检查：

| FPS 视图 | canvas 红色像素 | 截图红色像素 | 结论 |
| --- | ---: | ---: | --- |
| all | 334 | 739 | PASS |
| avg | 334 | 739 | PASS |
| low1 | 357 | 762 | PASS |
| low01 | 357 | 762 | PASS |

tooltip 抽查：

- tooltip 显示原始时间 `0:00`。
- tooltip 显示平均 FPS、1% Low、0.1% Low 和红色异常帧点的当前 raw point 值。

## 5. WebView2 smoke 边界

WebView2 live/reduced-motion smoke 使用源码树 `FrameScopeMonitor.exe` 和 `src\frontend\dist`。smoke harness 内部会做 bridge action、diagnostics.generate、monitor.start/stop 的可用性检查，但本轮没有启动真实游戏、没有运行安装器、没有推送 GitHub/Release。

## 6. 最终清理检查

报告写入后已执行最终清理检查：

- `git diff --check`：exit 0；无 whitespace error；仅输出既有 LF/CRLF 提示。
- 残留进程检查：未发现 `FrameScopeMonitor.exe`、`FrameScopeProcessSampler.exe`、`FrameScopeSystemSampler.exe`、`FrameScopeReportGenerator.exe`、`PresentMon`、`FrameScopeRenderProbe` 或本轮 artifact 相关残留进程。
