FrameScope Monitor 安装说明

本版本默认启动 WebView2 React 新界面。旧 WinForms 主界面已经从主程序构建中移除。

安装后直接运行：

  FrameScopeMonitor.exe

安装包选择：
  FrameScopeMonitor-Setup.exe
    推荐普通用户下载。适合大多数已经带有 Microsoft Edge WebView2 Runtime 的系统。

  FrameScopeMonitor-Full-Setup.exe
    完整安装包，内置 Microsoft Edge WebView2 Runtime 安装器。
    适合打开失败、提示缺少 WebView2 Runtime、离线环境或精简系统。
    安装器会先检测 WebView2 Runtime；已存在时不会重复安装，缺失时会静默安装 Runtime 后继续安装 FrameScope Monitor。

如果启动时提示系统缺少 Microsoft Edge WebView2 Runtime，请安装完整安装包，或前往 Microsoft 官网安装 WebView2 Runtime。

主要文件：

  FrameScopeMonitor.exe
  FrameScopeProcessSampler.exe
  FrameScopeSystemSampler.exe
  FrameScopeReportGenerator.exe
  Microsoft.Web.WebView2.Core.dll
  Microsoft.Web.WebView2.WinForms.dll
  WebView2Loader.dll
  tools\PresentMon-2.4.1-x64.exe
  frontend\index.html
  frontend\assets\*

默认安装目录：

  %LOCALAPPDATA%\FrameScopeMonitor

默认数据目录：

  %LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs

基本流程：

1. 启动 FrameScope Monitor。
2. 在“监控目标”页确认或保存需要监测的进程目标。
3. 点击启动监控。
4. 进入游戏场景。
5. 停止监控或退出游戏后等待报告生成。
6. 在“报告”页打开 HTML 报告、报告目录或重新生成报告。

卸载：

  运行 Uninstall-FrameScopeMonitor.cmd 或 FrameScopeUninstaller.exe。
  卸载 FrameScope Monitor 不会删除系统里的 Microsoft Edge WebView2 Runtime。
