# FrameScope Monitor

FrameScope Monitor 是一个面向 Windows 游戏卡顿排查的性能监测工具。它可以监听你配置的游戏进程：游戏启动时自动开始记录，游戏退出后自动停止记录，并生成可交互的 HTML 性能报告。

## 功能

- 原生 WinForms 图形界面，用于选择和管理需要监测的游戏进程。
- 自动监听模式：先启动 FrameScope Monitor，再正常打开游戏即可自动开始记录。
- 高频采样后台进程占用，包括 CPU、内存、磁盘 IO 和进程活动。
- 基于 PresentMon 捕获帧时间数据，用于分析平均 FPS、1% Low 和 0.1% Low。
- 自动生成交互式 HTML 报告，包含 FPS 波动、系统指标、进程占用和性能图表。
- 提供离线安装包，安装后不需要用户额外安装 Python 等依赖。

## 默认预设

默认配置包含以下游戏进程预设：

- Counter-Strike 2
- Delta Force
- Neverness To Everness
- Valorant
- Cyberpunk 2077
- Battlefield 6
- Hogwarts Legacy
- OPUS Prism Peak

也可以在软件界面里手动添加更多进程。

## 安装

从 GitHub Releases 下载 `FrameScopeMonitor-Setup.exe`，双击运行即可安装。

安装包内置：

- FrameScope Monitor 主程序
- PresentMon
- 便携 Python 运行时
- 自动监测与报告生成脚本

默认安装位置：

```text
%LOCALAPPDATA%\FrameScopeMonitor
```

安装完成后会创建桌面快捷方式和开始菜单快捷方式。

## 使用方式

1. 启动 `FrameScope Monitor`。
2. 在列表中勾选需要监测的游戏进程。
3. 点击启动监测。
4. 正常打开游戏。
5. 游戏退出后，软件会停止记录并打开对应的 HTML 报告。

生成的数据和报告默认保存在安装目录下的 `framescope-runs` 文件夹中。

## 从源码构建

运行：

```powershell
.\build.ps1
```

构建脚本会调用 Windows 自带的 .NET Framework 编译器，并期望便携 Python 位于：

```text
%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\python
```

如果只需要编译 GUI 主程序，可以直接用 .NET Framework 4.x 引用编译 `FrameScopeNativeMonitor.cs`。

## 核心运行文件

- `FrameScopeMonitor.exe`
- `FrameScopeWatcher.ps1`
- `Monitor-CS2-HighFreq.ps1`
- `Generate-CS2-FrameScope-Interactive-Report.py`
- `tools\PresentMon-2.4.1-x64.exe`

## 仓库说明

以下内容不会提交到 git：

- 本地监测数据
- HTML 报告输出
- 运行日志
- 本地配置文件
- 旧脚本备份
- `dist` 打包产物

安装包体积较大，因此通过 GitHub Releases 发布，不直接放进仓库源码中。
