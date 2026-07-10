# FrameScope Monitor

FrameScope Monitor 是本地 Windows 游戏性能诊断工具。它把帧时间、后台进程和系统遥测放到同一条时间线上，用于排查掉帧、卡顿、加载抖动和后台资源竞争。采集数据默认只保存在本机。

## 主要能力

- PresentMon 帧时间、平均 FPS、1% Low 与 0.1% Low。
- 进程 CPU、内存、读写吞吐和窗口信息。
- 系统 CPU、GPU、内存、磁盘、网络、温度、频率、功耗与可用硬件遥测。
- React 报告管理、目标管理、设置和诊断入口。
- 本地交互式 HTML 报告及原始 CSV/JSON 证据。
- 对采集失败、辅助采样器失败和无帧数据场景给出明确分类。

## 当前架构

```text
React + Vite
  -> WebView2 C# host
  -> FrameScopeWebBridge request / response / event
  -> native watcher
  -> one monitor-session worker per active target
  -> PresentMon + ProcessSampler + SystemSampler
  -> status.json + summary.json + raw CSV
  -> bounded ReportGenerator
  -> data.js + HTML + manifest
```

前端位于 `src/frontend/`，宿主和 native worker 位于 `src/app/`。多个 FrameScopeMonitor 进程通常是 UI、watcher 和 monitor-session worker 的正常分工，并不表示打开了多个主窗口。

## 使用流程

1. 安装并启动 FrameScope Monitor。
2. 在 Targets 页面确认游戏进程名并启用目标。
3. 启动监控，然后进入需要排查的游戏场景。
4. 游戏退出或停止监控后，等待报告生成完成。
5. 在 Reports 页面打开报告、报告目录或重新生成报告。

默认安装目录：

```text
%LOCALAPPDATA%\FrameScopeMonitor
```

默认数据目录：

```text
%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs
```

## 报告状态

- `full`：有帧数据，进程与系统采样器均健康。
- `partial`：有帧数据，但至少一个辅助采样器不健康。
- `diagnostic`：没有帧数据，但进程或系统数据仍可用于诊断。
- `error`：没有足够的帧、进程或系统数据。

可打开的完整报告必须同时包含 data.js、HTML 与可解析 manifest。只看到 HTML 不代表报告已完整发布。

## 采样间隔

`TelemetrySampleIntervalMs` 是全局持久采样间隔。旧 target JSON 中的 `SampleIntervalMs`、`ProcessSampleIntervalMs` 和 `SlowSampleIntervalMs` 仅为兼容字段，配置归一化后会跟随全局值。

## 从源码构建

先验证并构建 React 前端：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

再编译 C# 程序和安装包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

C# 测试入口：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
```

current documentation 门禁：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-CurrentDocumentation.ps1
```

## 项目边界

GameLite 自动轻量化是独立项目。普通 FrameScope build 不安装、不修改也不运行 GameLite 脚本或 WMI 触发器。

`docs/implementation-reports/` 与 `docs/test-reports/` 是历史证据；其中记录的旧实现保持历史语义，不作为当前架构指导。
