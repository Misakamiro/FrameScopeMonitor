FrameScope Monitor 安装说明

FrameScope Monitor 使用 React + Vite 前端，并由 C# WebView2 host 加载。
系统需要 Microsoft Edge WebView2 Runtime。

安装包：

  FrameScopeMonitor-Setup.exe
    标准安装包，适用于已安装 WebView2 Runtime 的系统。

  FrameScopeMonitor-Full-Setup.exe
    包含 WebView2 Runtime 安装器；缺少 Runtime 或离线环境时使用。

主要 payload：

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
2. 在 Targets 页面确认目标进程。
3. 启动监控并进入游戏场景。
4. 游戏退出或停止监控后等待报告生成。
5. 在 Reports 页面查看报告、打开目录或重试生成。

报告说明：

  full        有帧数据，进程与系统采样器健康。
  partial     有帧数据，但至少一个辅助采样器不健康。
  diagnostic  无帧数据，但进程或系统数据可用于诊断。
  error       没有足够的帧、进程或系统数据。

完整报告必须同时包含 data.js、HTML 与 manifest。不要把仅有 HTML 的目录当作可打开报告。

卸载：

  运行 Uninstall-FrameScopeMonitor.cmd 或 FrameScopeUninstaller.exe。
  卸载 FrameScope Monitor 不会删除系统中的 WebView2 Runtime。
  是否删除历史 run 数据由卸载流程单独询问。

GameLite 边界：

  GameLite 是独立项目。普通 FrameScope build 和安装器不会安装、修改或运行
  GameLite 脚本，也不会创建或删除 GameLite WMI 触发器。发布包不包含
  GameLite 执行入口、兼容包装器或独立项目路径。
