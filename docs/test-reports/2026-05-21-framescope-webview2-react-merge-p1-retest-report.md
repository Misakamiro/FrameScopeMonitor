# FrameScope Monitor WebView2 React Merge P1 Retest Report

Date: 2026-05-21
Role: WebView2 React UI merge retest tester
Scope: read-only retest, except writing this report

## 1. 当前复测结论

通过。

两个 P1 候选修复均通过本轮复测：

- UI motion P1: 已解除。连续帧中未再发现新 header/nav 搭配旧页面主体、Targets 主体残留到 Reports/About、Settings 空白内容区、整页 spinner 等待。
- 前端依赖复现 P1: 已解除。`node_modules` 被临时移走后，`tools\Run-Frontend.ps1 install` 能重新执行 `npm ci` 并完成后续 typecheck/test/build/verify，不依赖系统 npm/pnpm/yarn/corepack。

非阻塞观察：部分 transition 的第 01 帧会看到目标页面本身处于低透明度绘制状态，但页面主体已经是目标页面，不是旧页面残影，也不是空白骨架。

## 2. 两个 P1 是否解除

| P1 | Result | Evidence |
| --- | --- | --- |
| UI motion 页面切换混绘/空白帧 | RESOLVED | WebView2 normal/reduced smoke 截图和连续帧均未发现旧主体混入新页面。 |
| 前端依赖可复现安装 | RESOLVED | 临时移走 `src\frontend\node_modules` 后，`Run-Frontend.ps1 install` PASS。 |

## 3. 是否仍有 WebView2 UI/bridge 阻塞

未发现阻塞。

已实现 bridge surface 仍真实接入：

- `state.snapshot`
- `config.get`
- `config.save`
- `processes.refresh`

未实现的 action 仍保持 disabled 或 next-stage 标识，没有发现假成功。

## 4. 是否可以进入后端 bridge 扩展

可以。建议下一轮扩展后端 bridge adapter，但继续保持 host-side authority 和 disabled 边界，直到对应 C# adapter 真正实现。

## 5. 是否可以进入打包

可以进入打包前准备或打包候选验证。建议打包窗口仍重新跑一次安装环境、WinForms fallback、WebView2 smoke、残留进程检查，因为本轮只负责合并复测，不负责发布包签出或 installer 交付。

## 6. 命令验证 PASS/FAIL

完整日志：

- `artifacts/webview2-react-merge-retest-20260521/command-results.json`

| Command | Result | Notes |
| --- | --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 install` | PASS | `node_modules` 已先移到 `%TEMP%\framescope-merge-retest-node_modules-backups\...`；重新安装成功。 |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 typecheck` | PASS | `tsc --noEmit` 通过。 |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 test` | PASS | Vitest 2 files / 7 tests passed。 |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 build` | PASS | Vite build 通过。 |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | install + typecheck + test + build 全链通过。 |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | 生成 `dist\FrameScopeMonitor-Setup.exe`。 |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | 测试重新构建通过。 |
| `.\tests\FrameScopeUiStateTests.exe` | PASS | `FrameScopeUiStateTests: PASS`。 |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS | `FrameScopeReportProgressTests: PASS`。 |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | `FrameScopeReportManifestTests: PASS`。 |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS | `FrameScopeWebBridgeTests: PASS`。 |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS | `chart-sampling-tests: PASS`。 |
| `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo` | PASS | 0 warnings / 0 errors。 |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS | Exit 0；仅已有 LF/CRLF warning。 |

## 7. motion 验证结果和证据路径

Evidence directory:

- `artifacts/webview2-react-merge-retest-20260521`

Normal motion:

- `webview2-react-smoke-overview.png`
- `webview2-react-smoke-targets-loading.png`
- `webview2-react-smoke-targets-result.png`
- `webview2-react-smoke-reports.png`
- `webview2-react-smoke-settings-clean.png`
- `webview2-react-smoke-settings-dirty.png`
- `webview2-react-smoke-settings-saving.png`
- `webview2-react-smoke-settings-saved.png`
- `webview2-react-smoke-about.png`
- `webview2-react-smoke-transition-overview-targets-01.png`
- `webview2-react-smoke-transition-overview-targets-02.png`
- `webview2-react-smoke-transition-overview-targets-03.png`
- `webview2-react-smoke-transition-targets-reports-01.png`
- `webview2-react-smoke-transition-targets-reports-02.png`
- `webview2-react-smoke-transition-targets-reports-03.png`
- `webview2-react-smoke-transition-reports-settings-01.png`
- `webview2-react-smoke-transition-reports-settings-02.png`
- `webview2-react-smoke-transition-reports-settings-03.png`
- `webview2-react-smoke-transition-targets-about-01.png`
- `webview2-react-smoke-transition-targets-about-02.png`
- `webview2-react-smoke-transition-targets-about-03.png`

