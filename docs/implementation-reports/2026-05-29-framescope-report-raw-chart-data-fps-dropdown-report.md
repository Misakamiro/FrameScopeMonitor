# FrameScope 报告原始图表数据与 FPS 下拉修正报告

结论：PASS

## 实现结论

1. 已彻底移除本次目标范围内的 1s bucket 图表数据生成口径：
   - FPS 不再写入 `bucketMs` / `lowWindowMs`。
   - CPU 核心频率、CPU 电压、CPU Core VID 不再写入 `displayBucketMs`。
   - 报告数据生成阶段不再按 1000ms 整数秒聚合这些图表序列。

2. 已改为 raw/original data 的图表：
   - FPS：来自原始 PresentMon 帧行。
   - 性能、系统、IO：来自原始 `system-samples.csv` 行。
   - 后台进程：来自原始 `process-samples.csv` 采样行。
   - CPU 核心频率：来自原始 `cpu-core-samples.csv` 采样时间点。
   - CPU Core VID：来自原始 `cpu-vid-samples.csv` 采样时间点，仍标注为请求/目标电压。
   - CPU 电压：来自原始 `cpu-voltage-samples.csv` 真实 per-core voltage 采样时间点；无真实 per-core Vcore 时仍不造假。

3. FPS 下拉最终选项：
   - 平均 FPS / 1% Low / 0.1% Low
   - 只看平均 FPS
   - 只看 1% Low
   - 只看 0.1% Low

4. “只看最低瞬时 FPS”已从下拉和独立图表模式中删除。红色异常帧点不再作为下拉选项存在。

5. 红色异常帧点：
   - 来源为原始 PresentMon 帧行的 instant FPS。
   - 在组合视图、只看平均 FPS、只看 1% Low、只看 0.1% Low 中默认叠加显示。
   - tooltip 在 FPS 视图中保留原始时间点对应的红色异常帧点值。

6. FPS 统计仍使用原始 PresentMon 帧数据计算：
   - average FPS、1% Low、0.1% Low 的统计值没有改成绘制层抽样结果。

## 主要修改

- `src/reporting/FrameScopeReportGenerator.Analysis.cs`
  - `BucketFps(...)` 改为 `BuildRawFps(...)`。
  - FPS `t/avg/low1/low01/min` 按原始帧时间点输出。

- `src/reporting/FrameScopeReportGenerator.SystemData.cs`
  - CPU core / CPU voltage / CPU VID 从整数秒 bucket 改为原始 `ElapsedMs` 或 `Time` 时间点。
  - 移除 `displayBucketMs` 输出。

- `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`
  - FPS 下拉恢复四个目标选项。
  - FPS 曲线恢复 average / 1% Low / 0.1% Low 渲染能力。
  - 红色异常帧点作为所有 FPS 视图默认 overlay。
  - canvas 绘制层继续保留像素级抽样/缓存，但不改变 `DATA` 原始序列。

- `tests/FrameScopeReportManifestTests.cs`
  - 增加 raw FPS 点数、无 bucket metadata、CPU raw 时间点断言。

- `tests/chart-sampling-tests.js`
  - 增加 FPS 下拉选项、删除最低瞬时 FPS 模式、异常点 overlay、CPU VID 图表入口断言。

## Synthetic 验证

验证目录：

- `artifacts/report-raw-data-20260529/synthetic-240`
- `artifacts/report-raw-data-20260529/synthetic-large-20000`

240 帧报告检查结果：

- PresentMon 原始帧：240 行。
- `DATA.fps.t/avg/low1/low01/min`：均为 240 点。
- spike 原始时间和值：第 60 帧 `t=0.5s`，`min=25 FPS`。
- `frameStats.average=131.82`，`low1=17.14`，`low01=12.50`，均按原始帧重新计算匹配。
- `DATA.system.t=6`，`DATA.process.t=6`，`DATA.cpuCore.t=6`，`DATA.cpuVoltage.t=6`，`DATA.cpuVid.t=6`。

大样本抽查：

- synthetic-large-20000 生成成功。
- `framescope-interactive-data.js` 约 740 KB。
- Edge headless 截图 `large-report-fps.png` 生成成功，非空，页面未空白或崩溃。

## 截图证据

- FPS 下拉四项：`artifacts/report-raw-data-20260529/screenshots/fps-dropdown.png`
- 平均 FPS / 1% Low / 0.1% Low + 红色异常点：`artifacts/report-raw-data-20260529/screenshots/fps-all.png`
- 只看平均 FPS + 红色异常点：`artifacts/report-raw-data-20260529/screenshots/fps-avg.png`
- 只看 1% Low + 红色异常点：`artifacts/report-raw-data-20260529/screenshots/fps-low1.png`
- 只看 0.1% Low + 红色异常点：`artifacts/report-raw-data-20260529/screenshots/fps-low01.png`
- CPU Core VID 正常：`artifacts/report-raw-data-20260529/screenshots/cpu-vid.png`

## 验证命令

已通过：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
- `.\tests\FrameScopeReportManifestTests.exe`
- `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`
- `.\tests\FrameScopeConfigStoreTests.exe`
- `.\tests\FrameScopeWebBridgeTests.exe`
- `.\tests\FrameScopeNativeMonitorChildProcessTests.exe`
- `.\tests\FrameScopeProcessSamplerTests.exe`
- `.\tests\FrameScopeDiagnosticsTests.exe`
- `.\tests\FrameScopeLoggingPolicyTests.exe`
- `.\tests\FrameScopePresentMonDiagnosticsTests.exe`
- `.\tests\FrameScopeProcessCleanupTests.exe`
- `.\tests\FrameScopeNativeWatcherPolicyTests.exe`
- `.\tests\FrameScopeWebHostLifecycleTests.exe`
- `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tests\chart-sampling-tests.js`
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`
- WebView2 live smoke：`success=True`，`pageLoaded=True`，`pageReady=True`
- WebView2 reduced-motion smoke：`success=True`，`pageLoaded=True`，`pageReady=True`，`reducedMotion=True`
- `git diff --check`：无空白错误；仅输出 LF/CRLF 提示。
- 残留进程检查：未发现 FrameScope/ReportGenerator/Sampler/RenderProbe/msedge 残留进程。

## 边界确认

- 未测试 BF6。
- 未启动真实游戏。
- 未运行安装器。
- 未推 GitHub。
- 未更新 Release。
- `TelemetrySampleIntervalMs`、Settings / 目标列表全局采样间隔、日志诊断策略、CPU Core VID 与真实 CPU 电压分离语义未改动。

## 是否建议进入复测窗口

建议进入复测窗口。当前实现、单元/集成验证、synthetic report、WebView2 live/reduced-motion smoke 和截图证据均通过；复测窗口可以聚焦真实历史大报告打开体验和 tooltip 交互手感，不需要重新打开真实游戏。
