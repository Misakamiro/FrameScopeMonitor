# FrameScope Monitor 全软件性能优化必要性分析

日期: 2026-05-29
范围: 前端页面、UI 动画、报告生成、图表交互、后端监测、日志记录
模式: 只测量、分析和设计优化方案；未改源码，未打包发布，未安装，未推 GitHub/Release，未测试 BF6，未启动真实游戏。

## 1. 结论

结论: PASS。

本轮分析覆盖了用户指定的 6 个区域，并生成了可复查 artifact。当前软件不是“全局都慢”，真正值得优先优化的是大报告生成和大报告进程图交互；前端常规页面、默认日志、默认监测链路在本轮 synthetic/fake target 和历史 run 副本下没有发现必须立刻优化的证据。

需要注意一个 P2 风险: 在深层 `docs\diagnostics\artifacts\...` 路径直接生成报告时，`FrameScopeReportGenerator.exe` 触发 `System.IO.PathTooLongException`。正式指标改用短路径 `artifacts\perf0529` 重新测得，输出存在且可打开。这个问题属于可靠性和路径兼容性风险，不是性能热点。

本轮只运行了 `tools\Run-Frontend.ps1 verify` 做前端基线健康检查，它会执行 npm/install/typecheck/test/Vite build 并刷新前端 dist 产物；未运行 `build.ps1`，未安装，未打包，未发布。

## 2. Artifact 清单

主要证据目录:

| 类型 | 路径 |
| --- | --- |
| 总 artifact | `docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis` |
| 前端 idle 和页面切换 | `docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis\frontend` |
| 报告生成指标 | `docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis\reports\report-generation-metrics-shortpath.json` |
| 短路径生成输出 | `artifacts\perf0529\reports` |
| 图表交互指标和截图 | `docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis\charts` |
| 后端 synthetic/fake target 监测 | `docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis\monitoring` |
| 日志指标 | `docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis\logging` |
| 残留进程检查 | `docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis\residual\residual-process-check.json` |

关键截图:

| 场景 | 截图 |
| --- | --- |
| synthetic 20,000 帧，1280x720 初始 | `docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis\charts\synthetic-l20k-1280x720-initial.png` |
| synthetic 20,000 帧，1280x720 交互后 | `docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis\charts\synthetic-l20k-1280x720-after-interactions.png` |
| 历史 Valorant 大 run，900x760 初始 | `docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis\charts\history-valorant-900x760-initial.png` |
| 历史 Valorant 大 run，900x760 交互后 | `docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis\charts\history-valorant-900x760-after-interactions.png` |

## 3. 六区域优化必要性矩阵

| 区域 | 是否需要优化 | 判断依据 | 优先级 |
| --- | --- | --- | --- |
| A. 前端显示页面性能 | 可选，当前不需要专项优化 | WebView2 idle CPU 为 0%，页面 DOM 169 到 251 节点，常规切换自动化耗时约 414 到 431 ms，未见大 DOM 或 idle 轮询压力 | P2 |
| B. 前端 UI 动画性能 | 可选 | normal/reduced motion smoke 都通过；动画期间没有证据显示 layout thrash 或长时间 CPU/GPU 压力；保留当前动效即可 | P2 |
| C. 生成图表速度优化 | 需要 | 历史大 run 876,585 帧生成耗时 10,018 ms，单核 CPU 约 99.82%，峰值 private 547.58 MB，`framescope-interactive-data.js` 47,945,544 B | P0 |
| D. 图表交互性能 | 部分需要 | FPS、系统、IO、CPU Core VID、CPU 核心频率绘制可接受；历史大 run 的 process 视图 draw 20.4 ms，原始源 2,099,874 点，绘制 72,552 点，是交互热点 | P1 |
| E. 后端监测占用优化 | 可选 | 1000 ms 默认 synthetic/fake target 下 FrameScopeMonitor 1.845% 单核，ProcessSampler 0.76% 单核，SystemSampler 2.062% 单核；总机器占比小，但 ProcessSampler 文件增长和扫描可优化 | P1/P2 |
| F. 日志记录性能优化 | 默认不需要，verbose/perf 可选 | 默认每次 session 1 行 watcher log，约 130 B；verbose/perf 打开后仍只有 4 到 6 行，未见默认日志过量 | P2 |

## 4. P0/P1/P2 瓶颈排序

