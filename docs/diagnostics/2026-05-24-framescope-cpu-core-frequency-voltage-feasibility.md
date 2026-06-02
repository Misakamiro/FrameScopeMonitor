# FrameScope CPU 每核心频率和电压采集可行性诊断

日期：2026-05-25
输出文件：`docs\diagnostics\2026-05-24-framescope-cpu-core-frequency-voltage-feasibility.md`
范围：只写诊断报告；未改源码、未构建、未打包、未推 GitHub。

## 结论

状态：PARTIAL

CPU 每逻辑处理器频率采集可行。当前这台 Windows 机器可以通过系统自带的 `Processor Information` 性能计数器读到 16 个逻辑处理器的 `Actual Frequency`，本机验证时能看到大约 4.1-4.8 GHz 的动态变化。

CPU 电压采集只能作为可选能力，不能承诺每台机器都能拿到。标准 Windows WMI / PerformanceCounter 没有提供可靠的实时 VID、Vcore 或 package voltage；`Win32_Processor.CurrentVoltage` 是 SMBIOS 元数据，不应当当作实时 Vcore。电压如果要做，只能走 LibreHardwareMonitor / OpenHardwareMonitor / HWiNFO 这类硬件传感器路线，并且必须运行时探测、允许缺失、清楚标注来源。

## 当前 FrameScope 数据流

当前 FrameScope 是 native C# 监测链路：

1. `FrameScopeMonitor.exe --monitor-session` 创建 run 目录。
2. 监测会话启动 `PresentMon`、`FrameScopeProcessSampler.exe`、`FrameScopeSystemSampler.exe`。
3. `FrameScopeProcessSampler.exe` 默认 100 ms 写入 `process-samples.csv`、`topcpu-samples.csv`、`topio-samples.csv`、`sample-alerts.csv`。
4. `FrameScopeSystemSampler.exe` 默认 1000 ms 写入 `system-samples.csv`，代码里最低限制是 500 ms。
5. 捕获结束后 watcher 启动 `FrameScopeReportGenerator.exe`。
6. 报告生成器读取 `presentmon.csv`、`system-samples.csv`、`process-samples.csv`，再写入 `charts\framescope-interactive-data.js`、`charts\framescope-interactive-report.html`、`charts\framescope-interactive-manifest.json`。

已检查的关键源码：

- `src\app\FrameScopeNativeMonitor.MonitorSession.cs`：启动 PresentMon、进程采样器、系统采样器。
- `src\app\FrameScopeNativeMonitor.MonitorSession.Paths.cs`：`SamplesCsv = system-samples.csv`，`ProcessCsv = process-samples.csv`。
- `src\app\FrameScopeNativeMonitor.MonitorSession.Status.cs`：把 `SlowSampleIntervalMs`、`SamplesCsv`、`ProcessCsv`、采样器 exe 路径写入 `status.json` / `summary.json`。
- `src\monitoring\FrameScopeSystemSampler.cs`：写 `system-samples.csv` 表头和数据行。
- `src\monitoring\FrameScopeSystemSampler.PerfCounters.cs`：当前只读 `_Total` 的 `Processor Frequency` 和 `% Processor Performance`。
- `src\monitoring\FrameScopeProcessSampler.cs`：写进程维度 CSV，按进程名聚合。
- `src\reporting\FrameScopeReportGenerator.SystemData.cs`：读取系统采样 CSV，并生成 `system.perf.cpuFreq`。
- `src\reporting\FrameScopeReportGenerator.ProcessData.cs`：读取 `process-samples.csv`，生成后台进程 CPU / 内存矩阵。

当前 `system-samples.csv` CPU 相关字段：

```text
Time,SampleIndex,Cs2Running,TargetRunning,TotalCpuPct,CpuFrequencyMHz,CpuPerformancePct,...
```

当前报告里的 CPU 频率逻辑：

