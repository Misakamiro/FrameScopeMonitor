# FrameScope Monitor WebView2 UI Interaction Polish Retest Report

Date: 2026-05-24
Source root: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Verdict

PASS.

本轮只做 WebView2 React UI/交互复测：没有改业务源码、没有打包、没有推 GitHub。新增的内容只有复测证据目录和本复测报告。

结论：用户指出的 6 个 UI/交互问题均已复测通过，不需要继续针对这 6 项做 UI 修复。后续如果继续处理，只建议做非阻塞的 P2 视觉精修；本报告不证明安装包、payload 同步、GitHub Release 或完整发布链路通过。

## Evidence Directory

复测证据目录：

`artifacts\webview2-ui-interaction-polish-retest-20260524`

关键文件：

- `ui-interaction-polish-evidence.json`
- `retest-summary.json`
- `webview2-live-smoke.json`
- `webview2-live-smoke.png`
- `webview2-reduced-motion-smoke.json`
- `webview2-reduced-motion-smoke.png`
- 79 张 PNG 截图，包含 Sidebar、Targets 五状态、Reports 菜单、Reports 动画连续帧、Overview 首次点击反馈和 WebView2 smoke 截图。

## Required Verification

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
- Vite production build completed.

### Static UI Evidence

Command:

```powershell
& "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe" .\artifacts\webview2-ui-interaction-polish-retest-20260524\capture-ui-interaction-polish-evidence.cjs
```

Result: PASS.

Observed:

- `success=true`
- `failures=[]`
- `screenshots=19`
- `ui-interaction-polish-evidence.json` regenerated under the retest evidence directory.

### WebView2 Live Smoke

Command:

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-ui-interaction-polish-retest-20260524\webview2-live-smoke.json --web-ui-screenshot artifacts\webview2-ui-interaction-polish-retest-20260524\webview2-live-smoke.png --web-ui-timeout-ms 120000
```

Result: PASS.

Observed:

- `success=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=false`
- console count: 0
- error count: 0
- `reportLiveActionSmoke.success=true`
- `reportOpenClickOk=true`
- `reportOpenDirectoryClickOk=true`
- `reportRegenerateClickCompleted=true`
- `bridgeExtensionSmoke.success=true`
- `monitorStartAccepted=true`
- `monitorStarted=true`
- `monitorStopped=true`
- smoke message audit: exactly 1 `monitor.start` request, 1 accepted event, 1 started event.

### WebView2 Reduced-Motion Smoke

Command:

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-ui-interaction-polish-retest-20260524\webview2-reduced-motion-smoke.json --web-ui-screenshot artifacts\webview2-ui-interaction-polish-retest-20260524\webview2-reduced-motion-smoke.png --web-ui-timeout-ms 120000
```

Result: PASS.

Observed:

- `success=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=true`
- console count: 0
- error count: 0
- `reportLiveActionSmoke.success=true`
- `reportOpenClickOk=true`
- `reportOpenDirectoryClickOk=true`
- `reportRegenerateClickCompleted=true`
- `bridgeExtensionSmoke.success=true`
- `monitorStartAccepted=true`
- `monitorStarted=true`
- `monitorStopped=true`
- smoke message audit: exactly 1 `monitor.start` request, 1 accepted event, 1 started event.

### Diff Check

Command:

```powershell
git diff --check
```

Result: PASS.

Observed:

- Exit code: 0.
- Git only printed existing Windows LF-to-CRLF conversion warnings.
- No whitespace error was reported.

### Residual Process Check

Checked project-related processes and listeners:

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `PresentMon*.exe`
- project-related `node.exe`
- project-related `msedge.exe`
- ports `4259`, `4260`, `4261`, `5174`, `4174`, `9423`

Result: PASS.

Observed:

- Related project process count: 0.
- Related listener count: 0.

## Six-Issue Retest Matrix

