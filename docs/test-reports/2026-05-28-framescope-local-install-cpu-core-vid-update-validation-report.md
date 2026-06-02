# FrameScope Monitor 本机安装更新验证报告

日期：2026-05-28

结论：PASS

源码根目录：
`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

前置报告：
`docs\test-reports\2026-05-28-framescope-cpu-core-vid-retest-report.md`

证据根目录：
`artifacts\local-install-cpu-vid-update-20260528-132840`

## 边界

本轮执行的是本机安装版更新和验证，不测试 BF6，不启动真实游戏，不推 GitHub，不更新 Release，不删除 `%LOCALAPPDATA%\FrameScopeMonitorData`，不清空历史 runs/reports/config/log。

安装更新使用完整 quiet installer 流程：`dist\FrameScopeMonitor-Full-Setup.exe /quiet`。没有手工 payload copy。外层等待曾到达 300s timeout，但安装目录 `install.log` 在 `2026-05-28T13:32:12` 记录了 `fullPackage=True` 和 `install-complete`，后续安装目录 SHA256 与 payload 全量关键文件比对也通过。

## 结论和建议

| 项目 | 结论 |
| --- | --- |
| 本机安装更新 | PASS。Full quiet installer 已把当前 build payload 更新到 `%LOCALAPPDATA%\FrameScopeMonitor`。 |
| 用户数据保留 | PASS。安装前后 `%LOCALAPPDATA%\FrameScopeMonitorData` 存在，递归 run 目录计数保持 41；未删除历史 reports/config/log。 |
| CPU Core VID | PASS。本机 provider 通过 `builtin-librehardwaremonitor` 采到 `Core #1 VID` 到 `Core #8 VID`，独立进入 CPU Core VID 图。 |
| CPU 电压真实 per-core Vcore 状态 | PASS。当前机器只检测到 non-per-core CPU voltage；CPU 电压图不把 VID/Vcore/SOC/Package 画成真实 per-core Vcore。 |
| 报告图表和 FPS 指标 | PASS。CPU 核心频率图、CPU Core VID 图、CPU 电压无真实 per-core Vcore 状态、FPS 红色异常帧点均通过 UI/数据验证；平均 FPS / 1% Low / 0.1% Low 仍由原始帧计算。 |
| Settings 采样间隔 | PASS。默认值、保存、重启读取、主题和托盘设置均通过安装版 roundtrip 验证。 |
| 是否建议进入最终打包验证 | 建议可以进入下一轮独立的最终打包验证。 |
| 是否建议 GitHub 提交 | 本轮不建议直接提交 GitHub；应等用户明确授权，并在最终打包验证后再做提交/Release。 |

## 1. 安装前状态

证据：`initial-state\pre-install-state.json`

