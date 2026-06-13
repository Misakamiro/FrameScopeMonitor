# CHANGELOG

## 2026-06-13 - FrameScope Monitor 发布窗口

### 用户可见变化

- 报告和图表用户界面完成中文化，覆盖报告/图表 tab、标题、tooltip、summary、legend、空状态、参考线和 Top 进程等用户可见文案。
- 监控 worker 说明已补齐：任务管理器中多个 `FrameScopeMonitor.exe` 是 watcher / monitor-session worker 架构的正常表现，不代表重复打开软件。
- 普通 UI 加入单实例启动保护：软件已运行时再次点击快捷方式不会打开第二窗口，会提示“FrameScope Monitor 已在运行，请勿重复打开。”并退出第二进程。
- worker / diagnostic 启动路径继续绕过普通 UI 单实例锁，监控和报告工作进程不会被误挡。
- `CPU Voltage / Vcore` 和 `CPU Core VID` 继续分离，VID 仍只表示 CPU 请求/目标电压，不冒充真实 per-core Vcore。
- FPS 图表仍保持 `bucketMs=1000` 的展示聚合，平均 FPS、1% Low、0.1% Low 等统计继续来自 raw PresentMon 数据口径。

### 安装包和验证

- 已同步构建产物并确认 `dist\FrameScopeMonitor-Setup.exe`、`dist\FrameScopeMonitor-Full-Setup.exe` 和 payload 主程序 SHA256 与发布窗口记录一致。
- 本地 `FrameScopeMonitor-Full-Setup.exe` 更新安装验证 PASS，安装后用户数据目录保留。
- 发布前最小验证 PASS：前端 typecheck/Vitest/build、原生测试重建、单实例保护、报告 manifest、monitor child process、process cleanup、chart sampling、`git diff --check` 和残留进程检查均通过。

### 已知边界

- 本轮没有启动真实游戏。
- 本轮没有进行 BF6 真实游戏测试。

## 2026-06-03 - FrameScope Monitor v1.1.3 发布收尾

### 新增功能

- WebView2 React UI 作为默认界面继续交付，安装后直接进入新界面。
- 新增/修正 CPU Core VID 记录链路，覆盖采样、CSV、manifest、`DATA.cpuVid` 和独立报告图表。
- 报告加入独立 CPU Core VID 视图，明确 VID 是请求/目标电压，不是真实 per-core Vcore。
- target 管理支持删除最后一个目标后保存空列表。

### 修复

- 修复 target 删除最后一项后保存按钮不可用的问题，空 target 列表现在可以正常持久化。
- 修正 CPU Voltage / Vcore 与 CPU Core VID 的数据口径，避免把 VID、SOC、Package、VBAT、VIN 或编号核心电压混入整体 Vcore。
- 修正 VID-only 报告误导风险，VID-only 数据不会生成 CPU Voltage / Vcore。
- 修复 native watcher 策略与轮询间隔相关回归，并补充 watcher policy 覆盖。

### 性能优化

- 大型报告生成优化，报告生成速度提升且峰值内存降低。
- 后端监测占用降低，采样、子进程和 watcher 轮询更克制。
- 大报告 process 图交互优化，搜索、hover、tab 切换和图表绘制更轻。
- 前端大列表引入 windowing，减少大型 target/report/process 列表的 DOM 压力。
- UI 动画和过渡优化，移除高成本动效并保留 reduced-motion 支持。
- 日志限频和 diagnostics tail trim，控制重复日志与过大日志文件。
- data root 扫描保护，覆盖大目录、损坏 JSON、深层目录和 reparse/junction 场景。

### 报告/图表

- FPS 报告图表改为 GamePP 风格，同时保持 raw PresentMon 统计语义和 `bucketMs=1000`。
- 全报告图表统一为 GamePP 风格，包括 FPS、CPU、GPU、系统占用、后台进程、IO/温度、CPU 核心频率、CPU Voltage / Vcore 和 CPU Core VID。
- CPU Voltage / Vcore 独立表示整体真实电压口径。
- CPU Core VID 独立表示 CPU 请求/目标电压。
- 明确不把 VID 冒充 Vcore；没有真实 Vcore 时不使用 VID 填充。

### 安装/打包验证

- 重新打包生成 `dist\FrameScopeMonitor-Setup.exe` 和 `dist\FrameScopeMonitor-Full-Setup.exe`。
- 本机 Full Setup 静默安装验证通过。
- payload hash parity 通过，mismatch count 为 `0`。
- WebView2 live smoke PASS。
- WebView2 reduced-motion smoke PASS。
- target add/edit/delete PASS。
- Settings persistence PASS。
- report resource smoke PASS。

### 测试验证

- `tools\Run-Frontend.ps1 verify` PASS。
- `build.ps1` PASS。
- `tests\Build-FrameScopeTests.ps1` PASS。
- `FrameScopeReportManifestTests.exe` PASS。
- `FrameScopeDiagnosticsTests.exe` PASS。
- `FrameScopeSystemSamplerCpuCoreTests.exe` PASS。
- `FrameScopeReportProgressTests.exe` PASS。
- `FrameScopeLoggingPolicyTests.exe` PASS。
- `FrameScopeWebBridgeTests.exe` PASS。
- `FrameScopeNativeWatcherPolicyTests.exe` PASS。
- `FrameScopeNativeMonitorChildProcessTests.exe` PASS。
- bundled Node `tests\chart-sampling-tests.js` PASS。
- `git diff --check` PASS。
- 残留进程检查 PASS，`NO_MATCHING_RESIDUAL_PROCESSES`。

### 未包含事项

- 没有创建或更新 GitHub Release。
- 没有做真实游戏验收。
- 没有做 BF6 真实游戏验收。
