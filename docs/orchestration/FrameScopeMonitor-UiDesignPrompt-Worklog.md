# FrameScope Monitor UI Design Prompt Worklog

## 2026-05-15

### 本次用户要求

用户指定我担任 FrameScope Monitor 的 UI 设计提示词及 Skill 分配负责人。

本次没有提供新的参考图片。当前工作是先建立并维护自己的角色文件和工作日志，后续当用户发来参考图时，再按参考图生成下游 UI 实现提示词。

### 项目路径

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

### 本次已读取的项目文档

- `AGENTS.md`
- `docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md`
- `docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md`
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\modules\software-ui.md`
- `docs\modules\ui-interactions.md`
- `docs\FrameScopeMonitor-progress.md`
- `docs\FrameScopeMonitor-next-prompt.md`

### 本次已读取的历史 UI 设计资料

- `docs\FrameScopeMonitor-design-system.md`
- `docs\FrameScopeMonitor-reference-ui-plan.md`
- `docs\superpowers\plans\2026-05-15-framescope-ui-components-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-ui-shell-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-ui-interactions-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-reference-sidebar-split.md`

### 本次调用 / 使用的 skills

- `ui-ux-pro-max`
- `ckm:design-system`
- `plan-design-review`
- `design-review`
- `writing-plans`
- `verification-before-completion`

说明：本轮没有直接实现 UI，因此 `design-review` 只用于确定下游视觉 QA 和截图验证要求；`plan-design-review` 和 `writing-plans` 用于提示词计划结构；`verification-before-completion` 用于要求交付前有实际验证证据。

### 从已有资料确认的视觉方向

当前没有新参考图。本次只从历史设计资料确认以下已有方向：

- 旧的 B Professional Performance Dashboard 方向已被 2026-05-10 用户参考图覆盖。
- 当前参考源是真实参考图驱动的 dark tech / gaming performance dashboard。
- 核心结构是左侧导航、顶部标题和状态卡、页面内容卡、底部报告生成进度卡。
- 视觉语言是近黑 / 深蓝背景、半透明深蓝卡片、1px 蓝色边框、柔和青色发光、青蓝主色、绿色健康状态、紫色分析/诊断、琥珀警告、红色停止/错误。
- UI 功能文案默认中文；`FrameScope Monitor` 可以作为产品名保留英文。
- 所有按钮、状态、图表、日志、报告入口必须接真实逻辑；无数据时显示空状态或明确标注演示数据。
- WinForms 可以近似参考图的玻璃感、发光和卡片层级，但不能要求像 Web/CSS 一样逐像素复刻。

### 当前实现边界摘要

UI 视觉可由下游实现对话框处理的主要文件：

- `src\ui\FrameScopeUiTheme.cs`
- `src\ui\FrameScopeRoundedDrawing.cs`
- `src\ui\FrameScopePanels.cs`
- `src\ui\FrameScopeButtons.cs`
- `src\ui\FrameScopeStatusControls.cs`
- `src\ui\FrameScopeLiveChart.cs`
- `src\ui\FrameScopeReferenceSidebar*.cs`
- `src\app\FrameScopeNativeMonitor.UiShell.cs`
- `src\app\FrameScopeNativeMonitor.UiVisualHelpers.cs`
- `src\app\FrameScopeNativeMonitor.UiVisualCards.cs`
- `src\app\FrameScopeNativeMonitor.UiVisualSections.cs`
- `src\app\FrameScopeNativeMonitor.UiVisualButtons.cs`
- `src\app\FrameScopeNativeMonitor.UiReportProgress.cs`
- `src\app\FrameScopeNativeMonitor.UiScreenshots.cs`
- `src\app\FrameScopeNativeMonitor.UiStatusDisplay.cs`
- `src\app\FrameScopeNativeMonitor.PageOverview.cs`
- `src\app\FrameScopeNativeMonitor.PageSettings.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Layout.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs`
- `src\ui\FrameScopeReportPage.Layout.cs`

报告 HTML 视觉如果单独安排，可处理：

- `src\reporting\FrameScopeReportGenerator.Html.Styles.cs`
- `src\reporting\FrameScopeReportGenerator.Html.Sections.cs`
- `src\reporting\FrameScopeReportGenerator.Html.Layout.cs`

### 下游提示词允许修改文件

下游 UI 视觉实现提示词默认只允许改角色文件中列出的 UI 视觉文件。若需要新增/删除 C# 文件，必须独占 `build.ps1` 并明确更新编译列表。

### 下游提示词禁止修改文件

默认禁止改：

- `src\app\FrameScopeNativeMonitor.cs`
- `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`
- `src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Log.cs`
- `src\ui\FrameScopeReportPage.Actions.cs`
- `src\ui\FrameScopeReportPage.Detail.cs`
- `src\ui\FrameScopeUiState.cs`
- `src\ui\FrameScopeLiveData.cs`
- `src\ui\FrameScopeLiveData.Csv.cs`
- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession*.cs`
- `src\app\FrameScopeNativeMonitor.ReportOrchestration*.cs`
- `src\app\FrameScopeNativeMonitor.ReportOpen*.cs`
- `src\app\FrameScopeNativeMonitor.ReportStatus.cs`
- `src\core\`
- `src\monitoring\`
- `src\diagnostics\`
- `src\reporting\FrameScopeReportGenerator.cs`
- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`
- `scripts\lightweight\`
- WMI trigger / GameLite / SGuard 相关文件

### 已生成的下游提示词版本

本次没有生成最终下游 UI 实现提示词，因为用户尚未提供本轮参考图片。

后续收到参考图后，应生成 `v1` 下游提示词，并在这里记录：

- 参考图路径或图片说明。
- 明确观察。
- 需要用户确认的推断。
- 页面映射。
- 下游允许修改文件。
- 下游禁止修改文件。
- 验证命令和截图要求。

### 验证要求模板

下游 UI 实现对话框至少需要跑：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeUiStateTests.exe
.\tests\FrameScopeReportProgressTests.exe
node .\tests\chart-sampling-tests.js
"C:\Program Files\Git\cmd\git.exe" diff --check
```

