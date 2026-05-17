# FrameScope UI Interaction Reference Prompt Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 根据用户 2026-05-16 提供的五张 UI 参考图，生成下游 UI 交互实现对话框的完整提示词，并默认加入 Codex `/goal` 模式启动段。

**Architecture:** 本计划只负责提示词和交互方案，不直接改 C# 源码。下游实现只允许在 UI 交互 seam 内连接真实逻辑、完善状态反馈和验证路径，不允许修改视觉主题、后端采样、报告数据结构、GameLite/WMI/SGuard。

**Tech Stack:** C# WinForms, FrameScope partial-class UI modules, PowerShell build/test scripts, Node chart sampling tests.

---

## 参考图片

- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (1).png`：概览页。
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (3).png`：监控目标页。
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (2).png`：设置页。
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (4).png`：报告中心页。
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_38_46.png`：关于我们页。

## 参考图明确表达的交互

- 左侧导航必须能在 `概览`、`监控目标`、`报告`、`设置`、`关于我们` 间切换，并显示明确 active 状态。
- 顶部三张状态卡展示监测器、已启用目标、软件状态；状态文本和颜色必须来自真实 watcher/config/report 状态。
- 底部 `报告生成` 卡在所有页面保持可见，显示报告生成阶段、百分比、ETA、进度条、就绪/完成状态，并提供 `打开报告目录`。
- 概览页有 `启动监测` 和 `打开输出目录` 快速操作，必须调用真实 watcher start 和 data root open。
- 监控目标页有表格复选框、采样间隔、自动打开报告、进程输入、刷新进程、添加进程、保存配置、启动监测、停止监测。
- 设置页有采样间隔下拉/输入、自动打开报告、详细日志、自动生成诊断报告、性能诊断、保留天数、最大 MB、数据目录选择。
- 报告页有报告列表、选中报告摘要、打开报告目录、打开 HTML 报告、打开详细报告、导出选项状态。
- 关于我们页主要是只读信息，但 `联系方式` 中的邮箱和网址如果实现为可点击控件，必须调用真实打开行为；如果当前控件不是可点击，不能伪装成按钮。

## 从常见软件体验推断的交互补全

- 所有按钮应有 hover、pressed、disabled 状态；如果当前视觉组件已支持，只绑定状态；如果不支持，不修改视觉主题文件，只报告给 UI 设计对话框。
- `启动监测`、`停止监测`、`保存配置`、`重新生成报告`、`导出支持包` 这类长/风险操作需要开始、成功、失败状态文案，并避免重复点击。
- 表格编辑后应有 dirty 状态或保存提示；保存成功后重新读取配置，保存失败说明具体行和原因。
- 报告列表选择改变时应刷新右侧摘要和可用按钮状态；未选中报告时按钮禁用或显示明确原因。
- 报告路径、数据目录、邮箱、网址等文本过长时应保留 tooltip 或状态栏完整路径提示。

## 需要用户确认的交互

- 参考图未展示实时监控页；下游不能凭空新增 fake FPS 动画，只能沿用现有 live 页真实数据/空状态规则。
- 参考图未展示“详细报告”的真实文件类型；如果当前项目没有独立详细报告文件，只能禁用该按钮或映射到现有诊断/支持包，并在状态里说明。
- 参考图未展示“导出格式”下拉或导出面板；下游不能新增新的报告数据格式，只能展示现有 HTML、诊断报告、支持包能力。
- 关于我们页的邮箱/网址是否必须可点击未确认；默认只在现有控件适合时做真实打开，否则保持只读。

## 下游完整可复制提示词 v2

```text
/goal 根据 2026-05-16 五张 UI 参考图完成 FrameScope Monitor UI 交互实现：只改允许的 UI 交互文件，保持所有按钮、状态、设置、日志、报告入口连接真实逻辑，禁止改视觉主题、后端采样、report generator 数据结构、GameLite/WMI/SGuard，并在交付前完成构建、测试、截图、按钮 wiring、live timer 和残留进程验证。

你现在是 FrameScope Monitor 的 UI 交互实现对话框。

项目路径：
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d

必须调用的 skills：
- diagnose
- review
- improve-codebase-architecture
- tdd
- health
- verification-before-completion

