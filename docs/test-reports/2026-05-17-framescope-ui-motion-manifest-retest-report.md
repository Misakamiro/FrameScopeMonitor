# FrameScope Monitor UI Motion + Manifest 合并复测报告

日期：2026-05-17
角色：FrameScope Monitor 测试员
项目路径：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 1. 复测结论

部分通过。

本轮合并后，后端 manifest JSON 问题已通过复测：stable simulator 最新产物的 `framescope-interactive-manifest.json` 可被 PowerShell 默认读取、PowerShell UTF-8 读取和 Node `JSON.parse` 正常解析，关键字段合理。Settings 底部数据目录、日志保留、最大 MB、选择目录按钮区域也比上一轮自然，没有发现新的明显裁切或按钮挤压。

但 UI motion 专项仍未完全通过。重新生成的本轮连续帧仍可复现页面切换中间帧旧页/新页横向混绘，尤其 `targets -> reports` 和 `reports -> settings`，不再是上一轮那种空骨架帧，但仍属于明显跳变/撕裂式过渡。因此不建议直接进入打包，应先交给 UI 交互窗口修复 motion transition。

## 2. 三个原问题逐项复测结果

| 原问题 | 复测结果 | 证据 |
|---|---|---|
| 报告 manifest 不是合法 JSON | 通过 | 最新 stable run manifest 可被 `ConvertFrom-Json` 默认读取、`ConvertFrom-Json -Encoding UTF8`、Node `JSON.parse` 解析 |
| 页面切换动效仍有空骨架帧 | 部分通过 | 空骨架帧未再出现为主要问题，但本轮新帧仍有旧页/新页横向混绘和突兀切换 |
| Settings 底部区域偏挤/裁切 | 通过 | `artifacts\20260517-settings-polish-fix.png` 与本轮 `artifacts\ui-motion-manifest-retest-20260517\settings.png` 检查通过 |

## 3. 已读取资料

