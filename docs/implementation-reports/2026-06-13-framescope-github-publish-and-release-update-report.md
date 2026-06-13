# FrameScope Monitor GitHub 发布和 Release 更新报告

日期：2026-06-13

## 范围

- 更新 README 和 CHANGELOG，记录本轮用户可见变化、验证结果和已知边界。
- 提交并推送 `main` 到 GitHub。
- 更新 GitHub Release notes，并上传/替换安装包资产。

## 发布前产物校验

| 文件 | SHA256 |
| --- | --- |
| `dist\FrameScopeMonitor-Setup.exe` | `E9CE5D97C2673BA1ECE9DBF95073BEB32A4D33769B6C18B1F4639F6FEDD90C06` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | `D4BA6AABB83CC4F6C6BE89F0CFDA8EC35746054BABF60D4F82864DCC823D02B1` |
| `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe` | `790BFA2A303738F3FD3B7A1A03C71735ADA34260479C946E7F86B9351A3AE4A6` |

结果：三项 SHA256 与发布窗口给定值一致。

## 本轮发布内容

- 报告/图表 tab、标题、tooltip、summary、legend、空状态、参考线和 Top 进程等用户可见文案中文化。
- README/CHANGELOG 补充监控 worker 说明：任务管理器中多个 `FrameScopeMonitor.exe` 是 watcher / monitor-session worker 架构，不是重复打开软件。
- 普通 UI 单实例启动保护：重复启动提示“FrameScope Monitor 已在运行，请勿重复打开。”，不会打开第二个主窗口。
- worker / diagnostic 启动路径不受普通 UI 单实例锁影响。
- `CPU Voltage / Vcore` 与 `CPU Core VID` 继续分离。
- FPS `bucketMs=1000` 展示聚合和 raw PresentMon 统计语义保持不变。

## 发布前验证

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`：PASS，Vitest `6 files / 64 tests` passed，Vite build passed。
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`：PASS，`FrameScope tests rebuilt.`
- `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe`：PASS。
- `.\tests\FrameScopeReportManifestTests.exe`：PASS。
- `.\tests\FrameScopeNativeMonitorChildProcessTests.exe`：PASS。
- `.\tests\FrameScopeProcessCleanupTests.exe`：PASS。
- bundled Node `.\tests\chart-sampling-tests.js`：PASS。
- `git diff --check`：PASS。
- 残留进程检查：`NO_MATCHING_RESIDUAL_PROCESSES`。

## 已知边界

- 未启动真实游戏。
- 未测试 BF6。

## Git 和 GitHub Release

- 发布 commit：`aa93b3e2a7addbe0c72281ed6fa17bb215bb62da`
- push 结果：`main -> main` 成功，本地 `main` 与 `origin/main` 一致。
- Release URL：`https://github.com/Misakamiro/FrameScopeMonitor/releases/tag/v1.2`
- Release tag：`v1.2`
- Release target commit：`aa93b3e2a7addbe0c72281ed6fa17bb215bb62da`
- tag ref：`refs/tags/v1.2` 已更新到 `aa93b3e2a7addbe0c72281ed6fa17bb215bb62da`。
- Release notes：已覆盖中文图表文案、监控 worker 说明、单实例启动保护、本地安装更新验证 PASS、SHA256 和已知边界。
- Release assets：已删除旧的两个 installer 资产并重新上传。
  - `FrameScopeMonitor-Setup.exe`：uploaded，大小 `2706432` bytes。
  - `FrameScopeMonitor-Full-Setup.exe`：uploaded，大小 `201885696` bytes。

## 未执行事项

- 未启动真实游戏。
- 未测试 BF6。
