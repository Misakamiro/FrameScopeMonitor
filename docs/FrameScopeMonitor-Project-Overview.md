# FrameScope Monitor 项目总览

## 项目是什么

FrameScope Monitor 是面向 Windows 游戏卡顿排查的本地性能监测工具。它不是普通硬件监控面板，而是记录一次游戏会话的完整诊断时间线：帧时间、FPS、后台进程、CPU/GPU/内存/磁盘/网络、日志和报告生成状态。目标是帮助定位掉帧、卡顿、后台进程干扰、启动器占用、反作弊相关干扰和报告生成失败。

当前主实现是原生 C#：

- 主程序和 GUI：`FrameScopeMonitor.exe`
- 帧时间采集：PresentMon
- 后台进程采样：`FrameScopeProcessSampler.exe`
- 系统资源采样：`FrameScopeSystemSampler.exe`
- HTML 报告生成：`FrameScopeReportGenerator.exe`

## 主要功能

- 管理监控目标：启用/停用目标、添加进程、刷新系统进程、编辑游戏名、进程名、采样间隔和自动打开报告配置。
- 自动监听游戏：启动监听后按配置扫描目标进程，发现目标游戏后自动启动一次监测会话。
- 采集帧时间：通过 PresentMon 写出 `presentmon.csv`，用于平均 FPS、1% Low、0.1% Low、最低瞬时 FPS 和帧时间曲线分析。
- 采集后台进程：按目标采样间隔记录全进程 CPU、内存和 IO，用于定位后台干扰。
- 采集系统资源：记录 CPU/GPU/内存/磁盘/网络/GPU 频率/显存/功耗等系统指标。
- 生成报告：游戏退出后生成交互式 HTML 报告，并可按配置自动打开。
- 实时监控：进入实时监控页后刷新最近 FPS、帧时间、CPU/GPU、当前进程和日志；离开页面停止 UI 刷新，不影响后台采集。
- 诊断和日志：支持诊断报告、报告生成进度、日志清理、隐私脱敏和状态汇总。

## 支持的游戏监测能力

默认配置覆盖常见游戏或软件目标，例如 CS2、Delta Force、Valorant、Cyberpunk 2077、Battlefield、Hogwarts Legacy、OPUS Prism Peak、PUBG 等。用户也可以手动添加其他进程名。

PUBG 相关逻辑在捕获规划模块中保留专门处理：

- `TslGame.exe`
- `TslGame-Win64-Shipping.exe`
- PresentMon 使用进程名别名捕获，避免锁死到短生命周期 pid。

## UI 功能

UI 使用 WinForms 实现，已经整理为独立 UI 模块目录：

- `src\ui\FrameScopeUiComponents.cs`：卡片、按钮、表格、图表容器、侧边栏、主题视觉组件。
- `src\ui\FrameScopeUiState.cs`：实时监控刷新状态和目标编辑规则。
- `src\ui\FrameScopeLiveData.cs`：实时监控页读取最近 run 数据。
- `src\ui\FrameScopeReportPage.cs`：报告页列表和报告操作。
- `src\app\FrameScopeNativeMonitor.cs`：主窗口、页面组合、按钮事件和应用编排。

后续只改视觉主题、卡片、按钮、表格、侧边栏时，优先看 `docs\modules\software-ui.md`。

## 实时监控功能

实时监控页遵循明确生命周期：

1. 软件启动后不启动图表刷新。
2. 用户进入实时监控页后启动 UI 刷新定时器。
3. 目标进程只从已启用配置目标查找。
4. 找到目标时，UI 每 1 秒刷新显示层，采样器仍按原配置采样。
5. 找不到目标或目标退出时清空图表、当前 FPS、当前进程和旧状态。
6. 离开实时监控页后停止 UI 刷新定时器，避免重复定时器和内存泄漏。

维护实时监控交互时，优先看 `docs\modules\ui-interactions.md` 和 `src\ui\FrameScopeUiState.cs`。

## 报告和日志功能

报告生成分成三层：

- 监测会话写出 `presentmon.csv`、`process-samples.csv`、`system-samples.csv`、`summary.json`、`status.json`。
- `FrameScopeReportGenerator.exe` 读取 run 目录并生成 `charts\framescope-interactive-report.html`、`framescope-interactive-data.js`、`framescope-interactive-manifest.json`。
- UI 和 watcher 读取报告状态、报告进度和历史记录。

报告必须保留完整诊断数据。性能优化只能优化渲染、抽样、缓存和 hover 行为，不允许删除原始数据来换速度。

## 模块结构

- `src\app\`：应用入口、主窗口、页面组合、监听器和监测会话编排。
- `src\ui\`：UI 视觉、页面 UI、实时监控 UI 状态和报告页。
- `src\core\`：配置、捕获规划、报告进度等共享核心逻辑。
- `src\monitoring\`：后台进程采样器和系统采样器。
- `src\diagnostics\`：诊断报告、日志、清理、隐私脱敏。
- `src\reporting\`：HTML 报告生成器。
- `..\gamelite-auto-lightweight\`：独立的 GameLite 自动轻量化项目，不参与 FrameScope 主构建或监测链路。
- `packaging\`：安装器、卸载器、旧版清理工具。
- `tools\`：PresentMon、PUBG 模拟器、渲染探针。
- `tests\`：回归测试和测试重编译脚本。

## 后续修改入口

- 修改视觉主题、按钮、卡片、表格、侧边栏：看 `docs\modules\software-ui.md`。
- 修改页面切换、按钮事件、设置保存、表格编辑、实时页进入/离开：看 `docs\modules\ui-interactions.md`。
- 修改进程识别、采样、PresentMon、报告生成、日志诊断：看 `docs\modules\backend-monitoring.md`。
- 修改自动轻量化脚本：看 `docs\modules\lightweight-script.md`。

## 自动轻量化脚本

自动轻量化脚本已从主程序源码中提取到同级独立项目：

```text
..\gamelite-auto-lightweight
```

根目录仍保留同名 `.ps1` wrapper 和 `.cmd` 启动器，用于兼容旧快捷方式、旧 WMI 触发器和用户手动运行习惯。核心逻辑只维护 `..\gamelite-auto-lightweight\*.ps1`。这些 wrapper 是兼容桥，不是 FrameScope C# 主程序、build 或测试依赖。

常用入口：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Check-GameLiteAutoTrigger.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-GameLiteAutoTrigger.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Remove-GameLiteAutoTrigger.ps1
```

安装和移除 WMI 触发器需要管理员权限；检查脚本通常可以非管理员运行，但 WMI 读取权限可能影响输出。新安装会安装 GameLite 游戏启动触发器、游戏退出触发器和 SGuard late-start 触发器。SGuard 默认压制，`-AllowSGuardThrottle` 只作为兼容参数保留；需要关闭时使用 `-DisableSGuardThrottle`。旧 WMI consumer 如果仍存在，应通过 Check 输出识别并在用户明确授权后用 Remove/Install 迁移。

## 构建和验证

主构建：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

测试重编译：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
```

常规测试：

```powershell
.\tests\FrameScopeConfigStoreTests.exe
.\tests\FrameScopeCapturePlannerTests.exe
.\tests\FrameScopeReportProgressTests.exe
.\tests\FrameScopeDiagnosticsTests.exe
.\tests\FrameScopePubgSimulatorTests.exe
.\tests\FrameScopeUiStateTests.exe
node .\tests\chart-sampling-tests.js
```
