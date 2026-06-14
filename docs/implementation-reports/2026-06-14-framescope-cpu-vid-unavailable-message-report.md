# FrameScope CPU Core VID 不可信来源无数据提示优化报告

日期：2026-06-14

## 结论

PASS。

本次只优化报告侧 CPU Core VID 无数据状态、中文文案和必要测试。未修改采集策略，未恢复 0.7V 阈值策略，未把 Vcore 写入 `DATA.cpuVid`，未重新绘制 AMD LibreHardwareMonitor 约 0.5V 的不可信 VID。

## 用户可见文案

CPU Core VID 无数据状态现在显示：

> 这不是软件漏画图。当前硬件的 AMD LibreHardwareMonitor Core VID 来源不可信，已停止显示该错误 VID（约 0.5V）。CPU Voltage / Vcore 仍可在 CPU 电压 / Vcore 图表中查看；Vcore 不会冒充 VID。未来检测到合法 VID 来源时，CPU Core VID 图表会正常显示。

## 改动文件

- `src/reporting/FrameScopeReportGenerator.SystemData.cs`
  - 将 AMD LibreHardwareMonitor Core VID 不可信来源的报告侧无数据原因统一本地化为中文说明。
  - 报告读数仍按来源拒绝 AMD LHM Core VID，不恢复低电压阈值策略，不把 Vcore 回填为 VID。
- `src/reporting/FrameScopeReportGenerator.Metadata.cs`
  - Manifest 中 `cpuVidReason` 走相同中文本地化。
  - 当 telemetry 只有被拒绝的 AMD LHM Core VID 样本时，报告 manifest 仍保持 `cpuVidAvailable=false`、`cpuVidStatus=unavailable`。
- `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`
  - CPU Core VID tab 在无数据时仍可进入。
  - `viewNote` 和 canvas empty state 显示中文原因。
  - CPU VID unavailable 时 `buildSeries()` 不产出曲线。
  - 新增空态文案换行绘制，避免长中文说明在 canvas 右侧被裁切。
- `tests/chart-sampling-tests.js`
  - 覆盖 AMD LHM 来源不可信时 CPU Core VID 中文空态。
  - 覆盖 CPU Core VID tab 仍存在。
  - 覆盖 Vcore 仍在 CPU 电压 / Vcore 图表。
  - 覆盖合法 VID fixture 仍正常显示 series。
  - 覆盖 CPU Core VID 空态文案按宽度换行。
- `tests/FrameScopeReportManifestTests.cs`
  - 覆盖 report manifest 和 `DATA.cpuVid.reason` 的中文不可信来源文案。
  - 覆盖 CPU Core VID tab 不消失、不 disabled。
  - 覆盖 `cpuVid.series.Count == 0`，并确认 `cpuVoltage.series[0].key == "cpu-voltage:vcore"`。

## 最新 Valorant Run 验证

Run：

`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260614-114551`

重新生成报告：

- `charts\framescope-interactive-report.html`
- `charts\framescope-interactive-data.js`
- `charts\framescope-interactive-manifest.json`

生成后数据确认：

- `DATA.cpuVid.available=false`
- `DATA.cpuVid.t.length=0`
- `DATA.cpuVid.series.length=0`
- `DATA.cpuVid.reason` 为上述中文文案
- CPU Core VID tab 存在：`data-view='cpuVid'`
- CPU Core VID tab 未 disabled
- `DATA.cpuVoltage.available=true`
- `DATA.cpuVoltage.series[0].key="cpu-voltage:vcore"`
- `DATA.cpuVoltage.series[0].name="CPU 电压 / Vcore"`
- `DATA.cpuVid.series` 不包含 `vcore` 或 `cpu-voltage`

## 页面和布局证据

布局探针输出：

`docs\test-reports\2026-06-14-framescope-cpu-vid-unavailable-message-layout-evidence\report-overflow-probe.json`

确认：

- `ConvertFrom-Json=PASS`
- `allNoOverflow=true`
- CPU Core VID 桌面截图：`docs\test-reports\2026-06-14-framescope-cpu-vid-unavailable-message-layout-evidence\cpu-core-vid-1280x720.png`
- CPU Core VID 窄视口截图：`docs\test-reports\2026-06-14-framescope-cpu-vid-unavailable-message-layout-evidence\cpu-core-vid-900x760.png`

in-app Browser 也通过临时本地 HTTP 地址打开了同一份报告，点击 CPU Core VID tab 后确认：

- 页面标题：`FrameScope - Valorant 性能报告`
- CPU Core VID tab 按钮数量：1
- active tab 文本：`CPU 核心 VID（请求电压）`
- active tab class：`tab active`
- `viewNote` 显示完整中文无数据原因
- console warn/error：0

临时 HTTP 服务已关闭。

## 验证结果

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`：PASS，64 个 Vitest 通过，Vite build 通过。
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`：PASS。
- `.\tests\FrameScopeReportManifestTests.exe`：PASS。
- `.\tests\FrameScopeDiagnosticsTests.exe`：PASS。
- `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`：PASS。
- bundled Node `.\tests\chart-sampling-tests.js`：PASS。
- bundled Node `.\tools\Probe-ReportHtmlLayout.js`：PASS，JSON 可由 `ConvertFrom-Json` 解析，`allNoOverflow=true`。

## 明确未做

- 未改采集策略。
- 未恢复 0.7V 阈值策略。
- 未打包。
- 未安装。
- 未启动真实游戏。
- 未测试 BF6。
- 未推 GitHub。
- 未更新 Release。