如果改 WinForms UI，必须截图：

- overview
- settings
- live
- reports
- targets 如涉及则尝试；若 DataGridView screenshot harness 卡住，必须定位并停止对应 screenshot 进程。

如果改 HTML 报告视觉，必须补充：

- stable simulator 或直接报告生成。
- HTML/data.js/manifest 存在。
- Edge headless 打开报告截图。
- 非空像素检查。
- 不破坏 chart canvas、gauges、process rows、summary rows、data include、chart sampling script。

### 仍需用户确认的问题

- 需要用户提供本轮参考图片，或者明确指定继续沿用 2026-05-10 那组历史参考图。
- 如果只提供单张图，需要确认该图适用于全局外观，还是只适用于某个页面/组件。
- 如果参考图中有看不清的按钮、图标或数据内容，需要用户确认具体含义后才能写成硬性实现要求。

### 下一次对话接手点

下一次收到参考图后，从这里继续：

1. 读取本文件和 `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Role.md`。
2. 逐张拆解参考图的明确视觉观察。
3. 标注推断和待确认项。
4. 映射到概览、设置、实时监控、监控目标、报告、HTML 报告。
5. 生成完整可复制的下游 UI 实现提示词。
6. 把提示词版本、边界和待确认问题追加到本 Worklog。

## 2026-05-16

### 本次用户要求

用户提供了 5 张 2026-05-16 UI 参考图，要求根据参考图生成给下游 UI 设计/实现对话框使用的提示词和 skill 分配，并要求默认提示词加入 Codex goal 模式。

本轮仍是 UI 设计提示词负责人工作，不直接修改 `src` UI 源码。

### 本次参考图

- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (1).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (2).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (3).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (4).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_38_46.png`

### 本次已读取的项目文档

- `AGENTS.md`
- `docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md`
- `docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md`
- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Role.md`
- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Worklog.md`
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\modules\software-ui.md`
- `docs\modules\ui-interactions.md`
- `docs\FrameScopeMonitor-design-system.md`
- `docs\FrameScopeMonitor-reference-ui-plan.md`
- `docs\FrameScopeMonitor-progress.md`
- `docs\FrameScopeMonitor-next-prompt.md`

### 本次使用的 skills

- `ui-ux-pro-max`
- `ckm:design-system`
- `plan-design-review`
- `design-review`
- `writing-plans`
- `verification-before-completion`

### 来自图片的明确观察

