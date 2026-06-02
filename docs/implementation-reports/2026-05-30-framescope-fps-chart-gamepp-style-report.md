# FrameScope FPS 图表 GamePP 风格改造报告

日期：2026-05-30

范围：只改报告 FPS 图表显示样式与该图表需要的展示数据；未打包、未安装、未启动真实游戏；未改 CPU Core VID、Vcore 移除、PresentMon silent no-csv、全局采样间隔、主题、托盘、日志、目标列表等功能。

## 结论

PASS：FrameScope 报告 FPS 图表已改为接近 GamePP 视觉语言的蓝色面积折线图，并保留 raw PresentMon 统计口径与 1 秒 bucket 显示口径。

建议进入单独 FPS 图表 GamePP 风格复测窗口：建议。复测窗口只需要覆盖不同 run、不同宽度、四个 FPS 下拉视图、tooltip、截图视觉确认；不需要打包、安装或启动真实游戏。

## 变更内容

- `src/reporting/FrameScopeReportGenerator.cs`
  - 在 `frameStats` 中新增 raw `maxInstant`，用于 Max FPS 右侧数值和红色虚线。
  - `average`、`low1`、`low01`、`minInstant`、`maxInstant` 都仍由 raw PresentMon 帧时间计算。
- `src/reporting/FrameScopeReportGenerator.Analysis.cs`
  - FPS 显示数据继续保持 `bucketMs=1000`。
  - 新增 `fps.samples`，记录每个 1 秒 bucket 内 raw 帧数量，用于 tooltip。
  - 没有恢复 `fps.min` 显示序列。
- `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`
  - FPS 图表新增蓝色面积填充、亮蓝色主线、清晰深色网格。
  - 新增 Max 红色虚线、Average 绿色虚线、Min 蓝色虚线，右侧显示对应数值和箭头标记。
  - FPS 图例固定显示 `1% Low / 0.1% Low / Min / Max / Average`。
  - FPS tooltip 固定显示当前时间点、FPS、1% Low、0.1% Low、当前 bucket 样本数量。
  - 时间轴改为稀疏刻度；常规 run 使用 `HH:MM:SS`，例如 `00:00:00`、`00:02:02`。
  - 不包含红色异常点 overlay，不恢复最低异常帧点逻辑。
- `src/reporting/FrameScopeReportGenerator.Html.Styles.cs`
  - 只为 FPS 图例新增颜色样式：Min 蓝色、Max 红色、Average 绿色、1%/0.1% Low 浅色。
- `tests/chart-sampling-tests.js`
  - 锁定 4 项 FPS 下拉、无 `min` 选项、无异常点 overlay、GamePP 式时间轴、面积图/参考线/tooltip 关键路径。
- `tests/FrameScopeReportManifestTests.cs`
  - 锁定 raw `maxInstant`、1 秒 bucket、bucket 样本数、无 `fps.min` 显示序列。

## 必答项

1. 是否把 FrameScope FPS 图表改成 GamePP 风格面积折线图：是。截图中 FPS 主线为亮蓝色，曲线下方有蓝色半透明面积填充。
2. 是否显示 Min / Max / Average 横向虚线和右侧数值：是。Max 红色、Average 绿色、Min 蓝色，右侧有对应数值和小箭头标记。
3. 是否显示 1% Low / 0.1% Low / Min / Max / Average 图例：是。图例位于图表上方工具栏区域。
4. 是否没有恢复红色异常点：是。测试确认没有 `fpsAnomalyPoints` / `drawFpsAnomalyMarkers` / 红色异常帧点文案；图中的红色只用于 Max 参考线。
5. 是否没有恢复“只看最低瞬时 FPS”：是。FPS 下拉仍只有 4 项：`all`、`avg`、`low1`、`low01`。
6. 是否仍使用 raw PresentMon 计算统计：是。CS2 验证 run 中 raw frames 为 17472，统计为 Average 195.30、1% Low 108.01、0.1% Low 30.92、Min 6.683、Max 1739.433。
7. 图表显示是否仍使用 1 秒 bucket，保持可读：是。`bucketMs=1000`，CS2 图表显示 90 个 bucket 点，不直接绘制 17472 个 raw FPS 点。
8. CS2 历史 run 截图路径：见“截图证据”。
9. 1280x720 / 900x760 是否无溢出：是。Playwright probe 和 WebView2 probe 均显示 `overflow=false` / `chartScrollOverflowX=false`。
10. 所有验证命令结果：见“验证结果”。
11. 残留进程检查结果：PASS，`NO_MATCHING_RESIDUAL_PROCESSES`。
12. 是否建议进入单独 FPS 图表 GamePP 风格复测窗口：建议进入，仅做 FPS 图表复测，不做打包、安装、全量 QA 或真实游戏启动。

## CS2 历史 run 验证

原始 run：

`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260505-101253`

复制件：

`artifacts\fps-chart-gamepp-style-20260530-224252\Counter-Strike-2-20260505-101253-copy`

重新生成报告：

`artifacts\fps-chart-gamepp-style-20260530-224252\Counter-Strike-2-20260505-101253-copy\charts\framescope-interactive-report.html`

数据核对：

