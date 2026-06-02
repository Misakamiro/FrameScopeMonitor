# FrameScope Monitor CPU Core VID 复测报告

日期：2026-05-28

结论：PASS

## 复测边界

本轮只做复测和报告整理。未修改源码实现，未测试 BF6，未启动真实游戏，未安装，未推 GitHub，未更新 Release。

已写入/刷新测试产物：

- 最终命令日志：`artifacts\cpu-vid-final-retest-20260528-130613`
- 图表截图证据：`artifacts\cpu-vid-retest-20260528-124338\screenshots-retest`
- FPS 原始帧统计证据：`artifacts\cpu-vid-fps-raw-stats-retest-20260528-125836`

## 必答结论

| 问题 | 结论 |
| --- | --- |
| 本机是否采到 Core VID？ | PASS。最终 host provider probe 采到 `Core #1 VID` 到 `Core #8 VID`。 |
| VID 是否独立落盘并进图表？ | PASS。验证了 `cpu-vid-samples.csv`、`cpu-vid-telemetry-status.json`、`cpuVid*` manifest 字段、`DATA.cpuVid`、独立 CPU Core VID 图表。 |
| 是否没有把 VID 伪装成真实 Vcore？ | PASS。VID 文案标注为 CPU 请求/目标电压，不是真实 per-core Vcore；CPU 电压图没有 VID/Vcore/SOC/Package 曲线。 |
| 是否建议进入本机安装更新验证？ | 建议进入下一轮本机安装更新验证。源码/产物链复测已通过，但本轮按要求没有安装。 |

## 最终证据根目录

| 证据 | 路径 |
| --- | --- |
| 最终总汇总 | `artifacts\cpu-vid-final-retest-20260528-130613\final-verification-summary.json` |
| 命令日志 | `artifacts\cpu-vid-final-retest-20260528-130613\command-logs` |
| Host provider probe | `artifacts\cpu-vid-final-retest-20260528-130613\host-provider-probe` |
| Synthetic/fake 分离验证 | `artifacts\cpu-vid-final-retest-20260528-130613\synthetic-separation` |
| WebView2 live/reduced smoke | `artifacts\cpu-vid-final-retest-20260528-130613\webview2-smoke` |
| 图表截图 | `artifacts\cpu-vid-retest-20260528-124338\screenshots-retest` |

## 必跑命令结果

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| `tools\Run-Frontend.ps1 verify` | PASS | `command-logs\tools_Run-Frontend_verify.log`；TypeScript、50 个 Vitest、Vite build 通过。 |
| `build.ps1` | PASS | `command-logs\build_ps1.log`；生成 Setup/Full Setup，未执行安装器。 |
| `tests\Build-FrameScopeTests.ps1` | PASS | `command-logs\tests_Build-FrameScopeTests.log`。 |
| `FrameScopeConfigStoreTests.exe` | PASS | Settings/config 覆盖通过。 |
| `FrameScopeSystemSamplerCpuCoreTests.exe` | PASS | CPU core、VID、电压状态、间隔、隔离覆盖通过。 |
| `FrameScopeReportManifestTests.exe` | PASS | manifest、`DATA.cpuVid`、VID-only、aggregate rejection、CPU frequency、FPS raw stats 覆盖通过。 |
| `FrameScopeWebBridgeTests.exe` | PASS | Settings/bridge flow 覆盖通过。 |
| `FrameScopeNativeMonitorChildProcessTests.exe` | PASS | child process sampler path、VID CSV/status 路径覆盖通过。 |
| 其他受影响 C# 测试 | PASS | Diagnostics、cleanup、process sampler、PresentMon diagnostics、WebHost lifecycle、WebView2 runtime、capture planner、report progress、UI state、PUBG simulator 测试均通过；未启动真实游戏。 |
| `tests\chart-sampling-tests.js` | PASS | `command-logs\chart-sampling-tests.js.log`。 |
| Host provider probe | PASS | `host-provider-probe\provider-probe-summary-final.json`。 |
| Synthetic/fake run | PASS | `synthetic-separation\synthetic-summary-final.json`。 |
| WebView2 live smoke | PASS | `webview2-smoke\live\smoke.json`；`success=true`、`pageReady=true`、console errors=0。 |
| WebView2 reduced-motion smoke | PASS | `webview2-smoke\reduced-motion\smoke.json`；`reducedMotion=true`、console errors=0。 |
| `git diff --check` | PASS | `command-logs\git-diff-check.log`；只有 LF/CRLF 规范化 warning。 |
| 残留进程检查 | PASS | `residual-process-check.json`；`NO_MATCHING_RESIDUAL_PROCESSES`。 |

