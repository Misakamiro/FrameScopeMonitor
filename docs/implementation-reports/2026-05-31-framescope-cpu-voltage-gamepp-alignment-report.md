# FrameScope CPU Voltage / GamePP 对齐实施报告

## 结论
PASS。FrameScope 已把 `CPU Voltage` 口径收束为整体真实 CPU Vcore / CPU Voltage，并与 GamePP 的 `CPU Voltage  [V]` 对齐；`CPU Core VID` 继续保持请求/目标电压的独立含义。

## GamePP 口径
- 参考 CSV 列名：`CPU Voltage  [V]`
- 单位：`V`
- 采样数：117
- 范围：`1.044` - `1.104`
- 平均值：`1.063692`
- 结论：GamePP 这里展示的是整体 CPU Voltage / Vcore，不是 `CPU Core VID`

## FrameScope 新规则
- `CPU Voltage` / `CPU Vcore`：只接受明确表示整体 CPU Voltage / CPU Vcore 的传感器
- `CPU Core VID`：继续保留为单独指标，表示 CPU 请求/目标电压
- `CPU Voltage` 不再拿 VID、SOC、Package、VBAT、VIN、GPU、DRAM、DDR、Memory、Chipset、Misc 这些电压顶替
- 带核心编号的电压名也拒绝，例如 `Core #1 Vcore`、`Core 0 Voltage`
- 如果宿主机没有真实 Vcore，只能显示 `unavailable` 或 `non-per-core-only`，不生成假数据

## 实现内容
- `src/monitoring/FrameScopeSystemSampler.CpuCoreTelemetry.cs`
  - 重写 CPU Voltage 分类
  - 只收整体 Vcore / CPU Voltage
  - 拒绝 per-core、VID 和非 CPU 供电轨
- `src/monitoring/FrameScopeSystemSampler.PerfCounters.cs`
  - WMI / 内置 LibreHardwareMonitor 路径同步使用同一套 Vcore 分类
- `src/reporting/FrameScopeReportGenerator.SystemData.cs`
  - `DATA.cpuVoltage` 单独来自 Vcore 采样
  - `DATA.cpuVid` 继续独立
- `src/reporting/FrameScopeReportGenerator.Metadata.cs`
  - 严格模式下只把真实 Vcore 计入 `cpuVoltageAvailable`
  - `cpuVoltagePerCore*` 在报告里保留兼容字段，但不再作为 CPU Voltage 事实来源
- `src/reporting/FrameScopeReportGenerator.Html.Sections.cs`
  - 增加独立 `CPU Voltage` tab
- `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`
  - `CPU Voltage / Vcore` 和 `CPU Core VID` 使用不同标题、说明和数据源
- `src/app/FrameScopeNativeMonitor.Watcher.cs`
  - watcher 继续传 CPU Voltage / VID telemetry 参数

## 接受 / 拒绝
接受：
- `CPU VCore`
- `CPU Voltage`
- `CPU Core Voltage`
- `VDDCR CPU`
- 其他明确标记为整体 CPU Voltage / Vcore 的等价名称

拒绝：
- `VID`
- `SOC`
- `Package`
- `VBAT`
- `VIN`
- `GPU`
- `DRAM`
- `DDR`
- `Memory`
- `Chipset`
- `Misc`
- 任何 `Core #n ...` 的编号电压

## 没有真实 Vcore 时
- 报告会显示不可用
- 只会保留拒绝证据或 unavailable reason
- 不会把 VID 伪装成 CPU Voltage

## FPS 是否改动
- 没改
- 仍是 raw PresentMon 统计
- `bucketMs=1000` 保持不变

## 是否做了禁止项
- 未打包
- 未安装 FrameScope
- 未启动真实游戏
- 未测试 BF6
- 未推 GitHub
- 未更新 Release

## 验证结果
| 命令 | 结果 | 备注 |
| --- | --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | 测试构建通过 |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | CPU Voltage / VID 口径分离通过 |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS | 诊断输出通过 |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS | Vcore 接受 / 非 Vcore 拒绝通过 |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS | watcher 参数通过 |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS | 子进程链路通过 |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | bundled Node / npm 通过 |
| `.\tests\chart-sampling-tests.js` with bundled Node | PASS | CPU Voltage / CPU Core VID / FPS 断言通过 |
| `tools\Probe-ReportHtmlLayout.js` | PASS | `allNoOverflow=true` |
| `git diff --check` | PASS | 仅有 LF/CRLF 提示，无 diff 错误 |
| residual process check | PASS | `NO_MATCHING_RESIDUAL_PROCESSES` |

## Probe 证据
- `artifacts\cpu-voltage-gamepp-alignment-20260531\layout-probe\cpu-voltage-1280x720.png`
  - 标题：`CPU Voltage / Vcore`
  - 说明：整体 CPU Voltage / Vcore，单位 V
- `artifacts\cpu-voltage-gamepp-alignment-20260531\layout-probe\cpu-core-vid-1280x720.png`
  - 标题：`CPU Core VID`
  - 说明：请求/目标电压
- `artifacts\cpu-voltage-gamepp-alignment-20260531\layout-probe\fps-default-1280x720.png`
  - FPS 图表未被改坏

## 最终结论
PASS。FrameScope 现在把 GamePP 的 CPU Voltage 口径和 CPU Core VID 语义分开了，CPU Voltage 只吃真实整体 Vcore，FPS 语义保持不变，验证结果全部通过。
