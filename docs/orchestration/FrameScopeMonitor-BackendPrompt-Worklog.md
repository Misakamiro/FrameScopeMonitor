# FrameScope Monitor Backend Prompt Worklog

## 2026-05-15

### 本次用户需求

用户指定本对话框担任 FrameScope Monitor 的后端提示词及 Skill 分配负责人。

本次没有给出具体要实现的后端功能或 bug。当前工作是先建立并维护后端提示词负责人角色文件、工作日志和一版通用下游后端实现提示词模板。后续收到具体后端需求后，再根据本文件继续生成针对性的下游实现提示词。

### 项目路径

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

### 本次已读取的项目文档

- `AGENTS.md`
- `docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md`
- `docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md`
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\modules\backend-monitoring.md`
- `docs\modules\ui-interactions.md`
- `docs\modules\software-ui.md`
- `docs\modules\lightweight-script.md`
- `docs\FrameScopeMonitor-progress.md`
- `docs\FrameScopeMonitor-next-prompt.md`

### 本次已读取的 UI 设计、UI 交互和计划资料

- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Role.md`
- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Worklog.md`
- `docs\FrameScopeMonitor-design-system.md`
- `docs\FrameScopeMonitor-reference-ui-plan.md`
- `docs\superpowers\plans\2026-05-15-framescope-ui-components-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-ui-interactions-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-ui-shell-split.md`
- `docs\superpowers\plans\2026-05-14-framescope-report-generator-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-report-and-target-pages-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-report-generator-entry-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-report-html-template-split.md`
- `docs\superpowers\plans\2026-05-15-framescope-report-orchestration-split.md`

说明：`FrameScopeMonitor-UiInteractionPrompt-Role.md` 和 `FrameScopeMonitor-UiInteractionPrompt-Worklog.md` 当前不存在，因此本次以 `docs\modules\ui-interactions.md` 和 UI interaction split plan 作为交互边界来源。

### 本次使用的 skills

- `diagnose`：用于要求下游先建立真实反馈循环，不能凭猜测修 watcher/session/report 问题。
- `review`：用于要求下游在实现后做改动审查，重点检查并行边界、状态写入、错误路径和测试缺口。
- `improve-codebase-architecture`：用于确认后端 seam、模块深度和文件责任，不把新逻辑塞回高冲突大文件。
- `tdd`：用于要求下游按行为测试优先，避免一次写大量想象中的测试。
- `health`：用于要求下游跑项目现有构建、测试、chart、RenderProbe、simulator 和 diff 检查。
- `verification-before-completion`：用于强制下游在声明完成前提供新鲜验证证据。
- `writing-plans`：用于把本次后端提示词负责人工作写成可接手的计划文件。

### 当前 UI 按钮或状态需要的后端支持映射

| UI 功能或状态 | 真实后端动作 | 读写文件或数据 | 后端责任文件 |
|---|---|---|---|
| 启动监测 | 启动 watcher，按 enabled targets 扫描目标进程 | `framescope-config.json`、watcher state/log | `FrameScopeNativeMonitor.Watcher.cs`、`UiWatcherControls.cs` 只做调用 |
| 停止监测 | 停止 watcher，必要时清理 FrameScope 后台进程树 | watcher state/log、进程列表 | `FrameScopeNativeMonitor.Watcher.cs`、`UiProcessCleanup.cs` 如任务明确联动 |
| 目标配置保存 | 规范化目标、采样间隔、auto-open 等配置 | `framescope-config.json` | `FrameScopeConfigStore.cs` |
| 刷新/添加进程 | 读取当前系统进程，加入目标配置 | process list、`framescope-config.json` | UI 交互文件负责选择；后端只负责 config normalization |
| 实时监控页 FPS/帧时间 | 读取 active/latest run 的帧数据，不伪造真实捕获 | `presentmon.csv`、`status.json`、`summary.json` | `FrameScopeLiveData.cs` 读取；采集来源在 `MonitorSession*.cs`、PresentMon |
| 实时 CPU/GPU/内存状态 | 读取系统/进程采样输出 | `process-samples.csv`、`system-samples.csv` | `FrameScopeProcessSampler*.cs`、`FrameScopeSystemSampler*.cs` |
| 实时日志暂停 | 只暂停 UI 追加，不删除持久日志 | watcher/report logs | UI 交互负责；后端日志文件不能被清空 |
| 清空实时日志 | 只清空 UI 面板，不删除 log 文件 | UI buffer | UI 交互负责；后端无删除动作 |
| 报告生成进度 | report generator 写进度，watcher/UI 读取并展示 | `report-progress.json`、`status.json` | `FrameScopeReportProgress.cs`、`ReportOrchestration*.cs` |
| 打开最新报告 | 查找最新完成报告并打开 HTML，写 open marker | `history`、`status.json`、`report-opened.flag` | `ReportOpen*.cs`、`ReportStatus.cs` |
| 重新生成报告 | 对已完成 run 调用 `FrameScopeReportGenerator.exe` | run folder、`status.json`、manifest、log | `ReportOrchestration*.cs`、`FrameScopeReportGenerator*.cs` |
| 诊断报告 | 生成 markdown/json 诊断并脱敏 | diagnostics folder、logs、config、latest run | `FrameScopeDiagnostics*.cs` |
| 报告 HTML 图表 | 使用真实 data.js 和 manifest，不删除原始数据 | `framescope-interactive-data.js`、manifest、HTML | report data files 或 `Html.*.cs`，按任务类型拆 |
| PUBG 捕获状态 | 使用 simulator 验证，真实 PUBG 手动验证 | `presentmon.csv`、`status.json`、summary | `FrameScopeCapturePlanner.cs`、`MonitorSession*.cs` |
| GameLite/SGuard 状态 | 仅在用户明确要求时处理；默认不接入 FrameScope 后端 | `scripts\lightweight\` 状态/日志 | `scripts\lightweight\` 独立边界 |

### 本次定义的后端职责

- 后端只能输出真实状态、真实错误、真实报告、真实日志，或明确的 simulator/demo 路径。
- watcher/session/report open/report status 负责监测链路状态，不负责 UI 视觉。
- sampler 负责 CSV schema 和采样行为，不负责 UI 刷新频率。
- report generator 负责真实报告数据和 manifest，不负责 WinForms 页面按钮。
- diagnostics 负责支持包、日志、脱敏和保留策略。
- GameLite 是独立项目边界，不得作为 FrameScope 监测链路依赖。

### 已生成的下游提示词版本

版本：`backend-implementation-template-v1`

用途：当前还没有具体后端功能需求时，作为下一次生成针对性后端实现提示词的模板。收到具体需求后，必须把模板中的“待填需求”替换成明确按钮、状态、报告或采样链路需求。

```text
你现在是 FrameScope Monitor 的后端实现对话框。

