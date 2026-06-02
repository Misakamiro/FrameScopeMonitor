# FrameScope 全报告图表 GamePP 风格统一改造报告

日期：2026-05-31

范围：只改报告 HTML 图表 UI 与图表前端渲染逻辑。未打包、未安装 FrameScope、未启动真实游戏、未推 GitHub、未更新 Release。

## 结论

PASS：FrameScope 报告页内已有图表已统一到 GamePP 风格渲染路径，不再保留旧图表样式或样式切换入口。FPS 保持前一轮 GamePP 风格与 raw PresentMon 统计语义；本轮没有恢复红色异常帧点、没有恢复“只看最低瞬时 FPS”、没有把所有 raw FPS 点直接画到图上。

建议进入单独图表复测窗口：建议。原因是本轮已经覆盖指定 CS2 历史 run、截图和自动化验证，但该历史 run 没有 CPU core samples 和 CPU Core VID 数据；单独复测窗口可以补一组带 CPU 核心频率 / VID 数据的 synthetic 或真实采样 run，专门复核有数据状态下的多核心 tooltip 与摘要曲线。

## 主要改动

- `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`
  - 新增共享 GamePP 面积折线渲染：深色背景、蓝色/分组色面积填充、清晰网格线、顶部图例、黑色 tooltip、右侧数值标记、虚线参考线。
  - 非 FPS 图表统一走 `drawGameppArea` / `drawGameppReferenceLines`。
  - `PAD.r` 扩大到 98，为右侧数值标记留空间。
  - 时间轴改为可读 `HH:MM:SS` / 秒级短时长格式。
  - 系统占用图固定 Y 轴 0-100。
  - CPU 核心频率默认显示 Average / Highest / Lowest core，不默认铺满全部核心线。
  - CPU Core VID 默认显示 Average / Highest / Lowest VID，并在说明里继续明确 VID 是 CPU 请求/目标电压，不是真实 Vcore。
  - 后台进程默认只画 Top 10；搜索时最多放宽到 24 条，表格继续保留完整排序信息。
  - IO/温度拆成不同单位模式：Disk / network MB/s、Disk latency ms、GPU power W、GPU temperature C，不把温度、电力、吞吐、延迟强塞进同一轴。
  - CPU Voltage / Vcore 没有暴露为用户可见图表 tab。

- `src/reporting/FrameScopeReportGenerator.Html.Styles.cs`
  - 图表容器保持深色 GamePP 面板，tooltip 改为黑色高对比样式。
  - 图表默认宽度允许在窄视口内收缩：`min-width:min(900px,100%)`。
  - 工具栏、图例、输入控件增加 `max-width` / `overflow-wrap`，避免 900x760 与 1280x720 下横向溢出。

- `tests/chart-sampling-tests.js`
  - 锁定非 FPS 图表共享 GamePP 面积图和参考线。
  - 锁定系统占用 0-100 Y 轴。
  - 锁定 CPU 核心频率 / VID 默认 summary。
  - 锁定后台进程 Top N。
  - 锁定 IO 默认单单位视图，且不再合并 power/temp 混合轴。
  - 继续锁定 FPS 不恢复 raw dense mode、红色异常点、最低瞬时 FPS 下拉项和 `DATA.fps.min`。

- `tools/Probe-ReportHtmlLayout.js`
  - 覆盖 FPS、CPU 核心频率、CPU Core VID、性能图表、系统占用、后台进程、IO/温度、tooltip、1280x720、900x760 的截图与溢出 probe。

## 必答项

1. 是否所有报告图表都改成 GamePP 风格：是。报告页已有图表，包括 FPS、CPU 核心频率、CPU Core VID、性能图表、系统占用、后台进程、IO/温度和诊断报告视图，都走统一深色 GamePP 图表外观。
2. 是否不保留旧图表样式：是。没有新增样式切换；旧 raw dense 图表模式没有对外暴露；非 FPS 图表统一使用共享 GamePP 渲染函数。
3. FPS 是否保持前一轮 GamePP 风格：是。FPS 仍使用 1 秒 bucket 展示、蓝色面积折线、Min / Max / Average 参考线、1% Low / 0.1% Low 图例和 sample-count tooltip；raw PresentMon 统计语义未改。
4. CPU 核心频率如何避免多线混乱：默认不画 16 条核心线，而是用 `aggregateCoreSeries` 聚合成 Average core、Highest core、Lowest core 三条摘要线，单位 MHz；tooltip 在有 per-core 数据时仍列出每核心 MHz。
5. CPU Core VID 是否仍明确为请求/目标电压：是。CPU Core VID 图表标题独立，单位 V，说明保留“VID 是 CPU 请求/目标电压，不是真实 per-core Vcore”，并且不与 CPU Voltage / Vcore 混在一起。
6. 性能/系统/后台进程/IO/温度图表是否统一：是。性能和系统占用使用统一面积折线、图例 Cur / Avg / Peak、右侧当前值标记；系统占用固定 0-100%；后台进程默认 Top 10；IO/温度按单位拆分。
7. 1280x720 / 900x760 是否无横向溢出：是。`report-overflow-probe.json` 显示 `allNoOverflow=True`，21 个场景的 `overflow=false` 且 `chartScrollOverflowX=false`。
8. 是否建议进入单独图表复测窗口：建议。建议只做图表复测，不做打包、安装、真实游戏启动、GitHub push 或 Release 更新。

