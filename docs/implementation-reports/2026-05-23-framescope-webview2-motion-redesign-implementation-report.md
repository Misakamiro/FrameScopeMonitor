# FrameScope Monitor WebView2 Motion Redesign Implementation Report

日期：2026-05-23
源码根目录：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 当前结论

PASS。

本轮在新的 WebView2 React UI 基础上补齐了第二轮动效实现：Sidebar active/focus/hover 分层、Reports 更多菜单开合、Targets 查找进程的局部 busy 和结果更新反馈。页面切换继续保持同步 commit，没有恢复旧页 exit、整页 fade、page scale、page slide、blur 或 crossfade。

本轮没有修改 `src/app/FrameScopeNativeMonitor.WebHost.cs`、C# bridge 业务语义、后端采样、报告生成、diagnostics 语义、GameLite / WMI / SGuard、`build.ps1`、packaging、README 或 GitHub Release。当前工作树里仍有前序窗口留下的未提交 WebView2 / UI / smoke 相关改动，本报告只记录本轮动效二轮实现和验证。

## 修改文件清单

本轮产品源码和测试：

- `src/frontend/src/layout/SidebarNav.tsx`
- `src/frontend/src/layout/layout.css`
- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/pages/TargetsPage.tsx`
- `src/frontend/src/pages/pages.css`
- `src/frontend/src/uiMotionContract.test.ts`

本轮证据与报告：

- `artifacts/webview2-motion-redesign-20260523/capture-motion-redesign-evidence.cjs`
- `artifacts/webview2-motion-redesign-20260523/motion-redesign-audit.json`
- `artifacts/webview2-motion-redesign-20260523/*.png`
- `artifacts/webview2-motion-redesign-20260523/webview2-live-smoke.json`
- `artifacts/webview2-motion-redesign-20260523/webview2-reduced-motion-smoke.json`
- `docs/implementation-reports/2026-05-23-framescope-webview2-motion-redesign-implementation-report.md`

## Motion Token 与规则

本轮没有新增另一套 token，而是继续沿用当前前端已有的 motion token，并把新增动效都收敛到这些 token 上：

- `--fs-motion-micro`：hover、focus、轻量状态层变化。
- `--fs-motion-state`：菜单进入、inline 状态切换。
- `--fs-motion-content`：active indicator 移动、结果行短高亮。
- `--fs-motion-nav`：导航 rail / active 层级的局部反馈。
- `--fs-motion-press`：按钮按下反馈。
- `--fs-ease-standard`、`--fs-ease-settle`、`--fs-ease-press`：分别处理普通状态、短 settle、press。

`prefers-reduced-motion: reduce` 下，CSS token 折叠为 `0ms` 或 `1ms`；菜单和列表高亮禁用 transform/animation，仍保留颜色、focus outline 和文本状态反馈。

## Sidebar 动效处理

- `SidebarNav.tsx` 新增单一 `nav-active-indicator`，通过 `data-active-index` 和 `--fs-nav-active-index` 移动 active rail，不再让每个 nav item 各自竞争 active 动画。
- active 状态使用淡蓝底色、弱边框和独立 rail；hover 只用轻底色和文字色变化，不做位移，不抢 active。
- focus-visible 使用清晰 outline 和轻 inset shadow；当页面在 Reports、键盘焦点在 Targets 时，Reports 仍保留 active 背景和 rail，Targets 只显示 focus outline 与 hover/focus 背景，两者可以区分。
- 900x760 compact sidebar 保持 84px 左栏、48x48 导航目标、同一套 active / hover / focus 分层。
- Sidebar 自身保持 `overflow: hidden`，页面滚动只发生在右侧 viewport。

验证结果：

- normal：active=`reports`，focused=`targets`，active indicator 可见，sidebar overflow=`hidden`。
- compact 900x760：active=`reports`，focused=`settings`，active indicator 可见，sidebar overflow=`hidden`。

## Reports 更多菜单动效处理

- `ReportsPage.tsx` 新增 `closingReportMenuId` 和 `closeReportMenu()`，关闭时保留一个极短 closing 状态，CSS 完成退出后再卸载。
- 菜单打开使用 opacity + `translateY(-2px -> 0)`，`transform-origin: top right`，贴近触发行；关闭使用 opacity + `translateY(-1px)`，没有 bounce、blur、大 scale 或页面级遮罩。
- Esc 关闭后把焦点还给触发按钮；Tab 关闭菜单并允许焦点继续流动，不残留打开态。
- 菜单 item hover / focus 使用背景色变化，不使用重边框跳动。
- reduced motion 下菜单直接出现/消失，`animation: none`，transform 为 `none`。

验证结果：

- normal open/close frame：菜单锚定当前 report 行，打开第 2 帧稳定为 `opacity=1`、`transform=none`；关闭后 `menuOpen=false` 且焦点返回。
- reduced open/close frame：打开帧 `opacity=1`、`transform=none`；关闭后直接卸载，无位置动效残留。

## Targets 查找进程动效处理

- `TargetsPage.tsx` 新增 `lastProcessRefreshKey` / `settledProcessRefreshKey`，在结果签名变化时只给新结果行短暂 `process-result-row--updated`。
- Enter 查找和按钮查找走同一条 `refreshProcesses(query)` 路径，查找中按钮显示“正在查找”。
- 查找中保持旧结果区域可见，结果列表加 `process-result-list--refreshing` 轻 tint，不清空整块区域。
- 新结果出现时只对更新行做短暂 highlight，不做整表 fade；settled 后移除高亮 class。
- 空结果/失败状态通过原位 status 文本和稳定 empty/error 文案出现，不使用 shake 或 bounce。

验证结果：

- before enter：结果数 4，旧结果可见。
- busy：按钮为“正在查找”，status 为“正在查找...”，`oldResultsPreserved=true`。
- result updated：结果数 1，`updatedRowCount=1`。
- settled：结果数 1，`updatedRowCount=0`。

## Buttons / Inline Status / Long Task 动效

- Buttons 和 icon buttons 继续使用统一 token 驱动 hover / press / disabled；hover 以色层、边框、阴影轻变化为主，不上移、不横移。
- press 只保留极轻压缩和色层变化；focus-visible 使用统一 focus ring。
- 保存设置、保存目标、刷新进程、报告重新生成、诊断生成都使用局部按钮文本和 `InlineStatus` / row-local 状态反馈，没有整页 spinner。
- 保存失败文案明确说明输入未丢失；Targets 和 Settings 的 draft 都保留在页面中。
- 报告重新生成和诊断生成继续绑定到当前 report 行或详情区，状态贴近触发控件，不遮挡页面主体。

## 页面切换

页面切换继续保持同步替换：

- 第一帧目标页面内容完整可读。
- `pageOpacity=1`，`transitionOpacity=1`。
- `transitionTransform=none`，`transitionFilter=none`。
- 无旧页主体 + 新 header/nav 混绘。
- 无空白主体、无低透明主体、无 page spinner。
- 快速连续导航也保持 active nav 和 body 同步。

## Reduced Motion

reduced-motion audit 结果：

- `mediaMatches=true`
- `motionMicro=1ms`
- `motionState=1ms`
- `motionContent=1ms`
- `motionPress=1ms`
- `busyAnimation=""`
- `menuAnimation=""`
- `pageTransform=none`
- `pageOpacity=1`

也就是说 reduced motion 下不做非必要 transform、spinner rotation、stagger 或页面转场；保留颜色、focus 和文字状态反馈。

## Normal / Reduced Motion 连续帧路径

连续帧证据 JSON：

- `artifacts/webview2-motion-redesign-20260523/motion-redesign-audit.json`

normal motion 连续帧：

- `overview -> targets`：`artifacts/webview2-motion-redesign-20260523/normal-overview-to-targets-01.png` 到 `normal-overview-to-targets-05.png`
- `targets -> reports`：`artifacts/webview2-motion-redesign-20260523/normal-targets-to-reports-01.png` 到 `normal-targets-to-reports-05.png`
- `reports -> settings`：`artifacts/webview2-motion-redesign-20260523/normal-reports-to-settings-01.png` 到 `normal-reports-to-settings-05.png`
- `reports -> about`：`artifacts/webview2-motion-redesign-20260523/normal-reports-to-about-01.png` 到 `normal-reports-to-about-05.png`
- `settings -> overview`：`artifacts/webview2-motion-redesign-20260523/normal-settings-to-overview-01.png` 到 `normal-settings-to-overview-05.png`

reduced motion 连续帧：

- `overview -> targets`：`artifacts/webview2-motion-redesign-20260523/reduced-overview-to-targets-01.png` 到 `reduced-overview-to-targets-05.png`
- `reports -> settings`：`artifacts/webview2-motion-redesign-20260523/reduced-reports-to-settings-01.png` 到 `reduced-reports-to-settings-05.png`
- `settings -> overview`：`artifacts/webview2-motion-redesign-20260523/reduced-settings-to-overview-01.png` 到 `reduced-settings-to-overview-05.png`

快速连续导航：

- `overview -> targets -> reports -> settings -> about -> overview`
- 证据：`rapid-overview-to-targets-01.png`、`rapid-targets-to-reports-02.png`、`rapid-reports-to-settings-03.png`、`rapid-settings-to-about-04.png`、`rapid-about-to-overview-05.png`

audit 结果：

- `success=true`
- `frameCount=45`
- `failedFrames=[]`
- `checks.noOldPageResidue=true`
- `checks.noMixedNavBody=true`
- `checks.noBlankBody=true`
- `checks.noLowOpacityBody=true`
- `checks.noRouteTransform=true`
- `checks.sidebarNotScrolling=true`
- `checks.activeFocusDistinct=true`
- `checks.reducedMotionStatic=true`

## Reports Menu 连续帧路径

normal：

- 打开：`normal-reports-menu-open-01.png` 到 `normal-reports-menu-open-04.png`
- 关闭：`normal-reports-menu-close-01.png` 到 `normal-reports-menu-close-04.png`

reduced motion：

- 打开：`reduced-reports-menu-open-01.png` 到 `reduced-reports-menu-open-04.png`
- 关闭：`reduced-reports-menu-close-01.png` 到 `reduced-reports-menu-close-04.png`

audit 结果：

- `reportsMenu.normal.pass=true`
- `reportsMenu.reduced.pass=true`
- normal menu anchored to row：true
- reduced menu anchored to row：true
- close 后焦点返回：true

## Targets 查找进程截图路径

- 查找前：`artifacts/webview2-motion-redesign-20260523/targets-search-before-enter.png`
- Enter 后 busy：`artifacts/webview2-motion-redesign-20260523/targets-search-enter-busy.png`
- 结果更新：`artifacts/webview2-motion-redesign-20260523/targets-search-result-updated.png`
- settled：`artifacts/webview2-motion-redesign-20260523/targets-search-result-settled.png`

## Sidebar 截图路径

- normal active / hover / focus：`artifacts/webview2-motion-redesign-20260523/sidebar-normal-active-hover-focus-1280x720.png`
- compact 900x760 active / hover / focus：`artifacts/webview2-motion-redesign-20260523/sidebar-compact-active-hover-focus-900x760.png`

active / focus 不混淆验证：

- normal：active page 为 Reports，keyboard focus 在 Targets；Reports 保持 active background + rail，Targets 显示 focus outline。
- compact：active page 为 Reports，keyboard focus 在 Settings；Reports 和 Settings 视觉层级仍可区分。
- audit：`checks.activeFocusDistinct=true`。

## WebView2 Live / Reduced-Motion Smoke 结果

Live smoke 命令：

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-motion-redesign-20260523\webview2-live-smoke.json --web-ui-screenshot artifacts\webview2-motion-redesign-20260523\webview2-live-smoke.png --web-ui-timeout-ms 120000
```

结果：PASS，退出码 0。

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=false`
- `errors=0`
- `console=0`
- `reportLiveActionSmoke=true`
- `bridgeExtensionSmoke=true`

Reduced-motion smoke 命令：

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-motion-redesign-20260523\webview2-reduced-motion-smoke.json --web-ui-screenshot artifacts\webview2-motion-redesign-20260523\webview2-reduced-motion-smoke.png --web-ui-timeout-ms 120000
```

结果：PASS，退出码 0。

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=true`
- `errors=0`
- `console=0`
- `reportLiveActionSmoke=true`
- `bridgeExtensionSmoke=true`

## 命令验证结果

TDD red 证明：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 test
```

新增 `uiMotionContract.test.ts` 后首次运行失败，失败点正好覆盖 Sidebar shared active indicator、Reports menu anchored close motion、Targets process search local feedback。实现后同一测试通过。

Frontend verify：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

结果：PASS。

- `npm ci` 成功，安装 110 个包。
- `tsc --noEmit` 成功。
- Vitest：5 个测试文件、35 个测试全部通过。
- Vite production build 成功。
- 当前构建产物：`dist/assets/index-Bwr_RKdJ.css`、`dist/assets/index-DrdBxqGR.js`。

CDP 连续帧 / menu / targets / sidebar audit：

```powershell
& 'C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' .\artifacts\webview2-motion-redesign-20260523\capture-motion-redesign-evidence.cjs
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

`git diff --check`：

```powershell
& 'C:\Program Files\Git\cmd\git.exe' -c safe.directory='C:/Users/misakamiro/Documents/Codex/2026-05-02/files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d' -C 'C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d' diff --check
```

结果：PASS，退出码 0。输出只有 Git 的 LF/CRLF 提醒，没有 whitespace error。

## 残留进程检查

检查范围：

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `PresentMon.exe`
- 本轮证据脚本相关 `node.exe` / `msedge.exe` / `msedgewebview2.exe`
- `webview2-motion-redesign-20260523`
- `capture-motion-redesign-evidence`
- `remote-debugging-port=9397`
- `127.0.0.1:4297`

结果：PASS。

- 匹配本轮 smoke / CDP / FrameScope 的残留进程：`[]`。
- 未清理 Windows SearchHost/CBS 自身长期持有的系统 `msedgewebview2.exe`，因为它不属于本轮启动进程。

## 是否建议交给测试员最终复测

建议交给测试员最终复测。

理由：自动化已经覆盖前端 verify、WebView2 live smoke、WebView2 reduced-motion smoke、45 帧连续导航、快速连续导航、Reports 更多菜单开合、Targets Enter 查找、Sidebar normal 和 900x760 active/focus 区分。测试员最终复测应重点看真实桌面体感：快速点 Sidebar、键盘 Tab/Shift+Tab、Reports 菜单 Esc/Tab、Targets 连续查找、保存失败输入保留、系统 reduced motion 设置下是否仍然克制稳定。
