# FrameScope Monitor WebView2 Smoke Navigation Diagnosis Report

日期：2026-05-23
源码根目录：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 结论

本轮 WebView2 smoke 导航阻塞已完成诊断和修复验证。

旧失败不是 React root、asset path、mock/live 判断、hidden smoke probe 或 ready probe 被 UI 小修改坏。旧失败发生在 WebView2 导航完成前：host 只记录到 `host:navigate react-web-ui ... smoke=True`，没有 `NavigationCompleted`、`DOMContentLoaded`、React `webview-ready`。失败 profile 下存在 WebView2 Crashpad dump，因此 root cause 排序为 WebView2 runtime / renderer / navigation process 在导航完成前失败或卡死。

本轮代码修复分两部分：

- 增强 WebView2 smoke host 诊断：记录 navigation、resource response、DOMContentLoaded、console、exception、ProcessFailed，并在 smoke JSON 输出 `frontendPath`、`usingReactFrontend`、`reducedMotion`、`navigation`、`console`、`errors`。
- 修复 smoke harness 自身的 Reports 操作顺序：先 `reports.regenerate`，再 `reports.open` / `reports.openDirectory`。原因是 live smoke 后续复测暴露出测试流程先打开报告文件，再重生成同一个 `framescope-interactive-data.js`，会被外部浏览器文件锁打断。这不是报告生成语义修复，只是 smoke 顺序修复。

最终结果：WebView2 live smoke PASS，reduced-motion smoke PASS，均加载 `src\frontend\dist`，`pageLoaded=true`，`pageReady=true`，React `webview-ready` 成功，console/errors 为空。

## 复现证据

旧 live 失败：

- 文件：`artifacts\webview2-ui-redesign-polish-20260522\webview2-live-smoke.json`
- `success=false`
- `pageLoaded=false`
- `pageReady=false`
- `elapsedMs=120151`
- `error="Timed out waiting for WebView2 bridge smoke."`
- `messages` 只有 `host:navigate react-web-ui ... src\frontend\dist smoke=True`

旧 reduced-motion 失败：

- 文件：`artifacts\webview2-ui-redesign-polish-20260522\webview2-reduced-motion-smoke.json`
- `success=false`
- `pageLoaded=false`
- `pageReady=false`
- `elapsedMs=120159`
- `error="Timed out waiting for WebView2 bridge smoke."`
- `messages=[]`

对照成功基线：

- 文件：`artifacts\webview2-ui-redesign-20260522\webview2-live-smoke-final.json`
- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- 包含 `host:navigation-completed success=True status=200`
- 包含 `js->host {"type":"webview-ready","payload":{"client":"react"}}`

本轮环境证据：

- `src\frontend\dist\index.html` 存在。
- 当前 dist 引用：
  - `/assets/index-CrDMYm0P.js`
  - `/assets/index-DCsoWywY.css`
- 旧失败 WebView2 temp profile 下存在 Crashpad `.dmp`，例如：
  - `%TEMP%\FrameScopeMonitorWebView2\b125296a90fb4d2ebce6ad3cd425ad55\EBWebView\Crashpad\reports\3eff4645-e51f-4bac-909d-231f6b82c4f1.dmp`

## Root Cause

第一层 root cause：旧 PARTIAL 失败发生在 WebView2 导航生命周期内，React 尚未启动。证据是没有 `NavigationCompleted`、没有 `DOMContentLoaded`、没有 `webview-ready`，并且旧失败 profile 有 Crashpad dump。这个阶段还没有进入 Reports/Targets/Settings 交互，也没有执行 hidden smoke 操作。

第二层 root cause：本轮加上诊断后，导航已经稳定完成，但 live smoke 继续暴露了另一个 smoke harness 自身问题：Reports live action 先执行 `openReport` / `openDirectory`，再对同一个 report 执行 `regenerate`。在 Windows 上，刚打开的报告浏览器会占用 `framescope-interactive-data.js`，随后 `FrameScopeReportGenerator` 写入同名文件时失败：

```text
System.IO.IOException: 文件 ...\charts\framescope-interactive-data.js 正由另一进程使用，因此该进程无法访问此文件。
```

修复方式是只调整 smoke 顺序：先确认 regenerate 完成，再验证 open/openDirectory。这样保留真实 UI action 覆盖，也不改报告生成、后端采样或 bridge 业务语义。

## 排除假设

1. React dist asset path/base path 在 WebView2 下失效，JS 没启动。
   - 最终证据排除。`live-fixed-final.json` 和 `reduced-fixed-final.json` 中 `index.html`、CSS、JS 均为 `status=200`，React 发送 `webview-ready`。

2. React 已加载，但 ready probe 或 harness 判定条件坏了。
   - 旧失败时 React 未到 ready 阶段；修复后 `pageReady=true` 且 messages 包含 `webview-ready`。ready probe 本身可用。

3. Host 导航到错误路径或 fallback HTML。
   - 排除。最终 smoke 输出 `frontendPath=...\src\frontend\dist`，`usingReactFrontend=true`，资源路径是 `https://app.framescope.local/index.html` 和 dist assets。

4. 前端运行时报错导致 `webview-ready` 没发送。
   - 排除。最终 smoke 的 `console=[]`、`errors=[]`，并且 `webview-ready` 已收到。

5. 参数注入时机导致 ready 事件被错过。
   - 排除。最终 live/reduced 均能记录 `NavigationCompleted`、`DOMContentLoaded`、`webview-ready` 和后续 bridge requests。

