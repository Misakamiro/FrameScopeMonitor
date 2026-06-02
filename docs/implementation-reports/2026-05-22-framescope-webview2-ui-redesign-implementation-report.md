# FrameScope Monitor WebView2 React UI Redesign Implementation Report

日期：2026-05-22
角色：FrameScope Monitor WebView2 React UI 实现工程师
源码根目录：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 1. 当前结论

PASS。

本轮只改了 WebView2 React 前端 UI 层和必要的前端契约测试，未修改 C# bridge 语义、后端采样、报告生成、diagnostics、GameLite、WMI、SGuard、`build.ps1`、packaging、GitHub Release 或 README。

验证结果显示：

- 前端安装、类型检查、测试、构建通过。
- WebView2 live smoke 通过。
- WebView2 reduced-motion smoke 通过。
- 1280x720 和 900x760 静态布局截图与自动化审计通过。
- Reports / Settings 右侧内容区滚动时，左侧 sidebar 顶部品牌区、导航项和底部状态纵向位置不变。
- `git diff --check` 通过。
- 本轮证据采集进程和 FrameScope 相关进程无残留。

## 2. 修改文件清单

- `src/frontend/src/App.tsx`
- `src/frontend/src/layout/AppShell.tsx`
- `src/frontend/src/layout/TopStatusBar.tsx`
- `src/frontend/src/layout/SidebarNav.tsx`
- `src/frontend/src/layout/layout.css`
- `src/frontend/src/styles/global.css`
- `src/frontend/src/theme/tokens.css`
- `src/frontend/src/components/Button.tsx`
- `src/frontend/src/components/components.css`
- `src/frontend/src/pages/OverviewPage.tsx`
- `src/frontend/src/pages/TargetsPage.tsx`
- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/pages/SettingsPage.tsx`
- `src/frontend/src/pages/AboutPage.tsx`
- `src/frontend/src/pages/pages.css`
- `src/frontend/src/data/mockPreview.ts`
- `src/frontend/src/uiDesignContract.test.ts`
- `src/frontend/src/vite-env.d.ts`
- `docs/implementation-reports/2026-05-22-framescope-webview2-ui-redesign-implementation-report.md`

## 3. 每个页面具体改了什么

### 全局 Shell

- React UI 填满 WebView2 客户区，不再像内嵌小窗口。
- `body`、`#root`、`.app-root`、`.app-shell` 使用整窗高度并禁止全局滚动。
- `.page-viewport` 是唯一允许纵向滚动的主内容区。
- Sidebar 放在 shell 左侧，不放入页面滚动容器。
- 补齐 `min-height: 0` / `min-width: 0`，避免 WebView2 下 flex/grid 子元素把整个窗口撑出滚动。
- 900x760 下 sidebar 收缩为固定图标栏，仍然占据左侧并固定。
- 顶部栏不再重复页面标题，只显示本机连接、监控状态和需要注意的问题。

### Overview

- 改为“当前监控”主页面。
- 第一屏突出当前是否正在监控、是否有可监控目标、已启用目标数量和下一步主按钮。
- 保留“打开最新报告”入口，但不让报告历史、游戏名或指标卡片抢主任务。
- 主操作只突出一个：根据状态切换为“启动监控”“停止监控”或“打开最新报告”。

### Targets

- 默认显示可浏览目标列表，不再是整页输入框。
- 只有单行进入编辑状态时才显示输入框。
- 进程查找放到辅助区域，不抢目标管理主任务。
- 数据保存路径从目标列表顶部弱化到说明/次级信息。
- 1280x720 和 900x760 下通过自动化审计确认无横向溢出，中文没有竖排。

### Reports

- 改为报告列表 + 选中详情。
- 主列表优先显示游戏、时间、状态、帧数、大小和“打开报告”。
- 长 reportId 和路径移到详情区，不在主列表抢视觉。
- 每行可见按钮保持两个：“打开报告”“更多”。
- “打开文件夹”“重新生成报告”“生成诊断文件”放在详情区。
- 为保持 WebView2 live smoke 继续覆盖真实操作，保留了不可见的 smoke 操作入口，调用的仍是同一套真实 `openReportDirectory` / `regenerateReport` 前端 action，不增加用户可见按钮。
- 空状态给出下一步：有目标时去启动监控，没有目标时先添加目标。

### Settings

