# FrameScope Monitor WebView2 React UI / Bridge Retest Report

Date: 2026-05-21
Role: WebView2 React UI interaction tester
Scope: read-only retest, except writing this report

## 1. 当前复测结论

部分通过。

默认 WinForms fallback、WebView2 React dist 加载、已实现的 C# bridge action、mock/disabled 边界和后端回归测试大体通过。阻塞项仍在 WebView2 UI 层：页面切换截图和连续帧中仍能看到旧页面主体与新页面 header/nav 同帧，Settings 在 clean/dirty/saving 证据帧中出现接近空白内容区，reduced motion 下也没有完全消除该问题。

另外，本机没有可用 npm/pnpm/yarn/corepack，`npm install` 和 `npm run ...` 无法执行；使用现有 `node_modules` 与 Codex bundled Node 只能证明当前工作树可编译，不能证明依赖安装可复现。

## 2. 阻塞问题摘要

1. P1 `FSM-WEBVIEW2-RETEST-001`: 页面切换仍有旧页面主体和新页面 header/nav 混合、空白内容帧，影响 WebView2 UI motion 验收。
2. P1 `FSM-WEBVIEW2-RETEST-002`: 前端依赖安装不可复现，本机无 npm/pnpm/yarn/corepack，必跑的 `npm install` 和 npm scripts 全部失败。
3. P2 `FSM-WEBVIEW2-RETEST-003`: WebView2 smoke evidence JSON 带 UTF-8 BOM，PowerShell 可解析，但 Node 直接 `JSON.parse(fs.readFileSync(path, "utf8"))` 失败。

## 3. 是否仍有 WebView2 UI/bridge 阻塞

有。阻塞集中在 WebView2 React UI motion/截图证据和依赖复现性；已实现 bridge action 本身未发现假成功。

## 4. 是否可以进入下一轮后端 bridge 扩展

可以进入下一轮后端 bridge 扩展，但前提是继续保持未接入功能 disabled，不要把 `monitor.start/stop`、`reports.open`、`reports.regenerate`、`diagnostics.generate`、Overview 打开目录、Targets 直接新增/保存目标伪装成可用。

## 5. 是否可以进入打包

不建议直接进入打包。原因：UI motion 仍有可见问题，且依赖安装不可复现。当前更适合先交给 UI 交互和构建/前端环境修复，再做打包前完整验收。

## 6. 已读取文档和源码

