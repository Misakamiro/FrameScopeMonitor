# FrameScope Monitor PowerShell CPU Diagnosis

Date: 2026-05-24

Status: PASS

## 结论

本次复现没有发现 FrameScope Monitor 在监测期间周期性启动 `powershell.exe`、`pwsh.exe`、`Get-Counter`、`wmic` 或其它 PowerShell 采样脚本。报告里的“后台进程 CPU”确实会显示 `powershell`，但它代表“监测期间系统里被采样到的后台进程”，不是 FrameScope 自身开销汇总。

本次复现中 CPU 最高的 `powershell` 进程是外部 PowerShell：

- `powershell` PID `20508` 是本次诊断脚本宿主，由 `codex.exe` 启动，再由它启动 `FrameScopeMonitor.exe --monitor-session` 和合成测试进程。
- `powershell` PID `33676` 是 Codex Windows 命令安全层的长期 PowerShell AST parser，本次监测开始前已经存在。
- `powershell` PID `35280` 是 `gamelite-auto-lightweight\Exit-GameLite.ps1`，父进程是 `WmiPrvSE.exe`，出现在监测停止后，不是 FrameScope 子进程。
- `powershell` PID `19632` / `31932` 是并行的 `tools\Run-Frontend.ps1 build`，出现在报告生成后，不属于本次监测链路。

FrameScope 自身链路在本次监测期间的资源峰值较低：`FrameScopeMonitor` CPU 峰值 `0%`，`FrameScopeProcessSampler` 峰值 `1.8%`，`FrameScopeSystemSampler` 峰值 `5.33%`，`PresentMon-2.4.1-x64` 峰值 `0%`。因此，本次证据支持的根因是：报告图表把系统中已有或并行运行的 PowerShell 作为后台进程采样显示出来，用户看到的是被监测环境的后台负载，不是 FrameScope 自己常驻 PowerShell 造成的开销。

## 复现范围

源码根目录：

```text
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d
```

证据目录：

```text
artifacts\diagnostics\2026-05-24-powershell-cpu
```

复现 run：

```text
artifacts\diagnostics\2026-05-24-powershell-cpu\runs\SyntheticPUBG-20260524-004352
```

复现方式：

1. 使用已发布的 `FrameScopeMonitor.exe`。
2. 编译并启动合成 `TslGame.exe` 测试进程，避免使用 `Run-PubgSimulation.ps1` 这种 PowerShell 外层脚本干扰判断。
3. 启动 `FrameScopeMonitor.exe --monitor-session`，采样间隔为 `100ms`，慢采样间隔为 `1000ms`。
4. 分别记录启动前、监测中、监测停止后、报告生成后的进程列表和 CPU 采样。
5. 使用生成的 `process-samples.csv`、`topcpu-samples.csv`、`topio-samples.csv`、`system-samples.csv` 和进程快照交叉核对。

本次监测结果：

| 项目 | 值 |
|---|---:|
| Monitor PID | `38324` |
| Monitor exit code | `0` |
| Game PID | `36336` |
| Report exit code | `0` |
| PresentMon capture mode | `process_name` |
| PresentMon target | `TslGame.exe;TslGame-Win64-Shipping.exe` |
| process samples | `105` |
| system samples | `12` |
| presentmon.csv rows | `0` |
| frame capture status | `no-presentmon-csv` |

说明：本次 PresentMon 没有写出帧 CSV，`presentmon.stderr.log` 有 `warning: 99890 ETW events were lost.`；但本任务诊断的是 PowerShell CPU 归因，进程采样和系统采样已经完整生成，足够判断 PowerShell 是否由 FrameScope 监测链路启动。

## 进程快照

本次保存了四个快照：

```text
artifacts\diagnostics\2026-05-24-powershell-cpu\process-list-before.json
artifacts\diagnostics\2026-05-24-powershell-cpu\process-list-during-monitoring.json
artifacts\diagnostics\2026-05-24-powershell-cpu\process-list-after-monitor-stop-before-report.json
artifacts\diagnostics\2026-05-24-powershell-cpu\process-list-after-report.json
```

关键进程树：

