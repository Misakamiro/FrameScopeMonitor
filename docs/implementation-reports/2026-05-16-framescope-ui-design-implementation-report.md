# 2026-05-16 FrameScope UI Pixel-Match Implementation Report

## 修改目标

按用户提供的 5 张 2026-05-16 参考图，对 FrameScope Monitor WinForms UI 做像素贴近返工。参考图是唯一视觉目标；上一轮 `artifacts\20260516-fix-*.png` 只作为差异反例。Live 页没有参考图，本轮只同步 shell、卡片、按钮、底部报告卡和字体系统，不声明 Live 像素级匹配。

## 已读取文件和 skills

已读取文档：
- `AGENTS.md`
- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Role.md`
- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Worklog.md`
- `docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md`
- `docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md`
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\modules\software-ui.md`
- `docs\modules\ui-interactions.md`
- `docs\FrameScopeMonitor-design-system.md`
- `docs\FrameScopeMonitor-reference-ui-plan.md`
- `docs\superpowers\plans\2026-05-16-framescope-ui-reference-prompt-plan.md`
- `docs\superpowers\plans\2026-05-16-framescope-ui-pixel-match-prompt-plan.md`
- 本报告旧版本

已调用/读取 skills：
- `ui-ux-pro-max`
- `ckm:design-system`
- `design-review`
- `review`
- `health`
- `verification-before-completion`

参考图：
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (1).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (2).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (3).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (4).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_38_46.png`

## 阶段 0 差异清单

共享 shell：
- 参考图有深色标题栏、左上 app icon + `FrameScope Monitor`、右侧窗口按钮；上一轮截图标题栏缺少左上标题。已在 `UiShell.cs` 用 GDI+ 绘制标题栏图标和标题。
- 参考图 sidebar 更亮、更厚的 cyan active 边框和左侧发光条；现实现已接近，但 icon 细节不是逐像素同款。
- 参考图主内容起点与 sidebar 间距更紧；现实现因为 WinForms 固定列和滚动安全区，水平比例仍略宽。
- 顶部三张状态卡和底部报告生成卡已对齐到同一视觉系统，卡片尺寸仍有少量 WinForms 布局差异。

Overview：
- 参考图顶部 5 张指标卡横向均分；现实现数量和信息一致，字号/高度接近。
- 参考图“捕获链流程”是文字状态型卡片；上一轮长流程图已移除，改为状态、链路文本和绿色状态点。
- 参考图右侧只列 2 个受监控游戏并保留“打开输出目录”；现实现按该结构显示。
- 参考图输出目录是单行短路径；现实现使用单行尾部省略并保留完整路径 tooltip，不再硬换行拆词。

Settings：
- 参考图 checkbox 是小方形控件 + label；上一轮长条蓝色行按钮已移除。
- 参考图采样间隔是下拉样式；现实现仍复用真实文本配置输入，视觉上包在深色圆角输入框中，避免改配置写回逻辑。
- 参考图左侧主设置约 60%，右侧三卡；现实现结构一致，控件位置略受 WinForms 文本框原生高度限制。

Targets：
- 参考图表格是深色表头、深蓝行、细蓝网格、方形 checkbox；现实现已改深色表格、owner-draw checkbox 和更高行高。
- 参考图下方输入框和按钮两行分组；现实现已重排为输入/刷新/添加 + 保存/启动/停止。
- 参考图右侧设置 checkbox 也是小方形；现实现已同步。
- 仍不完全一致：ComboBox 仍有 WinForms 原生下拉区域痕迹，但白色块已明显压低。

Reports：
- 参考图是报告中心主卡、三张摘要卡、报告列表、右侧三卡；现实现已移除不在参考图里的搜索行，并改为同结构。
- 参考图列表状态为绿点/图标、打开按钮；现实现 owner-draw 状态点和按钮样式，并保持点击打开真实报告。
- 仍不完全一致：真实历史记录条数和字段来自本地数据，不做静态假数据；因此报告数量、名称、时间与参考图不同。

About：
- 参考图大卡左说明右 logo，下面开发者和联系方式两卡；现实现结构、比例、绿色圆形 check 图标已对齐。
- 仍不完全一致：logo 为项目现有 GDI+ 绘制版本，非参考图截图的完全同款位图。

## 修改文件清单

