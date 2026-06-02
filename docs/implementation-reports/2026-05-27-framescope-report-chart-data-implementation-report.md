# FrameScope 报告图表数据与显示实现报告

日期：2026-05-27 Asia/Hong_Kong

源码根目录：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 结论

本轮只修改报告图表、报告数据处理和必要的 CPU telemetry 报告接入。没有测试 BF6，没有推 GitHub，没有更新 Release，没有发布安装器。`build.ps1` 按验证要求执行，生成 `dist` 安装包属于构建脚本既有副作用，本轮没有安装或发布它们。

实现结果：

- FPS 图表只保留一类红色“最低异常帧点”，不再用红点/黄点分别表达 1% Low、0.1% Low。
- 图表读取模式下拉已移除“原始密集/原始数据”，用户只看到“保留尖峰”和“趋势易读”。
- FPS 图表显示数据改为 1s bucket；平均 FPS、1% Low、0.1% Low 仍使用原始帧数据计算。
- 新增“CPU 核心频率”图表，读取 `cpu-core-samples.csv` 的每逻辑处理器 `ActualFrequencyMHz`，每个逻辑核心单独成线，单位 MHz。
- 新增“CPU 电压”图表入口；仅在 CSV/manifest 有真实 per-core voltage 字段时显示曲线。当前 synthetic run 没有真实电压字段，因此显示无数据原因，不生成假 VID/Vcore。
- tooltip 单位随图表类型变化：FPS、MHz、V；CPU 核心频率 tooltip 显示时间点和各核心频率。

## 修改文件

- `src\reporting\FrameScopeReportGenerator.cs`
  - 保留原始 `frames` 用于 `frameStats`。
  - FPS 展示 bucket 调整为 `1.0` 秒。
  - 输出 `cpuCore`、`cpuVoltage` 图表数据。
- `src\reporting\FrameScopeReportGenerator.SystemData.cs`
  - 新增 CPU core chart 数据读取与 1s 聚合。
  - 新增真实 voltage 字段探测；无真实字段时输出 unavailable 状态和中文原因。
- `src\reporting\FrameScopeReportGenerator.Metadata.cs`
  - 透传 CPU voltage 状态、原因和来源字段，供报告 manifest/data 使用。
- `src\reporting\FrameScopeReportGenerator.Models.cs`
  - 新增 CPU core bucket value 辅助模型。
- `src\reporting\FrameScopeReportGenerator.Html.Sections.cs`
  - 新增“CPU 核心频率”“CPU 电压”tab。
  - 移除原始密集模式文案。
- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`
  - FPS 图只绘制平均 FPS 曲线和统一红色异常点。
  - 新增 CPU core / voltage 图表分支、legend、tooltip 单位处理。
  - 移除旧的混合 spike marker 入口。
- `src\reporting\FrameScopeReportGenerator.Html.Styles.cs`
  - 补充禁用 tab 和 tooltip 溢出样式。
- `tests\FrameScopeReportManifestTests.cs`
  - 新增 1s FPS 展示但 raw frame stats 不变的回归测试。
  - 新增 CPU core chart 数据接入和无假 voltage 的回归测试。
- `tests\chart-sampling-tests.js`
  - 覆盖下拉无 raw option、无旧 spike marker、CPU core / voltage tab、统一异常点。

## 数据口径

FPS：

- `presentmon.csv` 原始采样仍保留原始间隔，本轮 synthetic run 有 120 行原始帧数据。
- `frameStats.average`、`frameStats.low1`、`frameStats.low01` 在报告生成器里先从原始 `frames` 计算，再生成 1s 图表 bucket。
- `fps.bucketMs=1000`，synthetic run 图表展示 12 个点，不用 120 个原始点直接铺满图表。
- evidence 重算结果：报告内 `91.32 / 23.81 / 23.81` 与从原始 `presentmon.csv` 重算的平均 FPS / 1% Low / 0.1% Low 完全一致。

CPU 核心频率：

- 来源是 `cpu-core-samples.csv`。
- 字段是每逻辑处理器 `ActualFrequencyMHz`。
- 报告图表按 1s bucket 输出，每个逻辑处理器独立曲线。
- 这份数据只进入 CPU core 图表，不进入 FPS 统计计算。

CPU 电压：

- 当前代码只接受明确真实电压字段：`CoreVoltageV`、`CpuCoreVoltageV`、`ActualVoltageV`、`SensorVoltageV`、`VoltageV`。
- 当前 synthetic run 没有真实 per-core voltage 字段，所以 `cpuVoltage.available=false`，series 为空，UI 显示原因：`当前 run 未包含真实 per-core voltage 字段；不会显示伪造 VID/Vcore。`
- 本轮没有写死 VID/Vcore，也没有用 SMBIOS/WMI 静态电压冒充实时 per-core voltage。

## 证据路径

数据证据：

- `artifacts\report-chart-20260527\data-evidence.json`
- `artifacts\report-chart-20260527\synthetic-report-run\presentmon.csv`
- `artifacts\report-chart-20260527\synthetic-report-run\process-samples.csv`
- `artifacts\report-chart-20260527\synthetic-report-run\cpu-core-samples.csv`
- `artifacts\report-chart-20260527\synthetic-report-run\charts\framescope-interactive-report.html`
- `artifacts\report-chart-20260527\screenshots\report-shot-summary.json`

截图证据：

- FPS 红色异常点：`artifacts\report-chart-20260527\screenshots\fps-anomaly-1280.png`
- CPU 核心频率图和 MHz tooltip：`artifacts\report-chart-20260527\screenshots\cpu-core-tooltip-1280.png`
- CPU 电压无真实数据状态：`artifacts\report-chart-20260527\screenshots\cpu-voltage-no-data-1280.png`
- 900x760 下拉菜单：`artifacts\report-chart-20260527\screenshots\dropdown-900x760.png`

关键 evidence 摘要：

```json
{
  "presentMonRawRows": 120,
  "processSampleRows100ms": 120,
  "processSecondElapsedMs": 100,
  "fpsDisplayBucketMs": 1000,
  "fpsDisplayPointCount": 12,
  "frameStatsMatchRaw": true,
  "cpuCoreSeriesCount": 4,
  "cpuVoltageAvailable": false,
  "rawOptionPresentInHtml": false,
  "mixedSpikeMarkerPresentInHtml": false
}
```

## 验证结果

通过：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
  - TypeScript typecheck passed。
  - Vitest `5` files / `49` tests passed。
  - Vite build passed。
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`
  - Native build passed。
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
  - `FrameScope tests rebuilt.`
- C# tests：
  - `tests\FrameScopeReportManifestTests.exe` passed。
  - `tests\FrameScopeSystemSamplerCpuCoreTests.exe` passed。
  - `tests\FrameScopeWebBridgeTests.exe` passed。
  - `tests\FrameScopeNativeMonitorChildProcessTests.exe` passed。
- JS report chart contract：
  - `tests\chart-sampling-tests.js` passed。
- Synthetic report generator：
  - `FrameScopeReportGenerator.exe artifacts\report-chart-20260527\synthetic-report-run` exit `0`。
- WebView2 live smoke：
  - `artifacts\report-chart-20260527\webview2-live\smoke.json`
  - `success=true`、`smokePayload.success=true`、Reports live action / theme smoke / bridge extension smoke 均为 true。
- WebView2 reduced-motion smoke：
  - `artifacts\report-chart-20260527\webview2-reduced-motion\smoke.json`
  - `success=true`、`reducedMotion=true`、Reports live action / theme smoke / bridge extension smoke 均为 true。
- `git diff --check`
  - Git exit code `0`。
  - 仅有工作区既有 LF/CRLF warning，无 whitespace error。
- 残留进程检查：
  - `artifacts\report-chart-20260527\residual-process-check-final.json`
  - `matchingResidualCount=0`。

## 限制和后续

CPU 电压图表入口和数据通道已就绪，但当前采集结果没有真实 per-core voltage 字段，所以报告会显示无数据原因。后续如果要显示电压，需要先接入可信的实时传感器来源，并把来源/字段写入 CSV 或 manifest；不能从 VID、SMBIOS/WMI 静态值或固定假数生成曲线。
