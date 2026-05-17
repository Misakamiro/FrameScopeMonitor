# FrameScope Monitor UI Interaction Prompt Worklog

## 2026-05-15

### 本次用户要求

用户指定我担任 FrameScope Monitor 的 UI 交互提示词及 Skill 分配负责人，不直接改 UI 交互代码，而是读取项目资料、建立角色文件和工作日志，并为下游 UI 交互实现对话框生成完整提示词、skills 分配和文件边界。

本轮没有提供新的参考图片。交互方案基于现有 UI 设计资料、当前模块拆分、UI 交互文档和已读取的交互代码。

### 项目路径

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

### 本次已读取的项目文档

- `AGENTS.md`
- `docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md`
- `docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md`
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\modules\ui-interactions.md`
- `docs\modules\software-ui.md`
- `docs\FrameScopeMonitor-progress.md`
- `docs\FrameScopeMonitor-next-prompt.md`

### 本次已读取的 UI 设计资料

- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Role.md`
- `docs\orchestration\FrameScopeMonitor-UiDesignPrompt-Worklog.md`
- `docs\FrameScopeMonitor-design-system.md`
- `docs\FrameScopeMonitor-reference-ui-plan.md`
- `docs\superpowers\plans\2026-05-15-framescope-ui-interactions-split.md`

### 本次已读取的 UI 交互代码

- `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- `src\app\FrameScopeNativeMonitor.UiConfigActions.cs`
- `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Log.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`
- `src\ui\FrameScopeReportPage.Actions.cs`
- `src\ui\FrameScopeReportPage.Detail.cs`
- `src\ui\FrameScopeUiState.cs`
- `src\ui\FrameScopeLiveData.cs`

### 本次调用 / 使用的 skills

- `diagnose`：用于要求下游先建立反馈回路和可复现验证，不凭感觉改交互。
- `review`：用于把交互风险和真实 handler wiring 放进审查要求。
- `improve-codebase-architecture`：用于保持 UI 交互 seam 清晰，不把后端/report/GameLite 逻辑塞进交互文件。
- `tdd`：用于要求通过 `FrameScopeUiStateTests` 等可测试规则锁定行为。
- `verification-before-completion`：用于要求完成前必须有新鲜验证证据。
- `writing-plans`：用于生成可交给下游执行的阶段计划。
- `ui-ux-pro-max`：用于覆盖按钮状态、空态、错误、loading、进度和导航体验。
- `ckm:design-system`：用于把 hover、pressed、disabled、loading 等状态按组件状态规格写清楚。
- `plan-design-review`：用于从用户路径、状态完整性和设计系统一致性角度检查提示词。

### 提取的用户路径

- 概览页：用户需要看懂当前监测状态、启用目标、输出目录、最近报告和快速操作；按钮必须调用真实启动监测、打开输出目录、刷新进程或保存配置。
- 设置页：用户修改采样间隔、自动打开报告、诊断开关和数据目录后，需要保存成功/失败反馈；恢复默认要写入真实默认配置并刷新页面。
- 实时监控页：进入 live 页启动 1 秒 UI refresh timer，离开 live 页停止 timer；找不到目标或没有 FPS 数据时必须显示真实空状态，不切换到假曲线。
- 实时日志：暂停只暂停 UI 追加，继续恢复显示；清空只清空当前面板，不删除持久化日志文件。
- 监控目标页：刷新进程读取真实进程列表，添加进程需要按规则补 `.exe`，watcher 正在运行时先停止且不自动恢复；保存配置后重新读取。
- 监控开始/停止：启动前保存配置并检查已有 watcher；已有 watcher 时显示 PID，不重复启动；停止应清理 FrameScope 相关后台进程并反馈结果。
- 报告页：打开最近报告、打开数据目录、导出诊断、打开历史、打开选中报告、打开目录、导出支持包、重新生成报告、刷新页面都必须绑定真实 handler。

### 定义的按钮 / 状态 / 动画 / 反馈

- 按钮状态：normal、hover、pressed、disabled、loading。长操作按钮在后台任务开始后应至少给出状态文本；下游如果新增 loading 状态，不能改变视觉主题文件。
- 页面切换：nav active 状态清楚，切页后重建页面控件；live 页 timer 生命周期必须可验证。
- 空状态：无报告显示 `暂无可用报告`；无实时数据显示未捕获/等待数据，不伪造 FPS；缺 CSV 时说明无法重新生成。
- 错误反馈：保存失败、启动失败、停止失败、打开失败、报告缺失、运行目录不存在、采样数据缺失都要中文原因。
- WinForms 动画：只允许低成本状态过渡，例如 hover 发光、pressed 明暗、progress 更新、状态点轻微变化；不要求 GPU 粒子、复杂页面动效或采样期间持续装饰动画。

### 生成的下游提示词版本

已生成 `v1`，写入：

- `docs\superpowers\plans\2026-05-15-framescope-ui-interaction-prompt-plan.md`

### 下游允许修改文件

- `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- `src\app\FrameScopeNativeMonitor.UiConfigActions.cs`
- `src\app\FrameScopeNativeMonitor.UiProcessPicker.cs`
- `src\app\FrameScopeNativeMonitor.UiWatcherControls.cs`
- `src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs`
- `src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs`
- `src\app\FrameScopeNativeMonitor.UiDiagnosticActions.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Grid.cs`
- `src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Log.cs`
- `src\ui\FrameScopeReportPage.Actions.cs`
- `src\ui\FrameScopeReportPage.Detail.cs`
- `src\ui\FrameScopeUiState.cs`
- `src\ui\FrameScopeLiveData.cs`
- `src\ui\FrameScopeLiveData.Csv.cs`

