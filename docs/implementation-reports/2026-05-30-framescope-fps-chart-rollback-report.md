# FrameScope FPS 图表回退实现报告

日期：2026-05-30

## 范围

本次只回退 FPS 图表显示口径；没有打包、没有安装、没有启动真实游戏、没有推 GitHub、没有更新 Release、没有做全量 QA。

已先阅读最近三份相关报告：

- `docs\implementation-reports\2026-05-27-framescope-report-chart-data-implementation-report.md`
- `docs\implementation-reports\2026-05-29-framescope-report-raw-chart-data-fps-dropdown-report.md`
- `docs\implementation-reports\2026-05-30-framescope-fps-chart-readability-fix-report.md`

## 回退实现

1. FPS 图表显示回退为 1 秒显示桶：
   - `src\reporting\FrameScopeReportGenerator.Analysis.cs` 中的 FPS 图表数据改为 `BuildBucketedFps(...)`。
   - 输出 `bucketMs=1000`、`lowWindowMs=2000`、`t`、`avg`、`low1`、`low01`。
   - 不再把全部 raw PresentMon FPS 点直接画到图表上。

2. 统计值仍来自原始 PresentMon 帧数据：
   - `frameStats.average`、`frameStats.low1`、`frameStats.low01` 继续从 `presentmon.csv` 的原始帧时间计算。
   - 本次没有改 PresentMon 原始数据读取。

3. 红色异常点和最低异常帧点图表逻辑已移除：
   - `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs` 不再保留 `fpsDisplayDomainMax`、`fpsAnomalyPoints`、`drawFpsAnomalyMarkers`。
   - FPS 图例、tooltip、绘制路径不再加入红色异常帧点 overlay。
   - 图表数据不再输出 `DATA.fps.min`。
   - FPS 下拉不再出现 `value=min`，没有恢复“只看最低瞬时 FPS”。

4. FPS 下拉保留 4 项：
   - 平均 FPS / 1% Low / 0.1% Low
   - 只看平均 FPS
   - 只看 1% Low
   - 只看 0.1% Low

5. 定向测试已更新：
   - `tests\chart-sampling-tests.js` 检查不再出现 raw dense mode、robust display domain、红色异常点、`DATA.fps.min` 和最低瞬时 FPS 下拉项。
   - `tests\FrameScopeReportManifestTests.cs` 检查 FPS 图表显示为 1 秒 bucket，同时 raw frame statistics 仍匹配原始 PresentMon 帧数据。

未改动的边界：CPU Core VID、真实 Vcore、全局采样间隔、per-target sampling、主题、托盘、日志目录、silent no-csv 分类、报告目标名显示。

## CS2 历史 run 验证

原始 run：

`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260505-101253`

复制后的临时 run：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\fps-chart-rollback-20260530\Counter-Strike-2-20260505-101253-copy`

重新生成报告：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\fps-chart-rollback-20260530\Counter-Strike-2-20260505-101253-copy\charts\framescope-interactive-report.html`

关键数据：

- 原始 PresentMon 帧数：`17472`
- FPS 图表显示点数：`90`
- `fps.bucketMs`：`1000`
- `fps.lowWindowMs`：`2000`
- `DATA.fps.min`：不存在
- raw 统计值：平均 FPS `195.3`，1% Low `108.01`，0.1% Low `30.92`
- raw 瞬时最高 FPS：`1739.43`
- 图表显示最大值：平均 FPS `198.92`，1% Low `163.93`，0.1% Low `158.73`

结论：raw 异常尖峰不再把 FPS 图表 Y 轴拉爆，图表显示回到稳定、清楚、可读的 bucket 口径。

## FPS 截图

1280x720：

- `artifacts\fps-chart-rollback-20260530\fps-chart-screenshots\fps-all-1280x720.png`
- `artifacts\fps-chart-rollback-20260530\fps-chart-screenshots\fps-avg-1280x720.png`
- `artifacts\fps-chart-rollback-20260530\fps-chart-screenshots\fps-low1-1280x720.png`
- `artifacts\fps-chart-rollback-20260530\fps-chart-screenshots\fps-low01-1280x720.png`

900x760：

- `artifacts\fps-chart-rollback-20260530\fps-chart-screenshots\fps-all-900x760.png`
- `artifacts\fps-chart-rollback-20260530\fps-chart-screenshots\fps-avg-900x760.png`
- `artifacts\fps-chart-rollback-20260530\fps-chart-screenshots\fps-low1-900x760.png`
- `artifacts\fps-chart-rollback-20260530\fps-chart-screenshots\fps-low01-900x760.png`