- 五张图共享同一个深色 WinForms 应用壳层：顶部薄标题栏、左侧固定导航、主内容区、顶部三张状态卡、底部报告生成卡。
- 左侧导航包含产品 logo、`FrameScope Monitor`、导航项 `概览`、`监控目标`、`报告`、`设置`、`关于我们`，底部服务状态卡显示 `运行中` 和版本 `v1.1.1`。
- active nav 使用青色描边、蓝色渐变填充、发光和青色文字/图标。
- 背景为近黑到深蓝黑，卡片为半透明深蓝，边框为细蓝/青色，整体有柔和内外发光。
- 顶部状态卡固定为 `监测器 / 就绪`、`已启用目标 / 6 已启用`、`软件状态 / 就绪`。
- 底部报告生成卡在所有图中固定存在，显示绿色进度条、`报告生成：完成 100%`、`打开报告目录`、`报告状态：完成 100%`。
- 概览页包含五个指标卡、捕获链流程卡、受监控游戏卡、最近捕获/最近报告/输出目录/快速操作卡。
- 设置页包含主设置表单、配置摘要、目标状态、捕获链状态。
- 监控目标页包含目标表格、进程输入、刷新/添加/保存/启动/停止按钮、右侧捕获链/报告/设置卡。
- 报告页包含报告中心摘要、报告列表表格、右侧报告摘要/快速操作/导出选项。
- 关于页包含产品介绍、功能清单、大 logo、开发者和联系方式卡。

### 需要用户确认或只能推断的点

- 本轮没有实时监控页参考图；live 页面只能沿用同一壳层、卡片、图表和日志视觉语言，并根据现有功能推导布局。
- 图中部分图标属于参考形状，下游可用 WinForms 自绘近似，不要求逐像素一致。
- 具体窗口尺寸可按当前 screenshot harness 和 WinForms 约束适配，不强制固定到参考图像素。

### 已生成的下游提示词版本

已生成 `v1`，保存在：

- `docs\superpowers\plans\2026-05-16-framescope-ui-reference-prompt-plan.md`

该提示词包含 `/goal` 模式入口、项目路径、必读文档、skills、参考图路径、视觉要求、页面映射、允许/禁止文件、执行步骤、测试截图要求和最终输出格式。

### 下游允许修改文件

同 `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Role.md` 中 UI 视觉实现允许列表，重点是：

- `src\ui\FrameScopeUiTheme.cs`
- `src\ui\FrameScopeRoundedDrawing.cs`
- `src\ui\FrameScopePanels.cs`
- `src\ui\FrameScopeButtons.cs`
- `src\ui\FrameScopeStatusControls.cs`
- `src\ui\FrameScopeLiveChart.cs`
- `src\ui\FrameScopeReferenceSidebar*.cs`
- `src\app\FrameScopeNativeMonitor.UiShell.cs`
- `src\app\FrameScopeNativeMonitor.UiVisual*.cs`
- `src\app\FrameScopeNativeMonitor.UiReportProgress.cs`
- `src\app\FrameScopeNativeMonitor.UiScreenshots.cs`
- `src\app\FrameScopeNativeMonitor.UiStatusDisplay.cs`
- `src\app\FrameScopeNativeMonitor.PageOverview.cs`
- `src\app\FrameScopeNativeMonitor.PageSettings.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Layout.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs`
- `src\ui\FrameScopeReportPage.Layout.cs`

### 下游禁止修改文件

禁止修改 UI 交互、后端、报告数据、GameLite/WMI/SGuard 文件，尤其是：

- `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`
- `src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Log.cs`
- `src\ui\FrameScopeReportPage.Actions.cs`
- `src\ui\FrameScopeReportPage.Detail.cs`
- `src\ui\FrameScopeUiState.cs`
- `src\ui\FrameScopeLiveData*.cs`
- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession*.cs`
- `src\app\FrameScopeNativeMonitor.Report*.cs`
- `src\core\`
- `src\monitoring\`
- `src\diagnostics\`
- `src\reporting\FrameScopeReportGenerator.cs`
- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`
- `..\gamelite-auto-lightweight\`

### 下一次对话接手点

如果用户要开始 UI 实现，把 `docs\superpowers\plans\2026-05-16-framescope-ui-reference-prompt-plan.md` 中 `Downstream Prompt` 整段复制给下游 UI 实现对话框。

如果用户补充实时监控页参考图，需要先更新本 Worklog，再生成 `v2` 下游提示词。

## 2026-05-16 v2 像素级对齐返工提示词

### 本次用户需求

用户反馈上一轮 UI 设计编写结果“还是和参考图片不一样”，要求重新生成提示词，并且必须使用 Codex goal 命令，让下游 UI 实现对话框以“和参考图片一模一样”为目标返工。

本轮仍是 UI 设计提示词负责人工作：只更新提示词计划和本 Worklog，不直接修改 `src\` 下任何 UI 实现源码。

### 本次参考图

沿用用户再次提供的 5 张 2026-05-16 参考图：

- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (1).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (2).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (3).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (4).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_38_46.png`

