# FrameScope Valorant 20260510 PowerShell Origin Diagnosis

Status: PARTIAL

## Scope

指定 run:

`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260510-101646`

本报告只分析该 run 已存在的 artifacts 和现有 watcher log。不改源码、不改 UI、不打包、不推 GitHub。

## 结论

该 run 的报告里确实把 `powershell` 展示为“后台进程 CPU”，并且 `process-samples.csv` 里 `powershell` 的峰值被聚合到了 `35.38%` CPU、`468.7 MB` 内存。

但仅凭这个 run 不能精确判断这些 `powershell` 来自哪个软件或脚本。原因是当前 artifacts 缺少归因必需字段：Parent PID、父进程名、CommandLine、ExecutablePath、WorkingDirectory、StartTime、用户/会话、签名/公司名，以及进程创建事件。现有 CSV 只能说明“采样到了名为 powershell 的多个 PID”，不能说明这些 PID 是 FrameScope、Codex、GameLite、用户脚本、系统任务，还是别的软件启动的。

能确定的部分：

- 该 run 中没有 `pwsh` / `pwsh.exe` 记录；匹配到的进程名只有 `powershell`。
- `process-samples.csv` 包含 `Pids`，可证明不是单一 powershell：至少出现过 6 个不同 PID。
- `process-samples.csv` 按进程名聚合，报告数据继续只保留 `process.t` / `process.names` / `process.cpu` / `process.mem` / `process.stats`，所以报告 tooltip 不能辅助定位来源。
- `status.json` 显示该 run 是 `native-csharp` 监测模式，进程采样器是 `FrameScopeProcessSampler.exe`，系统采样器是 `FrameScopeSystemSampler.exe`，PresentMon 是独立 exe；这不支持“报告中的 powershell 就是采样器本体”的判断。
- 但因为缺少命令行和父进程字段，不能排除某个 FrameScope 外围流程、Codex、GameLite、用户脚本或系统任务曾经启动过这些 powershell。

## 读取的文件

已读取指定必读文件：

- `status.json`
- `summary.json`
- `process-samples.csv`
- `topcpu-samples.csv`
- `topio-samples.csv`
- `system-samples.csv`
- `sample-alerts.csv`
- `report-generation.log`
- `charts\framescope-interactive-manifest.json`
- `charts\framescope-interactive-report.html`

也检查了 run 目录下所有 stdout/stderr/log/json/csv 元数据文件：

- `event-samples.csv`
- `presentmon.csv`
- `presentmon.stderr.log`
- `presentmon.stdout.log`
- `presentmon-info.json`
- `report-opened.flag`
- `report-progress.json`

额外读取：

- `charts\framescope-interactive-data.js`
- `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-watcher.log`

该 run 目录下没有 `system-slow-sampler.log`。

## Run 时间范围

可用时间轴有几类，需要分开看：

| 来源 | 时间 |
|---|---:|
| run 目录名 | `Valorant-20260510-101646` |
| `process-samples.csv` 首条 | `2026-05-10T10:16:47.1562002+08:00` |
| `process-samples.csv` 末条 | `2026-05-10T10:18:09.2093499+08:00` |
| `status.json` `EndTime` | `2026-05-10T10:18:10.2775613+08:00` |
| `report-opened.flag` | `2026-05-10T10:18:11.8893720+08:00` |
| report JS `run.startLabel` | `2026-05-10 10:16:55` |
| report JS `run.endLabel` | `2026-05-10 10:18:05` |
| report JS `durationLabel` | `1分11秒` |

`status.json` 是会被后续打开/重新生成报告流程更新的状态文件；当前 `Time` / `ReportOpenedAt` 已经是 2026-05-24 的后续状态。因此判断 powershell 来源时，主要依据 2026-05-10 的采样 CSV，而不是把 2026-05-24 的状态时间当成原始采样时间。

`report-generation.log` 本身是 manifest JSON，没有写入 wall-clock 日志行；其文件时间显示报告曾在 2026-05-24 00:59:11 被重新生成。watcher log 也显示 2026-05-23/2026-05-24 对这个旧 run 有多次重新生成或打开报告记录。这些后续动作晚于 2026-05-10 采样，不可能解释 2026-05-10 采样里的 powershell 来源。

## 字段覆盖情况

