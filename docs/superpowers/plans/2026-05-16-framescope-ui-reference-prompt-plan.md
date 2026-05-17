# FrameScope UI Reference Implementation Prompt Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the FrameScope Monitor WinForms UI visual pass from the five 2026-05-16 reference images while preserving real wiring and strict module boundaries.

**Architecture:** Treat the images as the visual source of truth for shell, overview, settings, targets, reports, and about pages. Keep interaction and backend logic in their existing focused files; visual work stays in UI theme, controls, shell visual helpers, page layout files, and report-page layout.

**Tech Stack:** C# WinForms, existing FrameScope UI partial files, existing build/test scripts, offscreen screenshot harness.

---

## Reference Images

- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (1).png`: overview page.
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (2).png`: settings page.
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (3).png`: monitoring targets page.
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (4).png`: reports page.
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_38_46.png`: about page.

## Explicit Visual Observations From Images

### Shared Shell

- Borderless-feeling dark Windows desktop app with a thin top title bar and small app icon/title at top left.
- Fixed left sidebar around 300px wide, deep navy glass panel, rounded outer border, cyan glow edge, product logo block near top.
- Sidebar navigation uses icon + Chinese label; active item has cyan border, blue fill/glow, and cyan text/icon.
- Sidebar bottom service card shows `服务状态`, green dot `运行中`, `版本`, and `v1.1.1`.
- Main content starts to the right of sidebar with large `FrameScope Monitor` title and Chinese subtitle.
- Top right always has three status cards: `监测器 / 就绪`, `已启用目标 / 6 已启用`, `软件状态 / 就绪`.
- Persistent bottom `报告生成` card appears on every shown page with green progress bar, green status text, right-side `打开报告目录`, and `报告状态：完成 100%`.

### Overview

- Row of five compact metric cards: enabled targets, capture chain state, latest report status, output directory state, diagnostics mode.
- Middle layout uses a wide `捕获链流程` card and a `受监控游戏（6）` card.
- Lower row uses smaller cards for recent capture, latest report, output directory, and quick actions.
- Quick actions emphasize primary blue `启动监测` and secondary dark `打开输出目录`.

### Settings

- Main settings card occupies left 60 percent of content, with checkboxes, combo/select field, numeric spinner-like inputs, data directory field, and `选择目录` button.
- Right column contains `配置摘要`, `目标状态`, and `捕获链状态` cards.
- Settings form uses generous row spacing and visible labels, not placeholder-only labels.
- Checkbox accent is bright blue; unchecked boxes are outlined, not native white WinForms default.

### Monitoring Targets

- Main card contains target table with dark transparent rows, blue grid lines, custom blue checked boxes, and vertical scrollbar.
- Table columns visible: `启用`, `游戏 / 软件`, `进程名`, `采样(ms)`, `自动打开报告`.
- Below table: process input, `刷新进程`, `添加进程`, `保存配置`, primary `启动监测`, red `停止监测`.
- Right column shows capture-chain status, report status mini cards, and settings summary with checkboxes.
- Hint row at bottom references process aliases and PUBG/TslGame behavior.

### Reports

- Main `报告中心` card contains three summary cards: latest report availability, generated report count, export format.
- Report list table has columns `报告名称`, `类型`, `状态`, `生成时间`, `操作`, with per-row `打开` buttons.
- Right column has `报告摘要`, `快速操作`, and `导出选项`.
- Quick actions are stacked buttons; primary action is bright blue.

### About

- Main about card is split into explanatory text/checklist on left and large logo/product/version block on right.
- Feature checklist uses green circular check icons.
- Lower cards show developer and contact information.
- About page still keeps shared top status cards and bottom report generation card.

## Required Design System

- UI type: APP UI, data-dense desktop monitoring dashboard.
- Background: near-black and deep blue-black, with subtle radial depth only.
- Surfaces: translucent deep blue cards, 1px cyan/blue border, inner/outer cyan glow, 12-16px radius.
- Accent colors: cyan for active/nav/icons, blue for primary action, green for ready/success, purple for diagnostics/standard mode, red for stop/error.
- Typography: Windows-safe Segoe UI; large page title, bold card title, compact body text, tabular/monospace numbers where practical.
- Icons: thin-line tech icons. No emoji, no decorative icon circles unrelated to function.
- Text language: Chinese for all functional UI. `FrameScope Monitor` may remain English as product name.

## Page Mapping

- Overview maps directly from image `(1)`.
- Settings maps directly from image `(2)`.
- Monitoring targets maps directly from image `(3)`.
- Reports maps directly from image `(4)`.
- About maps directly from image `00_38_46.png`.
- Live page has no direct new reference image; implement it by reusing the same shell, top cards, bottom report card, card style, chart style, and log panel style already defined by the images. Treat live chart layout as inferred from existing product requirements, not as picture-observed detail.

## Downstream Prompt

```text
/goal FrameScope Monitor 2026-05-16 UI reference implementation: strictly implement the WinForms visual UI from the five supplied reference images for shell, overview, settings, monitoring targets, reports, and about pages, keep all buttons/status/report/log entries wired to real logic or explicit demo/test paths, and finish only after build, tests, screenshots, and diff checks provide fresh evidence.