| 阶段 | 进程 | PID | 父 PID / 父进程 | 工作目录 | 归因 |
|---|---:|---:|---|---|---|
| before | `powershell.exe` | `20508` | `35516` / `codex.exe` | repo root | 本次诊断脚本宿主，监测前已存在 |
| before | `powershell.exe` | `33676` | `35516` / `codex.exe` | 空 | Codex 长期 PowerShell AST parser，监测前已存在 |
| during | `FrameScopeMonitor.exe` | `38324` | `20508` / `powershell.exe` | repo root | 本次被测 FrameScope monitor session |
| during | `logman.exe` | `34600` | `38324` / `FrameScopeMonitor.exe` | 空 | FrameScope 用于查询/清理 PresentMon ETW session 的瞬时子进程 |
| during | `TslGame.exe` | `36336` | `20508` / `powershell.exe` | repo root | 合成游戏进程 |
| after stop | `powershell.exe` | `35280` | `5948` / `WmiPrvSE.exe` | `..\gamelite-auto-lightweight` | GameLite 退出脚本，不是 FrameScope 子进程 |
| after report | `powershell.exe` | `19632` | `35516` / `codex.exe` | `.\tools` | 并行前端 build 外部任务 |
| after report | `powershell.exe` | `31932` | `19632` / `powershell.exe` | `.\tools` | `Run-Frontend.ps1 build` 子进程 |

## 高 CPU PowerShell 细节

`topcpu-samples.csv` 捕获到的 `powershell` PID 峰值：

| PID | CPU 峰值 | 内存峰值 | 命令行 / 父进程 / 启动时间 / 工作目录 |
|---:|---:|---:|---|
| `33204` | `9.75%` | `90.2 MB` | 该进程只在 top CPU 采样中出现，快照未捕获到命令行、父进程、启动时间和工作目录；采样时已经能证明它是系统后台进程之一，但不能从现有证据恢复完整命令行。 |
| `20508` | `5.6%` | `174.9 MB` | 父进程 `35516` / `codex.exe`；命令行为本次诊断脚本 PowerShell，包含创建 `artifacts\diagnostics\2026-05-24-powershell-cpu`、启动合成 `TslGame.exe`、启动 `FrameScopeMonitor.exe --monitor-session`、采集进程快照等逻辑；工作目录 repo root；启动时间在快照中未返回，可确定在 `2026-05-24 00:43:47 +08:00` 前已存在。 |
| `42348` | `3.53%` | `113.1 MB` | 与 PID `33204` 一样，只在 top CPU 采样中出现，快照未捕获到完整命令行、父进程、启动时间和工作目录。 |
| `33676` | 非 top 峰值主因 | 当前约 `119.3 MB` | 父进程 `35516` / `codex.exe`；命令行为 `-NoLogo -NoProfile -NonInteractive -EncodedCommand ...`，内容是 Codex Windows 命令安全层长期 PowerShell AST parser；当前查询到启动时间 `2026-05-24T00:23:36.3481728+08:00`；监测前已存在。 |
| `35280` | 监测后出现 | 未作为监测期峰值 | 父进程 `5948` / `WmiPrvSE.exe`；命令行为 `-File "...gamelite-auto-lightweight\Exit-GameLite.ps1" -RequireNoActiveGame -ExitGraceSeconds 8`；工作目录 `C:\Users\misakamiro\Documents\Codex\2026-05-02\gamelite-auto-lightweight`；不是 FrameScope monitor session 子进程。 |
| `19632` / `31932` | 报告后出现 | 未作为监测期峰值 | 命令行为 `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 build`；并行外部构建任务，不属于本次监测链路。 |

判断重点：

- FrameScope monitor session 的 PID 是 `38324`，它的子进程树中本次只看到 `logman.exe` 这类瞬时 ETW 工具，没有看到 `powershell.exe` 子进程。
- CPU 峰值最高的 `powershell` PID `33204` 和 `42348` 没有被进程快照捕获到命令行，说明它们生命周期较短；现有证据不能还原完整命令行，但源码扫描和已捕获进程树均不支持“FrameScope 周期性启动 PowerShell”这个假设。
- `process-samples.csv` 是按进程名聚合的，所以报告里看到的 `powershell` 可能同时包含多个 PID；这会把外部 PowerShell 的 CPU、内存、IO 汇总到同一个 `powershell` 名称下。

## FrameScope 自身资源占用

来自本次 run 的 `process-samples.csv`、`topcpu-samples.csv`、`topio-samples.csv`：