项目路径：
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d

任务范围：
[在这里填入本次具体后端需求。必须说明：涉及哪个 UI 按钮、状态、报告入口、日志、监测流程、错误反馈或采样链路。]

你必须使用 / 调用的 skills：
- diagnose
- review
- improve-codebase-architecture
- tdd
- health
- verification-before-completion

如果本任务是 bug 修复或最终打包，还必须使用：
- ship

你必须先读取：
1. AGENTS.md
2. docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md
3. docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md
4. docs\FrameScopeMonitor-Project-Overview.md
5. docs\modules\backend-monitoring.md
6. docs\modules\ui-interactions.md
7. docs\modules\software-ui.md
8. docs\FrameScopeMonitor-progress.md
9. docs\FrameScopeMonitor-next-prompt.md

只有任务触及 GameLite、自动轻量化、WMI 或 SGuard 时，才读取：
- docs\modules\lightweight-script.md

执行前必须先回答：
1. 这个 UI 按钮或状态背后对应哪个真实后端动作？
2. 需要读取或写入哪个配置、status、summary、manifest、history 或 log？
3. 是否影响 watcher、monitor-session、report generation 或 report open？
4. 是否影响采样器 CSV schema？
5. 是否影响报告 HTML、data.js 或 manifest？
6. 是否影响真实游戏捕获，还是 simulator 可验证？
7. 是否需要安装目录同步或最终安装器打包？
8. 是否触碰 GameLite、WMI 或 SGuard 边界？

允许修改文件：
[按任务类型从下面选择，不要全选。]

后端监测 / watcher / session：
- src\app\FrameScopeNativeMonitor.Watcher.cs
- src\app\FrameScopeNativeMonitor.MonitorSession*.cs
- src\app\FrameScopeNativeMonitor.ReportOrchestration*.cs
- src\app\FrameScopeNativeMonitor.ReportStatus.cs
- src\app\FrameScopeNativeMonitor.ReportOpen*.cs
- src\core\FrameScopeConfigStore.cs
- src\core\FrameScopeCapturePlanner.cs
- src\core\FrameScopeReportProgress.cs

