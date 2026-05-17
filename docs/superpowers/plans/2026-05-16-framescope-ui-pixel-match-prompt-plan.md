# FrameScope UI Pixel Match Prompt Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework the FrameScope Monitor WinForms UI so the rendered screenshots match the five supplied 2026-05-16 reference images as closely as WinForms and GDI+ allow.

**Architecture:** Treat the reference images as the only visual source of truth. Do not use the previous `20260516-fix-*.png` screenshots as the target; use them only to identify mismatches that must be corrected. Keep backend, watcher, reporting data, GameLite, WMI, and monitor-session logic out of scope.

**Tech Stack:** C# WinForms, existing FrameScope UI partial files, existing screenshot harness, existing build/test scripts.

---

## Reference Images

- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (1).png`: overview reference.
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (2).png`: settings reference.
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (3).png`: monitoring targets reference.
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (4).png`: reports reference.
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_38_46.png`: about reference.

## Current Mismatch Summary

These are observed mismatches between the reference images and the current `artifacts\20260516-fix-*.png` screenshots. The downstream UI implementation must use this list as the first rework checklist.

- Shared shell: current content feels flatter and more rectangular than the reference. The reference has a brighter cyan glass border, more visible outer glow, rounder cards, stronger title-bar integration, and more generous spacing around the sidebar and content bands.
- Sidebar: current icons, nav row sizes, service status card, and bottom spacing are close but not identical. Active nav must match the reference: cyan border, blue inner fill, left glow bar, icon/label color, and row height.
- Top status cards: current cards are smaller/flatter and icon sizing differs. Match the reference card dimensions, icon scale, text vertical alignment, and green/cyan border treatment.
- Overview: current capture-chain card added a multi-step icon flow that is not visible in the reference overview. The reference overview has text-led capture-chain content and a compact monitored-games list with an output-directory button. Do not invent extra pipeline graphics on overview unless they are visible in the reference.
- Settings: current settings page is the largest mismatch. The reference uses standard small square blue checkboxes next to labels. The current long horizontal checkbox rows are wrong and must be removed. The reference also has a simpler form layout, different card proportions, and a right column of three cards.
- Targets: current right-column settings still uses long row-like checkbox controls. The reference uses compact checkboxes and a cleaner right-side card structure. The target table, input row, buttons, hint row, and right-column cards must be re-spaced to match the reference.
- Reports: current reports table is too dense and too much like a plain data grid. The reference uses a card-contained report center, three summary stat cards, a separate report-list table with clearer row spacing, and right-side summary/action/export cards.
- About: current page is closer, but the reference has larger left feature check circles, different text block width, stronger product-logo composition on the right, and tighter developer/contact card layout.
- Live: there is no supplied live reference. Only the shared shell, cards, typography, buttons, progress card, and visual language should be aligned. Do not claim live is pixel-matched to a missing reference.

## Non-Negotiable Pixel-Match Rules

- The reference images are the acceptance target. "Close enough" from the previous implementation is not acceptable.
- Before editing, open each reference image and the current matching `artifacts\20260516-fix-*.png` screenshot side by side. Create a page-by-page mismatch checklist.
- Do not introduce a new style because it looks better in code. If a shape, control, card, icon, or layout is not in the reference, remove it or justify it as required by existing real logic.
- Do not make checkboxes into full-width blue row buttons. Settings and Targets reference images show small square checkbox indicators with adjacent labels.
- Do not flatten report rows into a plain native grid look. The reports reference has a designed card/table hybrid with summary cards above and right-side action panels.
- Do not hide real controls to make a static mockup. Buttons, report links, config controls, target rows, status text, logs, and chart panels must keep real wiring or an explicit demo/test path.
- "WinForms limitation" is not a first answer. First try owner-drawn controls, custom painting, rounded panels, and existing UI helper APIs. Only list an item as technically approximate after attempting a feasible WinForms/GDI+ implementation.

## Downstream Prompt v2

Copy everything in this block into the downstream UI implementation conversation.

```text
/goal FrameScope Monitor UI pixel-match rework: make the WinForms UI screenshots match the five 2026-05-16 reference images as closely as WinForms/GDI+ allows, page by page, with screenshot evidence, without breaking real monitoring/report/config logic, and do not finish until build, tests, diff checks, and visual comparison are complete.

你是 FrameScope Monitor 的 UI 视觉实现对话框。本轮不是“做得更好看”，而是按用户提供的 5 张参考图做像素级贴近返工。参考图是唯一视觉标准；上一轮 `artifacts\20260516-fix-*.png` 只能作为反例和差异来源，不能作为验收目标。

