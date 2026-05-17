# FrameScope Monitor UI Design Polish Report

Date: 2026-05-16

## 1. 用户反馈的问题

- 当前深色仪表盘方向可继续，但 UI 基础质量不足：圆角、边框、glow、控件尺寸和间距不统一。
- Overview 页面层级接近，顶部状态卡、统计卡、流程卡、小卡片、底部报告条之间视觉权重过于接近。
- Settings 页面空白组织不自然，输入框高度和文字位置不协调，checkbox 与文字对齐不够精致。
- 路径文本需要更自然地处理，不应硬换行或粗暴截断。
- 表格、进度条、按钮、输入框不能破坏整体深色科技仪表盘观感。

## 2. 本轮设计修复原则

- 只做静态视觉基础修复，不做动画系统，不改按钮 handler，不改 watcher、monitor session、采样、报告生成、GameLite、WMI 或 SGuard 后端逻辑。
- 沿用当前深色科技仪表盘方向，重点统一半径、间距、输入框、checkbox、按钮和卡片层级。
- 降低所有卡片同时发光的强度，保留 active nav、关键状态和主按钮的视觉重点。
- 所有真实按钮、配置保存、报告入口、监控启动/停止仍连接既有逻辑。

## 3. 已读取文档和已调用 skills

已读取/沿用本轮要求的文档上下文：
- `AGENTS.md`
- `docs\modules\software-ui.md`
- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Role.md`
- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Worklog.md`
- `docs\implementation-reports\2026-05-16-framescope-ui-design-implementation-report.md`
- `docs\FrameScopeMonitor-design-system.md`
- `docs\FrameScopeMonitor-reference-ui-plan.md`

已调用/读取 skills：
- `ui-ux-pro-max`
- `ckm:design-system`
- `design-review`
- `review`
- `health`
- `verification-before-completion`

## 4. 修改文件清单

- `src\ui\FrameScopeUiTheme.cs`
- `src\ui\FrameScopePanels.cs`
- `src\ui\FrameScopeButtons.cs`
- `src\ui\FrameScopeStatusControls.cs`
- `src\app\FrameScopeNativeMonitor.UiVisualHelpers.cs`
- `src\app\FrameScopeNativeMonitor.UiVisualCards.cs`
- `src\app\FrameScopeNativeMonitor.UiVisualButtons.cs`
- `src\app\FrameScopeNativeMonitor.UiReportProgress.cs`
- `src\app\FrameScopeNativeMonitor.PageOverview.cs`
- `src\app\FrameScopeNativeMonitor.PageSettings.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Grid.cs`
- `src\ui\FrameScopeReportPage.Layout.cs`
- `docs\implementation-reports\2026-05-16-framescope-ui-design-polish-report.md`

## 5. 每个页面修复说明

Overview:
- 调整顶部统计卡高度和下方内容比例，减少拥挤感。
- 统一统计卡、流程卡、目标列表、底部小卡的圆角和边框强度。
- 输出目录继续使用单行省略和 tooltip，避免硬换行拆词。
- 快速操作和底部报告生成卡保持真实按钮和真实入口。

Settings:
- 将设置页主内容和右侧摘要比例调整为 64/36，让主表单和侧栏关系更稳定。
- 按监测设置、报告设置、诊断设置重新整理固定行高。
- checkbox 改为小方形勾选控件 + label，保留 backing CheckBox 同步逻辑。
- 输入框改成深色原生 TextBox 本体并做圆角裁剪，保证文字可见、可编辑、可保存；长路径保留 tooltip，并默认滚到路径末尾。
- “恢复默认 / 保存设置”仍连接原配置逻辑。

Targets:
- 表格表头、行高、网格线和 checkbox 视觉做深色化统一。
- 右侧设置卡沿用统一输入框和 checkbox 视觉。
- 刷新、添加、保存、启动、停止、报告入口等按钮未改 handler。

Reports:
- 报告中心、右侧摘要/快速操作/导出选项继续使用统一卡片内边距和半径。
- 报告列表按钮和状态点仍使用原 owner-draw 逻辑，报告打开行为未改。

About:
- 未改页面结构，只通过共享卡片、按钮、边框和 glow token 收敛整体观感。

## 6. 统一规则