- `docs/orchestration/FrameScopeMonitor-Orchestrator-Role.md`
- `docs/orchestration/FrameScopeMonitor-Handoff-2026-05-14.md`
- `docs/FrameScopeMonitor-Project-Overview.md`
- `docs/modules/software-ui.md`
- `docs/modules/ui-interactions.md`
- `docs/modules/backend-monitoring.md`
- `docs/superpowers/plans/2026-05-20-framescope-webview2-ui-redesign.md`
- `docs/implementation-reports/2026-05-20-framescope-webview2-bridge-report.md`
- `docs/implementation-reports/2026-05-20-framescope-webview2-react-ui-static-implementation-report.md`
- `docs/implementation-reports/2026-05-20-framescope-webview2-react-ui-bridge-interaction-report.md`
- `src/frontend/src/bridge/contract.ts`
- `src/frontend/src/bridge/webviewBridge.ts`
- `src/frontend/src/state/useFrameScopeBridgeState.ts`
- `src/frontend/src/App.tsx`
- `src/frontend/src/layout/AppShell.tsx`
- `src/frontend/src/layout/PageTransition.tsx`
- `src/frontend/src/pages/OverviewPage.tsx`
- `src/frontend/src/pages/TargetsPage.tsx`
- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/pages/SettingsPage.tsx`
- `src/app/FrameScopeWebBridge*.cs`
- `src/app/FrameScopeNativeMonitor.WebHost.cs`

## 7. 命令验证 PASS/FAIL

| Command | Result | Notes |
| --- | --- | --- |
| `npm install` | FAIL | `npm` not found. |
| `npm run typecheck` | FAIL | `npm` not found. |
| `npm test` | FAIL | `npm` not found. |
| `npm run build` | FAIL | `npm` not found. |
| bundled Node `tsc --noEmit` equivalent | PASS | Existing `node_modules` only. |
| bundled Node `vitest run` equivalent | PASS | 2 files, 7 tests passed. |
| bundled Node `vite build` equivalent | PASS | CSS 26.71 kB, JS 319.30 kB. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | Built `dist/FrameScopeMonitor-Setup.exe`. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | Tests rebuilt. |
| `.\tests\FrameScopeUiStateTests.exe` | PASS | `FrameScopeUiStateTests: PASS`. |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS | `FrameScopeReportProgressTests: PASS`. |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS | `FrameScopeWebBridgeTests: PASS`. |
| `node .\tests\chart-sampling-tests.js` | FAIL | WindowsApps `node.exe` Access denied. |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS | `chart-sampling-tests: PASS`. |
| `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo` | PASS | 0 warnings, 0 errors. |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS | Exit 0; only LF/CRLF warnings. |

Environment note: `Get-Command node,npm,pnpm,yarn,corepack` only found WindowsApps `node.exe`; npm/pnpm/yarn/corepack were unavailable.

## 8. 真实 bridge 行为验证结果

- `--web-ui` smoke log shows navigation to React dist under `src/frontend/dist`, not the old embedded smoke HTML.
- `state.snapshot` returned real C# bridge state: `bridgeStatus=ready`, watcher not running, config path present, 9 targets, 6 enabled targets, history file present.
- `config.get` populated Settings and Targets from real config.
- `config.save` path was exercised by changing `PollIntervalMs` from 1000 to 1001; UI observed dirty, saving and success states; smoke restored config afterwards. Final config check showed `PollIntervalMs=1000`.
- `processes.refresh` returned `accepted`, emitted `event.status processes.refreshing`, then `event.processesRefreshed`; observed about 250 process rows and `truncated=true`.

## 9. mock/disabled 边界验证结果

- WebView2 environment displayed `WebView2` / `Bridge ready` / `WebView2 bridge live`, and bridge calls came from C#.
- Browser mock adapter remains centralized in `mockPreview.ts`; React source uses it only when `window.chrome.webview` is absent.
- Overview "打开目录" style action is disabled and labeled next-stage.
- Targets direct add/save target buttons are disabled.
- Reports open/regenerate/diagnostics actions are disabled and labeled next-stage/read-only.
- Topbar search/refresh/notification toolbar buttons are disabled for this phase.
- No evidence found that unimplemented bridge actions fake success in WebView2.

## 10. 动画和 reduced motion 验证结果

Artifacts inspected:

- `artifacts/webview2-react-ui-retest-20260521-fresh3/webview2-react-smoke-transition-targets-01.png`
- `artifacts/webview2-react-ui-retest-20260521-fresh3/webview2-react-smoke-transition-targets-02.png`
- `artifacts/webview2-react-ui-retest-20260521-fresh3/webview2-react-smoke-transition-targets-03.png`
- `artifacts/webview2-react-ui-retest-20260521-fresh3/webview2-react-smoke-transition-settings-01.png`
- `artifacts/webview2-react-ui-retest-20260521-fresh3/webview2-react-smoke-transition-settings-02.png`
- reduced motion equivalents in the same directory.

Findings:

- `overview -> targets`: active nav/header switch to Targets while Overview content remains visible and fades out. No horizontal tear, but active nav and content are not synchronized during transition.
- `targets -> reports` evidence: `webview2-react-smoke-reports.png` shows Reports header/nav active while the main body is still Targets content. This is a visible mixed-page frame.
- `reports/about/settings` evidence also shows old Targets content fading under new header/nav in several frames.
- Settings `clean`, `dirty` and `saving` screenshots in normal motion show an almost blank content area before the final saved screenshot becomes visible.
- Reduced motion still has a blank Settings transition frame; animation is simpler, but not eliminated enough for the acceptance criteria.
- Source correlation: `App.tsx` uses `AnimatePresence` around `PageTransition`; `PageTransition` animates opacity/y/scale, while `AppShell` header/nav update immediately from `activePage`. This matches the observed header/body desynchronization.

## 11. 截图和证据路径

Evidence directory:

- `artifacts/webview2-react-ui-retest-20260521-fresh3`

Key files:

- `winforms-fallback-overview.png`
- `webview2-react-smoke.json`
- `webview2-react-smoke.png`
- `webview2-react-smoke-overview.png`
- `webview2-react-smoke-targets-loading.png`
- `webview2-react-smoke-targets-result.png`
- `webview2-react-smoke-reports.png`
- `webview2-react-smoke-about.png`
- `webview2-react-smoke-settings-clean.png`
- `webview2-react-smoke-settings-dirty.png`
- `webview2-react-smoke-settings-saving.png`
- `webview2-react-smoke-settings-saved.png`
- `webview2-react-reduced-motion-smoke.json`
- `webview2-react-reduced-motion-smoke.png`

## 12. 残留进程检查结果

Final residual check found no test-started residual processes for:

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`
- `msedgewebview2.exe` with `FrameScopeMonitorWebView2` temp user-data path

