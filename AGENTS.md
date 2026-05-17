# FrameScope Monitor 项目协作说明

## 项目定位

FrameScope Monitor 是一个面向 Windows 游戏卡顿排查的本地性能监测工具。它的目标不是做普通硬件监控面板，而是记录一次游戏会话中“帧时间 + 后台进程 + 系统资源”的完整时间线，方便定位卡顿、掉帧、后台进程干扰、反作弊/启动器占用等问题。

当前核心实现是原生 C# 模式：

- 游戏运行期间不应常驻旧版 PowerShell/Python 监测壳。
- 帧时间由 PresentMon 采集。
- 后台进程和系统资源由 C# 采样器采集。
- HTML 报告由 C# 原生生成器生成。

## 重要原则

- 修改前必须先看真实代码、真实配置和真实记录数据，不要凭印象改。
- 修改源码后，如果问题发生在用户本机安装版，必须同步更新 `%LOCALAPPDATA%\FrameScopeMonitor` 或重新安装新包；只改源码目录不算完成。
- 每次修改后必须做自测：语法/编译、启动、监听、采样、报告生成、报告自动打开、退出无残留。
- 不要为了降低占用牺牲软件核心功能：必须保留完整数据、图表切换、鼠标悬停查看时间点数据、后台进程曲线、性能图表。
- 不要把缺帧数据的空报告当作成功。必须检查 `presentmon.csv` 和 manifest/status 中的帧数据状态。
- 大数据报告优化必须保留完整原始数据，只能优化渲染和抽样策略，不能丢弃用户需要排查的数据。
- 遇到用户上传的 run 目录，优先分析该目录中的 CSV/JSON，不要用当前正在运行的进程反推过去的游戏过程。

## 目录和关键文件

项目源码目录通常是：

```text
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d
```

安装目录通常是：

```text
%LOCALAPPDATA%\FrameScopeMonitor
```

数据目录通常是：

```text
%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs
```

核心文件：

- `src\app\FrameScopeNativeMonitor.cs`：主程序入口、WinForms 主窗口、页面切换、UI 事件、自动监听器、单次原生监测会话编排。
- `src\ui\FrameScopeUiComponents.cs`：卡片、按钮、表格、图表容器、侧边栏、主题视觉组件。
- `src\ui\FrameScopeUiState.cs`：实时监控刷新状态和目标编辑规则等可测试 UI 状态逻辑。
- `src\ui\FrameScopeLiveData.cs`：实时监控页读取最近 run 数据、FPS/帧时间/日志数据。
- `src\ui\FrameScopeReportPage.cs`：报告页列表、详情和报告相关操作。
- `src\core\FrameScopeConfigStore.cs`：配置读取、保存、默认值、目标配置规范化。
- `src\core\FrameScopeCapturePlanner.cs`：进程别名、PUBG/TslGame 捕获规划、PresentMon 参数规划。
- `src\core\FrameScopeReportProgress.cs`：报告生成进度 JSON 读写。
- `src\monitoring\FrameScopeProcessSampler.cs`：后台进程采样器，默认 100ms 记录所有进程 CPU、内存、IO。
- `src\monitoring\FrameScopeSystemSampler.cs`：系统采样器，记录 CPU/GPU/内存/磁盘/网络/GPU 频率/显存/功耗。
- `src\diagnostics\FrameScopeDiagnostics.cs`：诊断报告、日志清理、隐私脱敏、状态汇总。
- `src\reporting\FrameScopeReportGenerator.cs`：原生 HTML 报告生成器。
- `build.ps1`：编译所有 exe 并生成安装包。
- `framescope-config.json`：本机运行配置。
- `framescope-config.example.json`：默认配置模板。
- `packaging\FrameScopeSetupNative.cs`：安装器源码。
- `packaging\FrameScopeUninstaller.cs`：卸载器源码。
- `packaging\FrameScopeLegacyCleanup.cs`：旧版完全清理工具源码。
- `tools\PresentMon-2.4.1-x64.exe`：帧时间采集工具。

模块说明文档：