| 字段 | 值 |
| --- | --- |
| 安装目录 | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor` |
| 注册表版本 | `1.1.3` |
| 配置文件 | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-config.json` |
| 用户数据目录 | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData` |
| 历史 run 目录计数 | 41 |
| 安装前残留进程 | 0 个 FrameScope/PresentMon/sampler/report generator 残留 |

## 2. 构建和 dist SHA256

证据：
- `command-logs\tools_Run-Frontend_verify.log`
- `command-logs\build_ps1.log`
- `install-state\dist-sha256-after-build.json`

| 检查 | 结果 |
| --- | --- |
| `tools\Run-Frontend.ps1 verify` | PASS，5 个 Vitest 文件 / 50 个测试通过，TypeScript 和 Vite build 通过。 |
| `build.ps1` | PASS，生成 Setup 和 Full Setup。 |
| `FrameScopeMonitor-Setup.exe` | `D71A6BB696677D5E253EA67C06622312250C02DF4E9F51C3012671C6225BCD8D` |
| `FrameScopeMonitor-Full-Setup.exe` | `F62583508378FE25F43A8F1C895212AD1E75999153F8F6F81F7E06BAF95F9988` |
| `FrameScopeMonitor-Installer.zip` | `5E9A20406AED27B4AE29449E9AA08E405EF163EBC2AEBD6F1C5CFB1890B6DDAD` |

我在收尾阶段重新核对了 Full installer 当前 SHA256，仍为 `F62583508378FE25F43A8F1C895212AD1E75999153F8F6F81F7E06BAF95F9988`。

## 3. Quiet installer 和安装目录关键文件

证据：
- `command-logs\quiet-installer.log`
- `%LOCALAPPDATA%\FrameScopeMonitor\install.log`
- `install-state\installed-payload-key-file-compare.json`

安装器记录：
- `2026-05-28T13:32:12.6968017+08:00 install-start`
- `2026-05-28T13:32:12.6988024+08:00 webview2-runtime available=True ... fullPackage=True`
- `2026-05-28T13:32:12.7833690+08:00 install-complete`

关键文件比对结果：`RequiredCount=26`，`MismatchCount=0`。

已验证安装目录内关键文件和依赖：
- `FrameScopeMonitor.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeReportGenerator.exe`
- `frontend\index.html`
- `frontend\assets\index-CrayTgJp.js`
- `frontend\assets\index-CZ7x6juY.css`
- `LibreHardwareMonitorLib.dll`
- `HidSharp.dll`
- `BlackSharp.Core.dll`
- `DiskInfoToolkit.dll`
- `RAMSPDToolkit-NDD.dll`
- `System.Memory.dll`
- `System.Buffers.dll`
- `System.Runtime.CompilerServices.Unsafe.dll`

## 4. 用户数据保留

证据：
- `initial-state\pre-install-state.json`
- `install-state\installed-payload-key-file-compare.json`
- `install-state\final-config-user-data-check.json`

结果：
- `%LOCALAPPDATA%\FrameScopeMonitorData` 未删除。
- `framescope-runs` 递归 run 目录计数为 41，与安装前一致。
- 当前用户数据目录仍包含 `diagnostic-reports` 和 `framescope-runs`。
- 当前配置文件存在，安装后补齐了新字段：`ThemeMode=system`、`CloseWindowBehavior=minimize-to-tray`、`TrayEnabled=true`、`CpuTelemetry.PerCoreSampleIntervalMs=1000`、`CpuTelemetry.PerCoreVoltageSampleIntervalMs=1000`。

## 5. 安装版 WebView2 React UI

证据：
- `ui-smoke\installed-webview2-live-smoke.json`
- `ui-smoke\installed-webview2-reduced-motion-smoke.json`

结果：
- WebView2 live smoke：PASS，`success=true`，`pageLoaded=true`，`pageReady=true`，`usingReactFrontend=true`，`frontendPath=%LOCALAPPDATA%\FrameScopeMonitor\frontend`，console/errors 为 0。
- Reduced-motion smoke：PASS，`reducedMotion=true`，`usingReactFrontend=true`，console/errors 为 0。
- 说明：`ui-smoke\installed-webview2-tray-smoke.json` 也通过；其中 `usingReactFrontend=false` 属于 tray lifecycle 专项路径，不作为默认 React UI 入口判断。

## 6. Settings 验证

证据：`settings\installed-settings-interval-roundtrip.json`

结果：PASS，失败列表为空。

已验证：
- 采样间隔区域字段存在：`process-sample-interval`、`slow-sample-interval`、`cpu-core-sample-interval`、`cpu-voltage-sample-interval`。
- 默认值通过：后台进程 1000ms，系统慢采样 1000ms，CPU 核心频率 1000ms，CPU Core VID/电压 1000ms。
- 保存并重启读取通过：后台进程 1250ms，系统慢采样 1500ms，CPU 核心频率 1600ms，CPU Core VID/电压 1700ms。
- 主题和托盘设置没有回退：默认 `system`、`minimize-to-tray`、tray enabled；保存/重启读取 `dark`、`exit`、tray disabled 也通过。
- 当前真实配置最终保持用户侧默认状态：`ThemeMode=system`、`CloseWindowBehavior=minimize-to-tray`、`TrayEnabled=true`。

## 7. 安装版 synthetic/fake target

证据：
- `synthetic-run\installed-synthetic-run-summary-short-root.json`
- `synthetic-run\installed-synthetic-report-data-validation.json`

结果：PASS。

本轮只运行 synthetic/fake target，未启动真实游戏，未测试 BF6。第一次使用较长 artifact run path 时 report generator 触发 `PathTooLongException`，随后改用短 `%TEMP%` root 重跑通过。通过的 run 为：
`C:\Users\misakamiro\AppData\Local\Temp\fsvid-0528-135134\runs\SyntheticFake-20260528-135134`

关键结果：
- `MonitorExit=0`
- `ReportExit=0`
- `PresentMonCsvRows=240`
- `FrameCaptureStatus=captured`
- `ManifestReportKind=full`
- `cpu-core-samples.csv`：112 行，进入 CPU 核心频率图，16 条 series。
- `cpu-vid-samples.csv`：56 行，进入独立 CPU Core VID 图，8 条 series。
- `cpu-voltage-samples.csv`：21 行，但状态为 non-per-core-only，不进入真实 per-core Vcore 图。

## 8. CPU VID / 电压状态

证据：`synthetic-run\installed-synthetic-report-data-validation.json`

CPU VID：
- `CpuVidAvailable=true`
- `CpuVidStatus=core-vid-available`
- `CpuVidSource=builtin-librehardwaremonitor`
- `CpuVidProviderKind=built-in`
- 本机 provider 采到 `Core #1 VID`、`Core #2 VID`、`Core #3 VID`、`Core #4 VID`、`Core #5 VID`、`Core #6 VID`、`Core #7 VID`、`Core #8 VID`。

