# FrameScope full-installed QA PARTIAL 修复报告

Date: 2026-05-30

Status: PASS for the 3 scoped PARTIAL items.

Source:

```text
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d
```

Base QA report:

```text
docs\test-reports\2026-05-30-framescope-full-installed-simulated-qa-report.md
```

Evidence root:

```text
artifacts\qa0530-full-installed
```

Latest focused evidence:

```text
artifacts\qa0530-full-installed\pfix-td-installed-final
artifacts\qa0530-full-installed\pfix-overflow-installed-final
artifacts\qa0530-full-installed\pfix-target-settings-final
artifacts\qa0530-full-installed\pfix-verification-final
```

## 结论

3 个 PARTIAL 项均已修复或补齐独立证据：

1. 报告用户可见 target 文案已优先显示配置目标名，例如 `Counter-Strike 2`，同时保留技术诊断里的进程名 `cs2.exe`。
2. 报告图表页 `1280x720` 横向溢出已消失。最新 probe 中 `fps-default-1280x720` 为 `scrollWidth=1280`、`clientWidth=1280`。
3. Target 新增/编辑/删除、Target edit modal、Settings 保存与重启保持均有独立 WebView2 截图级证据，且使用临时 profile，不触碰真实用户配置。

本轮没有启动真实 Valorant / 无畏契约，没有启动真实 BF6，也没有启动任何真实游戏。目标验证使用 fake target exe、fake PresentMon、synthetic run、WebView2 smoke。

## 修复内容

### 1. 报告目标显示名

修复点：

- `FrameScopeReportGenerator` 读取 `TargetDisplayName` / `ConfiguredTargetName` / `TargetName`，用户可见标题、summary、HTML 主标题、图表页 target 文案优先使用配置目标名。
- `DATA.target.displayName` 使用配置目标名，`DATA.target.processName` 保留 exe 进程名。
- report manifest 同时写出 `targetDisplayName` 和 `targetProcessName`。
- monitor session 接收并传播 `--TargetDisplayName`，capture diagnostics、status、summary 不再只能带进程名。
- 新增回归测试，防止以后退回把 exe 当主标题。

关键证据：

```text
artifacts\qa0530-full-installed\pfix-td-installed-final\target-display-name-summary.json
```

结果：

```text
allPass=true
targetCount=6
Counter-Strike 2 dataTargetDisplayName=Counter-Strike 2
Counter-Strike 2 dataTargetProcessName=cs2.exe
Valorant forbiddenTextAbsent=true
```

6 个 fake target 均通过：

| 配置目标名 | 进程名 | 用户可见 displayName | 技术 processName | 结果 |
|---|---|---|---|---|
| `Counter-Strike 2` | `cs2.exe` | `Counter-Strike 2` | `cs2.exe` | PASS |
| `PUBG: BATTLEGROUNDS` | `TslGame.exe` | `PUBG: BATTLEGROUNDS` | `TslGame.exe` | PASS |
| `Delta Force` | `DeltaForceClient-Win64-Shipping.exe` | `Delta Force` | `DeltaForceClient-Win64-Shipping.exe` | PASS |
| `Neverness To Everness` | `HTGame.exe` | `Neverness To Everness` | `HTGame.exe` | PASS |
| `Valorant` | `VALORANT-Win64-Shipping.exe` | `Valorant` | `VALORANT-Win64-Shipping.exe` | PASS, 不写 PUBG |
| `Battlefield 6` | `bf6.exe` | `Battlefield 6` | `bf6.exe` | PASS |

回答验收问题：`Counter-Strike 2` 现在显示配置目标名，不再只显示 `cs2.exe`；`cs2.exe` 仍保留在 processName / diagnostics / PresentMon 排查字段中。

### 2. 报告页 1280 横向溢出

复现来源：

- 原 full-installed QA 报告记录 `1280x720` 为 `scrollWidth=1310`、`clientWidth=1280`。

修复点：

- 报告 HTML 样式把窄布局切换点从 `1200px` 提前到 `1320px`，因为 `316px` sidebar、`900px` chart min width、gap 和页面边距在 1280 下无法稳定容纳。
- 对 toolbar group、select/button/input/range、stats card、legend、chart container 添加 `min-width:0`、`max-width:100%` 和换行约束。
- 没有用 `overflow-x:hidden` 掩盖内容。

最新 probe：

```text
artifacts\qa0530-full-installed\pfix-overflow-installed-final\report-overflow-probe.json
```

关键结果：

