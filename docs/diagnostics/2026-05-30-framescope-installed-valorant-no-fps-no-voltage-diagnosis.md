# FrameScope Monitor 安装版 Valorant 无 FPS / CPU 电压 / VID 诊断

Date: 2026-05-30

Status: PASS（诊断完成；本报告只读检查现有 run 和安装态，未改源码、未构建、未打包、未安装、未推送、未启动新游戏）

Run:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260530-123151
```

Installed app:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor
```

Source report path:

```text
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\docs\diagnostics\2026-05-30-framescope-installed-valorant-no-fps-no-voltage-diagnosis.md
```

## 总结判定

| 检查项 | 结果 | 结论 |
| --- | --- | --- |
| FPS / 帧数数据 | FAIL | PresentMon 已启动并退出码为 0，但本次没有创建 `presentmon.csv`，所以报告端没有任何帧行可读。 |
| FPS 根因分类 | PARTIAL | 可确认直接失败点是 `missing-presentmon-csv`；现有 artifacts 不能继续区分 Valorant 反作弊/全屏/覆盖层/ETW 静默阻断等更深层原因，因为 stderr 为空且退出码为 0。 |
| 目标进程匹配 | PASS | `TargetResolvedProcess=VALORANT-Win64-Shipping.exe`，`InitialTargetPid=27492`，`process-samples.csv` 中同一 PID 有 108 个采样点。 |
| ETW access denied 证据 | PASS（未命中） | `presentmon.stderr.log` 没有 `access denied`、`failed to start trace session`、`no process found`、ETW/session/start failure 文本；`PresentMonEtwAccessDenied=false`。不能把 BF6 的 access denied 结论套到这次 Valorant。 |
| Report generator | PASS | generator 正确生成 diagnostic 报告：`frames=0`、`rawPresentMonRows=0`、`validPresentMonRows=0`、`reportKind=diagnostic`。这不是 CSV 有数据但报告没读。 |
| CPU core frequency | PASS | `cpu-core-samples.csv` 存在，1712 行，16 个 logical processor，频率范围 4128-5047 MHz。CPU 核心频率没有丢。 |
| 真实 per-core CPU voltage | PASS（符合本机现状） | `cpu-voltage-samples.csv` 存在 321 行，但全部是 non-per-core `Vcore` / `Vcore SoC` / `Vcore Misc`。`CpuVoltagePerCoreSampleCount=0`，因此真实 per-core Vcore 没有图是正确语义，不是采样器完全失败。 |
| CPU Core VID | PASS | `cpu-vid-samples.csv` 存在 856 行，Core #1 VID 到 Core #8 VID 每个 107 行；`CpuVidAvailable=true`，`CpuVidStatus=core-vid-available`。这次 VID 没有丢。 |
| 安装包依赖 / provider | PASS | `LibreHardwareMonitorLib.dll` 和相关 DLL 存在；run 内 voltage/VID 数据源均为 `builtin-librehardwaremonitor`，证明 bundled provider 已加载。 |
| 配置影响 | PASS | installed config 未禁用 CPU telemetry；Valorant target 启用且进程名正确；`TelemetrySampleIntervalMs=1000`，不要和控制轮询字段混淆。 |

## FPS 直接原因

本次 Valorant run 没有 FPS 的直接原因是：PresentMon 进程启动过，但没有创建 `presentmon.csv`。

关键证据：

- `status.json`: `FrameCaptureStatus=no-presentmon-csv`
- `status.json`: `PresentMonFailureCategory=missing-presentmon-csv`
- `status.json`: `PresentMonCsvExists=false`
- `status.json`: `PresentMonCsvBytes=0`
- `status.json`: `PresentMonCsvRows=0`
- `status.json`: `PresentMonExitCode=0`
- `status.json`: `PresentMonStdoutTail=Started recording.`
- `presentmon.csv`: 文件不存在
- `presentmon.stdout.log`: 1 行，内容为 `Started recording.`
- `presentmon.stderr.log`: 3 bytes，0 行，无可读错误文本