| 优先级 | 瓶颈 | 收益 | 风险 | 结论 |
| --- | --- | --- | --- | --- |
| P0 | 大报告生成的 CSV 解析、process 矩阵构建、JSON 序列化、单体 `framescope-interactive-data.js` 写入 | 最高。可降低生成耗时、峰值内存和报告输出体积 | 中等。必须保留 raw/original data 和 FPS 原始帧统计口径 | 应优先做，但要以等价数据口径为验收前提 |
| P1 | 大报告 process 图交互绘制和缓存粒度 | 高。历史大 run process 视图 draw 20.4 ms，明显高于其他视图 | 中等。只能优化绘制层和缓存，不能改变原始数据或隐藏数据 | 应在 P0 后做，配合大报告 payload 优化 |
| P1 | ProcessSampler 扫描和 process CSV 文件增长 | 中等。默认 1000 ms 下 `process-samples.csv` 约 93,429 B/14.4s，1500 ms 降到 7,987 B/14.9s | 中等。不能靠提高默认采样间隔或减少功能伪装优化 | 可做元数据缓存、差分写入或字段复用 |
| P2 | 深路径 `PathTooLongException` | 中等。影响报告生成可靠性 | 低到中。路径处理需覆盖旧 .NET 行为 | 建议单独修复，避免长路径 artifact 目录失败 |
| P2 | 前端大列表虚拟化 | 低到中。当前 DOM 规模不大 | 低。只有真实大列表超过阈值才有收益 | 暂不急，保留为阈值触发项 |
| P2 | 日志轮转和保留策略 | 低到中。默认日志不大，但长期运行会累积 | 低 | 可作为维护项，不是当前性能瓶颈 |
| P2 | UI 动画降级 | 低。当前证据不支持必须删动画 | 低 | 保留 reduced motion 和现有动效，后续只在真实卡顿证据出现时改 |

## 5. 当前基线数据表

### 5.1 前端页面和 WebView2 idle

| 指标 | 结果 |
| --- | --- |
| `tools\Run-Frontend.ps1 verify` | exit 0，耗时 9,012 ms；说明: 刷新 frontend dist，仅作基线健康确认 |
| idle 测量时长 | 10,333 ms，4,032 ms warmup |
| `FrameScopeMonitor.exe` idle | CPU delta 0s，平均 0% 单核，峰值 WS 49.35 MB，峰值 private 30.93 MB |
| `msedgewebview2.exe` idle | CPU delta 0s，平均 0% 单核，峰值 WS 128.79 MB，峰值 private 120.13 MB |
| Vite 页面 DOM | Overview 169 nodes；Targets 247；Reports 245；Settings 251；About 195 |
| 页面切换自动化耗时 | Overview 429 ms；Targets 431 ms；Reports 416 ms；Settings 418 ms；About 414 ms |

解释: idle CPU 为 0% 表明当前 React/WebView2 没有明显空转轮询。DOM 节点规模也不高，未达到需要立即引入虚拟化或大规模状态拆分的程度。

### 5.2 UI 动画 smoke

| 模式 | 结果 | 耗时 | 说明 |
| --- | --- | --- | --- |
| normal motion | exit 0 | 7,576 ms | 覆盖页面切换、Reports、Targets、Settings 保存、报告再生成、打开报告等路径 |
| reduced motion | exit 0 | 7,805 ms | 覆盖同一 smoke 路径 |

说明: smoke 过程包含报告生成、外部 Edge 打开、截图和 bridge 操作，因此 smoke CPU 不能作为纯 UI idle CPU 解读。normal 和 reduced motion 都通过，当前没有证据要求删除或大幅简化动画。

### 5.3 报告生成

正式性能指标来自短路径 `artifacts\perf0529\reports`，避免深路径触发 `PathTooLongException`。

| 样本 | 帧数 | raw PresentMon 行 | 进程数/采样 | wall time | 单核 CPU | 峰值 WS | 峰值 private | 输入 CSV | 输出总量 | data.js |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| synthetic s1k | 1,000 | 1,000 | 12 / 20 | 215 ms | 0% | 33.33 MB | 24.84 MB | 137,189 B | 88,566 B | 42,020 B |
| synthetic m5k | 5,000 | 5,000 | 35 / 90 | 257 ms | 0% | 43.80 MB | 35.90 MB | 895,919 B | 253,820 B | 207,268 B |
| synthetic l20k | 20,000 | 20,000 | 80 / 340 | 390 ms | 0% | 51.06 MB | 44.80 MB | 5,081,792 B | 992,932 B | 946,367 B |
| history Valorant copy | 876,585 | 876,603 | 119 / 17,714 | 10,018 ms | 99.82% | 545.98 MB | 547.58 MB | 427,052,817 B | 47,992,339 B | 47,945,544 B |

