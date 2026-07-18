# FrameScope Monitor

FrameScope Monitor 是面向 Windows 游戏的本地性能监测与诊断工具。它把帧时间、FPS、进程资源占用和系统硬件遥测放到同一条时间线上，帮助定位掉帧、卡顿、加载抖动和后台资源竞争。

所有采集数据默认只保存在本机。

## 主要功能

- 使用 PresentMon 采集帧时间、平均 FPS、1% Low 和 0.1% Low。
- 记录目标进程的 CPU、内存、磁盘读写和窗口状态。
- 记录系统 CPU、GPU、内存、磁盘、网络、温度、频率、功耗及可用硬件遥测。
- 自动识别已启用的游戏目标，并为每次运行创建独立数据目录。
- 生成包含交互图表、摘要和原始证据的本地 HTML 报告。
- 提供目标管理、监测控制、报告管理、诊断和设置界面。
- 对 PresentMon、辅助采样器和报告生成失败给出明确状态与诊断信息。

## 系统要求

- Windows 10 或 Windows 11，64 位。
- Microsoft Edge WebView2 Runtime。
- 部分 PresentMon ETW 场景需要管理员权限，或将当前用户加入 `Performance Log Users`。

## 安装

构建后会在 `dist/` 生成两种安装包：

- `FrameScopeMonitor-Setup.exe`：标准安装包，适用于已安装 WebView2 Runtime 的系统。
- `FrameScopeMonitor-Full-Setup.exe`：包含 WebView2 Runtime 离线安装程序。

默认安装目录：

```text
%LOCALAPPDATA%\FrameScopeMonitor
```

默认数据与报告目录：

```text
%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs
```

安装更新会保留现有配置和报告目录。

## 使用方法

1. 启动 FrameScope Monitor。
2. 在“目标”页面确认游戏名称和进程名，并启用需要监测的目标。
3. 返回“监控”页面，单击“启动监测”。
4. 启动游戏并复现需要分析的场景。
5. 游戏退出或手动停止监测后，等待报告生成完成。
6. 在“报告”页面打开报告或报告目录。

任务管理器中可能出现多个 `FrameScopeMonitor.exe`。它们分别承担 UI、watcher 和 monitor-session worker 职责，不代表主窗口被重复启动。

## 报告状态

- `full`：帧数据、进程采样和系统采样均可用。
- `partial`：有帧数据，但至少一个辅助采样器不完整。
- `diagnostic`：无有效帧数据，但仍有进程或系统数据可用于诊断。
- `error`：没有足够数据生成有效报告。

旧版本生成的单 HTML 报告仍可直接打开；新报告同时发布 HTML、数据文件和 manifest，以支持完整性校验与恢复。

## 架构

```text
React + Vite UI
  -> WebView2 C# host
  -> FrameScopeWebBridge
  -> native watcher
  -> monitor-session worker
  -> PresentMon + ProcessSampler + SystemSampler
  -> status.json + summary.json + CSV
  -> bounded ReportGenerator
  -> interactive HTML report
```

主要目录：

- `src/frontend/`：React 前端。
- `src/app/`：WebView2 宿主、bridge、watcher 和会话编排。
- `src/core/`：配置、JSON、报告发布、恢复和保留策略。
- `src/monitoring/`：进程与系统采样器。
- `src/reporting/`：报告生成器。
- `packaging/`：安装、卸载和旧版本清理工具源码。
- `tests/`：C#、PowerShell 和前端回归测试。
- `tools/`：构建验证、模拟器和探针。

## 从源码构建

验证并构建前端：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

构建程序和安装包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建并运行 C# 测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
Get-ChildItem .\tests\FrameScope*Tests.exe | ForEach-Object { & $_.FullName }
```

运行边界与文档检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\lightweight-separation-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-CurrentDocumentation.ps1
```

## 数据与隐私

FrameScope Monitor 不需要云端服务。配置、日志、原始 CSV、状态文件和 HTML 报告默认只写入本机。分享报告前，请检查进程名、路径、硬件信息和运行时间是否包含不希望公开的内容。

## 常见问题

### 启动监测后没有 FPS

检查目标进程名、PresentMon 日志和界面诊断状态。若提示 ETW 权限不足，请以管理员身份运行，或配置 `Performance Log Users` 权限。

### 报告显示 partial 或 diagnostic

打开报告目录检查 `status.json`、PresentMon stderr 和采样器日志。报告会保留已成功采集的数据，不会用模拟数据替代真实失败。

### 无法打开界面

确认 WebView2 Runtime 已安装；离线环境可使用 Full Setup。

## 项目边界

GameLite 是完全独立的项目。FrameScopeMonitor 仓库、构建、安装器和运行时不包含、不调用也不修改任何 GameLite 执行入口、兼容包装器、独立项目路径或 WMI 触发器。

## 许可证

仓库当前未声明开源许可证。除非仓库后续添加明确许可证，否则保留所有权利。