必须先读取：
1. AGENTS.md
2. docs\FrameScopeMonitor-Project-Overview.md
3. docs\modules\ui-interactions.md
4. docs\modules\software-ui.md
5. docs\FrameScopeMonitor-progress.md
6. docs\FrameScopeMonitor-next-prompt.md
7. docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Role.md
8. docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Worklog.md

用户参考图片：
1. C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (1).png - 概览页。
2. C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (3).png - 监控目标页。
3. C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (2).png - 设置页。
4. C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (4).png - 报告中心页。
5. C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_38_46.png - 关于我们页。

参考图给出的交互目标：
1. 左侧导航：概览、监控目标、报告、设置、关于我们必须可切换，并显示当前 active 状态。切页时不能残留旧控件状态；进入 live 页启动刷新，离开 live 页停止刷新。
2. 顶部状态卡：监测器、已启用目标、软件状态必须来自真实 watcher/config/report 状态，不写静态假状态。
3. 底部报告生成卡：所有页面保持真实进度反馈；读取 `LatestReportProgress()` / report progress 状态；`打开报告目录` 调用真实目录打开逻辑。
4. 概览页快速操作：`启动监测` 调用真实 `StartWatcher()`；`打开输出目录` 调用真实 `OpenDataRoot()`；不可用时显示中文原因。
5. 监控目标页：表格 checkbox、采样间隔、自动打开报告写回真实配置；`刷新进程` 读取真实系统进程；`添加进程` 按现有规则补 `.exe`；`保存配置` 校验并保存；`启动监测` / `停止监测` 防重复点击和误操作。
6. 设置页：采样间隔、自动打开报告、详细日志、自动生成诊断报告、性能诊断、保留天数、最大 MB、数据目录选择都必须读写真实配置。保存后重新读取并刷新右侧配置摘要。
7. 报告页：报告列表来自历史记录/run 目录；选择列表项刷新右侧摘要；`打开报告目录`、`打开 HTML 报告`、`打开详细报告`、`重新生成`、`导出支持包` 必须调用真实 handler 或在不可用时禁用/显示原因。
8. 关于我们页：只读信息保持稳定；如果邮箱/网址做成可点击，必须真实打开 mailto/URL；否则不要伪装成按钮。

允许修改文件：
- src\app\FrameScopeNativeMonitor.UiRouting.cs
- src\app\FrameScopeNativeMonitor.UiConfigActions.cs
- src\app\FrameScopeNativeMonitor.UiProcessPicker.cs
- src\app\FrameScopeNativeMonitor.UiWatcherControls.cs
- src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs
- src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs
- src\app\FrameScopeNativeMonitor.UiDiagnosticActions.cs
- src\app\FrameScopeNativeMonitor.PageTargets.Grid.cs
- src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs
- src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs
- src\app\FrameScopeNativeMonitor.PageLive.Log.cs
- src\ui\FrameScopeReportPage.Actions.cs
- src\ui\FrameScopeReportPage.Detail.cs
- src\ui\FrameScopeUiState.cs
- src\ui\FrameScopeLiveData.cs
- src\ui\FrameScopeLiveData.Csv.cs

禁止修改文件：
- src\ui\FrameScopeUiTheme.cs
- src\ui\FrameScopeRoundedDrawing.cs
- src\ui\FrameScopePanels.cs
- src\ui\FrameScopeButtons.cs
- src\ui\FrameScopeStatusControls.cs
- src\ui\FrameScopeReferenceSidebar*.cs
- src\app\FrameScopeNativeMonitor.UiVisual*.cs
- src\app\FrameScopeNativeMonitor.PageOverview.cs
- src\app\FrameScopeNativeMonitor.PageSettings.cs
- src\app\FrameScopeNativeMonitor.PageLive.Layout.cs
- src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs
- src\ui\FrameScopeReportPage.Layout.cs
- src\app\FrameScopeNativeMonitor.Watcher.cs
- src\app\FrameScopeNativeMonitor.MonitorSession*.cs
- src\app\FrameScopeNativeMonitor.ReportOrchestration*.cs
- src\app\FrameScopeNativeMonitor.ReportOpen*.cs
- src\app\FrameScopeNativeMonitor.ReportStatus.cs
- src\core\
- src\monitoring\
- src\diagnostics\
- src\reporting\
- scripts\lightweight\
- ..\gamelite-auto-lightweight\
- WMI trigger / GameLite / SGuard 相关文件
- build.ps1，除非本轮明确新增/删除 C# 文件并独占。