## CS2 历史 run 验证

原始 run 未覆盖：

`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260505-101253`

复制后的 artifact：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\Counter-Strike-2-20260505-101253-copy`

重新生成报告：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\Counter-Strike-2-20260505-101253-copy\charts\framescope-interactive-report.html`

数据核对：

- raw frames：17472
- `bucketMs`：1000
- FPS bucket 点数：90
- `frameStats.average`：195.30
- `frameStats.low1`：108.01
- `frameStats.low01`：30.92
- `frameStats.minInstant`：6.683
- `frameStats.maxInstant`：1739.433
- `cpuCore.available`：false
- `cpuVid.available`：false
- 说明：该历史 CS2 run 没有 CPU 核心频率和 CPU Core VID 样本，所以对应截图验证的是无数据状态；代码路径和单元测试覆盖了有数据状态下的 summary 曲线。

## 截图路径

截图目录：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots`

- FPS 图表：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\fps-default-1280x720.png`
- FPS tooltip：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\fps-tooltip-1280x720.png`
- CPU 核心频率：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\cpu-core-frequency-1280x720.png`
- CPU Core VID 无数据状态：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\cpu-core-vid-1280x720.png`
- 性能图表：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\performance-chart-1280x720.png`
- 系统占用：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\system-usage-1280x720.png`
- 后台进程：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\background-process-1280x720.png`
- IO / 磁盘网络：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\io-disk-net-1280x720.png`
- IO / 温度：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\io-temperature-1280x720.png`
- 1280x720 诊断视图：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\diagnostic-report-1280x720.png`
- 900x760 FPS：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\fps-default-900x760.png`
- 900x760 CPU 核心频率：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\cpu-core-frequency-900x760.png`
- 900x760 CPU Core VID：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\cpu-core-vid-900x760.png`
- 900x760 性能图表：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\performance-chart-900x760.png`
- 900x760 系统占用：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\system-usage-900x760.png`
- 900x760 后台进程：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\background-process-900x760.png`
- 900x760 IO / 磁盘网络：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\io-disk-net-900x760.png`
- 900x760 IO / 温度：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\io-temperature-900x760.png`
- 900x760 诊断视图：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\diagnostic-report-900x760.png`
- 溢出 probe JSON：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\all-report-charts-gamepp-style-20260531-030915\screenshots\report-overflow-probe.json`

Tooltip 实测文本：

```text
00:00:48
FPS: 195.80 FPS
1% Low: 156.25 FPS
0.1% Low: 151.52 FPS
Sample count: 196 frames in 1000 ms bucket
```

## 验证命令结果

| 验证项 | 结果 | 证据 |
| --- | --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | `artifacts\all-report-charts-gamepp-style-20260531-030915\verification\run-frontend-verify.log`，57 个 Vitest 测试通过，Vite build 通过，`EXIT_CODE=0`。该脚本按项目既有设计执行了 `npm ci`，未安装 FrameScope 应用、未打包、未更新 Release。 |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | `artifacts\all-report-charts-gamepp-style-20260531-030915\verification\build-framescope-tests.log`，`FrameScope tests rebuilt.`，`EXIT_CODE=0`。 |
| `FrameScopeReportManifestTests.exe` | PASS | `artifacts\all-report-charts-gamepp-style-20260531-030915\verification\FrameScopeReportManifestTests.log`，`FrameScopeReportManifestTests: PASS`，`EXIT_CODE=0`。 |
| `FrameScopeDiagnosticsTests.exe` | PASS | `artifacts\all-report-charts-gamepp-style-20260531-030915\verification\FrameScopeDiagnosticsTests.log`，`FrameScopeDiagnosticsTests: PASS`，`EXIT_CODE=0`。 |
| `node .\tests\chart-sampling-tests.js` | 默认 PATH Node 失败，bundled Node PASS | 默认 WindowsApps `node.exe` 报 `Access is denied`，记录在 `chart-sampling-default-node.log`；使用 `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe` 后 `chart-sampling-tests: PASS`，`EXIT_CODE=0`。 |
| WebView2 report screenshot/probe | 部分受限，Edge/CDP probe PASS | Edge/CDP 截图与 layout probe 已完成，`report-overflow-probe.json` 显示 `allNoOverflow=True`。Codex in-app Browser 访问本地 `file://` 报告被安全策略阻止，未绕过，记录在 `browser-file-url-policy.log`。 |
| `git diff --check` | PASS | `artifacts\all-report-charts-gamepp-style-20260531-030915\verification\git-diff-check.log`，退出码 0；仅有既有 LF/CRLF warning，没有 whitespace error。 |
| 残留进程检查 | PASS | `artifacts\all-report-charts-gamepp-style-20260531-030915\verification\residual-process-check.log`，`NO_MATCHING_RESIDUAL_PROCESSES`，`EXIT_CODE=0`。 |

## 边界确认

- 未打包。
- 未安装 FrameScope 应用。
- 未启动 CS2 / Valorant / BF6 / 任何真实游戏。
- 未推 GitHub。
- 未更新 Release。
- 未改 CPU VID / Vcore 数据采集口径。
- 未把 CPU Core VID 与 CPU Voltage / Vcore 混成同一图表。
- 未恢复红色异常帧点。
- 未恢复“只看最低瞬时 FPS”。
- 未把所有 raw FPS 点直接画到 FPS 图上。
- 未恢复旧图表样式切换。
