# FrameScope FPS 图表可读性修复报告

日期：2026-05-30
范围：只修复 FPS 报告图表显示可读性；未打包、未安装、未启动真实游戏。

## 结论

本次修复保持了 raw PresentMon 帧数据语义，没有恢复 1s bucket，没有改 average FPS / 1% Low / 0.1% Low 的统计语义，也没有恢复“只看最低瞬时 FPS”选项。坏图场景已用 synthetic 短时长报告复现并覆盖：1.79s 原始帧数据中包含 3856.998 FPS 极端 spike、正常 100-900 FPS 曲线和 3 个 slow frames。

修复后，synthetic 报告的 FPS 图表显示域为 1022.70224，而原始最大瞬时 FPS 仍保留为 3856.998；X 轴显示 `0.0s / 0.5s / 1.0s / 1.5s`；红色异常帧点数量为 3，值为 `58 / 42 / 65`；1280x720 和 900x760 均无横向溢出。

## 坏图根因

1. Y 轴根因：旧图表把 raw instant FPS 极端高点纳入 Y 轴 max。截图里的 3000+ FPS spike 把 Y 轴拉到几千，导致平均 FPS / 1% Low / 0.1% Low 曲线被压在底部。
2. X 轴根因：短报告也使用按秒四舍五入的 `m:ss` 标签，并且 tick 较固定，1-2 秒报告会重复显示 `0:00 / 0:01`。
3. 红点根因：旧异常点策略过宽，接近 `avg * .85` 的普通波动也会被标红，并且上限偏高，视觉上像噪点。

## 实现变更

### Y 轴 display domain

在 `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs` 中新增 `fpsDisplayDomainMax()`。它只影响显示域，不改数据源和统计：

- 收集当前可见 FPS 绘制点和 raw `DATA.fps.min`。
- 使用四分位/IQR fence、p95 和 padding 计算 robust display max。
- 当极端高 spike 超过正常区间时，不让它决定 Y 轴。
- 绘制时 `y()` 会把超过 display max 的值 clamp 到顶部边界，tooltip / `DATA.fps.min` 仍保留真实 raw 值。

### X 轴短时长格式

新增 `formatTimeTick()`、`niceTimeStep()`、`timeTicks()`：

- 5 秒以内和 30 秒以内使用小数秒，例如 `0.0s`、`0.5s`、`1.0s`。
- 长时长继续使用 `m:ss` / `h:mm:ss`。
- tick 数按画布宽度限制在 3-8 个，避免短报告挤满或重复。

### 红色异常帧点

新增 `fpsAnomalyPoints()` 并收紧 `drawFpsAnomalyMarkers()`：

- 异常点只来自低 FPS / 高 frame time 的 slow frames。
- 优先用全局 1% Low / 0.1% Low 阈值附近的低帧筛选。
- 没有 Low 阈值时，退回明显低于局部/全局基线的点。
- 按像素宽度限制最大点数，当前上限 8-36。
- 点半径和 glow 降低，避免压过主曲线。
- 所有 FPS 视图默认叠加同一批红色 slow-frame 点。

### FPS 下拉项

FPS 下拉保持 4 项：

- 平均 FPS / 1% Low / 0.1% Low
- 只看平均 FPS
- 只看 1% Low
- 只看 0.1% Low

没有恢复“只看最低瞬时 FPS”。`buildSeries()` 仍只绘制 `avg`、`low1`、`low01` 三类曲线；raw `min` 只用于异常点和 tooltip 真实值。

### 响应式宽度

为避免 900x760 等窄视口横向溢出：

- `.chartbox` 改为 `min-width:min(900px,100%)`。
- `applyWidth()` 默认使用 `Math.max(minWidth, Math.round(base * scale))`，只有用户主动放大宽度滑块时才横向扩展。

## Raw data 语义确认

保持：

- FPS 图表数据仍来自原始 PresentMon 帧行。
- `average FPS / 1% Low / 0.1% Low` 仍从 raw frame data 计算。
- `DATA.fps.t / avg / low1 / low01 / min` 点数继续等于 raw 帧数。
- tooltip 和诊断数据仍能看到 raw instant FPS spike。

没有恢复：

- 没有恢复 1s bucket。
- 没有输出 `bucketMs`。
- 没有输出 `lowWindowMs`。
- 没有恢复最低瞬时 FPS 下拉项。

`tests/FrameScopeReportManifestTests.cs` 已把 raw spike 加入回归测试：第 5 个 raw `min` 点保持 4000 FPS，平均 FPS 仍按 raw frame time 重算为 90.81，同时断言没有 `bucketMs` / `lowWindowMs`。

## Synthetic 覆盖

更新 `tests/chart-sampling-tests.js`：

- 构造 180 个 raw FPS 点，时间范围约 1.79s。
- 正常 FPS 曲线约 100-900。
- 注入 3857 FPS 极端高 spike。
- 注入 slow frames：58、42、65 FPS。
- 断言 display max 大于 850 且小于 1500。
- 断言短时长 tick 不退化为重复 `0:00 / 0:01`。
- 断言异常点数量受控，且只标低 FPS，不标 3000+ spike。
- 断言 FPS 下拉没有 `<option value=min>`。

