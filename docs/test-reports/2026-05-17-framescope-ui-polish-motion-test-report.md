# FrameScope Monitor UI Polish + Motion 专项测试报告

日期：2026-05-17
角色：FrameScope Monitor 测试员
项目路径：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 1. 测试结论

部分通过。

基础构建、单元/回归测试、stable simulator、HTML 报告页面截图、UI 静态截图和真实点击冒烟均完成。`live` 页面未恢复，`--ui-page live` 已归一到 overview。

但本轮 UI polish + motion 不能建议直接打包：动画连续帧仍能看到页面切换早期空骨架/控件后补，Settings 页面仍有局部裁切和拥挤；stable simulator 生成的 `framescope-interactive-manifest.json` 不是合法 JSON，`ConvertFrom-Json` 解析失败。

## 2. 测试环境

- 系统：Windows，本机 PowerShell
- 日期：2026-05-17
- Node：`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`
- Edge：`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`
- 测试原则：只测试，不修代码，不提交，不安装/删除 WMI trigger
- 配置保护：交互测试期间临时修改 `framescope-config.json` 的 `DataRoot` 用于空报告状态验证；结束后已按 SHA256 恢复，`configRestored=True`

## 3. 读取的实现报告和文档

- `AGENTS.md`
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\modules\software-ui.md`
- `docs\modules\ui-interactions.md`
- `docs\modules\backend-monitoring.md`
- `docs\implementation-reports\2026-05-16-framescope-ui-design-polish-report.md`
- `docs\implementation-reports\2026-05-17-framescope-ui-motion-implementation-report.md`
- `docs\FrameScopeMonitor-progress.md`
- `docs\FrameScopeMonitor-next-prompt.md`

调用并应用的 skills：

- `health`：按项目既有构建、测试、chart、simulator、diff check 做健康检查
- `diagnose`：用截图、连续帧、点击冒烟和报告解析建立可复现证据
- `review`：只读归类问题、严重程度和建议修复窗口，不改代码
- `verification-before-completion`：最终结论只基于本轮 fresh 命令和截图证据

## 4. 命令验证结果

| 命令 | 结果 | 备注 |
|---|---:|---|
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | 生成 `dist\FrameScopeMonitor-Setup.exe` |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeConfigStoreTests.exe` | PASS | `FrameScopeConfigStoreTests: PASS` |
| `.\tests\FrameScopeCapturePlannerTests.exe` | PASS | `FrameScopeCapturePlannerTests: PASS` |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS | `FrameScopeReportProgressTests: PASS` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS | `FrameScopeDiagnosticsTests: PASS` |
| `.\tests\FrameScopePubgSimulatorTests.exe` | PASS | `FrameScopePubgSimulatorTests: PASS` |
| `.\tests\FrameScopeUiStateTests.exe` | PASS | 包含 motion helper、process picker 规则测试 |
| `node .\tests\chart-sampling-tests.js` | PASS | 使用 Codex bundled Node 放到 PATH 前面执行 |
| `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo` | PASS | 0 warnings, 0 errors |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario stable -DurationSeconds 4` | PASS | `monitorExit=0`, `reportExit=0`, `frames=240` |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS after PowerShell call correction | 需用 `& "...\git.exe" diff --check`；最终 exit 0，仅既有 LF/CRLF warning |

## 5. UI 静态截图结果

新截图目录：`artifacts\ui-polish-motion-test-20260517\`

| 页面 | 截图 | 结果 |
|---|---|---|
| overview | `overview.png` | PASS。卡片层级清楚，无明显重叠，`live` 未出现 |
| targets | `targets.png` | PASS。表格、进程输入、按钮行可用；原生滚动条仍突兀但未阻断 |
| settings | `settings-retry.png` | PARTIAL。独立重试截图正确，但数据目录按钮/下方区域仍偏挤，选择目录按钮有局部裁切感 |
| reports | `reports.png` | PASS。列表、摘要、快捷按钮可读 |
| about | `about.png` | PASS。布局稳定，无明显裁切 |
| live normalized overview | `live-normalized-overview.png` | PASS。`--ui-page live` 输出 overview，未恢复 Live 页面 |

补充：快速连续截 `targets -> settings` 时，第一次 `settings.png` 捕获到 targets/settings 混合画面。独立重试通过，判断更像截图 harness 在 screen-capture 页面的时序问题，但仍应记录。

## 6. 动画连续帧/视频检查结果

检查目录：`artifacts\ui-motion-20260517-frames\`

覆盖项：

- `overview -> targets`：`transition-overview-targets-00..08.png`
- `targets -> reports`：`transition-targets-reports-00..08.png`
- `reports -> settings`：`transition-reports-settings-00..08.png`
- sidebar hover：`sidebar-hover-about-00..06.png`、`sidebar-hover-return-00..04.png`
- button hover / pressed：`button-hover-save-00..05.png`、`button-pressed-save-00..04.png`
- status dirty feedback：`status-dirty-feedback-00..08.png`

结论：

- 未看到整页白屏或黑屏。
- 仍看到明显早期空骨架帧：例如 `transition-overview-targets-01.png` 只有大块空卡片和占位区域，文字/表格尚未出现；`transition-targets-reports-01.png` 同样出现大面积无文本卡片。
- 部分页切换早期 active nav 与内容不同步：例如 reports 内容出现时 sidebar active 仍停在 targets。
- button hover/pressed 和 status dirty 反馈没有闪色，但动效幅度较弱，只能判断为轻量过渡，不是明显闪烁。

## 7. 交互检查结果

交互日志：`artifacts\ui-polish-motion-test-20260517\interaction-smoke.log`

已执行真实点击：

- 左侧导航：overview、targets、reports、settings、about 均点击切换，无卡死。
- Settings：切换 checkbox、保存设置、恢复默认、选择目录并取消。
- Targets：刷新进程打开 picker、取消 picker、再次打开 picker、选择首个安全进程并走添加逻辑。
- Reports：打开报告/输出目录入口、打开 HTML 报告入口、打开详细报告入口，在临时空 DataRoot 下验证无卡死。

配置恢复：

- 测试前备份：`artifacts\ui-polish-motion-test-20260517\framescope-config.before-interaction.json`
- 恢复验证：`configRestored=True`
- SHA256：`651834AE4FB0F8D1D8F565BD2DF1CB0DBEF44D377B1AC4B420C66DE963B75AC3`

Handler 只读核对：

- `src\app\FrameScopeNativeMonitor.UiRouting.cs`：nav click 仍调用 `ShowPage(key)`。
- `src\app\FrameScopeNativeMonitor.UiConfigActions.cs` / `PageSettings.cs`：Settings 控件变更、保存、恢复、选择目录仍连接真实逻辑。
- `src\app\FrameScopeNativeMonitor.UiProcessPicker.cs`：picker 打开/取消/选择均有状态反馈。
- `src\ui\FrameScopeReportPage.Actions.cs`：Reports 主要按钮仍绑定真实 action，不是静态假按钮。

## 8. Simulator 和报告生成结果

stable simulator 输出：

- `outputRoot`: `artifacts\pubg-simulator\20260517-014019-881-stable`
- `runDir`: `artifacts\pubg-simulator\20260517-014019-881-stable\runs\SyntheticPUBG-20260517-014020`
- `monitorExit=0`
- `reportExit=0`
- `presentMonCaptureMode=process_name`
- `presentMonCsvRows=240`
- `hasFrameData=true`
- `frames=240`
- `reportKind=full`

报告文件：

- `charts\framescope-interactive-report.html`：存在
- `charts\framescope-interactive-data.js`：存在
- `charts\framescope-interactive-manifest.json`：存在但不是合法 JSON

manifest regex 字段提取：

- `hasFrameData=true`
- `reportKind=full`
- `frames=240`
- `processSamples=59`
- `systemSamples=2`
- `frameCaptureStatus=captured`
- `presentMonCsvRows=240`

manifest 解析问题：

- PowerShell `ConvertFrom-Json` 失败：`Invalid object passed in, ':' or '}' expected.`
- 失败位置在 `frameCaptureMessage` 附近：该字符串内容后缺少正常 closing quote，导致后续 `presentMonCsvBytes` 被并入坏字符串。

HTML 内容检查：

- chart canvas：PASS
- gauges：PASS
- process rows：PASS
- summary rows：PASS
- data include：PASS
- chart sampling script：PASS

Edge headless 报告截图：

- 路径：`artifacts\ui-polish-motion-test-20260517\edge-report.png`
- 大小：463923 bytes
- 结果：PASS，页面非空，canvas/gauge/report layout 可见。

## 9. 残留进程检查

最终检查未发现残留，命令输出为空：

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`