### 本次读取/使用的资料

- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Role.md`
- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Worklog.md`
- `docs\superpowers\plans\2026-05-16-framescope-ui-reference-prompt-plan.md`
- 当前下游实现截图：`artifacts\20260516-fix-*.png`
- 5 张用户参考图

### 本次使用的 skills

- `ui-ux-pro-max`
- `ckm:design-system`
- `plan-design-review`
- `design-review`
- `writing-plans`
- `verification-before-completion`

### 本次提取/确认的关键视觉返工要求

- 参考图必须作为唯一视觉验收目标，上一轮 `fix-*` 截图只能作为差异来源，不能作为新标准。
- Settings 页面上一轮把 checkbox 做成长条蓝色行按钮，这与参考图不一致；新版提示词明确禁止，要求改回参考图中的小方形 checkbox + label。
- Targets 右侧设置区同样不能使用长条 checkbox 行，必须匹配参考图小方框样式。
- Overview 页面上一轮加入了参考图没有的长流程图表达；新版提示词要求按参考图恢复为文字/状态型捕获链卡片。
- Reports 页面上一轮表格偏原生数据网格；新版提示词要求按参考图做卡片内报告中心、三张摘要卡、右侧三卡结构和更清晰的列表行距。
- About 页面整体接近，但仍需按参考图继续修正左右分区比例、绿色 check 图标大小、logo 组合和底部卡片布局。
- Live 页面没有参考图，不允许声称像素级一致，只能同步共享 shell、卡片、按钮、底部报告卡等视觉系统。
- 下游不能用“WinForms 做不到”作为提前降级理由；必须先尝试自绘和现有 helper，仍无法实现时写明具体技术原因。

### 已生成的下游提示词版本

已生成 `v2` 像素级对齐返工提示词，保存到：

- `docs\superpowers\plans\2026-05-16-framescope-ui-pixel-match-prompt-plan.md`

该提示词包含：

- `/goal` 第一行目标。
- 参考图路径。
- 当前 `fix-*` 截图作为反例。
- 页面级差异清单。
- 阶段 0 先做视觉差异清单，不允许直接盲改。
- 阶段 1-7 分别处理 shared shell、Overview、Settings、Targets、Reports、About、Live。
- 严格允许/禁止文件边界。
- 截图、测试、最终输出格式。

### 下游允许修改文件

新版在原 UI 视觉边界基础上，显式允许上一轮实际需要的视觉相关文件，避免再出现“边界例外”：

- `src\ui\FrameScopeUiTheme.cs`
- `src\ui\FrameScopeRoundedDrawing.cs`
- `src\ui\FrameScopePanels.cs`
- `src\ui\FrameScopeButtons.cs`
- `src\ui\FrameScopeStatusControls.cs`
- `src\ui\FrameScopeLiveChart.cs`
- `src\ui\FrameScopeReferenceSidebar*.cs`
- `src\app\FrameScopeNativeMonitor.UiShell.cs`
- `src\app\FrameScopeNativeMonitor.UiVisual*.cs`
- `src\app\FrameScopeNativeMonitor.UiReportProgress.cs`
- `src\app\FrameScopeNativeMonitor.UiScreenshots.cs`
- `src\app\FrameScopeNativeMonitor.UiStatusDisplay.cs`
- `src\app\FrameScopeNativeMonitor.PageOverview.cs`
- `src\app\FrameScopeNativeMonitor.PageSettings.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Layout.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Grid.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`
- `src\app\FrameScopeNativeMonitor.PageAbout.cs`
- `src\ui\FrameScopeReportPage.cs`
- `src\ui\FrameScopeReportPage.Layout.cs`

### 下游禁止修改文件

仍禁止修改后端、监控链路、报告数据、GameLite/WMI/SGuard、测试和构建脚本，尤其是：

