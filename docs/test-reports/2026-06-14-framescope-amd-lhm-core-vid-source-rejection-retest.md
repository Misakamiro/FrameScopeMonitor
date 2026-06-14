# FrameScope AMD LHM Core VID 来源拒绝修复专项复测

日期：2026-06-14
工作区：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 结论

PASS。

本轮只做复测和记录。未改产品源码，未修 bug，未打包，未安装，未启动真实游戏，未测试 BF6，未推 GitHub，未更新 Release。

确认点：

- 旧的 `0.7V` 阈值策略已移除：在指定源码/测试范围内未命中 `ImplausibleLowAmd`、`0.4-0.7`、`0.7V`、`value >= 0.7`、`>= 0.7`、`< 0.7` 等旧策略残留。
- AMD LibreHardwareMonitor Core VID 拒绝基于来源/identifier：当前源码按 `core + vid + amdcpu` 判定不可信来源，不按 `<0.7V` 数值过滤。
- AMD LHM `/amdcpu/0/voltage/2..9`、`Core #N VID` 被拒绝：正式测试覆盖 `0.762V` 仍拒绝；额外 current-source fixture 覆盖 `0.500V` 也拒绝。
- 合法 VID 仍可用：Intel/synthetic/非 AMD LHM Core VID fixture 仍生成 `DATA.cpuVid.series`。
- Vcore/VID 分离保持：`DATA.cpuVoltage` 继续显示 SuperIO/Vcore；`DATA.cpuVid` 不使用 Vcore；未把 `1.08V` 或 `0.960-1.104V` Vcore 冒充成 VID。
- `DATA.cpuVoltage` / `DATA.cpuVid` / `bucketMs=1000` 保持。
- screen-space same-x 图表修复未回退：Vcore duplicate screen x=0、same-x vertical=0、`Number(null)` 不再变 0；布局探针 JSON 可被 `ConvertFrom-Json` 解析，`allNoOverflow=true`。
- 图表中文化、worker 说明、普通 UI 单实例保护未回退；FPS raw PresentMon 语义保持，显示层 `bucketMs=1000`。

## 静态复核

旧阈值残留扫描：

```powershell
rg -n "ImplausibleLowAmd|0\.4-0\.7|0\.7V|value\s*>=\s*0\.7|>=\s*0\.7" src tests
```

结果：无命中，退出码 `1`，符合预期。

指定改动文件范围补充扫描：

```powershell
rg -n "ImplausibleLowAmd|0\.4-0\.7|0\.7V|value\s*>=\s*0\.7|>=\s*0\.7|<\s*0\.7" `
  src\monitoring\FrameScopeSystemSampler.CpuCoreTelemetry.cs `
  src\reporting\FrameScopeReportGenerator.SystemData.cs `
  src\reporting\FrameScopeReportGenerator.Metadata.cs `
  src\reporting\FrameScopeReportGenerator.Models.cs `
  tests\FrameScopeSystemSamplerCpuCoreTests.cs `
  tests\FrameScopeReportManifestTests.cs
```

结果：无命中，退出码 `1`，符合预期。

当前拒绝逻辑证据：

- `src\monitoring\FrameScopeSystemSampler.CpuCoreTelemetry.cs`：`IsUnreliableAmdLibreHardwareMonitorCoreVidSource` 使用 normalized sensor text，要求同时包含 `core`、`vid`、`amdcpu`。
- `src\reporting\FrameScopeReportGenerator.SystemData.cs`：`IsValidCpuVidValue` 先做通用电压有效性检查，再拒绝 AMD LHM Core VID 来源；报告端拒绝后使用 `UnreliableAmdLibreHardwareMonitorCoreVidReason()`。
- 这里仍保留通用电压合理性范围 `>0.2 && <5`，它不是旧的 AMD `<0.7V` 策略。

## 功能复测证据

正式测试覆盖：