| 场景 | viewport | scrollWidth | clientWidth | 结果 | 截图 |
|---|---:|---:|---:|---|---|
| Reports menu | `1280x720` | 1280 | 1280 | PASS | `pfix-overflow-installed-final\report-menu-1280x720.png` |
| FPS 默认图表 | `1280x720` | 1280 | 1280 | PASS | `pfix-overflow-installed-final\fps-default-1280x720.png` |
| FPS dropdown | `1280x720` | 1280 | 1280 | PASS | `pfix-overflow-installed-final\fps-dropdown-control-1280x720.png` |
| CPU Core Frequency | `1280x720` | 1280 | 1280 | PASS | `pfix-overflow-installed-final\cpu-core-frequency-1280x720.png` |
| CPU Core VID | `1280x720` | 1280 | 1280 | PASS | `pfix-overflow-installed-final\cpu-core-vid-1280x720.png` |
| diagnostic report | `1280x720` | 1280 | 1280 | PASS | `pfix-overflow-installed-final\diagnostic-report-1280x720.png` |
| FPS 默认图表 | `900x760` | 900 | 900 | PASS | `pfix-overflow-installed-final\fps-default-900x760.png` |
| CPU Core Frequency | `900x760` | 900 | 900 | PASS | `pfix-overflow-installed-final\cpu-core-frequency-900x760.png` |
| CPU Core VID | `900x760` | 900 | 900 | PASS | `pfix-overflow-installed-final\cpu-core-vid-900x760.png` |
| diagnostic report | `900x760` | 900 | 900 | PASS | `pfix-overflow-installed-final\diagnostic-report-900x760.png` |

回答验收问题：`1280x720` 横向溢出已消失，主证据为 `scrollWidth=1280`、`clientWidth=1280`。

### 3. Target CRUD 与 Settings persistence 独立证据

修复/补证据点：

- Targets 页面补充稳定 smoke selector，覆盖新增、编辑完成、删除、modal 检查。
- WebView2 host 增加临时 profile 支持：`--config`、`--state`、`--history`。
- 增加 Target/Settings 专用 smoke：
  - `--web-ui-target-settings-evidence-smoke`
  - `--web-ui-settings-persistence-read-smoke`
- 专用 smoke 拒绝默认真实 config path，必须使用安装目录下的临时 profile：

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\smoke-temp\pfix-target-settings\framescope-config.json
```

最新证据：

```text
artifacts\qa0530-full-installed\pfix-target-settings-final\target-settings-evidence-summary.json
```

结果：

```text
success=true
targetCrudSuccess=true
settingsRestartSuccess=true
targetAddSaved=true
targetEditSaved=true
targetDeleteSaved=true
targetEditNoPerTargetSampling=true
settingsSaved=true
savedTelemetrySampleIntervalMs=1375
restartTelemetrySampleIntervalMs=1375
finalConfigTelemetrySampleIntervalMs=1375
userConfigTouched=false
```

独立截图证据：

| 验收点 | 截图 |
|---|---|
| Target 新增成功 | `pfix-target-settings-final\screenshots\target-settings-crud-target-add-saved.png` |
| Target 编辑成功 | `pfix-target-settings-final\screenshots\target-settings-crud-target-edit-saved.png` |
| Target 删除成功 | `pfix-target-settings-final\screenshots\target-settings-crud-target-delete-saved.png` |
| Target edit modal 打开后没有 per-target sampling | `pfix-target-settings-final\screenshots\target-settings-crud-target-edit-modal-no-per-target-sampling.png` |
| Settings 修改后保存 | `pfix-target-settings-final\screenshots\target-settings-crud-settings-saved.png` |
| 关闭并重启安装版 UI 后 Settings 仍保持 | `pfix-target-settings-final\screenshots\settings-restart-persistence-settings-restart-persisted.png` |

回答验收问题：Target 新增/编辑/删除均有独立截图证据；Target edit modal 已确认没有 per-target sampling；Settings 保存后重启仍保持有独立证据。

## 验证命令结果

### Required commands

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
PASS: typecheck PASS, vitest 5 files / 57 tests passed, Vite build PASS.
Log: artifacts\qa0530-full-installed\pfix-verification-final\run-frontend-verify.log

powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
PASS: 0 warnings, 0 errors; setup and full setup generated.
Log: artifacts\qa0530-full-installed\pfix-verification-final\build.log

powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
PASS: FrameScope tests rebuilt.
Log: artifacts\qa0530-full-installed\pfix-verification-final\build-tests.log

node .\tests\chart-sampling-tests.js
Default PATH result: WindowsApps node.exe returned Access is denied.
Bundled Node PATH override with the same command shape: PASS, chart-sampling-tests: PASS.
Logs:
artifacts\qa0530-full-installed\pfix-verification-final\chart-sampling-tests.log
artifacts\qa0530-full-installed\pfix-verification-final\chart-sampling-tests-bundled-path.log
```

All `tests\FrameScope*Tests.exe`:

```text
PASS: all 18 executables exited 0.
Log: artifacts\qa0530-full-installed\pfix-verification-final\framescope-tests.log
```

Executed test executables:

```text
FrameScopeCapturePlannerTests.exe
FrameScopeConfigStoreTests.exe
FrameScopeDiagnosticsTests.exe
FrameScopeIconTests.exe
FrameScopeLoggingPolicyTests.exe
FrameScopeNativeMonitorChildProcessTests.exe
FrameScopeNativeWatcherPolicyTests.exe
FrameScopePresentMonDiagnosticsTests.exe
FrameScopeProcessCleanupTests.exe
FrameScopeProcessSamplerTests.exe
FrameScopePubgSimulatorTests.exe
FrameScopeReportManifestTests.exe
FrameScopeReportProgressTests.exe
FrameScopeSystemSamplerCpuCoreTests.exe
FrameScopeUiStateTests.exe
FrameScopeWebBridgeTests.exe
FrameScopeWebHostLifecycleTests.exe
FrameScopeWebView2RuntimeTests.exe
```

### Installed WebView2 smoke

The latest installed exe was updated from the freshly rebuilt full setup:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe
LastWriteTime: 2026-05-30 16:24:48
```

Installer note: the quiet setup wrapper timed out because the installer launched the UI and the wrapper kept waiting. The installed exe timestamp confirms the install completed. The newly launched install PID `14428` was stopped before smoke tests continued.

```text
WebView2 live smoke
PASS: success=true.
Evidence: artifacts\qa0530-full-installed\pfix-verification-final\webview2-live-smoke-final.json
Screenshot: artifacts\qa0530-full-installed\pfix-verification-final\webview2-live-smoke-final.png

WebView2 reduced-motion smoke
PASS: success=true, reducedMotion=true.
Evidence: artifacts\qa0530-full-installed\pfix-verification-final\webview2-reduced-motion-smoke-final.json
Screenshot: artifacts\qa0530-full-installed\pfix-verification-final\webview2-reduced-motion-smoke-final.png

Tray lifecycle smoke
PASS: success=true.
Evidence: artifacts\qa0530-full-installed\pfix-verification-final\webview2-tray-lifecycle-smoke-final.json
```

### Focused probes

```text
Fake target display-name smoke
PASS: allPass=true, 6/6 fake targets passed.
Evidence: artifacts\qa0530-full-installed\pfix-td-installed-final\target-display-name-summary.json

Report overflow screenshot/probe
PASS: allNoOverflow=true.
Evidence: artifacts\qa0530-full-installed\pfix-overflow-installed-final\report-overflow-probe.json

Target CRUD screenshot/probe
PASS: targetCrudSuccess=true.
Evidence: artifacts\qa0530-full-installed\pfix-target-settings-final\target-settings-evidence-summary.json

Settings restart persistence screenshot/probe
PASS: settingsRestartSuccess=true.
Evidence: artifacts\qa0530-full-installed\pfix-target-settings-final\target-settings-evidence-summary.json
```

### Git and residual process checks

```text
git diff --check
PASS: exit code 0.
Note: the command printed existing LF/CRLF conversion warnings, but no whitespace error.
Logs:
artifacts\qa0530-full-installed\pfix-verification-final\git-diff-check-pre-report.log
artifacts\qa0530-full-installed\pfix-verification-final\git-diff-check-final.log

Residual process check
PASS: NO_MATCHING_RESIDUAL_PROCESSES.
Checked FrameScope processes, samplers, report generator, PresentMon, fake target exes, Valorant/BF6/CS2/PUBG-style real-game process names, and repo-owned command lines.
Logs:
artifacts\qa0530-full-installed\pfix-verification-final\residual-process-check-pre-report.log
artifacts\qa0530-full-installed\pfix-verification-final\residual-process-check-final.log
```

## 边界确认

- 未启动真实 Valorant / 无畏契约。
- 未启动任何真实游戏。
- 未测试真实 BF6。
- 未推 GitHub。
- 未更新 Release。
- 未改 FPS raw data 语义。
- 未恢复 1s bucket 图表。
- 未恢复真实 Vcore 功能。
- 未恢复 per-target sampling。
- Target/Settings smoke 使用临时 profile，`userConfigTouched=false`。

## 是否建议重跑全量模拟 QA

建议重新跑一次 full installed simulated QA。

原因：本轮已经针对 3 个 PARTIAL 点给出 focused PASS 证据，但原始验收报告是全量矩阵。为了把总状态从 `PARTIAL` 提升到正式 `PASS`，下一次应基于当前已安装的 full setup 重新跑完整模拟 QA，并复用本轮新增的 target display、overflow、Target CRUD、Settings restart probes 作为全量验收的一部分。