### 下游禁止修改文件

- `src\ui\FrameScopeUiTheme.cs`
- `src\ui\FrameScopeRoundedDrawing.cs`
- `src\ui\FrameScopePanels.cs`
- `src\ui\FrameScopeButtons.cs`
- `src\ui\FrameScopeStatusControls.cs`
- `src\ui\FrameScopeReferenceSidebar*.cs`
- `src\app\FrameScopeNativeMonitor.UiVisual*.cs`
- `src\app\FrameScopeNativeMonitor.PageLive.Layout.cs`
- `src\ui\FrameScopeReportPage.Layout.cs`
- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession*.cs`
- `src\app\FrameScopeNativeMonitor.ReportOrchestration*.cs`
- `src\app\FrameScopeNativeMonitor.ReportOpen*.cs`
- `src\app\FrameScopeNativeMonitor.ReportStatus.cs`
- `src\core\`
- `src\monitoring\`
- `src\diagnostics\`
- `src\reporting\`
- `scripts\lightweight\`
- WMI trigger / GameLite / SGuard 相关文件
- `build.ps1`，除非本轮明确新增/删除 C# 文件并独占。

### 尚未确认的问题

- 用户本轮没有提供新的参考图；如果后续有参考图，需要重新拆解明确交互、合理推断和待确认项。
- 是否要下游实际实现 hover/pressed/loading 的新状态细节，需要根据当前视觉组件是否已支持来判断，不能强迫改视觉主题文件。
- 是否允许新增 toast 样式反馈未确认；默认使用现有 `SetStatus` / `SetStatusFromAnyThread` 状态反馈。

### 下一次对话接手点

下一次继续 UI 交互提示词工作时，先读取：

1. `docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Role.md`
2. `docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Worklog.md`
3. `docs\superpowers\plans\2026-05-15-framescope-ui-interaction-prompt-plan.md`

如果用户给参考图，先补充参考图交互拆解，再生成新版本下游提示词；如果用户直接要求交互实现，使用本 Worklog 中的文件边界和验证要求交给下游 UI 交互实现对话框。

## 2026-05-16

### 本次用户要求

用户提供五张 FrameScope Monitor UI 参考图，要求根据参考图片设计 UI 交互提示词和 skill 分配，让下游 UI 交互实现对话框继续编写工作；同时要求默认提示词加入 Codex goal 模式，因为用户已把 goal 模式命令加入 Codex。

### 本次参考图片

- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (1).png`：概览页。
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (3).png`：监控目标页。
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (2).png`：设置页。
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_33_13 (4).png`：报告中心页。
- `C:\Users\misakamiro\Downloads\ChatGPT Image 2026年5月16日 00_38_46.png`：关于我们页。

### 本次已读取的项目资料

- `AGENTS.md`
- `docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md`
- `docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md`
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\modules\ui-interactions.md`
- `docs\modules\software-ui.md`
- `docs\FrameScopeMonitor-progress.md`
- `docs\FrameScopeMonitor-next-prompt.md`
- `docs\FrameScopeMonitor-design-system.md`
- `docs\FrameScopeMonitor-reference-ui-plan.md`
- `docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Role.md`
- `docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Worklog.md`
- `docs\superpowers\plans\2026-05-15-framescope-ui-interaction-prompt-plan.md`