采样器：
- src\monitoring\FrameScopeProcessSampler.cs
- src\monitoring\FrameScopeProcessSampler.Models.cs
- src\monitoring\FrameScopeProcessSampler.Selection.cs
- src\monitoring\FrameScopeProcessSampler.IO.cs
- src\monitoring\FrameScopeSystemSampler.cs
- src\monitoring\FrameScopeSystemSampler.Models.cs
- src\monitoring\FrameScopeSystemSampler.PerfCounters.cs
- src\monitoring\FrameScopeSystemSampler.Gpu.cs
- src\monitoring\FrameScopeSystemSampler.Processes.cs
- src\monitoring\FrameScopeSystemSampler.IO.cs

诊断：
- src\diagnostics\FrameScopeDiagnostics*.cs
注意：FrameScopeDiagnostics.Redaction.cs 和 FrameScopeDiagnostics.Retention.cs 必须独占。

报告数据：
- src\reporting\FrameScopeReportGenerator.cs
- src\reporting\FrameScopeReportGenerator.Models.cs
- src\reporting\FrameScopeReportGenerator.Cli.cs
- src\reporting\FrameScopeReportGenerator.Progress.cs
- src\reporting\FrameScopeReportGenerator.Diagnostics.cs
- src\reporting\FrameScopeReportGenerator.PresentMon.cs
- src\reporting\FrameScopeReportGenerator.SystemData.cs
- src\reporting\FrameScopeReportGenerator.ProcessData.cs
- src\reporting\FrameScopeReportGenerator.Analysis.cs
- src\reporting\FrameScopeReportGenerator.Metadata.cs
- src\reporting\FrameScopeReportGenerator.Csv.cs

报告 HTML 模板：
只有任务明确是 report-template 时才允许：
- src\reporting\FrameScopeReportGenerator.Html.cs
- src\reporting\FrameScopeReportGenerator.Html.Layout.cs
- src\reporting\FrameScopeReportGenerator.Html.Styles.cs
- src\reporting\FrameScopeReportGenerator.Html.Sections.cs
- src\reporting\FrameScopeReportGenerator.Html.Scripts.cs

默认禁止修改：
- UI 纯视觉文件：
  - src\ui\FrameScopeUiTheme.cs
  - src\ui\FrameScopeRoundedDrawing.cs
  - src\ui\FrameScopePanels.cs
  - src\ui\FrameScopeButtons.cs
  - src\ui\FrameScopeStatusControls.cs
  - src\ui\FrameScopeReferenceSidebar*.cs
  - src\app\FrameScopeNativeMonitor.UiVisual*.cs
  - src\app\FrameScopeNativeMonitor.PageLive.Layout.cs
  - src\ui\FrameScopeReportPage.Layout.cs
- UI 交互文件，除非本任务明确需要联动：
  - src\app\FrameScopeNativeMonitor.UiRouting.cs
  - src\app\FrameScopeNativeMonitor.UiWatcherControls.cs
  - src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs
  - src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs
  - src\app\FrameScopeNativeMonitor.PageLive.Log.cs
  - src\ui\FrameScopeReportPage.Actions.cs
- GameLite / WMI / SGuard，除非用户明确要求。
- build.ps1，除非新增/删除 C# 文件并独占。
- packaging\，除非任务是最终打包或安装器。

明确不允许做什么：
- 不允许用假数据冒充真实采样、真实状态、真实报告或真实日志。
- 不允许把 GameLite 接回 FrameScope C# app、build、tests、report chain。
- 不允许安装/删除 WMI trigger，除非用户明确授权。
- 不允许隐藏 SGuard 默认行为或关闭开关。
- 不允许为了 UI 效果删除完整原始数据。
- 不允许只跑部分验证就声称完成。

分阶段执行：
1. 读取文档和相关代码，确认真实后端入口。
2. 用 diagnose 建立反馈循环：优先选择现有测试、simulator、直接报告生成、CLI 或 run folder fixture。
3. 用 tdd 写一条行为测试或说明为什么没有正确测试 seam。
4. 做最小实现，保持文件边界。
5. 用 review 检查 diff：状态写入、错误路径、残留进程、并行文件边界、无假数据。
6. 用 health 和 verification-before-completion 跑完整验证。
7. 如果触及报告数据，额外验证 HTML/data.js/manifest 和 Edge headless 截图。
8. 如果触及 watcher/session/sampler，额外验证 simulator 输出和残留进程。

