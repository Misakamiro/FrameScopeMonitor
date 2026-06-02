# FrameScope Monitor WebView2 UI Affordance + Motion Final Retest Report

日期：2026-05-23
源码根目录：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 1. 当前复测结论

PASS。

本轮只做复测、截图、连续帧和报告，没有修改实现代码。用户最新指出的 4 个原始问题均未复现；二轮 UI 美观修复和二轮 motion 实现未引入 smoke、交互、键盘、布局或 reduced-motion 回归。

说明：工作树在本轮开始前已经存在前序 WebView2 React UI / motion / smoke 相关未提交改动，这些是本轮复测对象。本轮新增证据目录为：

`artifacts\webview2-ui-affordance-motion-final-retest-20260523`

## 2. 用户 4 个原始问题是否解除

1. Targets 查找进程右上角无效搜索图标：已解除。复测截图显示搜索图标在输入框内部，作为输入语义前缀，不再是独立无效入口。
2. Reports 报告列表“更多”按钮无反应：已解除。点击后打开 `role=menu` 命令浮层，并同步选中当前报告行。
3. Sidebar 缺少边界和点击区域暗示：已解除。正常宽度和 900x760 compact 下都有独立点击区域，active / hover / focus 可区分。
4. UI 动画不够高级、美观、流畅：已解除到当前验收标准。页面切换保持同步 commit，无旧页混绘；菜单开合克制；Targets 搜索只做局部 busy/result 反馈；reduced motion 稳定。

## 3. 视觉问题清单

P0：无。

P1：无。

P2：无新的必须修复项。900x760 下 Targets 搜索区会随页面纵向滚动展示，未发现横向溢出、文字竖排、按钮挤压或同类用户反馈问题。

## 4. 动效问题清单

P0：无。

P1：无。

P2：无。连续帧审计 `frameCount=45`、`failedFrames=[]`；检查项 `noOldPageResidue`、`noMixedNavBody`、`noBlankBody`、`noLowOpacityBody`、`noRouteTransform`、`reducedMotionStatic` 全部为 true。

## 5. 交互 / 键盘问题清单

未发现回归。

- Targets 输入框 Enter 触发真实查找；“查找进程”按钮触发同一查找路径。
- Targets 查找中保留旧结果，按钮和局部状态显示“正在查找”，结果更新只高亮新结果行。
- Reports “更多”菜单包含且只包含：打开文件夹、重新生成报告、生成诊断文件；没有重复“打开报告”。
- Reports 菜单 ArrowDown / ArrowUp / Home / End / Enter / Esc / Tab 通过自动审计；Esc 关闭后焦点返回按钮，Tab 关闭后继续焦点流。
- Sidebar 正常宽度下 Reports active 与 Targets keyboard focus 可区分；900x760 compact 下 Reports active 与 Settings focus 可区分。
- Sidebar 固定滚动验证通过：右侧 viewport `scrollTop` 从 0 到 627，sidebar top 保持 0。

## 6. 截图路径

- Targets 查找进程 1280x720：`artifacts\webview2-ui-affordance-motion-final-retest-20260523\targets-search-area-1280x720.png`
- Targets 查找进程 900x760：`artifacts\webview2-ui-affordance-motion-final-retest-20260523\targets-search-area-900x760.png`
- Reports 更多菜单 1280x720：`artifacts\webview2-ui-affordance-motion-final-retest-20260523\reports-more-menu-open-1280x720.png`
- Reports 更多菜单 900x760：`artifacts\webview2-ui-affordance-motion-final-retest-20260523\reports-more-menu-open-900x760.png`
- Sidebar normal active / hover / focus：`artifacts\webview2-ui-affordance-motion-final-retest-20260523\sidebar-normal-active-hover-focus-1280x720.png`
- Sidebar compact 900x760 active / hover / focus：`artifacts\webview2-ui-affordance-motion-final-retest-20260523\sidebar-compact-active-hover-focus-900x760.png`

## 7. 连续帧路径

页面切换 normal：

- `normal-overview-to-targets-01.png` 到 `normal-overview-to-targets-05.png`
- `normal-targets-to-reports-01.png` 到 `normal-targets-to-reports-05.png`
- `normal-reports-to-settings-01.png` 到 `normal-reports-to-settings-05.png`
- `normal-reports-to-about-01.png` 到 `normal-reports-to-about-05.png`
- `normal-settings-to-overview-01.png` 到 `normal-settings-to-overview-05.png`

快速连续导航：

- `rapid-overview-to-targets-01.png`
- `rapid-targets-to-reports-02.png`
- `rapid-reports-to-settings-03.png`
- `rapid-settings-to-about-04.png`
- `rapid-about-to-overview-05.png`

页面切换 reduced motion：

- `reduced-overview-to-targets-01.png` 到 `reduced-overview-to-targets-05.png`
- `reduced-reports-to-settings-01.png` 到 `reduced-reports-to-settings-05.png`
- `reduced-settings-to-overview-01.png` 到 `reduced-settings-to-overview-05.png`