- `src\app\FrameScopeNativeMonitor.UiShell.cs`
- `src\app\FrameScopeNativeMonitor.PageOverview.cs`
- `src\app\FrameScopeNativeMonitor.PageSettings.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Grid.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs`
- `src\app\FrameScopeNativeMonitor.PageAbout.cs`
- `src\app\FrameScopeNativeMonitor.UiVisualCards.cs`
- `src\app\FrameScopeNativeMonitor.UiVisualSections.cs`
- `src\app\FrameScopeNativeMonitor.UiScreenshots.cs`
- `src\ui\FrameScopeButtons.cs`
- `src\ui\FrameScopeStatusControls.cs`
- `src\ui\FrameScopeReportPage.cs`
- `src\ui\FrameScopeReportPage.Layout.cs`
- `docs\implementation-reports\2026-05-16-framescope-ui-design-implementation-report.md`

## 每个文件改动说明

- `FrameScopeNativeMonitor.UiShell.cs`：统一窗口尺寸、shell、标题栏、sidebar/workspace/header/report progress 区域；本轮补绘标题栏左上 app icon + 标题，保持窗口按钮原逻辑。
- `FrameScopeNativeMonitor.PageOverview.cs`：Overview 按参考图收敛为 5 指标卡、文字型捕获链、受监控游戏、底部四卡；输出目录改为单行省略 + tooltip。
- `FrameScopeNativeMonitor.PageSettings.cs`：Settings 改为左主设置卡 + 右侧三卡；checkbox 改为小方形 checkbox + label，保留原隐藏 CheckBox 字段和配置写回。
- `FrameScopeNativeMonitor.PageTargets.cs`：调整 targets 页面 action 区高度，使下方输入和按钮贴近参考图。
- `FrameScopeNativeMonitor.PageTargets.Grid.cs`：表格深色化，表头/网格/行高/checkbox owner-draw 靠近参考图。
- `FrameScopeNativeMonitor.PageTargets.Actions.cs`：下方输入框、刷新/添加/保存/启动/停止按钮重排；ComboBox 改深色 host 和 owner-draw 下拉项，点击仍调用原方法。
- `FrameScopeNativeMonitor.PageTargets.Layout.cs`：Overview 的目标列表按参考图只展示启用目标摘要。
- `FrameScopeNativeMonitor.PageAbout.cs`：About 大卡、右侧 logo、绿色圆形 check、开发者/联系方式卡片按参考图重排。
- `FrameScopeNativeMonitor.UiVisualCards.cs`：捕获链卡从图形长流程改为参考图式状态/文字卡。
- `FrameScopeNativeMonitor.UiVisualSections.cs`：报告 ListView owner-draw，绘制深色列表、状态点和“打开”按钮单元格。
- `FrameScopeNativeMonitor.UiScreenshots.cs`：截图 harness 保持可输出 Settings/Targets/DataGridView 页面，最终截图已用正确入口生成。
- `FrameScopeButtons.cs`：补充按钮/checkbox 视觉绘制能力，用于蓝色方形勾选和深色按钮体系。
- `FrameScopeStatusControls.cs`：状态/checkbox 类控件视觉对齐参考图小方形勾选样式。
- `FrameScopeReportPage.cs`：保留空 partial 入口，原因是 `build.ps1` 仍显式编译该文件；没有把视觉布局放在入口文件。
- `FrameScopeReportPage.Layout.cs`：Reports 主体布局在此实现；本轮移除搜索行，改为标题、三摘要卡、报告列表和右侧三卡结构。

## 禁止文件边界

