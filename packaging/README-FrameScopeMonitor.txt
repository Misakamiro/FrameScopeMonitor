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
  tools\diagnose-pubg-capture.ps1
  dependencies\PawnIO\PawnIO_setup.exe
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

温度驱动（PawnIO）：

  安装包已附带 dependencies\PawnIO\PawnIO_setup.exe。
  在设置页点击「安装温度驱动」即可安装（需管理员权限，UAC 弹窗确认）。
  温度传感器读取依赖该驱动；未安装时温度列不可用。

游戏内 OSD 不显示（全屏独占模式）：

  DirectX「全屏（独占）」模式会绕过 Windows 桌面合成器 (DWM)，任何外部
  悬浮窗（包括 FrameScope OSD）都会被游戏画面直接覆盖。这是 Windows
  图形架构的限制，所有外部 OSD 工具皆如此，并非 FrameScope 的缺陷。
  解决方法：在游戏显示设置中改用「无边框窗口 / 窗口化全屏」。
  已验证：PUBG、Battlefield 6、Counter-Strike 2 的无边框窗口模式 OSD 正常。

采集异常（「采集进程异常退出，未能生成报告」）排查：

  1. 以管理员身份运行 FrameScope（ETW 帧采集需要足够权限）。
  2. 确认已进入游戏主界面，而不是启动器或大厅加载页。
  3. 关闭其他性能监控工具（RTSS、Afterburner 等，可能占用 ETW 会话）。
  4. 运行自助诊断脚本：
       powershell -NoProfile -ExecutionPolicy Bypass -File tools\diagnose-pubg-capture.ps1
     脚本会解读运行目录 status.json 的失败状态、盘点采样数据文件、检查
     硬件服务与残留 ETW 会话，并给出对应建议。
  5. 反馈问题时，请连同 %LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs\
     下对应的运行目录一并打包发送。