最低验证命令：
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeConfigStoreTests.exe
.\tests\FrameScopeCapturePlannerTests.exe
.\tests\FrameScopeReportProgressTests.exe
.\tests\FrameScopeDiagnosticsTests.exe
.\tests\FrameScopePubgSimulatorTests.exe
.\tests\FrameScopeUiStateTests.exe
node .\tests\chart-sampling-tests.js
dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario stable -DurationSeconds 4
"C:\Program Files\Git\cmd\git.exe" diff --check

如果改了报告生成或报告数据，还必须：
- 直接报告生成或 stable simulator 生成报告。
- 检查 HTML/data.js/manifest 存在。
- 检查 manifest 中 hasFrameData、reportKind、frame count、process/system sample count。
- Edge headless 打开报告并截图。
- 截图非空。
- 不破坏 chart canvas、gauges、process rows、summary rows、data include、chart sampling script。

如果改了 watcher/session/采样器，还必须：
- simulator 证明 monitorExit=0、reportExit=0。
- 检查 presentmon.csv、process-samples.csv、system-samples.csv、summary.json、status.json。
- 检查游戏退出后 monitor/session/sampler/PresentMon 没有残留。
- 如果真实 PUBG 不可用，写出真实 PUBG 手动验证步骤。

真实 PUBG 手动验证步骤：
1. 打开 FrameScope Monitor，确认 PUBG/TslGame 目标启用。
2. 启动监测。
3. 启动 PUBG 并进入一段真实渲染场景。
4. 退出 PUBG。
5. 确认 run 目录生成 presentmon.csv、process-samples.csv、system-samples.csv、summary.json、status.json。
6. 确认 reportExit=0、hasFrameData=true、reportKind=full。
7. 打开 HTML 报告，检查 FPS、1% Low、0.1% Low、process rows、system charts。
8. 确认退出后没有 FrameScopeMonitor --monitor-session、FrameScopeProcessSampler、FrameScopeSystemSampler、PresentMon 残留。

