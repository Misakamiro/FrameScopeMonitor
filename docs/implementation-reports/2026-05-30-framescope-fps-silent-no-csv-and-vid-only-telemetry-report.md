# FrameScope FPS silent no-csv 与 VID-only telemetry 修复报告

Date: 2026-05-30

Status: PASS（源码实现、构建、测试、WebView2 smoke 均完成；未启动真实游戏、未运行安装器、未推送 GitHub、未更新 Release）

Source:

```text
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d
```

Installed Valorant run used for diagnosis:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260530-123151
```

## 实现摘要

本轮把 `PresentMon exit 0 + stdout Started recording + stderr empty + no presentmon.csv` 从泛化的 `missing-presentmon-csv` 细分为 `presentmon-no-csv-silent`。monitor-session、summary/status、report manifest/data/html 现在都会保留更完整的诊断字段：PresentMon args、stdout/stderr tail、CSV path、CSV last check time、运行时长、target pid、resolved process、target 退出前状态检查等。

同时移除了新 run 的真实 per-core CPU Vcore 主动采集入口。新监听/monitor-session 只启用 CPU Core VID，不再主动传入或生成 `cpu-voltage-samples.csv` / `cpu-voltage-telemetry-status.json`。旧配置字段继续可加载，旧 run 里的 `cpu-voltage` 文件仍可被报告生成器兼容读取，但 UI 不再提供真实 Vcore/CPU Voltage 图表入口或 Settings 配置入口。

## 11 项结论

1. Valorant FPS 不显示的直接原因仍是 PresentMon 没有创建 `presentmon.csv`。在安装版 Valorant run 副本上重生报告后，`frames=0`、`reportKind=diagnostic`。
2. 已新增 silent no-csv 分类：`FrameCaptureStatus=presentmon-no-csv-silent`，`PresentMonFailureCategory=presentmon-no-csv-silent`。
3. 已修复 target-specific 诊断文案。Valorant 报告副本检查 `MessageHasPUBG=False`，不会再出现 PUBG 场景文案。
4. 不能在不启动真实 Valorant 的情况下证明 FPS 完全恢复。本轮只能证明 silent no-csv 分类、诊断字段、报告生成和无帧 diagnostic 路径正确。
5. 需要后续安装版真实 Valorant run 验证。该验证需要用户单独授权启动真实 Valorant，并且需要先授权安装更新或安装目录同步。
6. 已移除真实 per-core Vcore 的用户功能入口。新 watcher 和 monitor-session 不再主动启用 CPU Vcore telemetry。
7. 新 run 不再生成 `cpu-voltage-samples.csv` / `cpu-voltage-telemetry-status.json`，由 `FrameScopeNativeMonitorChildProcessTests.exe` 的 synthetic monitor-session 覆盖。
8. CPU Core VID 仍保留并强化：`cpu-vid-samples.csv`、`cpu-vid-telemetry-status.json`、manifest `cpuVid`、`DATA.cpuVid`、CPU VID 图表均保留。
9. UI 文案明确 VID 是 CPU 请求/目标电压，不是真实主板实测 Vcore。
10. 验证命令结果见下方。
11. 建议进入本机安装更新验证，但本轮没有运行安装器。下一步应先更新安装版，再跑真实 Valorant run 验证 FPS 是否恢复。

## 验证结果

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
PASS: typecheck, 57/57 frontend tests, Vite build

powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
PASS: 0 warnings, 0 errors; setup/full setup generated

powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
PASS: FrameScope tests rebuilt

.\tests\FrameScopeConfigStoreTests.exe
PASS

.\tests\FrameScopePresentMonDiagnosticsTests.exe
PASS

.\tests\FrameScopeNativeMonitorChildProcessTests.exe
PASS

.\tests\FrameScopeSystemSamplerCpuCoreTests.exe
PASS

.\tests\FrameScopeReportManifestTests.exe
PASS

.\tests\FrameScopeWebBridgeTests.exe
PASS

.\tests\FrameScopeDiagnosticsTests.exe
PASS

.\tests\FrameScopeNativeWatcherPolicyTests.exe
PASS

node .\tests\chart-sampling-tests.js
Default PATH result: FAIL, WindowsApps/Codex packaged node.exe returned Access is denied.
Bundled Node PATH override result: PASS, chart-sampling-tests: PASS

WebView2 live smoke
PASS: artifacts\webview2-bridge\2026-05-30-live-smoke.json success=true

WebView2 reduced-motion smoke
PASS: artifacts\webview2-bridge\2026-05-30-reduced-motion-smoke.json success=true, reducedMotion=true

git diff --check
PASS: exit code 0; only existing LF/CRLF conversion warnings were printed.

Residual process check
PASS for source-tree residuals: NO_SOURCE_TREE_RESIDUAL_PROCESSES.
Note: one pre-existing installed FrameScopeMonitor.exe was running from C:\Users\misakamiro\AppData\Local\FrameScopeMonitor, start time 2026-05-30 12:30:19. It was not started by this source-tree validation and was not killed.
```

Additional Valorant copy verification:

```text
FrameScopeReportGenerator.exe <temp copy of Valorant-20260530-123151>
PASS
FrameCaptureStatus=presentmon-no-csv-silent
PresentMonFailureCategory=presentmon-no-csv-silent
PresentMonEtwAccessDenied=False
Frames=0
ReportKind=diagnostic
MessageHasPUBG=False
HtmlHasCpuVoltageTab=False
HtmlHasCpuVidTab=True
DataHasCpuVid=True
DataHasVidNote=True
```

## 边界说明

本轮没有测试 BF6，没有启动真实 Valorant 或其他真实游戏，没有运行安装器，没有推送 GitHub，也没有更新 Release。FPS 没有被伪造，电压没有被伪造，PresentMon 原始帧数据、FPS average / 1% Low / 0.1% Low 的 raw-frame 计算和图表 raw data 语义未降级。