报告写入后重新执行 `& "C:\Program Files\Git\cmd\git.exe" diff --check`：PASS，仍只有既有 LF/CRLF warning。

## 10. 发现的问题列表

### FS-UI-MOTION-001

- 严重程度：高
- 复现步骤：
  1. 查看 `artifacts\ui-motion-20260517-frames\transition-overview-targets-01.png`。
  2. 查看 `artifacts\ui-motion-20260517-frames\transition-targets-reports-01.png`。
  3. 对比同序列第 02、08 帧。
- 实际结果：页面切换早期出现大面积空骨架/空卡片，文字和控件后补；部分帧 active nav 与实际内容不同步。
- 期望结果：切页时不出现明显空内容帧；目标页内容、active nav、状态应一起进入稳定过渡。
- 可能涉及文件：
  - `src\app\FrameScopeNativeMonitor.UiRouting.cs`
  - `src\ui\FrameScopeMotion.cs`
  - 相关页面 layout partial
- 建议修复窗口：UI 交互窗口为主，UI 设计窗口协助确认过渡策略。

### FS-UI-SETTINGS-001

- 严重程度：中
- 复现步骤：
  1. 打开 `artifacts\ui-polish-motion-test-20260517\settings-retry.png`。
  2. 查看 Settings 主表单底部的数据目录、日志保留、最大 MB、选择目录按钮区域。