项目路径：
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d

先执行 goal：
如果当前 Codex 环境支持 `/goal`，必须保留本提示词第一行的 `/goal ...` 目标。若命令格式略有不同，只允许调整命令语法，不允许删除目标内容。

必须先读取：
1. AGENTS.md
2. docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Role.md
3. docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Worklog.md
4. docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md
5. docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md
6. docs\FrameScopeMonitor-Project-Overview.md
7. docs\modules\software-ui.md
8. docs\modules\ui-interactions.md
9. docs\FrameScopeMonitor-design-system.md
10. docs\FrameScopeMonitor-reference-ui-plan.md
11. docs\superpowers\plans\2026-05-16-framescope-ui-reference-prompt-plan.md
12. docs\superpowers\plans\2026-05-16-framescope-ui-pixel-match-prompt-plan.md
13. docs\implementation-reports\2026-05-16-framescope-ui-design-implementation-report.md，如果存在

必须调用 skills：
- ui-ux-pro-max
- ckm:design-system
- design-review
- review
- health
- verification-before-completion

参考图片：
- C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (1).png
- C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (2).png
- C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (3).png
- C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (4).png
- C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_38_46.png

当前截图反例：
- artifacts\20260516-fix-overview.png
- artifacts\20260516-fix-settings.png
- artifacts\20260516-fix-targets.png
- artifacts\20260516-fix-reports.png
- artifacts\20260516-fix-about.png
- artifacts\20260516-fix-live.png

阶段 0：先做视觉差异清单，不要直接改代码
1. 逐张打开参考图和当前截图，对照 overview、settings、targets、reports、about。
2. 建立页面差异清单，至少覆盖：窗口比例、sidebar 宽度、主内容起点、标题位置、顶部状态卡尺寸、卡片宽高、卡片圆角、边框亮度、阴影/发光、文字字号、按钮高度、checkbox 形态、表格行高、右侧栏卡片比例、底部报告卡高度。
3. Live 没有参考图，只列共享 shell/卡片/按钮/底部报告卡需要同步的项目。
4. 差异清单写入最终工作报告，作为返工依据。

阶段 1：共享 shell 像素贴近
按参考图修正：
- 深色窗口背景和顶部标题栏。
- 左侧 sidebar 的宽度、内边距、logo 大小、产品名排版、导航项高度、图标大小、active nav cyan 边框/蓝色填充/左侧发光条。
- sidebar 底部服务状态卡的高度、分隔线、绿色状态点、版本号位置。
- 主内容区起点、页面标题字号、中文副标题位置。
- 右上三张状态卡的尺寸、间距、图标大小、文字对齐、cyan/green 边框。
- 底部报告生成卡的高度、左右分栏、绿色进度条高度和圆角、按钮位置、状态文本位置。

阶段 2：Overview 按参考图返工
必须匹配参考图 `(1)`：
- 顶部 5 张指标卡的数量、宽度、高度、间距、字号和颜色。
- 中间左侧“捕获链流程”是文字/状态型卡片，不要使用参考图没有的长流程图占满区域。
- 中间右侧“受监控游戏（6）”列表和“打开输出目录”按钮位置要接近参考图。
- 底部四张小卡：最近捕获状态、最近报告、输出目录、快速操作。
- 输出目录卡使用参考图的单行路径展示，不要换成过度压缩的 UI。

阶段 3：Settings 按参考图返工
必须匹配参考图 `(2)`：
- 移除上一轮的长条蓝色 checkbox 行。参考图是小方形 checkbox + 文字 label。
- 采样间隔、保留天数、最大 MB、数据目录、选择目录按钮的位置和比例要接近参考图。
- 左侧主设置卡占内容区约 60%，右侧是配置摘要、目标状态、捕获链状态三张卡。
- 右侧卡片的标题图标、文本行距、绿色状态文字和链路文本要贴近参考图。
- 保留真实配置写回逻辑，不要把 checkbox 变成静态 label。

阶段 4：Targets 按参考图返工
必须匹配参考图 `(3)`：
- 目标表格需要深色表头、透明深蓝行、蓝色细网格线、合适行高、参考图风格的 checkbox。
- 表格列、列名、列宽、滚动条位置尽量匹配参考图。
- 下方输入框、刷新/添加/保存/启动/停止按钮按参考图排布。
- 右侧三张卡：捕获链状态、报告、设置，按参考图卡片高度和按钮分组重排。
- 右侧设置里的 checkbox 也必须是小方形 checkbox + label，不允许长条行按钮。
- 如果 DataGridView screenshot harness 卡住，只停止 exact `FrameScopeMonitor.exe --ui-screenshot ... targets ...` 进程，不要留下残留进程，也不要误判主程序失败。