- 大卡片半径：`UiRadiusCard = 16`
- 控件半径：`UiRadiusControl = 10`
- 统一基础间距：`UiSpaceCard = 18`、`UiSpaceSection = 12`
- 按钮高度：常规设置按钮 `36px`，紧凑按钮保持稳定行高。
- checkbox：18px 小方形，4px 圆角，文字左侧间距固定，未选中态降低亮度。
- glow：普通卡片 glow 降低，仅 active nav、状态卡、主按钮保持明显强调。
- 输入框：深色背景、单行显示、tooltip 保留完整路径，真实 TextBox 继续参与配置写回。

## 7. 截图路径

- `artifacts\20260516-polish-overview.png`
- `artifacts\20260516-polish-settings.png`
- `artifacts\20260516-polish-targets.png`
- `artifacts\20260516-polish-reports.png`
- `artifacts\20260516-polish-about.png`

截图说明：
- settings/targets screenshot harness 使用屏幕拷贝，当前机器有已安装版主窗口干扰；已最小化非 watcher 的已安装主窗口后重新截图。
- 未生成 live 截图，本轮验收页仍为 overview、settings、targets、reports、about。
- 截图后检查没有残留 `--ui-screenshot` 进程。

## 8. 自我检查结果

- Overview 层级：通过。统计卡、流程卡、目标卡、底部卡和报告条的权重比上一轮更清楚。
- Settings 成熟度：部分通过。checkbox、行距和按钮更统一；输入框为保证真实可编辑和截图可见，使用深色原生 TextBox 近似实现，圆角质感不如完全自绘。
- 输入框文字垂直居中：部分通过。文字可见且不贴边，但 WinForms 原生 TextBox 垂直对齐仍有轻微技术限制。
- 路径文本：通过。单行显示，默认滚到末尾，tooltip 可查看完整路径。
- 按钮圆角：通过。设置按钮和紧凑按钮统一使用 `UiRadiusControl`。
- 卡片圆角：通过。普通卡片和 section 卡收敛到统一半径。
- checkbox 对齐：通过。Settings 和 Targets 均为小方形 checkbox + label/表格勾选样式。
- 表格、滚动条、进度条：通过。表格深色化，进度条半径和 glow 已统一；DataGridView 原生滚动条仍是 WinForms 限制。
- 重叠、溢出、裁切：未发现大面积重叠；Settings 的数据目录按钮区域在 WinForms 截图中仍略显紧，后续可继续微调。
- 交互/后端误改：未改禁止的 handler、后端、reporting、tests、build.ps1。

## 9. 测试结果

执行结果：
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`：PASS
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`：PASS
- `.\tests\FrameScopeUiStateTests.exe`：PASS
- `.\tests\FrameScopeReportProgressTests.exe`：PASS
- `node .\tests\chart-sampling-tests.js`：默认 WindowsApps `node.exe` 返回 `Access is denied`
- 使用 Codex runtime Node 放到 PATH 前面后重跑 `node .\tests\chart-sampling-tests.js`：PASS
- `"C:\Program Files\Git\cmd\git.exe" diff --check`：PASS，无 whitespace error；仅输出既有 LF/CRLF warning。

## 10. 禁止文件未修改确认

本轮未修改：
- `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`
- `src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Log.cs`
- `src\ui\FrameScopeReportPage.Actions.cs`
- `src\ui\FrameScopeReportPage.Detail.cs`
- `src\ui\FrameScopeUiState.cs`
- `src\ui\FrameScopeLiveData.cs`
- `src\core\`
- `src\monitoring\`
- `src\diagnostics\`
- `src\reporting\`
- `scripts\lightweight\`
- `packaging\`
- `tests\`
- `build.ps1`

## 11. 下一轮 UI 交互动画窗口注意事项

- 不要恢复 Live/FPS 实时监控 UI 入口；本轮截图和验收不包含 live。
- 交互动画只应加在现有真实按钮和状态反馈上，不要改 watcher、monitor session、报告生成或配置保存语义。
- Settings 输入框如果后续继续追求完全自绘质感，需要单独做 owner-draw 输入控件方案，并重新验证键盘输入、复制粘贴、保存配置和截图。
- Targets 的 DataGridView 滚动条仍是原生控件外观，如要像参考图完全一致，需要单独 owner-draw 或替代表格实现，但风险高于本轮视觉基础修复。