| 文件 | PID | Parent PID | CommandLine | ExecutablePath | WorkingDirectory | StartTime |
|---|---:|---:|---:|---:|---:|---:|
| `process-samples.csv` | PARTIAL: `Pids` 聚合列表 | NO | NO | NO | NO | NO |
| `topcpu-samples.csv` | YES: `Id` | NO | NO | NO | NO | NO |
| `topio-samples.csv` | YES: `Id` | NO | NO | NO | NO | NO |
| `system-samples.csv` | NO | NO | NO | NO | NO | NO |
| `sample-alerts.csv` | NO | NO | NO | NO | NO | NO |
| `charts\framescope-interactive-data.js` | NO | NO | NO | NO | NO | NO |
| `charts\framescope-interactive-report.html` | NO | NO | NO | NO | NO | NO |

源码读取到的采样器字段定义也与 artifact 一致：

- `process-samples.csv`: `Time,SampleIndex,ElapsedMs,ProcessName,Count,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,Priorities,Pids`
- `topcpu-samples.csv`: `Time,SampleIndex,ElapsedMs,ProcessName,Id,CpuPct,WorkingSetMB`
- `topio-samples.csv`: `Time,SampleIndex,ElapsedMs,ProcessName,Id,CpuPct,ReadMBps,WriteMBps,WorkingSetMB`

这些字段足够区分“同名进程是否多 PID”，但不足够做“哪个软件/脚本启动”的精确归因。

## PowerShell 采样统计

匹配规则：`powershell` / `powershell.exe` / `pwsh` / `pwsh.exe`，大小写不敏感。

结果：仅发现 `powershell`，没有发现 `powershell.exe`、`pwsh` 或 `pwsh.exe` 名称。

### process-samples.csv

| 指标 | 值 |
|---|---:|
| powershell 行数 | `743` |
| 出现范围 | `2026-05-10T10:16:47.1562002+08:00` 到 `2026-05-10T10:18:09.2093499+08:00` |
| CPU 平均值 | `0.1123%` |
| CPU 峰值 | `35.38%` |
| 内存峰值 | `468.7 MB` |
| 读 IO 峰值 | `6.194 MB/s` |
| 写 IO 峰值 | `0.074 MB/s` |
| 同时 powershell 数量峰值 | `5` |

按 `Pids` 聚合列表看：

| Pids | 行数 | 时间范围 | CPU 峰值 | 内存峰值 |
|---|---:|---|---:|---:|
| `27560;25840` | `735` | `10:16:47.486` - `10:18:09.209` | `7.18%` | `249.4 MB` |
| `27560` | `3` | `10:16:47.156` - `10:16:47.375` | `0%` | `118.6 MB` |
| `34472;27560;3292;25840;26012` | `2` | `10:16:52.458` - `10:16:52.569` | `35.38%` | `468.7 MB` |
| `27560;25840;18900` | `2` | `10:16:47.816` - `10:16:47.925` | `10.76%` | `307.9 MB` |
| `27560;25840;26012` | `1` | `10:16:52.678` | `3.56%` | `329.9 MB` |

解释：

- PID `27560` 从第一条样本开始就存在，持续到最后一条样本。
- PID `25840` 在采样开始后约 330 ms 出现，并持续到最后一条样本。
- PID `18900` 只出现在 `10:16:47.816` - `10:16:47.925` 附近。
- PID `3292`、`26012`、`34472` 出现在 `10:16:52.458` - `10:16:52.678` 附近，是 `35.38%` 聚合峰值的主要原因。
- 因为 `process-samples.csv` 是按 `ProcessName` 合并，所以 `35.38%` 不是单个 PID 的 CPU，而是同名 `powershell` 进程组在该采样点的聚合值。

### topcpu-samples.csv

| 指标 | 值 |
|---|---:|
| powershell 行数 | `17` |
| 出现范围 | `2026-05-10T10:16:47.5977253+08:00` 到 `2026-05-10T10:18:08.3227970+08:00` |
| CPU 平均值 | `4.9018%` |
| CPU 峰值 | `13.27%` |
| 内存峰值 | `130.8 MB` |

按单 PID：

| PID | topcpu 行数 | 时间范围 | CPU 峰值 | 内存峰值 |
|---:|---:|---|---:|---:|
| `25840` | `12` | `10:16:47.597` - `10:18:08.322` | `8.88%` | `130.8 MB` |
| `26012` | `2` | `10:16:52.569` - `10:16:52.678` | `13.27%` | `89.4 MB` |
| `3292` | `1` | `10:16:52.569` | `11.5%` | `75.4 MB` |
| `34472` | `1` | `10:16:52.569` | `10.61%` | `76.4 MB` |
| `18900` | `1` | `10:16:47.925` | `8.07%` | `67.5 MB` |
| `27560` | `0` | 只在 process 聚合中出现 | N/A | N/A |

