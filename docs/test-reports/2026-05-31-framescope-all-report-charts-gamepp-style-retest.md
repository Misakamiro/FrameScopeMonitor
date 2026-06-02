# FrameScope 全报告图表 GamePP 风格专项复测报告

日期：2026-05-31

结论：PASS

本轮是专项复测，只读取实施报告、当前代码差异、生成 synthetic 报告数据并运行验证命令。未修改源码，未打包，未安装 FrameScope，未启动真实游戏，未测试 BF6，未推 GitHub，未更新 Release。

## 复测范围核对

- 已读取实施报告：`docs/implementation-reports/2026-05-30-framescope-all-report-charts-gamepp-style-report.md`。
- 实施报告声明的本轮图表改造集中在：
  - `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`
  - `src/reporting/FrameScopeReportGenerator.Html.Styles.cs`
  - `tests/chart-sampling-tests.js`
  - `tools/Probe-ReportHtmlLayout.js`
- 针对上述已跟踪文件的差异统计：`FrameScopeReportGenerator.Html.Scripts.cs`、`FrameScopeReportGenerator.Html.Styles.cs`、`tests/chart-sampling-tests.js` 合计 123 insertions / 37 deletions；`tools/Probe-ReportHtmlLayout.js` 是当前未跟踪 probe 工具。
- 注意：当前工作树整体不是干净的单一图表 diff，`git diff --name-only` 显示 69 个已跟踪文件存在差异，并且还有多批未跟踪历史文件。因此我不能把“整个工作树”描述为只包含本轮图表改造；本轮复测只确认实施报告所列图表渲染、样式、测试、probe 范围，并且本轮我没有改任何源码。

## 数据与证据

本轮生成了一份 synthetic 报告数据，覆盖 raw PresentMon FPS、CPU core、CPU Core VID、后台进程 Top N、系统/GPU、IO、温度：

- Synthetic run：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\docs\test-reports\gamepp-retest-0531\synth-run-pass`
- Synthetic report：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\docs\test-reports\gamepp-retest-0531\synth-run-pass\charts\framescope-interactive-report.html`
- Probe JSON：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\docs\test-reports\gamepp-retest-0531\screenshots\synthetic-report-probe\report-overflow-probe.json`

数据核对：

- raw PresentMon rows：720
- valid PresentMon rows：720
- selectedRows：720
- FPS `bucketMs`：1000
- FPS display buckets：5
- CPU core samples：160
- CPU Core VID samples：160
- CPU Core VID status：`core-vid-available`
- CPU Voltage status：`unavailable`
- CPU Voltage chart series：0
- process count：12，默认图表路径 `PROCESS_TOP_N=10`

## 图表专项结果

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 所有报告图表 GamePP 风格统一 | PASS | `drawGameppArea` / `drawGameppReferenceLines` 被非 FPS 图表共享使用；probe 覆盖 FPS、CPU core、CPU Core VID、性能、系统、进程、IO/温度。 |
| 面积折线图视觉一致 | PASS | 截图显示深色面板、蓝色/分组色面积、网格、右侧当前值标签一致。 |
| 参考线/右侧数值 | PASS | FPS 有 Min/Max/Average 参考线；非 FPS 图表有右侧当前值标签。 |
| Tooltip | PASS | `fps-tooltip-1280x720.png` 显示 FPS、1% Low、0.1% Low 和 `Sample count: 159 frames in 1000 ms bucket`。 |
| CPU core summary | PASS | 默认 `summary` 聚合为 Average / Highest / Lowest core，避免铺满所有核心线；有数据截图通过。 |
| CPU Core VID summary | PASS | 默认 `summary` 聚合为 Average / Highest / Lowest VID；单位 V；note 明确 VID 是请求/目标电压。 |
| Top N 进程 | PASS | 默认 `PROCESS_TOP_N=10`，synthetic 数据有 12 个后台进程，截图显示 Top N 进程图表正常。 |
| IO / 温度单位拆分 | PASS | UI 选项拆为 `diskNet`、`diskLatency`、`power`、`temp`；数据包含 `disk`、`net`、`diskLatency`、`power`、`temp`，未混到同一轴。 |
| 1280x720 / 900x760 窄视口 | PASS | probe 共 21 个场景，`allNoOverflow=True`，全部 `overflow=false` 且 `chartScrollOverflowX=false`。 |

## FPS 回归核对

- PASS：FPS 图表仍保持 GamePP 风格，截图为蓝色面积折线、深色面板、参考线与右侧值标记。
- PASS：`bucketMs=1000`。
- PASS：统计来自 raw PresentMon 帧数据，synthetic 报告 `rawRows=720`、`validRows=720`、`selectedRows=720`。
- PASS：未恢复红色异常点路径；`tests/chart-sampling-tests.js` 明确断言不存在 `drawFpsAnomalyMarkers`、`fpsAnomalyPoints`、`红色异常帧点`、`color='#ff4f78'`。
- PASS：未变成“只看最低瞬时 FPS”；FPS 下拉项只有 `all`、`avg`、`low1`、`low01`，`chart-sampling-tests.js` 断言不存在 `DATA.fps.min` 和最低瞬时 FPS 下拉项。

## CPU 电压口径核对

- PASS：CPU Core VID 仍是请求/目标电压，报告 note 为“VID 是 CPU 请求/目标电压，不是真实 per-core Vcore。”
- PASS：没有把 CPU Core VID 伪装成 GamePP 的 CPU Voltage / Vcore。
- PASS：真实 CPU Voltage / Vcore 仍与 VID 分开；synthetic 报告中 `cpuVid.available=true`、`cpuVoltage.available=false`、`cpuVoltage.series.length=0`。
- PASS：真实 per-core Vcore 不可用时如实显示 `unavailable`；测试日志中也覆盖了 `non-per-core-only` 不能生成 per-core 电压图表的情况。
- 本轮未实现任何电压口径对齐，也未改 CPU VID / Vcore 采集口径。

## 截图路径

截图目录：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\docs\test-reports\gamepp-retest-0531\screenshots\synthetic-report-probe`

