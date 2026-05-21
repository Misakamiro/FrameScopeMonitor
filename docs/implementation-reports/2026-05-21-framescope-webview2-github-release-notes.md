# FrameScope Monitor v1.1.1 更新说明

本次发布更新 WebView2 React 新界面、C# WebView2 bridge、前端构建复现、Reports 修复和候选打包。默认启动路径仍然保留 WinForms，WebView2 React UI 需要通过 `FrameScopeMonitor.exe --web-ui` 启动。

## 本次更新

- 新增 WebView2 React UI 旁路入口：不带参数仍启动 WinForms，带 `--web-ui` 才进入新界面。
- 新增 React / Vite / Framer Motion 前端，打包后由 `frontend` 目录提供静态资源。
- 新增 C# WebView2 bridge，前端请求和 C# 响应使用 `requestId` 对齐。
- WebView2 bridge 接入 `state.snapshot`、`config.get`、`config.save`、`processes.refresh`、`reports.list`、`reports.open`、`reports.openDirectory`、`reports.regenerate`、`targets.get`、`targets.save`、`monitor.start`、`monitor.stop` 和 `diagnostics.generate`。
- 报告打开、目录打开、重新生成、目标配置保存、诊断生成和监控启停都由 C# host 侧校验，不信任前端传入的本地路径。
- 修复页面切换时的新旧页面混绘、空白帧和整页转圈等待问题。
- 修复前端依赖复现问题，`tools\Run-Frontend.ps1 verify` 可以在没有旧 `node_modules` 的情况下完成 `npm ci`、类型检查、测试和构建。
- 修复 Reports 页面 `Size` 在窄布局下竖向换行的问题。
- 修复 packaged payload 未包含 React dist 的打包缺口，`dist\FrameScopeMonitor-payload\frontend` 现在包含 WebView2 UI 静态资源。
- 继续保留 WinForms 默认入口，WebView2 还没有默认替换 WinForms。

## 验证内容

本轮发布前重新执行并通过以下验证：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
- `.\tests\FrameScopeUiStateTests.exe`
- `.\tests\FrameScopeReportProgressTests.exe`
- `.\tests\FrameScopeReportManifestTests.exe`
- `.\tests\FrameScopeWebBridgeTests.exe`
- bundled Node 运行 `.\tests\chart-sampling-tests.js`
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`
- `"C:\Program Files\Git\cmd\git.exe" diff --check`
- packaged payload 文件存在性检查，确认 React dist 和 WebView2 运行文件都进入 payload。
- packaged WinForms fallback 截图验证。
- packaged WebView2 live smoke，覆盖 state、config、processes、reports、targets、monitor 和 diagnostics。
- packaged Reports open / openDirectory / regenerate 真实 UI 点击路径。
- packaged reduced motion 验证，未发现混绘、空白帧或整页等待。
- packaged Reports 900x760 窄布局截图，确认 `Size` 不竖向换行。
- 本机安装目录 payload copy 更新后的 WinForms / WebView2 / Reports / reduced motion 复测。
- payload 与安装目录关键文件 SHA256 比对。
- 残留进程检查。

## 发布产物

- `FrameScopeMonitor-Setup.exe`
- `FrameScopeMonitor-Installer.zip`
- `FrameScopeMonitor-LegacyCleanup.exe`

本轮最终构建时间为 2026-05-21 16:40 +08:00。

- `FrameScopeMonitor-Setup.exe`：`2829BFB33C0D270CD6347E74F737B3A8D384B6AA64AC802B2F9D0242E13CDEF9`
- `FrameScopeMonitor-Installer.zip`：`263788AD94F8A3BD115B760D3CBABAD4837DB5A66E111801403FF7A26C1028CD`
- `FrameScopeMonitor-LegacyCleanup.exe`：`686D9EC23A41BD210BB091D89184490554DECA38A101A8C5679AFA1D880FE9BB`
- `payload.zip`：`9A3F1C78E39B8C16C6BF9C7D247F3F1A3178B27DFC2356BD6A77D06D1BCC3527`

## 安装和启动

- 推荐下载 `FrameScopeMonitor-Setup.exe` 并运行安装。
- 默认安装目录是 `%LOCALAPPDATA%\FrameScopeMonitor`。
- 默认数据目录是 `%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs`。
- 默认启动仍是 WinForms：`FrameScopeMonitor.exe`。
- WebView2 React UI 通过旁路入口启动：`FrameScopeMonitor.exe --web-ui`。

## 本机安装更新说明

本机安装目录在本轮发布后会使用 release-item 白名单从 `dist\FrameScopeMonitor-payload` 同步到 `%LOCALAPPDATA%\FrameScopeMonitor`。

这属于 payload copy 更新，不是完整交互安装器安装。它不会声称用户会自动更新，也不会把 smoke 运行生成的 `artifacts`、临时 config 或临时 log 当作发布项复制到安装目录。

## 已知说明

- WebView2 React UI 是新旁路入口，尚未默认替换 WinForms。
- 如果只运行 `FrameScopeMonitor.exe`，看到的仍是 WinForms 界面，这是当前设计。
- `--web-ui` 需要安装目录或 payload 中存在 `frontend\index.html`。
- 本次发布不改变监测采样语义、报告数据结构、诊断报告语义、GameLite、WMI 或 SGuard 行为。
