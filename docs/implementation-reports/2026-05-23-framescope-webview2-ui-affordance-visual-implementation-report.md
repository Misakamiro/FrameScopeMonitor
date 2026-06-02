# FrameScope Monitor WebView2 UI Affordance Visual Implementation Report

日期：2026-05-23
源码根目录：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 当前结论

PASS。

本轮按视觉重设计方案完成了 3 个局部：Targets 查找进程区域、Reports “更多”菜单、Sidebar 导航栏。修改保持在 WebView2 React 前端 UI 范围内，没有修改 C# bridge 业务语义、后端采样、报告生成、diagnostics 语义、GameLite / WMI / SGuard、`build.ps1`、packaging、README 或 GitHub Release。

说明：当前工作树在本轮开始前已有前序 WebView2 / motion / smoke 相关未提交改动，包括 `src/app/FrameScopeNativeMonitor.WebHost.cs`。本轮视觉实现没有改该 C# 文件。

## 修改文件清单

本轮 UI 和测试相关文件：

- `src/frontend/src/components/Button.tsx`
- `src/frontend/src/components/ChartShell.tsx`
- `src/frontend/src/components/InlineStatus.tsx`
- `src/frontend/src/components/ToolbarButton.tsx`
- `src/frontend/src/components/components.css`
- `src/frontend/src/layout/AppShell.tsx`
- `src/frontend/src/layout/PageTransition.tsx`
- `src/frontend/src/layout/SidebarNav.tsx`
- `src/frontend/src/layout/TopStatusBar.tsx`
- `src/frontend/src/layout/layout.css`
- `src/frontend/src/pages/AboutPage.tsx`
- `src/frontend/src/pages/OverviewPage.tsx`
- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/pages/SettingsPage.tsx`
- `src/frontend/src/pages/TargetsPage.tsx`
- `src/frontend/src/pages/pages.css`
- `src/frontend/src/styles/global.css`
- `src/frontend/src/theme/motion.ts`
- `src/frontend/src/theme/tokens.css`
- `src/frontend/src/uiDesignContract.test.ts`
- `src/frontend/src/uiMotionContract.test.ts`

本轮新增报告和证据：

- `docs/implementation-reports/2026-05-23-framescope-webview2-ui-affordance-visual-implementation-report.md`
- `artifacts/webview2-ui-affordance-visual-20260523/capture-visual-affordance-evidence.cjs`
- `artifacts/webview2-ui-affordance-visual-20260523/visual-affordance-audit.json`
- `artifacts/webview2-ui-affordance-visual-20260523/*.png`
- `artifacts/webview2-ui-affordance-visual-20260523/webview2-*.json`

## Targets 查找进程区域如何重做

- 将查找进程区域改成紧凑搜索工具条 + 结果列表。
- 搜索图标移入输入框左侧，作为输入语义前缀，不再形成独立假入口。
- `查找进程` 按钮改成固定宽度 compact secondary / tonal button；1280x720 下按钮宽 116px，不横跨整行。
- 输入框 Enter 与按钮点击共用 `bridgeState.refreshProcesses(query)`，仍触发真实查找。
- 查找状态改为标题右侧短状态 pill 和搜索行下方短帮助文案，不再用大块状态卡片。
- 结果列表改为数据列表：进程名、PID、窗口标题分列；selected 行有左侧 rail + 淡底色，hover / focus 有轻反馈。
- 900x760 下搜索区自动两行布局，按钮保持 116px；审计结果显示无横向溢出、无竖排风险。

证据：

- `visual-affordance-audit.json`: `targets1280.enterTriggered=true`
- `visual-affordance-audit.json`: `targets900.enterTriggered=true`
- `visual-affordance-audit.json`: `targets900.horizontalOverflow=false`
- `visual-affordance-audit.json`: `targets900.verticalTextRisk=false`

## Reports 更多菜单如何重做

- “更多”按钮改成 34x34 的 compact ghost icon command，文案只保留在 screen reader 文本里。
- 点击“更多”会同步选中该报告行，并打开与当前行绑定的菜单。
- 菜单只包含真实已有操作：
  - `打开文件夹`
  - `重新生成报告`
  - `生成诊断文件`
- 菜单没有重复 `打开报告`；`打开报告` 保持为行内主操作。
- 菜单使用 204px 宽度、8px 级圆角、轻边框、克制阴影、38px item 高度，更接近 MenuFlyout。
- 菜单支持 hover、focus、ArrowUp、ArrowDown、Home、End、Esc、Tab。
- Esc 关闭后焦点返回“更多”按钮；Tab 关闭菜单并继续流向下一可聚焦元素，不锁焦点。

证据：

- `visual-affordance-audit.json`: `reportsMenu1280.menuOpen=true`
- `visual-affordance-audit.json`: `reportsMenu1280.anchoredToRow=true`
- `visual-affordance-audit.json`: `reportsMenu900.menuOpen=true`
- `visual-affordance-audit.json`: `reportsMenu900.anchoredToRow=true`
- `visual-affordance-audit.json`: `reportsMenu1280.itemTexts=["打开文件夹","重新生成报告","生成诊断文件"]`
- `visual-affordance-audit.json`: `reportsMenu1280.duplicateOpenReportInMenu=false`

## Sidebar 导航如何重做

- Sidebar 不再呈现“浅色按钮墙”倾向，nav item 默认透明、边框透明，只有 hover / active / focus 才提高反馈。
- 正常宽度下 nav item 整行可点击，图标、标题、说明按 22px 图标列 + 文本列对齐。
- active 状态使用左侧 3px rail + 淡蓝底色 + 弱边框，不只靠蓝色边框。
- hover 仅使用浅底色和文字色变化，不做重阴影。
- focus-visible 使用清晰 outline，且不覆盖 active rail。
- 900x760 下 sidebar 折叠为 84px，每个导航图标保持 48x48 独立点击容器；active、hover、focus 仍可区分。
- `.sidebar` 仍为固定左侧栏，右侧 `.page-viewport` 独立滚动。

证据：

- `visual-affordance-audit.json`: `sidebarNormal.focusedNav="targets"`
- `visual-affordance-audit.json`: `sidebarNormal.activeNav="reports"`
- `visual-affordance-audit.json`: `sidebarCompact.focusedNav="settings"`
- `visual-affordance-audit.json`: `sidebarCompact.activeNav="reports"`
- `visual-affordance-audit.json`: `sidebarCompact.sidebarWidth=84`
- `visual-affordance-audit.json`: `sidebarScroll.fixed=true`

## 全局控件细节如何统一

- `button / icon button / input / menu / nav item / list row / status pill / panel` 统一使用 design token 中的 radius、border、shadow、hover、focus 规则。
- 新增并使用 `tonal` button 作为局部工具条强调，不抢页面主按钮层级。
- 菜单、列表行、导航、输入框都使用同一套 focus ring 和低强度边框。
- 减少重边框、重阴影、过大圆角和卡片套卡片；状态反馈靠原位 inline 状态和短 pill。
- 中文按钮保持固定高度和合理行高，不使用 viewport 缩放字体。
- 清理用户界面内部词：About 高级信息不再显示 `requestId`、`bridge/contract.ts`、`WebView2 宿主`；图表标签从 `SVG mock` 改为 `趋势预览`；Reports 详情不再显示 `内部编号`。
- 审计结果显示可见 UI 未匹配 `smoke`、`bridge`、`requestId`、`测试员`、`窗口分工`、`SVG mock`、`宿主应用`、`WebView2`。

证据：

- `visual-affordance-audit.json`: `internalTerms.matches=[]`

## 截图路径

必需截图：

- Targets 查找进程区域 1280x720：`artifacts/webview2-ui-affordance-visual-20260523/targets-search-area-1280x720.png`
- Targets 查找进程区域 900x760：`artifacts/webview2-ui-affordance-visual-20260523/targets-search-area-900x760.png`
- Reports 更多菜单打开 1280x720：`artifacts/webview2-ui-affordance-visual-20260523/reports-more-menu-open-1280x720.png`
- Reports 更多菜单打开 900x760：`artifacts/webview2-ui-affordance-visual-20260523/reports-more-menu-open-900x760.png`
- Sidebar normal active / hover / focus：`artifacts/webview2-ui-affordance-visual-20260523/sidebar-normal-active-hover-focus-1280x720.png`
- Sidebar compact 900x760 active / hover / focus：`artifacts/webview2-ui-affordance-visual-20260523/sidebar-compact-active-hover-focus-900x760.png`

WebView2 smoke 截图：

- Live：`artifacts/webview2-ui-affordance-visual-20260523/webview2-live-smoke.png`
- Reduced motion：`artifacts/webview2-ui-affordance-visual-20260523/webview2-reduced-motion-smoke.png`

截图尺寸检查：

- 1280x720 CDP 截图在 125% scale 下为 1600x900。
- 900x760 CDP 截图在 125% scale 下为 1125x950。
- WebView2 smoke 截图为 1164x721。
- 上述必需截图文件均存在，大小非 0。

## 键盘验证结果

自动化证据：`artifacts/webview2-ui-affordance-visual-20260523/visual-affordance-audit.json`

- 搜索输入 Enter：PASS。1280x720 和 900x760 下输入 `VALORANT` 后按 Enter，均触发进程刷新，`enterTriggered=true`。
- Reports 更多菜单方向键：PASS。初始焦点为 `打开文件夹`，ArrowDown 移到 `重新生成报告`，ArrowUp 回到 `打开文件夹`，End 到 `生成诊断文件`，Home 回到 `打开文件夹`。
- Reports 更多菜单 Esc：PASS。`menuOpen=false`，焦点返回更多按钮，`focusReturned=true`。
- Reports 更多菜单 Tab：PASS。`menuOpen=false`，焦点进入下一报告行按钮，未卡住焦点。
- Sidebar Tab / focus-visible：PASS。正常宽度和 compact 宽度下 focused nav 均有 `outline=solid`，active nav 的 rail 保持可见。

## WebView2 live / reduced-motion smoke 结果

Live smoke 命令：

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-ui-affordance-visual-20260523\webview2-live-smoke.json --web-ui-screenshot artifacts\webview2-ui-affordance-visual-20260523\webview2-live-smoke.png --web-ui-timeout-ms 120000
```

结果：PASS，退出码 0。

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=false`
- `console=[]`
- `errors=[]`
- `smokePayload.success=true`
- `reportLiveActionSmoke.success=true`
- `bridgeExtensionSmoke.success=true`

Reduced-motion smoke 命令：

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-ui-affordance-visual-20260523\webview2-reduced-motion-smoke.json --web-ui-screenshot artifacts\webview2-ui-affordance-visual-20260523\webview2-reduced-motion-smoke.png --web-ui-timeout-ms 120000
```

结果：PASS，退出码 0。

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=true`
- `console=[]`
- `errors=[]`
- `smokePayload.success=true`
- `reportLiveActionSmoke.success=true`
- `bridgeExtensionSmoke.success=true`

## 命令验证结果

Frontend test：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 test
```

结果：PASS。

- Vitest：5 个测试文件，32 个测试全部通过。

Frontend verify：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

结果：PASS。

- `npm ci` 成功，安装 110 个包。
- `tsc --noEmit` 成功。
- Vitest：5 个测试文件，32 个测试全部通过。
- Vite production build 成功。
- 当前 build 产物：
  - `dist/assets/index-DIfuKixP.css`
  - `dist/assets/index-6GDnFJKH.js`

视觉 / 键盘 / 滚动审计：

```powershell
& 'C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' .\artifacts\webview2-ui-affordance-visual-20260523\capture-visual-affordance-evidence.cjs
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

`git diff --check`：

```powershell
& 'C:\Program Files\Git\cmd\git.exe' -c safe.directory='C:/Users/misakamiro/Documents/Codex/2026-05-02/files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d' diff --check
```

结果：PASS，退出码 0。输出只有 Git 的 LF/CRLF 提示，没有 whitespace error。

## 残留进程检查

检查项：

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `PresentMon`
- 本轮证据脚本相关 `node` / `msedge` / `msedgewebview2`
- 本轮临时端口 / profile：`webview2-ui-affordance-visual-20260523`、`capture-visual-affordance-evidence`、`--remote-debugging-port=9389`、`127.0.0.1:4219`

最终结果：

- `NO_MATCHING_RESIDUAL_PROCESSES`

说明：一次中间检查误匹配到正在执行残留检查的 PowerShell 自身，已排除当前检查进程后重跑，最终无匹配残留。

## 是否建议交给动画实现窗口继续

不建议因为本轮 3 个局部继续交给动画实现窗口。

理由：本轮目标是视觉层级、可点击性和局部可用性重做，不是新的动画任务。当前 Targets、Reports 菜单、Sidebar 已通过合同测试、1280x720 / 900x760 截图、键盘验证、固定滚动验证、WebView2 live smoke、WebView2 reduced-motion smoke、`git diff --check` 和残留进程检查。

如果后续仍要交给动画实现窗口，只建议做非常局部的菜单开合、row selected settle、focus/hover token 微调；不要恢复旧页 exit，不要做 page crossfade / scale / blur / bounce，也不要改 C# bridge 或后端语义。