最终输出格式：
1. 当前结论。
2. 改了哪些文件。
3. 实现了哪些后端能力。
4. 修复了哪些 bug 或错误路径。
5. 哪些 UI 按钮/状态现在由真实后端支撑。
6. 验证命令和结果。
7. simulator 结果和真实 PUBG 手动验证步骤。
8. 残留风险或无法验证项。
```

### 下游允许修改文件

当前没有具体需求，因此没有派发实际允许修改文件。下一次必须按任务类型从角色文件中的边界列表精确选择。

### 下游禁止修改文件

当前默认禁止：

- 所有 UI 纯视觉文件。
- UI 交互文件，除非明确联动。
- `scripts\lightweight\`、WMI、SGuard，除非用户明确要求。
- `build.ps1`，除非新增/删除 C# 文件并独占。
- `packaging\`，除非最终打包。
- `docs\FrameScopeMonitor-progress.md`
- `docs\FrameScopeMonitor-next-prompt.md`

### 验证要求

本次只创建文档，没有后端代码改动，因此不运行 C# build/test/simulator 链路。自检范围是：

- 确认只新增后端提示词负责人文档、工作日志和后端提示词维护计划。
- 确认未修改 `src\`、`tests\`、`build.ps1`、`scripts\lightweight\`、`packaging\`。
- 用 `git diff --name-only` 和 `git diff --check` 检查本次文档改动。

实际自检结果：

- `git status --short --untracked-files=all -- docs/orchestration/FrameScopeMonitor-BackendPrompt-Role.md docs/orchestration/FrameScopeMonitor-BackendPrompt-Worklog.md docs/superpowers/plans/2026-05-15-framescope-backend-prompt-plan.md`：确认这 3 个文件为本次新增未跟踪文档。
- 新增 3 个文档行尾空白检查：全部 `no trailing whitespace`。
- `git diff --check`：未报告新增文档 whitespace 错误；只显示既有 `README.md`、`build.ps1`、`framescope-config.example.json` 的 LF/CRLF warning。
- `git diff --name-only -- src tests build.ps1 scripts/lightweight packaging`：输出 `build.ps1`，这是工作区已有 tracked 改动，不是本次文档写入造成；本次未写入实现文件。
- 本次未运行 C# build/test/simulator，因为没有后端代码改动。

### 尚未确认的问题

- 用户尚未提出具体后端功能、bug 或报告数据需求。
- 下一次需要先确认任务属于 watcher/session、sampler、diagnostics、report data、report template、packaging，还是 GameLite。
- 如果任务涉及真实 PUBG 捕获，当前机器仍默认走 simulator 验证，真实 PUBG 需要用户环境手动验证。
- 如果任务涉及安装目录或最终发布，需要用户明确是否同步 `%LOCALAPPDATA%\FrameScopeMonitor` 或重新打包。

### 下一次对话接手点

1. 先读取 `docs\orchestration\FrameScopeMonitor-BackendPrompt-Role.md` 和本工作日志。
2. 读取用户新需求，并按“UI 到后端能力的判断方法”回答 8 个问题。
3. 只选择本次任务需要的允许修改文件。
4. 生成针对性的下游后端实现提示词，并把版本追加到本工作日志。
5. 除非用户明确要求更新全局进度，不要改 `docs\FrameScopeMonitor-progress.md` 和 `docs\FrameScopeMonitor-next-prompt.md`。

## 2026-05-16

### 本次用户需求

用户提供 5 张 FrameScope Monitor 参考 UI 图片，并要求读取当前项目文件、UI 设计和 UI 交互成果，设计一个默认启用 Codex goal 模式的下游后端实现对话框提示词和 skill 分配方案，用来补齐缺少的后端能力，保证每个前端按钮、状态、报告、日志和监测流程都有真实后端可用。

本次仍属于后端提示词负责人工作，不是后端实现工作。因此只维护本角色文档、工作日志和一份 dated plan，不修改 `src\`、`tests\`、`build.ps1`、`scripts\lightweight\`、`packaging\`、`docs\FrameScopeMonitor-progress.md` 或 `docs\FrameScopeMonitor-next-prompt.md`。

### 参考图片

- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (1).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (2).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (3).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (4).png`
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_38_46.png`

参考图对应当前软件主导航：Overview、Targets、Reports、Settings、About。结合 UI 设计实现报告确认，Live/FPS 实时页当前已从可见导航移除，`live` 或空 page key 会归一到 `overview`，但后端监测、采样、报告生成链路仍然需要保留。

### 本次读取的文档

- `AGENTS.md`
- `docs\orchestration\FrameScopeMonitor-BackendPrompt-Role.md`
- `docs\orchestration\FrameScopeMonitor-BackendPrompt-Worklog.md`
- `docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md`
- `docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md`
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\modules\backend-monitoring.md`
- `docs\modules\ui-interactions.md`
- `docs\modules\software-ui.md`
- `docs\FrameScopeMonitor-progress.md`
- `docs\FrameScopeMonitor-next-prompt.md`
- `docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Role.md`
- `docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Worklog.md`
- `docs\implementation-reports\2026-05-16-framescope-ui-design-implementation-report.md`
- `docs\implementation-reports\2026-05-16-framescope-ui-interaction-implementation-report.md`

本次没有深入读取 `docs\modules\lightweight-script.md`，因为用户没有要求 GameLite、自动轻量化、WMI 或 SGuard 后端实现。

### 本次读取的 UI 和后端代码

- `src\app\FrameScopeNativeMonitor.PageOverview.cs`
- `src\app\FrameScopeNativeMonitor.PageSettings.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`
- `src\ui\FrameScopeReportPage.Layout.cs`
- `src\app\FrameScopeNativeMonitor.UiHelpers.cs`
- `src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs`
- `src\app\FrameScopeNativeMonitor.UiConfigActions.cs`
- `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`
- `src\ui\FrameScopeReportPage.Actions.cs`
- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `src\app\FrameScopeNativeMonitor.ReportStatus.cs`
- `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs`
- `src\app\FrameScopeNativeMonitor.ReportOpen.Status.cs`
- `src\core\FrameScopeReportProgress.cs`
- `src\core\FrameScopeConfigStore.cs`
- `src\reporting\FrameScopeReportGenerator.cs`
- `tests\FrameScopeUiStateTests.cs`
- `tests\FrameScopePubgSimulatorTests.cs`

### 本次判断出的 UI 按钮或状态后端支持点

