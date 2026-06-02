# FrameScope 性能优化总体验证报告

日期：2026-06-02
工作区：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
类型：总体验证，只记录证据，不修源码、不修 bug、不继续优化
最终结论：**PARTIAL**

## 1. 总结论

P0/P1/P2 的主要性能优化结果可以复现：大型历史报告生成、后端监测占用、process 图交互、前端大列表、UI 动画、日志、data root scan 的关键指标都在本轮或同日证据中保持优化后水平。FPS raw PresentMon 统计语义、`bucketMs=1000`、GamePP 风格报告图表、CPU Voltage/Vcore 与 CPU Core VID 分离、target/settings/report 基本流程均有验证证据。

但本轮必须运行的 `.\tests\FrameScopeNativeWatcherPolicyTests.exe` 失败，错误为：

```text
sampler arguments should not use PollIntervalMs:
unexpected <PollIntervalMs.ToString(CultureInfo.InvariantCulture)>
```

因此总体验证不能判为 PASS，只能判为 **PARTIAL**。本轮没有修复该问题。

## 2. 执行边界

本轮未修改源代码，未修 bug，未继续优化，未打包，未安装 FrameScope，未启动真实游戏，未测试 BF6，未推 GitHub，未更新 Release。

本轮新增/使用的验证证据主要在：