- `docs\FrameScopeMonitor-Project-Overview.md`：项目总览、目录结构、后续修改入口。
- `docs\modules\software-ui.md`：软件 UI、主题、视觉组件、页面组成。
- `docs\modules\ui-interactions.md`：页面切换、按钮事件、设置保存、表格编辑、实时监控进入/离开。
- `docs\modules\backend-monitoring.md`：进程识别、采样、PresentMon、报告生成、日志诊断。
- `docs\modules\lightweight-script.md`：自动轻量化脚本位置、运行方式、验证方式。

后续 Codex 修改本项目时，先读本文件，再读 `docs\FrameScopeMonitor-Project-Overview.md`，然后按任务类型读取对应 `docs\modules\*.md`。

## 运行流程

1. 用户打开 `FrameScopeMonitor.exe`。
2. GUI 读取 `framescope-config.json`。
3. 用户点击启动监听。
4. Watcher 按 `PollIntervalMs` 扫描配置中的游戏进程。
5. 发现目标游戏后，启动 `FrameScopeMonitor.exe --monitor-session`。
6. 监测会话启动 PresentMon、`FrameScopeProcessSampler.exe` 和 `FrameScopeSystemSampler.exe`。
7. 游戏退出后，监测会话停止采样，写入 `summary.json` 和 `status.json`。
8. Watcher 调用 `FrameScopeReportGenerator.exe` 生成 HTML 报告。
9. 报告生成成功后，写入历史记录，并按配置自动打开浏览器。

## 一次记录目录的文件含义

一次 run 通常位于：

```text
%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs\<GameName>\<GameName>-yyyyMMdd-HHmmss
```

关键文件：

- `presentmon.csv`：帧时间原始数据。
- `process-samples.csv`：后台进程聚合采样数据。
- `topcpu-samples.csv`：每个采样点 CPU 最高进程。
- `topio-samples.csv`：每个采样点 IO 最高进程。
- `sample-alerts.csv`：高 CPU、高 IO 等提示。
- `system-samples.csv`：系统、CPU、GPU、磁盘、网络等数据。
- `summary.json`：监测摘要。
- `status.json`：状态和报告生成结果。
- `report-generation.log`：报告生成日志。
- `report-opened.flag`：报告已自动打开标记。
- `charts\framescope-interactive-report.html`：最终 HTML 报告。
- `charts\framescope-interactive-data.js`：完整图表数据。
- `charts\framescope-interactive-manifest.json`：报告生成摘要。

## 配置说明

`framescope-config.json` 的关键字段：

```json
{
  "PollIntervalMs": 1000,
  "DataRoot": "C:\\Users\\misakamiro\\AppData\\Local\\FrameScopeMonitorData\\framescope-runs",
  "OpenReportOnComplete": true,
  "MonitorScript": "native-csharp",
  "Targets": [
    {
      "Enabled": true,
      "Name": "Valorant",
      "ProcessName": "VALORANT-Win64-Shipping.exe",
      "SampleIntervalMs": 100,
      "ProcessSampleIntervalMs": 100,
      "SlowSampleIntervalMs": 1000,
      "OpenReportOnComplete": true
    }
  ]
}
```

注意：

- `MonitorScript` 应保持为 `native-csharp`。
- `ProcessSampleIntervalMs` 最低按 100ms 处理。
- `SlowSampleIntervalMs` 用于系统/GPU 等慢速采样，通常 1000ms。
- `OpenReportOnComplete` 需要同时看全局配置和目标配置。

## 报告功能要求

HTML 报告必须保留这些能力：

- 帧率页：平均 FPS、1% Low、0.1% Low、最低瞬时 FPS。
- 性能图表：CPU 频率、GPU 频率、显存频率。
- 系统占用：CPU、GPU、内存、显存占用。
- IO/温度：磁盘、网络、磁盘延迟、GPU 功耗、GPU 温度。
- 后台进程：全部进程 CPU/内存时间线。
- 搜索进程。
- 鼠标悬停查看某个时间点的占用。
- 图表宽度可调。
- 读取模式：保留尖峰、趋势易读、原始密集。

大数据报告卡顿时，优先优化：

- Canvas 绘制抽样。
- hover overlay，避免 hover 重绘全图。
- 二分查找时间点。
- 按画布宽度和视图模式做 min/max 或趋势抽样。