阶段 5：Reports 按参考图返工
必须匹配参考图 `(4)`：
- 报告中心主卡顶部有标题、说明、三张摘要卡。
- 报告列表表格要更接近参考图：列距、行高、状态绿点/图标、打开按钮、表格边线、卡片边框。
- 右侧必须是报告摘要、快速操作、导出选项三张卡，按钮堆叠和文案贴近参考图。
- 不要改报告数据生成、打开、导出、data.js 或 Html.Scripts.cs。

阶段 6：About 按参考图返工
必须匹配 `00_38_46.png`：
- 大卡左右分区比例、左侧说明文字宽度、绿色圆形 check 图标、右侧大 logo/产品名/版本号位置要贴近。
- 下方开发者和联系方式两张卡的宽度、高度、间距、图标样式和文字位置要贴近。

阶段 7：Live 只同步共享视觉
Live 没有参考图。本轮只要求：
- 共享 shell、顶部状态卡、底部报告卡、卡片圆角/边框/发光、按钮风格和字体与参考图系统一致。
- 不改 `PageLive.Lifecycle.cs` 和 `PageLive.Log.cs`。
- 不改进入 live 启动刷新、离开 live 停止刷新的语义。

允许修改文件：
- src\ui\FrameScopeUiTheme.cs
- src\ui\FrameScopeRoundedDrawing.cs
- src\ui\FrameScopePanels.cs
- src\ui\FrameScopeButtons.cs
- src\ui\FrameScopeStatusControls.cs
- src\ui\FrameScopeLiveChart.cs
- src\ui\FrameScopeReferenceSidebar.cs
- src\ui\FrameScopeReferenceSidebar.Navigation.cs
- src\ui\FrameScopeReferenceSidebar.Drawing.cs
- src\ui\FrameScopeReferenceSidebar.CompactDrawing.cs
- src\ui\FrameScopeReferenceSidebar.ReferenceDrawing.cs
- src\ui\FrameScopeReferenceSidebar.LogoDrawing.cs
- src\app\FrameScopeNativeMonitor.UiShell.cs
- src\app\FrameScopeNativeMonitor.UiVisualHelpers.cs
- src\app\FrameScopeNativeMonitor.UiVisualCards.cs
- src\app\FrameScopeNativeMonitor.UiVisualSections.cs
- src\app\FrameScopeNativeMonitor.UiVisualButtons.cs
- src\app\FrameScopeNativeMonitor.UiReportProgress.cs
- src\app\FrameScopeNativeMonitor.UiScreenshots.cs
- src\app\FrameScopeNativeMonitor.UiStatusDisplay.cs
- src\app\FrameScopeNativeMonitor.PageOverview.cs
- src\app\FrameScopeNativeMonitor.PageSettings.cs
- src\app\FrameScopeNativeMonitor.PageLive.Layout.cs
- src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs
- src\app\FrameScopeNativeMonitor.PageTargets.Grid.cs
- src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs
- src\app\FrameScopeNativeMonitor.PageAbout.cs
- src\ui\FrameScopeReportPage.cs
- src\ui\FrameScopeReportPage.Layout.cs

仅当本轮明确包含生成的 HTML 报告视觉时，才允许修改：
- src\reporting\FrameScopeReportGenerator.Html.Styles.cs
- src\reporting\FrameScopeReportGenerator.Html.Sections.cs
- src\reporting\FrameScopeReportGenerator.Html.Layout.cs

禁止修改文件：
- build.ps1，除非新增/删除 C# 文件且你独占执行并说明原因
- tests\
- scripts\lightweight\
- packaging\
- src\app\FrameScopeNativeMonitor.cs
- src\app\FrameScopeNativeMonitor.UiRouting.cs，除非出现编译阻塞且只做最小修复
- src\app\FrameScopeNativeMonitor.UiWatcherControls.cs
- src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs
- src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs
- src\app\FrameScopeNativeMonitor.PageLive.Log.cs
- src\ui\FrameScopeReportPage.Actions.cs
- src\ui\FrameScopeReportPage.Detail.cs
- src\ui\FrameScopeUiState.cs
- src\ui\FrameScopeLiveData.cs
- src\ui\FrameScopeLiveData.Csv.cs
- src\app\FrameScopeNativeMonitor.Watcher.cs
- src\app\FrameScopeNativeMonitor.MonitorSession*.cs
- src\app\FrameScopeNativeMonitor.ReportOrchestration*.cs
- src\app\FrameScopeNativeMonitor.ReportOpen*.cs
- src\app\FrameScopeNativeMonitor.ReportStatus.cs
- src\core\
- src\monitoring\
- src\diagnostics\
- src\reporting\FrameScopeReportGenerator.cs
- src\reporting\FrameScopeReportGenerator.Html.Scripts.cs
- WMI trigger / GameLite / SGuard 相关文件

