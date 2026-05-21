# FrameScope WebView2 Bridge Extension Report - 2026-05-21

## Current Conclusion

The `--web-ui` bridge extension is implemented behind the existing WebView2 side entry. The default no-argument WinForms launch path remains unchanged.

This round adds real C# host adapters for monitor control, reports, diagnostics, and target config updates. Unsupported or unsafe payloads return explicit bridge errors; no new request fakes success.

## Added Requests

- `monitor.start`
- `monitor.stop`
- `reports.list`
- `reports.open`
- `reports.openDirectory`
- `reports.regenerate`
- `diagnostics.generate`
- `targets.get`
- `targets.save`

## Added Events

- `event.reportProgress`
- `event.reportsChanged`

Existing events remain:

- `event.status`
- `event.error`
- `event.processesRefreshed`

## C# Handler Map

- `monitor.start`: `FrameScopeWebBridge.Monitoring.cs` accepts the request, then calls the WebView2 host adapter in `FrameScopeNativeMonitor.WebHost.cs`, which starts the existing `--watcher --config <host config>` process.
- `monitor.stop`: `FrameScopeWebBridge.Monitoring.cs` accepts the request, then calls the host adapter, which reuses the existing FrameScope background-process enumeration and cleanup path.
- `reports.list`: `FrameScopeWebBridge.Reports.cs` reads validated history entries and report HTMLs under the resolved FrameScope data root.
- `reports.open`: `FrameScopeWebBridge.Reports.cs` resolves a host-generated `reportId`, then calls the host adapter to open the validated HTML through existing report-open logic.
- `reports.openDirectory`: resolves `reportId`, then opens the validated run directory through the host adapter.
- `reports.regenerate`: resolves `reportId`, checks monitor CSV presence, accepts the long task, then calls existing report generation orchestration through the host adapter.
- `diagnostics.generate`: accepts a long task and calls `FrameScopeDiagnostics.GenerateReport` through the host adapter.
- `targets.get`: returns current targets from `FrameScopeConfigStore`.
- `targets.save`: validates editable target payloads, uses `FrameScopeConfigStore.BuildConfigFromEditableTargets`, saves only the host config path, and returns the reloaded target state.

## Safety Boundary

- Frontend requests still use `{ requestId, type, payload }`.
- Responses still use `{ requestId, type: "response", ok, payload/error }`.
- `reports.open`, `reports.openDirectory`, `reports.regenerate`, and `diagnostics.generate` reject frontend path authority such as `path`, `runDir`, `reportHtml`, `directory`, or `file`.
- Report actions use host-generated `reportId`; C# re-resolves that ID from validated report history/data-root scans before opening or regenerating anything.
- `config.save` and `targets.save` reject config path overrides.
- Monitor start/stop and report/diagnostics long tasks return `accepted` or `in_flight` first, then push completion or error through events.

## Modified Files

- `src\app\FrameScopeWebBridge.Contracts.cs`
- `src\app\FrameScopeWebBridge.cs`
- `src\app\FrameScopeWebBridge.Monitoring.cs`
- `src\app\FrameScopeWebBridge.Reports.cs`
- `src\app\FrameScopeWebBridge.Diagnostics.cs`
- `src\app\FrameScopeWebBridge.Targets.cs`
- `src\app\FrameScopeNativeMonitor.WebHost.cs`
- `src\frontend\src\bridge\contract.ts`
- `tests\FrameScopeWebBridgeTests.cs`
- `tests\Build-FrameScopeTests.ps1`
- `build.ps1`

`build.ps1` and `tests\Build-FrameScopeTests.ps1` changed only because the new C# partial files must be included in explicit source lists.

## WebView2 Smoke Evidence

- `artifacts\webview2-bridge-extension\smoke.json`
- `artifacts\webview2-bridge-extension\smoke.png`

Observed smoke payload:

- `reportsListOk=true`
- `targetsGetOk=true`
- `reportOpenPathRejected=true`
- `targetsSavePathRejected=true`
- `reportRegenerateMissingRejected=true`
- `diagnosticsAccepted=true`
- `diagnosticsCompleted=true`
- `monitorStartAccepted=true`
- `monitorStarted=true`
- `monitorStopAccepted=true`
- `monitorStopped=true`

## WebView2 Reference / MSB3277

The shipped app still references only:

- `Microsoft.Web.WebView2.Core.dll`
- `Microsoft.Web.WebView2.WinForms.dll`
- `WebView2Loader.dll`

The WPF WebView2 assembly remains excluded from the main `csc.exe` build. The main shipped build therefore does not run MSBuild conflict resolution and does not emit the spike-only `MSB3277 WindowsBase` warning.

## Remaining Disabled / Not Enabled In UI

The backend bridge now exposes the host adapters, but the React UI action buttons should remain disabled until the UI interaction owner adds visible in-flight, success, failure, and retry states for each action.

Recommended next UI enablement order:

1. Enable `reports.list` read-only rendering.
2. Enable `targets.get` and `targets.save` with dirty-state protection.
3. Enable `monitor.start` and `monitor.stop` with strong in-flight feedback.
4. Enable `diagnostics.generate`.
5. Enable `reports.open`, `reports.openDirectory`, and `reports.regenerate`.

## Verification Status

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`: PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
- `.\tests\FrameScopeUiStateTests.exe`: PASS.
- `.\tests\FrameScopeReportProgressTests.exe`: PASS.
- `.\tests\FrameScopeReportManifestTests.exe`: PASS.
- `.\tests\FrameScopeWebBridgeTests.exe`: PASS.
- bundled Node `.\tests\chart-sampling-tests.js`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings / 0 errors.
- WebView2 bridge extension smoke: PASS.
- WinForms fallback screenshot: PASS.
- `"C:\Program Files\Git\cmd\git.exe" diff --check`: PASS with LF/CRLF warnings only.
- Residual process check: PASS, no matching FrameScope/PresentMon/sampler/report/FakePresentMon/TslGame/GameLite processes.