- `CpuFrequencyMHz` 读入 `SystemRow.CpuFrequency`。
- `CpuPerformancePct` 读入 `SystemRow.CpuPerformancePct`。
- `system.perf.cpuFreq` 由 `CpuFrequencyMHz * CpuPerformancePct / 100` 得到。
- 这个值是 `_Total` 汇总派生值，不是每核心频率，也不等同于 HWiNFO / Ryzen 工具里的 `Core Effective Clock`。

当前 `process-samples.csv` 不适合承载 CPU 核心频率或电压：

- 它是进程维度数据。
- 它按 `ProcessName` 聚合。
- 它回答的是“哪个后台进程占用资源”，不是“CPU 每个核心/线程的硬件状态”。

## 可用数据源

### 1. PerformanceCounter / Win32_Perf*

结论：频率 PASS，电压 FAIL。

本机可见来源：

- `\Processor Information(*)\Actual Frequency`
- `\Processor Information(*)\Processor Frequency`
- `\Processor Information(*)\% Processor Performance`
- `\Processor Information(*)\% of Maximum Frequency`
- `\Processor Information(*)\% Processor Utility`
- `Win32_PerfFormattedData_Counters_ProcessorInformation`

本机验证结果：

- `Win32_PerfFormattedData_Counters_ProcessorInformation` 有 `ActualFrequency`、`ProcessorFrequency`、`PercentProcessorPerformance`、`PercentofMaximumFrequency`、`PercentProcessorUtility`、`PercentProcessorTime`、`PercentIdleTime`、`PerformanceLimitFlags`。
- `Get-Counter '\Processor Information(*)\Actual Frequency'` 能返回 `0,0` 到 `0,15`，外加 `0,_Total` 和 `_Total`。
- 2026-05-25 00:13:52-00:13:54 +08:00 的短采样里，`Actual Frequency` 大约在 `4142 MHz` 到 `4772 MHz` 之间变化。
- 同一时刻 `Processor Frequency` 基本固定为 `4200 MHz`，而 `% Processor Performance` 大约在 `98%` 到 `114%` 之间变化。

判断：

- `Actual Frequency` 是当前 Windows 原生路线里最可靠的每逻辑处理器动态频率来源。
- `Processor Frequency` 单独不够，因为本机 7800X3D 上它固定返回 `4200 MHz`。
- 当前 FrameScope 的 `_Total` 频率派生值有参考意义，但不能宣称为 per-core frequency。
- Windows 实例名是逻辑处理器，不是物理核心 ID。本机 8 核 16 线程会看到 16 条逻辑处理器曲线。第一版应标注为 `logical processor`，不要直接写成物理核心。

代价：

- 低。
- 可以沿用当前 C# `PerformanceCounter` 风格。
- 本机读取不需要管理员。
- 不需要新驱动。
- 16 个逻辑处理器按 1 Hz 采样，开销应很低。

限制：

- 不同 Windows 版本、CPU、BIOS、性能计数器状态可能导致 `Actual Frequency` 不存在或不可读。
- `Actual Frequency` 不等同于 `Core Effective Clock`。有效频率通常还要考虑 C-state / residency，核心睡眠时会明显低于 active clock。
- 这个来源不提供 VID、Vcore、SoC voltage、package voltage。

### 2. Win32_Processor / 基础 WMI

结论：硬件身份 PASS，实时频率弱，实时电压 FAIL。

本机验证结果：

```text
Name: AMD Ryzen 7 7800X3D 8-Core Processor
Manufacturer: AuthenticAMD
NumberOfCores: 8
NumberOfLogicalProcessors: 16
MaxClockSpeed: 4200
CurrentClockSpeed: 4200
CurrentVoltage: 13
VoltageCaps: null
```

判断：

- `Win32_Processor` 可用于 CPU 名称、核心数、线程数、标称频率。
- `CurrentClockSpeed` / `MaxClockSpeed` 都是 `4200`，不能反映动态 boost。
- Microsoft 文档说明 `CurrentVoltage` 来自 SMBIOS processor voltage，只有 SMBIOS 指定电压值时才设置。
- 所以 `CurrentVoltage = 13` 不能当作 “1.3 V 实时 Vcore” 写进 FrameScope。
- `VoltageCaps = null` 表示这一层连电压能力也未知。