- 实际结果：数据目录和底部按钮区域偏挤，`选择目录` 按钮视觉上贴近/局部被下边界挤压；数值输入下方的小标签也显得不自然。
- 期望结果：Settings 输入框、路径、checkbox、按钮在同一节奏内，不出现裁切感或突兀拥挤。
- 可能涉及文件：
  - `src\app\FrameScopeNativeMonitor.PageSettings.cs`
  - `src\app\FrameScopeNativeMonitor.UiVisualSections.cs`
  - `src\app\FrameScopeNativeMonitor.UiVisualButtons.cs`
- 建议修复窗口：UI 设计窗口。

### FS-REPORT-MANIFEST-001

- 严重程度：高
- 复现步骤：
  1. 运行 stable simulator：`powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario stable -DurationSeconds 4`
  2. 对生成的 `charts\framescope-interactive-manifest.json` 执行 `Get-Content -Raw | ConvertFrom-Json`。
- 实际结果：manifest 文件存在，字段可用 regex 提取，但不是合法 JSON；`frameCaptureMessage` 字符串附近破坏了 JSON 结构。
- 期望结果：manifest 必须是合法 UTF-8 JSON，能被 `ConvertFrom-Json`、Node `JSON.parse` 和后续工具稳定解析。
- 可能涉及文件：
  - `src\reporting\FrameScopeReportGenerator.cs`
  - `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs`
  - `src\app\FrameScopeNativeMonitor.ReportStatus.cs`
- 建议修复窗口：后端/报告生成或 bug 修复窗口。

### FS-UI-SCREENSHOT-001

- 严重程度：低到中
- 复现步骤：
  1. 快速连续执行 UI 截图：overview、targets、settings、reports、about、live。
  2. 查看首次生成的 `artifacts\ui-polish-motion-test-20260517\settings.png`。
- 实际结果：首次 settings 截图捕获到 targets/settings 混合画面；独立重试 `settings-retry.png` 正常。
- 期望结果：截图 harness 在 screen-capture 页也应等待目标页完全稳定，不应捕获上一页残影。
- 可能涉及文件：
  - `src\app\FrameScopeNativeMonitor.UiScreenshots.cs`
  - `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- 建议修复窗口：bug 修复窗口。此项更像 harness 时序问题，不直接判定主程序页面功能失败。

## 11. 未覆盖项

- 未使用真实 PUBG、真实反作弊环境、真实全屏/无边框渲染链路。
- 未安装、删除或迁移任何 GameLite/WMI trigger。
- 未做安装包安装/覆盖安装/卸载验证。
- 未进行长时间人工视觉观感测试，motion 判断基于已有连续帧和本轮点击冒烟。
- Reports 外部打开浏览器/文件夹行为只做了点击冒烟与 handler wiring 检查，没有验证用户默认浏览器完整交互流程。

## 12. 是否建议进入 bug 修复及打包

建议先进入 bug 修复，不建议直接打包。

优先级建议：

1. 先修 `FS-REPORT-MANIFEST-001`，因为 manifest 是报告生成契约，当前不是合法 JSON。
2. 再修 `FS-UI-MOTION-001`，因为本轮专项目标就是去掉闪烁/跳变/控件后补，连续帧仍能复现。
3. 同步做 `FS-UI-SETTINGS-001` 的 Settings polish 微调。
4. `FS-UI-SCREENSHOT-001` 可作为 harness 稳定性修复，不应阻塞主功能，但会影响后续验收可靠性。
