# UI 交互与桥接

## 消息模型

React 前端不直接调用本地进程或文件。交互链路为：

```text
React action
  -> webviewBridge request(requestId, type, payload)
  -> C# FrameScopeWebBridge validation
  -> host adapter / native operation
  -> response
  -> progress or state event
  -> React state update
```

相关文件：

- `src/frontend/src/bridge/contract.ts`
- `src/frontend/src/bridge/webviewBridge.ts`
- `src/frontend/src/state/useFrameScopeBridgeState.ts`
- `src/app/FrameScopeWebBridge.Contracts.cs`
- `src/app/FrameScopeWebBridge.cs`
- `src/app/FrameScopeWebBridge.State.cs`
- `src/app/FrameScopeWebBridge.Config.cs`
- `src/app/FrameScopeWebBridge.Monitoring.cs`
- `src/app/FrameScopeWebBridge.Reports.cs`
- `src/app/FrameScopeWebBridge.Diagnostics.cs`
- `src/app/FrameScopeWebBridge.Targets.cs`

## 请求规则

- 每个请求必须有 requestId，response 必须回显它。
- payload 先在 C# 侧验证，再调用 host adapter。
- 报告操作使用 host 生成的 reportId，不接受前端提供的 path、runDir 或 reportHtml。
- 长操作先返回 accepted，再通过 event 发布最终状态或进度。
- 同类长操作使用 in-flight guard，避免重复启动。
- 错误返回稳定 code 和可读 message，不依赖解析日志文本。

## 状态与事件

state snapshot 汇总 watcher、配置、报告进度和 host window state。monitor start/stop、report regenerate 与 diagnostics generate 会发送对应事件。

新增字段时：

1. 更新 C# contract/payload。
2. 更新 `src/frontend/src/bridge/contract.ts`。
3. 更新 `src/frontend/src/types.ts` 或消费端类型。
4. 更新 bridge C# 测试与前端 bridge 测试。

## 报告交互

Reports 页面可以列出历史 run，即使某个 run 的报告不完整；但 canOpenReport 只能来自 `src/core/FrameScopeReportArtifacts.cs` 的完整性结果。

完整报告需要 data.js、HTML、manifest 和正确 canonical paths。reportId 只标识经过 DataRoot 边界验证的 run。目录打开与重新生成仍要由 host adapter 重新验证路径和 raw CSV。

## 设置与目标

Settings 页面保存全局 `TelemetrySampleIntervalMs`、DataRoot、主题、窗口行为和诊断选项。Targets 页面保存启用状态、名称、进程名和自动打开选项。

旧 target interval 字段不作为交互控件暴露；保存配置时由 `src/core/FrameScopeConfigStore.cs` 写回全局采样值。

## 测试入口

- `tests/FrameScopeWebBridgeTests.cs`
- `tests/FrameScopeWebHostLifecycleTests.cs`
- `src/frontend/src/bridge/webviewBridge.test.ts`
- `src/frontend/src/uiInteractionContract.test.ts`
