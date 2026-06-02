# FrameScope Monitor WebView2 Motion Implementation Report

日期：2026-05-23
角色：FrameScope Monitor WebView2 React UI 动画实现
源码根目录：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 1. 当前结论

PASS。

本轮在现有 WebView2 React UI redesign、UI polish、WebView2 smoke navigation diagnosis 改动基础上继续，实现了 motion design 文档要求的克制动画系统。页面切换保持同步 commit，不恢复旧页 exit，不使用整页 spinner，不让页面主体从低透明度进入；局部交互改为统一 token 驱动的 hover、press、busy、success、error、row highlight 反馈。

本轮未修改 `src/app/FrameScopeNativeMonitor.WebHost.cs`，未修改 C# bridge 语义、后端采样、报告生成、diagnostics 语义、GameLite、WMI、SGuard、`build.ps1`、packaging、README 或 GitHub Release。

## 2. 修改文件清单

本轮 motion 实现涉及：

- `src/frontend/src/theme/motion.ts`
- `src/frontend/src/theme/tokens.css`
- `src/frontend/src/layout/PageTransition.tsx`
- `src/frontend/src/components/Button.tsx`
- `src/frontend/src/components/ToolbarButton.tsx`
- `src/frontend/src/components/ChartShell.tsx`
- `src/frontend/src/components/InlineStatus.tsx`
- `src/frontend/src/components/components.css`
- `src/frontend/src/layout/layout.css`
- `src/frontend/src/pages/pages.css`
- `src/frontend/src/uiMotionContract.test.ts`
- `artifacts/webview2-motion-20260523/capture-motion-evidence.cjs`
- `docs/implementation-reports/2026-05-23-framescope-webview2-motion-implementation-report.md`

当前工作树还保留前置 UI redesign、polish、smoke navigation diagnosis 的未提交改动。本报告只记录本轮动画窗口的实现与验证。

## 3. 删除或约束的不合适动画

- 页面切换没有使用 `AnimatePresence` 旧页 exit。
- 页面主体没有 `initial opacity: 0`。
- 页面主体没有 `x`、`y`、`scale`、`filter/blur` 形式的 route transform。
- 普通页面切换没有整页 spinner，也没有 snapshot/crossfade。
- 删除按钮和工具按钮 hover 的 `y` 位移、横向位移和过重 press scale。
- sidebar hover 不再推动 nav item 位移，active indicator 保留为局部反馈，不阻塞页面切换。
- ChartShell 去掉 path draw、pathLength、delay/stagger 和 `motion.g` 演示式绘制。
- reduced motion 下禁用非必要 transform、spinner rotation 和 stagger，busy 状态改为静态图标加文本/颜色。

## 4. 新增或统一的 motion token

`src/frontend/src/theme/motion.ts` 统一为以下 token：

- `instant`：0 ms，用于 reduced motion 和同步替换。
- `micro`：90 ms，用于 hover、focus、轻量控件反馈。
- `state`：140 ms，用于 saving、saved、error、busy 等状态切换。
- `content`：180 ms，用于 row highlight、局部内容更新。
- `navCommit`：40 ms，用于同步页面 commit 后的极短稳定 paint，不改变主体透明度。
- `press`：80 ms，用于极轻 press compression。

`src/frontend/src/theme/tokens.css` 增加对应 CSS custom properties：

- `--fs-motion-instant`
- `--fs-motion-micro`
- `--fs-motion-state`
- `--fs-motion-content`
- `--fs-motion-nav`
- `--fs-motion-press`
- `--fs-ease-standard`
- `--fs-ease-settle`
- `--fs-ease-press`

`prefers-reduced-motion: reduce` 下，这些 duration 被折叠为 `0ms` 或 `1ms`，保留 focus、颜色和状态文字反馈。

## 5. 关键交互动效说明

页面切换：

- 点击 nav 后同步替换 active nav、topbar 和 page body。
- page body 从第一帧开始保持 `opacity: 1`。
- 不保留旧页面主体等待 exit。
- 不使用 slide、scale、blur、bounce 或 stagger。

按钮 hover / press / disabled：

- hover 使用颜色、边框、背景层级变化。
- press 只使用极轻 `scale: 0.992`，不造成位移。
- disabled 状态静态、可读，不依赖动画表达。

保存中 / 保存成功 / 保存失败：

- save button 立即进入 in-flight/disabled。
- inline status 在原位置切换 saving、saved、error。
- 保存失败不清空 draft，用户输入继续保留在页面中。

进程刷新：

- 目标列表和已有上下文保持可见。
- 刷新状态只附着在按钮、状态 chip 和结果区域。
- 新/更新行使用短暂背景或边缘 tint，不做整表 fade。

报告列表刷新：

- 旧 reports list 保持可见。
- 新数据直接替换。
- 新/更新行轻微高亮，不从下方进入，不做 stagger。

报告重新生成：

- row-local 状态覆盖 accepted、in-flight、completed、failed。
- 后端 progress event 只更新对应 report 行和详情状态，不遮挡整页。

诊断生成：

- 使用局部 status 与结果区域更新。
- 不阻塞页面、不覆盖主内容。

指标变化：

- 数字直接更新或只使用轻微局部高亮。
- 不做 demo 式 count-up、bounce 或图表重绘动画。

空状态和错误状态：

- 在原布局位置稳定出现。
- 不 shake、不 bounce、不推挤页面主体。

## 6. normal / reduced motion 连续帧路径

连续帧审计脚本：

```powershell
& 'C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' .\artifacts\webview2-motion-20260523\capture-motion-evidence.cjs
```

结果：PASS。

- 证据 JSON：`artifacts\webview2-motion-20260523\motion-audit.json`
- 总帧数：45
- 失败帧：0
- `reducedMedia=true`
- `uiPolish=true`
- `sidebar=true`

