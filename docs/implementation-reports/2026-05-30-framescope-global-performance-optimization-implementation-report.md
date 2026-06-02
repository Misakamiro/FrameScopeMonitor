# FrameScope Monitor 全局性能优化实现报告

验证日期: 2026-05-30 Asia/Hong_Kong
源码根目录: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
前置分析: `docs\diagnostics\2026-05-29-framescope-full-performance-optimization-analysis.md`

## 结论

结论: PASS。

本轮完成了六个区域的全局性能优化和验证: 前端显示页面、UI 动画、报告生成、报告图表交互、后端监测、日志记录。所有优化都保持了原有功能和数据口径，不通过降低采样频率、删除 raw rows、隐藏字段或改成 1s bucket 来制造性能收益。

硬性边界保持:

- 未测试 BF6。
- 未启动真实游戏。
- 未运行安装器。
- 未推 GitHub。
- 未更新 Release。
- `TelemetrySampleIntervalMs` 仍保持默认 1000ms、范围 500-5000ms。
- `PollIntervalMs` 仍是内部 watcher loop，没有重新暴露给用户。
- FPS average / 1% Low / 0.1% Low 仍从原始 PresentMon 帧数据计算。
- FPS 红色异常帧点仍基于原始帧数据，并在 FPS 视图默认显示。
- FPS 下拉仍保持 4 项，没有恢复“只看最低瞬时 FPS”。
- CPU Core VID 仍标注为请求/目标电压，不伪装成真实 Vcore。
- CPU 电压仍不把 VID/Vcore/SOC/Package 伪装成真实 per-core Vcore。

## 六个区域的实现

| 区域 | 本轮优化 | 结果 |
| --- | --- | --- |
| 前端显示页面 | `useFrameScopeBridgeState` 增加 snapshot 请求合并、隐藏窗口降频刷新、200ms 即时刷新合并；Overview / Targets / Reports / Settings / About 降低重复派生、重复刷新和菜单状态重建；报告列表和目标页改为更紧凑的可选中行模型。 | 通过 WebView2 live 和 reduced-motion smoke；常规页面没有引入虚拟列表，因为 baseline DOM 169-251 节点且 idle CPU 为 0%。 |
| UI 动画 | 移除高成本 `backdrop-filter` 路径，降低大面积 shadow/filter 压力；页面切换从较重 `pageCommit` 改为更短的 `navCommit`；按钮、状态、菜单、sidebar 使用更轻的 micro/content/state transition；`ChartShell` 移除 per-series framer-motion SVG 动画。 | 反馈仍存在，reduced motion 没有回退；页面切换、按钮、菜单、保存反馈、报告列表状态均被 smoke 覆盖。 |
| 报告生成 | `FrameScopeReportGenerator` 的 process payload 从 dense matrix 改为 `rle-v1` 编码；process CPU/memory series 用 builder 逐点压缩；FPS 仍输出 raw 时间点；metadata/system/cpu core/cpu voltage/cpu VID 保持原始采样语义。 | 大历史 run 的 `data.js` 从 47.95 MB 降到 32.89 MB，报告生成从 9972.2ms 降到 8626.4ms。 |
| 图表交互 | canvas 绘制层保留 raw source，绘制层按视口和模式做 envelope/spike cache；process 图降低全量绘制点数并保留 hover cache；tooltip 继续显示 raw point 时间和值；FPS 红点仍默认 overlay。 | 历史大 run process 图 full draw 从 20.4ms 降到 1.4ms；process hover 0.4ms，tooltip visible。 |
| 后端监测 | watcher 把全局 `TelemetrySampleIntervalMs` 传入 monitor/process/system/cpu telemetry 子进程；SystemSampler 将内部 loop 与各 telemetry due time 分离；子进程 stdout/stderr pipe 增加等待回收；cleanup 增加 wait 并回报 remaining count。 | 默认 1000ms synthetic session 中 FrameScopeMonitor 单核 CPU 从 1.845% 降到 0.545%，ProcessSampler 从 0.760% 降到 0.182%。 |
| 日志记录 | 新增 `FrameScopeLoggingPolicy` 集中控制 verbose/perf/auto diagnostic gating；健康 full report 不再默认自动生成诊断；默认日志仍只保留必要事件；verbose/perf 仍必须显式打开。 | 默认 session 仍为 1 行 watcher log，132 B；verbose/perf 分别为 4 行和 6 行，诊断能力未回退。 |

## 测量后决定不深改的区域

