FrameScope Monitor 安装说明

本版本默认启动 WebView2 React 新界面。旧 WinForms 主界面已经从主程序构建中移除。

安装后直接运行：

  FrameScopeMonitor.exe

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