| UI 功能或状态 | 当前观察到的来源 | 下游后端必须保证的真实能力 |
|---|---|---|
| 顶部监测器状态卡 | `IsWatcherRunningQuiet()` / watcher state path | 真实 watcher pid、phase、active monitor count、stale state 处理，不能显示假 ready/running。 |
| 已启用目标卡 | `FrameScopeConfigStore` targets | 配置读写必须保留 enabled count、别名、采样间隔、auto-open 字段。 |
| 软件状态卡 | watcher/config action 状态 pill | 后端错误必须通过 status/log 字段暴露，不能永远显示 ready。 |
| Overview 捕获链状态 | watcher running 和 latest report path | 必须区分 idle、monitoring、done、diagnostic report、error、missing data。 |
| Overview 最近报告和输出目录 | history/latest report path | history 必须指向真实 HTML/run dir，缺文件要明确报错或禁用动作。 |
| Targets 表格 | `framescope-config.json` | 保存目标不能丢失 `ProcessSampleIntervalMs`、`SlowSampleIntervalMs` 等字段。 |
| Targets 启动/停止 | `StartWatcher()` / `StopWatcher()` | watcher lifecycle 必须写 state/log，并清理 monitor/session/sampler 子进程。 |
| Settings 保存和重置 | `FrameScopeConfigStore` | diagnostic/log/retention/data-root 字段必须真实持久化并影响诊断/保留策略。 |
| Reports 页统计和摘要 | `RecentHistoryEntries()` / `LatestReportPath()` | 报告数量、状态和类型必须来自真实 history/status/manifest，不允许静态示例。 |
| Reports 打开/详情/重新生成 | selected history entry/run dir/report HTML | 缺 HTML、缺 run dir、缺 CSV 时必须有真实错误；重新生成必须调用真实 report generator。 |
| 底部报告生成进度卡 | latest `status.json` progress | 进度必须属于当前或最近有效 run，能区分阶段、错误和 stale run。 |

### 本次定义的后端职责

- 下游后端实现应优先补齐或加固 UI 可读的真实 status/report summary seam，而不是重做 UI。
- 后端只输出真实状态、真实错误、真实报告、真实日志，或明确标注 simulator 路径。
- watcher/session/report open/report status 负责监测链路状态和生命周期，不负责视觉布局。
- sampler 负责 CSV schema 和采样行为，不负责 UI 刷新频率。
- report generator 负责真实 HTML/data.js/manifest/summary 数据，不允许用空报告伪装 full 成功。
- diagnostics 负责支持包、日志、脱敏和保留策略。
- GameLite/WMI/SGuard 默认不触碰，不得重新接回 FrameScope C# app、build、tests 或 report chain。

### 本次生成的下游提示词版本

版本：`backend-ui-support-v2`

已写入计划文件：

- `docs\superpowers\plans\2026-05-16-framescope-backend-ui-support-prompt-plan.md`

该版本的下游提示词已包含：

- 开头 `/goal` 指令，默认启用 Codex goal 模式。
- 必须读取的项目文档、UI 设计实现报告、UI 交互实现报告和相关代码。
- UI 按钮/状态到真实后端能力的映射。
- 允许修改文件、禁止修改文件和高冲突独占文件。
- 明确禁止假数据、假状态、空报告冒充成功、GameLite/WMI/SGuard 回接、UI 视觉文件改动。
- 分阶段执行步骤：diagnose 反馈回路、可证伪假设、TDD 最小行为测试、最小后端实现、review、health、verification-before-completion。
- 完整 build/test/simulator/report/manifest/residual process 验证要求。
- 真实 PUBG 不可测时的 simulator 验证和真实 PUBG 手动验证步骤。

### 本次更新的人格文件

- `docs\orchestration\FrameScopeMonitor-BackendPrompt-Role.md`

新增或强化内容：

- `Codex Goal 模式规则`。
- 下游后端实现提示词必须以 `/goal` 或同等目标声明开头。
- goal 内容必须同时保留真实后端能力、严格文件边界和完整验证三个约束。

### 下游允许修改文件

本次给 `backend-ui-support-v2` 的默认允许范围：

- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession*.cs`
- `src\app\FrameScopeNativeMonitor.ReportOrchestration*.cs`
- `src\app\FrameScopeNativeMonitor.ReportStatus.cs`
- `src\app\FrameScopeNativeMonitor.ReportOpen*.cs`
- `src\core\FrameScopeConfigStore.cs`
- `src\core\FrameScopeCapturePlanner.cs`
- `src\core\FrameScopeReportProgress.cs`
- `src\diagnostics\FrameScopeDiagnostics*.cs`
- `src\reporting\FrameScopeReportGenerator*.cs`
- `tests\FrameScopeConfigStoreTests.cs`
- `tests\FrameScopeCapturePlannerTests.cs`
- `tests\FrameScopeReportProgressTests.cs`
- `tests\FrameScopeDiagnosticsTests.cs`
- `tests\FrameScopePubgSimulatorTests.cs`
- `tests\FrameScopeUiStateTests.cs`

只有下游证明 UI 交互文件必须读取新增后端状态 seam，且不改视觉布局时，才允许最小联动：

- `src\app\FrameScopeNativeMonitor.UiHelpers.cs`
- `src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs`
- `src\ui\FrameScopeReportPage.Detail.cs`

### 下游禁止修改文件

- UI 纯视觉和 layout 文件：`src\ui\FrameScopeUiTheme.cs`、`src\ui\FrameScopeRoundedDrawing.cs`、`src\ui\FrameScopePanels.cs`、`src\ui\FrameScopeButtons.cs`、`src\ui\FrameScopeStatusControls.cs`、`src\ui\FrameScopeReferenceSidebar*.cs`、`src\app\FrameScopeNativeMonitor.UiVisual*.cs`、`src\app\FrameScopeNativeMonitor.PageLive.Layout.cs`、`src\app\FrameScopeNativeMonitor.PageOverview.cs`、`src\app\FrameScopeNativeMonitor.PageSettings.cs`、`src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs`、`src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`、`src\ui\FrameScopeReportPage.Layout.cs`。
- `scripts\lightweight\` 和根目录 GameLite wrapper `.ps1` / `.cmd`。
- WMI trigger 安装、删除、迁移。
- SGuard 策略。
- `packaging\`。
- `docs\FrameScopeMonitor-progress.md`。
- `docs\FrameScopeMonitor-next-prompt.md`。

### 下游必须独占的高冲突文件

- `build.ps1`
- `src\app\FrameScopeNativeMonitor.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession.PresentMon.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession.Status.cs`
- `src\app\FrameScopeNativeMonitor.ReportStatus.cs`
- `src\app\FrameScopeNativeMonitor.ReportOpen*.cs`
- `src\reporting\FrameScopeReportGenerator.cs`
- `src\diagnostics\FrameScopeDiagnostics.Redaction.cs`
- `src\diagnostics\FrameScopeDiagnostics.Retention.cs`

### 验证要求

本次文档协调工作只需要文档级验证，不运行 C# build/test/simulator。下游后端实现必须至少运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeConfigStoreTests.exe
.\tests\FrameScopeCapturePlannerTests.exe
.\tests\FrameScopeReportProgressTests.exe
.\tests\FrameScopeDiagnosticsTests.exe
.\tests\FrameScopePubgSimulatorTests.exe
.\tests\FrameScopeUiStateTests.exe
node .\tests\chart-sampling-tests.js
dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario stable -DurationSeconds 4
"C:\Program Files\Git\cmd\git.exe" diff --check
```

如果改了报告生成或报告数据，下游还必须生成报告并检查 HTML/data.js/manifest、`hasFrameData`、`reportKind`、frame count、process/system sample count、Edge headless 截图、chart canvas、gauges、process rows、summary rows、data include 和 chart sampling script。

如果改了 watcher/session/采样器，下游还必须证明 simulator `monitorExit=0`、`reportExit=0`，检查 `presentmon.csv`、`process-samples.csv`、`system-samples.csv`、`summary.json`、`status.json`，并确认游戏退出后 monitor/session/sampler/PresentMon 没有残留。

### 尚未确认的问题

- 下游是否要真正实现“统一 UI 后端状态摘要 seam”，需要由实现对话框先诊断后确认，不允许预设一定改代码。
- 当前参考图没有要求恢复 Live/FPS 页面，因此下游不应把 Live 页重新暴露到导航。
- 真实 PUBG 捕获仍无法在本协调线程验证，必须由下游用 simulator 验证，并写出真实 PUBG 手动验证步骤。
- 用户没有授权 GameLite、WMI、SGuard、安装目录同步或最终打包，本次提示词默认全部排除。