| 进程 | PID / 口径 | CPU 峰值 | CPU 平均 | 内存峰值 | 读峰值 | 写峰值 | 判断 |
|---|---|---:|---:|---:|---:|---:|---|
| `FrameScopeMonitor` | name aggregate / PID `38324` | `0%` | `0%` | `28.4 MB` | `0.001 MB/s` | `0.001 MB/s` | 主控进程开销很低 |
| `FrameScopeProcessSampler` | PID `42744` | `1.8%` | `0.39%` | `30.0 MB` | `0 MB/s` | `0.93 MB/s` | 100ms 进程采样，有轻微写 CSV 开销 |
| `FrameScopeSystemSampler` | PID `42040` | `5.33%` | `0.22%` | `40.9 MB` | `1.224 MB/s` | `0.007 MB/s` | 慢采样器，峰值较短 |
| `PresentMon-2.4.1-x64` | PID `35044` during run | `0%` | `0%` | `13.1 MB` | `0 MB/s` | `0 MB/s` | 本次未写出 presentmon.csv |
| `FrameScopeReportGenerator` | 报告生成阶段 | 本次报告生成进程未形成高 CPU 证据 | - | - | - | - | 报告生成不是监测期 PowerShell 来源 |
| `WmiPrvSE` | 系统 WMI provider | `2.65%` | `0.11%` | `116.4 MB` | `0 MB/s` | `0 MB/s` | 系统 WMI provider，不是 FrameScope PowerShell |
| `powershell` | name aggregate | `9.75%` | `0.37%` | `406.6 MB` | `2.505 MB/s` | `2.516 MB/s` | 外部/系统 PowerShell 被后台进程图表采样到 |

系统总 CPU：

| 指标 | 值 |
|---|---:|
| system samples | `12` |
| Total CPU min | `6.79%` |
| Total CPU max | `18.91%` |
| Total CPU avg | `13.70%` |

## 源码扫描结论

扫描范围包括 `src`、`build.ps1`、`scripts`、`tests` 中的 C# / PowerShell / cmd 文件，重点查找 `powershell`、`pwsh`、`Get-Counter`、`wmic`、`logman`、`ProcessStartInfo`、`ManagementObjectSearcher`、`nvidia-smi`。

监测期间实际启动路径：

- `src\app\FrameScopeNativeMonitor.MonitorSession.cs`
  - 解析 `--SampleIntervalMs`、`--ProcessSampleIntervalMs`、`--SlowSampleIntervalMs`。
  - 监测会话启动 `PresentMon`、`FrameScopeProcessSampler.exe`、`FrameScopeSystemSampler.exe`。
  - `processSampleIntervalMs` 下限为 `100ms`，`slowSampleIntervalMs` 通常为 `1000ms`。
- `src\app\FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs`
  - `StartNativeMonitorChild(...)` 使用 `ProcessStartInfo` 启动子进程，默认 `CreateNoWindow=true`，并把优先级设为 `Idle`。
  - 本次路径没有发现 `powershell.exe` 或 `pwsh.exe` 作为子进程被启动。
- `src\app\FrameScopeNativeMonitor.MonitorSession.PresentMon.cs`
  - 会用 `logman.exe query -ets` 查询 ETW session。
  - 必要时会用 `logman.exe stop <session> -ets` 停止遗留 ETW session。
  - 这是外部命令，但不是 PowerShell，也不是周期性高频采样。
- `src\monitoring\FrameScopeProcessSampler.cs`
  - 使用 `.NET Process.GetProcesses()`、`TotalProcessorTime`、`WorkingSet64` 和 native IO counter 采样进程。
  - 没有启动 PowerShell、WMI、Get-Counter 或外部命令。
- `src\monitoring\FrameScopeSystemSampler.cs`
  - 使用 .NET `PerformanceCounter` 读取系统指标。
  - 没有 `Get-Counter` PowerShell 调用。
- `src\monitoring\FrameScopeSystemSampler.Gpu.cs`
  - 如果找到 `nvidia-smi.exe`，会按慢采样路径调用它读取 GPU 数据。
  - 这是 GPU 查询外部工具，不是 PowerShell。
- `src\reporting\FrameScopeReportGenerator.Metadata.cs`
  - 报告生成阶段使用 `ManagementObjectSearcher` 查询 CPU、OS、GPU 元数据。
  - 这是报告生成阶段的 WMI 元数据读取，不是监测期间周期性 PowerShell。
- `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs`
  - Watcher 在监测完成后启动 `FrameScopeReportGenerator.exe`。
  - 没有启动 PowerShell。
- `src\app\FrameScopeNativeMonitor.ProcessCleanup.cs`
  - 只在遗留清理逻辑里识别旧版 `powershell.exe`，条件是命令行包含 `framescopewatcher.ps1` 或 `monitor-cs2-highfreq.ps1`。
  - 这里是清理旧版残留的识别逻辑，不是当前 native C# monitor session 的启动路径。

