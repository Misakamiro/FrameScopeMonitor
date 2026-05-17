# FrameScope UI Interaction Prompt Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给下游 UI 交互实现对话框一份完整、可复制、边界清晰的提示词，用于改进 FrameScope Monitor 的页面切换、按钮状态、日志、报告、设置保存、live 页生命周期和错误反馈。

**Architecture:** 只允许下游在 UI 交互 seam 内改动，继续调用现有配置、watcher、报告、诊断和 live data helper；不改视觉主题、后端采样、报告数据结构、GameLite、WMI 或 SGuard。

**Tech Stack:** C# WinForms, existing FrameScope partial-class UI modules, existing PowerShell build/test scripts, Node chart sampling tests.

---

## 下游完整可复制提示词 v1

```text
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

参考 UI / 设计资料说明：
- 本轮没有新的参考图。沿用当前 FrameScope dark tech dashboard 视觉结构，但本任务只做交互，不改视觉主题。
- 交互必须服务真实用户路径：用户能看懂按钮、点击后有反馈、长操作不重复触发、错误能恢复、空数据不伪造。
- 如果发现需要视觉组件支持的新 hover/pressed/loading 样式，只能在最终报告中列为建议，不要改视觉主题文件。

用户路径和交互目标：
1. 页面切换：`ShowPage` 切换到 live 页时启动 UI refresh timer，离开 live 页时停止 timer；其他页面不能残留 live timer。
2. 概览/监控目标快速操作：启动监测、停止监测、刷新进程、添加进程、保存配置必须调用真实现有 handler。
3. 设置保存：保存前校验采样间隔；保存后重新读取配置并显示成功状态；失败用中文说明具体原因。
4. 监控开始：启动前保存配置；已有 watcher 时显示 PID 并阻止重复启动；启动失败显示错误。
5. 监控停止：没有 FrameScope 后台进程时说明无需停止；有进程时只清理 FrameScope 相关后台进程。
6. 实时监控：无活动目标、目标未运行、无可读 run、presentmon 无 FPS 时显示真实空状态；不能切换成假 FPS 曲线。
7. 实时日志：暂停只暂停 UI 追加；继续恢复显示；清空只清空当前 UI 面板，不删除持久化日志文件，并显示状态说明。
8. 报告页：打开最近报告、打开数据目录、生成诊断、打开历史、打开选中报告、打开目录、导出支持包、重新生成、刷新必须仍绑定真实 handler。
9. 报告错误：未选择报告、报告文件不存在、运行目录不存在、缺少 CSV 采样数据时给中文状态反馈。
10. 所有长操作：后台导出支持包、重新生成报告等必须有开始/成功/失败状态反馈，并避免用户误以为软件卡死。

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
- src\app\FrameScopeNativeMonitor.PageLive.Layout.cs
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
- WMI trigger / GameLite / SGuard 相关文件
- build.ps1，除非本轮明确新增/删除 C# 文件并独占。

明确任务范围：
1. 检查并修复 UI 交互中可能存在的无反馈、重复点击、错误不清楚、空态不真实、live timer 生命周期、日志清空语义、报告按钮 wiring 等问题。
2. 优先把可测试行为放到 `FrameScopeUiState.cs`，并用 `FrameScopeUiStateTests.exe` 覆盖。
3. 保持交互文件调用现有 helper，不把 watcher、monitor-session、report generator、diagnostics 数据结构改进来。
4. 不做视觉主题重设，不新增静态假 UI，不新增假 FPS 或假报告。

分阶段执行步骤：
1. diagnose：先用 `rg` 检查所有允许文件中的按钮绑定、timer、SetStatus、MessageBox、报告按钮和 live data 空态，建立当前反馈回路。
2. tdd：若发现需要改规则，先在现有 UI state 测试中增加一个最小行为测试；只测试公开/可测试规则，不测私有实现细节。
3. 实现：逐项修复交互问题，每次只改对应责任文件。例如 live timer 只改 `PageLive.Lifecycle.cs` / `FrameScopeUiState.cs`，报告按钮只改 `FrameScopeReportPage.Actions.cs` / `.Detail.cs`。
4. review：对照允许/禁止文件清单检查 diff，确认没有改视觉主题、后端采样、report generator 数据结构或 GameLite。
5. health + verification：运行完整验证命令和截图检查，确认没有残留进程。

验证要求至少运行：
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeUiStateTests.exe
.\tests\FrameScopeReportProgressTests.exe
node .\tests\chart-sampling-tests.js
"C:\Program Files\Git\cmd\git.exe" diff --check

如果改了页面切换、live 页、report 页或按钮 wiring，还必须验证：
- overview 页面截图
- settings 页面截图
- live 页面截图
- reports 页面截图
- 检查 report buttons 仍绑定真实 handler
- 检查 live page 进入启动刷新、离开停止刷新
- 检查无残留 FrameScopeMonitor / PresentMon / GameLite / FakePresentMon / TslGame / Valorant / CS2 等测试进程

最终输出格式：
1. 修改了哪些文件。
2. 每个文件改了什么交互行为。
3. 修复了哪些 bug 或风险。
4. 哪些按钮/状态/空态/错误反馈被验证。
5. 运行了哪些测试和截图，逐条写 PASS/FAIL。
6. 是否有无法真实验证的项目，以及原因。
7. 是否有残留进程。
8. 严格说明没有修改禁止文件；如果必须改了，说明用户授权和原因。
```

## 执行检查清单

- [ ] 下游提示词包含项目路径。
- [ ] 下游提示词包含必须读取文档。
- [ ] 下游提示词包含必须调用 skills。
- [ ] 下游提示词说明本轮没有新参考图，不把视觉资料误当成交互硬要求。
- [ ] 下游提示词覆盖用户路径和交互目标。
- [ ] 下游提示词包含允许修改文件列表。
- [ ] 下游提示词包含禁止修改文件列表。
- [ ] 下游提示词明确任务范围和不允许做什么。
- [ ] 下游提示词包含分阶段执行步骤。
- [ ] 下游提示词包含测试和验证要求。
- [ ] 下游提示词包含最终输出格式。
