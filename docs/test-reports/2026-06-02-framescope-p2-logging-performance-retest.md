# FrameScope P2 记录日志性能优化专项复测报告

日期：2026-06-02
工作区：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
类型：专项复测，只验证，不修源码
结论：PASS

## 1. 复测结论

P2 记录日志性能优化可以复现。默认 idle 日志保持低频和必要启动信息；verbose+perf idle 日志从实现报告里的 baseline 级别 `13 lines / 1599 bytes / duplicate 10 / 1.878 lines/s` 复测为 `3 lines / 530 bytes / duplicate 0 / 0.399 lines/s`。本轮 bytes 比实现报告 after 的 486 bytes 略高，原因是复测 evidence 目录路径更长；line count、duplicate count 和限频效果一致。

本轮没有修改源码，没有修 bug，没有处理前端、动画、大列表、报告生成、图表交互、后端采样、data root 递归扫描等其他优化板块；没有打包、没有安装 FrameScope、没有启动真实游戏、没有测试 BF6、没有推 GitHub、没有更新 Release。

## 2. 范围和 git 状态

已读取实施报告：`docs\implementation-reports\2026-06-01-framescope-p2-logging-performance-optimization-report.md`。

实施报告声明本轮日志优化涉及：

- `src\core\FrameScopeLoggingPolicy.cs`
- `tests\FrameScopeLoggingPolicyTests.cs`
- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession.cs`
- `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs`
- `src\diagnostics\FrameScopeDiagnostics.cs`

当前完整工作树不是“只有日志性能文件改动”：`git status --short` 显示大量既有 modified / deleted / untracked 文件，覆盖前端、reporting、monitoring、docs、工具等历史改动。本轮复测没有回滚、没有改这些源码，只新增复测 evidence 和本报告。

针对日志优化窗口的 tracked diff 复核结果：

```text
src/app/FrameScopeNativeMonitor.MonitorSession.cs  | 233 +++++++++++++++++++--
src/app/FrameScopeNativeMonitor.ReportOrchestration.cs | 34 ++-
src/app/FrameScopeNativeMonitor.Watcher.cs | 140 +++++++++++--
src/diagnostics/FrameScopeDiagnostics.cs | 8 +
4 files changed, 375 insertions(+), 40 deletions(-)
```

`src\core\FrameScopeLoggingPolicy.cs` 和 `tests\FrameScopeLoggingPolicyTests.cs` 当前以 untracked 文件出现在 `git status --short` 中，因此不出现在普通 tracked `git diff --stat` 里。

## 3. Idle 日志复测数据

Evidence：`docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\idle-logs\retest-log-metrics.json`

| Run | Mode | Log lines | Bytes | Duplicate count | Elapsed sec | Lines/s | 关键日志 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| `default-idle-01` | default | 1 | 302 | 0 | 7.529 | 0.133 | `native-watcher-start` |
| `default-idle-02` | default | 1 | 302 | 0 | 7.521 | 0.133 | `native-watcher-start` |
| `verbose-perf-idle-01` | verbose+perf | 3 | 530 | 0 | 7.517 | 0.399 | `native-watcher-start`, `target-scan`, `watcher-poll-ms` |
| `verbose-perf-idle-02` | verbose+perf | 3 | 530 | 0 | 7.515 | 0.399 | `native-watcher-start`, `target-scan`, `watcher-poll-ms` |

结论：

- default idle 两次都是 1 行、0 duplicate，保留 `native-watcher-start config=...`，没有丢关键启动/状态日志。
- verbose+perf 两次都是 3 行、0 duplicate，重复的 `target-scan` / `watcher-poll-ms` 没有按 poll 频率持续刷屏。
- 相比实现报告 baseline，verbose+perf 日志行数从 13 降到 3，duplicate 从 10 降到 0，lines/s 从约 1.878 降到 0.399。优化收益复现。

## 4. Rate limiter 复测

Evidence：

- `docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\rate-limiter-smoke\rate-limiter-smoke-result.json`
- `docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\idle-logs\retest-log-metrics.json`
- `docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\command-logs\06-FrameScopeLoggingPolicyTests.log`

直测结果：

- 同 key 首次写入：PASS。
- 同 key 同 state 在限频窗口内被 suppress：PASS。
- 同 key state 变化立即写入：PASS。
- 不同 key 不互相压制：PASS。
- 同 state 超过 interval 后 heartbeat 写入：PASS。
- `target-scan:*` 使用 verbose rate limiter：PASS。
- `watcher-poll` 使用 perf rate limiter：PASS。
- 关键 error/fail 路径仍走直接 `WriteFrameScopeLog(...)`：PASS。

功能观察：

- idle verbose+perf 复测中 `target-scan` 和 `watcher-poll-ms` 每次只出现 1 行。
- `FrameScopeLoggingPolicyTests.exe` PASS。
- WebView2 smoke 的 diagnostics / monitor start / stop 正常完成，未见 warn/error 被吞的证据。

## 5. Diagnostics tail trim 复测

Evidence：`docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\diagnostics-tail-trim\diagnostics-tail-trim-smoke-result.json`

| 字段 | 值 |
| --- | ---: |
| `trimmed` | true |
| `beforeBytes` | 5,449,602 |
| `afterBytes` | 4,193,914 |
| `notEmpty` | true |
| `keptTailMarker` | true |
| `tailMarkerCount` | 65 |
| `removedOldHeadMarker` | true |
| `keptOldTailMarker` | true |
| `normalAppendOk` | true |

结论：

- 超过阈值后保留 tail，旧 head 被移除。
- 文件没有被清空。
- 普通 append 仍正常。
- WebView2 `state.snapshot` 和 diagnostics.generate smoke 继续返回必要 diagnostics/status 字段，未发现 tail trim 破坏 JSON/status 字段。

## 6. Settings / Bridge / WebView2 诊断能力

Evidence：

- `docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\webview2-smoke-summary.json`
- `artifacts\p2logrt0602\wvroot\live\live.json`
- `artifacts\p2logrt0602\wvroot\reduced\reduced.json`
- `artifacts\p2logrt0602\logs\root-live.log`
- `artifacts\p2logrt0602\logs\root-reduced.log`
- `docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\command-logs\07-FrameScopeWebBridgeTests.log`

WebView2 final smoke 结果：

| Smoke | success | pageLoaded | pageReady | reducedMotion | console errors | host errors | bridgeExtensionSmoke |
| --- | --- | --- | --- | --- | ---: | ---: | --- |
| live | true | true | true | false | 0 | 0 | true |
| reduced | true | true | true | true | 0 | 0 | true |

Bridge / logs / diagnostics / monitor 证据：

- `logs.openDirectory` 带前端路径 `C:\Windows` 被拒绝：`path_not_allowed`。
- `logs.openDirectory` 空 payload 使用 host 解析目录并成功：`directory_opened`。
- `reports.open` 前端路径注入被拒绝：`path_not_allowed`。
- `targets.save` 前端路径注入被拒绝：`path_not_allowed`。
- `diagnostics.generate` accepted，并收到 completed event。
- `monitor.start` accepted，并收到 `monitor.started`。
- `monitor.stop` accepted，并收到 `monitor.stopped`，`remainingProcessCount=0`。
- `FrameScopeWebBridgeTests.exe` PASS，覆盖 Settings/log-directory 等价验证：前端路径拒绝、host-resolved 日志目录打开、缺失日志目录创建。

`state.snapshot` 必要字段仍存在：

- `bridgeStatus`
- `bridgeVersion`
- `generatedAt`
- `root`
- `watcher.running`
- `watcher.pid`
- `watcher.statePath`
- `watcher.completedRuns`
- `watcher.lastReport`
- `watcher.lastError`
- `config.exists`
- `config.path`
- `config.enabledTargetCount`
- `config.targetCount`
- `config.dataRoot`
- `host.windowVisible`
- `host.trayAvailable`
- `host.closeWindowBehavior`
- `reports.historyPath`
- `reports.historyExists`

说明：

- WebView2 smoke 前两次属于 harness 设置问题：一次 evidence 路径过深触发 Windows 路径长度限制；一次 isolated empty history/config 不满足现有 smoke 对可操作 report 的依赖而 timeout。随后改用短 evidence 路径和默认 root config/history 后，live 与 reduced-motion 均 PASS。该过程没有修改源码。
- WebView2 smoke 只执行 watcher start/stop 诊断能力验证，没有启动真实游戏，没有测试 BF6。

## 7. Report generation / FPS / CPU Voltage / VID 回归

`report-generation-ms` 本轮只验证 lazy formatting 不改变行为。复测依据：

- `FrameScopeReportManifestTests.exe`: PASS。
- WebView2 live/reduced smoke 中 `reports.regenerate` accepted、in-flight、completed，`exitCode=0`。
- 本轮没有改 report HTML/layout，因此没有运行 `Probe-ReportHtmlLayout.js`。跳过原因：专项复测范围不包含 report HTML/layout 改动，用户允许不运行但需说明。

其他成果回归：

- FPS raw 统计语义保持：`FrameScopeReportManifestTests.exe` 覆盖 `raw frame count preserved`、average/low/max 使用 raw frames。
- `bucketMs=1000` 保持：`FrameScopeReportManifestTests.exe` 覆盖 `fps chart should use one-second display buckets`。
- CPU Voltage / Vcore 独立：`FrameScopeSystemSamplerCpuCoreTests.exe` 和 `FrameScopeReportManifestTests.exe` PASS，覆盖 Vcore availability、非 Vcore 拒绝、VID 不进入 CPU Voltage。
- CPU Core VID 独立：`FrameScopeSystemSamplerCpuCoreTests.exe` 和 `chart-sampling-tests.js` PASS，覆盖 `DATA.cpuVid`、`cpuVidMetric`、VID request/target note。
- VID/Vcore 双向隔离：`FrameScopeReportManifestTests.exe` PASS，覆盖 VID-only 不生成 CPU Voltage / Vcore、Vcore/SOC/package 不进入 CPU VID。

## 8. 必跑命令结果

| 验证项 | 结果 | 证据 |
| --- | --- | --- |
| `git status --short` | 已运行；当前完整工作树存在大量既有 dirty/untracked，不只日志文件 | `command-logs\00-git-status-short.log` |
| default idle 日志复测 2 次 | PASS，1 line / 302 bytes / duplicate 0 / 0.133 lines/s，两次一致 | `idle-logs\retest-log-metrics.json` |
| verbose+perf idle 日志复测 2 次 | PASS，3 lines / 530 bytes / duplicate 0 / 0.399 lines/s，两次一致 | `idle-logs\retest-log-metrics.json` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS；verify 脚本正常 `added 110 packages`，typecheck PASS，Vitest 6 files / 62 tests PASS，Vite build PASS | `command-logs\01-run-frontend-verify.log` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS，`FrameScope tests rebuilt.` | `command-logs\02-build-framescope-tests.log` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | `command-logs\03-FrameScopeReportManifestTests.log` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS | `command-logs\04-FrameScopeDiagnosticsTests.log` |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS | `command-logs\05-FrameScopeSystemSamplerCpuCoreTests.log` |
| `.\tests\FrameScopeLoggingPolicyTests.exe` | PASS | `command-logs\06-FrameScopeLoggingPolicyTests.log` |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS | `command-logs\07-FrameScopeWebBridgeTests.log` |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS，`chart-sampling-tests: PASS` | `command-logs\08-chart-sampling-tests-bundled-node.log` |
| WebView2 live smoke | PASS，`success=true`, `pageReady=true`, path injection rejected, logs directory OK, diagnostics/start/stop OK | `artifacts\p2logrt0602\wvroot\live\live.json` |
| WebView2 reduced-motion smoke | PASS，`success=true`, `pageReady=true`, `reducedMotion=true`, path injection rejected, logs directory OK, diagnostics/start/stop OK | `artifacts\p2logrt0602\wvroot\reduced\reduced.json` |
| Settings/log-directory smoke 或等价验证 | PASS，WebBridge unit + WebView2 bridge smoke 均覆盖 | `command-logs\07-FrameScopeWebBridgeTests.log`, WebView2 JSON |
| `git diff --check` | PASS，exit 0；只出现当前工作树 LF/CRLF warning，没有 whitespace error | `command-logs\09-git-diff-check.log` |
| 残留进程检查 | PASS，`NO_MATCHING_RESIDUAL_PROCESSES` | `command-logs\10-residual-process-check.log` |

Bundled Node 路径：`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`

## 9. 15 个必答点

1. P2 日志优化是否可复现：是，PASS。
2. default idle 日志是否保持必要信息：是，两次均保留 `native-watcher-start`，没有丢关键启动日志。
3. verbose+perf idle 数据：两次均为 3 lines / 530 bytes / duplicate 0 / 0.399 lines/s。
4. rate limiter 是否正常：正常，同 key 限频、不同 key 不互相压制、target-scan/watcher-poll 限频、error 路径直写均通过。
5. diagnostics tail trim 是否正常：正常，超过阈值后保留 tail，不清空文件，不影响普通 append。
6. Settings 打开日志目录是否正常：正常，WebBridge 和 WebView2 smoke 均验证 host-resolved log directory 打开成功。
7. 前端路径注入是否仍被拒绝：是，`logs.openDirectory`, `reports.open`, `targets.save` 的前端路径注入均被拒绝。
8. WebView2 live/reduced smoke 是否证据完整：完整，live/reduced JSON、截图、host log 均存在，核心字段均 PASS。
9. diagnostics/status 是否未丢字段：未丢，`state.snapshot` 必要字段和 diagnostics completed event 均存在。
10. FPS / CPU Voltage / CPU Core VID 是否未受影响：未受影响，相关 C# tests 和 chart sampling test 全部 PASS。
11. 是否有源码修改：本轮复测没有修改源码；只新增 docs/test-reports 复测报告和必要 evidence。注意当前工作树本身有大量既有源码 dirty 状态。
12. 是否处理其他优化板块：没有。
13. 是否打包、安装、启动真实游戏、测试 BF6、推 GitHub、更新 Release：均没有。`Run-Frontend.ps1 verify` 的 package restore / dist rebuild 是验证脚本正常行为，不算产品安装或打包。
14. 所有验证命令结果：见第 8 节，必跑项均已运行并记录。
15. 最终结论：PASS。