Reduced motion:

- `webview2-react-reduced-motion-smoke.json`
- `webview2-react-reduced-motion-smoke.png`
- `webview2-react-reduced-motion-smoke-transition-overview-targets-*.png`
- `webview2-react-reduced-motion-smoke-transition-targets-reports-*.png`
- `webview2-react-reduced-motion-smoke-transition-reports-settings-*.png`
- `webview2-react-reduced-motion-smoke-transition-targets-about-*.png`

Findings:

- `overview -> targets`: no old Overview body remains after Targets header/nav activates.
- `targets -> reports`: Reports page body is Reports read-only content, not Targets table/list.
- `reports -> settings`: Settings page body is Settings form, not Reports body or blank content.
- `targets -> about`: About page body is About content, not Targets body.
- Settings clean/dirty/saving/saved: all four screenshots show complete Settings form and action state.
- Reduced motion: no old page body mixed into new page; no blank card skeleton or whole-page spinner.

## 8. 前端依赖复现验证结果

Clean-install proof:

- Existing `src\frontend\node_modules` was moved to:
  - `%TEMP%\framescope-merge-retest-node_modules-backups\node_modules-20260521-111517`
- `tools\Run-Frontend.ps1 install` then performed a fresh install and passed.
- Subsequent `typecheck`, `test`, `build`, and combined `verify` all passed.

This proves the previous P1 is fixed for this environment: the frontend workflow no longer depends on system npm/pnpm/yarn/corepack and does not rely on a pre-existing `node_modules`.

## 9. bridge/mock/disabled 边界验证结果

Smoke JSON:

- `webview2-react-smoke.json`
- `webview2-react-reduced-motion-smoke.json`

Observed:

- `success=True`
- `pageLoaded=True`
- `pageReady=True`
- `frontendPath=...\src\frontend\dist`
- `reactOverviewLoaded=True`
- `reactTargetsLoaded=True`
- `reactReportsLoaded=True`
- `reactSettingsLoaded=True`
- `reactAboutLoaded=True`
- `processRefreshObserved=True`
- `configDirtyObserved=True`
- `configSavingObserved=True`
- `configSaveSuccessObserved=True`

Source/screenshot boundary check:

- WebView2 badge and `Bridge ready` visible.
- Overview open-folder action remains disabled / next-stage.
- Targets add/save target actions remain disabled.
- Reports open/regenerate/diagnostics actions remain disabled.
- About page explicitly lists disabled boundaries for monitor and reports/diagnostics.
- Browser mock adapter remains separate from WebView2 live bridge path.

Config restore:

- After smoke, `framescope-config.json` is back to `PollIntervalMs=1000`, `LogRetentionDays=14`, `MaxLogDiskMb=100`, `Targets=9`.

Evidence JSON note:

- Smoke evidence JSON still has UTF-8 BOM. PowerShell parses it. Node parse succeeds with BOM stripping. This remains a non-blocking tooling note from the previous report, not one of the two P1 blockers retested here.

## 10. 残留进程检查结果

Final check found no test-started residual processes for:

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`
- `msedgewebview2.exe` with FrameScopeMonitor WebView2 user-data path

Pre-existing processes still present and not killed:

- `node.exe` PID 22388 running Vite on `127.0.0.1:5174`, parent PID 10732.
- `esbuild.exe` PID 20024, child of PID 22388.
- `node.exe` PID 8308 running npm dev command, parent PID 25076.
- unrelated `msedgewebview2.exe` groups for Windows Search, Clash Verge, and GameViewer.

These processes existed outside this retest scope or were unrelated to FrameScope smoke runs, so they were only recorded.

## 11. 需要交给各窗口的问题

No P1 blockers remain from this merge retest.

Follow-up items:

- UI 交互: optional polish only. First transition frame can show target page at low opacity; it is not a mixed-page blocker, but the UI owner can decide whether pageCommit should become fully instant.
- 前端构建: no blocking issue. Keep `tools\Run-Frontend.ps1` as the documented entry point for install/typecheck/test/build/verify.
- 后端 bridge: can proceed to real adapters for monitor/report/diagnostics actions while preserving disabled UI until each adapter exists.
- 打包窗口: can begin final packaging validation, but should rerun the same frontend verify, WebView2 smoke, WinForms fallback, and residual process checks against the packaged payload.