## Host provider probe

最终 probe 使用短 fake target 驱动 `FrameScopeSystemSampler.exe`，没有启动真实游戏。

结果：

- `cpu-vid-samples.csv`：8 行 VID 样本。
- VID sensors：`Core #1 VID`、`Core #2 VID`、`Core #3 VID`、`Core #4 VID`、`Core #5 VID`、`Core #6 VID`、`Core #7 VID`、`Core #8 VID`。
- `cpu-vid-telemetry-status.json`：`CpuVidAvailable=true`、`CpuVidStatus=core-vid-available`、`CpuVidSource=builtin-librehardwaremonitor`。
- manifest：`cpuVidAvailable=true`、`cpuVidSampleCount=8`、`cpuVidCoreCount=8`、`cpuVidNote=VID 是 CPU 请求/目标电压，不是真实 per-core Vcore。`
- `DATA.cpuVid`：available=true，8 条 VID series。
- CPU 电压：只检测到 non-per-core `Vcore`、`Vcore Misc`、`Vcore SoC`，`DATA.cpuVoltage.series=0`。
- CPU 核心频率：`DATA.cpuCore.series=16`，核心频率图链路正常。

## Synthetic/fake 分离验证

`synthetic-separation\synthetic-summary-final.json` 结论为 PASS。

- `vid-only`：`cpuVidAvailable=true`、`cpuVidSeries=2`、`cpuVoltageAvailable=false`、`cpuVoltageSeries=0`。
- `aggregate-vcore-soc-package`：`cpuVidAvailable=false`、`cpuVidSeries=0`、`cpuVoltageAvailable=false`、`cpuVoltageSeries=0`、`cpuVoltageNonPerCoreAvailable=true`。

这证明：

- VID-only 会进入 CPU Core VID 图。
- aggregate `Vcore` / `Vcore SoC` / `CPU Package` 不进入 CPU Core VID 图。
- VID 不进入 CPU 电压图。
- CPU 电压图只接受真实 per-core voltage，不把 VID/Vcore/SOC/Package 伪装成 per-core voltage。

## 图表截图证据

截图汇总：`artifacts\cpu-vid-retest-20260528-124338\screenshots-retest\report-chart-screenshot-summary-retest.json`

结果：`problemCount=0`。

必需截图：

- CPU Core VID 图：`report-cpu-core-vid-chart-1280x720-retest.png`
- CPU 电压无真实 per-core voltage 图：`report-cpu-voltage-no-real-per-core-chart-1280x720-retest.png`
- CPU 核心频率图：`report-cpu-core-frequency-chart-1280x720-retest.png`
- 图表下拉菜单：`report-cpu-vid-dropdown-expanded-900x760-retest.png`
- 900x760 布局：`report-cpu-core-vid-chart-900x760-retest.png`

截图状态核对：

- CPU Core VID 标题为 `CPU Core VID`，下拉包含 `Core #1 VID` 到 `Core #8 VID`。
- CPU 电压图标题为 `CPU 电压`，metric disabled=true，表示没有真实 per-core voltage 曲线。
- CPU 核心频率图标题为 `CPU 核心频率`，下拉包含 16 个逻辑处理器。
- 1280x720 与 900x760 布局均非空，截图字节数正常。

## Settings 与 FPS

Settings 采样间隔没有回退：

- `framescope-config.json`：`PollIntervalMs=1003`。
- 所有 target：`SampleIntervalMs=100`、`ProcessSampleIntervalMs=100`、`SlowSampleIntervalMs=1000`。
- CPU telemetry：`PerCoreSampleIntervalMs=1000`、`PerCoreVoltageSampleIntervalMs=1000`。

FPS 原始帧统计没有被图表 1s bucket 破坏：

- 证据：`artifacts\cpu-vid-fps-raw-stats-retest-20260528-125836\fps-raw-stats-summary.json`
- raw frames：20。
- manifest raw/valid PresentMon rows：20 / 20。
- chart display bucket：1000ms。
- display points：2。
- raw-frame stats：average 86.96，1% low 25，0.1% low 25。

1s bucket 只影响图表显示密度，没有替代原始帧统计。

## 最终判断

PASS。

CPU Core VID 是独立链路，VID 明确标注为请求/目标电压，不是真实 per-core Vcore；CPU 电压图没有把 VID、Vcore、SOC 或 Package 画成 per-core voltage；CPU 核心频率图仍正常；Settings 采样间隔和 FPS 原始帧统计均保持正确。

建议进入独立的本机安装更新验证窗口：先做安装包/本机安装同步，再验证安装态 UI、报告、VID、电压隔离和残留进程。该步骤本轮未执行。