- 拆成三个清楚设置组：数据与报告、监控刷新、日志与诊断。
- 每组说明缩短为普通用户能理解的文案。
- 长路径用单行省略和 title 兜底，不撑破布局。
- 保存按钮改为“保存修改”，不再出现“无修改”这类按钮名。

### About

- 改为“关于与帮助”。
- 第一屏说明软件用途、版本、本地数据保存、常见问题和帮助入口。
- WebView2、bridge、requestId、smoke 等内部信息从主界面移入高级折叠区域，不在普通用户第一屏出现。

## 4. 设计文档要求实现情况

已实现：

- 全局 shell 填满 WebView2 客户区。
- Sidebar 固定在左侧，右侧内容区单独滚动。
- 900x760 下固定图标栏布局。
- Overview 改为“当前监控”决策页。
- 顶部栏与页面标题去重。
- Targets 改为目标管理列表。
- Reports 改为列表 + 详情。
- Settings 按数据与报告、监控刷新、日志与诊断分组。
- About 改为普通用户优先的“关于与帮助”。
- 中文文案按“动作 + 对象”重写，空状态和错误提示给出下一步。
- 视觉系统收束为实体面板、细边框、轻阴影、统一字号/间距/圆角。
- 保持现有真实前端 action：monitor start/stop、targets get/save、processes.refresh、reports list/open/openDirectory/regenerate、diagnostics.generate、settings config.get/save。
- mock/live 边界未被改写，真实 WebView2 smoke 仍通过。

未实现或未展开：

- 动画专项大改未做。原因：本轮明确要求只做 UI 信息架构、排版、视觉系统、文案和 sidebar 固定，动画只允许必要适配。当前 reduced-motion smoke 已通过，建议交给后续 UI 动画窗口继续。
- WebView2 smoke 窗口本身没有 900x760 CLI 参数。900x760 布局使用同一份 built React dist 通过 headless Edge/CDP 做静态截图和布局审计；真实 live action 由 WebView2 smoke 覆盖。

## 5. 侧边栏固定实现和验证

实现方式：

- `.app-shell` 使用 `height: 100dvh`、`overflow: hidden`。
- `.sidebar` 位于 shell 左侧，使用 `height: 100dvh` 和固定布局，不进入 `.page-viewport`。
- `.workspace` / `.content-region` / `.page-viewport` 设置 `min-height: 0`。
- `.page-viewport` 设置 `overflow-y: auto`，右侧内容独立滚动。
- 900px 窄布局没有把 sidebar 改成顶部栏，而是收缩为左侧图标栏。

自动化证据：

- 审计 JSON：`artifacts\webview2-ui-redesign-20260522\browser-layout-audit.json`
- Reports 滚动前：`artifacts\webview2-ui-redesign-20260522\scroll-reports-before.png`
- Reports 滚动后：`artifacts\webview2-ui-redesign-20260522\scroll-reports-after.png`
- Settings 滚动前：`artifacts\webview2-ui-redesign-20260522\scroll-settings-before.png`
- Settings 滚动后：`artifacts\webview2-ui-redesign-20260522\scroll-settings-after.png`

坐标检查：

- Reports before：`brandTop=16, navTop=202, statusTop=708, scrollTop=0`
- Reports after：`brandTop=16, navTop=202, statusTop=708, scrollTop=678`
- Settings before：`brandTop=16, navTop=264, statusTop=708, scrollTop=0`
- Settings after：`brandTop=16, navTop=264, statusTop=708, scrollTop=860`
- 两组 `fixed=true`。

## 6. 1280x720 / 900x760 截图路径

1280x720：

- Overview：`artifacts\webview2-ui-redesign-20260522\browser-overview-1280x720.png`
- Targets：`artifacts\webview2-ui-redesign-20260522\browser-targets-1280x720.png`
- Reports：`artifacts\webview2-ui-redesign-20260522\browser-reports-1280x720.png`
- Settings：`artifacts\webview2-ui-redesign-20260522\browser-settings-1280x720.png`
- About：`artifacts\webview2-ui-redesign-20260522\browser-about-1280x720.png`

900x760：

- Overview：`artifacts\webview2-ui-redesign-20260522\browser-overview-900x760.png`
- Targets：`artifacts\webview2-ui-redesign-20260522\browser-targets-900x760.png`
- Reports：`artifacts\webview2-ui-redesign-20260522\browser-reports-900x760.png`
- Settings：`artifacts\webview2-ui-redesign-20260522\browser-settings-900x760.png`

布局审计摘要：

