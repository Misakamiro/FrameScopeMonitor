# FrameScope CPU Voltage / GamePP 对齐专项复测报告

日期：2026-05-31
结论：PASS

## 复测范围

本轮只做专项复测：没有修源码、没有修 bug、没有打包、没有安装 FrameScope、没有启动真实游戏、没有测试 BF6、没有推 GitHub、没有更新 Release。`FrameScopeReportManifestTests` 中出现的 `bf6.exe` 只是合成测试数据，不是真实游戏测试。

实施报告 `docs/implementation-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-report.md` 声称的实现范围已复核：GamePP `CPU Voltage  [V]` 对齐为整体 `CPU Voltage / Vcore`；`CPU Core VID` 保留为请求/目标电压；二者数据链路、报告字段、图表、tooltip、单位分开；FPS 图表保持 `bucketMs=1000` 和 raw PresentMon 统计语义。

## 直接回答

| 问题 | 复测结论 |
| --- | --- |
| GamePP `CPU Voltage [V]` 是否被正确理解为整体 `CPU Voltage / Vcore` | 是。CSV 实际列名为 `CPU Voltage  [V]`，归一化后等同 `CPU Voltage [V]`，单位 `V`，117 条样本范围 `1.044-1.104 V`，符合整体 Vcore 口径。 |
| FrameScope 是否新增/恢复独立 CPU Voltage / Vcore 口径 | 是。`DATA.cpuVoltage` 独立存在，series key 为 `cpu-voltage:vcore`，名称 `CPU Voltage / Vcore`，单位 `V`。 |
| `CPU Core VID` 是否仍独立且未被伪装成 Vcore | 是。`DATA.cpuVid` 独立存在，source field 为 `VidVolts`，note 明确 `VID 是 CPU 请求/目标电压，不是真实 per-core Vcore`。 |
| VID / SOC / Package / VBAT / VIN 是否不会进入 CPU Voltage | 是。采样分类和报告 CSV 二次过滤均拒绝这些 token；测试覆盖通过。 |
| 没有真实 Vcore 时是否如实 unavailable / non-per-core-only | 是。无 Vcore 时报告 reason 保持不可用；只有非 Vcore 电压时 status 为 `non-per-core-only`，不合成假 CPU Voltage。 |
| CPU Voltage 和 CPU Core VID 图表标题、单位、tooltip、数据字段是否分开 | 是。CPU Voltage 图表标题 `CPU Voltage / Vcore`、单位 `V`、读 `DATA.cpuVoltage`；CPU Core VID 图表标题 `CPU Core VID`、单位 `V`、读 `DATA.cpuVid`。tooltip 用当前图表的 `currentUnit` 和对应 series，不混用字段。 |
| FPS 是否保持 `bucketMs=1000` 和 raw 统计语义 | 是。`BuildBucketedFps(frames, start, 1.0, 2.0)` 生成 `bucketMs=1000`；报告数据保留 `rawRows=31`、`validRows=30`、`selectedRows=30`，tooltip 显示 `1000 ms bucket`。 |
| 是否有源码修改 | 本轮复测没有修改源码。当前工作区复测前已存在大量实现 diff；本轮只新增 `docs/test-reports` 下的复测报告和证据产物。 |
| 是否有打包、安装、真实游戏、GitHub、Release 行为 | 没有。 |

## GamePP CSV 证据

- 参考 CSV：`C:\Users\misakamiro\Desktop\2026-05-30  22_11 VALORANT-Win64-Shipping.csv`
- 逻辑列名：`CPU Voltage [V]`
- 实际列名：`CPU Voltage  [V]`
- 单位：`V`
- 样本数：117
- 范围：`1.044-1.104 V`
- 平均：`1.063692 V`
- 证据：`docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/00-gamepp-csv-stats.json`

## 静态检查结论

