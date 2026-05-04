# FrameScope Monitor

FrameScope Monitor 是一个面向 Windows 游戏卡顿排查的本地性能监测工具。它会监听你配置的游戏进程：游戏启动时自动开始记录，游戏退出后自动停止记录，并生成可交互的 HTML 性能报告。

## 主要功能

- 原生 WinForms 图形界面，用于选择、添加和管理需要监测的游戏进程。
- 自动监听模式：先启动 FrameScope Monitor，再正常打开游戏即可自动记录。
- 使用 PresentMon 捕获帧时间数据，用于分析平均 FPS、1% Low、0.1% Low 和异常帧时间。
- 使用轻量原生采样器记录后台进程 CPU、内存、磁盘 IO，降低监控工具本身对游戏的干扰。
- 监测会话由 `FrameScopeMonitor.exe` 的原生 C# 模式编排，游戏运行期间不再常驻 PowerShell 监测壳。
- 系统性能采样也由原生采样器负责，减少监控工具本身的 CPU 占用。
- 记录系统指标，包括 CPU 频率、CPU 占用、内存、磁盘、网络、GPU 利用率、GPU 频率、显存和功耗。
- 游戏退出后自动生成 HTML 报告，并在历史记录中保存报告与原始 CSV 数据路径。
- 安装包内置所需运行环境，用户不需要额外安装 Python 等依赖。

## 默认预设

默认包含以下进程预设：

- Counter-Strike 2
- Delta Force
- Neverness To Everness
- Valorant
- Cyberpunk 2077
- Battlefield 6
- Hogwarts Legacy
- OPUS Prism Peak

也可以在软件界面中手动添加更多进程。

## 安装

从 GitHub Releases 下载 `FrameScopeMonitor-Setup.exe`，双击运行即可安装。

默认安装位置：

```text
%LOCALAPPDATA%\FrameScopeMonitor
```

安装后会创建桌面快捷方式和开始菜单快捷方式。

## 使用方式

1. 启动 `FrameScope Monitor`。
2. 在列表中勾选需要监测的游戏进程。
3. 点击 `Start` 启动自动监听。
4. 正常打开游戏。
5. 游戏退出后，软件会停止记录并自动打开对应 HTML 报告。

生成的数据默认保存在：

```text
%LOCALAPPDATA%\FrameScopeMonitor\framescope-runs
```

## 性能策略

帧时间由 PresentMon 按实际帧记录，不依赖低频轮询。监测会话由 `FrameScopeMonitor.exe --monitor-session` 原生 C# 模式负责，后台进程采样由 `FrameScopeProcessSampler.exe` 执行，默认每 100ms 记录一次全进程 CPU、内存和 IO 数据；系统采样由 `FrameScopeSystemSampler.exe` 执行。采样器会跟随监控父进程退出，避免旧版 PowerShell 监测壳导致监控器自身占用偏高或退出残留。

## 从源码构建

运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建脚本会调用 Windows 自带的 .NET Framework 编译器，并打包便携 Python 运行时。

## 核心文件

- `FrameScopeMonitor.exe`：主界面、自动监听器和单次原生监测会话。
- `FrameScopeProcessSampler.exe`：低占用后台进程采样器。
- `FrameScopeSystemSampler.exe`：系统性能采样器。
- `Generate-CS2-FrameScope-Interactive-Report.py`：HTML 报告生成器。
- `tools\PresentMon-2.4.1-x64.exe`：帧时间采集工具。

## 仓库说明

以下内容不会提交到 git：

- 本地监测数据
- HTML 报告输出
- 运行日志
- 本地配置文件
- 旧脚本备份
- `dist` 打包产物

安装包体积较大，因此通过 GitHub Releases 发布，不直接放入源码仓库。
