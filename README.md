# FrameScope Monitor

FrameScope Monitor 是一个面向 Windows 游戏性能排查的本地监测工具。它用于记录游戏运行期间的帧时间、FPS、后台进程占用、系统资源状态和报告生成过程，帮助定位掉帧、卡顿、后台进程干扰、采样异常和报告生成失败等问题。

当前项目重点服务于 PUBG 等按进程名配置的游戏目标，也支持手动添加任意 `.exe` 进程名作为监测目标。

## 当前能力

- 游戏性能监测：启动监控后，FrameScope 会监听已启用的目标进程，目标出现后开始一次监测会话。
- PUBG / 游戏目标配置：可以在界面中启用、停用、编辑目标进程，也可以按进程名添加新目标。
- PresentMon 帧数据采集：通过内置 PresentMon 写出帧时间数据，用于分析平均 FPS、1% Low、0.1% Low、最低瞬时 FPS 和帧时间曲线。
- 后台进程采样：记录系统进程的 CPU、内存和磁盘 IO，用于判断启动器、浏览器、录屏、系统服务等后台程序是否抢占资源。
- 系统状态采样：记录 CPU、GPU、内存、磁盘、网络、显存、GPU 频率和功耗等指标。
- 报告生成：监测结束后生成报告目录，包含原始 CSV、摘要 JSON、状态文件和图表数据。
- HTML 报告：生成可交互的 HTML 报告，包含 FPS、帧时间、异常帧、后台进程、系统状态、诊断信息和原始数据引用。
- 诊断报告：可以生成 markdown/json 诊断报告，并对隐私字段和敏感路径做脱敏处理。
- WebView2 React 新界面：新增 React / Vite / Framer Motion 前端和 C# WebView2 bridge，可通过旁路参数启动。
- WinForms 默认 fallback：不带参数启动时仍进入原 WinForms 界面；WebView2 还没有默认替换 WinForms。

## 启动方式

默认启动仍然是 WinForms：

```powershell
.\FrameScopeMonitor.exe
```

启动 WebView2 React 新界面：

```powershell
.\FrameScopeMonitor.exe --web-ui
```

说明：

- `--web-ui` 是旁路入口，用于加载打包目录中的 `frontend` 或源码目录中的 `src\frontend\dist`。
- 不带 `--web-ui` 时不会进入 WebView2，新界面没有默认替换 WinForms。
- WebView2 前端请求通过 C# host bridge 执行，文件系统、进程、报告打开和配置写入等权限仍由 C# 侧验证。

## 基本使用

1. 启动 FrameScope Monitor。
2. 打开监控目标页，确认需要监测的游戏进程已启用。
3. 如需新增目标，按真实 `.exe` 进程名添加或编辑目标。
4. 点击启动监控。
5. 正常启动游戏并进入实际场景。
6. 退出游戏或停止监控后，等待报告生成完成。
7. 在报告页打开 HTML 报告、报告目录，或生成诊断报告。

没有真实 PUBG 环境时，可以使用项目内的 simulator 做链路验证；真实游戏结论仍建议用实际游戏会话手动确认。

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

HTML 报告主要包含：

- FPS、平均帧时间、1% Low、0.1% Low 和异常帧概览。
- 帧时间曲线、FPS 曲线和可交互图表。
- 后台进程 CPU、内存和 IO 占用。
- CPU、GPU、内存、磁盘、网络、显存和功耗等系统采样。
- 捕获状态、报告状态、诊断信息和原始数据路径。

## 下载和安装

从 GitHub Releases 下载发布产物：

- `FrameScopeMonitor-Setup.exe`：推荐使用的安装程序。
- `FrameScopeMonitor-Installer.zip`：包含安装程序、旧版本清理工具和发布说明文本。
- `FrameScopeMonitor-LegacyCleanup.exe`：旧版本残留清理工具，只在需要清理早期版本残留时使用。

默认安装位置：

```text
%LOCALAPPDATA%\FrameScopeMonitor
```

默认数据目录：

```text
%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs
```

安装目录中的主要发布项：

```text
FrameScopeMonitor.exe
FrameScopeProcessSampler.exe
FrameScopeSystemSampler.exe
FrameScopeReportGenerator.exe
FrameScopeUninstaller.exe
Microsoft.Web.WebView2.Core.dll
Microsoft.Web.WebView2.WinForms.dll
WebView2Loader.dll
tools\PresentMon-2.4.1-x64.exe
frontend\index.html
frontend\assets\*
README-FrameScopeMonitor.txt
Uninstall-FrameScopeMonitor.cmd
```

构建过程还会生成 `dist\FrameScopeMonitor-payload`。这是打包和验证用的 payload 目录，不是源码仓库的一部分；正式分发仍以 GitHub Release 附件为准。

## 从源码构建

先验证并构建前端：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

再构建主程序、采样器、报告生成器、卸载器、payload、安装器和 release zip：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建产物输出到：