| # | User issue | Result | Evidence |
| --- | --- | --- | --- |
| 1 | Sidebar 蓝条是否和当前导航按钮垂直居中、对齐，不再偏移 | PASS | `sidebar-1280x720.png`, `sidebar-900x760.png`, `ui-interaction-polish-evidence.json` |
| 2 | Sidebar active/hover/focus 是否干净，不出现选中框、蓝条、文字、图标错位 | PASS | `sidebar-1280x720.png`, `sidebar-900x760.png` |
| 3 | Targets 查找进程五种状态文案是否正确；不能再出现“尚未刷新进程” | PASS | `targets-lookup-idle-1280x720.png`, `targets-lookup-loading-1280x720.png`, `targets-lookup-empty-1280x720.png`, `targets-lookup-failure-1280x720.png`, `targets-lookup-results-1280x720.png` |
| 4 | Reports 三点按钮居中；菜单贴近按钮展开，不偏移、不飘 | PASS | `reports-menu-1280x720.png`, `reports-menu-900x760.png` |
| 5 | Reports 列表刷新/选中/菜单动画更顺滑；reduced motion 稳定 | PASS | 9 张 `reports-animation-*.png`, `webview2-reduced-motion-smoke.json` |
| 6 | Overview 启动监测第一次点击立刻反馈，只需点击一次，不重复发送 `monitor.start` | PASS | `overview-first-click-start-feedback.png`, `webview2-live-smoke.json`, `webview2-reduced-motion-smoke.json` |

## Detailed Findings

### 1. Sidebar Active Rail Alignment

Result: PASS.

Evidence:

- `sidebar-1280x720.png`
- `sidebar-900x760.png`

Measured:

- `sidebarCount=2`
- `railInsideItem=true` for both 1280 and 900 screenshots.
- `railCentered=true` for both 1280 and 900 screenshots.
- `hasOuterIndicator=false`
- `horizontalOverflow=false`

Manual visual check:

- 1280 宽度下，Reports 当前项的蓝色竖条在按钮内部，和按钮高度中心对齐。
- 900 compact 宽度下，蓝条仍然贴在当前按钮内部，没有向上或向下偏移。
- 没看到旧的外置 active indicator。

### 2. Sidebar Active / Hover / Focus Cleanliness

Result: PASS.

Evidence:

- `sidebar-1280x720.png`
- `sidebar-900x760.png`

Measured:

- `allIconsCentered=true`
- `activeNav=reports`
- `focusedNav=settings`
- all sidebar screenshots have `horizontalOverflow=false`

Manual visual check:

- active 状态、鼠标悬停状态、键盘 focus 状态没有互相挤压。
- 图标和文字没有明显错位。
- focus 只表达键盘位置，不再制造额外的选中框错觉。
- 900 compact 下短标签可读，未退化成含义不清的纯图标栏。

### 3. Targets Process Lookup States

Result: PASS.

Evidence:

- `targets-lookup-idle-1280x720.png`
- `targets-lookup-loading-1280x720.png`
- `targets-lookup-empty-1280x720.png`
- `targets-lookup-failure-1280x720.png`
- `targets-lookup-results-1280x720.png`

Measured state text:

| State | Status text | Empty title / row count |
| --- | --- | --- |
| idle | `尚未查找` | `尚未查找进程` |
| loading | `正在查找...` | `正在查找匹配进程` |
| empty | `没有结果` | `没有找到匹配进程` |
| failure | `查找失败` | `查找进程失败` |
| results | `已找到 1 项` | row count `1` |

Measured layout:

- `targetsLegacyTextCount=0`
- `overlapsToolbar=false` for all five states.
- `emptyBelowToolbar=true` for all five states.
- `horizontalOverflow=false` for all five states.

Manual visual check:

- 未查找状态已经是“尚未查找进程”，没有出现“尚未刷新进程”。
- 查找中、无结果、失败、有结果的文案含义明确。
- 空状态和结果区都在 toolbar 下方，没有压住输入框和按钮。

