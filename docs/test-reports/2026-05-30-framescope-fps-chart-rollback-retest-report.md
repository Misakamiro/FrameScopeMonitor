# FrameScope FPS 图表回退专项复测报告

日期：2026-05-30

## 结论

**PASS（专项源码/当前生成器口径通过）**

本次只复测 FPS 图表回退，不打包、不安装、不启动真实 Valorant / BF6 / 任何真实游戏、不修改源码、不做全量软件 QA。复测使用历史 run 的复制件，原始 run 没有覆盖。

当前源码重新编译出的临时报告生成器通过历史 run 复测：FPS 图表显示使用 `bucketMs=1000` 的 1 秒 bucket；average FPS / 1% Low / 0.1% Low 统计仍从 raw PresentMon 帧数据重算一致；`DATA.fps.min` 不存在；无红色异常帧点；FPS 下拉只有 4 项；1280x720 和 900x760 均无横向溢出；截图清楚可读。

额外注意：根目录现有 `FrameScopeReportGenerator.exe` 时间早于本次源码修改，直接用它生成报告会得到旧结果（`DATA.fps.min` 存在、点数等于 raw 帧数）。本次按边界没有覆盖根目录 exe、没有打包、没有安装；因此本报告只证明当前源码/临时编译生成器通过，不证明安装版或根目录旧二进制已经更新。

## 使用的历史 run

本机历史数据中，除指定 Counter-Strike 2 外，只发现 Valorant 目录存在 `presentmon.csv`；未发现 PUBG、Apex 或其他游戏的 `presentmon.csv`。因此追加选择了 3 个不同 Valorant 历史 run。

Artifacts 根目录：

`artifacts\fps-chart-rollback-retest-20260530`

| 游戏 | 原始历史 run | 复制件 |
| --- | --- | --- |
| Counter-Strike 2 | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260505-101253` | `artifacts\fps-chart-rollback-retest-20260530\runs\Counter-Strike-2-20260505-101253-copy` |
| Valorant | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260524-000615` | `artifacts\fps-chart-rollback-retest-20260530\runs\Valorant-20260524-000615-copy` |
| Valorant | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260517-161851` | `artifacts\fps-chart-rollback-retest-20260530\runs\Valorant-20260517-161851-copy` |
| Valorant | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260517-153629` | `artifacts\fps-chart-rollback-retest-20260530\runs\Valorant-20260517-153629-copy` |

## 数据复测结果

数据证据：

- `artifacts\fps-chart-rollback-retest-20260530\fps-run-analysis-current-source.json`
- `artifacts\fps-chart-rollback-retest-20260530\report-generation-results-current-source.json`
- 临时当前源码生成器：`artifacts\fps-chart-rollback-retest-20260530\compiled-current-generator\FrameScopeReportGenerator-current.exe`

| run | raw frame count | 图表显示点数 | `bucketMs` | raw average / 1% Low / 0.1% Low | 报告统计是否匹配 raw | `DATA.fps.min` | Y 轴是否被 raw 极端值拉爆 |
| --- | ---: | ---: | ---: | --- | --- | --- | --- |
| `Counter-Strike-2-20260505-101253` | 17472 | 90 | 1000 | `195.30 / 108.01 / 30.92` | 是 | 不存在 | 否。raw 最高瞬时 FPS `1739.433`，bucket 显示最大 `198.92` |
| `Valorant-20260524-000615` | 60317 | 75 | 1000 | `819.23 / 63.13 / 8.97` | 是 | 不存在 | 否。raw 最高瞬时 FPS `3571.429`，bucket 显示最大 `1250.07` |
| `Valorant-20260517-161851` | 129014 | 168 | 1000 | `771.71 / 128.13 / 20.61` | 是 | 不存在 | 否。raw 最高瞬时 FPS `3667.033`，bucket 显示最大 `1250.15` |
| `Valorant-20260517-153629` | 248614 | 372 | 1000 | `673.52 / 159.13 / 28.24` | 是 | 不存在 | 否。raw 最高瞬时 FPS `3998.401`，bucket 显示最大 `1167.31` |

结论：

- 4 个 run 的 `presentmon.csv` 均存在且有效帧数 > 0。
- 4 个 run 的 FPS 图表点数均明显小于 raw frame count。
- 4 个 run 的 `bucketMs=1000`。
- 4 个 run 的 average FPS / 1% Low / 0.1% Low 均与 raw PresentMon 帧数据重算一致。
- 4 个 run 均无 `DATA.fps.min`。

## UI / 截图复测结果

WebView2 截图/探针证据：

- `artifacts\fps-chart-rollback-retest-20260530\webview2-fps-probe-results.json`
- `artifacts\fps-chart-rollback-retest-20260530\fps-layout-tick-probe-results.json`

汇总：

- WebView2 截图数量：`32`。
- 32 个组合全部 `overflow=false`。
- 32 个组合全部 `redPixelCount=0`。
- 32 个组合全部 `hasMinOption=false`。
- 32 个组合全部 `hasMinSeries=false`。
- 32 个组合全部 `fpsBucketMs=1000`。
- FPS 下拉项均为 4 项：`all`、`avg`、`low1`、`low01`。
- 没有“只看最低瞬时 FPS”。
- X 轴标签可读：布局探针 `allPass=true`，最大 tick 数 `7`，最小 tick 间距 `161px`，无重复标签。
- 1280x720 和 900x760 均无横向溢出。
- 抽样目视检查了 CS2 `fps-all-1280x720.png` 和 Valorant `fps-low01-900x760.png`，截图清楚可读，曲线没有被红色异常点覆盖。