PresentMon 启动参数如下：

```text
--process_id 27492 --output_file C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260530-123151\presentmon.csv --date_time --terminate_on_proc_exit --no_console_stats --stop_existing_session --session_name FrameScopeNativePresentMon_Valorant
```

这排除了几个常见错误：

- 不是 PresentMon 可执行文件缺失：`PresentMonPreflightToolExists=true`，实际路径是 `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\tools\PresentMon-2.4.1-x64.exe`。
- 不是显式 ETW access denied：stderr 没有 `access denied` / `failed to start trace session`，`PresentMonEtwAccessDenied=false`。
- 不是目标进程没匹配：run 锁定 `VALORANT-Win64-Shipping.exe` PID `27492`。
- 不是 CSV 有数据但 report generator 没读：CSV 根本不存在，manifest 和 report log 都是 `rawPresentMonRows=0`。
- 不是 manifest/data 写坏：manifest、data.js、HTML 三者都一致表达 diagnostic report。

现有 artifacts 不能证明更深层原因是哪一种。最严谨的分类只能到：

```text
PresentMon launched -> no stderr error -> exit code 0 -> presentmon.csv not created -> report has no frame data
```

因此，这次 FPS 不能归因到 BF6 那种已证实的 ETW access denied；也不能归因到报告生成失败。更深层可能仍包括 Valorant/反作弊/全屏/覆盖层/ETW 静默限制，或 PresentMon 对该 PID 没收到 presentation events，但本次日志没有足够证据继续细分。

注意：`FrameCaptureMessage` 文案里仍写了 “PUBG 场景下通常...”。这只是诊断文案不够按 target 命名，不是本次无 FPS 的数据根因。

## Target process 匹配检查

`framescope-config.json` 中 Valorant target：

```json
{
  "Enabled": true,
  "Name": "Valorant",
  "ProcessName": "VALORANT-Win64-Shipping.exe",
  "SampleIntervalMs": 1000,
  "ProcessSampleIntervalMs": 1000,
  "SlowSampleIntervalMs": 1000,
  "OpenReportOnComplete": true,
  "ProcessSamplingMode": "normal"
}
```

run 状态：

- `TargetProcess=VALORANT-Win64-Shipping.exe`
- `TargetResolvedProcess=VALORANT-Win64-Shipping.exe`
- `InitialTargetPid=27492`
- `TargetPid=27492`
- `PresentMonCaptureMode=process_id`
- `PresentMonCaptureTarget=27492`
- `TargetProcessCandidates=VALORANT-Win64-Shipping`

`process-samples.csv` 验证：

- 总行数：11484
- `VALORANT-Win64-Shipping`：108 行，PID `27492`
- `VALORANT` launcher：108 行，PID `32548`
- `system-samples.csv`: 107 行，`TargetRunning=True` 为 107 行，`TargetRunning=False` 为 0 行

结论：目标进程匹配是 PASS，不是这次 FPS 缺失的根因。

## Report generator 检查

`report-generation.log` 和 `charts\framescope-interactive-manifest.json` 一致：

- `frames=0`
- `rawPresentMonRows=0`
- `validPresentMonRows=0`
- `presentMonSelectionMode=missing`
- `hasFrameData=false`
- `reportKind=diagnostic`
- `frameCaptureStatus=no-presentmon-csv`
- `presentMonFailureCategory=missing-presentmon-csv`
- `presentMonEtwAccessDenied=false`
- `ReportGenerationExitCode=0`

`charts\framescope-interactive-data.js` 中：

- `counts.frames=0`
- `presentMon.rawRows=0`
- `presentMon.validRows=0`
- `fps.t=[]`
- `fps.avg=[]`
- `fps.low1=[]`
- `fps.low01=[]`
- `fps.min=[]`

结论：report generator 没有丢帧数据；它只是忠实反映 PresentMon 没有产出 CSV。