Reports menu open/close：

- `normal-reports-menu-open-01.png` 到 `normal-reports-menu-open-04.png`
- `normal-reports-menu-close-01.png` 到 `normal-reports-menu-close-04.png`
- `reduced-reports-menu-open-01.png` 到 `reduced-reports-menu-open-04.png`
- `reduced-reports-menu-close-01.png` 到 `reduced-reports-menu-close-04.png`

Targets search busy/result：

- `targets-search-before-enter.png`
- `targets-search-enter-busy.png`
- `targets-search-result-updated.png`
- `targets-search-result-settled.png`

以上文件均位于：`artifacts\webview2-ui-affordance-motion-final-retest-20260523`

## 8. WebView2 live / reduced-motion smoke 结果

Live smoke：PASS，退出码 0。

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=false`
- `webview-ready` 成功收到
- `navigation-completed success=true status=200`
- `console=[]`
- `errors=[]`
- `reports.open=true`
- `reports.openDirectory=true`
- `reports.regenerate accepted/completed=true`
- `targets.get=true`
- `targets refresh observed=true`
- `settings saving/saved observed=true`
- `diagnostics.generate accepted/completed=true`
- `monitor.start accepted/started=true`
- `monitor.stop accepted/stopped=true`

Reduced-motion smoke：PASS，退出码 0。

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=true`
- `webview-ready` 成功收到
- `navigation-completed success=true status=200`
- `console=[]`
- `errors=[]`
- reports / targets / settings / monitor / diagnostics smoke 字段同样通过

证据：

- `artifacts\webview2-ui-affordance-motion-final-retest-20260523\webview2-live-smoke.json`
- `artifacts\webview2-ui-affordance-motion-final-retest-20260523\webview2-reduced-motion-smoke.json`

## 9. 命令验证结果

Frontend verify：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

结果：PASS。

- `npm ci` 成功，安装 110 个包
- `tsc --noEmit` 成功
- Vitest：5 个测试文件，35 个测试全部通过
- Vite production build 成功
- build 产物：`dist/assets/index-Bwr_RKdJ.css`、`dist/assets/index-DrdBxqGR.js`

WebView2 live smoke：

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-ui-affordance-motion-final-retest-20260523\webview2-live-smoke.json --web-ui-screenshot artifacts\webview2-ui-affordance-motion-final-retest-20260523\webview2-live-smoke.png --web-ui-timeout-ms 120000
```

结果：PASS，退出码 0。

WebView2 reduced-motion smoke：

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-ui-affordance-motion-final-retest-20260523\webview2-reduced-motion-smoke.json --web-ui-screenshot artifacts\webview2-ui-affordance-motion-final-retest-20260523\webview2-reduced-motion-smoke.png --web-ui-timeout-ms 120000
```

结果：PASS，退出码 0。

UI / keyboard / sidebar audit：

```powershell
& 'C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' .\artifacts\webview2-ui-affordance-motion-final-retest-20260523\capture-visual-affordance-evidence.cjs
```

结果：PASS。

- `targetsEnter1280=true`
- `targetsEnter900=true`
- `reportsMenu1280=true`
- `reportsMenu900=true`
- `reportsEscClosed=true`
- `reportsTabClosed=true`
- `sidebarScrollFixed=true`
- `internalTerms=[]`

Motion continuous-frame audit：

```powershell
& 'C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' .\artifacts\webview2-ui-affordance-motion-final-retest-20260523\capture-motion-redesign-evidence.cjs
```

结果：PASS。

- `success=true`
- `frameCount=45`
- `failedFrames=0`
- `rapid=true`
- `menuNormal=true`
- `menuReduced=true`
- `targetsSearch=true`
- `sidebar=true`
- `reducedMotion=true`

Git whitespace：

```powershell
& 'C:\Program Files\Git\cmd\git.exe' -c safe.directory='C:/Users/misakamiro/Documents/Codex/2026-05-02/files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d' diff --check
```

结果：PASS。输出只有 LF/CRLF 提示，没有 whitespace error。

## 10. 残留进程检查

检查范围：

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `PresentMon.exe`
- 本轮证据脚本相关 `node.exe`
- 本轮 CDP / smoke 相关 `msedge.exe`、`msedgewebview2.exe`
- 本轮端口：4219、4297、9389、9397

结果：

- `NO_MATCHING_RESIDUAL_PROCESSES`
- `NO_MATCHING_LISTEN_PORTS`

## 11. 是否建议进入最终打包验证

建议进入最终打包验证。

理由：用户 4 个原始 UI 问题已解除；二轮视觉修复和 motion 修复的截图、键盘、连续帧、live smoke、reduced-motion smoke、frontend verify、`git diff --check`、残留进程检查均通过。本轮未发现 P0/P1/P2 阻断项。
