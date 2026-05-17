# 软件 UI 模块说明

## 这个板块负责什么

软件 UI 模块负责 FrameScope Monitor 的可视界面，包括主题颜色、字体、圆角、边框、发光效果、卡片、按钮、输入框、表格、图表容器、侧边栏、顶部状态栏、底部报告生成区域和各页面视觉布局。

它不负责采样、PresentMon、报告生成业务逻辑。UI 控件可以展示真实状态，也可以触发操作，但核心数据来源和监测行为应留在 core、monitoring、diagnostics、reporting 或 app 编排层。

## 包含目录和文件

- `src\ui\FrameScopeUiComponents.cs`
  - 深色主题视觉组件。
  - 卡片、圆角按钮、侧边栏、状态标签、图表绘制控件、目标表格视觉绘制。
  - 参考图风格相关 token 和绘制逻辑主要在这里维护。
- `src\ui\FrameScopeUiState.cs`
  - 可测试 UI 状态规则。
  - 包含实时监控页是否刷新、是否清空图表、目标编辑校验、添加进程时是否先停止 watcher 等规则。
- `src\ui\FrameScopeLiveData.cs`
  - 实时监控页读取最近 run 的 FPS、帧时间、系统指标、日志尾部。
- `src\ui\FrameScopeReportPage.cs`
  - 报告页 UI、报告列表、报告详情、报告操作。
- `src\app\FrameScopeNativeMonitor.cs`
  - 仍包含主窗口、页面组装、部分页面布局和 WinForms 事件绑定。
  - 后续拆分页面 builder 时，应从这里小步提取。

## UI 主题和组件结构

当前 UI 是深蓝黑监控面板风格：

- 背景：深蓝黑。
- 卡片：深色渐变、轻边框、内发光。
- 主色：蓝/青色。
- 成功色：绿色。
- 警告色：黄色。
- 危险色：红色，主要用于停止类操作。
- 紫色：用于强调和辅助状态。
- 表格：深色 DataGridView 皮肤，自绘复选框，避免默认白色控件感。
- 图表：深色网格、滚动最近数据、空状态时不显示假曲线。

## 页面组成

- 概览页：状态卡、启用目标、捕获链、最近报告、输出目录、快速操作。
- 设置页：采样间隔、自动打开报告、详细日志、诊断报告、性能诊断、保留天数、最大 MB、数据目录。
- 实时监控页：FPS 图、帧时间图、当前 FPS、CPU/GPU、当前进程、捕获链、实时日志、暂停、清空。
- 监控目标页：目标表格、进程输入、刷新进程、添加进程、保存配置、启动/停止监测、状态卡。
- 报告页：报告列表、报告详情、打开报告、打开目录、生成诊断报告。

## 最近整理了哪些内容