## CPU telemetry 检查

### CPU core frequency

`cpu-core-samples.csv`：

- 存在
- 1713 行，其中 1712 行数据
- `Source=windows-perfcounter`
- logical processor：0-15
- `ActualFrequencyMHz` 范围：4128-5047 MHz

`cpu-core-telemetry-status.json`：

- `CpuCoreTelemetryAvailable=true`
- `CpuCoreSampleCount=1712`
- `CpuCoreLogicalProcessorCount=16`

结论：CPU 核心频率没有丢。

### 真实 per-core CPU voltage

`cpu-voltage-samples.csv`：

- 存在
- 322 行，其中 321 行数据
- `Source=builtin-librehardwaremonitor`
- `Provider=built-in`
- `Status=non-per-core`
- 传感器只有：
  - `Vcore`: 107 行
  - `Vcore SoC`: 107 行
  - `Vcore Misc`: 107 行

`cpu-voltage-telemetry-status.json`：

- `CpuVoltageTelemetryEnabled=true`
- `CpuVoltageProviderRequested=auto`
- `CpuVoltageProviderKind=built-in`
- `CpuVoltageTelemetrySource=builtin-librehardwaremonitor`
- `CpuVoltageAvailable=false`
- `CpuVoltagePerCoreAvailable=false`
- `CpuVoltageNonPerCoreAvailable=true`
- `CpuVoltageStatus=non-per-core-only`
- `CpuVoltageSampleCount=321`
- `CpuVoltagePerCoreSampleCount=0`
- `CpuVoltageNonPerCoreSampleCount=321`
- reason: `仅检测到 non-per-core CPU 电压传感器；图表只显示真实 per-core voltage。`

结论：真实 per-core Vcore 没有数据是符合这台机器之前的硬件/provider 状态的。FrameScope 不应该把 `VID`、aggregate `Vcore`、`Vcore SoC`、`Vcore Misc`、package/SOC 之类的值伪装成真实 per-core Vcore。此处 PASS。

### CPU Core VID

`cpu-vid-samples.csv`：

- 存在
- 857 行，其中 856 行数据
- `Source=builtin-librehardwaremonitor`
- `Provider=built-in`
- `Status=core-vid`
- 传感器：
  - `Core #1 VID`: 107 行
  - `Core #2 VID`: 107 行
  - `Core #3 VID`: 107 行
  - `Core #4 VID`: 107 行
  - `Core #5 VID`: 107 行
  - `Core #6 VID`: 107 行
  - `Core #7 VID`: 107 行
  - `Core #8 VID`: 107 行

`cpu-vid-telemetry-status.json`：

- `CpuVidTelemetryEnabled=true`
- `CpuVidProviderRequested=auto`
- `CpuVidProviderKind=built-in`
- `CpuVidTelemetrySource=builtin-librehardwaremonitor`
- `CpuVidAvailable=true`
- `CpuVidStatus=core-vid-available`
- `CpuVidSampleIntervalMs=1000`
- `CpuVidSampleCount=856`
- `CpuVidCoreCount=8`
- note: `VID 是 CPU 请求/目标电压，不是真实 per-core Vcore。`

结论：CPU Core VID 这次没有丢。它不是安装包缺 DLL、不是 provider 加载失败、不是采样器参数没打开、不是 config 禁用、也不是 report generator 没读取。若用户看到 “CPU 电压” 无图，那是因为真实 per-core Vcore 没有；应切到独立的 `CPU Core VID` 视图理解 VID 数据。

## 安装态检查

安装目录关键文件：

| 文件 | 状态 |
| --- | --- |
| `FrameScopeMonitor.exe` | 存在，313344 bytes，2026-05-30 07:43:52 |
| `FrameScopeSystemSampler.exe` | 存在，53248 bytes，2026-05-30 07:43:52 |
| `FrameScopeReportGenerator.exe` | 存在，155136 bytes，2026-05-30 07:43:52 |
| `LibreHardwareMonitorLib.dll` | 存在，1203200 bytes，2026-02-14 19:16:28 |
| `tools\PresentMon-2.4.1-x64.exe` | 存在，927304 bytes，2026-05-03 01:15:16 |