- 9 个页面/尺寸组合均 `bodyScrollX=false`、`docScrollX=false`。
- 9 个页面/尺寸组合均 `overflowingCount=0`。
- 9 个页面/尺寸组合均 `badTextCount=0`。
- Sidebar 高度在 1280x720 为 720，在 900x760 为 760。

## 7. WebView2 live / reduced-motion 验证结果

Live smoke：

- 命令：`.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-ui-redesign-20260522\webview2-live-smoke-final.json --web-ui-screenshot artifacts\webview2-ui-redesign-20260522\webview2-live-smoke-final.png --web-ui-timeout-ms 120000`
- 结果：PASS。
- `success=true`
- `smokePayload.success=true`
- `reportLiveActionSmoke.success=true`
- `reportOpenClickOk=true`
- `reportOpenDirectoryClickOk=true`
- `reportRegenerateClickAccepted=true`
- `reportRegenerateClickCompleted=true`
- `bridgeExtensionSmoke.success=true`
- `reportsListOk=true`
- `targetsGetOk=true`
- `diagnosticsCompleted=true`
- `monitorStarted=true`
- `monitorStopped=true`
- `configSaveSuccessObserved=true`
- 证据：`artifacts\webview2-ui-redesign-20260522\webview2-live-smoke-final.json`
- 截图：`artifacts\webview2-ui-redesign-20260522\webview2-live-smoke-final.png`

Reduced-motion smoke：

- 命令：`.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-ui-redesign-20260522\webview2-reduced-motion-smoke-final.json --web-ui-screenshot artifacts\webview2-ui-redesign-20260522\webview2-reduced-motion-smoke-final.png --web-ui-timeout-ms 120000`
- 结果：PASS。
- `success=true`
- `smokePayload.reducedMotion=true`
- `smokePayload.success=true`
- `reportLiveActionSmoke.success=true`
- `bridgeExtensionSmoke.success=true`
- `reportsListOk=true`
- `targetsGetOk=true`
- `diagnosticsCompleted=true`
- `monitorStarted=true`
- `monitorStopped=true`
- `configSaveSuccessObserved=true`
- 证据：`artifacts\webview2-ui-redesign-20260522\webview2-reduced-motion-smoke-final.json`
- 截图：`artifacts\webview2-ui-redesign-20260522\webview2-reduced-motion-smoke-final.png`

## 8. 命令验证结果

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`：PASS。
  - `npm ci` 成功，安装 110 个包。
  - `tsc --noEmit` 成功。
  - Vitest：4 个测试文件、20 个测试全部通过。
  - Vite build 成功。
- TDD 修复过程：
  - 先补 `src/frontend/src/uiDesignContract.test.ts`，验证 Reports 缺少隐藏 smoke 操作入口时测试失败。
  - 再补 `src/frontend/src/pages/ReportsPage.tsx` 隐藏 action。
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 test`：PASS，4 files / 20 tests。
- `git diff --check`：PASS，退出码 0。输出只有 Git 的 LF/CRLF 提示，没有空白错误。
- 浏览器布局证据刷新命令：使用 bundled Node 执行 `artifacts\webview2-ui-redesign-20260522\capture-browser-evidence.cjs`，生成 9 张页面截图和 2 组滚动验证截图，退出码 0。

## 9. 残留进程检查

已清理本轮证据采集专用进程：

- `artifacts\webview2-ui-redesign-20260522\static-server.cjs`
- `--remote-debugging-port=9224`
- `artifacts\webview2-ui-redesign-20260522\edge-cdp-profile`

最终检查：

- `webview2-ui-redesign-20260522` / `--remote-debugging-port=9224` 相关进程：无残留。
- `FrameScopeMonitor`、`FrameScopeProcessSampler`、`FrameScopeSystemSampler`、`FrameScopeReportGenerator`、`PresentMon`：无残留。

未清理用户自己的普通 Edge / Codex Node 进程。

## 10. 是否建议交给 UI 动画窗口继续

建议交给 UI 动画窗口继续。

当前 UI 信息架构、布局、文案、视觉收束、sidebar 固定和真实 WebView2 action 验证已通过。本轮按要求没有做动画专项大改。后续动画窗口可以在这个稳定 shell 基线上处理路由动效、列表状态切换、按钮反馈、reduced-motion 细节和 WebView2 帧稳定性，但需要继续保持 C# bridge、后端采样、报告生成和 diagnostics 语义不变。
