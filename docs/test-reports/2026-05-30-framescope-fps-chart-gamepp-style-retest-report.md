# FrameScope FPS 图表 GamePP 风格专项复测报告

日期：2026-05-31

结论：PASS。

本次只复测 FPS 图表 GamePP 风格与 FPS 统计语义。未打包、未安装、未启动 Valorant / BF6 / CS2 / PUBG / Apex / 任何真实游戏，未修改源码，未做全量 QA。只新增 artifacts 证据和本报告。

## 范围与 artifacts

- 源码路径：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
- 证据目录：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\fps-chart-gamepp-style-retest-20260531-021611`
- 必读实现报告已核对：`docs\implementation-reports\2026-05-30-framescope-fps-chart-gamepp-style-report.md`
- 探针 JSON：`artifacts\fps-chart-gamepp-style-retest-20260531-021611\fps-chart-retest-probe.json`
- WebView2 汇总：`artifacts\fps-chart-gamepp-style-retest-20260531-021611\webview2-report-probe\outputs\webview2-summary.json`

## 历史 run 覆盖

原始 run 均只读，全部复制到 artifacts 后重新生成报告。

| Run | 原始路径 | 复制件 | raw frames | bucket points | bucketMs | Average | 1% Low | 0.1% Low | Min | Max |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Counter-Strike-2-20260505-101253 | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260505-101253` | `artifacts\fps-chart-gamepp-style-retest-20260531-021611\runs\Counter-Strike-2-20260505-101253-copy` | 17472 | 90 | 1000 | 195.30 | 108.01 | 30.92 | 6.683 | 1739.433 |
| Valorant-20260524-000615 | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260524-000615` | `artifacts\fps-chart-gamepp-style-retest-20260531-021611\runs\Valorant-20260524-000615-copy` | 60317 | 75 | 1000 | 819.23 | 63.13 | 8.97 | 1.020 | 3571.429 |
| Valorant-20260530-220252 | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260530-220252` | `artifacts\fps-chart-gamepp-style-retest-20260531-021611\runs\Valorant-20260530-220252-copy` | 73232 | 84 | 1000 | 877.96 | 102.87 | 14.54 | 1.034 | 4784.689 |

## GamePP 风格视觉

PASS：FPS 图表已显示为接近 GamePP 风格的深色网格、蓝色面积折线图。

- 蓝色面积折线图存在：PASS。截图与 canvas 像素探针均确认存在亮蓝主线和蓝色面积填充。
- Min / Max / Average 横向虚线存在：PASS。Min 为蓝色虚线，Max 为红色虚线，Average 为绿色虚线。
- 右侧 Min / Max / Average 数值存在且未裁切：PASS。1280x720 与 900x760 截图右侧标签可见。
- 顶部图例显示 `1% Low / 0.1% Low / Min / Max / Average`：PASS。探针确认每个截图视图均包含这些图例文本，且不重叠。
- tooltip 黑色浮层可读：PASS。3 个 run 均包含时间点、FPS、1% Low、0.1% Low、Sample count / bucket 样本数。
- 图表背景、网格、线条接近 GamePP 风格：PASS。不复制 GamePP logo 或完整界面。

Tooltip 实测文本示例：

```text
00:00:48
FPS: 195.80 FPS
1% Low: 156.25 FPS
0.1% Low: 151.52 FPS
Sample count: 196 frames in 1000 ms bucket
```

## FPS 数据语义

PASS：没有破坏 FPS 统计语义。

- `Average FPS / 1% Low / 0.1% Low / Min / Max` 仍从 raw PresentMon 帧数据计算：PASS。`frameStats` 保留 raw 统计，且 raw frame count 与 chart bucket point count 明显不同。
- 图表显示仍使用 1 秒 bucket：PASS。3 个 run 均为 `bucketMs=1000`。
- 不把所有 raw FPS 点直接画到图上：PASS。CS2 为 17472 raw frames -> 90 bucket points；两个 Valorant run 分别为 60317 -> 75、73232 -> 84。
- 不恢复 1s bucket 作为统计数据源：PASS。源码路径中 `frameStats` 由 raw `frameMs` 计算，`BuildBucketedFps` 只生成展示序列；`FrameScopeReportManifestTests.exe` 已通过。
- 不恢复 `DATA.fps.min` 视图数据：PASS。探针确认 `hasFpsMinSeries=false`，源码/HTML 不读取 `DATA.fps.min`。
- raw `maxInstant` 存在并用于 Max 指标：PASS。3 个 run 的 `frameStats.maxInstant` 均存在，Max 虚线/图例从该指标显示。

## FPS 下拉项

PASS：FPS 下拉只有 4 项：

1. `平均 FPS / 1% Low / 0.1% Low`
2. `只看平均 FPS`
3. `只看 1% Low`
4. `只看 0.1% Low`

未出现 `只看最低瞬时 FPS`。探针中所有 24 张视图截图的 option values 均为 `all, avg, low1, low01`。

## 红色异常点 overlay

PASS：没有红色异常帧散点 overlay。

- 源码/生成 HTML 中未发现 `fpsAnomalyPoints`、`drawFpsAnomalyMarkers`、`drawSpikeMarkers`、`红色异常帧点` 或旧红色异常点颜色路径。
- 图中红色仅用于 Max 虚线和 Max 图例/右侧值，符合允许范围。

## 截图路径

每个 run 均生成 1280x720 与 900x760 的四个 FPS 视图截图，并生成 tooltip 截图。共 27 张。