- `docs\test-reports\2026-05-17-framescope-ui-polish-motion-test-report.md`
- `docs\implementation-reports\2026-05-17-framescope-ui-motion-implementation-report.md`
- `docs\implementation-reports\2026-05-16-framescope-ui-design-polish-report.md`
- `docs\implementation-reports\2026-05-16-framescope-backend-implementation-report.md`
- `docs\bugfix-reports\2026-05-16-framescope-bugfix-package-report.md`
- `docs\bugfix-reports\2026-05-16-framescope-process-picker-dialog-bugfix-report.md`
- `tests\FrameScopeReportManifestTests.cs`
- `src\reporting\FrameScopeReportGenerator.cs`
- `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- `src\ui\FrameScopeMotion.cs`
- `src\app\FrameScopeNativeMonitor.PageSettings.cs`
- `src\ui\FrameScopeButtons.cs`

本轮调用并应用的 skills：`health`、`verification-before-completion`、`diagnose`、`review`。其中 `review` 按用户只读测试边界使用，只做源码和差异风险复核，不执行自动修复。

## 4. 完整命令验证结果

| 命令 | 结果 | 备注 |
|---|---:|---|
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | 生成 `dist\FrameScopeMonitor-Setup.exe` |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeConfigStoreTests.exe` | PASS | `FrameScopeConfigStoreTests: PASS` |
| `.\tests\FrameScopeCapturePlannerTests.exe` | PASS | `FrameScopeCapturePlannerTests: PASS` |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS | `FrameScopeReportProgressTests: PASS` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS | 沙盒内首次因 AppData 写权限失败；沙盒外复跑 PASS。反射隔离确认失败点为 `%LOCALAPPDATA%\FrameScopeMonitorData\diagnostic-reports` 权限，不是断言失败 |
| `.\tests\FrameScopePubgSimulatorTests.exe` | PASS | `FrameScopePubgSimulatorTests: PASS` |
| `.\tests\FrameScopeUiStateTests.exe` | PASS | `FrameScopeUiStateTests: PASS` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | `FrameScopeReportManifestTests: PASS` |
| `node .\tests\chart-sampling-tests.js` | PASS | 系统 `node` 命中 WindowsApps `Access is denied`；使用 Codex bundled Node 后 PASS |
| `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo` | PASS | 0 warnings, 0 errors |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario stable -DurationSeconds 4` | PASS | `monitorExit=0`, `reportExit=0`, `frames=240`, `hasFrameData=true` |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS | exit 0，仅既有 LF/CRLF warning |

## 5. Manifest JSON 专项验证

最新 stable run：

`artifacts\pubg-simulator\20260517-031040-816-stable\runs\SyntheticPUBG-20260517-031041`

manifest 路径：

`artifacts\pubg-simulator\20260517-031040-816-stable\runs\SyntheticPUBG-20260517-031041\charts\framescope-interactive-manifest.json`

验证结果：

- `Get-Content -Raw <manifest> | ConvertFrom-Json`：PASS
- `Get-Content -Raw -Encoding UTF8 <manifest> | ConvertFrom-Json`：PASS
- `node -e "JSON.parse(...)" <manifest>`：PASS

字段检查：

- `hasFrameData=True`
- `reportKind=full`
- `frames=240`
- `processSamples=59`
- `systemSamples=7`
- `frameCaptureStatus=captured`

源码复核：`FrameScopeReportGenerator.SerializeArtifactJson()` 先用 `JavaScriptSerializer` 序列化，再将非 ASCII JSON 文本转义为 `\uXXXX`，与新增 `FrameScopeReportManifestTests.exe` 的覆盖目标一致。

## 6. UI 静态截图结果

本轮重新生成目录：`artifacts\ui-motion-manifest-retest-20260517\`

| 页面 | 截图 | 结果 |
|---|---|---|
| overview | `overview.png` | PASS，内容完整，active nav 正确 |
| targets | `targets.png` | PASS，表格、进程输入、按钮行可读；本轮 targets 截图未触发 DataGridView harness 卡住 |
| settings | `settings.png` | PASS，底部数据目录、保留天数、最大 MB、选择目录、保存/恢复按钮未见明显裁切；路径输入框显示末尾，符合长路径处理预期 |
| reports | `reports.png` | PASS，报告列表、摘要、快捷操作按钮可读 |
| about | `about.png` | PASS，未见明显溢出或重叠 |
| live normalized overview | `live-normalized-overview.png` | PASS，`--ui-page live` 归一到 overview，Live 页面未恢复 |

补充：`artifacts\20260517-settings-polish-fix.png` 与本轮 `settings.png` 视觉一致，Settings 底部原问题已复测通过。

## 7. UI 动画连续帧检查结果

先检查了负责人提供的新目录：

`artifacts\ui-motion-20260517-skeleton-final-frames`

该目录帧时间早于本轮构建，因此按要求重新生成本轮连续帧：

`artifacts\ui-motion-manifest-retest-20260517-frames`

覆盖项：

- `transition-overview-targets-00.png` 到 `transition-overview-targets-09.png`
- `transition-targets-reports-00.png` 到 `transition-targets-reports-09.png`
- `transition-reports-settings-00.png` 到 `transition-reports-settings-09.png`

复核结论：

- 未见整页白屏或黑屏。
- 原先大面积空卡片/空控件骨架问题有明显改善。
- 仍发现明显混绘/跳变：`transition-targets-reports-04.png` 中左侧主区域已显示 reports 内容，但右侧仍残留 targets/settings 区块，画面被竖向切开；`transition-reports-settings-04.png` 中 settings 页面出现时右侧捕获链区域残留旧报告页/导出选项痕迹。
- `overview -> targets` 基本可接受，但 `targets -> reports`、`reports -> settings` 仍不满足“active nav 和页面内容同步、无明显跳变”的专项验收要求。
- `FrameScopeButtons.cs` 的 nav/button active immediate 兼容扩展未观察到按钮 hover/pressed 独立失败；主要问题集中在 `UiRouting` 页面快照 overlay/延迟提交策略。

## 8. 真实交互冒烟结果

安全交互证据目录：

`artifacts\ui-motion-manifest-retest-20260517-interaction-safe`

执行结果：

- 左侧导航：overview、targets、reports、settings 可点击切换。
- Settings：checkbox 切换、保存设置、选择目录并取消、恢复默认均可操作；配置文件已恢复。
- Targets：进程选择器可从输入框打开，可取消；也可从刷新按钮打开。
- Targets：选择一个可见安全进程并添加，行可出现在目标表格；配置文件已恢复。
- Reports：页面可打开，按钮区域仍可见；源码复核 `FrameScopeReportPage.Actions.cs` 仍保留真实 handler，不是静态假按钮。

配置保护：

- 测试前 SHA256：`651834AE4FB0F8D1D8F565BD2DF1CB0DBEF44D377B1AC4B420C66DE963B75AC3`
- 测试后恢复 SHA256：`651834AE4FB0F8D1D8F565BD2DF1CB0DBEF44D377B1AC4B420C66DE963B75AC3`
- `CONFIG_RESTORED=True`

说明：首次交互 harness 因等待时间太短，导航未稳定时后续坐标误点启动了 watcher；已定位为本轮测试 harness 误点，停止了该残留 watcher，并用更长等待重跑安全交互，不作为产品功能失败计入。

## 9. Simulator 和报告页面验证

stable simulator 输出：

- `outputRoot`: `artifacts\pubg-simulator\20260517-031040-816-stable`
- `runDir`: `artifacts\pubg-simulator\20260517-031040-816-stable\runs\SyntheticPUBG-20260517-031041`
- `monitorExit=0`
- `reportExit=0`
- `presentMonCaptureMode=process_name`
- `presentMonCsvRows=240`
- `frameCaptureStatus=captured`
- `hasFrameData=true`
- `frames=240`
- `reportKind=full`

报告三件套：

- `charts\framescope-interactive-report.html`：存在，40277 bytes
- `charts\framescope-interactive-data.js`：存在，59371 bytes
- `charts\framescope-interactive-manifest.json`：存在，1164 bytes

HTML 内容检查：

- chart canvas：PASS
- gauges：PASS
- process rows：PASS
- summary rows：PASS
- data include：PASS
- chart sampling script：PASS

Edge/headless 截图：

- 截图路径：`artifacts\ui-motion-manifest-retest-20260517\edge-report.png`
- 结果：PASS，页面非空，可见 FrameScope 报告侧栏、gauges、FPS canvas 区域和导航按钮。

## 10. 残留进程检查

最终检查未发现以下残留：

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`