### 本次使用的 skills

- `diagnose`
- `review`
- `improve-codebase-architecture`
- `tdd`
- `verification-before-completion`
- `writing-plans`
- `ui-ux-pro-max`
- `ckm:design-system`
- `plan-design-review`

### 参考图明确表达的交互

- 左侧导航：`概览`、`监控目标`、`报告`、`设置`、`关于我们` 有明确 active 状态。
- 顶部状态卡：监测器、已启用目标、软件状态应跟真实运行/config 状态同步。
- 底部报告生成卡：所有页面都有报告生成进度、完成百分比、就绪状态和打开报告目录按钮。
- 概览页：快速操作包含 `启动监测`、`打开输出目录`；状态卡包含启用目标、捕获链、最近报告、输出目录、诊断模式。
- 监控目标页：表格 checkbox、采样间隔、自动打开报告、刷新进程、添加进程、保存配置、启动监测、停止监测都应可操作。
- 设置页：采样间隔、自动打开报告、详细日志、自动生成诊断报告、性能诊断、保留天数、最大 MB、数据目录选择都应读写配置。
- 报告页：报告列表选择、右侧报告摘要、打开报告目录、打开 HTML 报告、打开详细报告、导出选项状态都需要真实逻辑或明确不可用状态。
- 关于我们页：主要是只读信息；邮箱/网址如果做成可点击，必须真实打开。

### 合理推断的交互补全

- 按钮需要 hover / pressed / disabled / loading，但本轮交互提示词不能要求修改视觉主题文件。
- 长操作需要开始、成功、失败反馈，防止重复点击。
- 表格编辑后保存失败要定位到行和字段。
- 报告列表未选中时，相关按钮应禁用或显示中文原因。
- 路径和长文本需要 tooltip 或状态栏完整提示。

### 需要用户确认的问题

- 参考图未展示实时监控页，所以不能让下游凭空新增 fake FPS 动画；只能沿用现有 live 真实数据/空状态规则。
- `详细报告` 是否对应已有诊断报告、支持包，还是未来新格式，尚未确认。
- `导出格式` 是否需要新增格式尚未确认；默认不新增 report generator 数据结构。
- 关于我们页邮箱/网址是否必须可点击尚未确认；默认能真实打开才可点击。

### 已生成的下游提示词版本

已生成 `v2`，写入：

- `docs\superpowers\plans\2026-05-16-framescope-ui-interaction-reference-prompt-plan.md`

同时更新：

- `docs\orchestration\FrameScopeMonitor-UiInteractionPrompt-Role.md`：新增 Codex goal 模式默认提示词规则。

### 下一次对话接手点

如果用户要求启动下游 UI 交互实现工作，直接复制 `docs\superpowers\plans\2026-05-16-framescope-ui-interaction-reference-prompt-plan.md` 中的 `下游完整可复制提示词 v2`。如果用户继续补充新参考图，应先追加新的“明确表达 / 合理推断 / 待确认问题”，再生成 v3。
