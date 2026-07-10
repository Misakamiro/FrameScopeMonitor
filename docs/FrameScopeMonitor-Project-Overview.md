# FrameScope Monitor 项目总览

## 产品目标

FrameScope Monitor 记录一次游戏会话中的帧表现、后台进程与系统资源变化，并把可视化报告和原始证据保存在本机。它的重点是帮助用户定位时间相关的性能问题，而不是只显示当前硬件数值。

## 端到端数据流

1. `src/frontend/src/App.tsx` 组合 React 页面。
2. `src/frontend/src/bridge/webviewBridge.ts` 发送带 requestId 的消息并订阅 host event。
3. `src/app/FrameScopeNativeMonitor.WebHost.cs` 在 WebView2 中加载前端。
4. `src/app/FrameScopeWebBridge.cs` 验证请求并调用 host adapter。
5. `src/app/FrameScopeNativeMonitor.Watcher.cs` 扫描配置中的目标进程。
6. 每个 active target 启动一个由 `src/app/FrameScopeNativeMonitor.MonitorSession.cs` 编排的 worker。
7. worker 启动 PresentMon、`src/monitoring/FrameScopeProcessSampler.cs` 与 `src/monitoring/FrameScopeSystemSampler.cs` 对应的可执行程序。
8. worker 写入 status、summary 和原始 CSV；watcher 在目标结束后接管报告生成与恢复。
9. `src/core/FrameScopeBoundedProcessRunner.cs` 以总截止时间运行 ReportGenerator，同时 drain stdout/stderr，并在超时后终止进程树。
10. `src/reporting/FrameScopeReportGenerator.cs` 生成 staging artifacts；`src/core/FrameScopeReportPublisher.cs` 校验后事务发布。

## 目录职责

- `src/frontend/`：React + Vite 前端、组件、页面、主题和 bridge client。
- `src/app/`：WebView2 host、bridge server、watcher、monitor-session worker、报告编排。
- `src/core/`：配置、run contract、报告完整性、原子文件、进程运行器和 retention 策略。
- `src/monitoring/`：ProcessSampler 与 SystemSampler。
- `src/reporting/`：报告读取、分析、序列化与 HTML 模板。
- `src/diagnostics/`：隐私脱敏的诊断 JSON/Markdown。
- `tests/`：C#、PowerShell 和 JavaScript 回归测试。
- `tools/`：前端构建、probe、evidence smoke 和文档门禁。
- `packaging/`：安装、卸载与 payload 说明。

## 进程模型

普通 UI 启动受单实例锁保护。watcher、monitor-session worker 与诊断入口不受普通 UI 锁阻止。

watcher 可以同时管理多个目标，但每个 active target 使用独立 worker。这里的“独立”描述 worker 生命周期，不表示拥有独立采样间隔：所有目标共享持久化的 `TelemetrySampleIntervalMs`。

monitor-session worker 负责解析目标别名与 PID、启动三个采集进程、收集退出证据、原子更新 status/summary，并确保会话进程得到清理。

## 配置模型

`src/core/FrameScopeConfigStore.cs` 是配置规范化入口。

- `PollIntervalMs`：watcher 扫描间隔。
- `TelemetrySampleIntervalMs`：全局持久遥测间隔，范围 500–5000 ms。
- `DataRoot`：run 根目录。
- `OpenReportOnComplete`：全局自动打开开关。
- Targets：启用状态、显示名、进程名和目标级自动打开开关。

旧 target interval 字段继续反序列化，避免破坏旧配置；Normalize 会把它们写回全局采样值。

## run 数据

一次 run 的核心文件：

- `status.json`
- `summary.json`
- `presentmon.csv`
- `process-samples.csv`
- `system-samples.csv`
- `report-progress.json`
- `report-generation.log`
- `charts/framescope-interactive-data.js`
- `charts/framescope-interactive-report.html`
- `charts/framescope-interactive-manifest.json`

报告完整性由 `src/core/FrameScopeReportArtifacts.cs` 判断。manifest 中的 report/data 必须指向同一 run 的最终 canonical 路径。

## report kind

`src/core/FrameScopeRunContract.cs` 提供统一分类：

| kind | 含义 |
| --- | --- |
| full | 有有效帧数据，两个必需辅助采样器健康 |
| partial | 有有效帧数据，但至少一个辅助采样器不健康 |
| diagnostic | 无有效帧数据，但进程或系统数据可用 |
| error | 没有足够的帧、进程或系统数据 |

分类描述当前 run 的证据质量，不应被 UI、bridge 或 diagnostics 各自重新解释。

## 文档生命周期

根 README、AGENTS、本文与 `docs/modules/` 是当前指导。`docs/implementation-reports/` 和 `docs/test-reports/` 是历史快照；保留其中的旧文件名与当时结论，避免破坏证据链。