### 4. Reports More Button And Menu Anchor

Result: PASS.

Evidence:

- `reports-menu-1280x720.png`
- `reports-menu-900x760.png`

Measured:

- `dotsCentered=true` at 1280 and 900.
- `anchoredToButton=true` at 1280 and 900.
- `menuRole=menu`
- `ariaExpanded=true`
- `horizontalOverflow=false`

Manual visual check:

- 三点按钮里的三个点位于 34x34 按钮视觉中心。
- 菜单紧贴触发按钮下方展开，没有漂到列表中间或远离触发点。
- 900 宽度下菜单仍然跟随当前行按钮，不遮挡关键 report 信息。

### 5. Reports Animation And Reduced Motion

Result: PASS.

Evidence:

- `reports-animation-selected-before.png`
- `reports-animation-selected-after-01.png`
- `reports-animation-selected-after-02.png`
- `reports-animation-refresh-01.png`
- `reports-animation-refresh-02.png`
- `reports-animation-menu-open-01.png`
- `reports-animation-menu-open-02.png`
- `reports-animation-menu-close-01.png`
- `reports-animation-menu-close-02.png`
- `webview2-reduced-motion-smoke.json`

Measured:

- `reportsAnimation.success=true`
- `frameCount=9`
- `framesOk=true`
- every captured frame has `activePage=reports`
- every captured frame has `visibleReportRows=3`
- every captured frame has `horizontalOverflow=false`
- reduced-motion smoke `success=true`, `reducedMotion=true`, console/errors both 0.

Manual visual check:

- 选中态和菜单开合是局部变化，没有整块列表硬闪。
- refresh 连续帧没有出现空白列表或页面错乱。
- menu close 有短暂 closing 状态，然后稳定消失。
- reduced motion 下功能稳定，没有依赖动画遮盖布局问题。

### 6. Overview First Click Feedback And Duplicate Start Guard

Result: PASS.

Evidence:

- `overview-first-click-start-feedback.png`
- `webview2-live-smoke.json`
- `webview2-reduced-motion-smoke.json`

Measured browser-preview feedback:

- button text: `正在启动`
- button disabled: `true`
- feedback text: `已收到点击，正在启动监控...`
- `mockAdapterSingleSendProof=true`
- `horizontalOverflow=false`

Measured WebView2 smoke:

- live smoke: exactly 1 `monitor.start` request, 1 accepted event, 1 started event.
- reduced-motion smoke: exactly 1 `monitor.start` request, 1 accepted event, 1 started event.
- `monitorStartAccepted=true`
- `monitorStarted=true`
- `monitorStopped=true`

Manual visual check:

- 第一次点击后按钮立即变成“正在启动”，无需第二次点击。
- 页面同时出现“已收到点击，正在启动监控...”的局部反馈。
- WebView2 真宿主 smoke 没有重复发送 `monitor.start`。

## Git / Scope Notes

Current worktree already had many UI/WebView2 related dirty files before this retest. 本轮没有回退、没有整理、没有改这些既有源码改动。

本轮新增/生成的复测产物：

- `artifacts\webview2-ui-interaction-polish-retest-20260524\`
- `docs\test-reports\2026-05-24-framescope-webview2-ui-interaction-polish-retest-report.md`

## Final Conclusion

PASS.

这 6 个 UI/交互问题不需要继续修：

- Sidebar active 蓝条和当前项对齐。
- Sidebar active/hover/focus 状态干净。
- Targets 五状态文案正确，未发现“尚未刷新进程”残留。
- Reports 三点按钮居中，菜单锚定正确。
- Reports 刷新、选中、菜单连续帧稳定，reduced motion smoke 通过。
- Overview 首次点击立即反馈，WebView2 smoke 只发送一次 `monitor.start`。

建议下一步如果继续推进，应进入单独的最终人工验收或包装验证窗口；不要把本次 UI 复测结论扩大成打包/发布结论。