不要通过删除数据或只选峰值进程来“优化”。

## 构建方式

在项目根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

`build.ps1` 使用 Windows 自带的：

```text
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

构建产物包括：

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `FrameScopeUninstaller.exe`
- `FrameScopeLegacyCleanup.exe`
- `dist\FrameScopeMonitor-Setup.exe`
- `dist\FrameScopeMonitor-LegacyCleanup.exe`
- `dist\FrameScopeMonitor-Installer.zip`

## 安装和发布注意事项

- 安装器把程序安装到 `%LOCALAPPDATA%\FrameScopeMonitor`。
- 数据和报告目录应与程序目录分离，默认 `%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs`。
- 安装器需要写入卸载注册表项，使 GeekUninstaller 等卸载工具能识别。
- 卸载器需要询问是否删除数据和报告。
- 旧版用户需要单独的 `FrameScopeMonitor-LegacyCleanup.exe` 清理旧 PowerShell/Python/PresentMon 残留。
- 打包后必须确认 payload 里包含的是最新 exe，不要只看 build 成功。

## 必做测试清单

每次修改后至少做以下检查：

1. C# 编译通过。
2. GUI 可以启动。
3. `framescope-config.json` 可以正常解析。
4. 启动监听后能识别目标进程。
5. 游戏或测试进程退出后能停止监测。
6. `presentmon.csv`、`process-samples.csv`、`system-samples.csv` 正常生成。
7. 报告 HTML 正常生成。
8. `charts\framescope-interactive-manifest.json` 中 `hasFrameData` 与实际数据一致。
9. 报告能自动打开；不能只生成不弹出。
10. 报告里 FPS、1% Low、0.1% Low 不应异常相同，除非数据本身确实如此。
11. 图表曲线不要过粗，Y 轴应根据当前数据合理缩放。
12. 大数据报告打开和切换视图不能明显卡死。
13. 退出后不能残留 `FrameScopeProcessSampler.exe`、`FrameScopeSystemSampler.exe`、`FrameScopeReportGenerator.exe`、FrameScope 专用 PresentMon 会话。
14. 游戏运行期间不应出现 FrameScope 自己导致的高 CPU PowerShell 常驻。
15. 如果改了安装器，必须测试安装、覆盖安装、卸载、保留数据、删除数据。

## 诊断用户上传数据时的顺序

用户上传 run 目录后，先看：

1. `summary.json`
2. `status.json`
3. `charts\framescope-interactive-manifest.json`
4. `report-generation.log`
5. `presentmon.csv` 前几行和大小
6. `process-samples.csv` 中异常进程的时间段
7. `system-samples.csv` 中 CPU/GPU/磁盘/GPU 频率
8. `sample-alerts.csv` 中高 CPU、高 IO 片段

分析进程占用时要区分：

- 平均占用。
- P95/P99。
- 单次峰值。
- 峰值是否与帧时间异常重叠。
- 是否只是启动、切屏、加载、退出阶段。

不要只看图表峰值就判断某进程是主因。

## 自动轻量化脚本边界

GameLite 自动轻量化已经从本项目源码树中提取为独立项目，它和 FrameScope 监测软件是两个独立功能，不要混淆。

相关文件：

- 核心脚本已移动到同级独立项目 `..\gamelite-auto-lightweight\`。
- 本项目根目录保留同名 `.ps1` 薄 wrapper，兼容旧快捷方式、旧 WMI 触发器和用户手动运行习惯；wrapper 只是兼容桥，不是 FrameScope 主程序依赖。
- 根目录 `.cmd` 启动器必须继续通过 `%*` 转发参数。
- `Install-GameLiteAutoTrigger.ps1` -> `..\gamelite-auto-lightweight\Install-GameLiteAutoTrigger.ps1`
- `Check-GameLiteAutoTrigger.ps1` -> `..\gamelite-auto-lightweight\Check-GameLiteAutoTrigger.ps1`
- `Remove-GameLiteAutoTrigger.ps1` -> `..\gamelite-auto-lightweight\Remove-GameLiteAutoTrigger.ps1`
- `GameLiteSession.ps1` -> `..\gamelite-auto-lightweight\GameLiteSession.ps1`
- `Enter-GameLite.ps1` -> `..\gamelite-auto-lightweight\Enter-GameLite.ps1`
- `Exit-GameLite.ps1` -> `..\gamelite-auto-lightweight\Exit-GameLite.ps1`
- `Invoke-GameLiteSGuardThrottle.ps1` -> `..\gamelite-auto-lightweight\Invoke-GameLiteSGuardThrottle.ps1`

原则：

- 轻量化脚本只在配置游戏启动时自动进入，游戏退出时恢复。
- FrameScope C# 主程序、采样器、报告生成器、`build.ps1` 和 `tests\Build-FrameScopeTests.ps1` 不得依赖轻量化脚本或 WMI 触发器。
- 轻量化脚本不得依赖 `FrameScopeMonitor.exe`、PresentMon、FrameScope run 目录、报告生成器或监测链路。
- 轻量化自己的日志、状态和备份文件保存在 `..\gamelite-auto-lightweight\`，不要写入 FrameScope 数据目录。
- 不要常驻高占用监测器。
- 日志只记录错误或异常，避免持续写日志。
- 不要杀进程，不要禁用正常功能链路。
- Steam Overlay、Steam 网络、OPPO Connect、Everything、QQEX、WeChatAppEx 等交互链路要谨慎保护。
- `Weixin.exe` 当前只做保守游戏时降级：`BelowNormal + IO Low`。
- `SGuard64`、`SGuardUpdate64`、`SGuardSvc64` 默认压制；`-AllowSGuardThrottle` 只作为兼容参数保留，显式关闭必须使用 `-DisableSGuardThrottle`。默认策略为 Idle priority、IO priority 0、page priority 1、affinity 最后两个逻辑核心；`-StrictSGuard` 改为最后一个逻辑核心。

如果继续压制 SGuard：

- 需要明确默认策略和严格策略的差异。
- 不要默认启用线程挂起、Job Object CPU 配额、服务级限制等更激进手段。
- 必须测试是否引发游戏黑屏、鼠标焦点异常、反作弊异常、切屏后恢复等问题。

WMI 注意事项：

- 旧机器上可能残留 `GameLiteSGuardTriggerFilter` / `GameLiteSGuardTriggerConsumer`。不要未经用户明确授权安装或移除 WMI 触发器。
- `Check-GameLiteAutoTrigger.ps1` 会报告新游戏启动触发器、新游戏退出触发器、SGuard late-start 触发器、旧 SGuard consumer 是否存在、SGuard 默认策略、状态文件和运行中的 GameLite PowerShell。
- 旧 game start consumer 如果仍指向 `GameLiteSession.ps1`，当前 `GameLiteSession.ps1` 必须先检查新的 stop 触发器是否存在；没有 stop 触发器时 no-op，避免只进入轻量化但无法自动恢复。
- `Exit-GameLite.ps1` 只能按本次保存的 snapshot 恢复，不要加入宽泛 fallback 恢复列表，避免误改未被本次 Enter 修改过的进程。

## 常见坑

- 当前活体进程不能代表用户游戏时状态，必须以 run 目录数据为准。
- 旧报告可能没有新字段，要兼容缺列。
- PresentMon 可能记录启动/切屏/退出阶段的大帧时间，需要识别伪峰值。
- PUBG/Valorant/CS2 可能存在不同 PresentMon swapchain，需要选择主渲染轨道。
- CPU 频率不能只用 MaxClockSpeed，要优先用系统采样里的有效频率。
- 如果 HTML 显示 N/A，先检查数据生成、manifest、字段名和 JS 数据路径。
- 如果用户说“本机已经更新了吗”，要查安装目录文件时间和版本，不要只看源码。
- 如果用户说“打包了吗”，要明确是源码热修、安装目录热修，还是重新生成安装包。

## 交付口径

交付时必须说明：

- 改了哪些文件。
- 改了哪些功能或 bug。
- 是否更新了本机安装目录。
- 是否重新打包。
- 做了哪些测试。
- 哪些测试无法真实执行。
- 是否有备份或回退方式。
