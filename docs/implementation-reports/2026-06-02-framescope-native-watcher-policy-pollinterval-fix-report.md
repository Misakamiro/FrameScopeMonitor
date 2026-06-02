# FrameScope Native Watcher Policy PollInterval Fix Report

日期: 2026-06-02
工作区: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
结论: **PASS**

## 1. 失败是否复现

已复现。

复现步骤:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeNativeWatcherPolicyTests.exe
```

结果:

- `Build-FrameScopeTests.ps1`: exit 0, `FrameScope tests rebuilt.`
- `FrameScopeNativeWatcherPolicyTests.exe`: exit 1
- 失败信息: `sampler arguments should not use PollIntervalMs: unexpected <PollIntervalMs.ToString(CultureInfo.InvariantCulture)>`

## 2. Root Cause

root cause 是 **测试匹配范围误判**，不是 watcher 仍把旧 `PollIntervalMs` 传给 sampler。

`tests\FrameScopeNativeWatcherPolicyTests.cs` 原先在整个 `src\app\FrameScopeNativeMonitor.Watcher.cs` 源文件上检查:

```csharp
AssertDoesNotContain(source, "PollIntervalMs.ToString(CultureInfo.InvariantCulture)", ...)
```

但 watcher 文件中存在合法代码:

```csharp
FrameScopeConfigStore.InternalPollIntervalMs.ToString(CultureInfo.InvariantCulture)
```

`InternalPollIntervalMs.ToString(...)` 包含后缀文本 `PollIntervalMs.ToString(...)`，导致测试把 watcher performance log 的固定 internal poll interval 误判为 sampler 参数残留。

## 3. PollIntervalMs 残留位置

本轮 `rg -n "PollIntervalMs\\.ToString|PollIntervalMs|telemetrySampleMs|CpuVoltageSampleIntervalMs|CpuVidSampleIntervalMs" src tests` 的关键结论:

- `src\app\FrameScopeNativeMonitor.Watcher.cs:172`: `FrameScopeConfigStore.InternalPollIntervalMs.ToString(CultureInfo.InvariantCulture)`，用于 watcher poll performance log，合法保留。
- `src\app\FrameScopeNativeMonitor.Watcher.cs:176`: `Thread.Sleep(FrameScopeConfigStore.InternalPollIntervalMs)`，用于固定 internal watcher loop sleep，合法保留。
- `src\core\FrameScopeConfigStore.cs`: `PollIntervalMs` 作为 legacy/config compatibility 字段，并被 normalize 到 `InternalPollIntervalMs`，合法保留。
- `src\diagnostics\FrameScopeDiagnostics.Sections.cs`: diagnostics 输出 config `pollIntervalMs`，合法保留。
- `src\app\FrameScopeNativeMonitor.MonitorSession.cs` / `.Status.cs`: `ControlPollIntervalMs` 是 monitor session 控制循环参数，不是 telemetry sampler interval，合法保留。
- `src\app\FrameScopeNativeMonitor.Watcher.cs:192-207`: watcher 读取并 clamp `TelemetrySampleIntervalMs`，然后把 `sampleMs`、`processSampleMs`、`slowSampleMs`、`cpuCoreSampleMs`、`cpuVoltageSampleMs`、`cpuVidSampleMs` 统一设为 `telemetrySampleMs`。
- `src\app\FrameScopeNativeMonitor.Watcher.cs:218-227`: monitor/session/sampler 参数使用上述 telemetry 变量，没有使用 legacy `config.PollIntervalMs`。

## 4. 修改文件

本轮修改:

- `tests\FrameScopeNativeWatcherPolicyTests.cs`
- `docs\implementation-reports\2026-06-02-framescope-native-watcher-policy-pollinterval-fix-report.md`

没有修改 production source。当前 worktree 中 `src\app\FrameScopeNativeMonitor.Watcher.cs` 已是既有 modified 状态，但本轮只读取它做根因定位，没有改动它。

## 5. 修复内容

`FrameScopeNativeWatcherPolicyTests` 保留原有断言语义，但把 sampler 参数负向检查限定到 monitor start argument block:

- 新增 `ExtractStartMonitorArgumentsSource(...)`，只截取 `var args =` 到 `WriteVerboseFrameScopeLog` 前的 monitor 启动参数拼接区域。
- sampler 参数相关 `AssertContains(...)` 改为检查该参数块。
- `AssertDoesNotContain(..., "PollIntervalMs.ToString(CultureInfo.InvariantCulture)", ...)` 改为检查该参数块，避免误扫 watcher loop/logging 区域。
- 新增 `AssertDoesNotContain(..., "config.PollIntervalMs", ...)`，明确 sampler 参数块不能读取 legacy `PollIntervalMs`。
- 新增正向断言允许并要求 watcher performance log 使用 `FrameScopeConfigStore.InternalPollIntervalMs.ToString(...)`，因为它属于固定 internal watcher poll 语义。

这不是删除断言，也不是弱化测试；断言从“全文件误扫”改成“精确检查 sampler 参数来源”，同时对 legacy config read 增加了更直接的负向检查。

## 6. TelemetrySampleIntervalMs 语义

修复不影响 `TelemetrySampleIntervalMs` 语义，因为本轮没有修改 watcher production code。

当前 watcher 路径仍是:

- 从 `config.TelemetrySampleIntervalMs` 读取用户可见采样间隔。
- 使用 `TelemetrySampleIntervalMinMs` / `TelemetrySampleIntervalMaxMs` clamp。
- `sampleMs`、`processSampleMs`、`slowSampleMs`、`cpuCoreSampleMs`、`cpuVoltageSampleMs`、`cpuVidSampleMs` 均跟随 `telemetrySampleMs`。

## 7. 不会恢复 per-target sampling

本轮没有恢复或新增 per-target sampling。测试仍要求:

- `var processSampleMs = telemetrySampleMs`
- `var slowSampleMs = telemetrySampleMs`
- `var cpuCoreSampleMs = telemetrySampleMs`
- `var cpuVoltageSampleMs = telemetrySampleMs`
- `var cpuVidSampleMs = telemetrySampleMs`

并且 monitor start argument block 不能包含 `config.PollIntervalMs` 或 `PollIntervalMs.ToString(...)`。

## 8. CPU Voltage / Vcore 与 CPU Core VID

CPU Voltage / Vcore 和 CPU Core VID 仍跟随正确 interval:

- watcher policy test 继续断言 `cpuVoltageSampleMs = telemetrySampleMs` 和 `cpuVidSampleMs = telemetrySampleMs`。
- watcher policy test 继续断言 `--EnableCpuVoltageTelemetry`、`--CpuVoltageSampleIntervalMs`、`--CpuVoltageProvider`、`--CpuVidSampleIntervalMs` 参数存在。
- `FrameScopeSystemSamplerCpuCoreTests.exe` PASS，覆盖 CPU Voltage / Vcore 与 CPU Core VID interval/status 语义。
- `FrameScopeReportManifestTests.exe` PASS，覆盖 report metadata 中 CPU Voltage / Vcore 与 CPU Core VID 字段。

没有禁用 CPU Voltage / Vcore，没有禁用 CPU Core VID，也没有混淆 Vcore 和 VID。

## 9. FPS / Report / Performance 优化影响

本轮没有修改 FPS raw 统计、`bucketMs=1000`、报告图表或 P0/P1/P2 优化相关 production code。

验证覆盖:

- `FrameScopeReportManifestTests.exe`: PASS
- bundled Node `.\tests\chart-sampling-tests.js`: PASS
- `Run-Frontend.ps1 verify`: typecheck PASS, Vitest 6 files / 62 tests PASS, Vite build PASS

因此本轮修复未影响 FPS raw 统计、chart sampling、report manifest 或前端构建。

## 10. 验证命令结果

| 命令 | 结果 |
| --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS, exit 0, `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS, exit 0, `FrameScopeNativeWatcherPolicyTests: PASS` |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS, exit 0, `FrameScopeNativeMonitorChildProcessTests: PASS` |
| `.\tests\FrameScopeConfigStoreTests.exe` | PASS, exit 0, `FrameScopeConfigStoreTests: PASS` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS, exit 0, `FrameScopeReportManifestTests: PASS` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS, exit 0, `FrameScopeDiagnosticsTests: PASS` |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS, exit 0, `FrameScopeSystemSamplerCpuCoreTests: PASS` |
| `.\tests\FrameScopeLoggingPolicyTests.exe` | PASS, exit 0, `FrameScopeLoggingPolicyTests: PASS` |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS, exit 0, `FrameScopeWebBridgeTests: PASS` |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS, exit 0; script ran `added 110 packages`, typecheck PASS, Vitest 6 files / 62 tests PASS, Vite build PASS |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS, exit 0, `chart-sampling-tests: PASS` |
| `git diff --check` | PASS, exit 0; only LF/CRLF warnings, no whitespace error |
| residual process check | PASS, exit 0, `NO_MATCHING_RESIDUAL_PROCESSES` |

## 11. 明确未执行事项

本轮没有:

- 打包
- 安装 FrameScope
- 启动真实游戏
- 测试 BF6
- 推 GitHub
- 更新 Release
- 运行 `build.ps1`

`Run-Frontend.ps1 verify` 重新安装 frontend packages 并生成 frontend `dist`，这是验证脚本正常行为，不等同于产品安装或 installer/setup 打包。

## 12. 最终结论

**PASS**

`FrameScopeNativeWatcherPolicyTests.exe` 的 `PollIntervalMs` 残留失败已修复。根因是测试全文件字符串匹配误扫 `InternalPollIntervalMs.ToString(...)`，修复后测试精确检查 monitor start sampler argument block，同时保留 global telemetry interval、CPU Voltage / Vcore、CPU Core VID、legacy compatibility 和 fixed internal watcher poll 的正确语义。
