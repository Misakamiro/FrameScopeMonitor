# FrameScope UI Navigation Performance Report

日期：2026-05-17
角色：FrameScope Monitor UI 交互性能 / 页面切换动画修复负责人
范围：只处理页面切换性能、缓存、切换 commit 和连续帧验证；不恢复 Live 页面，不修改后端采样、report generator、GameLite/WMI/SGuard 或 packaging。

## 1. 当前卡顿根因

用户反馈的“点击后像转圈圈一样等一会儿才加载出来”，主要来自上一轮为修复混绘而采用的保守页面切换路径：

- full-window snapshot overlay 曾使用 WaitCursor 和延迟释放，虽然避免了撕裂，但会制造等待感。
- `ShowPage()` 每次切换都重建目标页控件，Reports / Targets / Settings 的复杂控件和报告/状态读取会放大卡顿。
- commit 阶段使用整窗 `RedrawWindow(... UpdateNow/EraseNow)` 强制同步刷新，实测缓存命中切页仍可到 180-360ms。
- commit 完成时同步 `UpdateWatcherStatus()`，状态刷新会读取 watcher state 和报告进度状态，容易把页面出现时间拖慢。

## 2. 是否实现页面缓存

已实现。`FrameScopeNativeMonitor.UiRouting.cs` 增加 `pageCache`，缓存 `overview / targets / reports / settings / about` 页面及其页面级控件引用。首次构建后再次切换直接复用缓存页面，实测缓存命中时 `buildMs=0`。

缓存失效路径：

- 保存设置后：标记 `overview / targets / reports / settings` dirty。
- 恢复默认后：标记 `overview / targets / reports / settings` dirty。
- 添加目标进程后：标记 `overview / settings / reports` dirty。
- Reports 刷新 / 重新生成后：刷新 reports 缓存。

## 3. 是否移除或弱化全窗口快照等待

已移除 active 切页路径中的 full-window snapshot 等待。`FrameScopeWindowSnapshotOverlay` 类型也已从 `FrameScopeMotion.cs` 移除，避免后续误用 WaitCursor overlay。

当前策略：

- 不做 page slide。
- 不使用透明/局部 overlay。
- 缓存页直接在 redraw lock 内一次性替换。
- active nav 与 content page 在同一个 commit 中更新。
- commit 后只做局部 `contentHost` / sidebar redraw，整窗 `RedrawWindow` 不再使用同步 `UpdateNow/EraseNow`。
- watcher/status 刷新改为 commit 后 `BeginInvoke` 排队，不再阻塞页面出现。

## 4. 每个页面切换耗时

耗时来源：`framescope-watcher.log` 中 `ui-page-switch` 诊断日志，取本轮最终 v3 连续帧录制时段。

| 路径 | cached | buildMs | commitMs | totalMs |
|---|---:|---:|---:|---:|
| app initial -> overview | False | 12 | 3 | 16 |
| overview -> targets | True | 0 | 148 | 148 |
| targets -> reports | True | 0 | 126 | 126 |
| reports -> settings | True | 0 | 97 | 97 |
| settings -> overview | True | 0 | 145 | 145 |
| rapid overview -> targets | True | 0 | 63 | 63 |
| rapid targets -> reports | True | 0 | 88 | 88 |
| rapid reports -> settings | True | 0 | 55 | 55 |
| rapid settings -> overview | True | 0 | 145 | 145 |

## 5. 连续帧 / 截图路径

最终证据目录：

`artifacts\ui-navigation-performance-20260517-frames-v3`

包含：

- `transition-overview-targets-00.png` 到 `transition-overview-targets-14.png`
- `transition-targets-reports-00.png` 到 `transition-targets-reports-14.png`
- `transition-reports-settings-00.png` 到 `transition-reports-settings-14.png`
- `transition-settings-overview-00.png` 到 `transition-settings-overview-14.png`
- `static-overview.png`
- `static-targets.png`
- `static-reports.png`
- `static-settings.png`
- `static-about.png`
- `rapid-nav-final-overview.png`
- `live-normalized-overview.png`
- `capture-summary.json`

逐帧抽查结论：

- `overview -> targets`：早期帧从完整 Overview 切到完整 Targets，未见空骨架帧。
- `targets -> reports`：`01` 起为完整 Reports，未见旧 Targets 左半边 + 新 Reports 右半边混绘。
- `reports -> settings`：`01` 起为完整 Settings，active nav 与内容同步。
- `settings -> overview`：`01` 起为完整 Overview，未见撕裂式横向混绘。
- 快速连点后最终回到完整 Overview，未发现残留半页或 wait cursor。