### 3. Win32_VoltageProbe / CurrentProbe / TemperatureProbe / Power/Thermal

结论：本机 FAIL。

本机验证结果：

- `Win32_VoltageProbe`：0 个实例。
- `Win32_CurrentProbe`：0 个实例。
- `Win32_TemperatureProbe`：0 个实例。
- `Win32_PowerManagementEvent`：0 个实例。
- `root\wmi:MSAcpi_ThermalZoneTemperature`：有 1 个 ACPI thermal zone，但不是 CPU core voltage/frequency。
- `Power Meter` 性能计数器存在，但不是 CPU VID/Vcore/package voltage。

判断：

- 标准 WMI probe 类不能作为 FrameScope 电压来源。
- ACPI thermal zone 也不能解决每核心频率或 CPU 电压。

### 4. LibreHardwareMonitorLib

结论：PARTIAL。可作为可选硬件传感器路线，但不是所有机器都能稳定拿到电压。

外部资料验证：

- LibreHardwareMonitor 项目说明它可以监控温度、风扇、电压、负载、时钟。
- 本次查询 NuGet 元数据时，`LibreHardwareMonitorLib 0.9.7-pre699` 发布于 2026-05-24，包含 `.NETFramework4.7.2`、`.NETStandard2.0`、`net8.0`、`net9.0`、`net10.0` target group。
- 依赖包含 `HidSharp`、`RAMSPDToolkit-NDD`、`System.Management`、`System.Memory` 等，视 target 而定。
- License：MPL-2.0。

本机验证结果：

- `root\LibreHardwareMonitor` WMI namespace 不存在。
- 没有找到已安装或正在运行的 LibreHardwareMonitor 应用。
- 没有找到 LibreHardwareMonitor 命名的系统驱动。

判断：

- 直接接入类库技术上可行，但对当前仓库是中等成本，因为 `build.ps1` 现在用 .NET Framework `csc.exe` 直接编译 exe，不是 SDK-style restore/build。
- 真接入要处理 NuGet 获取、引用、传递依赖 DLL、打包、安装器 payload、许可说明。
- 读取 AMD CPU 电压/时钟可能需要底层驱动或特权传感器路径。游戏场景还要考虑反作弊和安全软件对内核硬件监控驱动的态度。
- 即便 LHM 可以运行，传感器名称和可见项也会受 CPU、主板 sensor chip、BIOS、驱动权限影响。可能有 clock，没有 Vcore；可能有 VID，没有实测 Vcore；也可能只有部分 package/SoC 相关电压。

建议：

- 先不要把 LHM 作为第一阶段必需依赖。
- 第一阶段先用 Windows 原生 `Actual Frequency` 做频率。
- LHM 放到第二阶段，作为可选 sensor provider。
- 最好独立成可选 helper exe，避免传感器初始化/驱动异常影响核心帧捕获和进程采样。
- 输出时保留 `HardwareName`、`SensorName`、`SensorType`、`Unit`、`Source`，不要硬编码把所有 voltage 都叫 Vcore。

### 5. OpenHardwareMonitor / OpenHardwareMonitorLib

结论：PARTIAL，但优先级低于 LibreHardwareMonitor。

外部资料验证：

- OpenHardwareMonitor 有硬件传感器项目和库。
- 本次查询 NuGet 元数据时，`OpenHardwareMonitorLib 1.0.9513` 有 `.NETFramework4.7.2` 和 `.NETStandard2.0` target group，说明描述也覆盖温度、风扇、电压、负载、时钟。
- License：MPL-2.0。

本机验证结果：

- `root\OpenHardwareMonitor` WMI namespace 不存在。
- 没有找到已安装或正在运行的 OpenHardwareMonitor 应用。
- 没有找到 OpenHardwareMonitor 命名的驱动。

判断：

- OHM 路线可以做传感器枚举原型，但对 Ryzen 7000 / X3D 的支持新鲜度和可维护性应按风险处理。
- 如果要接第三方硬件传感器，优先验证 LibreHardwareMonitor。