normal motion 连续帧：

- `overview -> targets`：5 帧，0 失败
- `targets -> reports`：5 帧，0 失败
- `reports -> settings`：5 帧，0 失败
- `reports -> about`：5 帧，0 失败
- `settings -> overview`：5 帧，0 失败

reduced motion 连续帧：

- `overview -> targets`：5 帧，0 失败
- `reports -> settings`：5 帧，0 失败
- `settings -> overview`：5 帧，0 失败

每帧审计项目：

- 只有一个可见 `[data-smoke-page]`
- active nav 与 page body 同步
- 页面标题/导航文本匹配目标页
- 页面内容区非空白
- page body 和 transition opacity 均不低于 0.99
- route transform 为 `none`
- route filter 为 `none`
- 无旧页面残留文本
- 无页面级 spinner
- sidebar 坐标保持固定

## 7. 快速连续导航验证结果

快速连续导航路径：

```text
overview -> targets -> reports -> settings -> about -> overview
```

结果：PASS。

- 采集快速切换帧：5
- 失败帧：0
- 未发现旧页残留、新旧混绘、空白内容区、低透明主体或 active nav 与页面不同步。

## 8. UI 小修验收是否仍通过

结果：PASS。

连续帧脚本中的 `uiPolish.pass=true`，覆盖：

- Overview 1280/900 只有一个 primary CTA。
- Targets 无修改时 `保存修改` 为 disabled secondary。
- Settings 无修改时 `保存修改` 为 disabled secondary。
- Reports hidden smoke 入口存在但不可见、不可 Tab 聚焦、`aria-hidden=true`。

## 9. WebView2 live / reduced-motion smoke 结果

Reduced-motion smoke：

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-motion-20260523\webview2-reduced-motion-smoke-final.json --web-ui-screenshot artifacts\webview2-motion-20260523\webview2-reduced-motion-smoke-final.png --web-ui-timeout-ms 120000
```

结果：PASS，退出码 0。

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=true`
- `errorsCount=0`
- `consoleCount=0`
- `smokePayload.success=true`
- `smokePayload.reducedMotion=true`
- `reportLiveActionSmoke.success=true`
- `bridgeExtensionSmoke.success=true`
- `diagnosticsCompleted=true`
- `monitorStarted=true`
- `monitorStopped=true`
- `configSaveSuccessObserved=true`

Live smoke：

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-motion-20260523\webview2-live-smoke-final.json --web-ui-screenshot artifacts\webview2-motion-20260523\webview2-live-smoke-final.png --web-ui-timeout-ms 120000
```

结果：PASS，退出码 0。

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=false`
- `errorsCount=0`
- `consoleCount=0`
- `smokePayload.success=true`
- `reportLiveActionSmoke.success=true`
- `bridgeExtensionSmoke.success=true`
- `diagnosticsCompleted=true`
- `monitorStarted=true`
- `monitorStopped=true`
- `configSaveSuccessObserved=true`

说明：曾有一次并行运行 smoke 时 reduced-motion JSON 显示 bridge/diagnostics 未完成，但页面加载、ready、React frontend、console/errors 均正常。按 smoke navigation diagnosis 报告字段分类，它不是导航/ready/资源加载或动画问题；串行重跑后 live 与 reduced-motion 均 PASS，本报告采用串行最终证据。

## 10. 命令验证结果

Frontend verify：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

结果：PASS。

- `npm ci` 成功，安装 110 个包。
- `tsc --noEmit` 成功。
- Vitest：5 个测试文件，25 个测试全部通过。
- `src/frontend/src/uiMotionContract.test.ts` 覆盖 motion contract。
- Vite production build 成功。
- 当前 build 产物：
  - `dist/assets/index-Cci9OgVZ.css`
  - `dist/assets/index-JeBY-Un3.js`

TDD 过程：

- 先新增 `src/frontend/src/uiMotionContract.test.ts`。
- 初次运行 `Run-Frontend.ps1 test` 失败，失败点包括旧 page variant 的 `y/scale`、缺少 token vocabulary、ToolbarButton hover/press 位移过重、ChartShell pathLength/delay/motion.g、InlineStatus 未使用 reduced-motion 友好的 busy class。
- 实现 motion 约束后，测试通过。

`git diff --check`：

- PASS，退出码 0。
- 输出只有 Git 的 LF/CRLF 提示，没有 whitespace error。

## 11. 残留进程检查

最终检查命令覆盖：

- `FrameScopeMonitor`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `PresentMon`
- 本轮 motion evidence 使用的 Node / Edge / CDP profile / `remote-debugging-port=9345`

结果：PASS。

- `InterestingCount=0`
- 未发现 FrameScope smoke 启动的 `FrameScopeMonitor.exe` 残留。
- 未发现 sampler、report generator、PresentMon 残留。
- 未发现 `webview2-motion-20260523`、`capture-motion-evidence`、`remote-debugging-port=9345` 相关 Node/Edge/CDP 残留。
- 仍存在系统 `msedgewebview2.exe`，命令行指向 `MicrosoftWindows.Client.CBS` / `SearchHost.exe`，属于 Windows SearchHost/CBS 的长期 WebView2，不是本轮 smoke 或 motion evidence 残留，未清理。

## 12. 是否建议交给测试员复测

建议交给测试员复测。

理由：本轮自动化已经覆盖前端 verify、motion contract、45 帧 continuous-frame audit、快速连续导航、WebView2 live smoke、WebView2 reduced-motion smoke 和 UI 小修验收。测试员复测重点建议放在真实 Windows 桌面观感上：快速点击侧边栏、保存失败 draft 保留、报告 regenerate 行内状态、诊断生成局部反馈、以及 reduced motion 系统设置下的体感一致性。