中途发现一次本轮交互 harness 误点留下的 `FrameScopeMonitor.exe --watcher --config ...`，已确认命令行属于本轮项目测试残留并停止。最终复查为空。

## 11. 仍有问题

### FSM-RETEST-MOTION-001

- 严重级别：高
- 复现步骤：
  1. 打开 `artifacts\ui-motion-manifest-retest-20260517-frames\transition-targets-reports-04.png`。
  2. 打开 `artifacts\ui-motion-manifest-retest-20260517-frames\transition-reports-settings-04.png`。
  3. 对比同序列前后帧。
- 实际结果：页面切换中间帧出现旧页面和新页面横向混绘/残影，部分区域已切到目标页，右侧仍残留前一页内容；视觉上像页面被竖向切开。
- 期望结果：页面切换期间要么保持完整旧页快照，要么一次性切到完整新页并进入轻量过渡，不能出现旧新页面混在同一帧的撕裂效果。
- 可能涉及文件：
  - `src\app\FrameScopeNativeMonitor.UiRouting.cs`
  - `src\ui\FrameScopeMotion.cs`
  - `src\ui\FrameScopeButtons.cs`
- 建议交给：UI 交互窗口，必要时 UI 设计窗口协助确认过渡策略。

## 12. 未覆盖项

- 未启动真实 PUBG。stable simulator 覆盖了 report/sampler/report generator 链路，但未覆盖真实反作弊、真实 ETW、独占全屏、真实游戏生命周期。
- 未安装、删除或迁移 GameLite/WMI trigger。
- 未执行安装包安装/卸载流程。
- Reports 外部打开目录/HTML/详细报告只做了源码 handler 复核和页面冒烟，未长时间验证用户默认浏览器/资源管理器的完整交互。

## 13. 是否建议进入 bug 修复及打包

建议先进入 bug 修复，不建议直接打包。

理由：manifest JSON 和 Settings polish 已达到复测通过标准，但 UI motion 专项仍有可见中间帧混绘。这个问题属于本轮专项验收目标，建议先交给 UI 交互窗口修 `FSM-RETEST-MOTION-001`，再做一次轻量复测；复测通过后再进入打包。