### 6. HWiNFO / 本机已有低层驱动迹象

结论：本机有低层硬件监控驱动迹象，但不建议作为第一实现路径。

本机验证结果：

- 没有发现 HWiNFO 应用正在运行或被安装列表识别。
- 发现运行中的驱动 `HWiNFO_214`，路径位于用户 temp。
- 发现运行中的 `JYWinRing0_130` 和 `inpoutx64` 驱动。

判断：

- 这说明本机存在第三方硬件监控/底层 IO 驱动痕迹，传感器路线在本机大概率不是完全不可行。
- 但这不等于 FrameScope 有稳定、可再分发、可默认启用的 API。
- HWiNFO shared memory 还涉及授权、配置和部署边界，不是最小实现路径。

## AMD Ryzen 7 7800X3D 支持情况

本机硬件：

- CPU：`AMD Ryzen 7 7800X3D 8-Core Processor`
- 8 核，16 逻辑处理器。
- WMI `MaxClockSpeed`：`4200 MHz`。
- Windows `Actual Frequency`：本次短验证中约 `4142-4772 MHz`。

AMD 官方产品页：

- 该产品为 `AMD Ryzen 7 7800X3D Desktop Processor`。
- 页面包含 `# of CPU Cores`、`# of Threads`、`Max. Boost Clock`、`Base Clock` 字段。
- AMD 对 max boost 的说明是：单核心 burst workload 下可达到的最大频率，受散热、主板/BIOS、芯片组驱动、OS 更新等因素影响。

对 FrameScope 的含义：

- 本机 7800X3D 的每逻辑处理器频率可以通过 Windows 计数器采。
- Windows 原生来源拿不到实时 VID/Vcore/package voltage。
- 第三方库可能拿到 AMD 相关时钟/电压，但必须 runtime 探测，不能写死承诺。

## 必须区分的数据语义

### 每核心频率

第一版应称为“每逻辑处理器实际频率”，不要直接称为“每物理核心频率”。

原因：

- Windows 暴露的是 `0,0` 到 `0,15` 这类 logical processor instance。
- 7800X3D 是 8C/16T，一个物理核心通常对应两个逻辑处理器。
- 逻辑处理器到物理核心的映射需要额外拓扑验证，不能靠序号猜。

建议字段：

- `LogicalProcessor`
- `ActualFrequencyMHz`
- `PhysicalCoreId` 先留空，等拓扑映射实现后再填。

### package/core effective clock

不要把这些混为一谈：

- Windows `Actual Frequency`
- 当前 FrameScope 的 `CpuFrequencyMHz * CpuPerformancePct / 100`
- HWiNFO / Ryzen 工具里的 `Core Effective Clock`

有效频率通常包含 residency / sleep 状态的时间加权。核心 active clock 很高时，如果它多数时间在睡眠，effective clock 仍然可能很低。FrameScope 未来展示时必须写清数据来源。

### VID / Vcore / package voltage

这些也不能混为一谈：

- `VID`：CPU 请求电压，不一定是实测电压。
- `Vcore`：主板/VRM sensor 读数，强依赖主板和 sensor chip。
- `Package voltage`：CPU package 或 SoC 相关电压，命名随平台和库变化。
- `Win32_Processor.CurrentVoltage`：SMBIOS 元数据，不是实时电压。

FrameScope 不应承诺所有 CPU 都能拿到电压，也不应把未知电压源强行命名为 Vcore。

## 权限、驱动和管理员评估

Windows PerformanceCounter：

- 本机读取不需要管理员。
- 不需要新驱动。
- 与当前 `FrameScopeSystemSampler` 技术路线一致。
- 失败模式主要是计数器不存在、计数器损坏、权限/系统策略限制。

WMI `Win32_Processor`：

- 不需要管理员。
- 只适合硬件 metadata。
- 不适合实时电压。

LibreHardwareMonitor / OpenHardwareMonitor：