6. 最近 UI 小修改坏 root element、adapter 初始化、mock/live 判断或 hidden smoke probe。
   - 排除。最终 smoke 覆盖 overview、targets、reports、settings、about，Reports hidden action 也能执行真实 regenerate/open/openDirectory。

## 修改文件

- `src/app/FrameScopeNativeMonitor.WebHost.cs`
  - 新增 WebView2 navigation/resource/console/exception/process-failed 诊断输出。
  - smoke JSON 增加 `frontendPath`、`usingReactFrontend`、`reducedMotion`、`navigation`、`console`、`errors`。
  - smoke 中 WebView2 `ProcessFailed` 发生时提前失败并写入具体 kind/reason/exitCode。
  - Reports smoke action 顺序改为先 regenerate，再 open/openDirectory，避免 smoke 自己造成文件锁。

未修改：

- 后端采样
- 报告生成器语义
- diagnostics 语义
- GameLite
- WMI
- SGuard
- packaging 源码
- README
- GitHub Release

说明：执行过 `build.ps1` 是为了重新编译 `FrameScopeMonitor.exe` 并验证本轮 C# 改动；该脚本会刷新 `dist` 安装包产物，但本轮没有修改 packaging 代码，也没有发布 release。

## Smoke 结果

WebView2 live smoke：

- 命令：`.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-smoke-navigation-diagnosis-20260523\live-fixed-final.json --web-ui-screenshot artifacts\webview2-smoke-navigation-diagnosis-20260523\live-fixed-final.png --web-ui-timeout-ms 120000`
- 结果：PASS
- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `frontendPath=...\src\frontend\dist`
- `NavigationCompleted success=true status=200`
- `index.html` / CSS / JS 均 `status=200`
- `webview-ready` 收到
- `console=[]`
- `errors=[]`
- `reportLiveActionSmoke.success=true`
- `reportRegenerateClickCompleted=true`
- `reportOpenClickOk=true`
- `reportOpenDirectoryClickOk=true`

WebView2 reduced-motion smoke：

- 命令：`.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-smoke-navigation-diagnosis-20260523\reduced-fixed-final.json --web-ui-screenshot artifacts\webview2-smoke-navigation-diagnosis-20260523\reduced-fixed-final.png --web-ui-timeout-ms 120000`
- 结果：PASS
- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `reducedMotion=true`
- `usingReactFrontend=true`
- `NavigationCompleted success=true status=200`
- `webview-ready` 收到
- `console=[]`
- `errors=[]`
- `reportLiveActionSmoke.success=true`

## UI 小修验收

Browser/CDP UI 审计：

- 命令：使用 bundled Node 运行 `artifacts\webview2-ui-redesign-polish-20260522\capture-polish-evidence.cjs`；由于本机 9234 被 `O+Connect.exe` 占用，实际复核使用临时 9334 CDP 端口和 `127.0.0.1`，未改仓库文件。
- 文件：`artifacts\webview2-ui-redesign-polish-20260522\browser-polish-audit.json`
- Overview 1280x720：`primaryButtonCount=1`，唯一 primary 为 `启动监控`。
- Targets 1280x720：`primaryButtonCount=0`。
- Settings 1280x720：`primaryButtonCount=0`。
- Hidden smoke：`hiddenCount=6`，`anyVisible=false`，`anyTabbable=false`，`tabOrderContainsHiddenSmoke=false`。
- Reports / Settings sidebar scroll：`fixed=true`。
- 9 个页面/尺寸组合：`bodyScrollX=false`，`docScrollX=false`，`overflowing=[]`，`badText=[]`。

保存按钮 DOM 复核：

- 文件：`artifacts\webview2-smoke-navigation-diagnosis-20260523\save-buttons-dom-audit.json`
- Targets：`data-smoke-action="save-targets"`，文本 `保存修改`，`disabled=true`，class `fs-button fs-button--secondary`。
- Settings：`data-smoke-action="save-config"`，文本 `保存修改`，`disabled=true`，class `fs-button fs-button--secondary`。

## 命令验证

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`：PASS
  - `npm ci` 成功
  - `tsc --noEmit` 成功
  - Vitest：4 个测试文件、20 个测试全部通过
  - Vite build 成功
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`：PASS
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`：PASS
- `.\tests\FrameScopeWebBridgeTests.exe`：PASS
- `git diff --check`：PASS；只有 LF/CRLF 提示，没有 whitespace error。

## 残留进程

最终残留进程检查未发现本轮 smoke 启动的 `FrameScopeMonitor.exe`、sampler、report generator、PresentMon、临时 Edge/CDP 或 Node 进程残留。

检查时仍存在一个长期系统 WebView2 进程：

- `msedgewebview2.exe`
- `CreationDate=2026/5/17 22:48:15`
- `user-data-dir=...\MicrosoftWindows.Client.CBS...\EBWebView`
- `webview-exe-name=SearchHost.exe`

这是 Windows SearchHost/CBS 的系统 WebView2，不是本轮 smoke 残留，未清理。

## 是否建议回到 UI 动画窗口

建议可以回到 UI 动画窗口。

理由：本轮已把原 PARTIAL 的 WebView2 导航阻塞拆清楚并补上可观测性；最终 live / reduced-motion smoke 都能稳定到 React ready，并完成 Reports、Targets、Settings、monitor、diagnostics 相关 smoke 覆盖。后续动画窗口可以继续做路由动效、按钮反馈和 reduced-motion 细节，但不应再把旧导航失败当作动画问题处理。