解释：`topcpu-samples.csv` 是每个采样点 CPU 前列进程，所以它只记录 powershell 进入 top CPU 列表的时刻。长驻 PID `27560` 存在于 `process-samples.csv`，但没有进入 top CPU。

### topio-samples.csv

| 指标 | 值 |
|---|---:|
| powershell 行数 | `7` |
| 出现范围 | `2026-05-10T10:16:47.5977253+08:00` 到 `2026-05-10T10:16:52.6788883+08:00` |
| CPU 平均值 | `8.7429%` |
| CPU 峰值 | `13.27%` |
| 内存峰值 | `109.2 MB` |
| 读 IO 峰值 | `2.068 MB/s` |
| 写 IO 峰值 | `0.074 MB/s` |

按单 PID：

| PID | topio 行数 | 时间范围 | 读 IO 峰值 | 写 IO 峰值 |
|---:|---:|---|---:|---:|
| `25840` | `2` | `10:16:47.597` - `10:16:47.706` | `1.000 MB/s` | `0.074 MB/s` |
| `26012` | `2` | `10:16:52.569` - `10:16:52.678` | `2.061 MB/s` | `0.036 MB/s` |
| `3292` | `1` | `10:16:52.569` | `2.065 MB/s` | `0.001 MB/s` |
| `34472` | `1` | `10:16:52.569` | `2.068 MB/s` | `0.001 MB/s` |
| `18900` | `1` | `10:16:47.925` | `1.760 MB/s` | `0.001 MB/s` |

### sample-alerts.csv

`sample-alerts.csv` 中 `powershell` 作为 `TopCpuProcess` 出现 3 次：

| 时间 | SampleIndex | Alerts | TopCpuPct | TopIoProcess |
|---|---:|---|---:|---|
| `2026-05-10T10:16:47.5977253+08:00` | `4` | `heavy-process-io` | `7.01%` | `VALORANT-Win64-Shipping` |
| `2026-05-10T10:16:47.7065856+08:00` | `5` | `heavy-process-io` | `7.18%` | `VALORANT-Win64-Shipping` |
| `2026-05-10T10:16:47.8165109+08:00` | `6` | `heavy-process-io` | `8.88%` | `VALORANT-Win64-Shipping` |

这里的 alert 类型是 `heavy-process-io`，对应 top IO 进程是 `VALORANT-Win64-Shipping`，不是 powershell。也就是说这 3 行说明 powershell 曾经是 top CPU 进程，但 alert 触发原因不是 powershell IO。

## 是否与监测开始/停止/报告生成重合

### 监测开始

`powershell` 从 `process-samples.csv` 第一条样本 `10:16:47.156` 就已经出现。结合 run 目录名 `10:16:46`、`presentmon-info.json` 文件时间 `10:16:47`，可以判断它非常接近监测开始。

但这只能说明“监测开始时系统里已经能采样到 powershell”，不能说明它是 FrameScope 启动的。缺少 `StartTime`、`Parent PID` 和 `CommandLine`，所以无法判断 PID `27560` 是监测前已存在、监测启动瞬间由外部触发，还是某个 FrameScope 外围流程间接启动。

### 监测停止

`process-samples.csv` 末条是 `10:18:09.209`，`status.json` 的 `EndTime` 是 `10:18:10.277`。PID `27560` / `25840` 一直存在到末条样本，但 CPU 峰值主要发生在开始后 0.4 秒和 5.4 秒附近，不集中在停止阶段。

现有证据不能把 powershell 和停止流程绑定。

### 报告生成

原始 `report-opened.flag` 是 `10:18:11.889`，晚于采样结束。当前 `report-generation.log` 是 2026-05-24 重新生成后的 manifest JSON，watcher log 也只保留 2026-05-23/2026-05-24 对该旧 run 的重新生成/打开记录。

因此，2026-05-10 采样中的 powershell 不可能由 2026-05-23/2026-05-24 的后续报告重新生成导致。至于 2026-05-10 原始报告生成阶段是否启动过 powershell，当前 run 的采样已经在 `10:18:09.209` 结束，且没有原始 watcher log 命令行记录，不能确认。

## Watcher log 对照

读取到的 watcher log:

`C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-watcher.log`

与本 run 相关的日志只覆盖 2026-05-23 和 2026-05-24 的后续操作，包括：

- 多次 `report-generate-start` / `report-generate-complete`
- 多次 `open-report-default-browser`
- 多次 `web-bridge-monitor-started`
- 多次 `native-watcher-start`
- `2026-05-24T02:32:00` 对该旧 run 的打开报告记录

