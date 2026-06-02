# FrameScope WebView2 UI Interaction Polish Fix Report

Date: 2026-05-24
Source root: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Verdict

PASS.

本轮只处理 React 前端 UI、交互和动效问题。没有打包，没有推 GitHub，没有修改 C# bridge、后端采样、报告生成、GameLite、WMI、SGuard、README 或 Release。

前端验证、WebView2 live smoke、WebView2 reduced-motion smoke、截图证据、无横向溢出检查、`git diff --check` 和残留进程检查均已完成。建议测试员复测，因为本轮修的是用户截图指出的可视/交互问题，最终体验仍应由真实 WebView2 窗口再人工确认一遍。

## Modified Files

本轮涉及的前端文件和测试合同：

- `src/frontend/src/layout/SidebarNav.tsx`
- `src/frontend/src/layout/layout.css`
- `src/frontend/src/pages/TargetsPage.tsx`
- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/pages/OverviewPage.tsx`
- `src/frontend/src/pages/pages.css`
- `src/frontend/src/uiDesignContract.test.ts`
- `src/frontend/src/uiInteractionContract.test.ts`
- `src/frontend/src/uiMotionContract.test.ts`

本轮输出证据和报告：

- `artifacts/webview2-ui-interaction-polish-20260524/capture-ui-interaction-polish-evidence.cjs`
- `artifacts/webview2-ui-interaction-polish-20260524/ui-interaction-polish-evidence.json`
- `artifacts/webview2-ui-interaction-polish-20260524/*.png`
- `artifacts/webview2-ui-interaction-polish-20260524/webview2-live-smoke.json`
- `artifacts/webview2-ui-interaction-polish-20260524/webview2-reduced-motion-smoke.json`
- `docs/implementation-reports/2026-05-24-framescope-webview2-ui-interaction-polish-fix-report.md`

说明：工作树里仍有前序 UI/WebView2 相关未提交改动，包括 `src/app/FrameScopeNativeMonitor.WebHost.cs`。本轮没有改它，也没有把问题转移到后端或 bridge 语义上。

## Issue Fixes

### 1. Sidebar Active 蓝条位置错误

Root cause:

旧实现把 active 视觉状态和导航按钮的可点击容器耦合得不够稳定，compact 宽度下容易出现 rail、选中块和按钮容器视觉不在同一参考系的问题。active、hover、focus 也容易叠成一层重反馈。

Fix:

- 在 `SidebarNav.tsx` 中保留 item 内部的 `nav-item__rail`，不使用外置 active indicator。
- 在 `layout.css` 中让 rail 绝对定位在当前 `.nav-item` 内部，宽度 3px，使用 top/bottom inset 垂直居中。
- active、hover、focus-visible 分开处理，active-focus 只补键盘边界，不额外偏移 rail。

Evidence:

- `artifacts/webview2-ui-interaction-polish-20260524/sidebar-1280x720.png`
- `artifacts/webview2-ui-interaction-polish-20260524/sidebar-900x760.png`
- `ui-interaction-polish-evidence.json`: `railInsideItem=true`, `railCentered=true`, `hasOuterIndicator=false`

Verification:

1280 和 900 compact 截图均确认 rail 在当前 nav item 内部。JSON 检查显示 active item、rail、icon 均垂直居中，横向溢出为 false。

### 2. Sidebar 选中框、Icon、文字视觉居中

Root cause:

compact 模式下导航项从两列变成紧凑单列后，icon、label、active 背景和 focus ring 没有用稳定尺寸约束，导致选中状态看起来偏、脏、重。

Fix:

- `layout.css` 为 desktop 和 compact 分别固定 nav item 高度、宽度、grid 布局和 label 尺寸。
- hover 只做浅背景，active 只做当前页语义，focus-visible 只表达键盘位置。
- compact 模式继续显示短标签，不退化成纯图标按钮。

Evidence:

- `artifacts/webview2-ui-interaction-polish-20260524/sidebar-1280x720.png`
- `artifacts/webview2-ui-interaction-polish-20260524/sidebar-900x760.png`

Verification:

`ui-interaction-polish-evidence.json` 中 1280/900 的每个 nav item 都有 `iconCentered=true`，`focusOutline=solid`，页面 `horizontalOverflow=false`。可见文字没有裁切；脚本记录的裁切项只来自 `.sr-only` 无障碍隐藏文本。

### 3. Targets 查找进程状态文案和布局

Root cause:

原交互把“未刷新/未查找/查找中/空结果/失败/有结果”混在同一个刷新状态里，未操作时会出现“尚未刷新进程”这类残留式提示。空结果块也容易和输入框、按钮处于同一视觉层，造成重叠或像是按钮反馈还没结束。

Fix:

- `TargetsPage.tsx` 增加 `getProcessLookupState`，明确拆出 `idle / loading / empty / error / results`。
- 每种状态使用独立 short message、help、empty title、empty description。
- `pages.css` 把 toolbar、help、empty/result list 分层，empty/result 块固定在 toolbar 下方。

Evidence:

- `artifacts/webview2-ui-interaction-polish-20260524/targets-lookup-idle-1280x720.png`
- `artifacts/webview2-ui-interaction-polish-20260524/targets-lookup-loading-1280x720.png`
- `artifacts/webview2-ui-interaction-polish-20260524/targets-lookup-empty-1280x720.png`
- `artifacts/webview2-ui-interaction-polish-20260524/targets-lookup-failure-1280x720.png`
- `artifacts/webview2-ui-interaction-polish-20260524/targets-lookup-results-1280x720.png`

Verification:

JSON 证据覆盖 5 个状态，`overlapsToolbar=false`，`emptyBelowToolbar=true`，`horizontalOverflow=false`。未查找状态显示“尚未查找”，不再使用“尚未刷新进程”。

### 4. Reports 三点按钮和菜单锚定

Root cause:

三点按钮继承了普通按钮的文字布局和 gap，图标可能不是真正视觉居中。菜单此前相对行容器或更大区域定位，用户会感觉菜单从按钮旁边飘走。

Fix:

- `ReportsPage.tsx` 使用 `MoreHorizontal` 图标和 `report-more-button--icon`。
- `pages.css` 将三点按钮固定为 `34px x 34px`，内部图标 16px 居中，文本只作为 `.sr-only`。
- 菜单挂在 `.report-row-actions` 的相对定位上下文中，`right: 0; top: calc(100% + var(--fs-space-1))`，保持和按钮明确关联。
- 菜单增加 `role="menu"`、`role="menuitem"`、Escape/Tab/Arrow/Home/End 键盘行为。

Evidence:

- `artifacts/webview2-ui-interaction-polish-20260524/reports-menu-1280x720.png`
- `artifacts/webview2-ui-interaction-polish-20260524/reports-menu-900x760.png`

Verification:

`ui-interaction-polish-evidence.json` 显示 1280/900 均为 `dotsCentered=true`、`anchoredToButton=true`、`menuRole=menu`、`ariaExpanded=true`。横向溢出为 false。

### 5. Reports 列表刷新、选中和菜单动画生硬

Root cause:

列表刷新和菜单开合原本更像 DOM 瞬时切换，局部状态没有保留短暂的 transition/closing 阶段；视觉上会像整块列表硬闪。reduced motion 下又不能用淡入淡出掩盖。

Fix:

- `ReportsPage.tsx` 使用 `selectedReportId`，选中行只更新局部 row 状态。
- 菜单增加 `closingReportMenuId` 和 110ms close timer，关闭时有短暂局部退出状态。
- `pages.css` 对 row background/border、menu enter/exit 使用克制局部动效。
- `prefers-reduced-motion: reduce` 下关闭 menu animation 和 row update animation。

Evidence:

- `artifacts/webview2-ui-interaction-polish-20260524/reports-animation-selected-before.png`
- `artifacts/webview2-ui-interaction-polish-20260524/reports-animation-selected-after-01.png`
- `artifacts/webview2-ui-interaction-polish-20260524/reports-animation-selected-after-02.png`
- `artifacts/webview2-ui-interaction-polish-20260524/reports-animation-refresh-01.png`
- `artifacts/webview2-ui-interaction-polish-20260524/reports-animation-refresh-02.png`
- `artifacts/webview2-ui-interaction-polish-20260524/reports-animation-menu-open-01.png`
- `artifacts/webview2-ui-interaction-polish-20260524/reports-animation-menu-open-02.png`
- `artifacts/webview2-ui-interaction-polish-20260524/reports-animation-menu-close-01.png`
- `artifacts/webview2-ui-interaction-polish-20260524/reports-animation-menu-close-02.png`

Verification:

证据脚本捕获 9 帧，`reportsAnimation.success=true`。每帧 `activePage=reports`，`visibleReportRows=3`，横向溢出为 false。reduced-motion smoke 通过，说明 reduced motion 下可以直接切换。

### 6. Overview 启动监测第一次点击无反馈

Root cause:

启动监测依赖 bridge 异步响应和事件回推；点击后如果本地没有立即 pending 状态，用户会看到按钮像没反应。同时如果没有本地 in-flight guard，第一次点击到事件返回之间存在重复触发风险。

Fix:

- `OverviewPage.tsx` 增加 `monitorStartFeedback` 和 `monitorStartInFlightRef`。
- 点击启动时先同步设置 `pending`，按钮立刻变成“正在启动”，并显示局部 `aria-live` 反馈。
- 在 pending 或 ref in-flight 时直接 return，保证一次点击只触发一次 `monitor.start`。
- bridge 成功/失败或运行态变更后再清理本地 pending。

Evidence:

- `artifacts/webview2-ui-interaction-polish-20260524/overview-first-click-start-feedback.png`
- `ui-interaction-polish-evidence.json`: `buttonText=正在启动`, `buttonDisabled=true`, `mockAdapterSingleSendProof=true`

Verification:

live smoke 的 bridge extension 检查显示 `monitorStartAccepted=true`、`monitorStarted=true`。截图证明确认第一次点击后立即出现局部反馈，不需要点两次。

### 7. Reports Live Smoke 后续失败收尾

Root cause:

重新生成报告成功后，Reports 列表会刷新并重新排序。WebView2 smoke 先记录旧 `reportIndex`，再按旧 index 点击 `open-report-N` 和 `open-directory-N`。列表刷新后旧 index 可能已经指向另一份报告，导致真实请求发出，但 smoke 等不到原 reportId 的 `opened/directory_opened` 响应。

Fix:

- `ReportsPage.tsx` 增加 `reportSmokeIndexByIdRef` 和 `smokeIndexByReportId`。
- 每个 reportId 首次出现时分配稳定 smoke index；列表刷新/重排后同一 reportId 仍保留原 `data-smoke-action` index。
- `uiInteractionContract.test.ts` 增加稳定 index 合同。
- `uiDesignContract.test.ts` 同步从旧 `index` 合同改为 `smokeIndex` 合同。

Evidence:

- `artifacts/webview2-ui-interaction-polish-20260524/webview2-live-smoke.json`
- `artifacts/webview2-ui-interaction-polish-20260524/webview2-live-smoke-reports-open-success.png`
- `artifacts/webview2-ui-interaction-polish-20260524/webview2-live-smoke-reports-open-directory-success.png`
- `artifacts/webview2-ui-interaction-polish-20260524/webview2-live-smoke-reports-regenerate-success.png`

Verification:

重新运行 live smoke 后：

- `success=true`
- `reportLiveActionSmoke.success=true`
- `reportOpenClickOk=true`
- `reportOpenDirectoryClickOk=true`
- `reportRegenerateClickCompleted=true`
- `bridgeExtensionSmoke.success=true`
- `console=[]`
- `errors=[]`

## Screenshot And Evidence Summary

Evidence directory:

`artifacts/webview2-ui-interaction-polish-20260524`

Important files:

- `ui-interaction-polish-evidence.json`
- `webview2-live-smoke.json`
- `webview2-live-smoke.png`
- `webview2-reduced-motion-smoke.json`
- `webview2-reduced-motion-smoke.png`
- 19 static browser screenshots from the evidence script.
- WebView2 live/reduced smoke page, transition, report-action, settings, and targets screenshots.

Automated screenshot evidence summary:

```json
{
  "success": true,
  "failures": 0,
  "sidebar": 2,
  "targets": 5,
  "menus": 2,
  "reportsAnimationFrames": 9,
  "horizontalOverflowFailures": 0
}
```

No visible overlap or visible text clipping was found in the required evidence. The only clipped text reported by the mechanical checker is `.sr-only` accessibility text, which is intentionally hidden.

## Verification Results

### Frontend Verify

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result: PASS.

Observed:

- `npm ci`: completed.
- `tsc --noEmit`: completed.
- Vitest: 5 files passed, 44 tests passed.
- Vite build: completed, `src/frontend/dist` regenerated.

### Static Visual Evidence

Command:

```powershell
& "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe" .\artifacts\webview2-ui-interaction-polish-20260524\capture-ui-interaction-polish-evidence.cjs
```

Result: PASS.

Observed:

- `success=true`
- `failures=[]`
- `screenshots=19`
- `ui-interaction-polish-evidence.json` generated successfully.

### WebView2 Live Smoke

Command:

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-ui-interaction-polish-20260524\webview2-live-smoke.json --web-ui-screenshot artifacts\webview2-ui-interaction-polish-20260524\webview2-live-smoke.png --web-ui-timeout-ms 120000
```

Result: PASS.

Observed:

- `success=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=false`
- `reportLiveActionSmoke.success=true`
- `reportOpenClickOk=true`
- `reportOpenDirectoryClickOk=true`
- `reportRegenerateClickCompleted=true`
- `bridgeExtensionSmoke.success=true`
- `monitorStartAccepted=true`
- `monitorStarted=true`
- console count: 0
- error count: 0

### WebView2 Reduced-Motion Smoke

Command:

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-ui-interaction-polish-20260524\webview2-reduced-motion-smoke.json --web-ui-screenshot artifacts\webview2-ui-interaction-polish-20260524\webview2-reduced-motion-smoke.png --web-ui-timeout-ms 120000
```

Result: PASS.

Observed:

- `success=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=true`
- `reportLiveActionSmoke.success=true`
- `reportOpenClickOk=true`
- `reportOpenDirectoryClickOk=true`
- `reportRegenerateClickCompleted=true`
- `bridgeExtensionSmoke.success=true`
- console count: 0
- error count: 0

### Diff Whitespace Check

Command:

```powershell
git diff --check
```

Result: PASS.

Notes:

Git printed existing LF-to-CRLF warnings for dirty working-copy files, but no whitespace error was reported and exit code was 0.

### Residual Process Check

Checked for project-related residual processes and listeners:

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `PresentMon*.exe`
- project-related `node.exe`
- project-related `msedge.exe` / WebView2 smoke temp profile
- ports `4259`, `4260`, `4261`, `5174`, `4174`, `9423`

Result: PASS.

No matching project-related process or listener remained after the verification runs.

## Retest Recommendation

建议测试员复测：YES.

原因：

- 本轮修的是截图可见的 UI 对齐、状态文案、菜单锚定、局部动画和第一次点击反馈问题，人工视觉确认仍有价值。
- 自动截图、live smoke 和 reduced-motion smoke 已经给出可复测基线。
- 复测范围应保持在 React WebView2 前端 UI/交互，不要把本报告当成安装包、payload 同步、GitHub Release 或后端采样验证结论。