## 6. Handler 回归结果

本轮没有重做视觉主题，也没有改后端 handler。复核结果：

- 左侧导航：`FrameScopeReferenceSidebar.NavigationRequested` 仍调用 `ShowPage(e.Key)`。
- `ShowPage("live")` / `--ui-page live`：仍归一到 `overview`，未恢复 Live/FPS 实时页，未调用 `StartLiveRefresh()`。
- Reports：
  - 打开报告目录：`OpenSelectedReportFolder()`
  - 打开 HTML 报告：`OpenSelectedReport()`
  - 打开详细报告：`OpenSelectedDetailedReport(Button)` -> `FrameScopeDiagnostics.GenerateReport(...)` -> 打开 markdown
  - 刷新列表：`RefreshCachedPage("reports")`
  - 重新生成：`RegenerateSelectedReport(Button)`
- Settings：
  - checkbox / 输入框变更：`AttachConfigDirtyFeedback(...)` -> `SetConfigDirtyStatus()`
  - 保存设置：`SaveConfigFromGrid()`
  - 恢复默认：`ResetConfigToDefaultsFromUi()`
  - 选择目录：`BrowseDataRoot()`
- Targets：
  - 刷新进程：`RefreshProcessList()`
  - 添加进程：`AddSelectedProcess()`
  - 保存配置：`SaveConfigFromGrid()`
  - 启动监控：`StartWatcher()`
  - 停止监控：`StopWatcher()`
  - 进程选择器切页后的预载：`RefreshProcessList(false)` 异步预载 autocomplete，不阻塞页面切换。

真实交互冒烟：

- 已启动真实 GUI 并用坐标点击左侧导航，覆盖 overview / targets / reports / settings / about 及快速连续切换。
- Settings / Targets / Reports handler 本轮以 wiring 复核和已有 UI state tests 验证为主；未在本轮自动点击“打开报告/打开目录/打开详细报告”，避免测试过程额外打开浏览器、Explorer 或生成诊断报告影响用户桌面。

## 7. 测试命令结果

| 命令 | 结果 | 备注 |
|---|---:|---|
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | 输出 `Build complete: ...\dist\FrameScopeMonitor-Setup.exe` |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | 输出 `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeUiStateTests.exe` | PASS | 输出 `FrameScopeUiStateTests: PASS` |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS | 输出 `FrameScopeReportProgressTests: PASS` |
| `node .\tests\chart-sampling-tests.js` | PASS with bundled Node | WindowsApps `node.exe` 返回 Access denied；将 `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin` 放到 PATH 前面后同一命令 PASS |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS | exit 0；仅输出既有 LF/CRLF warning：`README.md`、`build.ps1`、`framescope-config.example.json` |

## 8. 残留进程检查

最终检查未发现以下残留进程：

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`

## 9. 修改文件清单

核心 UI 交互源码：

- `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- `src\app\FrameScopeNativeMonitor.UiFields.cs`
- `src\app\FrameScopeNativeMonitor.UiConfigActions.cs`
- `src\app\FrameScopeNativeMonitor.UiProcessPicker.cs`
- `src\ui\FrameScopeReportPage.Actions.cs`
- `src\ui\FrameScopeReportPage.Detail.cs`
- `src\ui\FrameScopeMotion.cs`

测试 / 交付支撑：

- `tests\FrameScopeUiStateTests.cs`
- `docs\implementation-reports\2026-05-17-framescope-ui-navigation-performance-report.md`
- `artifacts\ui-navigation-performance-20260517-frames-v3\*`

没有修改：

- `src\reporting\`
- `src\core\`
- `src\monitoring\`
- `src\diagnostics\`
- `scripts\lightweight\`
- GameLite / WMI / SGuard
- `packaging\`
- `build.ps1`
- Live 页面没有恢复

说明：当前 git 工作树仍存在既有/并行变更，且该仓库状态显示大量源码为 untracked；本线程交付范围以上述交互源码、测试、报告和 artifacts 为准。

## 10. 是否建议测试员复测

建议测试员做一次轻量复测，重点只看页面切换体感和连续帧：

- overview -> targets
- targets -> reports
- reports -> settings
- settings -> overview
- 快速连续点击导航

预期：页面应在约 100-150ms 内完成稳定切换；不应出现 wait cursor、空骨架、旧/新页横向混绘、active nav 与页面内容不同步。