- `src/monitoring/FrameScopeSystemSampler.CpuCoreTelemetry.cs`：`ClassifyCpuVoltageSensorText` 先拒绝 VID，再拒绝 SOC / Package / VBAT / VIN / GPU / DRAM / DDR / Memory / Chipset / Misc / 3.3V / 5V / 12V / per-core 编号；只接受明确 `vcore` / `cpu-vcore` / `cpu-voltage` / `CPU Voltage` / `VDDCR CPU` 等整体 Vcore 名称。
- `src/reporting/FrameScopeReportGenerator.SystemData.cs`：`ReadCpuVoltageChartFromCsv` 只把 `IsCpuVcoreVoltageCsvRow(...)` 通过的行加入 `DATA.cpuVoltage`；`ReadCpuVidChart` 单独读取 `cpu-vid-samples.csv` 和 `VidVolts`。
- `src/reporting/FrameScopeReportGenerator.Metadata.cs`：严格模式下 `cpuVoltageAvailable = voltageVcoreAvailable`，非 Vcore 只会影响 `cpuVoltageNonPerCoreAvailable` / rejected counts。
- `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`：`cpuVoltage` 和 `cpuVid` 的 title、note、series、metric state 分开；FPS note 和 tooltip 保留 raw PresentMon bucket 语义。
- `src/reporting/FrameScopeReportGenerator.cs` + `Analysis.cs`：FPS 使用 `BuildBucketedFps(frames, start, 1.0, 2.0)`，即 `bucketMs=1000`。
- 静态证据：`docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/16-static-code-evidence.txt`

## 图表与 Probe 证据

- Probe 输出：`docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/layout-probe/report-overflow-probe.json`
- Probe 汇总：`docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/11-probe-summary.json`
- `allNoOverflow=true`，23 个场景无页面/图表横向溢出。
- CPU Voltage / Vcore 图表证据：`docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/layout-probe/cpu-voltage-1280x720.png`
- CPU Core VID 图表证据：`docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/layout-probe/cpu-core-vid-1280x720.png`
- FPS 图表未回退证据：`docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/layout-probe/fps-default-1280x720.png`
- FPS tooltip / `1000 ms bucket` 证据：`docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/layout-probe/fps-tooltip-1280x720.png`
- 图表数据证据：`docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/15-chart-data-evidence.json`

## 验证命令结果

| 命令 | 结果 | 日志 |
| --- | --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS, exit 0 | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/01-build-framescope-tests.log` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS, exit 0 | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/02-report-manifest-tests.log` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS, exit 0 | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/03-diagnostics-tests.log` |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS, exit 0 | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/04-system-sampler-cpu-core-tests.log` |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS, exit 0 | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/05-native-watcher-policy-tests.log` |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS, exit 0 | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/06-native-monitor-child-process-tests.log` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS, exit 0 | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/07-frontend-verify.log` |
| PATH `node --version` 环境检查 | WindowsApps `node.exe` Access is denied，环境问题，不计产品失败 | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/08-path-node-version-env-check.log` |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS, exit 0 | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/09-chart-sampling-tests-bundled-node.log` |
| bundled Node `tools\Probe-ReportHtmlLayout.js` | PASS, exit 0, `allNoOverflow=true` | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/10-report-html-layout-probe.log` |
| `git diff --check` | PASS, exit 0；只有 LF/CRLF warning，无 whitespace error | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/12-git-diff-check.log` |
| 残留进程检查 | PASS, `NO_MATCHING_RESIDUAL_PROCESSES` | `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/13-residual-process-check.log` |

汇总证据：`docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/17-validation-summary.json`

## 残留与工作区状态

- 残留进程：`NO_MATCHING_RESIDUAL_PROCESSES`
- 工作区状态证据：`docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/14-git-status-after-retest.txt`
- 说明：工作区中已有实现相关源码 diff 和未跟踪文件；本轮复测没有修复或编辑源码，只新增本报告及 `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/`。

## 最终结论

PASS。FrameScope 当前实现把 GamePP 的整体 `CPU Voltage / Vcore` 与 `CPU Core VID` 请求/目标电压分开处理；VID / SOC / Package / VBAT / VIN 不会进入 CPU Voltage；无真实 Vcore 时如实不可用或 `non-per-core-only`；报告图表标题、单位、tooltip、数据字段分开；FPS 仍保持 `bucketMs=1000` 和 raw PresentMon 统计语义。