- UI 源码从根目录移动到 `src\ui\`。
- `FrameScopeNativeMonitor.cs` 移动到 `src\app\`，主窗口入口和页面事件仍在那里。
- `build.ps1` 已更新为从 `src\ui\` 编译 UI 文件。
- UI 状态测试通过 `tests\Build-FrameScopeTests.ps1` 可重新编译。

## 后续修改 UI 应该看哪些文件

- 改视觉组件：`src\ui\FrameScopeUiComponents.cs`
- 改实时监控 UI 数据展示：`src\ui\FrameScopeLiveData.cs`
- 改报告页 UI：`src\ui\FrameScopeReportPage.cs`
- 改可测试 UI 状态规则：`src\ui\FrameScopeUiState.cs`
- 改主窗口页面组合和事件绑定：`src\app\FrameScopeNativeMonitor.cs`

维护原则：

- 不要在每个页面单独写一套颜色和按钮风格。
- 不要用固定假数据冒充真实状态。
- 没有真实数据时显示空状态。
- 视觉变更后至少跑构建、UI 状态测试和截图/启动烟测。
## Stage 21 partial split ownership

Stage 21 split the main WinForms app into partial class files. UI design work should now avoid `src\app\FrameScopeNativeMonitor.cs` unless changing startup arguments or shared app constants.

Primary UI design files:

- `src\ui\FrameScopeUiTheme.cs`: shared app colors and UI radius constants.
- `src\ui\FrameScopeUiComponents.cs`: custom cards, sidebar, charts, buttons, and drawing primitives.
- `src\app\FrameScopeNativeMonitor.UiShell.cs`: shell frame, sidebar, header, report-progress card, screenshot helpers, and common visual helper methods.
- `src\app\FrameScopeNativeMonitor.PageOverview.cs`: overview page layout.
- `src\app\FrameScopeNativeMonitor.PageSettings.cs`: settings page layout and settings visual rows.
- `src\app\FrameScopeNativeMonitor.PageLive.cs`: live page layout, metric cards, chart cards, and log panel visuals.
- `src\app\FrameScopeNativeMonitor.PageTargets.cs`: target page layout and target table visuals.
- `src\ui\FrameScopeReportPage.cs`: reports page layout, report list, report detail card, and report page controls.

Do not put watcher loops, PresentMon arguments, sampler startup, report generator execution, WMI/GameLite behavior, or monitor-session status writing into these UI design files.

Parallel editing rule:

- UI design conversations can edit the files above.
- `src\ui\FrameScopeUiTheme.cs` and `src\ui\FrameScopeUiComponents.cs` should be exclusive when changing shared tokens/components because every page depends on them.
- `build.ps1` must be exclusive when adding or removing C# source files from the main executable compile list.

## Stage 23-28 refined UI design ownership

The old `src\ui\FrameScopeUiComponents.cs` file is now only an umbrella note. Shared visual controls were split into focused files:

- `src\ui\FrameScopeRoundedDrawing.cs`: rounded-region and shared drawing helpers.
- `src\ui\FrameScopePanels.cs`: card, workspace, setting-row, sidebar-panel, and rounded table layout panel controls.
- `src\ui\FrameScopeButtons.cs`: rounded button and nav button controls.
- `src\ui\FrameScopeStatusControls.cs`: status label, capture-chain visual, toggle checkbox, sidebar logo, and glow dot controls.
- `src\ui\FrameScopeLiveChart.cs`: live snapshot model and mini chart panel.
- `src\ui\FrameScopeReferenceSidebar.cs`: reference sidebar control state, constructor, and mouse navigation events.
- `src\ui\FrameScopeReferenceSidebar.Navigation.cs`: sidebar navigation event args.
- `src\ui\FrameScopeReferenceSidebar.Drawing.cs`: compact/reference sidebar drawing, logo, nav item, service card, and text drawing helpers.

The app UI shell was also split:

- `src\app\FrameScopeNativeMonitor.UiShell.cs`: form shell, sidebar creation, dark title bar, and header creation.
- `src\app\FrameScopeNativeMonitor.UiVisualHelpers.cs`: shared card, metric, section, list, and button visual factories.
- `src\app\FrameScopeNativeMonitor.UiReportProgress.cs`: bottom report progress card and progress bar width updates.
- `src\app\FrameScopeNativeMonitor.UiScreenshots.cs`: UI screenshot harness and sidebar screenshot helper.
- `src\app\FrameScopeNativeMonitor.UiStatusDisplay.cs`: status label/pill display updates and form fade-in.

Parallel UI design rule:

- Button/card/control visual changes should target the matching `src\ui\FrameScope*.cs` file, not `FrameScopeUiComponents.cs`.
- Shell-level layout and common visual helpers should be edited in `UiShell.cs`, `UiVisualHelpers.cs`, `UiReportProgress.cs`, `UiScreenshots.cs`, or `UiStatusDisplay.cs` according to responsibility.
- Page-specific visual changes should stay in the page partial file: `PageOverview.cs`, `PageSettings.cs`, `PageLive.cs`, `PageTargets.cs`, `PageAbout.cs`, or `src\ui\FrameScopeReportPage.cs`.
- `src\ui\FrameScopeUiTheme.cs`, `src\ui\FrameScopeReferenceSidebar.Drawing.cs`, and `src\app\FrameScopeNativeMonitor.UiVisualHelpers.cs` should be exclusive when changing shared visual language.

## Stage 29-32 final UI design ownership

The remaining high-conflict UI files were split further:

- `src\ui\FrameScopeReportPage.cs`: reports page entry and two-column composition only.
- `src\ui\FrameScopeReportPage.Layout.cs`: report action card, report list card, report detail card, and report page controls.
- `src\ui\FrameScopeReportPage.Detail.cs`: report detail text and latest-report lookup.
- `src\ui\FrameScopeReportPage.Actions.cs`: report open/history/folder/support-bundle/regenerate actions. Treat as interaction-sensitive, not pure styling.
- `src\app\FrameScopeNativeMonitor.PageTargets.cs`: target page entry and left/right page composition only.
- `src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs`: target list visual card and target settings card.
- `src\app\FrameScopeNativeMonitor.PageTargets.Grid.cs`: target `DataGridView` columns, styling, validation hooks, and checkbox painting.
- `src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`: process combo host, target action row, and target action button placement.
- `src\app\FrameScopeNativeMonitor.UiVisualHelpers.cs`: basic shared visual helpers only (`GlassCard`, `UiPurple`, `AppVersionText`, `IconBlock`, `MakeRounded`).
- `src\app\FrameScopeNativeMonitor.UiVisualCards.cs`: status, metric, info, capture-chain, and metric-block cards.
- `src\app\FrameScopeNativeMonitor.UiVisualSections.cs`: section panels, form labels, and dark `ListView` styling.
- `src\app\FrameScopeNativeMonitor.UiVisualButtons.cs`: dashboard/settings button factories and button palettes.
- `src\ui\FrameScopeReferenceSidebar.Drawing.cs`: sidebar paint entry points only.
- `src\ui\FrameScopeReferenceSidebar.CompactDrawing.cs`: compact runtime sidebar drawing.
- `src\ui\FrameScopeReferenceSidebar.ReferenceDrawing.cs`: full reference sidebar drawing used by the screenshot/reference path.
- `src\ui\FrameScopeReferenceSidebar.LogoDrawing.cs`: FrameScope logo, status dot, and shared text drawing.

Parallel UI design rule:

- Report-page visual layout should usually target `FrameScopeReportPage.Layout.cs`; report actions should be coordinated with UI interaction work before editing `FrameScopeReportPage.Actions.cs`.
- Target table visual changes should target `PageTargets.Grid.cs`; target settings/list visual changes should target `PageTargets.Layout.cs`; action-row control placement should target `PageTargets.Actions.cs`.
- Shared card/section/button appearance should target `UiVisualCards.cs`, `UiVisualSections.cs`, or `UiVisualButtons.cs` instead of the umbrella `UiVisualHelpers.cs`.
- Compact sidebar and full reference sidebar can now be edited independently, but `FrameScopeReferenceSidebar.LogoDrawing.cs` and `FrameScopeUiTheme.cs` should remain exclusive when changing shared brand marks or colors.
- `build.ps1` must stay exclusive whenever adding/removing C# source files from compilation.

## Stage 33 residual UI boundary cleanup

The residual report/live page split refined the last mixed UI files:

- `src\ui\FrameScopeReportPage.Layout.cs`: report page visual structure only. It creates the action card, list card, detail card, and button controls, then delegates button wiring to `FrameScopeReportPage.Actions.cs`.
- `src\ui\FrameScopeReportPage.Actions.cs`: report page action binding and action handlers. Treat this as interaction-sensitive, not a pure styling file.
- `src\app\FrameScopeNativeMonitor.PageLive.cs`: ownership note only.
- `src\app\FrameScopeNativeMonitor.PageLive.Layout.cs`: live page visual structure, chart cards, metric cards, and labels.
- `src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs`: live refresh timer startup, shutdown, and live page rebuild lifecycle.
- `src\app\FrameScopeNativeMonitor.PageLive.Log.cs`: live log pause, clear, display text, and log panel button behavior.
- `src\ui\FrameScopeLiveData.Csv.cs`: CSV helper methods used by live snapshot loading.

Parallel UI design rule:

- UI design conversations can edit `FrameScopeReportPage.Layout.cs` and `PageLive.Layout.cs` for visual layout changes.
- Do not add click handlers, timers, report open behavior, watcher/session calls, or CSV parsing logic to layout files.
- `FrameScopeReportPage.Actions.cs`, `PageLive.Lifecycle.cs`, and `PageLive.Log.cs` should be coordinated with UI interaction work before editing.

## Stage 34 report HTML template ownership

The generated HTML report template is separate from the WinForms report page. It now has focused files under `src\reporting\`:

- `FrameScopeReportGenerator.Html.cs`: template assembly entry only.
- `FrameScopeReportGenerator.Html.Layout.cs`: document/body wrapper fragments.
- `FrameScopeReportGenerator.Html.Styles.cs`: embedded report CSS.
- `FrameScopeReportGenerator.Html.Sections.cs`: static report body sections.
- `FrameScopeReportGenerator.Html.Scripts.cs`: chart, canvas, hover, zoom/pan, export, and tab interaction JavaScript.

Parallel UI/report rule:

- WinForms report page visual work remains in `src\ui\FrameScopeReportPage.Layout.cs`.
- WinForms report page button/action work remains in `src\ui\FrameScopeReportPage.Actions.cs`.
- Generated HTML report styling can edit `FrameScopeReportGenerator.Html.Styles.cs`.
- Generated HTML report body layout can edit `FrameScopeReportGenerator.Html.Layout.cs` and `FrameScopeReportGenerator.Html.Sections.cs`.
- Generated HTML report chart interaction can edit `FrameScopeReportGenerator.Html.Scripts.cs`, with chart sampling tests required.