截图目录：`artifacts\fps-chart-gamepp-style-retest-20260531-021611\screenshots`

- `Counter-Strike-2-20260505-101253-all-1280x720.png`
- `Counter-Strike-2-20260505-101253-avg-1280x720.png`
- `Counter-Strike-2-20260505-101253-low1-1280x720.png`
- `Counter-Strike-2-20260505-101253-low01-1280x720.png`
- `Counter-Strike-2-20260505-101253-all-900x760.png`
- `Counter-Strike-2-20260505-101253-avg-900x760.png`
- `Counter-Strike-2-20260505-101253-low1-900x760.png`
- `Counter-Strike-2-20260505-101253-low01-900x760.png`
- `Counter-Strike-2-20260505-101253-tooltip-1280x720.png`
- `Valorant-20260524-000615-all-1280x720.png`
- `Valorant-20260524-000615-avg-1280x720.png`
- `Valorant-20260524-000615-low1-1280x720.png`
- `Valorant-20260524-000615-low01-1280x720.png`
- `Valorant-20260524-000615-all-900x760.png`
- `Valorant-20260524-000615-avg-900x760.png`
- `Valorant-20260524-000615-low1-900x760.png`
- `Valorant-20260524-000615-low01-900x760.png`
- `Valorant-20260524-000615-tooltip-1280x720.png`
- `Valorant-20260530-220252-all-1280x720.png`
- `Valorant-20260530-220252-avg-1280x720.png`
- `Valorant-20260530-220252-low1-1280x720.png`
- `Valorant-20260530-220252-low01-1280x720.png`
- `Valorant-20260530-220252-all-900x760.png`
- `Valorant-20260530-220252-avg-900x760.png`
- `Valorant-20260530-220252-low1-900x760.png`
- `Valorant-20260530-220252-low01-900x760.png`
- `Valorant-20260530-220252-tooltip-1280x720.png`

WebView2 截图/probe 输出目录：`artifacts\fps-chart-gamepp-style-retest-20260531-021611\webview2-report-probe\outputs`

WebView2 覆盖 3 个 run 的 `all` 视图，尺寸为 1280x720 与 900x760。6 个 WebView2 JSON 均 `success=true`、`pageReady=true`、`overflow=false`、`chartScrollOverflowX=false`、`hasMinOption=false`、`hasMinSeries=false`。

## 1280x720 / 900x760 溢出检查

PASS。

- CDP/Edge 截图探针：24 个 run+视图+尺寸组合均 `overflow=false`、`chartScrollOverflowX=false`。
- WebView2 probe：6 个 run+尺寸组合均 `overflow=false`、`chartScrollOverflowX=false`。
- 图例不重叠：PASS。
- 右侧 Min / Max / Average 数值未裁切：PASS，截图抽检确认可见。
- tooltip 不遮挡到不可读：PASS，tooltip 位于 viewport 内，黑色浮层文本可读。

## 验证命令结果

| 命令 | 结果 | 日志 |
| --- | --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS，57 个 Vitest 测试通过，Vite build 通过 | `artifacts\fps-chart-gamepp-style-retest-20260531-021611\verification\run-frontend-verify.log` |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | `artifacts\fps-chart-gamepp-style-retest-20260531-021611\verification\build-framescope-tests.log` |
| `FrameScopeReportManifestTests.exe` | PASS | `artifacts\fps-chart-gamepp-style-retest-20260531-021611\verification\FrameScopeReportManifestTests.log` |
| `FrameScopeDiagnosticsTests.exe` | PASS | `artifacts\fps-chart-gamepp-style-retest-20260531-021611\verification\FrameScopeDiagnosticsTests.log` |
| `node .\tests\chart-sampling-tests.js` | 默认 PATH `node.exe` 失败：`Access is denied`；bundled Node PASS | `verification\chart-sampling-default-node.log`、`verification\chart-sampling-bundled-node.log` |
| WebView2 report screenshot/probe | PASS，3 个 run x 2 个尺寸 | `webview2-report-probe\outputs\webview2-summary.json` |
| `git diff --check` | PASS，退出码 0；仅既有 LF/CRLF warning，无 whitespace error | `artifacts\fps-chart-gamepp-style-retest-20260531-021611\verification\git-diff-check.log` |
| 残留进程检查 | PASS，`NO_MATCHING_RESIDUAL_PROCESSES` | `artifacts\fps-chart-gamepp-style-retest-20260531-021611\verification\residual-process-check.log` |

默认 PATH Node 记录：

```text
Program 'node.exe' failed to run: Access is denied
```

Bundled Node：

```text
chart-sampling-tests: PASS
NODE=C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe
EXIT_CODE=0
```

## 边界确认

- 未打包。
- 未安装。
- 未启动真实 Valorant / BF6 / CS2 / PUBG / Apex / 任何真实游戏。
- 未修改源码。
- 未做全量软件 QA。
- 仅复制历史 run 到 artifacts。
- 仅重新生成报告 HTML/data/manifest。
- 仅针对 FPS 图表执行截图、tooltip、下拉、bucket/raw 语义和 WebView2 probe。

## 后续建议

建议进入 CPU 电压口径实现板块。理由：FPS 图表 GamePP 风格专项复测已 PASS，raw PresentMon 统计语义与 1 秒 bucket 展示语义未被破坏；可以把下一阶段注意力切到 CPU 电压口径，但本次没有实现或修改 CPU 电压相关源码。