Existing unrelated WebView2 processes were present for Windows Search (`SearchHost.exe`) and Clash Verge. They were not test residuals and were not killed.

## 13. 需要交给各窗口修复的问题

### `FSM-WEBVIEW2-RETEST-001`

Severity: P1 / blocking for WebView2 UI motion.

Repro steps:

1. Launch `FrameScopeMonitor.exe --web-ui` with React dist available.
2. Switch Overview -> Targets, Targets -> Reports, Reports -> Settings/About.
3. Capture continuous frames or page screenshots during and immediately after navigation.

Actual result:

- New header/nav becomes active while old page body remains visible or fades out.
- Reports/About evidence can show Targets body with Reports/About header.
- Settings dirty/saving screenshots can show a nearly blank content area.
- Reduced motion still shows blank content frame.

Expected result:

- A frame should show either the old complete page or the new complete page.
- Active nav/header should match the visible page body.
- Reduced motion should avoid perceptible blank/mixed-page states.

Likely files:

- `src/frontend/src/App.tsx`
- `src/frontend/src/layout/PageTransition.tsx`
- `src/frontend/src/layout/AppShell.tsx`
- `src/frontend/src/theme/motion.ts`

Recommended owner:

- UI 交互窗口.

### `FSM-WEBVIEW2-RETEST-002`

Severity: P1 / packaging and reproducible build risk.

Repro steps:

1. In `src/frontend`, run `npm install`.
2. Run `npm run typecheck`, `npm test`, `npm run build`.

Actual result:

- All npm commands fail because `npm` is not available.
- System `node` resolves to WindowsApps Codex node and `node .\tests\chart-sampling-tests.js` fails with Access denied.
- Existing `node_modules` plus bundled Node can run equivalent checks, but this does not validate fresh install reproducibility.

Expected result:

- A clean checkout can install dependencies and run frontend scripts with a documented runtime.

Likely files/modules:

- `src/frontend/package.json`
- project setup/build documentation
- packaging/build environment setup

Recommended owner:

- 构建/打包窗口 or frontend infrastructure.

### `FSM-WEBVIEW2-RETEST-003`

Severity: P2 / evidence tooling compatibility risk.

Repro steps:

1. Read `artifacts/webview2-react-ui-retest-20260521-fresh3/webview2-react-smoke.json`.
2. Parse with PowerShell `Get-Content -Raw -Encoding UTF8 | ConvertFrom-Json`.
3. Parse with Node `JSON.parse(fs.readFileSync(path, "utf8"))`.

Actual result:

- File has UTF-8 BOM.
- PowerShell parses successfully.
- Node raw parse fails until BOM is stripped.

Expected result:

- Evidence JSON should be parseable by standard JSON tooling without special BOM stripping.

Likely files/modules:

- `src/app/FrameScopeNativeMonitor.WebHost.cs`
- WebView2 smoke/evidence writer.

Recommended owner:

- 后端 bridge / test tooling window.
