# FrameScope WebView2 React UI Bridge Interaction Report

Date: 2026-05-20

## Current Conclusion

The React WebView2 UI is now bridge-first for the currently implemented C# bridge surface:

- `state.snapshot`
- `config.get`
- `processes.refresh`
- `config.save`

The default WinForms path is still preserved. WebView2 remains behind the explicit `--web-ui` side entry. Reports, diagnostics, monitor start/stop, and report-open actions remain disabled because the C# bridge does not yet expose those adapters.

## Files Changed In This Round

Frontend interaction and contract:

- `src/frontend/src/bridge/contract.ts`
- `src/frontend/src/bridge/webviewBridge.ts`
- `src/frontend/src/bridge/webviewBridge.test.ts`
- `src/frontend/src/state/useFrameScopeBridgeState.ts`
- `src/frontend/src/App.tsx`
- `src/frontend/src/layout/AppShell.tsx`
- `src/frontend/src/layout/TopStatusBar.tsx`
- `src/frontend/src/layout/SidebarNav.tsx`
- `src/frontend/src/components/ToolbarButton.tsx`
- `src/frontend/src/pages/OverviewPage.tsx`
- `src/frontend/src/pages/TargetsPage.tsx`
- `src/frontend/src/pages/SettingsPage.tsx`
- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/pages/AboutPage.tsx`
- `src/frontend/src/pages/pages.css`
- `src/frontend/src/data/mockPreview.ts`

Host smoke evidence support:

- `src/app/FrameScopeNativeMonitor.WebHost.cs`

Report:

- `docs/implementation-reports/2026-05-20-framescope-webview2-react-ui-bridge-interaction-report.md`

Related WebView2 bridge/build files already present in the broader WebView2 lane remain part of the same dirty worktree. This report does not claim unrelated GameLite, monitoring sampler, report generator, diagnostics, or installer-release work.

## Bridge Client Structure

`contract.ts` defines the frontend contract that matches the C# bridge shape:

- request envelope: `{ requestId, type, payload }`
- response envelope: `{ requestId, type: "response", ok, payload, error }`
- event envelope: `{ type, payload, sentAt? }`
- request types: `state.snapshot`, `config.get`, `config.save`, `processes.refresh`
- event types: `event.status`, `event.error`, `event.processesRefreshed`
- typed payloads for snapshot, config, process refresh, process list, and bridge errors

`webviewBridge.ts` owns the real WebView2 adapter:

- detects `window.chrome.webview`
- sends every request with a generated `requestId`
- tracks pending requests by `requestId`
- resolves matched `response` messages
- rejects failed responses using `BridgeRequestError`
- rejects timed-out requests
- supports event subscription/unsubscription
- exposes a single `createFrameScopeBridgeAdapter()` factory

The browser/Vite preview path falls back to `createMockBridgeAdapter()` from `mockPreview.ts`; mock behavior is centralized there and not scattered across pages.

## Real Requests Wired

- `state.snapshot`: loaded on app startup and refreshable from Overview.
- `config.get`: loaded on app startup, used by Targets and Settings.
- `processes.refresh`: enabled from Targets, returns accepted/in-flight through the response and updates the process list through `event.processesRefreshed`.
- `config.save`: enabled from Settings when the local form is dirty; sends only `payload.config`; no frontend path override is sent.

## Disabled Boundaries

These remain disabled because the C# bridge has not implemented safe host-side adapters yet:

- `monitor.start`
- `monitor.stop`
- report open
- report regenerate
- diagnostics generate
- add target from process picker
- direct target save from Targets page
- open data folder from Overview

The UI labels these actions as next-stage or disabled instead of pretending they work.

## Page Interaction Status

Overview:

- Reads real `state.snapshot`.
- Shows bridge status, watcher state, config summary, and report history availability.
- Shows empty/error/loading states if snapshot data is unavailable.

Targets:

- Reads target config from `config.get`.
- Enables process refresh through `processes.refresh`.
- Shows local loading/success/failure feedback.
- Updates the process result list from `event.processesRefreshed`.

Settings:

- Reads real config from `config.get`.
- Edits a local draft.
- Shows `dirty` versus `clean`.
- Saves through `config.save`.
- Refreshes the normalized returned config after save.
- Shows saving/success/failure feedback.

Reports:

- Remains read-only.
- Uses snapshot report-history metadata only.
- Report open/regenerate/diagnostics actions stay disabled.

About:

- Static boundary and version information.
- Shows whether the current adapter is WebView2 live or browser mock preview.

## Loading, Success, Failure Behavior

- Snapshot/config load states are local inline states, not full-window blockers.
- Process refresh disables only the refresh button while waiting and has a 15s event timeout after accepted responses.
- Config save disables save/reset only while saving and keeps a short local saving state visible.
- Bridge request timeouts reject instead of leaving buttons stuck loading.
- `event.error` updates the relevant process-refresh failure state when request IDs match.

## Screenshot And Evidence Paths

WebView2 React final smoke:

- `artifacts/webview2-ui-bridge/react-interaction-smoke-final.json`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-overview.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-targets-loading.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-targets-result.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-reports.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-about.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-settings-clean.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-settings-dirty.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-settings-saving.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-settings-saved.png`