不允许做：
- 不允许用静态假界面替代真实 UI。
- 不允许为了视觉对齐断开按钮、报告入口、配置保存、监控启动/停止、日志、状态、图表。
- 不允许把参考图没有的风格当成优化加入。
- 不允许把 Settings/Targets checkbox 做成长条蓝色行按钮。
- 不允许只说“WinForms 做不到”就结束。必须先尝试自绘或现有 helper。
- 不允许改后端监控、报告数据结构、GameLite、WMI、SGuard。

截图要求：
每轮关键改动后至少生成一次最终截图，最终必须输出：
- artifacts\YYYYMMDD-pixel-overview.png
- artifacts\YYYYMMDD-pixel-settings.png
- artifacts\YYYYMMDD-pixel-targets.png
- artifacts\YYYYMMDD-pixel-reports.png
- artifacts\YYYYMMDD-pixel-about.png
- artifacts\YYYYMMDD-pixel-live.png

最终视觉验收要求：
- 对 overview/settings/targets/reports/about 逐页给出“参考图一致性检查表”。
- 每页列出：已对齐项、仍不一致项、原因、是否属于 WinForms 技术限制。
- 如果仍不一致，不能直接写“完成”；必须说明剩余差异和下一步具体文件。
- Live 只能写“共享视觉已对齐”，不能写“和参考图一模一样”，因为用户没有提供 live 参考图。

验证命令必须全部运行：
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeUiStateTests.exe
.\tests\FrameScopeReportProgressTests.exe
node .\tests\chart-sampling-tests.js
"C:\Program Files\Git\cmd\git.exe" diff --check
```

如果默认 `node.exe` 是 WindowsApps 并返回 `Access is denied`，使用 Codex runtime Node 放到 PATH 前面后再运行 `node .\tests\chart-sampling-tests.js`，并在最终报告写明。

如果改了生成的 HTML 报告视觉，还必须：
- 跑 stable simulator 或直接生成报告。
- 确认 HTML/data.js/manifest 存在。
- Edge headless 打开报告并截图。
- 做截图非空像素检查。
- 确认不破坏 chart canvas、gauges、process rows、summary rows、data include、chart sampling script。
- 如果碰到 `Html.Scripts.cs`，必须跑 `node .\tests\chart-sampling-tests.js`。

真实 PUBG 说明：
本轮 UI 视觉返工不要求真实 PUBG 验证；但不能破坏 simulator、报告生成链路、报告打开入口、配置读写、watcher start/stop 的现有行为。

最终输出格式：
1. 当前结论：是否已经按参考图完成像素贴近返工。
2. 已读取的文档和已调用 skills。
3. 修改文件清单。
4. 每页差异清单和修复说明。
5. 每页截图路径。
6. 每页参考图一致性自评分：overview/settings/targets/reports/about，必须说明扣分原因。
7. 真实逻辑连接检查：按钮、状态、报告、配置、监控、日志、图表是否仍接真实逻辑。
8. 验证命令和 PASS/FAIL 结果。
9. 未完成或无法像素级一致的项目，以及具体技术原因。
10. Goal 完成状态和耗时记录。
```

## Strict File Boundary Summary

Allowed for this downstream pixel-match pass:

- UI theme, drawing, panels, buttons, status controls, live chart, reference sidebar visual files.
- UI shell and visual helper files.
- Overview/settings/live layout/targets layout/targets grid/targets actions/about/report-page layout files.
- Screenshot harness only for visual verification reliability.

Forbidden unless explicitly justified as a build blocker:

- Backend monitoring.
- Capture/session/report orchestration.
- Report data and report scripts.
- UI watcher controls and live lifecycle/log files.
- Build/test/package scripts.
- GameLite, WMI, SGuard files.

## Verification For This Prompt Plan

This file is prompt-coordination work only. The downstream implementation must do the build, tests, screenshots, and visual comparison described above.
