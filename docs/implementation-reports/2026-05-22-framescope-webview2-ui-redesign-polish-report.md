# FrameScope Monitor WebView2 React UI Redesign Polish Report

日期：2026-05-23
角色：FrameScope Monitor WebView2 React UI 小修收尾工程师
源码根目录：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 当前结论

PARTIAL。

本轮 UI 小修目标已完成，并通过前端验证、浏览器截图审计、hidden smoke 入口审计、侧边栏滚动固定验证和 `git diff --check`。但 WebView2 live smoke 与 reduced-motion smoke 在当前环境均未通过：两者都卡在 WebView2 导航完成前，未进入 React `webview-ready`，因此本轮不能给 PASS。

本轮没有修改 C# bridge 语义、后端采样、报告生成、diagnostics、GameLite、WMI、SGuard、`build.ps1`、packaging、README 或 GitHub Release。

## 修改文件清单

本轮小修实际修改：

- `src/frontend/src/pages/OverviewPage.tsx`
- `src/frontend/src/pages/TargetsPage.tsx`
- `src/frontend/src/pages/SettingsPage.tsx`
- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/pages/pages.css`
- `src/frontend/src/uiDesignContract.test.ts`
- `docs/implementation-reports/2026-05-22-framescope-webview2-ui-redesign-polish-report.md`

本轮新增验证证据：

- `artifacts/webview2-ui-redesign-polish-20260522/browser-polish-audit.json`
- `artifacts/webview2-ui-redesign-polish-20260522/capture-polish-evidence.cjs`
- `artifacts/webview2-ui-redesign-polish-20260522/browser-*.png`
- `artifacts/webview2-ui-redesign-polish-20260522/scroll-*.png`
- `artifacts/webview2-ui-redesign-polish-20260522/webview2-*.json`

当前工作树还保留上一轮 UI redesign 的未提交改动，详见：

- `docs/implementation-reports/2026-05-22-framescope-webview2-ui-redesign-implementation-report.md`

## Overview 重复主按钮如何修复

- 移除了 Overview 页面 header 右侧的 `primaryAction` 主按钮。
- 保留主状态卡片中的 `启动监控` / `停止监控` / `打开最新报告` 作为唯一主操作。
- 将 `data-smoke-action={primaryAction.smokeAction}` 移到主状态卡片按钮上，避免破坏原有 smoke 选择器。
- Header 现在只保留 `刷新状态` 次级按钮。

验收证据：

- `artifacts/webview2-ui-redesign-polish-20260522/browser-polish-audit.json`
- Overview 1280x720：`primaryButtons=["启动监控"]`，`primaryButtonCount=1`
- Overview 900x760：`primaryButtons=["启动监控"]`，`primaryButtonCount=1`

## 保存按钮状态如何修复

Targets：

- `保存修改` 文案保持不变。
- `dirty=false` 时按钮为 `secondary` 且 disabled，不再显示为可点击蓝色主按钮。
- `dirty=true` 时按钮切回 `primary`，并保持原有保存逻辑。
- `撤销` 和 `重新读取` 从 header 移到右侧状态/辅助区域，减少 900x760 下 header 噪声。
- 保存失败时仍由原有 `saveDraft` 逻辑保留草稿输入，不清空用户修改。

Settings：

- `保存修改` 文案保持不变。
- `dirty=false` 时按钮为 `secondary` 且 disabled。
- `dirty=true` 时按钮切回 `primary`。
- `撤销` 移到右侧状态区，header 只保留保存入口。
- 保存失败时仍由原有 `saveDraft` 逻辑保留当前 draft，不丢输入。

验收证据：

- Targets 1280x720：`primaryButtonCount=0`
- Targets 900x760：`primaryButtonCount=0`
- Settings 1280x720：`primaryButtonCount=0`
- Settings 900x760：`primaryButtonCount=0`
- 契约测试新增断言：Targets / Settings 均包含 `variant={dirty ? "primary" : "secondary"}`

## Header 操作层级如何处理

- Overview：header 移除主操作，只保留 `刷新状态`。
- Targets：header 保留 `添加目标` 和 `保存修改`，但无修改时 `保存修改` 是 disabled secondary；`撤销`、`重新读取` 下放到辅助区域。
- Settings：header 只保留 `保存修改`，无修改时 disabled secondary；`撤销` 下放到辅助区域。
- Reports：header 保留页面级 `打开最新报告`，详情区的 `打开报告` 降级为 `secondary`，避免两个同等蓝色主按钮同时抢重点。
- 900x760 浏览器截图审计确认没有横向溢出，Targets / Settings header 不挤压标题。

## Hidden smoke 入口复核结果

Reports 中保留 hidden smoke 入口，以便 WebView2 smoke 继续点击真实 `openReportDirectory` / `regenerateReport` 前端 action，但它们不会暴露给普通用户：

- `className="sr-only"`
- `aria-hidden="true"`
- `tabIndex={-1}`
- 不在普通 Tab 顺序中
- 不显示在可见 UI 中

自动化审计结果：

- `hiddenCount=6`
- `anyVisible=false`
- `anyTabbable=false`
- `tabOrderContainsHiddenSmoke=false`
- 所有隐藏入口均为 `ariaHidden="true"`、`tabIndex=-1`

用户可见文本审计：

- 9 个页面/尺寸组合 `badText=[]`
- 未在可见 UI 中发现 `smoke`、`bridge contract`、`requestId`、`payload copy`、`窗口分工`

## 截图路径

本轮重新生成的必需截图：

- Overview 1280x720：`artifacts/webview2-ui-redesign-polish-20260522/browser-overview-1280x720.png`
- Overview 900x760：`artifacts/webview2-ui-redesign-polish-20260522/browser-overview-900x760.png`
- Targets 900x760：`artifacts/webview2-ui-redesign-polish-20260522/browser-targets-900x760.png`
- Settings 900x760：`artifacts/webview2-ui-redesign-polish-20260522/browser-settings-900x760.png`
- Reports 1280x720：`artifacts/webview2-ui-redesign-polish-20260522/browser-reports-1280x720.png`

额外截图：

- Targets 1280x720：`artifacts/webview2-ui-redesign-polish-20260522/browser-targets-1280x720.png`
- Reports 900x760：`artifacts/webview2-ui-redesign-polish-20260522/browser-reports-900x760.png`
- Settings 1280x720：`artifacts/webview2-ui-redesign-polish-20260522/browser-settings-1280x720.png`
- About 1280x720：`artifacts/webview2-ui-redesign-polish-20260522/browser-about-1280x720.png`

## 侧边栏固定滚动验证

证据：

- 审计 JSON：`artifacts/webview2-ui-redesign-polish-20260522/browser-polish-audit.json`
- Reports 滚动前：`artifacts/webview2-ui-redesign-polish-20260522/scroll-reports-before.png`
- Reports 滚动后：`artifacts/webview2-ui-redesign-polish-20260522/scroll-reports-after.png`
- Settings 滚动前：`artifacts/webview2-ui-redesign-polish-20260522/scroll-settings-before.png`
- Settings 滚动后：`artifacts/webview2-ui-redesign-polish-20260522/scroll-settings-after.png`

坐标检查：

- Reports：`brandTop 16->16`，`navTop 202->202`，`statusTop 708->708`，`scrollTop 0->678`，`fixed=true`
- Settings：`brandTop 16->16`，`navTop 264->264`，`statusTop 708->708`，`scrollTop 0->910`，`fixed=true`

## 验证命令结果

### 1. Frontend verify

命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

结果：PASS。

- `npm ci` 成功，安装 110 个包。
- `tsc --noEmit` 成功。
- Vitest：4 个测试文件，20 个测试全部通过。
- Vite build 成功。

### 2. Browser screenshot / layout audit

命令：

```powershell
& 'C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' .\artifacts\webview2-ui-redesign-polish-20260522\capture-polish-evidence.cjs
```

结果：PASS。

- 生成 9 张页面截图。
- 9 个页面/尺寸组合 `bodyScrollX=false`、`docScrollX=false`。
- 9 个页面/尺寸组合 `overflowing=[]`。
- 9 个页面/尺寸组合 `badText=[]`。
- Hidden smoke 审计通过。
- Reports / Settings 侧边栏滚动固定通过。

### 3. WebView2 live smoke

命令：

```powershell
FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-ui-redesign-polish-20260522\webview2-live-smoke.json --web-ui-screenshot artifacts\webview2-ui-redesign-polish-20260522\webview2-live-smoke.png --web-ui-timeout-ms 120000
```

结果：FAIL。

证据：

- `artifacts/webview2-ui-redesign-polish-20260522/webview2-live-smoke.json`
- `success=false`
- `pageLoaded=false`
- `pageReady=false`
- `elapsedMs=120151`
- `error="Timed out waiting for WebView2 bridge smoke."`
- `messages` 只到 `host:navigate react-web-ui ... smoke=True`

复核尝试：

- `webview2-live-smoke-use-shell.json`：同样超时。
- `webview2-live-smoke-payload.json`：payload exe 同样超时。
- `webview2-live-smoke-visible.json`：可见窗口同样超时。
- `webview2-live-smoke-disable-gpu.json`：改为 `ConnectionAborted`，仍未进入 React ready。

对照上一轮成功证据：

- `artifacts/webview2-ui-redesign-20260522/webview2-live-smoke-final.json` 中上一轮成功路径包含 `host:navigation-completed success=True status=200` 和 `js->host {"type":"webview-ready"}`。
- 本轮失败路径没有 `NavigationCompleted` 成功事件，也没有 `webview-ready`。

当前判断：失败发生在 WebView2 导航完成前，尚未进入 React 页面或本轮修改的按钮/hidden smoke 操作。

### 4. WebView2 reduced-motion smoke

命令：

```powershell
FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-ui-redesign-polish-20260522\webview2-reduced-motion-smoke.json --web-ui-screenshot artifacts\webview2-ui-redesign-polish-20260522\webview2-reduced-motion-smoke.png --web-ui-timeout-ms 120000
```

结果：FAIL。

证据：

- `artifacts/webview2-ui-redesign-polish-20260522/webview2-reduced-motion-smoke.json`
- `success=false`
- `pageLoaded=false`
- `pageReady=false`
- `elapsedMs=120159`
- `error="Timed out waiting for WebView2 bridge smoke."`

当前判断：失败点与 live smoke 一致，仍在 WebView2 导航/ready 之前。

### 5. git diff check

命令：

```powershell
& 'C:\Program Files\Git\cmd\git.exe' diff --check
```

结果：PASS。

输出只有 Git 的 LF/CRLF 提示，没有 whitespace error。

## 残留进程检查

检查项：

- `FrameScopeMonitor`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `PresentMon`
- 本轮 30 分钟内启动的 `msedgewebview2`
- 本轮 2 小时内启动的 `node` / `msedge` / `msedgewebview2`

结果：

- FrameScope 相关进程：无残留。
- 本轮新启动的 Node / Edge / WebView2 证据采集进程：无残留。
- 端口 `4183` / `9234` / `4173` / `9224` 未发现本轮 LISTENING 残留。
- `192.168.5.111:9234 -> 49.7.118.175:443` 属于既有 `O+Connect` 进程，不是本轮 CDP/Edge 监听，也不是 FrameScope。

## 是否建议交给 UI 动画窗口继续

建议交给 UI 动画窗口继续，但前提是动画窗口不要把当前 WebView2 smoke 导航失败当作动画问题处理。

本轮 UI 层级问题已经完成：

- Overview 第一屏只剩一个 primary CTA。
- Targets / Settings 无修改时保存按钮不再是可点击蓝色主按钮。
- Header 操作数量和视觉层级已收束。
- Reports hidden smoke 入口不可见、不可 Tab 聚焦。

后续动画窗口可以继续处理 motion 方案，但需要先保留当前 shell 和 smoke selector，不要改 C# bridge 或后端语义。WebView2 smoke 当前失败点在导航完成前，建议单独作为运行环境/宿主验证问题跟进。
