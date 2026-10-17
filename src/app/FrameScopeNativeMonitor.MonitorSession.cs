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
        Process runtimeBackup = null;
        string runtimeBackupSession = null;
        string runtimeBackupExecutable = null;
        Process processSampler = null;
        Process systemSampler = null;
        NativeMonitorSamplerState processSamplerState = null;
        NativeMonitorSamplerState systemSamplerState = null;
        MonitorSessionPaths paths = null;
        FrameScopeLiveFrameStats liveFrameStats = null;
        bool liveStatsCompleted = false;
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
            var controlPollIntervalMs = ParseIntArgument(args, "--ControlPollIntervalMs", 1000);
            var targetProcessName = GetArgValue(args, "--TargetProcessName", "cs2.exe");
            var runRoot = GetArgValue(args, "--RunRoot", DefaultDataRoot);
            var runNamePrefix = GetArgValue(args, "--RunNamePrefix", GetTargetBaseName(targetProcessName));
            var requestedRunDirectory = GetArgValue(args, "--RunDirectory", "");
            var stopRequestToken = GetArgValue(args, "--StopRequestToken", "");
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

            var prefix = SafeName(runNamePrefix, targetBaseName);
            string runDirectory;
            if (string.IsNullOrWhiteSpace(requestedRunDirectory))
            {
                runDirectory = FrameScopeTargetLifecycle.ReserveUniqueRunDirectory(
                    runRoot,
                    prefix,
                    DateTime.Now,
                    Process.GetCurrentProcess().Id,
                    Guid.NewGuid().ToString("N"));
            }
            else
            {
                runDirectory = Path.GetFullPath(requestedRunDirectory);
                string fullRunRoot = Path.GetFullPath(runRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!runDirectory.StartsWith(fullRunRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Reserved run directory is outside the configured run root.");
                if (!Directory.Exists(runDirectory) || !File.Exists(Path.Combine(runDirectory, ".framescope-run-owner")))
                    throw new InvalidOperationException("Reserved run directory is missing its ownership marker.");
            }
            paths = CreateMonitorSessionPaths(runDirectory);
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

            if (IsMonitorStopRequested(paths.RunDir, stopRequestToken))
            {
                createdStatus["StoppedBeforeCapture"] = true;
                WriteNativeMonitorStatus(paths, "done", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, createdStatus);
                return 0;
            }
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
            if (IsMonitorStopRequested(paths.RunDir, stopRequestToken))
            {
                DisposeProcess(targetProc);
                waitingStatus["StoppedBeforeCapture"] = true;
                WriteNativeMonitorStatus(paths, "done", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, waitingStatus);
                return 0;
            }
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
                    captureSeconds, true);
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
                    paths.PresentMonCsv,
                    paths.PresentMonStderr,
                    ProcessPriorityClass.BelowNormal, true);
                if (presentMon != null)
                {
                    presentMonStartedAt = DateTime.Now;
                    presentMonRuntimeStopwatch = Stopwatch.StartNew();
                }
                // Keep application submissions from the beginning, so recovery
                // does not lose the first seconds while full display tracing waits.
                string runtimeBackupCsv = Path.Combine(paths.RunDir, "presentmon-runtime.csv");
                string runtimeBackupError = Path.Combine(paths.RunDir, "presentmon-runtime.stderr.log");
                runtimeBackupSession = FrameScopePresentMonSessionPolicy.CreateSessionName(prefix,
                    Process.GetCurrentProcess().Id, Guid.NewGuid().ToString("N"));
                runtimeBackupExecutable = presentMonPath;
                var runtimePlan = FrameScopeCapturePlanner.CreatePresentMonPlan(targetProcessBases,
                    targetProcessName, targetDisplayLabel, targetProc.Id, runtimeBackupCsv,
                    runtimeBackupSession, !captureUntilTargetExit, captureSeconds, true, true);
                string runtimeArguments = JoinArguments(runtimePlan.Arguments);
                try {
                    runtimeBackup = StartNativeMonitorChild(presentMonPath, runtimeArguments, Root,
                        runtimeBackupCsv, runtimeBackupError, ProcessPriorityClass.BelowNormal, true);
                } catch (Exception backupError) {
                    WriteFrameScopeLog("presentmon-runtime-backup-start-failed " + backupError.Message);
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
                // The overlay's own snapshot stream: one hardware-samples.csv row
                // per second so the report shows exactly what the user saw.
                var hardwareRecorder = new FrameScopeHardwareSampleRecorder(paths.HardwareSamplesCsv);
                liveFrameStats = new FrameScopeLiveFrameStats(paths.PresentMonCsv, true);
                var backupStats = new FrameScopeLiveFrameStats(runtimeBackupCsv, true);
                int runtimeRetries = 0;
                long runtimeLastStartMs = 0;
                DateTime runtimeStartedAt = DateTime.Now;
                var runtimeStopwatch = Stopwatch.StartNew();
                bool runtimeSelected = false;
                presentMonPreflight["PresentMonProfile"] = "full";
                Action<bool> restartRuntime = delegate(bool selected) {
                    var previous = selected ? presentMon : runtimeBackup;
                    string csv = selected ? paths.PresentMonCsv : runtimeBackupCsv;
                    string session = selected ? presentMonSessionName : runtimeBackupSession;
                    if (!ProcessExited(previous)) RequestPresentMonStopWithTargetedFallback(presentMonPath, session);
                    StopMonitorChild(previous, 3000, true);
                    if (!ProcessExited(previous)) throw new IOException("Runtime PresentMon writer did not stop for retry.");
                    WaitForNativeMonitorChildOutput(previous, 5000);
                    CleanupOwnedPresentMonSessionIfPresent(presentMonPath, session);
                    DisposeNativeMonitorChild(previous);
                    runtimeRetries++;
                    string retryError = Path.Combine(paths.RunDir, "presentmon-runtime-retry-" + runtimeRetries + ".stderr.log");
                    session = FrameScopePresentMonSessionPolicy.CreateSessionName(prefix,
                        Process.GetCurrentProcess().Id, Guid.NewGuid().ToString("N"));
                    int remainingSeconds = captureUntilTargetExit ? 0 : Math.Max(1, (int)Math.Ceiling((captureDeadline - DateTime.Now).TotalSeconds));
                    runtimePlan = FrameScopeCapturePlanner.CreatePresentMonPlan(targetProcessBases,
                        targetProcessName, targetDisplayLabel, targetProc.Id, csv,
                        session, !captureUntilTargetExit, remainingSeconds, true, true);
                    runtimeArguments = JoinArguments(runtimePlan.Arguments);
                    // Same v1 schema, append after the old writer drains. Never
                    // truncate frames already recorded for the final report.
                    var replacement = StartNativeMonitorChild(presentMonPath, runtimeArguments, Root,
                        csv, retryError, ProcessPriorityClass.BelowNormal, true, true);
                    runtimeLastStartMs = captureLoopStopwatch.ElapsedMilliseconds;
                    runtimeStartedAt = DateTime.Now;
                    runtimeStopwatch = Stopwatch.StartNew();
                    if (selected) {
                        presentMon = replacement;
                        presentMonSessionName = session;
                        presentMonArguments = runtimeArguments;
                        presentMonExitCode = null;
                        presentMonExitedEarly = false;
                        presentMonExitedAt = null;
                        presentMonRuntimeMs = null;
                        presentMonStartedAt = runtimeStartedAt;
                        presentMonRuntimeStopwatch = runtimeStopwatch;
                    } else {
                        runtimeBackup = replacement;
                        runtimeBackupSession = session;
                    }
                    runtimeBackupError = retryError;
                    presentMonPreflight["PresentMonRuntimeRetries"] = runtimeRetries;
                    presentMonPreflight["PresentMonRuntimeStderr"] = retryError;
                    WriteFrameScopeLog("presentmon-runtime-retry attempt=" + runtimeRetries + " selected=" + selected);
                };
                Action<bool> selectCapture = delegate(bool final) {
                    if (!final && runtimeRetries < 2) {
                        bool exited = runtimeSelected ? ProcessExited(presentMon) : runtimeBackup == null || ProcessExited(runtimeBackup);
                        bool empty = !runtimeSelected && !backupStats.HasFrames && !liveFrameStats.HasFrames;
                        long delay = exited ? 3000 : 10000;
                        if ((exited || empty) && captureLoopStopwatch.ElapsedMilliseconds - runtimeLastStartMs >= delay
                            && IsAnyTargetProcessRunning(targetProcessBases)) {
                            restartRuntime(runtimeSelected);
                            return;
                        }
                    }
                    if (runtimeBackup == null) return;
                    // Observe the backup continuously, including while full
                    // tracing works, so freshness never comes from old file rows.
                    var backup = backupStats.Poll(final);
                    if (final ? liveFrameStats.HasFrames : !FrameScopeCapturePlanner.ShouldRecoverStalledCapture(
                        captureLoopStopwatch.ElapsedMilliseconds, liveFrameStats.SourceAgeMilliseconds, runtimeSelected)) return;
                    if (backup.Error != null || !backup.CaughtUp || !backupStats.HasFrames) return;
                    if (!final && (backup.Count == 0 || !backupStats.SourceAgeMilliseconds.HasValue
                        || backupStats.SourceAgeMilliseconds.Value >= 2000 || ProcessExited(runtimeBackup))) return;
                    string recoveryReason = liveFrameStats.HasFrames ? "full-trace-stalled" : "full-trace-produced-no-frames";
                    // Stop/drain the old writer before renaming. The backup
                    // writer explicitly shares DELETE and keeps its file handle.
                    if (!ProcessExited(presentMon)) RequestPresentMonStopWithTargetedFallback(presentMonPath, presentMonSessionName);
                    StopMonitorChild(presentMon, 3000, true);
                    if (!ProcessExited(presentMon)) throw new IOException("Full PresentMon writer did not stop for recovery.");
                    WaitForNativeMonitorChildOutput(presentMon, 5000);
                    CleanupOwnedPresentMonSessionIfPresent(presentMonPath, presentMonSessionName);
                    presentMonPreflight["PresentMonFullTraceExitCode"] = presentMon.ExitCode;
                    presentMonPreflight["PresentMonFullTraceArgs"] = presentMonArguments;
                    if (File.Exists(paths.PresentMonCsv)) File.Move(paths.PresentMonCsv, Path.Combine(paths.RunDir, "presentmon-full-attempt.csv"));
                    if (File.Exists(paths.PresentMonStderr)) File.Move(paths.PresentMonStderr, Path.Combine(paths.RunDir, "presentmon-full-attempt.stderr.log"));
                    File.Move(runtimeBackupCsv, paths.PresentMonCsv);
                    DisposeNativeMonitorChild(presentMon);
                    presentMon = runtimeBackup;
                    runtimeBackup = null;
                    presentMonSessionName = runtimeBackupSession;
                    // The redirected error writer retains its original path.
                    presentMonPreflight["PresentMonRuntimeStderr"] = runtimeBackupError;
                    presentMonArguments = runtimeArguments;
                    presentMonExitCode = null;
                    presentMonExitedEarly = false;
                    presentMonExitedAt = null;
                    presentMonRuntimeMs = null;
                    presentMonStartedAt = runtimeStartedAt;
                    presentMonRuntimeStopwatch = runtimeStopwatch;
                    runtimeSelected = true;
                    presentMonPreflight["PresentMonProfile"] = "application-present-v1";
                    presentMonPreflight["PresentMonRecoveryReason"] = recoveryReason;
                    presentMonPreflight["PresentMonDisplayTimingAvailable"] = false;
                    backupStats.Relocate(paths.PresentMonCsv);
                    liveFrameStats = backupStats;
                    WriteFrameScopeLog("presentmon-recovered-application-submissions session=" + presentMonSessionName);
                };
                var lastLiveStats = DateTime.MinValue;
                hardwareRecorder.Start();

                while (true)
                {
                    // Leave through the existing sampler/PresentMon flush path,
                    // never terminate the worker to acknowledge a UI stop.
                    if (IsMonitorStopRequested(paths.RunDir, stopRequestToken)) break;
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
                    // Read between publications too, so a bounded parser slice
                    // can catch up without waiting another full second.
                    liveFrameStats.ReadAvailable();
                    if ((now - lastLiveStats).TotalSeconds >= 1) {
                        selectCapture(false);
                        liveFrameStats.Publish(paths.RunDir, Process.GetCurrentProcess().Id,
                            presentMonExitedEarly || File.Exists(paths.PresentMonCsv + ".capture-error.txt") ? "unavailable" : "capturing", false);
                        lastLiveStats = now;
                    }
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
                            { "PresentMonArgs", presentMonArguments },
                            { "HardwareSampleRows", hardwareRecorder.RowsWritten },
                            { "HardwareSampleLastError", hardwareRecorder.LastError ?? "" }
                        };
                        AddDictionary(capturingStatus, BuildNativeMonitorSamplerDiagnostics(processSamplerState, systemSamplerState, false));
                        AddDictionary(capturingStatus, presentMonPreflight);
                        WriteNativeMonitorStatus(paths, "capturing", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, capturingStatus);
                        lastStatusWrite = now;
                    }

                    sampleIndex++;
                    // Live FPS/Low expires after 3s; publication must not wait
                    // for a configured 3s control interval plus its I/O jitter.
                    var remainingMs = Math.Min(controlPollIntervalMs, 1000);
                    if (!captureUntilTargetExit)
                    {
                        remainingMs = Math.Max(1, Math.Min(remainingMs, (int)Math.Max(1, (captureDeadline - DateTime.Now).TotalMilliseconds)));
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
                            aliasWaitMs = Math.Max(1, Math.Min(remainingMs, (int)Math.Max(1, (captureDeadline - DateTime.Now).TotalMilliseconds)));
                        }
                        Thread.Sleep(aliasWaitMs);
                    }
                }
                captureLoopStopwatch.Stop();
                hardwareRecorder.Stop();
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

                liveFrameStats.Poll(false);
                selectCapture(true);
                if (presentMon != null && !ProcessExited(presentMon))
                {
                    targetRunningAtPresentMonExitCheck = IsAnyTargetProcessRunning(targetProcessBases);
                    targetPidRunningAtPresentMonExitCheck = IsTargetPidRunning(targetProc.Id, targetProcessBases);
                    presentMonStopRequested = RequestPresentMonStopWithTargetedFallback(presentMonPath, presentMonSessionName);
                    if (!WaitForNativeMonitorChildExit(presentMon, 15000, 15000))
                    {
                        presentMonForcedStop = true;
                        StopMonitorChild(presentMon, 0, true);
            // R-B8: the recorder runs on a background thread - stop it here
            // too, instead of relying on process exit.
            FrameScopeHardwareSampleRecorder.StopActive();
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
                if (runtimeSelected && File.Exists(runtimeBackupError))
                    File.Copy(runtimeBackupError, paths.PresentMonStderr, true);
                if (!presentMonExitCode.HasValue && File.Exists(paths.PresentMonCsv)) presentMonExitCode = 0;
                CleanupOwnedPresentMonSessionIfPresent(presentMonPath, presentMonSessionName);
                long presentMonCsvPostExitWaitMs = WaitForPresentMonCsvFlush(paths.PresentMonCsv, 2000);
                liveFrameStats.Publish(paths.RunDir, Process.GetCurrentProcess().Id, "completed", true);
                liveStatsCompleted = true;

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
                // status.json evidence for the hardware sample stream: a zero
                // row count with a healthy pipeline means the recorder died
                // silently, which previously could only be guessed from the
                // report's missing GPU overlay columns. Merged AFTER the done
                // write - the finalizing/done passes rewrite the whole file.
                try
                {
                    FrameScopeNativeMonitor.WriteHardwareSampleEvidence(
                        paths.StatusPath,
                        hardwareRecorder.RowsWritten,
                        hardwareRecorder.LastError);
                }
                catch { }
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
            if (runtimeBackup != null) {
                if (!ProcessExited(runtimeBackup)) RequestPresentMonStopWithTargetedFallback(runtimeBackupExecutable, runtimeBackupSession);
                StopMonitorChild(runtimeBackup, 3000, true);
                DisposeNativeMonitorChild(runtimeBackup);
            }
            if (!liveStatsCompleted && liveFrameStats != null && paths != null) {
                try { liveFrameStats.Publish(paths.RunDir, Process.GetCurrentProcess().Id, "unavailable", false); }
                catch (IOException) { }
            }
            DisposeNativeMonitorChild(processSampler);
            DisposeNativeMonitorChild(systemSampler);
            DisposeNativeMonitorChild(presentMon);
        }
    }
}