### 下一次对话接手点

1. 先打开 `docs\superpowers\plans\2026-05-16-framescope-backend-ui-support-prompt-plan.md`，复制 `Downstream Complete Copy Prompt v2` 给后端实现对话框。
2. 如果用户提出更具体的后端 bug 或功能，再在 `backend-ui-support-v2` 基础上收窄允许文件列表。
3. 继续保持后端提示词负责人边界：只写 orchestration/worklog/plan，除非用户明确授权，不进入 `src\` 实现。

### 下游后端实现回传验收

用户回传了后端实现对话框输出，声明 goal 已 complete，耗时 1076 秒，并写入实现报告：

- `docs\implementation-reports\2026-05-16-framescope-backend-implementation-report.md`

下游声明的实际修改文件：

- `src\core\FrameScopeReportProgress.cs`
- `src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs`
- `tests\FrameScopeReportProgressTests.cs`
- `docs\implementation-reports\2026-05-16-framescope-backend-implementation-report.md`

本协调线程做了只读验收，没有改动下游实现文件。验收读取了实现报告、目标文件状态和关键代码片段，确认本轮实现焦点是底部“报告生成”卡片的真实进度来源：

- `FrameScopeReportProgress.FindLatestEffectiveStatus()` 按 `ReportProgressUpdatedAt` 和新鲜度窗口选择有效 run。
- `FrameScopeNativeMonitor.UiStatusRefresh.LatestReportProgress()` 改为读取 core seam。
- `FrameScopeReportProgressTests.FindsFreshProgressInsteadOfTouchedStaleStatus()` 覆盖旧 `status.json` 被 touch 后不应覆盖新鲜活动 run。

边界验收结论：

- 未发现下游实现报告声明触碰 GameLite/WMI/SGuard。
- 未发现下游实现报告声明触碰 UI 视觉/layout 文件。
- 未发现下游实现报告声明修改 `docs\FrameScopeMonitor-progress.md` 或 `docs\FrameScopeMonitor-next-prompt.md`。
- 下游对 `src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs` 的最小联动符合 `backend-ui-support-v2` 中“只有需要读取新增后端状态 seam 时允许最小联动”的例外。

下游报告中的验证结果摘要：

- `build.ps1` PASS。
- `tests\Build-FrameScopeTests.ps1` PASS。
- `FrameScopeConfigStoreTests.exe`、`FrameScopeCapturePlannerTests.exe`、`FrameScopeReportProgressTests.exe`、`FrameScopeDiagnosticsTests.exe`、`FrameScopePubgSimulatorTests.exe`、`FrameScopeUiStateTests.exe` 全部 PASS。
- `chart-sampling-tests.js` 首次命中 WindowsApps Node `Access is denied`，切换 Codex Node runtime 后 PASS。
- `FrameScopeRenderProbe` Release build PASS，0 warnings，0 errors。
- `git diff --check` 退出码 0，仅有既有 CRLF warning。
- stable simulator：`monitorExit=0`、`reportExit=0`、`hasFrameData=true`、`frames=240`、`reportKind=full`。
- 报告产物存在并检查了 HTML、data.js、manifest、chart canvas、gauges、process rows、summary rows、data include 和 chart sampling script。
- Edge headless 截图非空：`artifacts\backend-report-headless-20260516-221024.png`，496560 bytes。
- FrameScope/PresentMon/sampler/FakePresentMon/TslGame/GameLite 未残留。

本协调线程的附加观察：

- 当前这些目标文件在 `git status --short --untracked-files=all -- <target files>` 中显示为未跟踪文件，因此不能只依赖 `git diff` 表示本轮变化来源；后续测试或 bugfix 对话框需要以实现报告和文件内容为准。
- 本协调线程没有重新运行完整 C# build/test/simulator，只核对了实现报告和关键代码路径。若要做发布前验收，仍应由测试员或 ship 对话框重新跑完整链路。
- 真实 PUBG 仍未在本机验证，下游已按要求写出真实 PUBG 手动验证步骤。

后续接手建议：

1. 测试员对话框优先验证底部“报告生成”卡不会读取旧 run 进度。
2. 如果继续做后端补齐，下一轮不要重复改 UI 视觉文件，优先从 status/report/core seam 继续。
3. 如果准备打包或发布，先读取本实现报告，再由 bugfix/package 对话框重新跑完整验证链和安装目录同步检查。
