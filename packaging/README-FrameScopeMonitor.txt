FrameScope Monitor

安装：
运行 FrameScopeMonitor-Setup.exe。
安装时可以选择数据和报告目录。

安装位置：
%LOCALAPPDATA%\FrameScopeMonitor

数据和报告：
默认写入 %LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs。

说明：
监测会话由 FrameScopeMonitor.exe 的原生 C# 模式执行，游戏运行期间不再常驻 PowerShell 监测壳。
报告生成由 FrameScopeReportGenerator.exe 的原生 .NET 模式执行，不再调用 Python，也不再打包 Python runtime。
安装包内置 PresentMon 和所有必需组件，用户不需要额外安装依赖。

卸载：
卸载时会询问是否同时删除数据和报告目录；选择“否”会保留历史报告和 CSV 数据。

旧版本完全清理：
如果用户安装过早期版本并出现 PowerShell/Python 监测残留、快捷方式或卸载入口残留，请单独运行 FrameScopeMonitor-LegacyCleanup.exe。
该工具会扫描旧进程、旧启动项、计划任务、快捷方式、卸载注册表、旧程序目录，并可删除旧数据和 HTML 报告目录。
清理日志会写到桌面。
