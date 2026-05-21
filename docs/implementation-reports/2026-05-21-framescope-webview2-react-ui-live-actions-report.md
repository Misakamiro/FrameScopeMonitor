# FrameScope WebView2 React UI Live Actions Report - 2026-05-21

## Current Conclusion

PASS. The React WebView2 UI now enables the backend bridge requests that were added in the bridge extension round:

- `reports.list`
- `reports.open`
- `reports.openDirectory`
- `reports.regenerate`
- `targets.get`
- `targets.save`
- `monitor.start`
- `monitor.stop`
- `diagnostics.generate`

The changes stayed inside the frontend UI/state/mock-preview surface plus this report. No C# bridge, monitoring, reporting, diagnostics, lightweight script, build script, packaging logic, GameLite, WMI, or SGuard source was edited in this round.

`build.ps1` was run only because it is part of the requested verification chain. This run produced the usual `dist\FrameScopeMonitor-Setup.exe` build output, but this round did not change packaging logic or perform an installer release handoff.

## Enabled Buttons And Bridge Requests

Overview:

- `启动监控`: sends `monitor.start`.
- `停止监控`: sends `monitor.stop`.
- `刷新状态`: still sends `state.snapshot`.
- `打开数据目录待接入`: remains disabled because no matching request was included in this UI enablement scope.

Targets:

- `读取目标`: sends `targets.get`.
- `保存目标`: sends `targets.save` with editable `targets`, optional `dataRoot`, and optional `openReportOnComplete`.
- `刷新进程`: still sends `processes.refresh`.
- `新增目标待接入`: remains disabled. The UI does not invent process-picker-to-target write semantics.

Reports:

- `刷新列表`: sends `reports.list`.
- `打开报告`: sends `reports.open` with backend-returned `reportId` only.
- `打开目录`: sends `reports.openDirectory` with backend-returned `reportId` only.
- `重新生成`: sends `reports.regenerate` with backend-returned `reportId` only.
- `生成诊断` / row `诊断`: sends `diagnostics.generate`; row action includes backend-returned `reportId`.

## Page State Behavior

Reports:

- `reports.list` has inline loading, success, and failure states.
- `reports.open` and `reports.openDirectory` disable the row action while in flight and show success or failure inline.
- `reports.regenerate` treats the initial bridge response as accepted only; final success/failure is driven by `event.reportProgress`.
- The UI never builds report paths. It stores and sends only `reportId` for report actions.

Targets:

- `targets.get` owns the editable target form source of truth.
- Dirty state compares the local target/data-root/open-report draft against the last bridge-returned payload.
- `targets.save` has dirty, saving, saved, and failed states.
- Save failure leaves the user's draft values in place.
- The UI does not send `configPath` or any config path override.

Monitor:

- Start/stop buttons are disabled during an in-flight monitor action.
- Start is disabled once the runtime state is running; stop is disabled when it is stopped.
- `monitor.start` and `monitor.stop` are accepted/in-flight first and completed through `event.status`.
- `event.status` updates visible watcher runtime state. `event.error` turns the monitor operation into a retryable failure.
- The UI does not invent PUBG, PresentMon, or game process health. It displays only bridge snapshot/status-event facts.

Diagnostics:

- `diagnostics.generate` shows generating, completed, and failed states.
- Completed state displays backend-returned `markdownPath`, `jsonPath`, `runDir`, and `reportHtml` when present.
- The UI does not construct diagnostics paths.

## Mock / Live / Disabled Boundary

- Browser/Vite preview uses `createMockBridgeAdapter()` and visibly labels itself as browser mock preview.
- The browser mock adapter now covers the same request names so the UI can be tested without WebView2, but it remains labeled as mock and can be forced to fail with `?mockFailure=...` for failure-state evidence.
- WebView2 live uses `window.chrome.webview` via `WebViewBridgeClient`; if the real bridge returns unsupported/error, the UI displays the real failure and does not fall back to mock success.
- Disabled controls remain only where no request was included in this round: Overview open data directory, target creation from process picker, topbar search/refresh/notification shortcuts.

## Modified Files

- `src\frontend\src\data\mockPreview.ts`
- `src\frontend\src\state\useFrameScopeBridgeState.ts`
- `src\frontend\src\pages\OverviewPage.tsx`
- `src\frontend\src\pages\TargetsPage.tsx`
- `src\frontend\src\pages\ReportsPage.tsx`
- `src\frontend\src\pages\AboutPage.tsx`
- `src\frontend\src\pages\pages.css`
- `src\frontend\src\layout\AppShell.tsx`
- `src\frontend\src\layout\SidebarNav.tsx`
- `docs\implementation-reports\2026-05-21-framescope-webview2-react-ui-live-actions-report.md`

## Evidence Paths

Evidence root:

- `artifacts\webview2-react-ui-live-actions-20260521`

WebView2 live smoke:

- `webview2-react-live-smoke.json`
- `webview2-react-live-smoke.png`
- `webview2-react-live-smoke-overview.png`
- `webview2-react-live-smoke-targets-loading.png`
- `webview2-react-live-smoke-targets-result.png`
- `webview2-react-live-smoke-reports.png`
- `webview2-react-live-smoke-settings-clean.png`
- `webview2-react-live-smoke-settings-dirty.png`
- `webview2-react-live-smoke-settings-saving.png`
- `webview2-react-live-smoke-settings-saved.png`
- `webview2-react-live-smoke-about.png`

WebView2 motion frames:

- `webview2-react-live-smoke-transition-overview-targets-01.png`
- `webview2-react-live-smoke-transition-overview-targets-02.png`
- `webview2-react-live-smoke-transition-overview-targets-03.png`
- `webview2-react-live-smoke-transition-targets-reports-01.png`
- `webview2-react-live-smoke-transition-targets-reports-02.png`
- `webview2-react-live-smoke-transition-targets-reports-03.png`
- `webview2-react-live-smoke-transition-reports-settings-01.png`
- `webview2-react-live-smoke-transition-reports-settings-02.png`
- `webview2-react-live-smoke-transition-reports-settings-03.png`
- `webview2-react-live-smoke-transition-targets-about-01.png`
- `webview2-react-live-smoke-transition-targets-about-02.png`
- `webview2-react-live-smoke-transition-targets-about-03.png`

Reduced motion:

- `webview2-react-reduced-motion-smoke.json`
- `webview2-react-reduced-motion-smoke.png`
- `webview2-react-reduced-motion-smoke-overview.png`
- `webview2-react-reduced-motion-smoke-targets-loading.png`
- `webview2-react-reduced-motion-smoke-targets-result.png`
- `webview2-react-reduced-motion-smoke-reports.png`
- `webview2-react-reduced-motion-smoke-settings-clean.png`
- `webview2-react-reduced-motion-smoke-settings-dirty.png`
- `webview2-react-reduced-motion-smoke-settings-saving.png`
- `webview2-react-reduced-motion-smoke-settings-saved.png`
- `webview2-react-reduced-motion-smoke-about.png`
- `webview2-react-reduced-motion-smoke-transition-*.png`

Browser mock UI state screenshots:

- `browser-mock-overview.png`
- `browser-mock-monitor-start-inflight.png`
- `browser-mock-monitor-started.png`
- `browser-mock-monitor-stop-inflight.png`
- `browser-mock-monitor-stopped.png`
- `browser-mock-monitor-start-failed.png`
- `browser-mock-targets-result.png`
- `browser-mock-targets-dirty.png`
- `browser-mock-targets-saving.png`
- `browser-mock-targets-saved.png`
- `browser-mock-targets-failed-kept-input.png`
- `browser-mock-reports-result.png`
- `browser-mock-reports-list-loading.png`
- `browser-mock-reports-list-result-after-refresh.png`
- `browser-mock-reports-open-inflight.png`
- `browser-mock-reports-open.png`
- `browser-mock-reports-open-directory-inflight.png`
- `browser-mock-reports-open-directory.png`
- `browser-mock-reports-regenerate-inflight.png`
- `browser-mock-reports-regenerate-completed.png`
- `browser-mock-reports-regenerate-failed.png`
- `browser-mock-diagnostics-generating.png`
- `browser-mock-diagnostics-completed.png`
- `browser-mock-diagnostics-failed.png`
- `browser-mock-settings.png`
- `browser-mock-about.png`
- `browser-mock-state-summary.json`

WinForms fallback:

- `winforms-fallback-overview.png`

## WebView2 Smoke Result

Normal WebView2 smoke: PASS.

Key JSON values:

- `success=true`
- `smokePayload.success=true`
- `smokePayload.frontendPath=...\src\frontend\dist`
- `bridgeExtensionSmoke.success=true`
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

Reduced-motion WebView2 smoke: PASS.

Key JSON values:

- `success=true`
- `smokePayload.reducedMotion=true`
- `bridgeExtensionSmoke.success=true`

## Verification Results

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`: PASS after stopping project-local Vite/esbuild processes that locked `node_modules\@esbuild\win32-x64\esbuild.exe`; rerun completed `npm ci`, typecheck, tests, and build.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
- `.\tests\FrameScopeUiStateTests.exe`: PASS.
- `.\tests\FrameScopeReportProgressTests.exe`: PASS.
- `.\tests\FrameScopeReportManifestTests.exe`: PASS.
- `.\tests\FrameScopeWebBridgeTests.exe`: PASS.
- bundled Node `.\tests\chart-sampling-tests.js`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings / 0 errors.
- WebView2 React live smoke: PASS.
- WebView2 React reduced-motion smoke: PASS.
- WinForms fallback screenshot: PASS.

Final gates after writing this report:

- `"C:\Program Files\Git\cmd\git.exe" diff --check`: PASS, exit 0 with existing LF/CRLF warnings only.
- residual process check: PASS, no matching FrameScope/WebView2/front-end preview processes found.

## Retest Recommendation

Recommend tester retest: YES.

The bridge and frontend smoke gates are passing, but this round changes real user-facing action availability. A tester should specifically retest WebView2 live reports/targets/monitor/diagnostics buttons with real user data, plus one forced backend error path, before treating this as ready for packaging validation.