CPU 电压：
- `CpuVoltageAvailable=false`
- `CpuVoltagePerCoreAvailable=false`
- `CpuVoltageNonPerCoreAvailable=true`
- `CpuVoltageStatus=non-per-core-only`
- CPU 电压图 `series=0`，没有把 VID/Vcore/SOC/Package 伪装成真实 per-core Vcore。

VID 标注：
- CPU Core VID 图 note 明确标注 VID 是 CPU 请求/目标电压，不是真实 per-core Vcore。

## 9. FPS 原始帧统计

证据：`synthetic-run\installed-synthetic-report-data-validation.json`

结果：PASS。

FPS 指标由 240 行原始 PresentMon frame 数据计算：
- Average FPS：数据 64.21，原始帧复算 64.21。
- 1% Low：数据 8.93，原始帧复算 8.93。
- 0.1% Low：数据 8.93，原始帧复算 8.93。
- Min instant：数据 8.929，原始帧复算 8.929。
- FPS 红色最低异常帧点源数据存在 spike color。

## 10. 报告 UI 验证

证据：
- `report-ui\report-ui-cdp-audit.json`
- `report-ui\report-fps-1280x720.png`
- `report-ui\report-fps-900x760.png`
- `report-ui\report-cpuCore-1280x720.png`
- `report-ui\report-cpuCore-900x760.png`
- `report-ui\report-cpuVid-1280x720.png`
- `report-ui\report-cpuVid-900x760.png`
- `report-ui\report-cpuVoltage-1280x720.png`
- `report-ui\report-cpuVoltage-900x760.png`

结果：PASS。

CDP audit 覆盖 `fps`、`cpuCore`、`cpuVid`、`cpuVoltage` 四个视图，以及 `1280x720` 和 `900x760` 两个 viewport。

已验证：
- CPU 核心频率图存在，无 section overlap。
- CPU Core VID 图存在，`hasCpuVidNote=true`，明确标注请求/目标电压。
- CPU 电压视图存在，`hasCpuVoltageNoPerCoreText=true`，明确显示无真实 per-core Vcore 状态。
- FPS 视图存在红色异常帧点：两个 viewport 均检测到 red pixels。
- `1280x720` 和 `900x760` 视图 `overlaps=[]`。

## 11. git diff --check 和残留进程

证据：
- `command-logs\git-diff-check-final.log`
- `residual-process-check.json`

`git diff --check` 收尾重跑结果：exit code 0。日志中只有当前工作树已有文件的 LF/CRLF warning，没有 whitespace error。

最终残留检查结果：PASS。

残留计数：
- FrameScope/PresentMon/sampler/report generator：0
- Vite：0
- 测试 Edge profile 进程：0
- 测试 Edge profile 目录：0

## 最终判定

PASS。

当前 FrameScope Monitor 构建结果已经通过完整 quiet installer 更新到本机安装版。安装目录关键文件与 build payload SHA256 一致；WebView2 React UI 是默认入口；Settings 采样间隔默认、保存和重启读取通过；synthetic/fake target run 生成的 CPU 核心频率、CPU Core VID、CPU 电压状态和 FPS 原始帧统计均符合要求；报告 UI 在 1280x720 和 900x760 下无重叠；收尾 `git diff --check` exit code 0；无残留进程或测试 Edge profile。

建议：可以把本轮结果作为进入下一轮最终打包验证的依据。GitHub 提交和 Release 更新不应在本轮继续执行，需要用户明确授权后再做。