- `docs\test-reports\2026-06-02-framescope-performance-optimization-overall-verification-evidence\`
- `docs\test-reports\overall-0602-evidence\`
- `docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\`
- `artifacts\p2logrt0602\wvroot\live\live.json`
- `artifacts\p2logrt0602\wvroot\reduced\reduced.json`

说明：`Run-Frontend.ps1 verify` 执行时重新安装 frontend 依赖并重建 `dist`，这是验证脚本正常行为，不等同于产品安装或打包。

## 3. Git Caveat

当前 worktree 是已知的多轮大 diff 状态。`git status --short` 显示大量既有 modified/deleted/untracked 文件，覆盖 frontend、monitoring、reporting、docs、tools 等范围。本报告将“功能/性能验证结论”和“git diff 范围隔离 caveat”分开：大 diff 本身不直接否定性能验证，但会限制本轮对 diff 归因的精度。

`git diff --check`：exit 0，仅出现 LF/CRLF warning，未发现 whitespace error。
残留进程检查：`NO_MATCHING_RESIDUAL_PROCESSES`，exit 0。

## 4. P0 大型历史报告生成

结论：**可复现，通过**。

证据：

- `docs\test-reports\overall-0602-evidence\p0\p0-after-runs-r3-r4.json`
- `docs\test-reports\overall-0602-evidence\p0\r4-frame-stats-raw-compare.json`
- `docs\test-reports\overall-0602-evidence\layout-r4\report-overflow-probe.json`

| Run | exit | elapsed | CPU | peak WS | peak private | data.js | frames | raw rows | valid rows | process samples | system samples |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| r3 | 0 | 4,615 ms | 4,562.5 ms | 198.70 MB | 207.46 MB | 1,266,085 bytes | 876,585 | 876,603 | 876,593 | 17,714 | 1,933 |
| r4 | 0 | 4,730 ms | 4,671.875 ms | 198.68 MB | 207.65 MB | 1,266,085 bytes | 876,585 | 876,603 | 876,593 | 17,714 | 1,933 |

raw 语义校验：

- `frameStatsMatchRaw=true`
- `bucketMs=1000`
- `lowWindowMs=2000`
- mismatches：0
- average：587.96
- 1% Low：164.57
- 0.1% Low：36.60
- minInstant：1.104
- maxInstant：3544.842

布局与 GamePP 风格回归：

- `Probe-ReportHtmlLayout.js` exit 0。
- `allNoOverflow=true`，23 个截图/视口探针无横向 overflow。
- FPS dropdown 保留 `Average FPS / 1% Low / 0.1% Low`，没有恢复 `minInstant` 下拉项。

备注：第一次长 evidence 路径运行遇到 Windows `PathTooLongException`，属于 harness/evidence 路径问题；改用短路径后 r3/r4 通过。

## 5. P1 后端监测占用

结论：**可复现，通过**。

证据：`docs\test-reports\overall-0602-evidence\p1bm\after.json`

| Run | duration | interval | capture | SystemSampler CPU | SystemSampler peak WS | SystemSampler peak private | system rows | cpu-core rows | vcore rows | VID rows | process rows |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| overall-01 | 15,262 ms | 1000 ms | 12 s | 0.6875 s / 4.95% single-core | 51.54 MB | 61.17 MB | 13 | 208 | 13 | 104 | 1,415 |
| overall-02 | 16,015 ms | 1000 ms | 12 s | 0.6719 s / 4.67% single-core | 51.15 MB | 60.68 MB | 14 | 224 | 14 | 112 | 1,414 |

语义检查：

- `SampleIntervalMs=1000`
- `ProcessSampleIntervalMs=1000`
- `SlowSampleIntervalMs=1000`
- `CpuCoreSampleIntervalMs=1000`
- `CpuVoltageSampleIntervalMs=1000`
- `CpuVidSampleIntervalMs=1000`
- `CpuVoltageStatus=vcore-available`
- `CpuVidStatus=core-vid-available`
- `FrameCaptureStatus=captured`

CPU Voltage/Vcore 与 CPU Core VID 仍分开记录：`cpu-voltage-samples.csv` 与 `cpu-vid-samples.csv` 独立存在，行数独立，status 字段独立。

## 6. P1 大报告 Process 交互

结论：**可复现，通过**。

证据：`docs\test-reports\overall-0602-evidence\process-interaction\overall-after-process-interaction.json`

| Run | tab draw | tab elapsed | max search draw | max input dispatch | hover | DOM nodes | process names | process samples |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 13.7 ms | 93.2 ms | 9.9 ms | 10.0 ms | 0.2 ms | 319 | 119 | 17,714 |
| 2 | 9.5 ms | 86.3 ms | 7.6 ms | 7.6 ms | 0.1 ms | 319 | 119 | 17,714 |

功能语义：

- `chartNonBlank=true`
- `tooltipVisible=true`
- case-insensitive search 正常。
- no-result search 返回 0。
- process names / process samples 未丢失。
- `fpsBucketMs=1000`。

说明：`searchMaxDispatchMs` 为 7.6-10.0 ms，仍是毫秒级；报告中保留该数值作为后续对比基线。

## 7. P2 前端大列表

结论：**可复现，通过**。

证据：`docs\test-reports\overall-0602-evidence\large-lists\overall-large-list-frontend-large-list-probe.json`

250 行大列表：

| Run | initial rendered | total | windowed | initial DOM | initial CDP nodes | scroll rendered | search dispatch | search frame | filtered rows | after-filter DOM |
| --- | ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 19 | 250 | true | 275 | 1,575 | 10 | 1.3 ms | 31.9 ms | 51 | 459 |
| 2 | 19 | 250 | true | 275 | 3,897 | 10 | 1.2 ms | 30.5 ms | 51 | 459 |

普通列表与 smoke：

- Reports 普通列表：3/3 rows，DOM 250，overflowX=false。
- 小 process 列表：搜索后 1/1 row，windowed=false。
- target/settings/report smoke：`success=true`。
- target：startRows=4，finalRows=4。
- settings：saved=true，保存值 `1375`。
- reports：startRows=3，finalRows=3，operationStatusVisible=true。

## 8. P2 UI 动画

结论：**可复现，通过**。

证据：`docs\test-reports\overall-0602-evidence\ui-animation\overall-ui-animation-frontend-ui-animation-probe.json`

静态扫描：

| 指标 | 值 |
| --- | ---: |
| files scanned | 11 |
| framer-motion imports | 0 |
| motion.* elements | 0 |
| whileTap | 0 |
| transition-all | 0 |
| box-shadow transitions | 0 |
| filter declarations | 0 |
| backdrop-filter declarations | 0 |
| blur references | 0 |
| prefers-reduced-motion blocks | 3 |

运行时 probe：

| Mode | pages | max nav | max layout duration | max task duration | transition-all | box-shadow transitioned | framer inline transform |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| ordinary | 8 | 53.3 ms | 24.93 ms | 97.91 ms | 0 | 0 | 0 |
| reduced | 8 | 53.4 ms | 5.59 ms | 101.23 ms | 0 | 0 | 0 |

交互 probe：

- ordinary：targets refresh max 33.0 ms，reports menu max 27.3 ms，settings save max 24.5 ms。
- reduced：targets refresh max 31.9 ms，reports menu max 7.3 ms，settings save max 22.7 ms。
- ordinary/reduced 的 target/settings/report smoke 均 `success=true`。

## 9. P2 日志性能

结论：**可复现，通过**。

证据：

- `docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\idle-logs\retest-log-metrics.json`
- `docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\rate-limiter-smoke\rate-limiter-smoke-result.json`
- `docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\diagnostics-tail-trim\diagnostics-tail-trim-smoke-result.json`
- `docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\webview2-smoke-summary.json`
- fresh unit evidence：`command-logs\07-FrameScopeLoggingPolicyTests.log`、`command-logs\08-FrameScopeWebBridgeTests.log`

Idle 日志：

| Run | mode | lines | bytes | duplicates | elapsed | lines/s |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| default-idle-01 | default | 1 | 302 | 0 | 7.529 s | 0.133 |
| default-idle-02 | default | 1 | 302 | 0 | 7.521 s | 0.133 |
| verbose-perf-idle-01 | verbose+perf | 3 | 530 | 0 | 7.517 s | 0.399 |
| verbose-perf-idle-02 | verbose+perf | 3 | 530 | 0 | 7.515 s | 0.399 |

Rate limiter：

- first same key：PASS。
- repeated same key suppressed：PASS。
- changed state writes：PASS。
- different key not suppressed：PASS。
- heartbeat after interval：PASS。
- target scan uses limiter：PASS。
- watcher poll uses limiter：PASS。
- error paths use direct log：PASS。
- `allPass=true`。

Diagnostics tail trim：

- `trimmed=true`
- beforeBytes=5,449,602
- afterBytes=4,193,914
- tail marker kept：true
- old head marker removed：true
- normal append：true

Settings 打开日志目录：

- `FrameScopeWebBridgeTests.exe` PASS。
- WebView2 live/reduced smoke 中 `logs.openDirectory` 空 payload 成功，前端路径注入被 `path_not_allowed` 拒绝。

## 10. P2 Data Root Scan

结论：**可复现，通过**。

证据：

- `docs\test-reports\overall-0602-evidence\data-root\comparison\comparison-scan-metrics.json`
- `docs\test-reports\overall-0602-evidence\data-root\edge-cases\edge-case-smoke.json`

large/noisy root：

| Run | status scan | reports refresh | reports found | status matches | visited dirs | skipped dirs | visited files | reparse skipped | depth hits |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| after-1 | 11.1079 ms | 16.5139 ms | 46/46 | 71 | 132 | 14 | 256 | 2 | 1 |
| after-2 | 10.8192 ms | 16.3262 ms | 46/46 | 71 | 132 | 14 | 256 | 2 | 1 |

small root：

- reports：3/3。
- status JSON parsed：4。
- damaged JSON：1。
- after status scan：2.6825 ms / 0.8104 ms。
- after reports refresh：13.233 ms / 1.6046 ms。

Edge case：

- damagedJson=25。
- directoriesVisited=132。
- directoriesSkipped=14。
- filesVisited=256。
- reparseDirectoriesSkipped=2。
- depthLimitHits=1。
- enumerationErrors=0。
- skipReasons 包含 `bin/cache/dist/node_modules/obj/tmp/reparse-point`。

## 11. 核心数据语义回归

FPS raw / bucket：

- `frameStatsMatchRaw=true`。
- `bucketMs=1000`。
- `chart-sampling-tests.js` PASS。
- report manifest tests 覆盖 raw frame stats、FPS bucket、GamePP chart data。

CPU Voltage / Vcore / CPU Core VID：

- `FrameScopeSystemSamplerCpuCoreTests.exe` PASS。
- `FrameScopeReportManifestTests.exe` PASS。
- P1 backend evidence 中 `CpuVoltageStatus=vcore-available`，`CpuVidStatus=core-vid-available`。
- Vcore rows 与 VID rows 分别写入 `cpu-voltage-samples.csv` / `cpu-vid-samples.csv`，未混用。

GamePP 风格报告图表：

- `Probe-ReportHtmlLayout.js` PASS。
- `allNoOverflow=true`。
- FPS default/dropdown/tooltip 截图已生成。

target/settings/report 基本流程：

- 大列表 smoke 与 UI 动画 smoke 均覆盖 target/settings/report。
- target 4/4，settings saved=true value=1375，reports 3/3。
- WebView2 live/reduced smoke 覆盖 overview/targets/reports/settings、bridge、diagnostics、logs directory、monitor start/stop。

GameLite/lightweight：

- 本轮未运行 GameLite/lightweight 脚本，未安装/移除 WMI，未修改 sibling `gamelite-auto-lightweight`。
- 只读检查 `git diff --name-only | rg -i "gamelite|lightweight"`：`NO_DIFF_PATH_MATCHES`。
- 仓库内容中仍有既有 GameLite/lightweight 文档与兼容 wrapper 引用，但本轮验证未触碰这些边界。

## 12. WebView2 Smoke

证据：

- `artifacts\p2logrt0602\wvroot\live\live.json`
- `artifacts\p2logrt0602\wvroot\reduced\reduced.json`
- `docs\test-reports\2026-06-02-framescope-p2-logging-performance-retest-evidence\webview2-smoke-summary.json`

| Smoke | success | elapsed | reducedMotion | console errors | host errors | overview | targets | reports | settings | bridge | theme | monitor start/stop |
| --- | --- | ---: | --- | ---: | ---: | --- | --- | --- | --- | --- | --- | --- |
| live | true | 7,120 ms | false | 0 | 0 | true | true | true | true | true | true | true |
| reduced | true | 6,971 ms | true | 0 | 0 | true | true | true | true | true | true | true |

说明：长路径 docs evidence 下的 WebView2 smoke 曾因 Windows 截图路径长度限制失败；短路径 `artifacts\p2logrt0602\wvroot` 证据通过。该问题按 harness 路径 caveat 记录。

## 13. 验证命令结果

| 命令/检查 | 结果 | 证据 |
| --- | --- | --- |
| `git status --short` | exit 0，显示已知大 diff | `command-logs\00-git-status-short.log` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS：typecheck PASS，Vitest 6 files / 62 tests PASS，Vite build PASS | `command-logs\01-run-frontend-verify.log` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS：`FrameScope tests rebuilt.` | `command-logs\02-build-framescope-tests.log` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | `command-logs\03-FrameScopeReportManifestTests.log` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS | `command-logs\04-FrameScopeDiagnosticsTests.log` |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS | `command-logs\05-FrameScopeSystemSamplerCpuCoreTests.log` |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS | `command-logs\06-FrameScopeReportProgressTests.log` |
| `.\tests\FrameScopeLoggingPolicyTests.exe` | PASS | `command-logs\07-FrameScopeLoggingPolicyTests.log` |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS | `command-logs\08-FrameScopeWebBridgeTests.log` |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | **FAIL / exit 1** | `command-logs\09-FrameScopeNativeWatcherPolicyTests.log` |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS | `command-logs\10-FrameScopeNativeMonitorChildProcessTests.log` |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS | `command-logs\11-chart-sampling-tests-bundled-node.log` |
| bundled Node raw compare | PASS：`frameStatsMatchRaw=true` | `command-logs\12-p0-raw-compare-bundled-node.log` |
| bundled Node `tools\Probe-ReportHtmlLayout.js` | PASS | `command-logs\13-probe-report-html-layout.log` |
| bundled Node `tools\Probe-ReportProcessInteraction.js` | PASS | `command-logs\14-probe-report-process-interaction.log` |
| bundled Node `tools\Probe-FrontendLargeLists.js` | PASS：results=6，smokeSuccess=true | `command-logs\15-probe-frontend-large-lists.log` |
| bundled Node `tools\Probe-FrontendUiAnimation.js` | PASS：results=16，smokeSuccess=true | `command-logs\16-probe-frontend-ui-animation.log` |
| P1 measurement runtime build | PASS | `command-logs\17-p1-build-measurement-runtime.log` |
| P1 backend overhead measure | PASS | `command-logs\18-p1-measure-backend-monitor-overhead.log` |
| data root probes compile | PASS | `command-logs\19-data-root-compile-probes.log` |
| data root probes run | PASS：COMPARISON_EXIT=0，EDGE_EXIT=0 | `command-logs\20-data-root-run-probes.log` |
| final `git diff --check` | exit 0，仅 LF/CRLF warning | `command-logs\21-final-git-diff-check.log` |
| final residual process check | exit 0，`NO_MATCHING_RESIDUAL_PROCESSES` | `command-logs\22-final-residual-process-check.log` |
| final `git status --short` | exit 0，仍为已知大 diff | `command-logs\23-final-git-status-short.log` |
| GameLite/lightweight read-only check | exit 0，diff path 无匹配 | `command-logs\24-gamelite-lightweight-readonly-check.log` |

## 14. 18 个必答点

1. P0/P1/P2 优化是否整体可复现：**部分可复现**。性能 probe 与功能 smoke 大多通过，但 `FrameScopeNativeWatcherPolicyTests.exe` 失败，所以整体为 PARTIAL。
2. 是否存在任一优化互相破坏：未发现 P0/P1/P2 性能 probe 之间互相破坏的证据；但 NativeWatcherPolicyTests 暴露 watcher sampler 参数策略回归风险。
3. P0 大报告 after 数据：r3/r4 均 exit 0，elapsed 4.615s/4.730s，CPU 4.5625s/4.671875s，peak WS 约 198.7 MB，peak private 约 207.5 MB，data.js 1,266,085 bytes。
4. P1 后端监测 after 数据：SystemSampler CPU 0.6875s/0.6719s，single-core 4.95%/4.67%，peak WS 51.54/51.15 MB，peak private 61.17/60.68 MB，sample interval 1000 ms，system/core/Vcore/VID rows 保持预期。
5. P1 process 交互 after 数据：tab draw 13.7/9.5 ms，search draw max 9.9/7.6 ms，input dispatch max 10.0/7.6 ms，hover 0.2/0.1 ms，process names 119，samples 17,714。
6. P2 大列表 after 数据：250 行初始 windowing 19/250，DOM 275，scroll 后 10 rows，search dispatch 1.3/1.2 ms，filtered 51 rows；小列表和 Reports 普通列表正常。
7. P2 UI 动画 after 数据：framer-motion imports/motion/whileTap/transition-all/box-shadow transition 均 0；ordinary/reduced smoke 正常。
8. P2 日志 after 数据：default idle 1 line / 302 bytes / duplicate 0；verbose+perf 3 lines / 530 bytes / duplicate 0；rate limiter、diagnostics tail trim、Settings 打开日志目录均通过。
9. P2 data root scan after 数据：large/noisy root status scan 11.1079/10.8192 ms，Reports refresh 16.5139/16.3262 ms，reports 46/46；small root 3/3；damaged JSON/deep/reparse 安全处理。
10. FPS raw / `bucketMs=1000` 是否保持：保持，`frameStatsMatchRaw=true`，`bucketMs=1000`，chart sampling PASS。
11. CPU Voltage/Vcore 和 CPU Core VID 是否仍分开：仍分开，Vcore 与 VID 独立 CSV/状态/行数，相关 C# tests PASS。
12. target/settings/report 基本流程是否通过：通过，large-list/ui smoke 覆盖 target/settings/report，WebView2 smoke 覆盖页面与 bridge。
13. GameLite/lightweight 是否未触碰：未触碰；diff path 无 `gamelite|lightweight` 匹配，未运行/安装/移除 WMI 或 sibling 项目脚本。
14. 是否有源代码修改：本轮没有源代码修改；只新增/更新 docs/test-reports 下验证报告和 evidence。当前源代码 dirty 状态是既有 caveat。
15. 是否打包、安装、启动真实游戏、测试 BF6、推 GitHub、更新 Release：均没有。
16. git diff 范围 caveat：当前 worktree 是多轮大 diff，不能用本轮报告对所有 diff 做精确归因；但这不直接否定本轮性能/功能 probe 结果。
17. 所有验证命令结果：见第 13 节；除 `FrameScopeNativeWatcherPolicyTests.exe` 外，其余必跑命令/探针通过或以同日 evidence 覆盖。
18. 最终结论：**PARTIAL**。

## 15. 后续风险记录

- `FrameScopeNativeWatcherPolicyTests.exe` 失败需要后续单独处理；本轮只记录，不修复。
- WebView2 smoke 在长 docs evidence 路径下会触发截图路径长度问题；短路径 evidence 可通过，后续建议统一短路径或让 smoke 内部缩短截图文件名。
- 当前大 diff 范围较大，若要做 release/merge 级判断，需要在干净分支或明确 diff 范围后再跑一次完整验证。
