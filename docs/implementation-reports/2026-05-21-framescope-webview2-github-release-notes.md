# FrameScope Monitor v1.1.2 更新说明

本次发布把 WebView2 React 新界面切换为默认界面，并移除旧 WinForms 主界面的构建路径。安装后直接打开 `FrameScopeMonitor.exe` 就会进入新界面，不再需要 `--web-ui` 参数。

## 本次更新

- 默认启动 WebView2 React UI：`FrameScopeMonitor.exe` 直接加载安装目录中的 `frontend`。
- 移除旧 WinForms 主界面代码和主程序构建引用，避免安装后继续打开旧界面。
- 保留 watcher、monitor session、PresentMon 采样、报告生成、诊断报告、GameLite 边界等后端行为不变。
- React / Vite / Framer Motion 前端会打入 `dist\FrameScopeMonitor-payload\frontend`。
- C# WebView2 bridge 使用 `requestId` 对齐前端请求和 C# 响应。
- WebView2 bridge 已接入 `state.snapshot`、`config.get`、`config.save`、`processes.refresh`、`reports.list`、`reports.open`、`reports.openDirectory`、`reports.regenerate`、`targets.get`、`targets.save`、`monitor.start`、`monitor.stop` 和 `diagnostics.generate`。
- 报告打开、目录打开、重新生成、目标配置保存、诊断生成和监控启停都由 C# host 校验，不信任前端传入的本地路径。
- 修复页面切换时的新旧页面混绘、空白帧和整页转圈等待问题。
- 修复前端依赖复现问题，`tools\Run-Frontend.ps1 verify` 可以完成安装、类型检查、测试和构建。
- 修复 Reports 页面 `Size` 在窄布局下竖向换行的问题。
- 修复 packaged payload 未包含 React dist 的打包缺口。
- 安装器版本提升到 `1.1.2`。

## 验证内容

本轮发布前已重新执行并通过：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
- `.\tests\FrameScopeReportProgressTests.exe`
- `.\tests\FrameScopeReportManifestTests.exe`
- `.\tests\FrameScopeWebBridgeTests.exe`
- bundled Node 运行 `.\tests\chart-sampling-tests.js`
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`
- packaged payload WebView2 live smoke
- 安装目录 WebView2 live smoke
- Reports open / openDirectory / regenerate 真实 UI 点击链路
- reduced motion 验证
- payload 与安装目录关键文件 SHA256 比对
- 残留进程检查

## 发布产物

- `FrameScopeMonitor-Setup.exe`
- `FrameScopeMonitor-Installer.zip`
- `FrameScopeMonitor-LegacyCleanup.exe`

## 安装和启动

- 推荐下载 `FrameScopeMonitor-Setup.exe` 并运行安装。
- 默认安装目录是 `%LOCALAPPDATA%\FrameScopeMonitor`。
- 默认数据目录是 `%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs`。
- 安装后直接运行 `FrameScopeMonitor.exe`，默认进入 WebView2 React 新界面。

## 已知说明

- 本次发布不改变监测采样语义、报告数据结构、诊断报告语义、GameLite、WMI 或 SGuard 行为。
- 本机开发环境更新使用的是 payload copy 同步，不是完整交互安装器流程；Release 中的安装器是本轮重新构建的发布产物。