热点判断:

- 小/中/20k synthetic 报告生成都很快，峰值内存低，说明生成器对正常规模不需要急改。
- 历史大 run 触发了真实瓶颈: 单核满载约 10 秒、峰值 private 约 548 MB、单体 data.js 接近 48 MB。
- 代码路径显示 `FrameScopeReportGenerator.cs` 读取 CSV 后统一构造 payload 并用 `JavaScriptSerializer` 写 `window.FRAMESCOPE_DATA`；`FrameScopeReportGenerator.ProcessData.cs` 的进程矩阵和 process-samples 处理是大 run 的主要体积来源。
- `FrameScopeReportGenerator.Analysis.cs` 保留原始 PresentMon 帧行进行 FPS average / 1% Low / 0.1% Low 计算，这个口径不能改成 1s bucket。

### 5.4 图表交互

| 场景 | 视口 | load | JS heap reported | 数据规模 |
| --- | --- | ---: | ---: | --- |
| synthetic 20k | 1280x720 | 114.2 ms | 9.54 MB | 20,000 frames，80 processes，340 process samples |
| history Valorant | 900x760 | 616.5 ms | 9.54 MB | 876,585 frames，119 processes，17,714 process samples |

视图切换和绘制:

| 场景 | 视图 | settle | draw | 原始源 | 绘制点 | buckets |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| synthetic 20k | FPS | 169.3 ms | 1.1 ms | 60,000 | 3,416 | 1,560 |
| synthetic 20k | CPU Core | 172.3 ms | 2.2 ms | 5,440 | 5,440 | 5,440 |
| synthetic 20k | CPU Core VID | 172.7 ms | 1.6 ms | 5,440 | 5,440 | 5,440 |
| synthetic 20k | process | 174.2 ms | 4.8 ms | 27,200 | 27,200 | 27,200 |
| history Valorant | FPS | 165.8 ms | 9.6 ms | 2,629,755 | 4,590 | 1,560 |
| history Valorant | process | 170.6 ms | 20.4 ms | 2,099,874 | 72,552 | 48,314 |
| history Valorant | system | 171.4 ms | 1.6 ms | 9,655 | 6,085 | 2,115 |
| history Valorant | IO | 172.0 ms | 2.9 ms | 9,655 | 6,181 | 2,115 |

交互:

| 场景 | hover tooltip | zoom draw | pan draw | PNG export |
| --- | ---: | ---: | ---: | ---: |
| synthetic 20k | 95.2 ms settle | 0.6 ms | 0.5 ms | 116.5 ms，3,090,584 B |
| history Valorant | 93.8 ms settle | 1.3 ms | 0.6 ms | 124.6 ms，3,148,404 B |

解释:

- report HTML 已经使用 canvas、render cache、bucket/envelope 绘制和 hover cache；不是 DOM 图表导致的卡顿。
- 历史大 run 的 FPS raw 源达到 2,629,755 点，但 draw 仍为 9.6 ms，可以接受。
- process 视图是明确热点: 原始源 2,099,874 点，绘制 72,552 点，draw 20.4 ms，且 cache size 到 130。这里值得做绘制层和数据结构优化。
- settle 约 166 到 174 ms 包含现有交互延迟和 harness 等待，不能直接解读为纯 draw 成本；真正的 canvas draw 指标应看 `drawMs`。

### 5.5 后端 synthetic/fake target monitor-session