```text
dist\
dist\FrameScopeMonitor-payload\
dist\FrameScopeMonitor-Setup.exe
dist\FrameScopeMonitor-Installer.zip
dist\FrameScopeMonitor-LegacyCleanup.exe
```

构建要求：

- Windows。
- .NET Framework `csc.exe`，通常位于 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`。
- Microsoft WebView2 NuGet 包；缺失时先运行 `dotnet restore .\tools\WebView2Spike\WebView2Spike.csproj`。
- Node.js。`tools\Run-Frontend.ps1` 会优先使用 `FRAMESCOPE_NODE_EXE`、Codex bundled Node 或系统 `node.exe`，并可自动 bootstrap npm。

## 测试和验证

重建 C# 测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
```

常用回归验证：

```powershell
.\tests\FrameScopeUiStateTests.exe
.\tests\FrameScopeReportProgressTests.exe
.\tests\FrameScopeReportManifestTests.exe
.\tests\FrameScopeWebBridgeTests.exe
```

图表采样测试：

```powershell
node .\tests\chart-sampling-tests.js
```

RenderProbe 构建：

```powershell
dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo
```

发布前还应验证：

- `dist\FrameScopeMonitor-payload\frontend` 存在并包含 React dist。
- payload 默认启动仍是 WinForms。
- payload `FrameScopeMonitor.exe --web-ui` 能加载 WebView2 React 前端。
- WebView2 live smoke 覆盖 state、config、processes、reports、targets、monitor、diagnostics。
- reduced motion 下没有页面混绘、空白帧或整页转圈等待。
- Reports 窄布局下 `Size` 不竖向换行。
- payload 与安装目录关键文件 SHA256 一致。
- 结束后没有 FrameScope、PresentMon、采样器、报告生成器、WebView2 测试 user-data、Vite、esbuild 或项目 Node 残留进程。

## WebView2 bridge 范围

当前 WebView2 bridge 已接入：

- `state.snapshot`
- `config.get`
- `config.save`
- `processes.refresh`
- `reports.list`
- `reports.open`
- `reports.openDirectory`
- `reports.regenerate`
- `targets.get`
- `targets.save`
- `monitor.start`
- `monitor.stop`
- `diagnostics.generate`

安全边界：

- 前端请求必须携带 `requestId`。
- C# 响应必须回传匹配的 `requestId`。
- 长任务先返回 accepted 或 in-flight，再通过事件通知完成或失败。
- 前端不能直接指定任意报告路径、配置路径或目录路径。
- 报告打开、目录打开、重新生成和诊断都由 C# host 重新解析和校验。

## 项目结构

```text
src\app          主程序、WinForms 界面入口、WebView2 host、bridge、watcher、monitor session
src\frontend     React / Vite / Framer Motion WebView2 前端
src\ui           WinForms UI 组件、主题、报告页和共享 UI 状态
src\core         配置、捕获规划、报告进度等共享核心逻辑
src\monitoring   进程采样器和系统采样器
src\diagnostics  诊断报告、日志清理和隐私脱敏
src\reporting    HTML 报告生成器、CSV 解析、manifest、summary 和图表模板
tests            C# 回归测试、Web bridge 测试和 chart sampling 测试
tools            PresentMon、WebView2 spike、RenderProbe 和前端运行脚本
packaging        安装器、卸载器、旧版本清理工具和安装说明
docs             模块说明、实现报告、测试报告和交接文档
```

## GameLite 边界

GameLite 自动轻量化已经从 FrameScope Monitor 主项目中分离。FrameScope 根目录保留的 GameLite 脚本是兼容入口，用于旧快捷方式、旧 WMI consumer 或用户手动入口。

维护边界：

- FrameScope C# 主程序、构建、测试、监测和报告生成不依赖 GameLite。
- 不要把 GameLite 核心逻辑重新塞回 FrameScope 主项目。
- 没有明确授权时，不要安装、删除或迁移 WMI trigger。
- SGuard、WMI 和 GameLite 默认策略属于 GameLite 侧维护范围。

## 不提交到仓库的内容

以下内容属于本地运行、构建或验证输出，不应提交：

- 本地配置和历史记录。
- 运行日志。
- 监测数据和 HTML 报告输出。
- `dist` 打包产物。
- `artifacts` 截图和测试证据。
- `src\frontend\node_modules` 和 `src\frontend\dist`。
- `tools\.cache`。
- 根目录构建生成的 `.exe`、WebView2 DLL、`bin`、`obj` 等输出。

## 维护入口

- 项目总览：`docs\FrameScopeMonitor-Project-Overview.md`
- UI 视觉和布局：`docs\modules\software-ui.md`
- UI 交互和按钮 handler：`docs\modules\ui-interactions.md`
- 后端监测、PresentMon、采样器、报告状态和诊断：`docs\modules\backend-monitoring.md`
- GameLite 自动轻量化边界：`docs\modules\lightweight-script.md`