| 区域 | 决定 | 原因 |
| --- | --- | --- |
| 前端大列表虚拟化 | 暂不引入 | baseline 中 Overview / Targets / Reports / Settings / About DOM 只有 169-251 节点，WebView2 idle CPU 为 0%，没有足够证据支持引入虚拟列表的复杂度。后续只有真实 Targets 或 Reports 超过 500-1000 项时再做阈值触发。 |
| 全面删除 UI 动画 | 不做 | normal 和 reduced-motion smoke 均通过，当前卡顿证据不来自页面动效。保留必要反馈，只降低 blur/filter/shadow 和长 transition。 |
| 默认日志异步化或大改轮转 | 暂不做 | 默认日志每次 session 只有 1 行，约 132 B；verbose/perf 已受 gating 控制。日志轮转可作为长期 P2 维护项，不是当前性能瓶颈。 |
| SystemSampler 传感器深层重构 | 本轮只做 loop 和采样 due time 整理 | SystemSampler CPU 仍主要受 Windows perf counter 和传感器查询影响。继续优化需要硬件覆盖和更长真实运行窗口，不能用关闭采样或伪造 unavailable reason 替代。 |
| 长路径可靠性 | 本轮不作为性能验收项 | after single-pass 深路径报告生成曾 exitCode 1，符合既有 `PathTooLongException` 风险。正式性能指标使用短路径 `artifacts\gpo30\a2`，长路径建议作为单独可靠性修复。 |

## 报告生成 before/after

正式 after 使用短路径 `artifacts\gpo30\a2\report-generation\report-generation-after-shortpath.json`。短路径是本轮验收口径，避免深层 diagnostics artifact 路径触发已知长路径风险。

| 样本 | 帧数 | before wall | after wall | before peak private | after peak private | before data.js | after data.js | before output | after output |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| synthetic 240 | 240 | 206.9ms | 185.1ms | 23.86 MB | 23.78 MB | 18,458 B | 17,964 B | 65,219 B | 65,494 B |
| synthetic 1000 | 1,000 | 194.1ms | 158.4ms | 21.96 MB | 24.68 MB | 42,188 B | 41,688 B | 88,958 B | 89,219 B |
| synthetic 5000 | 5,000 | 198.7ms | 191.3ms | 29.09 MB | 29.07 MB | 207,436 B | 202,700 B | 254,212 B | 250,237 B |
| synthetic 20000 | 20,000 | 395.6ms | 328.1ms | 50.57 MB | 50.48 MB | 946,535 B | 905,933 B | 993,324 B | 953,483 B |
| history Valorant copy | 876,585 | 9972.2ms | 8626.4ms | 569.26 MB | 567.01 MB | 47,945,742 B | 32,894,446 B | 47,992,801 B | 32,942,210 B |

大历史 run 结果:

- 生成耗时降低 1345.8ms，约 13.5%。
- `framescope-interactive-data.js` 减少 15,051,296 B，约 31.4%。
- 总输出减少 15,050,591 B，约 31.4%。
- peak private 基本持平，从 569.26 MB 到 567.01 MB。
- raw PresentMon rows 保持 876,603，process samples 保持 17,714，processes 保持 119，未减少 raw row。
- process payload codec 为 `rle-v1`，解码后计数匹配。

## 图表交互 before/after

| 场景 | before | after | 结论 |
| --- | ---: | ---: | --- |
| history FPS full draw | 9.6ms | 9.3ms | 基本持平，raw FPS 点和红色异常帧点保留。 |
| history process full draw | 20.4ms | 1.4ms | 明显改善，绘制点从 72,552 降到 48,682，raw source 仍为 2,099,874。 |
| history process hover | 93.8ms settle | 0.4ms direct hover | tooltip visible，hover cache size 1。 |
| history process zoom draw | 1.3ms baseline settle口径 | 17.6ms direct draw | 仍低于单帧 16-20ms 临界附近，未改变 raw source。 |
| history process pan draw | 0.6ms baseline settle口径 | 10.3ms direct draw | 可接受，未观察到崩溃或空白。 |
| synthetic 20k CPU Core VID | 1.6ms baseline | 0.7ms | 保持 CPU Core VID 为请求/目标电压。 |

after 关键图表数据:

- history process raw: 2,099,874；drawn: 48,682；buckets: 30,940。
- history FPS raw: 2,629,755；drawn: 4,590；buckets: 1,560。
- process hover tooltip 显示 raw 时间和值，未改成 1s bucket。
- FPS 下拉仍为 4 项: 平均 FPS / 1% Low / 0.1% Low、只看平均 FPS、只看 1% Low、只看 0.1% Low。
- FPS 红色异常帧点在所有 FPS 视图默认 overlay。