- 可能需要管理员或驱动访问才能读取 MSR / SMBus / sensor chip。
- 可能加载或依赖低层驱动。
- 游戏/反作弊环境下需要谨慎，不能默认强开。
- 传感器枚举可能失败、卡顿、返回部分数据或返回空。
- 应设计为 optional provider，不应影响 PresentMon 和进程采样主链路。

## 性能开销评估

Windows 计数器路线：

- 推荐采样频率：1000 ms，跟现有 `SlowSampleIntervalMs` 对齐。
- 最低可考虑：500 ms，跟当前系统采样器下限一致。
- 不建议放进 100 ms 的进程采样器。
- 16 个逻辑处理器按 1 Hz 读取，预期开销低。

第三方传感器路线：

- 推荐采样频率：1000-2000 ms。
- 不要每次采样都重新枚举硬件；应初始化一次、缓存 sensor handle、循环更新。
- 传感器读取应留在慢采样路径或独立 helper。
- 如果 provider 初始化失败，应记录失败原因并继续核心捕获。

CSV / 报告开销：

- `system-samples.csv` 当前是每个慢采样点一行。
- 每逻辑处理器数据会让本机每秒增加 16 行左右，如果独立 CSV 存储，体量仍然可控。
- 报告图表暂时不应直接渲染所有高维传感器数据，后续需要单独降采样和 UI 方案。

## 最小实现建议

建议：新增 `cpu-core-samples.csv`，不要写进 `process-samples.csv`，也不要第一版把 `system-samples.csv` 扩成可变列宽的 per-core 宽表。

原因：

- `process-samples.csv` 是进程聚合采样，语义不匹配。
- `system-samples.csv` 是固定一行的系统摘要，加入 16/32/更多 CPU 逻辑处理器列会导致 schema 随机器变化。
- 独立 CSV 更适合后续扩展 VID/Vcore/package voltage 和 source/capability 标注。

### 第一阶段：Windows 原生频率

新增：

```text
cpu-core-samples.csv
```

建议字段：

```text
Time,SampleIndex,Source,ProcessorGroup,LogicalProcessor,PhysicalCoreId,ThreadIndex,ActualFrequencyMHz,ProcessorFrequencyMHz,ProcessorPerformancePct,PercentOfMaximumFrequency,ProcessorUtilityPct,PerformanceLimitFlags
```

第一版规则：

- `Source = windows-perfcounter`
- `PhysicalCoreId` 先留空。
- 采样周期使用 `SlowSampleIntervalMs`。
- `Actual Frequency` 不可用时留空，并在 `status.json` / `summary.json` 写清 unavailable reason。
- 可以继续保留现有 `system-samples.csv` 的 `_Total` 字段，避免破坏现有报告。

### 第二阶段：可选硬件传感器

如后续验证 LHM/OHM 可用，再新增：

```text
cpu-sensor-samples.csv
```

建议字段：

```text
Time,SampleIndex,Source,HardwareName,SensorType,SensorName,Unit,Value,RawIdentifier
```

规则：

- 需要显式配置开启，例如 `EnableHardwareSensorTelemetry`。
- 保留原始 sensor 名称。
- 记录 provider 状态：`available`、`unavailable`、`permission-denied`、`driver-unavailable`、`provider-error`。
- 电压缺失不能导致捕获失败。
- 除非 sensor/source 明确表示是 Vcore，否则不要展示成 Vcore。

### 第三阶段：报告图表

本次诊断不建议直接加图表。

后续加图时：

- 频率和电压分开展示。
- 每条曲线标注 source。
- 电压缺失不应显示为捕获失败。
- 报告里明确说明电压可用性依赖硬件、主板、驱动和 provider。

## 兼容风险

- 部分 Windows 系统可能没有 `Actual Frequency`。
- 性能计数器可能损坏或被系统策略限制。
- 逻辑处理器实例不等于物理核心 ID。
- 异构核心、多 processor group、多 socket、关闭 SMT 的机器需要更谨慎的标签。
- 电压传感器依赖 CPU、主板 sensor chip、BIOS、驱动权限和第三方库支持。
- VID 不是实测 Vcore。
- Vcore / package voltage 可能不存在、命名不同或不准确。
- 内核硬件监控驱动可能引起反作弊或安全软件问题。
- 接入 LHM/OHM 会影响依赖、安装包、许可说明和故障模式。