明确不允许做什么：
- 不改视觉主题、颜色、圆角、卡片绘制、图标绘制或页面视觉布局。
- 不新增静态假按钮、假报告、假 FPS、假 watcher 状态。
- 不修改 PresentMon、sampler、monitor-session、report generator 数据结构、status JSON shape。
- 不把 GameLite/lightweight 逻辑接回 FrameScope C# app、build 或 tests。
- 不为了截图绕过真实 handler，不把空数据当成成功数据。

分阶段执行步骤：
1. diagnose：用 `rg` 检查允许文件中的 `Click +=`、`ShowPage`、`StartLiveRefresh`、`StopLiveRefresh`、`SetStatus`、`MessageBox`、report action、config save、process picker、live empty-state 逻辑，建立当前真实 wiring 清单。
2. tdd：凡是涉及可测试规则的改动，先补 `FrameScopeUiStateTests` 或现有测试覆盖。例如 live 页 active/clear 状态、采样间隔校验、添加进程时 watcher 处理、报告按钮可用性规则。
3. 实现交互：逐项修复。页面路由只改 `UiRouting.cs`；配置只改 `UiConfigActions.cs`；watcher 只改 `UiWatcherControls.cs` / `UiProcessCleanup.cs`；目标表格只改 `PageTargets.Grid.cs` / `PageTargets.Actions.cs`；报告按钮只改 `FrameScopeReportPage.Actions.cs` / `.Detail.cs`；live timer/log 只改 `PageLive.Lifecycle.cs` / `PageLive.Log.cs` / `FrameScopeLiveData*.cs`。
4. 状态反馈：为保存、启动、停止、刷新、添加、打开报告、打开目录、导出支持包、重新生成报告补齐开始/成功/失败中文状态。长操作要避免重复点击或至少在状态栏明确“后台执行中”。
5. review：检查 diff 只包含允许文件；检查每个按钮仍调用真实 handler；检查没有新增假数据；检查没有跨到视觉/后端/GameLite。
6. health + verification-before-completion：运行完整验证，不允许在没有新鲜验证证据时声称完成。

验证要求至少运行：
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeUiStateTests.exe
.\tests\FrameScopeReportProgressTests.exe
node .\tests\chart-sampling-tests.js
"C:\Program Files\Git\cmd\git.exe" diff --check

如果改了页面切换、live 页、report 页或按钮 wiring，还必须验证：
- overview 页面截图
- targets 页面截图；如果触发已知 DataGridView screenshot harness 问题，必须定位并停止对应 `--ui-page targets --ui-screenshot` 进程，并说明这是已知截图 harness 风险，不当作主程序失败。
- settings 页面截图
- live 页面截图
- reports 页面截图
- about 页面截图
- 检查 report buttons 仍绑定真实 handler
- 检查 live page 进入启动刷新、离开停止刷新
- 检查无残留 FrameScopeMonitor / PresentMon / GameLite / FakePresentMon / TslGame / Valorant / CS2 等测试进程

最终输出格式：
1. 当前结论：已完成 / 部分完成 / 阻塞。
2. 修改了哪些文件。
3. 按页面说明改了哪些交互：概览、监控目标、设置、报告、关于我们、底部报告生成卡。
4. 每个按钮、设置、状态、空态、错误反馈如何连接真实逻辑。
5. 修复了哪些 bug 或交互风险。
6. 运行了哪些测试和截图，逐条写 PASS/FAIL。
7. 是否有无法真实验证的项目，以及原因。
8. 是否有残留进程。
9. 严格说明没有修改禁止文件；如果必须改了，说明用户授权和原因。
```

## 下游验收清单

- [ ] `/goal` 已作为提示词第一段。
- [ ] 下游提示词包含五张参考图路径和页面含义。
- [ ] 明确区分参考图硬性表达、合理推断、待确认项。
- [ ] 覆盖概览、监控目标、设置、报告、关于我们、底部报告生成卡。
- [ ] 保持 UI 交互文件边界，不跨视觉、后端、report generator、GameLite。
- [ ] 验证要求包含 build、测试、截图、按钮 wiring、live timer、残留进程。