你是 FrameScope Monitor 的 UI 视觉实现对话框。你负责按 2026-05-16 五张参考图实现 WinForms UI 视觉，不负责后端、监控链路、报告数据逻辑、GameLite 或 WMI。

项目路径：
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d

必须先读取：
1. AGENTS.md
2. docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Role.md
3. docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Worklog.md
4. docs\FrameScopeMonitor-Project-Overview.md
5. docs\modules\software-ui.md
6. docs\modules\ui-interactions.md
7. docs\FrameScopeMonitor-design-system.md
8. docs\FrameScopeMonitor-reference-ui-plan.md
9. docs\FrameScopeMonitor-next-prompt.md
10. docs\superpowers\plans\2026-05-16-framescope-ui-reference-prompt-plan.md

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

任务范围：
1. 按参考图统一 shell：深色窗口、左侧导航、logo、active nav、服务状态卡、顶部三张状态卡、底部报告生成卡。
2. 按图实现/修正 overview、settings、targets、reports、about 页面视觉布局。
3. live 页面没有本轮参考图，必须沿用同一视觉系统，只能根据现有 live 功能做一致化布局。
4. 所有按钮、状态、报告入口、日志、图表必须接真实现有逻辑；无真实数据时显示空状态或明确 `演示数据`。
5. 不允许为了视觉效果删除监控、采样、报告、配置或诊断能力。

允许修改：
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
- src\ui\FrameScopeReportPage.Layout.cs

禁止修改：
- build.ps1，除非新增/删除 C# 文件且独占执行
- src\app\FrameScopeNativeMonitor.cs
- src\app\FrameScopeNativeMonitor.UiRouting.cs
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
- ..\gamelite-auto-lightweight\
- WMI trigger / GameLite / SGuard 相关文件

执行步骤：
1. 先审查当前 UI 文件和截图 harness，确认页面/组件职责。
2. 更新共享 token：背景、卡片、边框、发光、文本、状态色、按钮色、圆角、间距。
3. 更新 sidebar/logo/nav/service card，使五个页面共享同一左侧导航。
4. 更新顶部状态卡和底部报告生成卡，保持所有页面一致。
5. 按图逐页实现 overview、settings、targets、reports、about。
6. live 页面只做同视觉系统一致化，不改生命周期和日志行为。
7. 每完成一个页面，生成截图并和参考图人工对照：布局、中文、卡片、发光、按钮、文本是否一致。
8. 最后跑完整验证。

验证至少运行：
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeUiStateTests.exe
.\tests\FrameScopeReportProgressTests.exe
node .\tests\chart-sampling-tests.js
"C:\Program Files\Git\cmd\git.exe" diff --check

截图要求：
- overview 页面截图
- settings 页面截图
- live 页面截图
- reports 页面截图
- about 页面截图
- targets 页面截图也要尝试；如果 DataGridView screenshot harness 卡住，定位并停止 exact screenshot command line 对应进程，不能留下残留进程。

如果真实 PUBG 无法测试：
- 本轮 UI 视觉验证不要求真实 PUBG。
- 不能破坏 stable simulator、报告生成链路、报告打开入口、配置读写、watcher start/stop 现有行为。

最终汇报必须包含：
1. 改了哪些文件。
2. 每个页面按参考图实现了哪些结构。
3. 哪些地方是 WinForms 近似实现。
4. 所有按钮/状态是否仍连接真实逻辑。
5. 截图路径。
6. 测试命令和结果。
7. 未验证项和原因。
```

## Not In Scope

- Do not redesign beyond the reference images.
- Do not rewrite backend monitoring, capture planning, report data shape, or GameLite.
- Do not implement report HTML visual changes in this pass unless the user explicitly separates that as a report-template task.
- Do not update global progress files unless explicitly instructed.

## Plan Design Review Notes

- Classification: APP UI.
- Initial prompt completeness: 8/10. It covers visual source, pages, files, skills, non-goals, and verification.
- Main residual uncertainty: live page has no direct screenshot. It must be implemented by applying the shared visual language to existing live-page product requirements.
- Design-review requirement after implementation: screenshots are evidence; visual acceptance must be based on rendered screenshots, not source inspection alone.

## Verification For This Prompt Plan

- This file is planning/prompt work only.
- No source code, build scripts, tests, or global progress files should be modified by this planning step.