本轮没有修改 watcher、monitor session、report orchestration/open/status、core、monitoring、diagnostics、reporting 数据逻辑、GameLite/WMI/SGuard、`tests\`、`scripts\lightweight\`、`packaging\`。

`src\app\FrameScopeNativeMonitor.UiRouting.cs` 本轮没有修改。当前文件中已有 `BuildPageForKey(key, config)` 拆分属于上一轮编译阻塞修复：修改前页面创建逻辑在 `ShowPage()` 内直接按 key 分支；修改后提取为 `BuildPageForKey`，仍保持 `targets/settings/reports/live/about/overview` 同一映射，`!livePage` 停止刷新、`livePage && pageLoaded` 启动刷新语义未改变。

## 截图验证结果

最终截图：
- overview：`artifacts\20260516-pixel-overview.png`
- settings：`artifacts\20260516-pixel-settings.png`
- targets：`artifacts\20260516-pixel-targets.png`
- reports：`artifacts\20260516-pixel-reports.png`
- about：`artifacts\20260516-pixel-about.png`
- live：`artifacts\20260516-pixel-live.png`

截图命令第一次误用了 `dist\FrameScopeMonitor-Setup.exe`，该入口不是 UI 程序，未写出新截图；随后改用根目录 `FrameScopeMonitor.exe --ui-screenshot ... --ui-page ...`，六页均 exit 0。复查无残留 `FrameScopeMonitor.exe` 或 `FrameScopeMonitor-Setup.exe --ui-screenshot` 进程。

## 参考图一致性检查和自评分

Overview：8/10
- 已对齐：shell、sidebar、5 指标卡、文字型捕获链、受监控游戏、底部四卡、报告生成卡。
- 扣分：左上标题栏 icon 不是完全同款参考图位图；主内容比例和卡片间距仍有 WinForms 固定布局差异。
- 技术限制：WinForms GDI+ 自绘能接近但不具备参考图 CSS/图标资源的逐像素材质。

Settings：8/10
- 已对齐：小方形 checkbox + label、左主设置卡、右侧配置摘要/目标状态/捕获链三卡、底部报告卡。
- 扣分：采样间隔使用真实文本输入框，不是完全同款下拉；部分输入控件高度受 WinForms 原生 TextBox 限制。
- 技术限制：为了保持配置写回逻辑，未替换成静态假控件。

Targets：7.5/10
- 已对齐：深色表格、checkbox、按钮分组、右侧三卡、下方设置 checkbox。
- 扣分：ComboBox 原生下拉区域仍有细小视觉痕迹；真实表格滚动条和列宽与参考图不能完全一致。
- 技术限制：DataGridView/ComboBox 原生绘制即便 owner-draw，仍有系统主题残留。

Reports：8/10
- 已对齐：报告中心、三张摘要卡、报告列表、右侧报告摘要/快速操作/导出选项三卡、状态点和打开按钮。
- 扣分：列表真实数据与参考图静态样例不同；WinForms ListView 行间距和表头细节不能完全贴图一致。
- 技术限制：不使用假数据，列表内容来自真实历史记录。

About：8.5/10
- 已对齐：大卡左右比例、右侧大 logo/版本、绿色圆形 check、底部开发者/联系方式卡。
- 扣分：logo 是项目 GDI+ 绘制版本，不是参考图完全同款位图。
- 技术限制：未引入外部位图资源，避免新增资产和构建边界。

Live：共享视觉已对齐
- Live 没有参考图，只同步 shell、header、卡片、按钮、底部报告卡、深色图表/日志视觉。
- 未改 `PageLive.Lifecycle.cs` 和 `PageLive.Log.cs`，未改变进入 live 启动刷新、离开 live 停止刷新的语义。

## 真实逻辑连接检查

- Settings checkbox 保留真实配置字段，保存仍走现有配置写回。
- Targets 刷新/添加/保存/启动/停止仍分别调用 `RefreshProcessList`、`AddSelectedProcess`、`SaveConfigFromGrid`、`StartWatcher`、`StopWatcher`。
- Reports 列表点击“打开”仍调用现有 `OpenSelectedReport()`；右侧按钮仍使用 report action bind helpers。
- Overview/Reports/Targets 的输出目录按钮仍走现有打开目录逻辑。
- Live 日志和图表仍使用现有 live 数据/生命周期，不注入假数据。
- 无真实 PUBG 验证；本轮视觉验收不要求真实 PUBG，但未改 simulator、报告生成链路、报告打开入口、配置读写、watcher start/stop 逻辑。

## 验证命令和结果

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`：PASS，输出 `Build complete: ...\dist\FrameScopeMonitor-Setup.exe`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`：PASS，输出 `FrameScope tests rebuilt.`
- `.\tests\FrameScopeUiStateTests.exe`：PASS
- `.\tests\FrameScopeReportProgressTests.exe`：PASS
- `node .\tests\chart-sampling-tests.js`：默认 WindowsApps `node.exe` 返回 `Access is denied`；将 `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin` 放到 PATH 前面后重跑：PASS，输出 `chart-sampling-tests: PASS`
- `"C:\Program Files\Git\cmd\git.exe" diff --check`：PASS，仅有既有 `README.md`、`build.ps1`、`framescope-config.example.json` LF/CRLF warning

## 已知风险

- 参考图是高保真设计稿，WinForms GDI+ 近似不能保证图标、阴影、发光和原生控件完全逐像素一致。
- Targets 的 ComboBox/DataGridView 仍可能受 Windows 主题影响；若后续必须进一步贴图，需要把这些控件替换为完全自绘控件，但那会扩大交互风险。
- 当前 git 工作树本身包含大量既有未跟踪目录和 `build.ps1` 状态；本轮未回滚或清理这些既有状态。
- 本轮未跑真实 PUBG，只验证 UI、构建、单元测试、chart 采样和截图。

## 给 bug 修复对话框的定位提示

- 标题栏和 shell：看 `src\app\FrameScopeNativeMonitor.UiShell.cs`
- Overview 输出目录和捕获链：看 `src\app\FrameScopeNativeMonitor.PageOverview.cs`、`src\app\FrameScopeNativeMonitor.UiVisualCards.cs`
- Settings checkbox 和配置写回：看 `src\app\FrameScopeNativeMonitor.PageSettings.cs`
- Targets 表格/下拉/按钮：看 `src\app\FrameScopeNativeMonitor.PageTargets.Grid.cs`、`src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`
- Reports 主卡/列表/右侧卡：看 `src\ui\FrameScopeReportPage.Layout.cs`、`src\app\FrameScopeNativeMonitor.UiVisualSections.cs`
- About logo/check/底部卡：看 `src\app\FrameScopeNativeMonitor.PageAbout.cs`
- 截图 harness：看 `src\app\FrameScopeNativeMonitor.UiScreenshots.cs`

## 2026-05-16 Live / FPS 实时监控 UI 移除补充

### 修改目标

上一轮把 Live 页面当作共享视觉验收页处理，这是范围理解错误。本轮修正为：不再从 UI 暴露 Live / FPS 实时监控页面，不再把 Live 作为截图验收页，同时保留 watcher、monitor session、采样器、报告生成、GameLite、WMI、SGuard 和后端监控能力。

### 修改文件

- `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Layout.cs`
- `src\app\FrameScopeNativeMonitor.UiScreenshots.cs`
- `docs\implementation-reports\2026-05-16-framescope-ui-design-implementation-report.md`

### 具体改动

- `FrameScopeNativeMonitor.UiRouting.cs`：新增 `NormalizeVisiblePageKey()`，将空 key 和 `live` key 都归一到 `overview`。因此即使内部或截图参数传入 `live`，也不会创建 Live 页面，也不会触发 `StartLiveRefresh()`。页面映射仍保留 overview/settings/targets/reports/about。
- `FrameScopeNativeMonitor.PageLive.Layout.cs`：删除原 FPS 实时监控、帧时间图表、四张实时指标卡、实时日志区域布局；`BuildLivePage()` 仅作为编译兼容壳返回 `BuildOverviewPage(config)`。
- `FrameScopeNativeMonitor.UiScreenshots.cs`：截图入口先调用 `NormalizeVisiblePageKey()`，因此 `--ui-page live` 不再生成实时监控页；sidebar 截图默认 active key 也改为 `overview`。

### UI 入口结论

- Live / FPS 实时监控入口：已从可见 UI 暴露范围移除。
- 左侧导航：当前只显示 `概览`、`监控目标`、`报告`、`设置`、`关于我们`，没有 `实时监控` 入口。
- 用户是否还能从左侧导航进入实时监控页：不能。
- 截图验收范围：只输出 overview/settings/targets/reports/about，不再输出 live。
- 后端监控、采样、报告生成逻辑：保留，未修改 watcher、monitor session、sampling、reporting、GameLite、WMI、SGuard 或相关后端目录。

### 本轮截图

- overview：`artifacts\20260516-no-live-overview.png`
- settings：`artifacts\20260516-no-live-settings.png`
- targets：`artifacts\20260516-no-live-targets.png`
- reports：`artifacts\20260516-no-live-reports.png`
- about：`artifacts\20260516-no-live-about.png`

未生成 live 截图。截图使用根目录 `FrameScopeMonitor.exe --ui-screenshot ... --ui-page ...`，五页均 exit 0；复查无残留 `FrameScopeMonitor.exe` 或 `FrameScopeMonitor-Setup.exe --ui-screenshot` 进程。

### 本轮验证结果

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`：PASS，输出 `Build complete: ...\dist\FrameScopeMonitor-Setup.exe`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`：PASS，输出 `FrameScope tests rebuilt.`
- `.\tests\FrameScopeUiStateTests.exe`：PASS
- `.\tests\FrameScopeReportProgressTests.exe`：PASS
- `node .\tests\chart-sampling-tests.js`：默认 WindowsApps `node.exe` 返回 `Access is denied`；将 `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin` 放到 PATH 前面后重跑：PASS，输出 `chart-sampling-tests: PASS`
- `"C:\Program Files\Git\cmd\git.exe" diff --check`：PASS，仅有既有 `README.md`、`build.ps1`、`framescope-config.example.json` LF/CRLF warning
