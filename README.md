# FrameScope Monitor

## 2026-06-13 用户可见更新

- 报告和图表界面完成中文化：报告/图表 tab、标题、tooltip、summary、legend、空状态、参考线和 Top 进程等用户可见文案现在使用中文表达。
- 任务管理器中出现多个 `FrameScopeMonitor.exe` 属于正常架构：其中一部分是 watcher / monitor-session worker，用来监听目标进程、采样和生成报告，不代表重复打开了多个软件主窗口。
- 普通 UI 启动加入单实例保护：软件已经运行时，再次点击桌面快捷方式或开始菜单入口不会打开第二个窗口，而是提示“FrameScope Monitor 已在运行，请勿重复打开。”，随后第二个进程退出。
- worker / diagnostic 启动不会被普通 UI 单实例锁误挡，后台监控流程仍可正常创建需要的工作进程。
- `CPU Voltage / Vcore` 与 `CPU Core VID` 继续分离展示：真实整体 Vcore/CPU Voltage 不会被 VID、SOC、Package、VBAT、VIN 或编号核心电压冒充；CPU Core VID 仍表示 CPU 请求/目标电压。
- 图表会把采集失败产生的无效 `0`、极低电压/频率/温度/功耗样本过滤或断开为 `null` gap，不再画成扎底竖线。
- 真实低功耗状态仍会保留，例如 GPU `225 MHz` 这类有效低 P-state 不会被误删。
- AMD LibreHardwareMonitor 的 `0.4-0.7V` Core VID 已识别为不可信低区间并拒绝显示；约 `1.08V` 属于 `CPU Voltage / Vcore`，不会冒充 `CPU Core VID`。
- 报告数据键保持兼容：`DATA.cpuVoltage`、`DATA.cpuVid` 和 `bucketMs=1000` 均保持不变。
- FPS 图表展示仍使用 `bucketMs=1000` 的 1 秒聚合；平均 FPS、1% Low、0.1% Low 等统计语义继续来自 raw PresentMon 数据，不改写原始统计口径。
- 本轮已完成本地 Full Setup 更新安装验证，结论 PASS，用户数据目录保留。
- 已知边界：本轮没有启动真实游戏，没有进行 BF6 真实游戏测试。

FrameScope Monitor 是一个面向 Windows 游戏玩家的本地游戏性能排查工具。它会在游戏运行时记录帧时间、FPS、后台进程占用和系统资源变化，帮助你判断卡顿、掉帧、加载异常或后台程序抢占资源的原因。

当前发布版本为 v1.2，WebView2 React UI 是默认界面。旧 WinForms 主界面已移除，安装后直接启动软件就会进入新界面。

## 主要功能

- 自动监听游戏进程：当配置好的游戏进程启动后，FrameScope Monitor 会开始记录一次监测会话。
- 帧时间和 FPS 分析：记录平均 FPS、1% Low、0.1% Low、最低瞬时 FPS 和帧时间趋势。
- GamePP 风格报告图表：FPS、CPU、GPU、内存、IO、后台进程、CPU 核心频率和 CPU Core VID 等报告图表已统一为深色 GamePP 风格。
- 后台进程占用记录：查看浏览器、启动器、录屏、聊天软件或系统服务是否在游戏时抢占 CPU、内存或磁盘 IO。
- 系统资源采样：记录 CPU、GPU、内存、磁盘、网络、显存、GPU 频率、功耗、CPU Voltage / Vcore 和 CPU Core VID 等指标。
- 目标管理：在界面里启用、停用、编辑、新增或删除需要监测的游戏进程。删除最后一个 target 后，也可以保存为空列表。
- 报告管理：查看历史报告、打开报告目录、重新生成报告。
- 诊断信息：在排查复杂问题时生成诊断报告，方便整理当前软件状态和关键日志。

## 下载和安装

请到 GitHub Releases 下载最新版本。