- `tests\FrameScopeSystemSamplerCpuCoreTests.cs`
  - `AmdLibreHardwareMonitorCoreVidSourceDoesNotCreateCsv`
  - fixture：`SensorName = "Core #1 VID"`，`SensorIdentifier = "/amdcpu/0/voltage/2"`，`VidV = 0.762`
  - 断言：不创建 `cpu-vid-samples.csv`，`CpuVidAvailable=false`，`CpuVidSampleCount=0`，`CpuVidRejectedSampleCount=1`，reason 提到 AMD 来源拒绝。
- `tests\FrameScopeReportManifestTests.cs`
  - `AmdLibreHardwareMonitorCoreVidSamplesAreRejectedAsUnreliable`
  - fixture：`/amdcpu/0/voltage/2..9`，每核 `0.762V`
  - 断言：`manifest.cpuVidAvailable=false`，`cpuVid.status=unavailable`，`DATA.cpuVid.series` 数量为 0；`DATA.cpuVoltage.available=true`。
- `SyntheticCpuVidTelemetryWritesDedicatedCsvAndStatus` / Intel VID fixture
  - 合法 VID 仍可生成 `DATA.cpuVid.series`，8 个 core series，且不让 `DATA.cpuVoltage` 变 available。
- Vcore/VID 分离 fixture
  - Vcore/SOC/package voltage 不生成 `DATA.cpuVid.series`。
  - 显式 aggregate Vcore 生成 `DATA.cpuVoltage.series`。

额外 0.5V current-source fixture：

路径：`smoke-temp\amd-lhm-0p5-current-source\VerifyAmdLhm0p5Fixture.cs`
说明：这是本轮为复测创建的临时证据 helper，编译时直接引用当前 `src\reporting` 源码，不是产品源码改动。

输入：

- `cpu-vid-samples.csv`：`Core #1..#8 VID`，identifier `/amdcpu/0/voltage/2..9`，值全部 `0.500V`。
- `cpu-voltage-samples.csv`：SuperIO Vcore `/lpc/it8689e/0/voltage/0`，值 `0.960V`、`1.104V`。

运行结果：

```text
PASS
cpuVoltage.available=True
cpuVoltage.seriesCount=1
cpuVoltage.tCount=2
cpuVid.available=False
cpuVid.seriesCount=0
cpuVid.reason=AMD LibreHardwareMonitor Core VID samples were rejected because this sensor source does not match the validated CPU Voltage / Vcore reading on this system; CPU Voltage / Vcore remains separate and is not used as VID.
fps.bucketMs=1000
```

这证明当前源码对用户历史 0.5V AMD LHM Core VID 的结果是：`DATA.cpuVid.available=false`、`DATA.cpuVid.series=[]`、reason 说明 AMD LHM Core VID 来源不可信/不可用；同时 `DATA.cpuVoltage.available=true`，Vcore 范围与源数据 `0.960-1.104V` 一致。

注意：仓库中已有旧的 `smoke-temp\amd-lhm-0p5` 产物仍带旧 `0.4-0.7V` 文案，疑似由旧根目录 exe 生成。本报告不把该旧产物作为当前源码通过依据。

## 必跑命令结果

1. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
   - 退出码：`0`
   - 结果：typecheck 通过；Vitest `6` 个测试文件、`64` 个测试通过；Vite production build 通过。

2. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
   - 退出码：`0`
   - 结果：`FrameScope tests rebuilt.`

3. `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`
   - 退出码：`0`
   - 结果：`FrameScopeSystemSamplerCpuCoreTests: PASS`

4. `.\tests\FrameScopeReportManifestTests.exe`
   - 退出码：`0`
   - 结果：`FrameScopeReportManifestTests: PASS`

5. `.\tests\FrameScopeDiagnosticsTests.exe`
   - 退出码：`0`
   - 结果：`FrameScopeDiagnosticsTests: PASS`

6. `.\tests\FrameScopeNativeWatcherPolicyTests.exe`
   - 退出码：`0`
   - 结果：`FrameScopeNativeWatcherPolicyTests: PASS`

7. `.\tests\FrameScopeNativeMonitorChildProcessTests.exe`
   - 退出码：`0`
   - 结果：`FrameScopeNativeMonitorChildProcessTests: PASS`

