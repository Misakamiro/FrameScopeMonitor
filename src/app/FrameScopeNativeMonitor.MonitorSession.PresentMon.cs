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
                return stopper.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("presentmon-stop-request-failed session=" + sessionName + " error=" + ex.Message);
            return false;
        }
    }

    private static bool RequestPresentMonStopWithTargetedFallback(string presentMonPath, string sessionName)
    {
        FrameScopePresentMonSessionStopResult result = FrameScopePresentMonSessionPolicy.StopOwnedSession(
            sessionName,
            delegate(string ownedSession) { return RequestPresentMonStop(presentMonPath, ownedSession); },
            delegate(string ownedSession) { return StopEtwSessionWithLogman(ownedSession); });
        WriteFrameScopeLog(
            "presentmon-owned-session-stop session=" + result.SessionName
            + " primarySucceeded=" + result.PrimarySucceeded
            + " fallbackAttempted=" + result.FallbackAttempted
            + " fallbackSucceeded=" + result.FallbackSucceeded
            + " stopped=" + result.Succeeded);
        return result.Succeeded;
    }

    private static void CleanupStaleOwnedPresentMonSessions(string presentMonPath)
    {
        IList<FrameScopePresentMonSessionStopResult> results = FrameScopePresentMonSessionPolicy.StopStaleOwnedSessions(
            QueryFrameScopePresentMonSessions(),
            IsPresentMonSessionOwnerAlive,
            delegate(string ownedSession) { return RequestPresentMonStop(presentMonPath, ownedSession); },
            delegate(string ownedSession) { return StopEtwSessionWithLogman(ownedSession); });
        if (results.Count > 0)
        {
            int stoppedCount = results.Count(result => result.Succeeded);
            int failedCount = results.Count - stoppedCount;
            WriteFrameScopeLog(
                "presentmon-stale-owned-session-cleanup attempted=" + results.Count.ToString(CultureInfo.InvariantCulture)
                + " stopped=" + stoppedCount.ToString(CultureInfo.InvariantCulture)
                + " failed=" + failedCount.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void CleanupOwnedPresentMonSessionIfPresent(string presentMonPath, string sessionName)
    {
        if (string.IsNullOrWhiteSpace(sessionName)) return;
        var sessions = QueryFrameScopePresentMonSessions();
        if (!sessions.Contains(sessionName, StringComparer.OrdinalIgnoreCase)) return;

        bool stopped = RequestPresentMonStopWithTargetedFallback(presentMonPath, sessionName);
        WriteFrameScopeLog("presentmon-owned-session-cleanup session=" + sessionName + " stopped=" + stopped);
    }

    private static bool IsPresentMonSessionOwnerAlive(int ownerPid)
    {
        try
        {
            using (var owner = Process.GetProcessById(ownerPid))
            {
                return !owner.HasExited;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("presentmon-session-owner-check-unknown pid=" + ownerPid.ToString(CultureInfo.InvariantCulture) + " error=" + ex.Message);
            return true;
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

    private static long WaitForPresentMonCsvFlush(string csvPath, int waitMs)
    {
        if (string.IsNullOrWhiteSpace(csvPath) || waitMs <= 0) return 0;
        var stopwatch = Stopwatch.StartNew();
        long lastSize = -1;
        int stableChecks = 0;
        while (stopwatch.ElapsedMilliseconds < waitMs)
        {
            try
            {
                if (File.Exists(csvPath))
                {
                    long size = new FileInfo(csvPath).Length;
                    if (size == lastSize)
                    {
                        stableChecks++;
                        if (stableChecks >= 2) break;
                    }
                    else
                    {
                        lastSize = size;
                        stableChecks = 0;
                    }
                }
            }
            catch
            {
            }
            Thread.Sleep(100);
        }
        stopwatch.Stop();
        return stopwatch.ElapsedMilliseconds;
    }

    private static bool IsTargetPidRunning(int pid, List<string> targetProcessBases)
    {
        if (pid <= 0) return false;
        try
        {
            using (Process process = Process.GetProcessById(pid))
            {
                if (process.HasExited) return false;
                if (targetProcessBases == null || targetProcessBases.Count == 0) return true;
                string processName = process.ProcessName ?? "";
                foreach (string candidate in targetProcessBases)
                {
                    if (string.Equals(processName, candidate, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, object> BuildPresentMonCaptureDiagnostics(
        MonitorSessionPaths paths,
        int? presentMonExitCode,
        bool presentMonExitedEarly,
        bool presentMonForcedStop,
        FrameScopePresentMonCaptureDiagnosticContext context)
    {
        return FrameScopePresentMonDiagnostics.BuildCaptureDiagnostics(
            paths.PresentMonCsv,
            paths.PresentMonStdout,
            paths.PresentMonStderr,
            presentMonExitCode,
            presentMonExitedEarly,
            presentMonForcedStop,
            context);
    }
}
