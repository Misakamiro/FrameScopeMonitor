# FrameScope Monitor worker 说明与图表中文化专项复测报告

日期：2026-06-13

结论：PASS

本轮只做复测和记录。未改源码，未修 bug，未打包，未安装，未启动真实游戏，未测试 BF6，未推 GitHub，未更新 Release。

工作区：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

复测输入报告：

`docs\implementation-reports\2026-06-13-framescope-monitor-worker-and-chart-chinese-localization-report.md`

## 一、必须命令执行结果

| 序号 | 命令 | 结果 | 记录 |
| --- | --- | --- | --- |
| 1 | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | 使用 bundled Node：`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`；`npm ci` 完成；typecheck PASS；Vitest 6 files / 64 tests PASS；Vite build PASS |
| 2 | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | 输出 `FrameScope tests rebuilt.` |
| 3 | `.\tests\FrameScopeReportManifestTests.exe` | PASS | 输出 `FrameScopeReportManifestTests: PASS`；期间生成的 manifest/data 证据包含 `cpuVoltage`、`cpuVid`、`bucketMs=1000`、VID 说明和诊断中文信息 |
| 4 | `.\tests\FrameScopeDiagnosticsTests.exe` | PASS | 输出 `FrameScopeDiagnosticsTests: PASS` |
| 5 | `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS | 输出 `FrameScopeSystemSamplerCpuCoreTests: PASS` |
| 6 | `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS | 输出 `FrameScopeNativeWatcherPolicyTests: PASS` |
| 7 | `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS | 输出 `FrameScopeNativeMonitorChildProcessTests: PASS` |
| 8 | `.\tests\FrameScopeProcessCleanupTests.exe` | PASS | 输出 `FrameScopeProcessCleanupTests: PASS` |
| 9 | bundled Node 运行 `.\tests\chart-sampling-tests.js` | PASS | 输出 `chart-sampling-tests: PASS` |
| 10 | bundled Node 运行 `.\tools\Probe-ReportHtmlLayout.js` | PASS | 覆盖 23 个场景，包含 1280x720 和 900x760；`allNoOverflow=true`；23 张重点截图非空白，`nonblankFailures=0` |
| 11 | `git diff --check` | PASS | 仅有 Git 换行提示：`LF will be replaced by CRLF`；没有 whitespace error |
| 12 | 残留进程检查 | PASS | 输出 `NO_MATCHING_RESIDUAL_PROCESSES` |

layout probe 证据目录：

`docs\test-reports\2026-06-13-framescope-monitor-worker-and-chart-chinese-localization-retest-evidence\layout-probe`

其中 `report-overflow-probe.json` 解析结果：

- `allNoOverflow=True`
- `scenarioCount=23`
- 视口覆盖：1280x720、900x760
- 图表标题覆盖：`FPS GamePP 图表`、`CPU 核心频率`、`CPU 电压 / Vcore`、`CPU 核心 VID（请求电压）`、`性能频率`、`系统占用`、`后台进程`、`IO / 温度`
- 截图检查：`screenshotCount=23`，`nonblankFailures=0`

说明：layout probe 第一次尝试因 diagnostic HTML 参数为空在脚本参数校验阶段退出，未完成测试；随后使用本轮可用的 synthetic report 重新运行成功。诊断类中文文案另由 `FrameScopeReportManifestTests.exe` 和 `FrameScopeDiagnosticsTests.exe` 覆盖。

## 二、监控 worker 行为复测

结论：PASS，双 `FrameScopeMonitor.exe` 可确认为预期 worker 架构，不是重复启动 UI。

复核到的链路：

- `src\app\FrameScopeNativeMonitor.cs:40` 识别 `--monitor-session`，进入 monitor-session 模式。
- `src\app\FrameScopeNativeMonitor.cs:52` 识别 `--watcher`，进入 watcher 模式。
- `src\app\FrameScopeNativeMonitor.WebHost.cs:99` UI 侧启动同一个 `FrameScopeMonitor.exe`，参数包含 `--watcher --config`。
- `src\app\FrameScopeNativeMonitor.Watcher.cs:212` watcher 再启动同一个程序并传入 `--monitor-session`。
- `src\app\FrameScopeNativeMonitor.Watcher.cs:229` monitor-session 启动参数包含 `--MonitorProcessRole monitor-session-worker`，便于任务管理器/命令行诊断。

日志与诊断区分：

- `src\app\FrameScopeNativeMonitor.WebHost.cs:117` 写入 `monitor-worker-start role=watcher-worker process=FrameScopeMonitor.exe`。
- `src\app\FrameScopeNativeMonitor.Watcher.cs:263` 写入 `monitor-worker-start role=monitor-session-worker process=FrameScopeMonitor.exe`。
- `src\app\FrameScopeNativeMonitor.Watcher.cs:327-329` bridge/状态 payload 保留 `WorkerRole = monitor-session-worker`、`WorkerProcessName = FrameScopeMonitor.exe`、worker 解释文案。
- `src\app\FrameScopeWebBridge.State.cs:27-29` 当前 watcher 状态返回 `processName = FrameScopeMonitor.exe`、`processRole = watcher-worker` 和 worker 解释。

UI/状态说明：

- `src\frontend\src\pages\OverviewPage.tsx:104-106` 当前监控页明确提示：任务管理器中可能显示一个 `FrameScopeMonitor.exe` 子进程；这是监控 worker，不是重复打开软件。
- `src\frontend\src\state\useFrameScopeBridgeState.ts` 中启动/停止状态文案使用“监控 worker”。
- `src\frontend\src\data\mockPreview.ts:627-629` mock preview 中也保留 `FrameScopeMonitor.exe`、`watcher-worker` 和“不是重复打开软件”的解释。

停止监控与清理：

- `src\app\FrameScopeNativeMonitor.ProcessCleanup.cs:101-102` 清理策略限定为 `FrameScopeMonitor.exe` 且命令行包含 `--watcher` 或 `--monitor-session` 的 worker。
- `src\app\FrameScopeNativeMonitor.ProcessCleanup.cs:161-162` 对 monitor-session worker 也有单独识别。
- `tests\FrameScopeProcessCleanupTests.cs:13`、`:44` 通过 `--watcher --watcher-sleep` 模拟 worker 残留并验证清理。
- `FrameScopeNativeWatcherPolicyTests.exe`、`FrameScopeNativeMonitorChildProcessTests.exe`、`FrameScopeProcessCleanupTests.exe` 均通过。
- 最终残留进程检查输出 `NO_MATCHING_RESIDUAL_PROCESSES`。

## 三、图表中文化复测

结论：PASS，图表可见文案已覆盖主要用户界面位置。允许保留的必要缩写仍保留：FPS、Vcore、VID、P95/P99、ms、GB、MB/s、V、°C。

已复核覆盖：

- tab：`src\reporting\FrameScopeReportGenerator.Html.Sections.cs:18` 包含 `帧率`、`CPU 核心频率`、`CPU 电压 / Vcore`、`CPU 核心 VID（请求电压）`、`性能图表`、`系统占用`、`后台进程`、`IO/温度`。
- 标题/说明：`src\reporting\FrameScopeReportGenerator.Html.Scripts.cs:70` 覆盖 `FPS GamePP 图表`、`CPU 核心频率`、`CPU 电压 / Vcore`、`CPU 核心 VID（请求电压）`、`性能频率`、`系统占用`、`后台进程`、`IO / 温度`。
- legend/reference lines：`src\reporting\FrameScopeReportGenerator.Html.Scripts.cs:52`、`:57` 使用 `最小值`、`最大值`、`平均值` 等中文参考线/图例。
- tooltip：`src\reporting\FrameScopeReportGenerator.Html.Scripts.cs:79` 使用 `平均 FPS`、`样本数`、`帧`、`bucketMs=1000 ms` 等中文可见文案与机器 key。
- summary：`src\reporting\FrameScopeReportGenerator.Html.Scripts.cs:60` 使用 `平均 FPS`、`最低瞬时 FPS`、`最大帧时间`、`长帧`、`占用`、`温度` 等中文。
- 空状态：`src\reporting\FrameScopeReportGenerator.Html.Scripts.cs:73` 使用 `无可绘制数据`、`没有采集到 CPU 电压 / Vcore 样本`、`没有采集到 CPU 核心 VID 样本`。
- metric 下拉项：`src\reporting\FrameScopeReportGenerator.Html.Scripts.cs:69` 覆盖 FPS、CPU Core、CPU Voltage、CPU VID、性能频率、系统占用、后台进程、IO/温度。
- Top 进程：`src\reporting\FrameScopeReportGenerator.Html.Scripts.cs:70` 中后台进程说明为 `默认按记录到的峰值绘制前 10 个进程`。
- IO/温度/系统占用：`src\reporting\FrameScopeReportGenerator.Html.Scripts.cs:72` 使用 `磁盘吞吐`、`网络吞吐`、`磁盘延迟`、`GPU 功耗`、`GPU 温度`、`CPU 占用`、`GPU 占用`、`VRAM 占用` 等中文。

CPU 电压和 VID：

- CPU Voltage / Vcore 可见标题为 `CPU 电压 / Vcore`，layout probe 在 1280x720 和 900x760 均确认该标题。
- CPU Core VID 可见标题为 `CPU 核心 VID（请求电压）`，layout probe 在 1280x720 和 900x760 均确认该标题。
- VID 说明为 `VID 是 CPU 每核心请求/目标电压，不是真实 Vcore；它与 CPU 电压 / Vcore 分开显示。`，`chart-sampling-tests.js` 已锁定这条说明。

## 四、关键语义回归复测

结论：PASS，机器字段、采样语义和兼容字段名保持。

`DATA.cpuVoltage` / `DATA.cpuVid` / `bucketMs`：

- `src\reporting\FrameScopeReportGenerator.cs:165-166` 仍生成 `cpuVoltage` 和 `cpuVid` 数据字段。
- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs:72` 图表仍分别读取 `DATA.cpuVoltage` 和 `DATA.cpuVid`，没有互相复用。
- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs:79` tooltip 仍读取 `bucketMs=Number(fps.bucketMs)||1000`。
- `tests\FrameScopeReportManifestTests.cs:330` 断言 `fps["bucketMs"] == 1000`。
- 本轮 synthetic report 数据检查结果：`hasCpuVoltage=true`、`hasCpuVid=true`、`fpsBucketMs=1000`。

FPS raw PresentMon：

- `src\reporting\FrameScopeReportGenerator.PresentMon.cs:34-40` 仍读取英文 PresentMon CSV header：`TimeInDateTime`、`MsBetweenPresents`、`Application`、`ProcessID`、`SwapChainAddress`、`PresentMode`、`AllowsTearing`。
- `tests\FrameScopeReportManifestTests.cs:448` 继续断言 raw PresentMon row count。
- 图表说明保留 `raw PresentMon 统计仍作为源数据`，未把展示 bucket 替代为统计 source of truth。

Vcore / VID 双向隔离：

- Vcore 数据路径：`src\reporting\FrameScopeReportGenerator.SystemData.cs:141` 读取 `cpu-voltage-samples.csv`；`:398-419` 构建 `cpu-voltage:vcore`，名称为 `CPU 电压 / Vcore`。
- VID 数据路径：`src\reporting\FrameScopeReportGenerator.SystemData.cs:204` 读取 `cpu-vid-samples.csv`；`:380` 使用稳定 key `cpu-vid:<core>`。
- `src\reporting\FrameScopeReportGenerator.SystemData.cs:560-578` 明确筛选 CPU Vcore voltage，并排除非 Vcore/不合格电压进入 Vcore。
- `src\reporting\FrameScopeReportGenerator.SystemData.cs:667-671` VID 有独立 header 查找。
- `tests\FrameScopeReportManifestTests.cs` 覆盖 `CpuVidOnlyDoesNotMakeCpuVoltagePerCoreAvailable`、`CpuPackageSocAndAggregateVcoreDoNotEnterCpuVidChart`、`CpuVoltageNonPerCoreTelemetryDoesNotCreateChartSeries`、`NoCpuVidSensorUsesChineseReasonAndNoFakeData` 等隔离用例。
- 本轮 synthetic report 数据检查结果：`cpuVoltageSeries=["cpu-voltage:vcore:CPU 电压 / Vcore"]`，`cpuVidSeries=["cpu-vid:0:核心 #1 VID","cpu-vid:1:核心 #2 VID"]`。

CSV header / manifest 字段名兼容：

- 测试输入仍使用英文 CSV header，例如 `Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB`、`ProcessName`、`CpuPct`、`WorkingSetMB`、`VoltageVolts`、`SensorName`、`SensorIdentifier`、`Status`、`TimeInDateTime`、`MsBetweenPresents`。
- manifest 字段仍是英文机器字段。本轮数据检查列出的关键 manifest keys 包括：`cpuVoltageAvailable`、`cpuVoltageVcoreAvailable`、`cpuVoltageSampleCount`、`cpuVoltageSamplesCsv`、`cpuVidAvailable`、`cpuVidCoreCount`、`cpuVidNote`、`cpuVidSamplesCsv`、`frameCaptureStatus`、`reportKind`。
- `tests\FrameScopeReportManifestTests.cs:51` 仍断言 manifest JSON 对默认 PowerShell 读取保持 ASCII-safe，避免中文字段名破坏兼容。

## 五、最终判定

PASS。

- 双进程：确认是 watcher / monitor-session worker 架构，不是重复打开 UI。
- 停止监控后残留：通过 cleanup 测试和最终进程检查确认无匹配残留 worker。
- 图表中文化：tab、标题、说明、legend、tooltip、summary、空状态、参考线、metric 下拉项、Top 进程、IO/温度/系统占用均已覆盖中文；必要缩写保留。
- `DATA.cpuVoltage` / `DATA.cpuVid` / `bucketMs`：均未改名，`bucketMs=1000` 保持。
- FPS raw、Vcore、VID 语义：raw PresentMon 仍是统计源数据；Vcore 与 VID 双向隔离；VID 不伪装成 Vcore，Vcore 不进入 VID。
- layout probe：23 场景通过，`allNoOverflow=true`，重点图表截图非空白。
- 未做事项：未改源码、未修 bug、未打包、未安装、未启动真实游戏、未测试 BF6、未推 GitHub、未更新 Release。
