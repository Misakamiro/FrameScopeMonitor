# FrameScope Monitor

FrameScope Monitor 是一个面向 Windows 游戏卡顿排查的本地性能监测工具。它会在你启动游戏后自动记录帧时间、FPS、后台进程占用、系统资源状态和报告生成过程，并在监测结束后生成可交互的 HTML 报告，方便定位掉帧、卡顿、后台进程干扰、采样异常和报告生成失败等问题。

当前版本重点面向 PUBG、CS2、Valorant、Delta Force、Cyberpunk 2077、Battlefield、Hogwarts Legacy、OPUS Prism Peak 等游戏，也支持手动添加任意进程名。

## 现在可以做什么

- 管理监测目标：在图形界面中启用、停用、添加和编辑需要监测的游戏进程。
- 刷新并选择进程：从当前系统进程中选择目标，不再因为读取受保护进程信息导致界面卡死。
- 自动监听游戏：先启动 FrameScope Monitor，再正常打开游戏，软件会按配置识别目标进程并开始一次监测会话。
- 捕获帧时间：通过 PresentMon 写出 `presentmon.csv`，用于分析平均 FPS、1% Low、0.1% Low、最低瞬时 FPS 和帧时间曲线。
- 采样后台进程：通过原生采样器记录全进程 CPU、内存和磁盘 IO，帮助判断是否有启动器、反作弊、浏览器、录屏或系统服务抢占资源。
- 采样系统状态：记录 CPU、GPU、内存、磁盘、网络、显存、GPU 频率和功耗等系统指标。
- 生成交互报告：监测结束后生成 HTML 报告，包含摘要、图表、进程列表、系统信息、诊断信息和原始数据引用。
- 查看报告历史：在报告页查看历史监测记录，打开报告目录、打开 HTML 报告、生成详细诊断报告或重新生成报告。
- 调整软件设置：配置数据目录、日志保留天数、最大日志体积、是否自动打开报告、是否生成诊断报告等选项。
- 运行本地诊断：生成 markdown/json 诊断报告，并对敏感路径和隐私字段做脱敏处理。
- 使用内置模拟器验证：没有真实 PUBG 环境时，可以用项目内的 PUBG simulator 验证监测和报告链路。

## 最新版本改进

当前 `v1.1.1` 的代码和安装包已经完成一轮大范围整理和修复：

- 项目结构拆分为 `src\app`、`src\ui`、`src\core`、`src\monitoring`、`src\diagnostics`、`src\reporting`，后续 UI 设计、UI 交互、后端监测和报告模板可以按边界并行维护。
- UI 重新整理了页面布局、圆角、卡片、按钮、表格、设置页和报告页的视觉一致性。
- 页面切换从明显等待和混绘问题改成缓存命中后的快速切换，复测中未再出现转圈等待、空骨架帧、旧页新页混绘或导航状态不同步。
- 进程选择器改为非阻塞刷新，修复点击输入进程或刷新进程时整窗卡死的问题。
- 报告页按钮全部对齐真实 handler，不再出现“看起来能点但只是摆设”的按钮。
- 报告 manifest JSON 改为工具链兼容格式，PowerShell 默认读取、PowerShell UTF-8 读取和 Node 解析都能通过。
- 新增和补齐 UI state、report progress、manifest、capture planner、config store、diagnostics、PUBG simulator、GameLite separation、chart sampling 等测试。
- FrameScope Monitor 与 GameLite 自动轻量化继续保持分离。FrameScope 的构建、测试、监测和报告链路不依赖 GameLite。

## 下载安装

从 GitHub Releases 下载最新版本：

- `FrameScopeMonitor-Setup.exe`：推荐使用的安装程序。
- `FrameScopeMonitor-Installer.zip`：包含安装包和辅助工具的压缩包。
- `FrameScopeMonitor-LegacyCleanup.exe`：旧版本残留清理工具，仅在需要清理早期版本残留时使用。

默认安装位置：

```text
%LOCALAPPDATA%\FrameScopeMonitor
```

默认数据目录：

```text
%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs
```

安装后会创建桌面快捷方式和开始菜单快捷方式。卸载时可以选择是否删除历史报告和 CSV 数据。

## 基本使用

1. 启动 `FrameScope Monitor`。
2. 打开“监控目标”页面。
3. 勾选已有游戏目标，或点击进程选择器添加新的 `.exe` 进程名。
4. 点击“启动监控”。
5. 正常启动游戏并进入实际场景。
6. 游戏退出或停止监控后，等待报告生成完成。
7. 在“报告”页面打开 HTML 报告、报告目录或详细诊断报告。

如果真实 PUBG 无法立即测试，可以运行项目内的 stable simulator 来验证监测和报告链路。真实 PUBG 仍建议手动确认 PresentMon、反作弊、独占全屏、驱动叠加层和实际游戏生命周期是否正常。

## 报告内容

一次完整监测会在 run 目录中生成这些关键文件：