- raw frames：17472
- `bucketMs`：1000
- FPS 显示 bucket 点数：90
- `fps.samples`：存在，tooltip 可显示每个 bucket 内 raw 帧数量
- `fps.min`：不存在
- `frameStats.average`：195.30
- `frameStats.low1`：108.01
- `frameStats.low01`：30.92
- `frameStats.minInstant`：6.683
- `frameStats.maxInstant`：1739.433

## GamePP CSV 对照

CSV：

`C:\Users\misakamiro\Desktop\2026-05-30  22_11 VALORANT-Win64-Shipping.csv`

对照结果：

- `FPS`：117 行，Min 1，Max 1250，Average 787.36
- `FPS1`：117 行，Min 1，Max 951，Average 382.29
- `FPS01`：117 行，Min 1，Max 890，Average 277.67

用途：只作为视觉语言和数值口径参考，没有导入 FrameScope 软件。

## 截图证据

Playwright / Edge 截图：

- `artifacts\fps-chart-gamepp-style-20260530-224252\screenshots\fps-all-1280x720.png`
- `artifacts\fps-chart-gamepp-style-20260530-224252\screenshots\fps-avg-1280x720.png`
- `artifacts\fps-chart-gamepp-style-20260530-224252\screenshots\fps-low1-1280x720.png`
- `artifacts\fps-chart-gamepp-style-20260530-224252\screenshots\fps-low01-1280x720.png`
- `artifacts\fps-chart-gamepp-style-20260530-224252\screenshots\fps-all-900x760.png`
- `artifacts\fps-chart-gamepp-style-20260530-224252\screenshots\fps-avg-900x760.png`
- `artifacts\fps-chart-gamepp-style-20260530-224252\screenshots\fps-low1-900x760.png`
- `artifacts\fps-chart-gamepp-style-20260530-224252\screenshots\fps-low01-900x760.png`
- Tooltip：`artifacts\fps-chart-gamepp-style-20260530-224252\screenshots\fps-tooltip-1280x720.png`
- Probe JSON：`artifacts\fps-chart-gamepp-style-20260530-224252\screenshots\screenshot-probe.json`

Tooltip 实测文本：

```text
00:00:45
FPS: 196.58 FPS
1% Low: 161.29 FPS
0.1% Low: 153.85 FPS
Sample count: 196 frames in 1000 ms bucket
```

WebView2 report probe：

- 1280x720：`artifacts\fps-chart-gamepp-style-20260530-224252\webview2-report-probe\webview2-fps-all-1280x720.json`
- 1280x720 截图：`artifacts\fps-chart-gamepp-style-20260530-224252\webview2-report-probe\webview2-fps-all-1280x720.png`
- 900x760：`artifacts\fps-chart-gamepp-style-20260530-224252\webview2-report-probe\webview2-fps-all-900x760.json`
- 900x760 截图：`artifacts\fps-chart-gamepp-style-20260530-224252\webview2-report-probe\webview2-fps-all-900x760.png`

WebView2 probe 结果：

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `fpsBucketMs=1000`
- `fpsPointCount=90`
- `rawFrameCount=17472`
- `hasMinOption=false`
- `hasMinSeries=false`
- `overflow=false`

## 验证结果

| 验证项 | 结果 | 证据 |
| --- | --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | `artifacts\fps-chart-gamepp-style-20260530-224252\verification\run-frontend-verify.log`，57 个 Vitest 测试通过，Vite build 通过 |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | `artifacts\fps-chart-gamepp-style-20260530-224252\verification\build-framescope-tests.log` |
| `FrameScopeReportManifestTests.exe` | PASS | `artifacts\fps-chart-gamepp-style-20260530-224252\verification\FrameScopeReportManifestTests.log` |
| `FrameScopeDiagnosticsTests.exe` | PASS | `artifacts\fps-chart-gamepp-style-20260530-224252\verification\FrameScopeDiagnosticsTests.log` |
| `node .\tests\chart-sampling-tests.js` | 默认 PATH node 失败，bundled Node PASS | 默认 WindowsApps `node.exe` 报 `Access is denied`；bundled Node 日志为 `artifacts\fps-chart-gamepp-style-20260530-224252\verification\chart-sampling-bundled-node.log` |
| WebView2 report screenshot/probe | PASS | `artifacts\fps-chart-gamepp-style-20260530-224252\webview2-report-probe\*.json` / `*.png` |
| `git diff --check` | PASS | `artifacts\fps-chart-gamepp-style-20260530-224252\verification\git-diff-check-final.log`；只有既有 LF/CRLF warning，无 whitespace error，退出码 0 |
| 残留进程检查 | PASS | `artifacts\fps-chart-gamepp-style-20260530-224252\verification\residual-process-check-final.log`，`NO_MATCHING_RESIDUAL_PROCESSES` |

## 边界确认

- 未打包。
- 未安装。
- 未启动 Valorant / BF6 / CS2 / 任何真实游戏。
- 未改 CPU Core VID 语义。
- 未改 Vcore 移除逻辑。
- 未改 PresentMon silent no-csv 逻辑。
- 未改全局采样间隔。
- 未改主题、托盘、日志、目标列表等功能。
