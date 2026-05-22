# FrameScope Monitor v1.1.3 更新说明

FrameScope Monitor v1.1.3 默认使用新的 WebView2 React 界面，并新增完整安装包。这个版本重点解决安装后启动环境不一致的问题：普通安装包适合大多数 Windows 系统，完整安装包适合缺少 WebView2 Runtime、离线环境或精简系统。

## 本次更新

- 默认启动 WebView2 React 新界面，安装后直接打开 `FrameScopeMonitor.exe` 即可使用。
- 旧 WinForms 主界面已移除，界面和 Windows 桌面宿主的视觉体验更统一。
- 优化页面切换过程，减少切换时的空白、闪动和卡顿感。
- Reports、Targets、Settings、Diagnostics 等页面继续接入真实功能，可查看报告、管理目标、保存设置并生成诊断信息。
- 安装包已修复新界面资源打包问题，安装完成后默认就是新界面。
- 新增 `FrameScopeMonitor-Full-Setup.exe`，内置 Microsoft Edge WebView2 Runtime 安装器。

## 下载建议

- 推荐大多数用户下载 `FrameScopeMonitor-Setup.exe`。
- 如果打开失败，或系统提示缺少 Microsoft Edge WebView2 Runtime，请下载 `FrameScopeMonitor-Full-Setup.exe`。
- `FrameScopeMonitor-Installer.zip` 适合想保存完整发布包的用户。
- `FrameScopeMonitor-LegacyCleanup.exe` 只用于清理早期版本残留，正常安装不需要运行。

## WebView2 Runtime 说明

FrameScope Monitor 的新界面依赖 Microsoft Edge WebView2 Runtime。大多数 Windows 系统已经自带该组件。

完整安装包会先检查系统是否已有 WebView2 Runtime。已经存在时不会重复安装；缺少时会先安装 Runtime，再继续安装 FrameScope Monitor。

卸载 FrameScope Monitor 不会删除系统里的 WebView2 Runtime，因为它可能被其他软件共用。

## 校验信息

- `FrameScopeMonitor-Setup.exe`: `AB483C71C349A1B69AE876B0553D3BA7FCCA364BD6167DC3531D5A58B4AA70D0`
- `FrameScopeMonitor-Full-Setup.exe`: `806CF3DF8AE8FD2F03257FDF999064E256D4DDC320DEEF6944F67DFEC5845D98`
- `FrameScopeMonitor-Installer.zip`: `1DB395F98A14CBC945C2A99264FB2FEB2F8B796FDC5F4611D3A79ECE673F4F07`
- `FrameScopeMonitor-LegacyCleanup.exe`: `96B5DF9D2663CAF569AC56D7CEE1BFA08C6640C9EA2F002CB0C735811AD5F974`