## 截图和探针证据

Synthetic 报告：

- `artifacts/fps-chart-readability-20260530/synthetic-short-spike-run/charts/framescope-interactive-report.html`
- `artifacts/fps-chart-readability-20260530/synthetic-short-spike-run/charts/framescope-interactive-data.js`

截图证据：

- `artifacts/fps-chart-readability-20260530/screenshots/fps-all-1280x720.png`
- `artifacts/fps-chart-readability-20260530/screenshots/fps-avg-1280x720.png`
- `artifacts/fps-chart-readability-20260530/screenshots/fps-low1-1280x720.png`
- `artifacts/fps-chart-readability-20260530/screenshots/fps-low01-1280x720.png`
- `artifacts/fps-chart-readability-20260530/screenshots/fps-all-900x760.png`
- `artifacts/fps-chart-readability-20260530/screenshots/fps-avg-900x760.png`
- `artifacts/fps-chart-readability-20260530/screenshots/fps-low1-900x760.png`
- `artifacts/fps-chart-readability-20260530/screenshots/fps-low01-900x760.png`

探针证据：

- `artifacts/fps-chart-readability-20260530/screenshots/fps-chart-readability-probe.json`
- `artifacts/fps-chart-readability-20260530/layout-probe-rerun/report-overflow-probe.json`
- `artifacts/fps-chart-readability-20260530/webview2-report-probe/webview2-fps-all-1280x720.json`
- `artifacts/fps-chart-readability-20260530/webview2-report-probe/webview2-fps-all-1280x720.png`

关键探针结果：

- `maxRawInstantFps`: 3856.998
- `displayMax`: 1022.7022400000001
- `tickLabels`: `0.0s`, `0.5s`, `1.0s`, `1.5s`
- `anomalyCount`: 3
- `anomalyValues`: 58, 42, 65
- `dropdownOptions`: `all`, `avg`, `low1`, `low01`
- `hasBucketMs`: false
- `hasLowWindowMs`: false
- `overflow`: false
- WebView2 `nonBlankSamples`: 667
- Layout probe `allNoOverflow`: true

截图目视检查结论：

- 三线图没有被 3000+ spike 压扁。
- Y 轴约 0-1023，正常曲线可读。
- X 轴使用小数秒，未重复成一排 `0:00 / 0:01`。
- 红点只有少量 slow frames，不是满屏噪点。
- 900x760 无横向溢出。

## 验证命令结果

1. `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
   - PASS
   - typecheck PASS
   - Vitest: 5 files, 57 tests passed
   - Vite build PASS

2. `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
   - PASS
   - 输出：`FrameScope tests rebuilt.`

3. `.\tests\FrameScopeReportManifestTests.exe`
   - PASS
   - 输出：`FrameScopeReportManifestTests: PASS`

4. `.\tests\FrameScopeDiagnosticsTests.exe`
   - PASS
   - 输出：`FrameScopeDiagnosticsTests: PASS`

5. `node .\tests\chart-sampling-tests.js`
   - 默认 PATH 解析到 `C:\Program Files\WindowsApps\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\app\resources\node.exe`
   - 默认 node 运行失败：`Access is denied`
   - 按要求将 bundled Node 放到 PATH 前面后重跑：
     - Node: `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`
     - PASS
     - 输出：`chart-sampling-tests: PASS`

6. WebView2 report screenshot/probe
   - PASS
   - WebView2 加载 synthetic report 成功，截图成功，probe success true。
   - WebView2 probe 编译有 `WindowsBase` 版本冲突 warning，但 0 errors，不影响运行。

7. `node .\tools\Probe-ReportHtmlLayout.js --report ... --diagnostic ... --out .\artifacts\fps-chart-readability-20260530\layout-probe-rerun`
   - PASS
   - 输出：`artifacts\fps-chart-readability-20260530\layout-probe-rerun\report-overflow-probe.json`
   - `allNoOverflow`: true

8. `git diff --check`
   - PASS，退出码 0。
   - 仅输出当前脏工作区已有的 LF -> CRLF warning；未发现 trailing whitespace 或 conflict marker。

9. 残留进程检查
   - PASS
   - 聚焦 `FrameScopeReportWebView2Probe`、`framescope-report-probe-*`、`FrameScopeReportGenerator`、`PresentMon`、`WebView2ReportProbe`。
   - 输出：`NO_TASK_RESIDUAL_PROCESSES`

## 边界确认

- 未打包。
- 未安装。
- 未启动 Valorant / BF6 / 任何真实游戏。
- 未改 CPU VID / Vcore 功能。
- 未改全局采样间隔逻辑。
- 未把 FPS 图表回退到 1s bucket。
- 未把降采样作为数据源；图表统计仍来自 raw frame data，显示层只做可读性域控制。

## 是否建议进入单独复测窗口

建议进入单独复测窗口。原因是本窗口已经做了实现和验证，且源码树本身有大量既有未提交改动；单独复测窗口可以按“只测试不改代码”的方式重新跑截图、WebView2 probe、raw data payload 检查和目标命令，给出独立 PASS/PARTIAL/FAIL 结论。
