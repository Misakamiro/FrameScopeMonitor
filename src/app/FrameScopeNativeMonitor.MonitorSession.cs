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
        NativeMonitorSamplerState processSamplerState = null;
        NativeMonitorSamplerState systemSamplerState = null;
        MonitorSessionPaths paths = null;
        Dictionary<string, object> presentMonPreflight = null;
        bool verboseLogs = false;
        bool performanceDiagnosticsLogs = false;
        var sessionStopwatch = Stopwatch.StartNew();

        try
        {
            verboseLogs = ParseBoolArgument(args, "--EnableVerboseLogs", false);
            performanceDiagnosticsLogs = ParseBoolArgument(args, "--EnablePerformanceDiagnosticsLogs", false);
            var waitSeconds = ParseIntArgument(args, "--WaitSeconds", 600);
            var captureSeconds = ParseIntArgument(args, "--CaptureSeconds", 0);
            var sampleIntervalMs = ParseIntArgument(args, "--SampleIntervalMs", 100);
            var processSampleIntervalMs = ParseIntArgument(args, "--ProcessSampleIntervalMs", 100);
            var slowSampleIntervalMs = ParseIntArgument(args, "--SlowSampleIntervalMs", 1000);
            var enableCpuCoreTelemetry = ParseBoolArgument(args, "--EnableCpuCoreTelemetry", false);
            var cpuCoreSampleIntervalMs = ParseIntArgument(args, "--CpuCoreSampleIntervalMs", 1000);
            var enableCpuVoltageTelemetry = ParseBoolArgument(args, "--EnableCpuVoltageTelemetry", false);
            var cpuVoltageSampleIntervalMs = ParseIntArgument(args, "--CpuVoltageSampleIntervalMs", 1000);
            var cpuVoltageProvider = GetArgValue(args, "--CpuVoltageProvider", "auto");
            var enableCpuVidTelemetry = ParseBoolArgument(args, "--EnableCpuVidTelemetry", false);
            var cpuVidSampleIntervalMs = ParseIntArgument(args, "--CpuVidSampleIntervalMs", 1000);
            var cpuVidProvider = GetArgValue(args, "--CpuVidProvider", "auto");
            var controlPollIntervalMs = ParseIntArgument(args, "--ControlPollIntervalMs", 3000);
            var targetProcessName = GetArgValue(args, "--TargetProcessName", "cs2.exe");
            var runRoot = GetArgValue(args, "--RunRoot", DefaultDataRoot);
            var runNamePrefix = GetArgValue(args, "--RunNamePrefix", GetTargetBaseName(targetProcessName));
            var requestedPresentMon = GetArgValue(args, "--PresentMonExe", "");
            var requestedProcessSampler = GetArgValue(args, "--ProcessSamplerExe", "");
            var requestedSystemSampler = GetArgValue(args, "--SystemSamplerExe", "");
            var targetAliases = GetArgValue(args, "--TargetProcessAliases", "");
            var targetDisplayName = GetArgValue(args, "--TargetDisplayName", "");
            var targetDisplayLabel = string.IsNullOrWhiteSpace(targetDisplayName) ? targetProcessName : targetDisplayName;
            var initialTargetPid = ParseIntArgument(args, "--InitialTargetPid", 0);

            if (sampleIntervalMs < 50) sampleIntervalMs = 50;
            if (processSampleIntervalMs < 100) processSampleIntervalMs = 100;
            if (controlPollIntervalMs < 1000) controlPollIntervalMs = 1000;
            if (slowSampleIntervalMs < sampleIntervalMs) slowSampleIntervalMs = Math.Max(1000, sampleIntervalMs);
            if (cpuCoreSampleIntervalMs <= 0) cpuCoreSampleIntervalMs = 1000;
            if (cpuCoreSampleIntervalMs < 500) cpuCoreSampleIntervalMs = 500;
            if (cpuVidSampleIntervalMs <= 0) cpuVidSampleIntervalMs = 1000;
            if (cpuVidSampleIntervalMs < 500) cpuVidSampleIntervalMs = 500;
            if (cpuVidSampleIntervalMs > 5000) cpuVidSampleIntervalMs = 5000;

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
            var presentMonSessionName = FrameScopePresentMonSessionPolicy.CreateSessionName(
                prefix,
                Process.GetCurrentProcess().Id,
                Guid.NewGuid().ToString("N"));

            var presentMonPath = ResolvePresentMonPath(requestedPresentMon);
            var processSamplerPath = ResolveProcessSamplerPath(requestedProcessSampler);
            var systemSamplerPath = ResolveSystemSamplerPath(requestedSystemSampler);
            processSamplerState = CreateNativeMonitorSamplerState(true, processSamplerPath, paths.ProcessCsv, paths.ProcessSamplerStdout, paths.ProcessSamplerStderr);
            systemSamplerState = CreateNativeMonitorSamplerState(true, systemSamplerPath, paths.SamplesCsv, paths.SystemSamplerStdout, paths.SystemSamplerStderr);
            presentMonPreflight = FrameScopePresentMonDiagnostics.BuildPreflightDiagnostics(presentMonPath);
            var nvidiaSmiPath = ResolveNvidiaSmiPath();
            var captureUntilTargetExit = captureSeconds <= 0;
            var captureMode = captureUntilTargetExit ? "until-target-exit" : "timed";
            var presentMonCaptureMode = "waiting-for-pid";
            var presentMonCaptureTarget = presentMonProcessName;
            var presentMonArguments = "";

            var createdStatus = new Dictionary<string, object>
            {
                { "PresentMonCaptureMode", presentMonCaptureMode },
                { "PresentMonCaptureTarget", presentMonCaptureTarget },
                { "PresentMonSessionName", presentMonSessionName },
                { "TargetDisplayName", targetDisplayLabel },
                { "TargetProcessCandidates", string.Join(";", targetProcessBases.ToArray()) },
                { "InitialTargetPid", initialTargetPid },
                { "CpuCoreTelemetryEnabled", enableCpuCoreTelemetry },
                { "CpuCoreSampleIntervalMs", cpuCoreSampleIntervalMs },
                { "CpuVoltageTelemetryEnabled", enableCpuVoltageTelemetry },
                { "CpuVoltageSampleIntervalMs", cpuVoltageSampleIntervalMs },
                { "CpuVoltageProvider", cpuVoltageProvider },
                { "CpuVoltageAvailable", false },
                { "CpuVoltageVcoreAvailable", false },
                { "CpuVoltageStatus", "unavailable" },
                { "CpuVidTelemetryEnabled", enableCpuVidTelemetry },
                { "CpuVidSampleIntervalMs", cpuVidSampleIntervalMs },
                { "CpuVidProvider", cpuVidProvider },
                { "CpuVidAvailable", false },
                { "CpuVidStatus", "unavailable" }
            };
            AddDictionary(createdStatus, presentMonPreflight);
            WriteNativeMonitorStatus(paths, "created", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, createdStatus);
            WriteVerboseFrameScopeLog(verboseLogs, delegate { return "monitor-session-created run=" + paths.RunDir + " target=" + presentMonProcessName + " captureMode=" + captureMode; });
            WritePerformanceFrameScopeLog(performanceDiagnosticsLogs, delegate { return "monitor-session-created-ms=" + sessionStopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " run=" + paths.RunDir; });

            if (string.IsNullOrWhiteSpace(presentMonPath) || !File.Exists(presentMonPath))
            {
                throw new InvalidOperationException("PresentMon not found. Expected portable copy under tools\\PresentMon-2.4.1-x64.exe or NVIDIA FrameView SDK PresentMon.");
            }
            CleanupStaleOwnedPresentMonSessions(presentMonPath);
            WritePresentMonInfo(paths.PresentMonInfoPath, presentMonPath);

            var waitingStatus = new Dictionary<string, object>
            {
                { "WaitSeconds", waitSeconds },
                { "CaptureSeconds", captureSeconds },
                { "CaptureUntilTargetExit", captureUntilTargetExit },
                { "PresentMonCaptureMode", presentMonCaptureMode },
                { "PresentMonCaptureTarget", presentMonCaptureTarget },
                { "PresentMonSessionName", presentMonSessionName },
                { "TargetDisplayName", targetDisplayLabel },
                { "TargetProcessCandidates", string.Join(";", targetProcessBases.ToArray()) },
                { "InitialTargetPid", initialTargetPid },
                { "CpuCoreTelemetryEnabled", enableCpuCoreTelemetry },
                { "CpuCoreSampleIntervalMs", cpuCoreSampleIntervalMs },
                { "CpuVoltageTelemetryEnabled", enableCpuVoltageTelemetry },
                { "CpuVoltageSampleIntervalMs", cpuVoltageSampleIntervalMs },
                { "CpuVoltageProvider", cpuVoltageProvider },
                { "CpuVoltageAvailable", false },
                { "CpuVoltageVcoreAvailable", false },
                { "CpuVoltageStatus", "unavailable" },
                { "CpuVidTelemetryEnabled", enableCpuVidTelemetry },
                { "CpuVidSampleIntervalMs", cpuVidSampleIntervalMs },
                { "CpuVidProvider", cpuVidProvider },
                { "CpuVidAvailable", false },
                { "CpuVidStatus", "unavailable" }
            };
            AddDictionary(waitingStatus, presentMonPreflight);
            WriteNativeMonitorStatus(paths, "waiting-for-target", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, waitingStatus);

            TargetProcessSnapshot selectedTarget;
            var targetWaitStopwatch = Stopwatch.StartNew();
            var targetProc = WaitForTargetProcess(targetProcessBases, waitSeconds, initialTargetPid, out selectedTarget);
            targetWaitStopwatch.Stop();
            WritePerformanceFrameScopeLog(performanceDiagnosticsLogs, delegate
            {
                return "monitor-target-wait-ms=" + targetWaitStopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) +
                    " found=" + (targetProc != null) +
                    " initialPid=" + initialTargetPid.ToString(CultureInfo.InvariantCulture);
            });
            if (targetProc == null)
            {
                var timeoutStatus = FrameScopeCapturePlanner.CreateTargetNotFoundDiagnostic(targetProcessBases, initialTargetPid, string.IsNullOrWhiteSpace(targetDisplayName) ? targetProcessName : targetDisplayName, waitSeconds);
                timeoutStatus["TargetDisplayName"] = targetDisplayLabel;
                AddDictionary(timeoutStatus, presentMonPreflight);
                WriteNativeMonitorStatus(paths, "timeout-waiting-for-target", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, timeoutStatus);
                WriteVerboseFrameScopeLog(verboseLogs, delegate { return "monitor-target-timeout target=" + presentMonProcessName + " waitSeconds=" + waitSeconds.ToString(CultureInfo.InvariantCulture); });
                sessionStopwatch.Stop();
                WritePerformanceFrameScopeLog(performanceDiagnosticsLogs, delegate { return "monitor-session-total-ms=" + sessionStopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " samples=0 exit=2"; });
                return 2;
            }

            using (targetProc)
            {
                var startTime = DateTime.Now;
                targetBaseName = selectedTarget != null && !string.IsNullOrWhiteSpace(selectedTarget.BaseName) ? selectedTarget.BaseName : targetProc.ProcessName;
                presentMonProcessName = targetBaseName + ".exe";
                WriteVerboseFrameScopeLog(verboseLogs, delegate
                {
                    return "monitor-target-selected process=" + presentMonProcessName +
                        " pid=" + targetProc.Id.ToString(CultureInfo.InvariantCulture) +
                        " window=" + (selectedTarget == null ? "" : selectedTarget.WindowTitle);
                });
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

                var startingStatus = new Dictionary<string, object>
                {
                    { "TargetPid", targetProc.Id },
                    { "TargetResolvedProcess", presentMonProcessName },
                    { "TargetDisplayName", targetDisplayLabel },
                    { "TargetWindowTitle", selectedTarget == null ? "" : selectedTarget.WindowTitle },
                    { "TargetHasMainWindow", selectedTarget != null && selectedTarget.HasMainWindow },
                    { "StartTime", startTime.ToString("o") },
                    { "PresentMonCaptureMode", presentMonCaptureMode },
                    { "PresentMonCaptureTarget", presentMonCaptureTarget },
                    { "PresentMonSessionName", presentMonSessionName },
                    { "PresentMonArgs", presentMonArguments },
                    { "TargetProcessCandidates", string.Join(";", targetProcessBases.ToArray()) },
                    { "InitialTargetPid", initialTargetPid }
                };
                AddDictionary(startingStatus, presentMonPreflight);
                WriteNativeMonitorStatus(paths, "starting-presentmon", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, startingStatus);

                DateTime? presentMonStartedAt = null;
                DateTime? presentMonExitedAt = null;
                Stopwatch presentMonRuntimeStopwatch = null;
                long? presentMonRuntimeMs = null;
                bool? targetRunningAtPresentMonExitCheck = null;
                bool? targetPidRunningAtPresentMonExitCheck = null;
                var childStartStopwatch = Stopwatch.StartNew();
                presentMon = StartNativeMonitorChild(
                    presentMonPath,
                    presentMonArguments,
                    Root,
                    paths.PresentMonStdout,
                    paths.PresentMonStderr,
                    ProcessPriorityClass.BelowNormal);
                if (presentMon != null)
                {
                    presentMonStartedAt = DateTime.Now;
                    presentMonRuntimeStopwatch = Stopwatch.StartNew();
                }

                processSampler = StartNativeMonitorSampler(
                    processSamplerState,
                    JoinArguments(new[]
                    {
                        "--target", targetBaseName,
                        "--target-aliases", string.Join(";", targetProcessBases.ToArray()),
                        "--interval", processSampleIntervalMs.ToString(CultureInfo.InvariantCulture),
                        "--parent-pid", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
                        "--stop-file", paths.SamplerStopPath,
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
                        "--target-aliases", string.Join(";", targetProcessBases.ToArray()),
                        "--interval", slowSampleIntervalMs.ToString(CultureInfo.InvariantCulture),
                        "--parent-pid", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
                        "--stop-file", paths.SamplerStopPath,
                        "--system-csv", paths.SamplesCsv,
                        "--enable-cpu-core-telemetry", enableCpuCoreTelemetry ? "true" : "false",
                        "--cpu-core-csv", paths.CpuCoreSamplesCsv,
                        "--cpu-core-status", paths.CpuCoreTelemetryStatusPath,
                        "--cpu-core-interval", cpuCoreSampleIntervalMs.ToString(CultureInfo.InvariantCulture),
                        "--enable-cpu-voltage-telemetry", enableCpuVoltageTelemetry ? "true" : "false",
                        "--cpu-voltage-csv", paths.CpuVoltageSamplesCsv,
                        "--cpu-voltage-status", paths.CpuVoltageTelemetryStatusPath,
                        "--cpu-voltage-interval", cpuVoltageSampleIntervalMs.ToString(CultureInfo.InvariantCulture),
                        "--cpu-voltage-provider", cpuVoltageProvider,
                        "--enable-cpu-vid-telemetry", enableCpuVidTelemetry ? "true" : "false",
                        "--cpu-vid-csv", paths.CpuVidSamplesCsv,
                        "--cpu-vid-status", paths.CpuVidTelemetryStatusPath,
                        "--cpu-vid-interval", cpuVidSampleIntervalMs.ToString(CultureInfo.InvariantCulture),
                        "--cpu-vid-provider", cpuVidProvider
                    };
                    if (!string.IsNullOrWhiteSpace(nvidiaSmiPath)) systemArgs.AddRange(new[] { "--nvidia-smi", nvidiaSmiPath });
                    systemSampler = StartNativeMonitorSampler(systemSamplerState, JoinArguments(systemArgs.ToArray()), Root);
                }
                else
                {
                    File.WriteAllText(paths.SlowSamplerLogPath, "FrameScopeSystemSampler.exe was not found.", Encoding.UTF8);
                }
                childStartStopwatch.Stop();
                WriteVerboseFrameScopeLog(verboseLogs, delegate
                {
                    return "monitor-children-started presentMonPid=" + (presentMon == null ? 0 : presentMon.Id).ToString(CultureInfo.InvariantCulture) +
                        " processSamplerPid=" + (processSampler == null ? 0 : processSampler.Id).ToString(CultureInfo.InvariantCulture) +
                        " systemSamplerPid=" + (systemSampler == null ? 0 : systemSampler.Id).ToString(CultureInfo.InvariantCulture);
                });
                WritePerformanceFrameScopeLog(performanceDiagnosticsLogs, delegate
                {
                    return "monitor-child-start-ms=" + childStartStopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) +
                        " presentMonPid=" + (presentMon == null ? 0 : presentMon.Id).ToString(CultureInfo.InvariantCulture) +
                        " processSamplerPid=" + (processSampler == null ? 0 : processSampler.Id).ToString(CultureInfo.InvariantCulture) +
                        " systemSamplerPid=" + (systemSampler == null ? 0 : systemSampler.Id).ToString(CultureInfo.InvariantCulture);
                });

                var sampleIndex = 0;
                var captureDeadline = captureUntilTargetExit ? DateTime.MaxValue : DateTime.Now.AddSeconds(captureSeconds);
                var lastStatusWrite = DateTime.MinValue;
                var presentMonExitCode = (int?)null;
                var presentMonExitedEarly = false;
                var presentMonForcedStop = false;
                var presentMonStopRequested = false;
                var captureLoopStopwatch = Stopwatch.StartNew();
                var selectedPidExited = false;
                var anyAliasRunning = true;
                long aliasQuiescenceStartedMs = -1;

                while (true)
                {
                    RecordNativeMonitorSamplerExit(processSamplerState, true);
                    RecordNativeMonitorSamplerExit(systemSamplerState, true);
                    var selectedPidExitedAtIterationStart = selectedPidExited;
                    var deadlineReached = DateTime.Now >= captureDeadline;
                    if (captureUntilTargetExit && selectedPidExited)
                    {
                        anyAliasRunning = IsAnyTargetProcessRunning(targetProcessBases);
                        aliasQuiescenceStartedMs = FrameScopeTargetLifecycle.UpdateQuiescenceStartMilliseconds(
                            aliasQuiescenceStartedMs,
                            anyAliasRunning,
                            captureLoopStopwatch.ElapsedMilliseconds);
                    }
                    var aliasQuiescenceConfirmed = FrameScopeTargetLifecycle.IsQuiescenceConfirmed(
                        aliasQuiescenceStartedMs,
                        captureLoopStopwatch.ElapsedMilliseconds,
                        FrameScopeTargetLifecycle.DefaultAliasQuiescenceMilliseconds);
                    var effectiveAnyAliasRunning = anyAliasRunning || (captureUntilTargetExit && selectedPidExited && !aliasQuiescenceConfirmed);
                    if (FrameScopeTargetLifecycle.ShouldStopCapture(captureUntilTargetExit, selectedPidExited, effectiveAnyAliasRunning, deadlineReached)) break;

                    if (presentMon != null)
                    {
                        try
                        {
                            if (presentMon.HasExited && !presentMonExitCode.HasValue)
                            {
                                presentMonExitCode = presentMon.ExitCode;
                                presentMonExitedEarly = true;
                                presentMonExitedAt = DateTime.Now;
                                presentMonRuntimeMs = presentMonRuntimeStopwatch == null ? (long?)null : presentMonRuntimeStopwatch.ElapsedMilliseconds;
                                targetRunningAtPresentMonExitCheck = IsAnyTargetProcessRunning(targetProcessBases);
                                targetPidRunningAtPresentMonExitCheck = IsTargetPidRunning(targetProc.Id, targetProcessBases);
                            }
                        }
                        catch { }
                    }

                    var now = DateTime.Now;
                    if ((now - lastStatusWrite).TotalSeconds >= 10)
                    {
                        var capturingStatus = new Dictionary<string, object>
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
                        };
                        AddDictionary(capturingStatus, BuildNativeMonitorSamplerDiagnostics(processSamplerState, systemSamplerState));
                        AddDictionary(capturingStatus, presentMonPreflight);
                        WriteNativeMonitorStatus(paths, "capturing", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, capturingStatus);
                        lastStatusWrite = now;
                    }

                    sampleIndex++;
                    var remainingMs = controlPollIntervalMs;
                    if (!captureUntilTargetExit)
                    {
                        remainingMs = Math.Max(1, Math.Min(controlPollIntervalMs, (int)Math.Max(1, (captureDeadline - DateTime.Now).TotalMilliseconds)));
                    }

                    if (!selectedPidExited)
                    {
                        try
                        {
                            selectedPidExited = targetProc.WaitForExit(remainingMs);
                        }
                        catch
                        {
                            selectedPidExited = ProcessExited(targetProc);
                            if (!selectedPidExited) Thread.Sleep(remainingMs);
                        }
                    }

                    deadlineReached = DateTime.Now >= captureDeadline;
                    if (captureUntilTargetExit && selectedPidExited && !selectedPidExitedAtIterationStart)
                    {
                        anyAliasRunning = IsAnyTargetProcessRunning(targetProcessBases);
                        aliasQuiescenceStartedMs = FrameScopeTargetLifecycle.UpdateQuiescenceStartMilliseconds(
                            aliasQuiescenceStartedMs,
                            anyAliasRunning,
                            captureLoopStopwatch.ElapsedMilliseconds);
                    }
                    aliasQuiescenceConfirmed = FrameScopeTargetLifecycle.IsQuiescenceConfirmed(
                        aliasQuiescenceStartedMs,
                        captureLoopStopwatch.ElapsedMilliseconds,
                        FrameScopeTargetLifecycle.DefaultAliasQuiescenceMilliseconds);
                    effectiveAnyAliasRunning = anyAliasRunning || (captureUntilTargetExit && selectedPidExited && !aliasQuiescenceConfirmed);
                    if (FrameScopeTargetLifecycle.ShouldStopCapture(captureUntilTargetExit, selectedPidExited, effectiveAnyAliasRunning, deadlineReached)) break;

                    if (selectedPidExited)
                    {
                        var aliasWaitMs = captureUntilTargetExit
                            ? Math.Min(remainingMs, FrameScopeTargetLifecycle.AliasProbeIntervalMilliseconds)
                            : remainingMs;
                        if (!captureUntilTargetExit)
                        {
                            aliasWaitMs = Math.Max(1, Math.Min(controlPollIntervalMs, (int)Math.Max(1, (captureDeadline - DateTime.Now).TotalMilliseconds)));
                        }
                        Thread.Sleep(aliasWaitMs);
                    }
                }
                captureLoopStopwatch.Stop();
                WritePerformanceFrameScopeLog(performanceDiagnosticsLogs, delegate
                {
                    return "monitor-capture-loop-ms=" + captureLoopStopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) +
                        " samples=" + sampleIndex.ToString(CultureInfo.InvariantCulture) +
                        " timed=" + (!captureUntilTargetExit);
                });

                RecordNativeMonitorSamplerExit(processSamplerState, true);
                RecordNativeMonitorSamplerExit(systemSamplerState, true);
                bool samplerStopRequested = false;
                DateTime? samplerStopRequestedAt = null;
                try
                {
                    samplerStopRequestedAt = DateTime.Now;
                    File.WriteAllText(paths.SamplerStopPath, samplerStopRequestedAt.Value.ToString("o", CultureInfo.InvariantCulture), Encoding.UTF8);
                    samplerStopRequested = true;
                }
                catch { samplerStopRequestedAt = null; }
                processSamplerState.StopRequested = samplerStopRequested;
                processSamplerState.StopRequestedAt = samplerStopRequestedAt;
                systemSamplerState.StopRequested = samplerStopRequested;
                systemSamplerState.StopRequestedAt = samplerStopRequestedAt;
                StopNativeMonitorSampler(processSamplerState, 5000);
                StopNativeMonitorSampler(systemSamplerState, 5000);
                try { if (File.Exists(paths.SamplerStopPath)) File.Delete(paths.SamplerStopPath); }
                catch { }

                if (presentMon != null && !ProcessExited(presentMon))
                {
                    targetRunningAtPresentMonExitCheck = IsAnyTargetProcessRunning(targetProcessBases);
                    targetPidRunningAtPresentMonExitCheck = IsTargetPidRunning(targetProc.Id, targetProcessBases);
                    presentMonStopRequested = RequestPresentMonStopWithTargetedFallback(presentMonPath, presentMonSessionName);
                    if (!WaitForNativeMonitorChildExit(presentMon, 15000, 15000))
                    {
                        presentMonForcedStop = true;
                        StopMonitorChild(presentMon, 0, true);
                    }
                }
                else if (presentMon != null)
                {
                    if (!targetRunningAtPresentMonExitCheck.HasValue)
                    {
                        targetRunningAtPresentMonExitCheck = IsAnyTargetProcessRunning(targetProcessBases);
                        targetPidRunningAtPresentMonExitCheck = IsTargetPidRunning(targetProc.Id, targetProcessBases);
                    }
                    WaitForNativeMonitorChildOutput(presentMon, 5000);
                }
                if (presentMon != null && !presentMonExitCode.HasValue)
                {
                    try { presentMonExitCode = presentMon.ExitCode; }
                    catch { }
                }
                if (presentMon != null && !presentMonExitedAt.HasValue && ProcessExited(presentMon))
                {
                    presentMonExitedAt = DateTime.Now;
                }
                if (presentMon != null && !presentMonRuntimeMs.HasValue && presentMonRuntimeStopwatch != null)
                {
                    presentMonRuntimeMs = presentMonRuntimeStopwatch.ElapsedMilliseconds;
                }
                if (!presentMonExitCode.HasValue && File.Exists(paths.PresentMonCsv)) presentMonExitCode = 0;
                CleanupOwnedPresentMonSessionIfPresent(presentMonPath, presentMonSessionName);
                long presentMonCsvPostExitWaitMs = WaitForPresentMonCsvFlush(paths.PresentMonCsv, 2000);

                var endTime = DateTime.Now;
                var reportHtml = Path.Combine(paths.RunDir, "charts", "framescope-interactive-report.html");
                var presentMonDiagnosticContext = new FrameScopePresentMonCaptureDiagnosticContext
                {
                    TargetProcessName = presentMonProcessName,
                    TargetResolvedProcess = presentMonProcessName,
                    TargetPid = targetProc.Id,
                    PresentMonArgs = presentMonArguments,
                    PresentMonRuntimeMs = presentMonRuntimeMs,
                    PresentMonStartedAt = presentMonStartedAt.HasValue ? presentMonStartedAt.Value.ToString("o", CultureInfo.InvariantCulture) : "",
                    PresentMonExitedAt = presentMonExitedAt.HasValue ? presentMonExitedAt.Value.ToString("o", CultureInfo.InvariantCulture) : "",
                    TargetRunningAtPresentMonExitCheck = targetRunningAtPresentMonExitCheck,
                    TargetPidRunningAtPresentMonExitCheck = targetPidRunningAtPresentMonExitCheck,
                    PresentMonCsvPostExitWaitMs = presentMonCsvPostExitWaitMs
                };
                var captureDiagnostics = BuildPresentMonCaptureDiagnostics(paths, presentMonExitCode, presentMonExitedEarly, presentMonForcedStop, presentMonDiagnosticContext);
                captureDiagnostics["TargetDisplayName"] = targetDisplayLabel;
                AddDictionary(captureDiagnostics, BuildNativeMonitorSamplerDiagnostics(processSamplerState, systemSamplerState));
                AddDictionary(captureDiagnostics, BuildCpuCoreTelemetryDiagnostics(paths, enableCpuCoreTelemetry, enableCpuVoltageTelemetry, enableCpuVidTelemetry));
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
                    { "InitialTargetPid", initialTargetPid },
                    { "CpuCoreTelemetryEnabled", enableCpuCoreTelemetry },
                    { "CpuCoreSampleIntervalMs", cpuCoreSampleIntervalMs },
                    { "CpuVoltageTelemetryEnabled", enableCpuVoltageTelemetry },
                    { "CpuVoltageSampleIntervalMs", cpuVoltageSampleIntervalMs },
                    { "CpuVoltageProvider", cpuVoltageProvider },
                    { "CpuVidTelemetryEnabled", enableCpuVidTelemetry },
                    { "CpuVidSampleIntervalMs", cpuVidSampleIntervalMs },
                    { "CpuVidProvider", cpuVidProvider }
                };
                AddDictionary(finalizing, captureDiagnostics);
                AddDictionary(finalizing, presentMonPreflight);
                WriteNativeMonitorStatus(paths, "finalizing", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, finalizing);

                WriteEventCsvHeader(paths.EventsCsv);
                AddDictionary(captureDiagnostics, presentMonPreflight);
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
                    { "InitialTargetPid", initialTargetPid },
                    { "CpuCoreTelemetryEnabled", enableCpuCoreTelemetry },
                    { "CpuCoreSampleIntervalMs", cpuCoreSampleIntervalMs },
                    { "CpuVoltageTelemetryEnabled", enableCpuVoltageTelemetry },
                    { "CpuVoltageSampleIntervalMs", cpuVoltageSampleIntervalMs },
                    { "CpuVoltageProvider", cpuVoltageProvider },
                    { "CpuVidTelemetryEnabled", enableCpuVidTelemetry },
                    { "CpuVidSampleIntervalMs", cpuVidSampleIntervalMs },
                    { "CpuVidProvider", cpuVidProvider }
                };
                AddDictionary(done, captureDiagnostics);
                AddDictionary(done, presentMonPreflight);
                WriteNativeMonitorStatus(paths, "done", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, done);
                sessionStopwatch.Stop();
                WritePerformanceFrameScopeLog(performanceDiagnosticsLogs, delegate
                {
                    return "monitor-session-total-ms=" + sessionStopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) +
                        " samples=" + sampleIndex.ToString(CultureInfo.InvariantCulture) +
                        " exit=" + (presentMonExitCode.HasValue ? presentMonExitCode.Value.ToString(CultureInfo.InvariantCulture) : "");
                });
            }

            return 0;
        }
        catch (Exception ex)
        {
            RecordNativeMonitorSamplerExit(processSamplerState, true);
            RecordNativeMonitorSamplerExit(systemSamplerState, true);
            StopNativeMonitorSampler(processSamplerState, 0);
            StopNativeMonitorSampler(systemSamplerState, 0);
            StopMonitorChild(presentMon, 0, true);

            if (paths != null)
            {
                try { File.WriteAllText(paths.ErrorPath, ex.ToString(), Encoding.UTF8); }
                catch { }
                try
                {
                    var errorStatus = new Dictionary<string, object>
                    {
                        { "Error", ex.ToString() },
                        { "ErrorPath", paths.ErrorPath },
                        { "MonitorMode", "native-csharp" }
                    };
                    AddDictionary(errorStatus, BuildNativeMonitorSamplerDiagnostics(processSamplerState, systemSamplerState));
                    AddDictionary(errorStatus, presentMonPreflight);
                    WriteNativeMonitorStatus(paths, "error", "", "unknown", 100, 100, 1000, 3000, "", "", "", errorStatus);
                }
                catch { }
            }

            try { sessionStopwatch.Stop(); } catch { }
            WritePerformanceFrameScopeLog(performanceDiagnosticsLogs, delegate { return "monitor-session-error-ms=" + sessionStopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " error=" + ex.GetType().Name; });
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