安装目录相关 DLL 存在：

```text
BlackSharp.Core.dll
DiskInfoToolkit.dll
HidSharp.dll
LibreHardwareMonitorLib.dll
Microsoft.Web.WebView2.Core.dll
Microsoft.Web.WebView2.WinForms.dll
RAMSPDToolkit-NDD.dll
System.Buffers.dll
System.CodeDom.dll
System.Memory.dll
System.Numerics.Vectors.dll
System.Runtime.CompilerServices.Unsafe.dll
System.Security.AccessControl.dll
System.Security.Principal.Windows.dll
System.Threading.AccessControl.dll
WebView2Loader.dll
```

`PresentMon*.exe` 在安装目录 root 下没有直接命中，但 run 使用的路径是：

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\tools\PresentMon-2.4.1-x64.exe
```

这是实际存在并被 status 记录使用的路径。

`install.log` 最近一次安装：

- `2026-05-30T07:46:10.4813017+08:00 install-start`
- `fullPackage=True`
- `webview2-runtime available=True`
- `2026-05-30T07:46:10.5718145+08:00 install-complete`

结论：安装态没有显示缺少 `LibreHardwareMonitorLib.dll` 或明显 DLL 缺失。CPU VID / voltage 文件的 provider 字段也证明 SystemSampler 已能加载 bundled LibreHardwareMonitor provider。

## installed config 检查

`framescope-config.json` 关键字段：

- `PollIntervalMs=1000`
- `TelemetrySampleIntervalMs=1000`
- `MonitorScript=native-csharp`
- `CpuTelemetry.CollectPerCoreFrequency=true`
- `CpuTelemetry.CollectCpuVoltage=true`
- `CpuTelemetry.PerCoreSampleIntervalMs=1000`
- `CpuTelemetry.PerCoreVoltageSampleIntervalMs=1000`
- `CpuTelemetry.VoltageProvider=auto`

配置里没有独立的 Core VID 开关字段；run 状态里实际启用了：

- `CpuVidTelemetryEnabled=true`
- `CpuVidSampleIntervalMs=1000`
- `CpuVidProvider=auto`

本次 run 的实际采样间隔：

- `SampleIntervalMs=1000`
- `ProcessSampleIntervalMs=1000`
- `SlowSampleIntervalMs=1000`
- `CpuCoreSampleIntervalMs=1000`
- `CpuVoltageSampleIntervalMs=1000`
- `CpuVidSampleIntervalMs=1000`
- `ControlPollIntervalMs=3000`

结论：旧配置没有把 CPU telemetry 或 Valorant target 禁用。`PollIntervalMs` / `ControlPollIntervalMs` 不是本次 CPU telemetry 采样间隔的失败点；CPU telemetry 的有效间隔是各 telemetry sample interval，均为 1000 ms。

## 必检文件结果

| 文件 | 状态 | 大小 / 行数 |
| --- | --- | --- |
| `status.json` | PASS | 7871 bytes，1 行 |
| `summary.json` | PASS | 6140 bytes，1 行 |
| `presentmon.csv` | FAIL | 不存在 |
| `presentmon.stdout.log` | PASS | 23 bytes，1 行 |
| `presentmon.stderr.log` | PASS（空错误） | 3 bytes，0 行 |
| `presentmon-info.json` | PASS | 283 bytes，1 行 |
| `process-samples.csv` | PASS | 1017961 bytes，11485 行，11484 数据行 |
| `system-samples.csv` | PASS | 17177 bytes，108 行，107 数据行 |
| `cpu-core-samples.csv` | PASS | 169560 bytes，1713 行，1712 数据行 |
| `cpu-core-telemetry-status.json` | PASS | 748 bytes，1 行 |
| `cpu-voltage-samples.csv` | PASS | 60799 bytes，322 行，321 数据行 |
| `cpu-voltage-telemetry-status.json` | PASS | 854 bytes，1 行 |
| `cpu-vid-samples.csv` | PASS | 173723 bytes，857 行，856 数据行 |
| `cpu-vid-telemetry-status.json` | PASS | 638 bytes，1 行 |
| `report-generation.log` | PASS | 2693 bytes，1 行 |
| `charts\framescope-interactive-manifest.json` | PASS | 2688 bytes，1 行 |
| `charts\framescope-interactive-data.js` | PASS | 57960 bytes，1 行 |
| `charts\framescope-interactive-report.html` | PASS | 45021 bytes |

## 是否需要实现修复

FPS：

- 若目标只是解释本次 run：不需要立刻改代码；现有证据已经能确认直接原因是 `presentmon.csv` 没创建。
- 若目标是让 Valorant 捕获成功：需要后续单独做 capture 策略或权限/反作弊兼容验证，因为本次 artifacts 无法告诉我们更深层原因。
- 若目标是提高诊断质量：建议后续修复 target-specific 诊断文案，避免 Valorant 报告出现 `PUBG 场景下通常...`；也可以增加 PresentMon 静默无 CSV 时的更细分状态，但这不是本轮授权范围。

CPU voltage / VID：

- 真实 per-core Vcore 无数据不需要修复；这是本机/provider 只暴露 non-per-core voltage 的正确表现。
- CPU Core VID 本次已经存在且 report manifest/data 已读取，不需要实现修复。
- 不能把 VID、aggregate Vcore、SOC、Package 当作真实 per-core Vcore 显示。

## 是否需要重新本机安装验证

- 为了解释本次 CPU voltage / VID：不需要。安装依赖存在，bundled provider 已加载，VID 数据已采到。
- 为了解释本次 FPS 的直接原因：不需要重新安装；现有 run 已足够证明 `presentmon.csv` 未创建。
- 如果后续实现 FPS capture 兼容、PresentMon 参数、权限流程或诊断文案修复，则需要重新本机安装版验证，并且需要新的 Valorant run 才能证明修复是否有效。

## 残留进程检查

检查匹配进程：

```text
FrameScopeMonitor
FrameScopeSystemSampler
FrameScopeReportGenerator
FrameScopeProcessSampler
PresentMon
VALORANT
VALORANT-Win64-Shipping
RiotClientServices
```

结果：

- 仍有 `FrameScopeMonitor.exe` 运行：PID `23364`，路径 `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe`
- 未发现 `PresentMon`
- 未发现 `FrameScopeSystemSampler`
- 未发现 `FrameScopeReportGenerator`
- 未发现 `FrameScopeProcessSampler`
- 未发现 `VALORANT`
- 未发现 `VALORANT-Win64-Shipping`
- 未发现 `RiotClientServices`

结论：采集子进程和游戏进程没有残留；只剩安装版 UI 主进程仍在运行。

## 最终结论

这次 Valorant run 没有 FPS 的直接原因是 PresentMon 没有创建 `presentmon.csv`。它不是目标进程匹配失败，不是已证实的 ETW access denied，不是 report generator 读坏，也不是 manifest/data 写坏。更深层原因需要新的 Valorant capture 验证才能继续区分。

CPU 真实 per-core voltage 没有数据符合本机硬件/provider 现状：这次只采到了 non-per-core `Vcore` / `Vcore SoC` / `Vcore Misc`，FrameScope 正确没有把它们画成真实 per-core Vcore。

CPU Core VID 这次是有数据的：`cpu-vid-samples.csv` 存在，Core #1 VID 到 Core #8 VID 每个都有 107 条，总计 856 条。安装包依赖、provider 加载、采样器参数、config 和 report generator 都不是 VID 缺失原因，因为 VID 并没有缺失。
