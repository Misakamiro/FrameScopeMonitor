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
    private static int RunNativeMonitorSession(string[] args)
    {
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle; }
        catch { }

        Process presentMon = null;
        Process processSampler = null;
        Process systemSampler = null;
        MonitorSessionPaths paths = null;

        try
        {
            var waitSeconds = ParseIntArgument(args, "--WaitSeconds", 600);
            var captureSeconds = ParseIntArgument(args, "--CaptureSeconds", 0);
            var sampleIntervalMs = ParseIntArgument(args, "--SampleIntervalMs", 100);
            var processSampleIntervalMs = ParseIntArgument(args, "--ProcessSampleIntervalMs", 100);
            var slowSampleIntervalMs = ParseIntArgument(args, "--SlowSampleIntervalMs", 1000);
            var controlPollIntervalMs = ParseIntArgument(args, "--ControlPollIntervalMs", 3000);
            var targetProcessName = GetArgValue(args, "--TargetProcessName", "cs2.exe");
            var runRoot = GetArgValue(args, "--RunRoot", DefaultDataRoot);
            var runNamePrefix = GetArgValue(args, "--RunNamePrefix", GetTargetBaseName(targetProcessName));
            var requestedPresentMon = GetArgValue(args, "--PresentMonExe", "");
            var requestedProcessSampler = GetArgValue(args, "--ProcessSamplerExe", "");
            var requestedSystemSampler = GetArgValue(args, "--SystemSamplerExe", "");
            var targetAliases = GetArgValue(args, "--TargetProcessAliases", "");
            var targetDisplayName = GetArgValue(args, "--TargetDisplayName", "");
            var initialTargetPid = ParseIntArgument(args, "--InitialTargetPid", 0);

            if (sampleIntervalMs < 50) sampleIntervalMs = 50;
            if (processSampleIntervalMs < 100) processSampleIntervalMs = 100;
            if (controlPollIntervalMs < 1000) controlPollIntervalMs = 1000;
            if (slowSampleIntervalMs < sampleIntervalMs) slowSampleIntervalMs = Math.Max(1000, sampleIntervalMs);

            var targetBaseName = GetTargetBaseName(targetProcessName);
            if (string.IsNullOrWhiteSpace(targetBaseName)) targetBaseName = "cs2";
            var targetProcessBases = BuildTargetProcessBaseNames(string.IsNullOrWhiteSpace(targetAliases) ? targetProcessName : targetAliases, targetDisplayName);
            if (targetProcessBases.Count == 0) targetProcessBases.Add(targetBaseName);
            targetBaseName = targetProcessBases[0];
            var presentMonProcessName = targetProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? targetProcessName
                : targetBaseName + ".exe";

            if (string.IsNullOrWhiteSpace(runRoot)) runRoot = DefaultDataRoot;
            if (!Path.IsPathRooted(runRoot)) runRoot = Path.Combine(Root, runRoot);
            Directory.CreateDirectory(runRoot);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var prefix = SafeName(runNamePrefix, targetBaseName);
            paths = CreateMonitorSessionPaths(Path.Combine(runRoot, prefix + "-" + stamp));
            Directory.CreateDirectory(paths.RunDir);
            var presentMonSessionName = PresentMonSessionPrefix + SafeName(prefix, targetBaseName);

            var presentMonPath = ResolvePresentMonPath(requestedPresentMon);
            var processSamplerPath = ResolveProcessSamplerPath(requestedProcessSampler);
            var systemSamplerPath = ResolveSystemSamplerPath(requestedSystemSampler);
            var nvidiaSmiPath = ResolveNvidiaSmiPath();
            var captureUntilTargetExit = captureSeconds <= 0;
            var captureMode = captureUntilTargetExit ? "until-target-exit" : "timed";
            var presentMonCaptureMode = "waiting-for-pid";
            var presentMonCaptureTarget = presentMonProcessName;
            var presentMonArguments = "";

            WriteNativeMonitorStatus(paths, "created", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, new Dictionary<string, object>
            {
                { "PresentMonCaptureMode", presentMonCaptureMode },
                { "PresentMonCaptureTarget", presentMonCaptureTarget },
                { "PresentMonSessionName", presentMonSessionName },
                { "TargetProcessCandidates", string.Join(";", targetProcessBases.ToArray()) },
                { "InitialTargetPid", initialTargetPid }
            });

            if (string.IsNullOrWhiteSpace(presentMonPath) || !File.Exists(presentMonPath))
            {
                throw new InvalidOperationException("PresentMon not found. Expected portable copy under tools\\PresentMon-2.4.1-x64.exe or NVIDIA FrameView SDK PresentMon.");
            }
            if (string.IsNullOrWhiteSpace(processSamplerPath) || !File.Exists(processSamplerPath))
                {
                    throw new InvalidOperationException("FrameScopeProcessSampler.exe not found beside FrameScopeMonitor.exe.");
                }

            CleanupFrameScopePresentMonSessions(presentMonPath);
            WritePresentMonInfo(paths.PresentMonInfoPath, presentMonPath);

            WriteNativeMonitorStatus(paths, "waiting-for-target", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, new Dictionary<string, object>
            {
                { "WaitSeconds", waitSeconds },
                { "CaptureSeconds", captureSeconds },
                { "CaptureUntilTargetExit", captureUntilTargetExit },
                { "PresentMonCaptureMode", presentMonCaptureMode },
                { "PresentMonCaptureTarget", presentMonCaptureTarget },
                { "PresentMonSessionName", presentMonSessionName },
                { "TargetProcessCandidates", string.Join(";", targetProcessBases.ToArray()) },
                { "InitialTargetPid", initialTargetPid }
            });

            TargetProcessSnapshot selectedTarget;
            var targetProc = WaitForTargetProcess(targetProcessBases, waitSeconds, initialTargetPid, out selectedTarget);
            if (targetProc == null)
            {
                WriteNativeMonitorStatus(paths, "timeout-waiting-for-target", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, FrameScopeCapturePlanner.CreateTargetNotFoundDiagnostic(targetProcessBases, initialTargetPid, string.IsNullOrWhiteSpace(targetDisplayName) ? targetProcessName : targetDisplayName, waitSeconds));
                return 2;
            }

            using (targetProc)
            {
                var startTime = DateTime.Now;
                targetBaseName = selectedTarget != null && !string.IsNullOrWhiteSpace(selectedTarget.BaseName) ? selectedTarget.BaseName : targetProc.ProcessName;
                presentMonProcessName = targetBaseName + ".exe";
                var presentMonPlan = FrameScopeCapturePlanner.CreatePresentMonPlan(
                    targetProcessBases,
                    targetProcessName,
                    targetDisplayName,
                    targetProc.Id,
                    paths.PresentMonCsv,
                    presentMonSessionName,
                    !captureUntilTargetExit,
                    captureSeconds);
                presentMonCaptureMode = presentMonPlan.CaptureMode;
                presentMonCaptureTarget = presentMonPlan.CaptureTarget;
                presentMonArguments = JoinArguments(presentMonPlan.Arguments);

                WriteNativeMonitorStatus(paths, "starting-presentmon", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, new Dictionary<string, object>
                {
                    { "TargetPid", targetProc.Id },
                    { "TargetResolvedProcess", presentMonProcessName },
                    { "TargetWindowTitle", selectedTarget == null ? "" : selectedTarget.WindowTitle },
                    { "TargetHasMainWindow", selectedTarget != null && selectedTarget.HasMainWindow },
                    { "StartTime", startTime.ToString("o") },
                    { "PresentMonCaptureMode", presentMonCaptureMode },
                    { "PresentMonCaptureTarget", presentMonCaptureTarget },
                    { "PresentMonSessionName", presentMonSessionName },
                    { "PresentMonArgs", presentMonArguments },
                    { "TargetProcessCandidates", string.Join(";", targetProcessBases.ToArray()) },
                    { "InitialTargetPid", initialTargetPid }
                });

                presentMon = StartNativeMonitorChild(
                    presentMonPath,
                    presentMonArguments,
                    Root,
                    paths.PresentMonStdout,
                    paths.PresentMonStderr,
                    ProcessPriorityClass.BelowNormal);

                processSampler = StartNativeMonitorChild(
                    processSamplerPath,
                    JoinArguments(new[]
                    {
                        "--target", targetBaseName,
                        "--interval", processSampleIntervalMs.ToString(CultureInfo.InvariantCulture),
                        "--parent-pid", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
                        "--process-csv", paths.ProcessCsv,
                        "--top-cpu-csv", paths.TopCpuCsv,
                        "--top-io-csv", paths.TopIoCsv,
                        "--alerts-csv", paths.AlertsCsv
                    }),
                    Root);

                if (!string.IsNullOrWhiteSpace(systemSamplerPath) && File.Exists(systemSamplerPath))
                {
                    var systemArgs = new List<string>
                    {
                        "--target", targetBaseName,
                        "--interval", slowSampleIntervalMs.ToString(CultureInfo.InvariantCulture),
                        "--parent-pid", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
                        "--system-csv", paths.SamplesCsv
                    };
                    if (!string.IsNullOrWhiteSpace(nvidiaSmiPath)) systemArgs.AddRange(new[] { "--nvidia-smi", nvidiaSmiPath });
                    systemSampler = StartNativeMonitorChild(systemSamplerPath, JoinArguments(systemArgs.ToArray()), Root);
                }
                else
                {
                    File.WriteAllText(paths.SlowSamplerLogPath, "FrameScopeSystemSampler.exe was not found.", Encoding.UTF8);
                }

                var sampleIndex = 0;
                var captureDeadline = captureUntilTargetExit ? DateTime.MaxValue : DateTime.Now.AddSeconds(captureSeconds);
                var lastStatusWrite = DateTime.MinValue;
                var presentMonExitCode = (int?)null;
                var presentMonExitedEarly = false;
                var presentMonForcedStop = false;
                var presentMonStopRequested = false;

                while (DateTime.Now < captureDeadline)
                {
                    if (presentMon != null)
                    {
                        try
                        {
                            if (presentMon.HasExited && !presentMonExitCode.HasValue)
                            {
                                presentMonExitCode = presentMon.ExitCode;
                                presentMonExitedEarly = true;
                            }
                        }
                        catch { }
                    }

                    var now = DateTime.Now;
                    if ((now - lastStatusWrite).TotalSeconds >= 10)
                    {
                        WriteNativeMonitorStatus(paths, "capturing", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, new Dictionary<string, object>
                        {
                            { "TargetPid", targetProc.Id },
                            { "PresentMonPid", presentMon == null ? (int?)null : presentMon.Id },
                            { "ProcessSamplerPid", processSampler == null ? (int?)null : processSampler.Id },
                            { "SystemSamplerPid", systemSampler == null ? (int?)null : systemSampler.Id },
                            { "ProcessSamplerExited", ProcessExited(processSampler) },
                            { "PresentMonExitedEarly", presentMonExitedEarly },
                            { "PresentMonCsvExists", File.Exists(paths.PresentMonCsv) },
                            { "PresentMonCsvBytes", File.Exists(paths.PresentMonCsv) ? new FileInfo(paths.PresentMonCsv).Length : 0 },
                            { "SampleIndex", sampleIndex },
                            { "PresentMonCaptureMode", presentMonCaptureMode },
                            { "PresentMonCaptureTarget", presentMonCaptureTarget },
                            { "PresentMonSessionName", presentMonSessionName },
                            { "PresentMonArgs", presentMonArguments }
                        });
                        lastStatusWrite = now;
                    }

                    sampleIndex++;
                    var remainingMs = controlPollIntervalMs;
                    if (!captureUntilTargetExit)
                    {
                        remainingMs = Math.Max(1, Math.Min(controlPollIntervalMs, (int)Math.Max(1, (captureDeadline - DateTime.Now).TotalMilliseconds)));
                    }

                    try
                    {
                        if (targetProc.WaitForExit(remainingMs)) break;
                    }
                    catch
                    {
                        if (!IsAnyTargetProcessRunning(targetProcessBases)) break;
                        Thread.Sleep(remainingMs);
                    }
                }

                StopMonitorChild(processSampler, 5000, true);
                StopMonitorChild(systemSampler, 5000, true);

                if (presentMon != null && !ProcessExited(presentMon))
                {
                    presentMonStopRequested = RequestPresentMonStop(presentMonPath, presentMonSessionName);
                    if (!presentMon.WaitForExit(15000))
                    {
                        presentMonForcedStop = true;
                        StopMonitorChild(presentMon, 0, true);
                    }
                }
                if (presentMon != null && !presentMonExitCode.HasValue)
                {
                    try { presentMonExitCode = presentMon.ExitCode; }
                    catch { }
                }
                if (!presentMonExitCode.HasValue && File.Exists(paths.PresentMonCsv)) presentMonExitCode = 0;
                CleanupFrameScopePresentMonSessions(presentMonPath);

                var endTime = DateTime.Now;
                var reportHtml = Path.Combine(paths.RunDir, "charts", "framescope-interactive-report.html");
                var captureDiagnostics = BuildPresentMonCaptureDiagnostics(paths, presentMonExitCode, presentMonExitedEarly, presentMonForcedStop);
                var finalizing = new Dictionary<string, object>
                {
                    { "TargetPid", targetProc.Id },
                    { "TargetResolvedProcess", presentMonProcessName },
                    { "TargetWindowTitle", selectedTarget == null ? "" : selectedTarget.WindowTitle },
                    { "TargetHasMainWindow", selectedTarget != null && selectedTarget.HasMainWindow },
                    { "ExitCode", presentMonExitCode },
                    { "SampleCount", sampleIndex },
                    { "PresentMonStopRequested", presentMonStopRequested },
                    { "PresentMonForcedStop", presentMonForcedStop },
                    { "PresentMonExitedEarly", presentMonExitedEarly },
                    { "EndTime", endTime.ToString("o") },
                    { "SummaryPath", paths.SummaryPath },
                    { "ReportHtml", reportHtml },
                    { "PresentMonCaptureMode", presentMonCaptureMode },
                    { "PresentMonCaptureTarget", presentMonCaptureTarget },
                    { "PresentMonSessionName", presentMonSessionName },
                    { "PresentMonArgs", presentMonArguments },
                    { "TargetProcessCandidates", string.Join(";", targetProcessBases.ToArray()) },
                    { "InitialTargetPid", initialTargetPid }
                };
                AddDictionary(finalizing, captureDiagnostics);
                WriteNativeMonitorStatus(paths, "finalizing", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, finalizing);

                WriteEventCsvHeader(paths.EventsCsv);
                WriteNativeMonitorSummary(paths, presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, presentMonExitCode, presentMonExitedEarly, presentMonForcedStop, reportHtml, presentMonCaptureMode, presentMonCaptureTarget, presentMonArguments, captureDiagnostics);

                var done = new Dictionary<string, object>
                {
                    { "TargetPid", targetProc.Id },
                    { "TargetResolvedProcess", presentMonProcessName },
                    { "TargetWindowTitle", selectedTarget == null ? "" : selectedTarget.WindowTitle },
                    { "TargetHasMainWindow", selectedTarget != null && selectedTarget.HasMainWindow },
                    { "ExitCode", presentMonExitCode },
                    { "SampleCount", sampleIndex },
                    { "PresentMonStopRequested", presentMonStopRequested },
                    { "PresentMonForcedStop", presentMonForcedStop },
                    { "PresentMonExitedEarly", presentMonExitedEarly },
                    { "EndTime", endTime.ToString("o") },
                    { "SummaryPath", paths.SummaryPath },
                    { "ReportHtml", reportHtml },
                    { "ReportError", null },
                    { "ReportOpened", false },
                    { "MonitorMode", "native-csharp" },
                    { "PresentMonCaptureMode", presentMonCaptureMode },
                    { "PresentMonCaptureTarget", presentMonCaptureTarget },
                    { "PresentMonSessionName", presentMonSessionName },
                    { "PresentMonArgs", presentMonArguments },
                    { "TargetProcessCandidates", string.Join(";", targetProcessBases.ToArray()) },
                    { "InitialTargetPid", initialTargetPid }
                };
                AddDictionary(done, captureDiagnostics);
                WriteNativeMonitorStatus(paths, "done", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, done);
            }

            return 0;
        }
        catch (Exception ex)
        {
            StopMonitorChild(processSampler, 0, true);
            StopMonitorChild(systemSampler, 0, true);
            StopMonitorChild(presentMon, 0, true);

            if (paths != null)
            {
                try { File.WriteAllText(paths.ErrorPath, ex.ToString(), Encoding.UTF8); }
                catch { }
                try
                {
                    WriteNativeMonitorStatus(paths, "error", "", "unknown", 100, 100, 1000, 3000, "", "", "", new Dictionary<string, object>
                    {
                        { "Error", ex.ToString() },
                        { "ErrorPath", paths.ErrorPath },
                        { "MonitorMode", "native-csharp" }
                    });
                }
                catch { }
            }

            WriteFrameScopeLog("native-monitor-session-error " + ex.Message);
            return 1;
        }
        finally
        {
            DisposeProcess(processSampler);
            DisposeProcess(systemSampler);
            DisposeProcess(presentMon);
        }
    }
}