## 性能风险

- Windows per-logical-processor 计数器 1 Hz 风险低。
- 100 ms 采全部核心频率不建议。
- LHM/OHM 更新硬件传感器可能比 Windows counter 重，尤其是重复枚举硬件时。
- 某些 sensor driver 调用可能慢或阻塞。
- CSV 体积在 1 Hz 下可控，但应独立存放。
- 报告渲染高维 sensor 数据前，需要单独设计降采样和交互策略。

## 后续实现验收标准

后续真正实现时建议按这些标准验收：

1. 关闭 CPU core telemetry 时，现有 `presentmon.csv`、`process-samples.csv`、`system-samples.csv`、`summary.json`、`status.json`、报告生成行为保持不变。
2. 打开 CPU core telemetry 后，本机生成 `cpu-core-samples.csv`。
3. 在本机 7800X3D 上，至少 95% 慢采样点都能为 16 个逻辑处理器写入非空 `ActualFrequencyMHz`。
4. `ActualFrequencyMHz` 会随负载变化，不能只是固定 `4200 MHz`。
5. Windows 原生频率路径不要求管理员。
6. `Actual Frequency` 不存在时，捕获仍成功，并在状态/摘要里给出明确 unavailable reason。
7. 没有真实 provider sensor 数据时，不写、不展示、不承诺电压。
8. 打开硬件传感器 provider 但 LHM/OHM 初始化失败时，捕获仍成功，并记录 `provider-error` 或 `driver-unavailable`。
9. 新采样路径默认使用 `SlowSampleIntervalMs`，默认 1000 ms。
10. 短 synthetic capture 中，新 CPU core 采样路径在本机的平均 CPU 开销目标应低于 1%。
11. 捕获结束后不残留 `FrameScopeProcessSampler.exe`、`FrameScopeSystemSampler.exe`、可选 CPU sensor helper、`FrameScopeReportGenerator.exe`。
12. 在真正做报告图表前，报告生成器应能忽略新增 CPU telemetry CSV，不影响现有报告。

## 本机验证命令

以下命令均为只读诊断：

```powershell
Get-CimInstance -ClassName Win32_Processor
Get-CimClass -ClassName Win32_PerfFormattedData_Counters_ProcessorInformation
Get-CimInstance -ClassName Win32_PerfFormattedData_Counters_ProcessorInformation
Get-Counter -Counter '\Processor Information(*)\Actual Frequency','\Processor Information(*)\Processor Frequency','\Processor Information(*)\% Processor Performance','\Processor Information(*)\% Processor Utility' -SampleInterval 1 -MaxSamples 3
Get-CimInstance -ClassName Win32_VoltageProbe
Get-CimInstance -ClassName Win32_CurrentProbe
Get-CimInstance -ClassName Win32_TemperatureProbe
Get-CimInstance -Namespace root\wmi -ClassName MSAcpi_ThermalZoneTemperature
Get-CimClass -Namespace root\OpenHardwareMonitor
Get-CimClass -Namespace root\LibreHardwareMonitor
Get-CimInstance Win32_SystemDriver | Where-Object { $_.Name -match 'WinRing0|InpOut|LibreHardwareMonitor|OpenHardwareMonitor|HWiNFO' }
```

## 外部资料

- Microsoft Learn `Win32_Processor`：https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-processor
- LibreHardwareMonitor 仓库：https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- LibreHardwareMonitorLib NuGet：https://www.nuget.org/packages/LibreHardwareMonitorLib
- OpenHardwareMonitor 仓库：https://github.com/openhardwaremonitor/openhardwaremonitor
- OpenHardwareMonitorLib NuGet：https://www.nuget.org/packages/OpenHardwareMonitorLib
- AMD Ryzen 7 7800X3D 官方页：https://www.amd.com/en/products/processors/desktops/ryzen/7000-series/amd-ryzen-7-7800x3d.html
