# FrameScope Monitor 项目协作说明

## 当前事实来源

修改前先查看真实代码、配置和 run 数据。本文、根目录 README、项目总览和 `docs/modules/` 下的模块说明属于 current guidance。

`docs/implementation-reports/` 和 `docs/test-reports/` 记录当时的实现与证据，必须保留历史属性；其中出现的旧目录、旧界面或旧命令不代表当前生产架构，不应为了“看起来最新”而回写历史报告。

## 当前生产架构

FrameScope Monitor 是本地 Windows 游戏性能诊断工具。当前链路为：

1. `src/frontend/` 中的 React + Vite 前端渲染用户界面。
2. `src/app/FrameScopeNativeMonitor.WebHost.cs` 承载 WebView2，并把前端消息交给 C#。
3. `src/app/FrameScopeWebBridge.cs` 及其 partial 文件实现 request/response/event 契约。
4. native watcher 发现已启用目标；每个 active target 对应一个 monitor-session worker。
5. worker 启动 PresentMon、`FrameScopeProcessSampler.exe` 和 `FrameScopeSystemSampler.exe`。
6. worker 持久化 `status.json`、`summary.json` 与原始 CSV。
7. watcher 通过有界进程运行器调用 `FrameScopeReportGenerator.exe`。
8. 完整报告由 `framescope-interactive-data.js`、HTML 和 manifest 三件套组成。

多个 `FrameScopeMonitor.exe` 进程可能分别承担 UI、watcher 或 monitor-session worker 角色；不能仅按进程名判断重复启动，也不能粗暴结束全部同名进程。

## 代码归属

- `src/frontend/src/App.tsx`：前端应用入口与页面组合。
- `src/frontend/src/bridge/webviewBridge.ts`：WebView2 消息客户端。
- `src/frontend/src/pages/`：Overview、Reports、Settings、Targets、About 页面。
- `src/app/FrameScopeNativeMonitor.WebHost.cs`：WebView2 宿主和 host adapter。
- `src/app/FrameScopeWebBridge.Contracts.cs`：桥接请求、结果和宿主接口。
- `src/app/FrameScopeNativeMonitor.Watcher.cs`：目标发现、worker 生命周期和完成编排。
- `src/app/FrameScopeNativeMonitor.MonitorSession.cs`：一次目标会话的采集编排。
- `src/monitoring/FrameScopeProcessSampler.cs`：进程 CPU、内存与 IO 采样。
- `src/monitoring/FrameScopeSystemSampler.cs`：系统、GPU、磁盘、网络和硬件遥测采样。
- `src/app/FrameScopeNativeMonitor.ReportProcess.cs`：报告进程调用与结果处理。
- `src/core/FrameScopeBoundedProcessRunner.cs`：有总截止时间、并发 drain 和进程树终止的进程运行器。
- `src/reporting/FrameScopeReportGenerator.cs`：报告数据分析和发布入口。
- `src/core/FrameScopeReportArtifacts.cs`：报告完整性的唯一检查口径。
- `src/core/FrameScopeReportPublisher.cs`：报告 staging、校验、swap 和回滚。
- `tests/Build-FrameScopeTests.ps1`：C# 测试编译入口。

## 采样配置

`TelemetrySampleIntervalMs` 是持久化的全局遥测间隔，当前会归一化到 500–5000 ms。Targets 中保留的 `SampleIntervalMs`、`ProcessSampleIntervalMs` 和 `SlowSampleIntervalMs` 只是旧配置兼容字段；加载和保存时会统一归一化为全局值，不是可独立调节的生产设置。

`PollIntervalMs` 是 watcher 扫描周期，不等同于遥测采样周期。

## run 与报告契约

默认 run 位于 `%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs\<Game>\<Run>`。优先检查：

- `status.json`：会话阶段、采集证据、报告状态。
- `summary.json`：会话摘要和报告摘要。
- `presentmon.csv`：原始帧数据。
- `process-samples.csv`：进程采样。
- `system-samples.csv`：系统采样。
- `charts/framescope-interactive-data.js`：报告数据对象。
- `charts/framescope-interactive-report.html`：交互报告。
- `charts/framescope-interactive-manifest.json`：最终路径、计数和报告分类。

报告分类由 `src/core/FrameScopeRunContract.cs` 统一定义：

- `full`：有有效帧数据，且必需的进程与系统采样器健康。
- `partial`：有有效帧数据，但至少一个必需辅助采样器不健康。
- `diagnostic`：没有有效帧数据，但进程或系统 CSV 有有效数据。
- `error`：帧、进程和系统数据均不足以生成可用诊断。

不能把仅存在 HTML、缺 data.js、缺 manifest 或 manifest 路径错误的目录当成完整报告。

## 修改与验证原则

- 不根据当前活跃进程推断用户上传的历史 run；以该 run 的 CSV/JSON 为准。
- 不通过删除原始数据来优化报告；显示层可以抽样，统计口径仍来自完整数据。
- 状态 JSON 使用原子写入；append-only 日志和 history 保持追加语义。
- 修改桥接契约时同步更新 C# contract、前端 contract 和对应测试。
- 修改报告生成时验证四种 report kind、三件套完整性、失败回滚和残留清理。
- 修改采集时验证 worker、三个采集进程、退出清理与 status/summary 证据。
- 完成前执行与改动范围相称的测试、构建和 `git diff --check`。

## GameLite 边界

GameLite 自动轻量化是独立项目，不属于 FrameScope 生产链路。普通 FrameScope build、安装器和运行时不得安装、修改或调用 GameLite 脚本，也不得创建或删除其 WMI 触发器。

本仓库仅保留 `tests/lightweight-separation-tests.ps1` 作为边界验证。没有用户明确授权时，不执行任何 GameLite WMI 安装、移除或真实游戏操作。
