using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

internal static partial class FrameScopeNativeMonitor
{
    private static bool RequestPresentMonStop(string presentMonPath, string sessionName)
    {
        if (string.IsNullOrWhiteSpace(presentMonPath) || string.IsNullOrWhiteSpace(sessionName)) return false;
        if (!File.Exists(presentMonPath)) return false;
        try
        {
            var args = JoinArguments(new[] { "--terminate_existing_session", "--session_name", sessionName });
            using (var stopper = Process.Start(new ProcessStartInfo
            {
                FileName = presentMonPath,
                Arguments = args,
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }))
            {
                if (stopper == null) return false;
                if (!stopper.WaitForExit(10000))
                {
                    try { stopper.Kill(); } catch { }
                    return false;
                }
                WriteFrameScopeLog("presentmon-stop-requested session=" + sessionName + " exit=" + stopper.ExitCode);
                return true;
            }
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("presentmon-stop-request-failed session=" + sessionName + " error=" + ex.Message);
            return false;
        }
    }

    private static void CleanupFrameScopePresentMonSessions(string presentMonPath)
    {
        var sessions = QueryFrameScopePresentMonSessions();
        foreach (var session in sessions)
        {
            var stopped = RequestPresentMonStop(presentMonPath, session);
            if (!stopped) StopEtwSessionWithLogman(session);
        }
        if (sessions.Count > 0)
        {
            WriteFrameScopeLog("presentmon-session-cleanup count=" + sessions.Count);
        }
    }

    private static List<string> QueryFrameScopePresentMonSessions()
    {
        var result = new List<string>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "logman.exe",
                Arguments = "query -ets",
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default
            };
            using (var process = Process.Start(psi))
            {
                if (process == null) return result;
                var output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(10000))
                {
                    try { process.Kill(); } catch { }
                    return result;
                }

                using (var reader = new StringReader(output ?? ""))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (!line.StartsWith(PresentMonSessionPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0 && !result.Contains(parts[0], StringComparer.OrdinalIgnoreCase)) result.Add(parts[0]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("presentmon-session-query-failed " + ex.Message);
        }
        return result;
    }

    private static bool StopEtwSessionWithLogman(string sessionName)
    {
        if (string.IsNullOrWhiteSpace(sessionName)) return false;
        try
        {
            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = "logman.exe",
                Arguments = "stop " + QuoteCommandArgument(sessionName) + " -ets",
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default
            }))
            {
                if (process == null) return false;
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(10000))
                {
                    try { process.Kill(); } catch { }
                    return false;
                }
                WriteFrameScopeLog("presentmon-session-logman-stop session=" + sessionName + " exit=" + process.ExitCode);
                return process.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("presentmon-session-logman-stop-failed session=" + sessionName + " error=" + ex.Message);
            return false;
        }
    }

    private static void WritePresentMonInfo(string path, string presentMonPath)
    {
        try
        {
            var item = new FileInfo(presentMonPath);
            var version = FileVersionInfo.GetVersionInfo(presentMonPath);
            var info = new Dictionary<string, object>
            {
                { "Path", item.FullName },
                { "Length", item.Length },
                { "LastWriteTime", item.LastWriteTime.ToString("o") },
                { "FileVersion", version.FileVersion },
                { "ProductVersion", version.ProductVersion },
                { "ProductName", version.ProductName },
                { "CompanyName", version.CompanyName },
                { "SHA256", null },
                { "HashSkipped", true }
            };
            File.WriteAllText(path, Json.Serialize(info), Encoding.UTF8);
        }
        catch { }
    }

    private static Dictionary<string, object> BuildPresentMonCaptureDiagnostics(MonitorSessionPaths paths, int? presentMonExitCode, bool presentMonExitedEarly, bool presentMonForcedStop)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var exists = File.Exists(paths.PresentMonCsv);
        var bytes = exists ? new FileInfo(paths.PresentMonCsv).Length : 0;
        var rows = exists ? CountCsvDataRows(paths.PresentMonCsv, 2000000) : 0;
        var stdoutTail = TailText(paths.PresentMonStdout, 2000);
        var stderrTail = TailText(paths.PresentMonStderr, 4000);
        var status = "captured";
        var message = "PresentMon 已成功写入帧数据。";

        if (!exists)
        {
            status = "no-presentmon-csv";
            message = "PresentMon 已启动，但没有创建 presentmon.csv。PUBG 场景下通常是渲染进程切换、FrameScope 与游戏权限级别不一致、全屏/覆盖层采集限制，或 ETW 采集被游戏/反作弊阻断。";
        }
        else if (bytes <= 0)
        {
            status = "empty-presentmon-csv";
            message = "PresentMon 创建了 presentmon.csv，但文件为空。下一次 PUBG 采集建议以管理员身份运行 FrameScope，并优先使用无边框或窗口化全屏。";
        }
        else if (rows <= 0)
        {
            status = "no-presentmon-rows";
            message = "PresentMon 创建了 presentmon.csv，但没有写入帧记录。FrameScope 仍会保留进程和系统采样数据用于诊断。";
        }
        else if (presentMonExitCode.HasValue && presentMonExitCode.Value != 0)
        {
            status = "presentmon-exit-error";
            message = "PresentMon 写入了部分输出，但以非 0 代码退出。请查看 presentmon.stderr.log 获取具体采集错误。";
        }
        else if (presentMonExitedEarly)
        {
            status = "presentmon-exited-early";
            message = "PresentMon 在 FrameScope 请求停止前提前退出。如果 PUBG 重启过渲染进程，请在最终游戏窗口出现后重新开始采集。";
        }

        result["PresentMonCsvExists"] = exists;
        result["PresentMonCsvBytes"] = bytes;
        result["PresentMonCsvRows"] = rows;
        result["PresentMonStdoutTail"] = stdoutTail;
        result["PresentMonStderrTail"] = stderrTail;
        result["FrameCaptureStatus"] = status;
        result["FrameCaptureMessage"] = message;
        result["PresentMonExitCode"] = presentMonExitCode;
        result["PresentMonExitedEarly"] = presentMonExitedEarly;
        result["PresentMonForcedStop"] = presentMonForcedStop;
        return result;
    }
}