截图探针结果：

- 8 张 FPS 视图截图全部 `overflow=false`
- 8 张 FPS 视图截图全部 `redPixelCount=0`
- 8 张 FPS 视图截图全部 `hasMinSeries=false`
- 8 张 FPS 视图截图全部 `hasMinOption=false`
- 8 张 FPS 视图截图全部 `duplicateTickLabels=false`
- 1280x720 和 900x760 均无横向溢出

WebView2 report probe 复跑截图：

- `artifacts\fps-chart-rollback-20260530\webview2-report-probe\webview2-fps-all-1280x720-rerun.png`
- evidence：`artifacts\fps-chart-rollback-20260530\webview2-report-probe\webview2-fps-all-1280x720-rerun.json`
- 结果：`success=true`，`pageLoaded=true`，`pageReady=true`，`overflow=false`，`redPixelCount=0`，`fpsBucketMs=1000`，`fpsPointCount=90`，`rawFrameCount=17472`，`hasMinOption=false`，`hasMinSeries=false`

## 验证命令结果

| 命令 | 结果 |
| --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS，exit 0；typecheck PASS，Vitest 5 files / 57 tests PASS，Vite build PASS |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS，exit 0；`FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS，exit 0；`FrameScopeReportManifestTests: PASS` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS，exit 0；`FrameScopeDiagnosticsTests: PASS` |
| `node .\tests\chart-sampling-tests.js` | 默认 PATH 命中 WindowsApps `node.exe`，失败：`Access is denied` |
| bundled Node + `node .\tests\chart-sampling-tests.js` | PASS，exit 0；Node 路径：`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`；输出 `chart-sampling-tests: PASS` |
| WebView2 report probe | PASS，exit 0；截图和 JSON 见上方路径 |
| `git diff --check` | PASS，exit 0；仅输出既有 LF/CRLF 换行提示，没有 whitespace error |

## 残留进程检查

检查范围包含：

`FrameScopeReportGenerator.exe`、`FrameScopeDiagnosticsTests.exe`、`FrameScopeReportManifestTests.exe`、`FrameScopeMonitor.exe`、`FrameScopeNativeMonitor.exe`、`FrameScopeSystemSampler.exe`、`FrameScopeProcessSampler.exe`、`PresentMon.exe`、`WebView2ReportProbe.exe`、`msedge.exe`、`msedgewebview2.exe`

结果：

- FrameScope / PresentMon / WebView2ReportProbe 相关残留进程数：`0`
- 系统中仍有 unrelated `msedgewebview2.exe`，宿主为 `clash-verge` 和 Windows `SearchHost`，不是本次验证残留。

## 11 项问题回答

1. 回退到了什么 FPS 图表显示状态：回退到 1 秒 bucket 的稳定显示口径，图表显示 `avg` / `low1` / `low01`，不直接绘制全部 raw FPS 点。
2. 是否去掉红色异常点：是，截图探针 8 个视图 `redPixelCount=0`，脚本中不再保留红色异常点绘制路径。
3. 是否移除最低异常帧点相关逻辑：是，移除了 FPS 图表 `min` series、红色异常点 overlay、最低异常点 tooltip/legend/绘制逻辑。
4. 是否保留 4 个 FPS 下拉项：是，保留 `all`、`avg`、`low1`、`low01` 四项。
5. 是否没有恢复“只看最低瞬时 FPS”：是，没有 `value=min` 下拉项，也没有 `DATA.fps.min` 图表序列。
6. average / 1% Low / 0.1% Low 是否仍从原始 PresentMon 数据计算：是；CS2 run 原始 `17472` 帧重新计算结果与报告统计值一致：`195.3` / `108.01` / `30.92`。
7. CS2 历史 run 重新生成后的截图路径：见“FPS 截图”章节 8 张 PNG，以及 WebView2 复跑截图。
8. 1280x720 和 900x760 是否无溢出：是，FPS 截图探针和 layout probe 均显示无横向溢出。
9. 所有验证命令结果：见“验证命令结果”表。
10. 残留进程检查结果：FrameScope / PresentMon / WebView2ReportProbe 残留为 `0`；仅存在 unrelated WebView2 系统宿主。
11. 是否建议进入单独 FPS 图表复测窗口：建议进入单独 FPS 图表复测窗口，用更多历史 runs 和更多窗口尺寸做专项复测；本窗口已完成指定 CS2 历史 run 的定向回退验证，不建议在本窗口扩展成全量 QA、打包或安装验证。