8. `.\tests\FrameScopeProcessCleanupTests.exe`
   - 退出码：`0`
   - 结果：`FrameScopeProcessCleanupTests: PASS`

9. `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe`
   - 退出码：`0`
   - 结果：
     - `[PASS] ordinary UI launches are guarded`
     - `[PASS] worker and diagnostic launches bypass the UI guard`
     - `[PASS] duplicate UI lock is rejected and releases cleanly`
     - `[PASS] duplicate UI prompt stays Chinese`
     - `FrameScopeSingleInstanceLaunchGuardTests: PASS`

10. Bundled Node 跑 `.\tests\chart-sampling-tests.js`
    - 命令：`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tests\chart-sampling-tests.js`
    - 退出码：`0`
    - 结果：`chart-sampling-tests: PASS`
    - 覆盖：Vcore same-x / same-screen-x vertical artifact 断言为 0；CPU Voltage / CPU VID 分离；`DATA.cpuVoltage` 与 `DATA.cpuVid` tab/series 分离；`bucketMs=Number(fps.bucketMs)||1000` 保持。

11. Bundled Node 跑 `.\tools\Probe-ReportHtmlLayout.js`
    - 命令：`C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tools\Probe-ReportHtmlLayout.js --report .\smoke-temp\artifact-sync-screen-space-20260614\built-cs2-run-final\Counter-Strike-2-20260613-152103\charts\framescope-interactive-report.html --diagnostic .\smoke-temp\artifact-sync-screen-space-20260614\built-cs2-run-final\Counter-Strike-2-20260613-152103\charts\framescope-interactive-report.html --out .\docs\test-reports\2026-06-14-framescope-amd-lhm-core-vid-source-rejection-retest-evidence\layout-probe`
    - 退出码：`0`
    - JSON：`docs\test-reports\2026-06-14-framescope-amd-lhm-core-vid-source-rejection-retest-evidence\layout-probe\report-overflow-probe.json`
    - `Get-Content -Raw | ConvertFrom-Json` 可解析。
    - `allNoOverflow=True`
    - `resultCount=23`

12. `git diff --check`
    - 退出码：`0`
    - 结果：无 whitespace error；仅 Git 提示若干工作区文件下次 Git touch 时 LF 会转 CRLF。

13. 最终残留进程检查
    - 退出码：`0`
    - 结果：`NO_MATCHING_RESIDUAL_PROCESSES`

## 回归覆盖

- screen-space same-x 图表修复：
  - `chart-sampling-tests.js` 对稳定 Vcore 降采样、render points、duplicate adjacent screen x 都有断言。
  - 本轮结果：`chart-sampling-tests: PASS`。
- Vcore duplicate screen x=0 / same-x vertical=0：
  - `stable Vcore downsample should not create same-x min/max vertical artifacts` = 0。
  - `stable Vcore render points should not create same-screen-x vertical artifacts` = 0。
  - `stable Vcore render points should be compacted to one draw point per adjacent screen x` = 0。
- `Number(null)` 不再变 0：
  - 报告 manifest 测试覆盖 invalid zero/null gap，不把无效 Vcore/VID 绘制成 0。
- 图表中文化：
  - frontend verify 和 chart-sampling source assertions 通过。
- worker 说明：
  - `FrameScopeNativeWatcherPolicyTests: PASS`。
- 普通 UI 单实例：
  - `FrameScopeSingleInstanceLaunchGuardTests: PASS`，包含 duplicate UI prompt 中文断言。
- `bucketMs=1000` 与 FPS raw PresentMon 语义：
  - `FrameScopeReportManifestTests: PASS`。
  - `chart-sampling-tests.js` 断言 `bucketMs=Number(fps.bucketMs)||1000`。
  - 0.5V current-source fixture 输出 `fps.bucketMs=1000`。

## 未做事项

- 未改产品源码。
- 未修 bug。
- 未打包。
- 未安装。
- 未启动真实游戏。
- 未测试 BF6。
- 未推 GitHub。
- 未更新 Release。
