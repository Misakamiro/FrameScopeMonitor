# 2026-05-17 FrameScope UI Navigation Performance Retest Report

## 1. 当前复测结论

通过。

本轮只复测 UI navigation performance 和必要轻量回归。页面切换在 fresh 连续帧和真实 GUI 快速点击中均表现为稳定瞬时 commit：未看到 Windows wait cursor / 转圈圈、未看到明显卡住后再加载、未看到旧页/新页混绘、未看到空骨架帧，active nav 与页面内容同步。`--ui-page live` 仍归一到 overview。

## 2. 页面切换体感结果

真实 GUI 启动 `FrameScopeMonitor.exe` 后执行：

- overview -> targets
- targets -> reports
- reports -> settings
- settings -> overview
- 连续快速点击 targets / reports / settings / overview
- 重复点击当前 overview

结果：

- PASS：导航点击采样中未出现 wait cursor / app starting cursor。
- PASS：未观察到“转圈圈后再加载”的切页等待感。
- PASS：未观察到旧页和新页横向混绘。
- PASS：未观察到空卡片或控件骨架帧。
- PASS：快速连点后最终停在 overview，active nav 与页面内容同步。
- PASS：重复点击当前页无异常。
- PASS：nav hover / active、button hover / pressed、status 局部反馈未见明显抖动或闪烁。

补充观察：Reports 打开 HTML/详细报告、Settings 保存/恢复默认/选择目录这些非导航动作会触发外部打开或配置写入，采样到 OS cursor 变化；该现象不发生在导航切页路径，本轮不列为 UI navigation 阻塞。

## 3. 快速连点结果

PASS。

真实 GUI 快速连点序列完成后，页面停留在 overview，未卡死，未出现混绘或白屏/黑屏。重复点击当前 overview 没有产生异常跳变。

## 4. 连续帧检查结果

原实现报告连续帧目录：

`artifacts\ui-navigation-performance-20260517-frames-v3`

本轮 rebuild 后重新生成的 fresh 连续帧目录：

`artifacts\ui-navigation-performance-20260517-frames-v3-retest`

检查范围：

- `transition-overview-targets-00.png` 到 `transition-overview-targets-14.png`
- `transition-targets-reports-00.png` 到 `transition-targets-reports-14.png`
- `transition-reports-settings-00.png` 到 `transition-reports-settings-14.png`
- `transition-settings-overview-00.png` 到 `transition-settings-overview-14.png`

逐帧结论：

- overview -> targets：PASS。00 为完整旧 overview，01 起为完整 targets；无并排混绘、无残影重叠、无空骨架。
- targets -> reports：PASS。00 为完整旧 targets，01 起为完整 reports；active nav 与内容同步，无横向撕裂。
- reports -> settings：PASS。00 为完整旧 reports，01 起为完整 settings；无白屏/黑屏闪烁。
- settings -> overview：PASS。00 为完整旧 settings，01 起为完整 overview；无旧页控件残留。

结论：页面切换已从旧的撕裂/混绘风险收敛为稳定瞬时替换，不要求 slide 动画。

## 5. 关键按钮回归结果

Targets：

- PASS：刷新进程可点击，无卡死。
- PASS：进程选择器可打开，可取消。
- PASS：重新打开进程选择器并选择安全进程后，可点击添加进程。
- PASS：保存配置可点击，有状态/目标数量变化，说明不是假按钮。

Reports：

- PASS：打开目录可点击并触发外部目录打开。
- PASS：打开 HTML 报告可点击并触发外部打开。
- PASS：打开详细报告可点击并触发外部打开。
- PASS：报告列表行的打开按钮可点击。
- PASS：刷新/重新生成入口所在的报告区未见 handler 断线或卡死。

Settings：

- PASS：checkbox 切换后状态反馈正常。
- PASS：保存设置可点击。
- PASS：恢复默认可点击。
- PASS：选择目录可打开选择流程，取消后程序保持可用。

测试副作用处理：Settings 的“恢复默认/保存”会改写运行配置，这是预期 handler 行为，不作为缺陷。测试结束前已按测试开始截图中的 6 个启用目标状态恢复 `framescope-config.json` 的目标启用数量，避免留下本轮测试造成的配置副作用。

Live：

- PASS：执行 `FrameScopeMonitor.exe --ui-screenshot <temp_png> --ui-page live`，生成截图为 overview 页面，确认 live 未恢复且仍归一到 overview。

## 6. 命令验证结果

| 命令 | 结果 | 关键输出 |
| --- | --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | `Build complete: ...\dist\FrameScopeMonitor-Setup.exe` |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeUiStateTests.exe` | PASS | `FrameScopeUiStateTests: PASS` |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS | `FrameScopeReportProgressTests: PASS` |
| `node .\tests\chart-sampling-tests.js` | FAIL by environment | WindowsApps `node.exe` returned `Access is denied` |
| bundled Node `chart-sampling-tests.js` | PASS | `chart-sampling-tests: PASS` |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS | exit 0；仅有 CRLF warning |

Node 说明：系统 `node` 命中 WindowsApps `Access is denied`，已按要求改用 Codex bundled Node 验证通过。

## 7. 残留进程检查

测试结束后检查以下进程：

- FrameScopeMonitor：NOT RUNNING
- PresentMon：NOT RUNNING
- FrameScopeProcessSampler：NOT RUNNING
- FrameScopeSystemSampler：NOT RUNNING
- FrameScopeReportGenerator：NOT RUNNING
- FakePresentMon：NOT RUNNING
- TslGame：NOT RUNNING
- GameLite：NOT RUNNING

## 8. 是否仍有 UI navigation 阻塞

否。

本轮未复现“切换界面像转圈圈后再加载”的导航阻塞，也未复现旧页/新页混绘、空骨架、active nav 不同步。

## 9. 是否建议进入最终打包更新

建议进入最终打包更新。

前提：本轮只覆盖 navigation performance 轻量复测和指定关键按钮冒烟，不重新扩大到 manifest、Settings polish、后端/report generator/GameLite/WMI/SGuard 完整验收。