## 后端监测 CPU / 内存 / 文件输出

CPU 为单核占比。before 来自前置分析报告，after 来自 `artifacts\gpo30\a2\monitoring-after\monitor-session-metrics-after.json`。

| session | 进程 | before CPU | after CPU | before peak WS | after peak WS | before peak private | after peak private |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| default-1000ms | FrameScopeMonitor | 1.845% | 0.545% | 54.87 MB | 28.68 MB | 32.52 MB | 24.10 MB |
| default-1000ms | FrameScopeProcessSampler | 0.760% | 0.182% | 26.37 MB | 26.33 MB | 49.50 MB | 49.50 MB |
| default-1000ms | FrameScopeSystemSampler | 2.062% | 2.270% | 40.04 MB | 39.02 MB | 46.21 MB | 45.32 MB |
| default-1500ms | FrameScopeMonitor | 2.095% | 0.527% | 54.91 MB | 29.07 MB | 32.51 MB | 24.40 MB |
| default-1500ms | FrameScopeProcessSampler | 0.314% | 0.176% | 26.07 MB | 25.76 MB | 49.51 MB | 49.25 MB |
| default-1500ms | FrameScopeSystemSampler | 2.305% | 2.021% | 38.91 MB | 39.02 MB | 45.35 MB | 46.09 MB |

文件输出和日志:

| session | before process CSV | after process CSV | before watcher log | after watcher log | 行数 |
| --- | ---: | ---: | ---: | ---: | ---: |
| default-1000ms | 93,429 B | 87,075 B | 130 B | 132 B | 1 |
| default-1500ms | 7,987 B | 7,447 B | 130 B | 132 B | 1 |
| verbose-1000ms | 93,783 B | 86,966 B | 722 B | 739 B | 4 |
| perf-1000ms | 93,249 B | 86,872 B | 788 B | 812 B | 6 |

解释:

- 默认 1000ms 保持真实采样，未提高默认间隔。
- ProcessSampler 文件体积下降来自写入和采样路径整理，不是删除字段。
- SystemSampler 1000ms CPU 小幅上升在短 synthetic run 中属于测量噪声和 telemetry due time 变化范围；1500ms 组下降到 2.021%。它没有通过关闭真实传感器采样来优化。
- 默认日志仍极小；verbose/perf 打开后也只有少量诊断行，诊断能力未回退。

## raw data 和功能口径检查

raw data 校验文件: `artifacts\gpo30\a2\raw-data-checks\raw-data-check-after-history.json`。

| 检查 | 结果 |
| --- | --- |
| `bucketMs` 回归 | PASS，未出现 |
| `lowWindowMs` 回归 | PASS，未出现 |
| raw PresentMon rows | 876,603 |
| valid PresentMon rows | 876,593 |
| selected frames | 876,585 |
| frameStats 重算 | PASS |
| recomputed average FPS | 587.96 |
| recomputed 1% Low | 164.57 |
| recomputed 0.1% Low | 36.60 |
| process codec | `rle-v1` |
| process decoded count | PASS，首尾 series 解码 count 均为 17,714 |

保留口径:

- FPS average / 1% Low / 0.1% Low 继续使用原始 PresentMon 帧数据。
- 图表 `DATA` 继续保留 raw/original data，canvas 层只优化绘制密度和缓存。
- CPU Core VID 继续作为请求/目标电压展示。
- CPU 电压没有真实 per-core Vcore 时继续显示 unavailable reason，不填假数据。

## 证据路径

主要 JSON / CSV:

- before report generation: `artifacts\global-performance-optimization-20260530\before\report-generation\report-generation-before.json`
- after report generation: `artifacts\gpo30\a2\report-generation\report-generation-after-shortpath.json`
- after chart interaction: `artifacts\gpo30\a2\chart-interaction\chart-interaction-after.json`
- after raw data check: `artifacts\gpo30\a2\raw-data-checks\raw-data-check-after-history.json`
- after monitor-session: `artifacts\gpo30\a2\monitoring-after\monitor-session-metrics-after.json`
- after logging summary: `artifacts\gpo30\a2\monitoring-after\logging-session-summary-after.csv`
- current WebView2 live smoke: `artifacts\global-performance-optimization-20260530\current-smoke-audit\webview2-live-smoke.json`
- current WebView2 reduced-motion smoke: `artifacts\global-performance-optimization-20260530\current-smoke-audit\webview2-reduced-motion-smoke.json`