该 watcher log 中没有与 `Valorant-20260510-101646` 对应的 2026-05-10 原始 `monitor-start` / `monitor-complete` 行，也没有匹配 `powershell` / `pwsh` / cleanup / old script / external script 的行。

结论：watcher log 不能为该 run 的 powershell 来源提供精确归因。它只能说明后续重新生成/打开报告发生在 2026-05-23/2026-05-24，晚于原始采样。

## Report / Manifest 展示情况

`charts\framescope-interactive-manifest.json` 记录：

- `generator`: `native-csharp`
- `processes`: `114`
- `processSamples`: `743`
- `reportKind`: `full`
- `presentMonSelectedTrack.processId`: `26932`

`charts\framescope-interactive-data.js` 中：

- `DATA.process.names[0]` 是 `powershell`
- `DATA.process.stats` 第一项是 `powershell`
- `powershell.maxCpu` 是 `35.38`
- `powershell.avgCpu` 是 `0.11`
- `powershell.maxMem` 是 `468.7`
- `powershell.samples` 是 `743`

`charts\framescope-interactive-report.html` 中：

- “后台进程” tab 存在。
- metric selector 中有 “后台进程 CPU” 和 “后台进程内存”。
- tooltip 只显示当前时间点的进程名和值，例如 CPU % 或 MB。
- tooltip 数据源来自 `DATA.process.names`、`DATA.process.cpu`、`DATA.process.mem`。
- 没有 PID、Parent PID、CommandLine、ExecutablePath、WorkingDirectory、StartTime 等归因字段。

结论：报告确实把 `powershell` 展示为后台进程 CPU 第一名，但报告 UI 和 manifest 不能辅助判断来源。

## 是否能确定来源

不能确定。

能确定的是：

- 至少 6 个不同 PID 被聚合为 `powershell`。
- PID `27560` 和 `25840` 是长驻或接近长驻的 powershell。
- PID `18900`、`3292`、`26012`、`34472` 是短时 powershell，贡献了开始阶段和 5 秒附近的尖峰。
- 报告显示的 `35.38%` 是同名聚合峰值，不是单个 powershell PID 的峰值。

不能确定的是：

- 每个 powershell 的父进程是谁。
- 每个 powershell 的命令行是什么。
- 每个 powershell 的可执行文件路径是否是系统 PowerShell。
- 每个 powershell 的工作目录是否指向 FrameScope、Codex、GameLite、用户脚本目录或系统目录。
- 每个 powershell 的启动时间是否早于 FrameScope 监测。
- 短时 PID 是否来自同一个脚本、多个并行脚本、WMI/计划任务，或某个软件的子进程。

因此不能写明“来源是某某软件/脚本”。任何精确来源判断都会超出现有证据。

## 是否可能是 FrameScope 自身

不能完全排除，但现有证据不支持直接下结论说它来自 FrameScope。

支持“不是 FrameScope 采样器本体”的证据：

- `status.json` 显示 `MonitorScript` / `MonitorMode` 是 `native-csharp`。
- `ProcessSamplerExe` 是 `FrameScopeProcessSampler.exe`。
- `SystemSamplerExe` 是 `FrameScopeSystemSampler.exe`。
- `PresentMonExe` 是 `PresentMon-2.4.1-x64.exe`。
- `process-samples.csv` 中 FrameScope 相关进程以 `FrameScopeMonitor`、`FrameScopeProcessSampler`、`FrameScopeSystemSampler`、`PresentMon-2.4.1-x64` 等名字单独出现，不是 powershell。
- `powershell` PID 与 `FrameScopeMonitor` PIDs 不同。

仍不能排除的部分：

- 如果某个 FrameScope 外围脚本、旧版清理脚本、安装器残留、WMI 触发器或报告打开流程在同一时间启动了 powershell，当前 run 没有父进程和命令行字段，无法证明或排除。
- watcher log 没有 2026-05-10 原始监测行，无法对照当时是否有 cleanup 或脚本触发。

结论：当前证据只能写“未证明来自 FrameScope；从 native-csharp 监测链路看，报告中的 powershell 不是采样器/PresentMon/FrameScopeMonitor 本体”。不能写“确定不是 FrameScope 相关流程”。

## 是否可能是 Codex / GameLite / 用户脚本 / 系统任务

### Codex

可能，但不能确定。

该 run 的 `process-samples.csv` 中确实存在 `Codex` 进程统计，但没有任何字段把 `Codex` 与 powershell PID 关联起来。缺少 Parent PID 和 CommandLine，所以不能判断 powershell 是否由 Codex 启动。