```text
presentmon.csv
process-samples.csv
system-samples.csv
summary.json
status.json
report-progress.json
charts\framescope-interactive-report.html
charts\framescope-interactive-data.js
charts\framescope-interactive-manifest.json
```

HTML 报告会展示：

- FPS、平均帧时间、1% Low、0.1% Low 和异常帧概览。
- 帧时间曲线、FPS 曲线和可交互图表。
- 后台进程 CPU、内存和 IO 占用。
- CPU、GPU、内存、磁盘、网络、显存和功耗等系统采样。
- 捕获状态、报告状态、诊断信息和原始数据路径。

## 项目结构

```text
src\app          主程序、主窗口、页面组合、watcher、monitor session、报告打开和状态编排
src\ui           UI 主题、按钮、卡片、侧边栏、图表、报告页、UI 状态和动效
src\core         配置读写、捕获规划、报告进度等共享核心逻辑
src\monitoring   进程采样器和系统采样器
src\diagnostics  诊断报告、日志清理、隐私脱敏
src\reporting    HTML 报告生成器、CSV 解析、manifest、summary 和图表模板
tests            回归测试、chart sampling 测试、GameLite 分离测试
tools            PresentMon、PUBG simulator、RenderProbe 等辅助工具
packaging        安装器、卸载器、旧版本清理工具
docs             项目说明、模块边界、交接记录、测试报告和实现报告
```

旧的根目录单文件 C# 源码已经拆入 `src` 目录。`build.ps1` 会按当前拆分后的文件列表构建主程序、采样器、报告生成器、卸载器和安装包。

## GameLite 边界说明

GameLite 自动轻量化已经从 FrameScope Monitor 主项目中分离，核心逻辑位于同级独立项目：

```text
..\gamelite-auto-lightweight
```

FrameScope 根目录保留的 `Enter-GameLite.ps1`、`Exit-GameLite.ps1`、`GameLiteSession.ps1`、`Install-GameLiteAutoTrigger.ps1` 等脚本只是兼容 wrapper，用于旧快捷方式、旧 WMI consumer 和用户手动入口。

重要边界：

- FrameScope C# 主程序、构建、测试、监测和报告生成不依赖 GameLite。
- 不要把 GameLite 核心逻辑重新塞回 FrameScope 主项目。
- 不要在没有明确授权时安装、删除或迁移 WMI trigger。
- SGuard 压制属于 GameLite 侧行为，需要明确默认行为和关闭开关，不能隐藏高风险操作。

## 从源码构建

在项目根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建产物会输出到：

```text
dist\
```

常用产物：

```text
dist\FrameScopeMonitor-Setup.exe
dist\FrameScopeMonitor-Installer.zip
dist\FrameScopeMonitor-payload\
```

## 测试

重建测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
```

常用回归测试：

```powershell
.\tests\FrameScopeConfigStoreTests.exe
.\tests\FrameScopeCapturePlannerTests.exe
.\tests\FrameScopeReportProgressTests.exe
.\tests\FrameScopeReportManifestTests.exe
.\tests\FrameScopeDiagnosticsTests.exe
.\tests\FrameScopePubgSimulatorTests.exe
.\tests\FrameScopeUiStateTests.exe
node .\tests\chart-sampling-tests.js
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\lightweight-separation-tests.ps1
dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo
```

如果系统 `node.exe` 命中 WindowsApps 的 `Access is denied`，请换用可用的 Node 运行 `tests\chart-sampling-tests.js`。

## 真实 PUBG 手动验证建议

1. 启动已安装版 FrameScope Monitor。
2. 确认监控目标中包含 `TslGame.exe` 或 PUBG 预设。
3. 点击“启动监控”。
4. 启动 PUBG 并进入实际渲染场景。
5. 运行 2 到 3 分钟后退出游戏或停止监控。
6. 打开生成的 HTML 报告，检查 FPS、帧时间、1% Low、截图/图表、诊断、进程和系统信息。
7. 检查 run 目录是否包含 `presentmon.csv`、`process-samples.csv`、`system-samples.csv`、`summary.json`、`status.json` 和完整 HTML 报告。
8. 退出 PUBG 和 FrameScope Monitor 后，确认没有 FrameScope、PresentMon、采样器、报告生成器、GameLite 或 `TslGame` 残留进程。

## 仓库说明

以下内容不会提交到 git：

- 本地配置和历史记录。
- 运行日志。
- 监测数据和 HTML 报告输出。
- `dist` 打包产物。
- `artifacts` 测试和截图产物。
- `bin`、`obj` 等构建输出。

安装包通过 GitHub Releases 发布，不直接放入源码仓库。

## 维护入口

- UI 视觉和布局：查看 `docs\modules\software-ui.md`。
- UI 交互、按钮 handler、页面切换和状态反馈：查看 `docs\modules\ui-interactions.md`。
- 后端监测、PresentMon、采样器、报告状态和诊断：查看 `docs\modules\backend-monitoring.md`。
- GameLite 自动轻量化：查看 `docs\modules\lightweight-script.md`。
- 项目总览和边界：查看 `docs\FrameScopeMonitor-Project-Overview.md`。
