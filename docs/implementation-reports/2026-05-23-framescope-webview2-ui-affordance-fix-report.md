# FrameScope Monitor WebView2 UI Affordance Fix Report

日期：2026-05-23
源码根目录：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 当前结论

PASS。

本轮只修复用户指出的 3 个 UI 可用性问题：Targets 查找进程搜索图标、Reports “更多”按钮、左侧导航栏可点击性。未修改 C# bridge 业务语义、后端采样、报告生成、diagnostics、GameLite、WMI、SGuard、`build.ps1`、packaging、README 或 GitHub Release。

说明：当前工作树里仍有前序 WebView2 redesign / motion / smoke diagnosis 的未提交改动，包括 `src/app/FrameScopeNativeMonitor.WebHost.cs`。本轮没有改该 C# 文件，也没有因为本轮同步 WebView2 smoke 选择器。

## 修改文件清单

本轮实际修改的应用 / 测试文件：

- `src/frontend/src/pages/TargetsPage.tsx`
- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/layout/layout.css`
- `src/frontend/src/pages/pages.css`
- `src/frontend/src/uiDesignContract.test.ts`

本轮新增报告和证据：

- `docs/implementation-reports/2026-05-23-framescope-webview2-ui-affordance-fix-report.md`
- `artifacts/webview2-ui-affordance-fix-20260523/capture-affordance-evidence.cjs`
- `artifacts/webview2-ui-affordance-fix-20260523/affordance-audit.json`
- `artifacts/webview2-ui-affordance-fix-20260523/*.png`
- `artifacts/webview2-ui-affordance-fix-20260523/webview2-*.json`

## 查找进程图标如何处理

- 移除了“查找进程”标题右侧纯装饰、无点击行为的独立 `Search` 图标。
- 将搜索图标放进真实可操作的“查找进程”按钮：按钮点击仍调用 `bridgeState.refreshProcesses(query)`。
- 新增输入框 Enter 键触发同一个查找动作：`handleProcessSearchKeyDown` 调用同一条 `refreshProcessSearch()`。
- 证据：`affordance-audit.json` 中 `titleLooseSearchSvgCount=0`、`buttonSvgCount=1`、`targetInput=VALORANT`、`targetEnterCompleted=true`。

## Reports “更多”按钮如何处理

- “更多”不再只做选中行；现在点击后会打开 `role="menu"` 的小型菜单。
- 菜单包含 3 个真实已有能力：
  - 打开文件夹：`bridgeState.openReportDirectory(report.reportId)`
  - 重新生成报告：`bridgeState.regenerateReport(report.reportId)`
  - 生成诊断文件：`bridgeState.generateDiagnostics(report.reportId)`
- 未新增“复制报告信息”，因为当前前端状态层没有对应真实能力，本轮不加假功能。
- 菜单为绝对定位，不改变列表行高度，不导致布局跳动。
- 键盘行为：菜单打开后首个可用菜单项获得焦点；ArrowUp / ArrowDown 可移动；Esc 关闭并把焦点还给“更多”；Tab 关闭菜单并继续正常焦点流。

## Sidebar 可点击性如何加强

- 每个 nav item 保持整行 `<button>` 可点击，增加稳定容器底色、轻边框、圆角和 `cursor: pointer`。
- 新增 hover 状态：浅色背景、边框增强、文字变为主色。
- 新增 active 状态：白色实体底、蓝色左侧 active indicator、蓝色弱边框和轻阴影；active 比 hover 更明确。
- 新增 focus-visible 状态：蓝色边框、白色底、3px focus ring。
- 900x760 折叠侧栏下，每个图标项为独立 59x48 左侧按钮容器，文字隐藏但 `title` / `aria-label` 保留，active 和 focus-visible 仍可见。

## 截图路径

- Targets 查找进程区域：`artifacts/webview2-ui-affordance-fix-20260523/targets-search-area-1280x720.png`
- Reports 报告列表，包含“更多”菜单打开状态：`artifacts/webview2-ui-affordance-fix-20260523/reports-more-menu-open-1280x720.png`
- Sidebar 正常宽度 active / focus 视觉：`artifacts/webview2-ui-affordance-fix-20260523/sidebar-normal-active-focus-1280x720.png`
- Sidebar 900x760 折叠状态：`artifacts/webview2-ui-affordance-fix-20260523/sidebar-compact-900x760.png`
- WebView2 live smoke 截图：`artifacts/webview2-ui-affordance-fix-20260523/webview2-live-smoke.png`
- WebView2 reduced-motion smoke 截图：`artifacts/webview2-ui-affordance-fix-20260523/webview2-reduced-motion-smoke.png`

## 键盘验证结果

自动化证据：`artifacts/webview2-ui-affordance-fix-20260523/affordance-audit.json`

- 搜索输入 Enter：PASS。输入 `VALORANT` 后按 Enter，`Process refresh completed` 出现。
- Reports 更多菜单打开：PASS。菜单打开后有 `打开文件夹`、`重新生成报告`、`生成诊断文件`。
- Reports 更多菜单 Esc：PASS。Esc 后 `menuOpen=false`，焦点回到“更多”按钮。
- Reports 更多菜单 Tab：PASS。Tab 后 `menuOpen=false`，焦点继续流向下一项，未卡住焦点。
- Reports 菜单方向键：PASS。ArrowDown 从 `打开文件夹` 移到 `重新生成报告`。
- Sidebar Tab / focus-visible：PASS。正常宽度下 focused nav outline 为 `solid`、宽度 `3px`；900x760 下 active/focused 图标项仍有独立容器和 focus outline。

内置浏览器复核：

- Targets Enter 查找：`Process refresh completed`
- Reports menu item Esc：关闭菜单并聚焦“更多”
- Sidebar focus-visible：`outline=solid`、`outlineWidth=3px`

## WebView2 Smoke 结果

Live smoke 命令：

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-ui-affordance-fix-20260523\webview2-live-smoke.json --web-ui-screenshot artifacts\webview2-ui-affordance-fix-20260523\webview2-live-smoke.png --web-ui-timeout-ms 120000
```

结果：PASS，退出码 0。

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=false`
- `smokePayload.success=true`
- `reportLiveActionSmoke.success=true`
- `reportOpenClickOk=true`
- `reportOpenDirectoryClickOk=true`
- `reportRegenerateClickAccepted=true`
- `reportRegenerateClickCompleted=true`
- `bridgeExtensionSmoke.success=true`
- `targetsGetOk=true`
- `diagnosticsCompleted=true`
- `monitorStarted=true`
- `monitorStopped=true`

Reduced-motion smoke 命令：

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-ui-affordance-fix-20260523\webview2-reduced-motion-smoke.json --web-ui-screenshot artifacts\webview2-ui-affordance-fix-20260523\webview2-reduced-motion-smoke.png --web-ui-timeout-ms 120000
```

结果：PASS，退出码 0。

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=true`
- `smokePayload.success=true`
- `reportLiveActionSmoke.success=true`
- `bridgeExtensionSmoke.success=true`
- `targetsGetOk=true`
- `diagnosticsCompleted=true`
- `monitorStarted=true`
- `monitorStopped=true`

## 命令验证结果

### Frontend verify

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

结果：PASS。

- `npm ci` 成功，安装 110 个包。
- `tsc --noEmit` 成功。
- Vitest：5 个测试文件、28 个测试全部通过。
- Vite build 成功。
- 当前 build 产物：
  - `dist/assets/index-B8_Pj3ML.css`
  - `dist/assets/index-DA_-vf92.js`

### TDD 过程

先新增 3 个失败断言到 `src/frontend/src/uiDesignContract.test.ts`：

- 查找进程不能保留标题右侧纯装饰 `Search` 图标，必须有 Enter 查找。
- Reports “更多”必须是可访问 menu，并包含真实已有 action。
- Sidebar 必须有 hover / active / focus-visible / 900x760 独立容器。

红灯结果：`Run-Frontend.ps1 test` 失败 3 项，正好对应本轮 3 个问题。

实现后复测：`Run-Frontend.ps1 test` PASS，5 个测试文件、28 个测试全部通过。

### Screenshot / keyboard audit

```powershell
& 'C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' .\artifacts\webview2-ui-affordance-fix-20260523\capture-affordance-evidence.cjs
```

结果：PASS。

- `targetsEnterCompleted=true`
- `reportsMenuOpen=true`
- `reportsEscClosed=true`
- `reportsTabClosed=true`
- `sidebarFocus=solid`
- `compactSidebarWidth=84`

### git diff check

```powershell
& 'C:\Program Files\Git\cmd\git.exe' diff --check
```

结果：PASS，退出码 0。输出只有 Git 的 LF/CRLF 提示，没有 whitespace error。

## 残留进程检查

检查项：

- `FrameScopeMonitor`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `PresentMon`
- 本轮证据脚本相关 `node` / `msedge` / `msedgewebview2`
- 本轮端口 `4197` / `4198` / `9367`

结果：

- FrameScope / sampler / report generator / PresentMon：无残留。
- `artifacts\webview2-ui-affordance-fix-20260523`、`--remote-debugging-port=9367`、`127.0.0.1:4197` 相关进程：无残留。
- `4197` / `4198` / `9367`：无 LISTENING 残留。检查时曾看到 `9367 TimeWait` 和 `4198 FinWait2`，属于连接关闭后的 TCP 状态，不是仍在监听的进程。

## 是否建议继续交给动画设计窗口

不建议把本轮 3 个问题继续交给动画设计窗口。

本轮问题是点击入口和可访问性交互问题，已经在前端实现、契约测试、截图审计、键盘验证和 WebView2 smoke 中通过。后续如果继续做动画窗口，应只处理动画细节，不要重新打开这 3 个 affordance 问题，也不要改 C# bridge、后端采样、报告生成或 diagnostics 语义。