- `build.ps1`，除非新增/删除 C# 文件且独占执行并说明原因
- `tests\`
- `scripts\lightweight\`
- `packaging\`
- `src\app\FrameScopeNativeMonitor.cs`
- `src\app\FrameScopeNativeMonitor.UiRouting.cs`，除非出现编译阻塞且只做最小修复
- `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`
- `src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Log.cs`
- `src\ui\FrameScopeReportPage.Actions.cs`
- `src\ui\FrameScopeReportPage.Detail.cs`
- `src\ui\FrameScopeUiState.cs`
- `src\ui\FrameScopeLiveData*.cs`
- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession*.cs`
- `src\app\FrameScopeNativeMonitor.Report*.cs`
- `src\core\`
- `src\monitoring\`
- `src\diagnostics\`
- `src\reporting\FrameScopeReportGenerator.cs`
- `src\reporting\FrameScopeReportGenerator.Html.Scripts.cs`
- WMI trigger / GameLite / SGuard 相关文件

### 仍需用户确认的问题

- 用户本轮没有提供 Live 页面参考图，因此 Live 不能要求“和参考图一模一样”，只能要求共享视觉系统一致。
- 如果用户希望 HTML 报告模板也和某张参考图一致，需要单独提供 HTML 报告参考图或明确把 HTML 报告视觉纳入本轮范围。

### 下一次对话接手点

如果用户要继续让下游 UI 实现对话框返工，直接复制：

- `docs\superpowers\plans\2026-05-16-framescope-ui-pixel-match-prompt-plan.md` 中 `Downstream Prompt v2` 整段。

如果下游返工后再次输出截图，应先逐页对照参考图和新截图，再判断是否还需要 v3 提示词。

## 2026-05-16 Live / FPS 实时监控 UI 移除补丁

### 本次用户反馈

用户指出上一版像素贴近返工后仍保留了 `FPS 实时监控` / Live 页面，这是不应该继续暴露的 UI。问题来源是上一版提示词把 Live 写成“没有参考图，只同步共享视觉系统”，导致下游保留并美化了 Live 页面，而不是删除 UI 入口。

### 给下游的补充要求

已要求下游补做一轮 UI 修正：

- 删除或隐藏 Live / FPS 实时监控页面入口。
- 左侧导航只保留 `概览`、`监控目标`、`报告`、`设置`、`关于我们`。
- 不再把 Live 页面作为截图验收页。
- 不删除后端监控能力，不改 watcher、monitor session、采样器、报告生成、GameLite、WMI、SGuard。
- 如需兼容历史 `live` page key，只允许在 UI routing / screenshot 入口做最小归一。

### 下游报告的修正结果

下游报告称已完成：

- `src\app\FrameScopeNativeMonitor.UiRouting.cs`：`live` key 归一到 `overview`，不会再构建 Live 页面，也不会触发 `StartLiveRefresh()`。
- `src\app\FrameScopeNativeMonitor.PageLive.Layout.cs`：`BuildLivePage()` 仅保留兼容壳，返回 Overview。
- `src\app\FrameScopeNativeMonitor.UiScreenshots.cs`：截图入口归一 `live`，不再生成 Live 页面截图。
- `docs\implementation-reports\2026-05-16-framescope-ui-design-implementation-report.md`：追加本轮说明。

### 本轮实际核对

已查看下游输出的五张截图：

- `artifacts\20260516-no-live-overview.png`
- `artifacts\20260516-no-live-settings.png`
- `artifacts\20260516-no-live-targets.png`
- `artifacts\20260516-no-live-reports.png`
- `artifacts\20260516-no-live-about.png`

截图中左侧导航没有 `实时监控` / Live 入口，验收截图也不再包含 Live 页面。

已读取关键代码确认：

- `ShowPage()` 先执行 `NormalizeVisiblePageKey(key)`，再计算 `livePage`。
- `NormalizeVisiblePageKey("live")` 返回 `overview`。
- 因此外部传入 `live` 时，`livePage` 为 false，`StartLiveRefresh()` 不会执行。
- `BuildLivePage()` 返回 `BuildOverviewPage(config)`，作为历史调用兼容壳。

### 当前判断

本轮针对“删除 Live / FPS 实时监控 UI 暴露”的补丁方向正确，可以接受为 UI 暴露层修正。

需要注意：应用标题副标题和 About 功能清单中仍出现 `FPS 时间线` / `FPS 时间线与性能分析` 这类产品能力文案。这不是 Live 页面入口，但如果用户希望全局移除 FPS 相关文案，需要再给下游一条单独的文案清理提示词。

### 下一次接手点

如果用户继续要求“完全不出现 FPS 字样”，下一轮提示词应聚焦文案清理，而不是 Live 页面删除：

- 检查 shell subtitle、Overview、About、Reports 等页面中所有 `FPS` 字样。
- 区分是否删除产品能力文案，还是仅删除实时监控页面文案。
- 不改后端采样和报告数据逻辑。