### GameLite

可能，但不能确定。

源码根目录中存在 GameLite 相关 PowerShell 脚本，但指定 run 的 artifacts 没有保存 powershell 命令行、工作目录或父进程。watcher log 也没有 2026-05-10 对应的 GameLite 触发或 cleanup 记录。因此不能把本 run 的 powershell 归因到 GameLite。

### 用户脚本

可能，但不能确定。

如果用户当时手动运行了脚本，当前 run 也无法识别；缺少 CommandLine、WorkingDirectory、StartTime 和进程创建快照。

### 系统任务 / WMI / 计划任务

可能，但不能确定。

短时 PID `18900`、`3292`、`26012`、`34472` 的生命周期很短，形态上可能符合外部触发脚本或计划任务，但没有 Parent PID / CommandLine / StartTime / 事件日志，不能归因。

## 后续要精确归因需要新增的字段

建议新增两类诊断：高频采样字段和同步快照。

### 采样 CSV 字段

对 `process-samples.csv`、`topcpu-samples.csv`、`topio-samples.csv` 增加 per-PID 归因字段：

- `Id` / `Pid`
- `ParentProcessId`
- `ParentProcessName`
- `ProcessName`
- `ExecutablePath`
- `CommandLine`
- `CommandLineHash`
- `WorkingDirectory`
- `StartTime`
- `SessionId`
- `UserSid` 或可脱敏用户名
- `CompanyName`
- `ProductName`
- `FileDescription`
- `FileVersion`
- `Signer`
- `IsChildOfFrameScopeMonitor`
- `FrameScopeAncestorPid`
- `FirstSeenTime`
- `LastSeenTime`

如果担心隐私，可以同时保存：

- 完整 `CommandLine` 只进本地诊断 JSON，不展示到 HTML。
- HTML 只展示脱敏后的 `CommandLinePreview` 和 `CommandLineHash`。
- 路径可保留盘符和最后两级目录，完整路径放本地 sidecar。

### 同步快照

建议每个 run 额外写入：

- `process-origin-snapshot-start.json`: 监测开始前/开始瞬间所有 powershell/pwsh/cmd/python/node/FrameScope 相关进程。
- `process-origin-snapshot-first-sample.json`: 第一条 process sample 同步快照。
- `process-origin-snapshot-spikes.jsonl`: 每次 powershell 进入 topcpu/topio 或聚合 CPU 超过阈值时的快照。
- `process-origin-snapshot-stop.json`: 停止监测前后快照。
- `process-origin-snapshot-report-generation.json`: 报告生成前后快照。

每个快照至少包含：

- PID、PPID、父进程名
- 命令行
- exe 路径
- 工作目录
- 启动时间
- 用户/会话
- 进程树祖先链
- 是否属于 FrameScope 本次 monitor PID 的子树

### 进程创建事件

短时 powershell 很容易错过，需要事件级记录：

- 订阅 WMI `Win32_ProcessStartTrace` 或 ETW `Microsoft-Windows-Kernel-Process`。
- run 开始到结束期间写 `process-start-events.jsonl`。
- 对 `powershell.exe`、`pwsh.exe`、`cmd.exe`、`python.exe`、`node.exe`、`wscript.exe`、`cscript.exe` 做重点记录。
- 每条事件记录 PID、PPID、ImageName、CommandLine、ParentCommandLine、StartTime。

### Watcher / cleanup 日志

watcher log 需要写清楚：

- monitor session 启动命令行。
- report generation 启动命令行。
- legacy cleanup 是否运行。
- GameLite enter/exit 是否运行。
- web bridge/native watcher 是否启动，以及 PID。
- 每个外部进程启动的 FileName、Arguments、WorkingDirectory、PID。

这样下一次看到报告里的 `powershell`，可以直接从 PID 反查父进程和命令行，而不是只能按进程名猜。

## 最终判断

PARTIAL。

本 run 可以确认：

- 报告中的 `powershell` 是真实采样结果。
- 它被展示为“后台进程 CPU”。
- 它至少包含 6 个 PID，被 `process-samples.csv` 按名称合并。
- 主要尖峰发生在监测开始后 0.4 秒和 5.4 秒附近。

本 run 不能确认：

- powershell 来自哪个软件。
- powershell 来自哪个脚本。
- powershell 是否由 FrameScope、Codex、GameLite、用户脚本或系统任务启动。

原因不是样本不足，而是归因字段缺失。要精确归因，必须在采样时保存 per-PID 父进程、命令行、路径、工作目录、启动时间和进程创建事件。