Continuous page-switch frames:

- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-transition-targets-01.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-transition-targets-02.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-transition-targets-03.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-transition-settings-01.png`
- `artifacts/webview2-ui-bridge/react-interaction-smoke-final-transition-settings-02.png`

Reduced motion:

- `artifacts/webview2-ui-bridge/react-reduced-motion-smoke-final.json`
- `artifacts/webview2-ui-bridge/react-reduced-motion-smoke-final-overview.png`
- `artifacts/webview2-ui-bridge/react-reduced-motion-smoke-final-transition-targets-01.png`
- `artifacts/webview2-ui-bridge/react-reduced-motion-smoke-final-transition-targets-02.png`
- `artifacts/webview2-ui-bridge/react-reduced-motion-smoke-final-transition-targets-03.png`
- `artifacts/webview2-ui-bridge/react-reduced-motion-smoke-final-settings-saving.png`
- `artifacts/webview2-ui-bridge/react-reduced-motion-smoke-final-settings-saved.png`

WinForms fallback:

- `artifacts/webview2-ui-bridge/winforms-fallback-overview-final.png`

## Verification Results

- `npm install`: not run; no `npm` executable is available on PATH and the bundled Node runtime contains only `node.exe`. Dependencies were not changed, and validation used existing `node_modules` plus `package-lock.json`.
- `node.exe node_modules/typescript/bin/tsc --noEmit`: PASS.
- `node.exe node_modules/vitest/vitest.mjs run`: PASS, 2 files / 7 tests.
- `node.exe node_modules/vite/bin/vite.js build`: PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
- `.\tests\FrameScopeUiStateTests.exe`: PASS.
- `.\tests\FrameScopeReportProgressTests.exe`: PASS.
- `.\tests\FrameScopeReportManifestTests.exe`: PASS.
- `.\tests\FrameScopeWebBridgeTests.exe`: PASS.
- `node .\tests\chart-sampling-tests.js`: WindowsApps `Access is denied`.
- bundled Node `tests\chart-sampling-tests.js`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings / 0 errors.
- WebView2 normal smoke: PASS, `success=true`, React loaded from `src/frontend/dist`, `configDirtyObserved=true`, `configSavingObserved=true`, `configSaveSuccessObserved=true`.
- WebView2 reduced-motion smoke: PASS, `reducedMotion=true`, same bridge interactions passed.
- WinForms fallback screenshot: PASS, PNG created.
- `"C:\Program Files\Git\cmd\git.exe" diff --check`: PASS exit code 0; only LF/CRLF warnings on existing tracked files.
- Residual process check: PASS; no matching FrameScope/PresentMon/sampler/report/FakePresentMon/TslGame/GameLite processes remained.
- Smoke config restore check: PASS; `framescope-config.json` restored to `PollIntervalMs: 1000` after smoke.

## Known Risks

- Browser preview remains mock-only by design; it is useful for visual/dev preview, not runtime truth.
- `processes.refresh` currently returns up to 250 process rows and reports truncation; a later picker/search UX should handle large lists more deliberately.
- `config.save` has host-side path authority, but richer validation/error messages should remain on the C# side in future work.
- WebView2 evidence JSON is written UTF-8 with BOM by `File.WriteAllText(..., Encoding.UTF8)`; Node JSON parsing needs BOM stripping or a no-BOM writer if this becomes a toolchain issue.

## Suggested Next Backend Bridge Extensions

- Add host-side `monitor.start` and `monitor.stop` with accepted/in-flight response plus later status/error events.
- Add safe `reports.list` before any report-open action.
- Add `reports.open` with host-side path validation only.
- Add `diagnostics.generate` as accepted/in-flight with progress/status events.
- Add target update APIs with host validation rather than letting the frontend write arbitrary config paths.

## Next Tester Prompt

Use this in the next testing lane:

```text
/goal 你现在是 FrameScope Monitor WebView2 React UI 交互测试员。
项目路径：
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d

请基于 docs\implementation-reports\2026-05-20-framescope-webview2-react-ui-bridge-interaction-report.md 做验收。
重点验证：
1. 默认启动仍是 WinForms，不是 WebView2。
2. --web-ui 加载 React dist，不是旧 smoke HTML。
3. Overview 的 state.snapshot 是真实 bridge 数据。
4. Settings 的 config.get / dirty / config.save / success-failure 反馈真实可见。
5. Targets 的 processes.refresh 非阻塞，并通过 event.processesRefreshed 更新列表。
6. Reports/diagnostics/monitor.start/monitor.stop 等未接后端的按钮保持 disabled。
7. reduced motion 仍有效。
8. 跑完整前端、C#、WebView2 smoke、WinForms fallback、残留进程检查，并输出 PASS/FAIL 和截图路径。
```