- `FrameScopeMonitor-Setup.exe`：推荐普通用户下载。适合大多数已经带有 Microsoft Edge WebView2 Runtime 的系统。
- `FrameScopeMonitor-Full-Setup.exe`：完整安装包，内置 Microsoft Edge WebView2 Runtime 安装器。适合打开失败、提示缺少 WebView2 Runtime、离线环境或精简系统。
- `FrameScopeMonitor-Installer.zip`：压缩包版本，内含安装程序和附带说明，适合想保留完整发布包的用户。
- `FrameScopeMonitor-LegacyCleanup.exe`：旧版本清理工具。只有在早期版本残留影响使用时才需要运行。

默认安装位置：

```text
%LOCALAPPDATA%\FrameScopeMonitor
```

默认数据目录：

```text
%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs
```

## 启动方式

安装后从开始菜单、桌面快捷方式或安装目录运行：

```text
FrameScopeMonitor.exe
```

软件会直接进入 WebView2 React UI，不需要额外启动参数。新界面依赖 Microsoft Edge WebView2 Runtime。
如果系统缺少 Microsoft Edge WebView2 Runtime，软件会显示中文提示，不会白屏或崩溃。此时请安装 `FrameScopeMonitor-Full-Setup.exe`，或前往 Microsoft 官网安装 WebView2 Runtime。

## 基本使用流程

1. 打开 FrameScope Monitor。
2. 进入“监控目标”，确认要监测的游戏已经启用。
3. 如果列表里没有你的游戏，新增目标并填写真实的 `.exe` 进程名。
4. 点击启动监控。
5. 正常启动游戏并进入你想排查的场景。
6. 结束游戏或停止监控后，等待报告生成。
7. 在“报告”页打开 HTML 报告，或打开报告所在目录查看原始数据。

为了让报告更有参考价值，建议在一次监测中只排查一个明确场景，例如“进入大厅后卡顿”“开局前两分钟掉帧”或“切换地图时加载慢”。

## 报告里有什么

FrameScope Monitor 会为每次监测生成一个报告目录。HTML 报告通常包含：

- FPS 概览：平均 FPS、1% Low、0.1% Low、最低瞬时 FPS。
- GamePP 风格 FPS 图表：报告展示使用 1 秒 bucket 聚合，`bucketMs=1000` 保持不变；平均 FPS、1% Low、0.1% Low 等统计继续来自 raw PresentMon 数据，不改写原始统计口径。
- 全报告 GamePP 风格图表：CPU、GPU、系统占用、后台进程、IO/温度、CPU 核心频率、CPU Voltage / Vcore 和 CPU Core VID 等图表使用统一的深色面积折线、图例、参考线和 tooltip。
- 帧时间曲线：查看卡顿发生的大致时间点。
- 异常帧提示：帮助定位突然变慢或波动明显的片段。
- 后台进程占用：显示游戏过程中 CPU、内存和磁盘 IO 占用较高的进程。
- 系统资源变化：查看 CPU、GPU、内存、磁盘、网络、显存、功耗、电压和频率等趋势。
- 原始数据引用：需要深入排查时，可以继续查看 CSV、JSON 和图表数据文件。

报告重点不是给出一个简单结论，而是把游戏过程中的帧表现和系统状态放在同一条时间线上，方便你判断卡顿更像是游戏本身、后台进程、系统资源还是采集环境造成的。

## CPU Voltage / Vcore 与 CPU Core VID

FrameScope Monitor 现在把 CPU Voltage / Vcore 和 CPU Core VID 分开记录、分开展示：