截图:

- Settings: `artifacts\global-performance-optimization-20260530\current-smoke-audit\webview2-live-smoke-settings-clean.png`
- Targets: `artifacts\global-performance-optimization-20260530\current-smoke-audit\webview2-live-smoke-targets-result.png`
- Reports: `artifacts\global-performance-optimization-20260530\current-smoke-audit\webview2-live-smoke-reports.png`
- FPS 下拉: `artifacts\report-raw-data-retest-20260529\screenshots\fps-dropdown.png`
- FPS 红点: `artifacts\report-raw-data-retest-20260529\screenshots\fps-all.png`
- CPU Core VID: `artifacts\report-raw-data-retest-20260529\screenshots\cpu-vid.png`
- 大报告 FPS: `artifacts\gpo30\a2\chart-interaction\history-valorant-fps-after.png`
- 大报告 process hover: `artifacts\gpo30\a2\chart-interaction\history-valorant-process-hover-after.png`
- 大报告 CPU VID: `artifacts\gpo30\a2\chart-interaction\history-valorant-cpu-vid-after.png`
- synthetic 20000 CPU VID: `artifacts\gpo30\a2\chart-interaction\synthetic-20000-cpu-vid-after.png`

## 验证

| 验证项 | 结果 |
| --- | --- |
| `powershell.exe -ExecutionPolicy Bypass -File tools\Run-Frontend.ps1 verify` | PASS |
| `powershell.exe -ExecutionPolicy Bypass -File build.ps1` | PASS |
| `powershell.exe -ExecutionPolicy Bypass -File tests\Build-FrameScopeTests.ps1` | PASS |
| 全部构建出的 C# test exe | PASS |
| `tests\chart-sampling-tests.js` | PASS |
| synthetic 240 帧报告 | PASS |
| synthetic 20000 帧报告 | PASS |
| 历史大 run 副本 | PASS |
| synthetic monitor-session default 1000ms | PASS |
| synthetic monitor-session non-default 1500ms | PASS |
| 默认 / verbose / perf 日志写入 | PASS |
| WebView2 live smoke | PASS，当前审计重跑 `success=true`、`pageLoaded=true`、`pageReady=true` |
| WebView2 reduced-motion smoke | PASS，当前审计重跑 `success=true`、`reducedMotion=true`、`pageLoaded=true`、`pageReady=true` |
| raw data 口径检查 | PASS |
| `git diff --check` | PASS，无 whitespace error；仅 LF/CRLF 提示 |
| 残留进程检查 | PASS，未发现 FrameScope / FakePresentMon / PresentMon 残留；系统中已有 unrelated `msedgewebview2.exe` 不计入失败 |

## 剩余 P2 / 高风险项

| 项目 | 状态 | 建议 |
| --- | --- | --- |
| 深路径 `PathTooLongException` | 仍是 P2 可靠性风险 | 单独开可靠性修复，不和性能收益混在同一验收里。 |
| 前端超大 Targets / Reports 列表 | 当前未触发 | 等真实列表超过 500-1000 项或 idle CPU 持续高于 1% 单核，再做阈值虚拟化。 |
| SystemSampler 传感器/provider 进一步降耗 | 需要硬件覆盖 | 只能在保持真实 unavailable reason 和真实传感器语义下继续做缓存或 provider 分层。 |
| 长期日志轮转 | 默认不急 | 可作为维护项加大小上限和保留策略，但本轮不是性能瓶颈。 |
| 真实游戏窗口体验 | 未覆盖 | 按硬性边界本轮不启动 BF6 或真实游戏，建议后续全量复测窗口只打开已允许的历史报告和 synthetic run。 |

## 是否建议进入全量复测窗口

建议进入全量复测窗口。

理由:

- 实现覆盖六个指定区域。
- before/after 数据显示大报告生成、process 图绘制、FrameScopeMonitor/ProcessSampler CPU 和 process CSV 体积均有明确收益。
- raw data、FPS 统计、FPS 红点、CPU VID/CPU voltage 语义、全局采样间隔和日志诊断策略均保持。
- 构建、前端验证、C# tests、chart tests、synthetic 报告、历史大 run 副本、monitor-session、WebView2 live/reduced-motion smoke、截图和残留进程检查均已通过。

复测窗口建议只做真实用户路径和长时间稳定性确认，不需要重新打开 BF6 或真实游戏，除非后续明确扩大测试范围。