结论：当前 native C# 监测链路不是旧版 PowerShell/Python 监测壳。它的监测期子进程应是 `PresentMon`、`FrameScopeProcessSampler.exe`、`FrameScopeSystemSampler.exe`，外加瞬时 `logman.exe`；没有证据显示它周期性启动 PowerShell。

## 根因

本次问题的根因不是 FrameScope 自身 CPU 过高，而是报告里的“后台进程 CPU”图表按系统后台进程采样展示。这个图表的语义是：

```text
监测期间，系统里有哪些进程占用了 CPU/内存/IO
```

不是：

```text
FrameScope 自己消耗了多少 CPU/内存/IO
```

因此，如果监测期间系统中有 Codex、GameLite、用户手动运行的脚本、前端构建脚本、其它软件启动的 PowerShell，报告会把这些进程显示为 `powershell`。因为 `process-samples.csv` 按进程名聚合，多个 PowerShell PID 还可能合并成一个 `powershell` 曲线或排行项，看起来像“一个 PowerShell 很高”，实际可能是多个不同来源的 PowerShell 被同名归并。

## 是否由 FrameScope 导致

当前证据判断：否。

理由：

1. 进程树中 `FrameScopeMonitor.exe` PID `38324` 的已捕获子进程没有 `powershell.exe`。
2. 监测期间出现的 `powershell.exe` PID `20508` 和 `33676` 父进程都是 `codex.exe`，且监测前已经存在。
3. 监测停止后的 `powershell.exe` PID `35280` 父进程是 `WmiPrvSE.exe`，命令行指向 `gamelite-auto-lightweight\Exit-GameLite.ps1`，不是 FrameScope monitor session。
4. 报告后的 `powershell.exe` PID `19632` / `31932` 是 `tools\Run-Frontend.ps1 build`，与本次监测无关。
5. 源码扫描没有发现当前 native 监测路径周期性启动 PowerShell、`Get-Counter`、`wmic` 或 PowerShell WMI 命令。

限制：PID `33204` 和 `42348` 是短生命周期 PowerShell，只被 `topcpu-samples.csv` 捕获到 CPU 峰值，进程快照没来得及抓到命令行和父进程。因此不能对这两个 PID 单独还原完整命令行。但结合源码和其它快照，当前没有证据支持它们是 FrameScope 创建的子进程。

## 修复建议

本次不建议做性能修复，因为没有证据显示 FrameScope 自身引起 PowerShell 高 CPU。

如果后续要降低用户误解，最小产品改进方案是报告层标注语义，而不是改采样核心：

1. 在报告“后台进程监测”旁边增加说明：这里显示的是系统后台进程，不等于 FrameScope 自身开销。
2. 在数据层或报告层把 `FrameScopeMonitor`、`FrameScopeProcessSampler`、`FrameScopeSystemSampler`、`FrameScopeReportGenerator`、`PresentMon` 单独归类为“FrameScope 自身组件”。
3. 对 `powershell` 这类同名进程，后续可以在采样 CSV 中增加 PID 维度或在报告 tooltip 中显示 PID 列表，避免多个 PowerShell 被同名合并后产生误读。

这些属于解释性/可观测性改进，不是当前必须修复的性能 bug。按用户要求，本次没有改 UI、没有改代码、没有打包、没有推 GitHub。

## 还需要用户提供的证据

如果用户说的高 `powershell` 来自另一份真实游戏报告，而不是本次复现，需要提供原始 run 目录。最有用的文件是：

```text
process-samples.csv
topcpu-samples.csv
topio-samples.csv
system-samples.csv
status.json
summary.json
charts\framescope-interactive-manifest.json
```

如果问题正在发生，建议同时抓一次实时进程证据：

```powershell
Get-CimInstance Win32_Process |
  Where-Object { $_.Name -match 'powershell|pwsh|FrameScope|PresentMon|logman|WmiPrvSE' } |
  Select-Object ProcessId,ParentProcessId,Name,CreationDate,ExecutablePath,CommandLine |
  ConvertTo-Json -Depth 4
```

需要特别说明：旧报告里的 `process-samples.csv` 只有采样数据，不一定包含命令行、父进程和启动时间。如果当时没有同步保存进程快照，事后无法从 HTML 报告里完整恢复某个短生命周期 PowerShell 的命令行。

## 本次交付物

诊断报告：

```text
docs\diagnostics\2026-05-24-framescope-powershell-cpu-diagnosis.md
```

证据目录：

```text
artifacts\diagnostics\2026-05-24-powershell-cpu
```

本次没有修改源码。