- `CPU Voltage / Vcore`：整体真实电压口径，只接受明确表示 CPU Voltage / Vcore 的整体电压传感器。
- `CPU Core VID`：CPU 核心请求/目标电压，来自 VID 数据，不代表真实 per-core Vcore。
- FrameScope Monitor 不会把 VID 冒充 Vcore。如果设备没有可用的真实 CPU Voltage / Vcore，报告会显示不可用或保留诊断状态，而不是用 VID 填充。
- CPU Core VID 已新增/修正记录、CSV、manifest、`DATA.cpuVid` 和独立图表，VID-only 数据不会进入 `DATA.cpuVoltage`。
- AMD LibreHardwareMonitor 在部分 AMD 平台上可能给出 `0.4-0.7V` 的 Core VID；这类值会被判定为不可信低区间并拒绝，不会作为准确 CPU Core VID 展示。
- 如果你看到约 `1.08V` 的读数，它通常属于 SuperIO Vcore / CPU Voltage 口径；FrameScope 会保留在 `DATA.cpuVoltage`，不会填进 `DATA.cpuVid`。
- 报告仍保留 `DATA.cpuVoltage` 与 `DATA.cpuVid` 两个独立数据区，FPS 聚合窗口仍为 `bucketMs=1000`。

## 性能优化

本轮发布包含一组面向大报告和长期监测的性能优化：

- 大型报告生成更快，峰值内存更低。
- 后端监测占用降低，轮询、子进程和采样策略更克制。
- 大报告 process 图交互优化，搜索、hover 和切换更轻。
- 前端大列表使用 windowing，250 条以上列表不会一次性渲染全部 DOM。
- UI 动画/过渡优化，移除高成本动效并保留 reduced-motion 支持。
- 日志限频和 diagnostics tail trim，避免重复日志和过大日志拖慢诊断。
- data root 扫描保护，限制深层目录、损坏 JSON、reparse/junction 和大目录噪声造成的扫描风险。

## 本机安装验证

本轮在本机完成过安装和 smoke 验证，结论为 PASS：

- `FrameScopeMonitor-Full-Setup.exe /quiet` 静默安装成功。
- payload hash parity 通过，mismatch count 为 `0`。
- WebView2 live smoke PASS。
- WebView2 reduced-motion smoke PASS。
- target add/edit/delete PASS，删除最后一个目标后最终 target count 为 `0`。
- Settings persistence PASS。
- report resource smoke PASS，报告资源包含 `bucketMs=1000`、`DATA.cpuVoltage` 和 `DATA.cpuVid`。

本轮没有做真实游戏验收，也没有做 BF6 真实游戏验收。

## 常见问题

### 启动后没有自动记录怎么办？

先确认“监控目标”里对应游戏已经启用，并且进程名填写的是实际运行的 `.exe` 名称。可以打开任务管理器查看真实进程名。

### 为什么报告里没有帧数据？

常见原因是游戏进程没有被正确识别、监测时间太短、游戏没有进入实际渲染场景，或者当前游戏/反作弊环境限制了帧数据采集。建议确认目标进程名后，重新进行一次完整游戏会话。

### 为什么后台进程占用看起来很高？

短时间峰值不一定就是卡顿原因。建议结合帧时间曲线看同一时间点是否也出现明显波动，再判断该进程是否可能影响游戏。

### 报告打不开怎么办？

可以在“报告”页打开报告目录，手动打开 `charts\framescope-interactive-report.html`。如果浏览器拦截本地文件，请换一个浏览器或把报告目录复制到普通用户目录后再打开。

### 需要管理员权限吗？

通常不需要。少数游戏或安全软件可能限制采集行为，如果数据一直为空，可以尝试用管理员权限启动 FrameScope Monitor。

### 数据会上传吗？

FrameScope Monitor 是本地工具。监测数据默认保存在你的本机数据目录中，软件不会把报告上传到云端。

## 从源码构建

源码构建适合开发者或想自行打包的用户。普通用户建议直接下载 Release 安装包。

先准备并构建前端资源：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

再构建主程序和安装包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建完成后，安装包会输出到 `dist` 目录。

基本要求：

- Windows。
- .NET Framework 编译器。
- Node.js。
- Microsoft WebView2 运行环境。