- FPS：`fps-default-1280x720.png`
- FPS tooltip：`fps-tooltip-1280x720.png`
- FPS 900x760：`fps-default-900x760.png`
- CPU core：`cpu-core-frequency-1280x720.png`
- CPU Core VID：`cpu-core-vid-900x760.png`
- GPU / performance：`performance-chart-1280x720.png`
- System / GPU usage：`system-usage-900x760.png`
- Process Top N：`background-process-900x760.png`
- IO disk/network：`io-disk-net-1280x720.png`
- Temperature：`io-temperature-900x760.png`
- Probe JSON：`report-overflow-probe.json`

## 验证命令

| 命令 | 结果 | 日志 |
| --- | --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS，57/57 Vitest，通过 Vite build | `docs/test-reports/gamepp-retest-0531/verification/run-frontend-verify.log` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS，`FrameScope tests rebuilt.` | `docs/test-reports/gamepp-retest-0531/verification/build-framescope-tests.log` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | `docs/test-reports/gamepp-retest-0531/verification/FrameScopeReportManifestTests.log` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS | `docs/test-reports/gamepp-retest-0531/verification/FrameScopeDiagnosticsTests.log` |
| 默认 PATH `node .\tests\chart-sampling-tests.js` | 环境问题：WindowsApps `node.exe` 仍是 `Access is denied`，不计产品失败 | `docs/test-reports/gamepp-retest-0531/verification/chart-sampling-default-node.log` |
| bundled Node `tests\chart-sampling-tests.js` | PASS，`chart-sampling-tests: PASS` | `docs/test-reports/gamepp-retest-0531/verification/chart-sampling-bundled-node.log` |
| bundled Node `tools\Probe-ReportHtmlLayout.js --report <synthetic report> --diagnostic <synthetic report> --out <screenshots>` | PASS，21 个场景，`allNoOverflow=True` | `docs/test-reports/gamepp-retest-0531/verification/probe-report-html-layout-synthetic.log` |
| `git diff --check` | PASS，退出码 0；只有既有 LF/CRLF warning，无 whitespace error | `docs/test-reports/gamepp-retest-0531/verification/git-diff-check.log` |
| 残留进程检查 | PASS，`NO_MATCHING_RESIDUAL_PROCESSES` | `docs/test-reports/gamepp-retest-0531/verification/residual-process-check.log` |

## 边界确认

- 是否所有报告图表都完成 GamePP 风格统一：是，基于 synthetic 有数据报告、probe 截图、chart sampling 断言确认。
- 是否 FPS 图表保持 raw 统计语义和 `bucketMs=1000`：是。
- 是否 CPU Core VID / CPU Voltage 口径仍然分开：是。
- 是否 1280x720 和 900x760 截图/probe 无明显布局问题：是，`allNoOverflow=True`，人工查看关键截图未见明显重叠或横向溢出。
- 是否有任何源码修改：本轮复测没有修改源码；只新增 `docs/test-reports/gamepp-retest-0531/` 证据目录和本报告。当前工作树已有大量既存源码差异，未由本轮复测引入。
- 是否有打包、安装、真实游戏、GitHub、Release 行为：没有。

最终结论：PASS。