测量进程树:

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FakePresentMon.exe`
- synthetic `TslGame.exe`

CPU 和内存:

| session | interval | 进程 | 单核 CPU | 总机器 CPU | 峰值 WS | 峰值 private |
| --- | ---: | --- | ---: | ---: | ---: | ---: |
| default-1000ms | 1000 | FrameScopeMonitor | 1.845% | 0.115% | 54.87 MB | 32.52 MB |
| default-1000ms | 1000 | FrameScopeProcessSampler | 0.760% | 0.047% | 26.37 MB | 49.50 MB |
| default-1000ms | 1000 | FrameScopeSystemSampler | 2.062% | 0.129% | 40.04 MB | 46.21 MB |
| default-1500ms | 1500 | FrameScopeMonitor | 2.095% | 0.131% | 54.91 MB | 32.51 MB |
| default-1500ms | 1500 | FrameScopeProcessSampler | 0.314% | 0.020% | 26.07 MB | 49.51 MB |
| default-1500ms | 1500 | FrameScopeSystemSampler | 2.305% | 0.144% | 38.91 MB | 45.35 MB |
| verbose-1000ms | 1000 | FrameScopeMonitor | 2.280% | 0.142% | 55.15 MB | 32.66 MB |
| verbose-1000ms | 1000 | FrameScopeProcessSampler | 0.543% | 0.034% | 26.30 MB | 49.39 MB |
| verbose-1000ms | 1000 | FrameScopeSystemSampler | 2.497% | 0.156% | 39.51 MB | 45.82 MB |
| perf-1000ms | 1000 | FrameScopeMonitor | 2.280% | 0.142% | 55.21 MB | 32.69 MB |
| perf-1000ms | 1000 | FrameScopeProcessSampler | 0.326% | 0.020% | 26.38 MB | 49.57 MB |
| perf-1000ms | 1000 | FrameScopeSystemSampler | 2.497% | 0.156% | 39.00 MB | 45.27 MB |

文件增长:

| session | presentmon.csv | process-samples.csv | system-samples.csv | cpu-core-samples.csv | topcpu | topio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| default-1000ms | 22,179 B | 93,429 B | 2,321 B | 20,285 B | 13,765 B | 6,410 B |
| default-1500ms | 22,179 B | 7,987 B | 1,657 B | 14,048 B | 63 B | 82 B |
| verbose-1000ms | 22,179 B | 93,783 B | 2,302 B | 20,269 B | 13,878 B | 6,727 B |
| perf-1000ms | 22,179 B | 93,249 B | 2,298 B | 20,265 B | 13,795 B | 5,958 B |

解释:

- 1500 ms 组的 `process-samples.csv` 明显更小，但不能把默认采样从 1000 ms 改成 1500 ms 来“优化”，因为用户要求默认 `TelemetrySampleIntervalMs=1000ms`、范围 `500-5000ms` 不回退。
- ProcessSampler 的文件量和扫描量有优化空间，但默认总机器 CPU 占比很低。
- SystemSampler 在短 synthetic run 中约 2.1 到 2.5% 单核，1500 ms 没有稳定下降，说明它可能受性能计数器、传感器枚举或测量噪声影响。当前不建议用降低采样功能解决。

### 5.6 日志记录

| session | verbose | perf diagnostics | watcher 行数 | 默认行 | verbose 行 | perf 行 | watcher bytes | bytes/s |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| default-1000ms | false | false | 1 | 1 | 0 | 0 | 130 B | 9.03 |
| default-1500ms | false | false | 1 | 1 | 0 | 0 | 130 B | 8.72 |
| verbose-1000ms | true | false | 4 | 1 | 3 | 0 | 722 B | 50.17 |
| perf-1000ms | false | true | 6 | 1 | 0 | 5 | 788 B | 54.75 |

解释:

- 默认日志不过量，只有 `presentmon-stop-requested` 一类结束事件。
- verbose/perf 都保持 gating，打开后本轮也没有明显文件爆炸。
- 长期运行仍建议做轮转和保留上限，但不是当前 P0 性能瓶颈。

## 6. 区域分析

### A. 前端显示页面性能

结论: 可选优化，当前不需要专项性能改造。

依据:

- WebView2 idle 测量中，`FrameScopeMonitor.exe` 和 `msedgewebview2.exe` CPU delta 都是 0s。
- Overview/Targets/Reports/Settings/About DOM 节点数只有 169 到 251。
- Settings 当前没有超长滚动压力，`scrollHeight` 等于 720；Targets 和 Reports 的 synthetic 列表也没有大到需要立即虚拟化。
- 未发现必须处理的过密轮询、过大 DOM 或空闲状态重复更新证据。

建议:

- 保留现状。
- 仅在真实 Targets 或 Reports 列表超过 500 到 1,000 项、或 WebView2 idle CPU 连续高于 1% 单核时，再做列表虚拟化和 selector 拆分。
- 不要为了“优化”提前重写 UI 状态层。

### B. 前端 UI 动画性能

结论: 可选优化，当前动效可保留。

依据:

- normal 和 reduced motion smoke 均 exit 0。
- 截图覆盖页面切换、sidebar 状态、按钮状态、Reports 菜单、Targets 查找、Settings 保存反馈。
- 没有观测到动画导致的持续 CPU/GPU 占用。

建议:

- 保留现有 reduced motion 分支。
- 避免新增大面积 blur/filter/shadow 动效。
- 如果后续真实机器上出现 GPU/CPU 峰值，再优先降级长 transition、阴影和模糊，而不是删除所有状态反馈。

### C. 生成图表速度优化

结论: 需要优化，P0。

依据:

- synthetic 20k 只有 390 ms、峰值 private 44.80 MB，说明正常体量没问题。
- 历史大 run 需要 10,018 ms、峰值 private 547.58 MB，data.js 47,945,544 B，是本轮最大明确瓶颈。
- 大 run 输入中 `presentmon.csv` 261,232,024 B，`process-samples.csv` 165,503,651 B，process 数据占比很高。

可优化方向:

- process 数据改为稀疏或索引结构，避免为所有进程构造过宽的 dense matrix。
- 避免重复读取 `process-samples.csv`，或把解析结果分阶段流式传递给统计和输出。
- 允许内部 payload 分段或 lazy-load，但必须保留原始 CSV 和原始统计口径。
- 使用更可控的 JSON 写出策略，减少 `JavaScriptSerializer` 一次性大对象序列化的峰值内存。
- 长路径写入改为 long-path-safe API 或短临时路径再移动，避免 `PathTooLongException`。

不能做:

- 不能把 raw report data 改成 1s bucket。
- 不能让 FPS average / 1% Low / 0.1% Low 使用聚合数据。
- 不能丢弃 process/system 原始数据来换体积。

### D. 图表交互性能

结论: process 大数据视图需要优化，其他视图可选。

依据:

- synthetic 20k 所有视图 draw 都在 0.8 到 4.8 ms。
- 历史大 run 的 FPS raw 源有 2,629,755 点，但 draw 9.6 ms，仍可接受。
- 历史大 run process 视图 draw 20.4 ms，绘制 72,552 点，原始源 2,099,874 点，是交互侧唯一明显热点。

可优化方向:

- process view 默认不要在空搜索时重建全部 series；可以缓存 top N、当前可见范围、搜索结果索引。
- hover tooltip 使用当前时间点的 process 排名 cache，避免反复扫描全部 process series。
- render cache key 保持精确，但减少 process 视图 cache 爆炸。
- 可考虑 worker/lazy parse 或分块初始化，但页面必须仍能访问完整 raw/original report data。

### E. 后端监测占用优化

结论: 可选优化，ProcessSampler 文件量和扫描可以作为 P1/P2 做。

依据:

- 默认 1000 ms synthetic/fake target 下，总机器 CPU 占比很低。
- ProcessSampler 在 1000 ms 组写出约 93 KB/14.4s，1500 ms 组明显变小，但不能用提高默认间隔作为优化策略。
- SystemSampler 单核 CPU 约 2.1 到 2.5%，但短测未证明它会随 1500 ms 明显下降。

可优化方向:

- 缓存稳定进程元数据，避免每次采样重复格式化相同字段。
- 对 unchanged process rows 做轻量差分或字段复用，但输出语义要保持兼容。
- 减少重复 WMI 或传感器枚举，保留真实传感器状态和 unavailable reason。
- 子进程等待和清理路径可以保留当前功能，同时减少无用轮询。

### F. 日志记录性能

结论: 默认不需要优化，轮转和保留策略可选。

依据:

- 默认 session 只有 1 行 watcher log。
- verbose/perf 打开后分别 4 行和 6 行，仍很小。
- 源码路径 `FrameScopeLoggingPolicy.cs` 和 `FrameScopeNativeMonitor.Watcher.cs` 已经通过 gating 控制 verbose/perf 输出。

可优化方向:

- 增加 watcher log 轮转和最大保留大小。
- 保持 verbose/perf gating，不要让性能诊断日志默认打开。
- 只有在真实长时间运行显示日志写入过密时，再考虑异步写入或缓冲。

## 7. 安全优化项

| 项 | 说明 | 为什么安全 | 优先级 |
| --- | --- | --- | --- |
| 大报告 process payload 稀疏化 | 保留原始 CSV 和指标口径，把内部 JS payload 从 dense matrix 改成按进程/时间索引的稀疏结构 | 不改变 FPS 和原始数据来源，只改变展示层数据结构 | P0 |
| 报告生成分段写出 | `framescope-interactive-data.js` 可按模块分段或增量写出，降低峰值内存 | 只改变写出方式，不改变内容口径 | P0 |
| 避免重复解析 `process-samples.csv` | 解析一次，统计和输出共享结构 | 降低 CPU/内存，不影响结果 | P0 |
| process 图 render cache 优化 | 对 search、time range、metric 做更细粒度 cache；避免空搜索重建全部 series | 只优化绘制层，不丢数据 | P1 |
| ProcessSampler 元数据缓存 | 缓存进程名、路径、静态字段，采样时只更新动态值 | 不减少采样频率，不隐藏数据 | P1 |
| 长路径安全写出 | 用短临时目录或 long-path-safe 写文件策略 | 修复可靠性，不改变报告内容 | P2 |
| 日志轮转 | `framescope-watcher.log` 设置大小和保留上限 | 保持默认日志内容，只控制长期文件膨胀 | P2 |
| 前端大列表阈值虚拟化 | 仅当列表真实超过阈值时启用 | 当前不改常规 UI，避免过早复杂化 | P2 |

## 8. 高风险或暂不建议项

| 项 | 风险 | 当前建议 |
| --- | --- | --- |
| 把 report raw data 改成 1s bucket | 会破坏图表原始数据和 FPS 统计口径 | 禁止 |
| 丢弃 process/system raw 数据，只保留 top N | 会影响诊断和交互完整性 | 不建议 |
| 把默认采样间隔从 1000 ms 提高到 1500 ms | 看似降低文件量，但等于降低功能密度 | 禁止作为默认优化 |
| 把 CPU Core VID 当作真实 Vcore 展示 | 会误导用户，破坏电压语义 | 禁止 |
| 在没有真实 per-core Vcore 时填充假电压 | 会制造假数据 | 禁止 |
| 全面重写图表引擎 | 风险高，容易破坏 raw/original 语义 | 暂不建议，除非先有独立 prototype 和验收数据 |
| 默认打开 verbose/perf 诊断日志 | 增加长期 I/O 和日志噪声 | 不建议 |

## 9. 禁止项

这些边界在任何后续优化实现中都不能回退:

- 不能重新改成 1s bucket。
- 不能丢 raw chart data。
- FPS average / 1% Low / 0.1% Low 必须继续用原始 PresentMon 帧数据。
- CPU Core VID 必须继续标注为请求/目标电压，不能伪装成真实 Vcore。
- CPU 电压没有真实 per-core Vcore 时必须继续显示无数据原因。
- `TelemetrySampleIntervalMs` 默认必须保持 1000 ms，范围保持 500 到 5000 ms。
- 不能通过降低采样功能、隐藏采样数据或减少诊断字段来假装优化。
- verbose/perf 日志诊断 gating 不能回退。

## 10. 推荐实现顺序

1. P0: 大报告生成和 data.js 体积优化。
   - 目标是降低历史大 run 的 10,018 ms 生成时间、547.58 MB 峰值 private、47,945,544 B data.js。
   - 先做 process payload 和序列化峰值，不碰 FPS 原始帧统计。

2. P1: 大报告 process 图交互优化。
   - 目标是把历史大 run process view draw 从 20.4 ms 降到稳定 12 ms 以下。
   - 保持 raw/original 数据可访问，优化 cache、search、hover 和可见范围绘制。

3. P1/P2: ProcessSampler 和文件增长优化。
   - 目标是默认 1000 ms 下减少 `process-samples.csv`、`topcpu-samples.csv`、`topio-samples.csv` 的重复字段和无用写入。
   - 不改变默认采样间隔。

4. P2: 长路径可靠性修复。
   - 目标是深层 `docs\diagnostics\artifacts\...` 路径不再触发 `PathTooLongException`。

5. P2: 日志轮转和前端阈值虚拟化。
   - 作为维护项做，不抢 P0/P1。

## 11. 验收标准

| 优化项 | 验收标准 |
| --- | --- |
| 大报告生成优化 | synthetic 20k 仍 <= 500 ms；历史 Valorant copy 生成时间比 10,018 ms 至少下降 25%；峰值 private 比 547.58 MB 至少下降 25%；输出报告可打开 |
| data.js/payload 优化 | 不丢 raw/original 数据；`framescope-interactive-manifest.json` 中 frames、rawPresentMonRows、processes、processSamples 与优化前一致；FPS average / 1% Low / 0.1% Low 数值一致 |
| process 图交互优化 | 历史大 run process view draw 从 20.4 ms 降到 12 ms 以下；hover/zoom/pan 仍显示正确 tooltip 和范围；1280x720 与 900x760 截图无空白或遮挡 |
| ProcessSampler 优化 | 默认 1000 ms 下采样间隔不变；`process-samples.csv` 字段语义兼容；CPU 不高于本轮 default-1000ms 基线；文件增长减少需用 bytes/s 证明 |
| SystemSampler 优化 | CPU Core VID 仍标注请求/目标电压；无真实 Vcore 时仍显示 unavailable reason；CPU 电压不生成假数据 |
| 日志轮转 | 默认日志内容不增加；verbose/perf 仍按 gating 控制；轮转后能保留最近诊断信息 |
| 长路径修复 | 在本轮深层 artifact 路径直接生成 synthetic small/medium/large 和历史 copy 报告，不再出现 `PathTooLongException` |
| 前端可选优化 | WebView2 idle CPU 保持 0 到 1% 单核；normal/reduced smoke 均通过；页面文本和控件无重叠 |

## 12. 后续实现窗口提示词草案

### Goal 1: P0 大报告生成和 payload 优化

对 FrameScope Monitor 做 P0 大报告生成优化。只改报告生成相关代码，保留 raw/original data 口径，不允许改成 1s bucket，不允许丢 raw chart data，不允许改变 FPS average / 1% Low / 0.1% Low 的原始 PresentMon 帧数据计算。基线报告在 `docs\diagnostics\2026-05-29-framescope-full-performance-optimization-analysis.md`，重点对比历史 Valorant copy: 当前 10,018 ms、峰值 private 547.58 MB、data.js 47,945,544 B。目标是至少降低 25% 生成时间和峰值 private，同时 synthetic 20k 仍 <= 500 ms。完成后写 before/after artifact 和验证报告。

### Goal 2: P1 大报告 process 图交互优化

对 FrameScope Monitor 交互式报告的 process 图做 P1 优化。只改绘制层、cache、search、hover 或可见范围数据结构，不改变 report raw/original data，不丢 process/system 数据。基线是历史 Valorant 900x760 process view: raw 2,099,874 点、drawn 72,552 点、draw 20.4 ms。目标 draw < 12 ms，hover/zoom/pan/export PNG 正常，必须输出 1280x720 和 900x760 截图、交互 JSON、残留进程检查。

### Goal 3: P1/P2 后端采样和日志维护优化

对 FrameScope Monitor 的 ProcessSampler、SystemSampler 和日志做保守优化。不能改变 `TelemetrySampleIntervalMs=1000ms` 默认值和 500-5000ms 范围，不能降低采样功能，不能伪造 CPU 电压/VID，不能回退日志 gating。优先缓存进程静态元数据、减少重复格式化和无用文件写入；日志只做轮转/保留上限。验收要跑 default-1000ms、default-1500ms、verbose-1000ms、perf-1000ms synthetic/fake target，对比 CPU、内存、文件 bytes、日志行数，并做残留进程检查。

## 13. 残留进程检查

最终残留检查文件:

`docs\diagnostics\artifacts\2026-05-29-full-performance-optimization-analysis\residual\residual-process-check.json`

结果:

| 项 | 值 |
| --- | ---: |
| 检查时间 | 2026-05-29T23:53:57.5526540+08:00 |
| 清理对象 | 本轮误触发的 `FrameScopeMonitor.exe --help` 进程树 |
| 已停止进程数 | 7 |
| 剩余匹配 FrameScope/Fake target/PresentMon 进程数 | 0 |
| 剩余本轮分析拥有的进程数 | 0 |

说明: 只清理了命令行精确匹配本轮分析残留的 `FrameScopeMonitor.exe --help` 及其 WebView2 子进程，没有清理无关系统或其他应用进程。

## 14. 最终判断

当前优化必要性不是平均分布的:

- 真正需要做: P0 大报告生成和 payload 体积，P1 大报告 process 图交互。
- 可选做: ProcessSampler 文件量和扫描、长路径可靠性、日志轮转。
- 当前不需要专项做: 常规前端页面性能、默认日志性能、UI 动画大幅降级。

下一步如果进入实现，应先拆 P0 小窗口，围绕历史大 run 的生成时间、峰值内存和 data.js 体积做等价优化；不要先动 UI 动画或默认采样间隔。
