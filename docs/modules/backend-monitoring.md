# 后端监控与报告模块

## 运行链路

```text
native watcher
  -> monitor-session worker per active target
  -> PresentMon + ProcessSampler + SystemSampler
  -> status / summary / raw CSV
  -> bounded FrameScopeReportGenerator.exe
  -> transactional report artifacts
```

主要入口：

- `src/app/FrameScopeNativeMonitor.Watcher.cs`
- `src/app/FrameScopeNativeMonitor.MonitorSession.cs`
- `src/app/FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs`
- `src/monitoring/FrameScopeProcessSampler.cs`
- `src/monitoring/FrameScopeSystemSampler.cs`
- `src/app/FrameScopeNativeMonitor.ReportProcess.cs`
- `src/reporting/FrameScopeReportGenerator.cs`

## watcher 与 worker

watcher 读取规范化配置并扫描目标进程。一个 target 进入 active 状态后，watcher 启动一个 monitor-session worker；worker 结束后，watcher 读取该 run 的最终状态、恢复缺失报告、写 history，并按配置打开完整报告。

worker 角色通过命令行区分，不能按 `FrameScopeMonitor.exe` 进程名统一结束。清理逻辑需要保留 UI、watcher 与其他仍活跃 worker 的边界。

## 采样器

PresentMon 负责帧呈现数据。ProcessSampler 负责进程 CPU、内存和 IO；SystemSampler 负责系统、GPU、磁盘、网络与硬件遥测。

`TelemetrySampleIntervalMs` 是当前持久配置中的全局间隔。旧 target interval 属性只用于兼容旧 JSON，`src/core/FrameScopeConfigStore.cs` 会将它们归一化为全局值。

采样器健康不能只看 exe 是否启动，还要结合 executable、PID、起止时间、exit code、提前退出/强制停止、CSV 有效行与 stderr tail。这些证据进入 status、summary、manifest 和 diagnostics。

## 报告进程边界

`src/core/FrameScopeBoundedProcessRunner.cs` 并发读取 stdout/stderr，并以固定 wall-clock deadline 约束 ReportGenerator。等待进度更新不会重置截止时间；超时会终止完整进程树。

失败或超时时，status/summary 保存起止时间、timeout、exit code、错误和可重试状态。输出 tail 有大小上限，避免大量输出造成死锁或无界内存。

## 完整性与事务发布

`src/core/FrameScopeReportArtifacts.cs` 是报告可用性的唯一权威。完整报告需要预期 HTML、`framescope-interactive-data.js`、可解析 manifest，以及与当前 run 最终 canonical 路径一致的 manifest report/data。

`src/core/FrameScopeReportPublisher.cs` 在 run 下的 sibling staging 目录生成文件，校验成功后才 swap 到 charts。旧 charts 在发布期间进入 backup；发布失败时回滚，重启时恢复遗留 backup。manifest 最后写入，并记录最终路径。

## 恢复与分类

恢复扫描只接受 CSV 中的有效数据行，不凭空文件或 header-only CSV 触发生成。terminal run 缺完整报告时可以恢复；capturing run 只有在确认不活跃的恢复语境下才处理。

- `full`：有效帧 + 健康的进程和系统采样。
- `partial`：有效帧 + 辅助采样不完整。
- `diagnostic`：无有效帧，但进程或系统数据可用。
- `error`：没有可用采集数据。

## 修改检查

- 子进程变更：覆盖正常退出、提前退出、强制停止与进程树清理。
- CSV 变更：覆盖 header、有效行计数、损坏行和空文件。
- report 变更：覆盖三件套、canonical path、rollback 和 residue。
- status 变更：保持 status、summary、history 与 bridge 字段一致。