每个 run 的截图目录和文件：

| run | 截图目录 | 文件 |
| --- | --- | --- |
| `Counter-Strike-2-20260505-101253` | `artifacts\fps-chart-rollback-retest-20260530\webview2-fps-screenshots\Counter-Strike-2-20260505-101253` | `fps-all-1280x720.png`、`fps-avg-1280x720.png`、`fps-low1-1280x720.png`、`fps-low01-1280x720.png`、`fps-all-900x760.png`、`fps-avg-900x760.png`、`fps-low1-900x760.png`、`fps-low01-900x760.png` |
| `Valorant-20260524-000615` | `artifacts\fps-chart-rollback-retest-20260530\webview2-fps-screenshots\Valorant-20260524-000615` | `fps-all-1280x720.png`、`fps-avg-1280x720.png`、`fps-low1-1280x720.png`、`fps-low01-1280x720.png`、`fps-all-900x760.png`、`fps-avg-900x760.png`、`fps-low1-900x760.png`、`fps-low01-900x760.png` |
| `Valorant-20260517-161851` | `artifacts\fps-chart-rollback-retest-20260530\webview2-fps-screenshots\Valorant-20260517-161851` | `fps-all-1280x720.png`、`fps-avg-1280x720.png`、`fps-low1-1280x720.png`、`fps-low01-1280x720.png`、`fps-all-900x760.png`、`fps-avg-900x760.png`、`fps-low1-900x760.png`、`fps-low01-900x760.png` |
| `Valorant-20260517-153629` | `artifacts\fps-chart-rollback-retest-20260530\webview2-fps-screenshots\Valorant-20260517-153629` | `fps-all-1280x720.png`、`fps-avg-1280x720.png`、`fps-low1-1280x720.png`、`fps-low01-1280x720.png`、`fps-all-900x760.png`、`fps-avg-900x760.png`、`fps-low1-900x760.png`、`fps-low01-900x760.png` |

## 验证命令结果

命令日志目录：

`artifacts\fps-chart-rollback-retest-20260530\command-logs`

| 命令 | 结果 |
| --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS，exit 0。TypeScript typecheck PASS，Vitest `5 files / 57 tests` PASS，Vite build PASS。 |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS，exit 0，输出 `FrameScope tests rebuilt.` |
| `FrameScopeReportManifestTests.exe` | PASS，exit 0，输出结尾 `FrameScopeReportManifestTests: PASS`。 |
| `FrameScopeDiagnosticsTests.exe` | PASS，exit 0，输出 `FrameScopeDiagnosticsTests: PASS`。 |
| `node .\tests\chart-sampling-tests.js`（默认 PATH） | 默认 PATH 命中 WindowsApps `node.exe`，FAIL，exit 1，输出 `Access is denied.`；`where node` 显示 `C:\Program Files\WindowsApps\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\app\resources\node.exe`。 |
| bundled Node + `node .\tests\chart-sampling-tests.js` | PASS，exit 0，输出 `chart-sampling-tests: PASS`；bundled Node 路径：`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`。 |
| WebView2 report screenshot/probe | PASS，32 张截图和 JSON 证据全部成功，`failureCount=0`。 |
| `git diff --check` | PASS，exit 0；仅有既有 LF/CRLF warning，没有 whitespace error。 |

## 残留进程检查

证据：

`artifacts\fps-chart-rollback-retest-20260530\residual-process-check.json`

检查范围包含：

`FrameScopeReportGenerator.exe`、`FrameScopeReportGenerator-current.exe`、`FrameScopeDiagnosticsTests.exe`、`FrameScopeReportManifestTests.exe`、`FrameScopeMonitor.exe`、`FrameScopeNativeMonitor.exe`、`FrameScopeSystemSampler.exe`、`FrameScopeProcessSampler.exe`、`PresentMon.exe`、`WebView2ReportProbe.exe`、`msedge.exe`、`msedgewebview2.exe`、`node.exe`

结果：

- 与本次专项复测相关的残留进程数：`0`。
- 系统中存在 unrelated `msedgewebview2.exe` / `node.exe` 等进程，但命令行不属于本次 `fps-chart-rollback-retest`、WebView2 probe、chart-sampling 或 FrameScope 复测链路。

## 边界确认

- 没有打包。
- 没有安装。
- 没有启动真实游戏。
- 没有修改源码。
- 没有覆盖原始历史 run。
- 没有执行全量软件 QA。
- 只新增本次 artifacts、临时探针脚本/临时生成器和指定测试报告。

## 是否建议进入下一个独立板块

建议进入下一个独立板块。

如果下一个板块是打包/安装验证，建议第一步先重建并确认根目录/安装包里的 `FrameScopeReportGenerator.exe` 已包含本次 FPS 回退源码；本次专项复测没有覆盖二进制落盘更新、安装目录更新或发布包更新。
