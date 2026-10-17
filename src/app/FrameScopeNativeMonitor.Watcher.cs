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

internal sealed class ActiveMonitor
{
    public FrameScopeTarget Target;
    public List<string> TargetAliases;
    public Process Process;
    public int MonitorPid;
    public string RunRoot;
    public string RunDir;
    public DateTime StartedAt;
    public string StopToken;
    public bool StopSignalled;
}

internal static partial class FrameScopeNativeMonitor
{
    private static readonly FrameScopeLogRateLimiter VerboseWatcherLogRateLimiter = new FrameScopeLogRateLimiter();
    private static readonly FrameScopeLogRateLimiter PerformanceWatcherLogRateLimiter = new FrameScopeLogRateLimiter();
    private static readonly TimeSpan WatcherTargetScanLogInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan WatcherPerformancePollLogInterval = TimeSpan.FromSeconds(15);
    private static string WatcherInstanceId;
    private static long WatcherStartTimeUnix;
    private static bool WatcherStopRequested;
    private static string WatcherStopError = "";
    private static string WatcherStopErrorCode = "";

    private static void RecordWatcherStopError(string code, string error)
    {
        if (code == "report-finalization-failed" && !string.IsNullOrEmpty(WatcherStopErrorCode)) return;
        WatcherStopErrorCode = code;
        WatcherStopError = error;
    }

    private static bool PollWatcherStop(string configPath)
    {
        if (WatcherStopRequested) return true;
        try
        {
            var path = Path.Combine(Root, "framescope-watcher-stop-" + WatcherInstanceId + ".json");
            if (!File.Exists(path)) return false;
            var request = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            WatcherStopRequested = Convert.ToString(request["Action"]) == "stop" &&
                Convert.ToString(request["WatcherInstanceId"]) == WatcherInstanceId &&
                Convert.ToInt32(request["WatcherPid"]) == Process.GetCurrentProcess().Id &&
                Convert.ToInt64(request["WatcherStartTimeUnix"]) == WatcherStartTimeUnix &&
                string.Equals(Path.GetFullPath(Convert.ToString(request["ConfigPath"])),
                    Path.GetFullPath(configPath), StringComparison.OrdinalIgnoreCase);
        }
        catch { }
        return WatcherStopRequested;
    }

    private static void SignalMonitorStops(Dictionary<string, ActiveMonitor> activeMonitors)
    {
        foreach (var item in activeMonitors.Values)
        {
            if (item.StopSignalled || string.IsNullOrEmpty(item.StopToken)) continue;
            try
            {
                FrameScopeJsonFile.Write(Path.Combine(item.RunDir, "monitor-stop-request.json"),
                    Json.Serialize(new { Action = "stop", WorkerToken = item.StopToken }));
                item.StopSignalled = true;
            }
            catch (Exception ex)
            {
                WriteFrameScopeLog("worker-stop-request-retry pid=" + item.MonitorPid + " error=" + ex.Message);
            }
        }
    }

    private static bool IsMonitorStopRequested(string runDir, string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            string path = Path.Combine(runDir, "monitor-stop-request.json");
            if (!File.Exists(path)) return false;
            var request = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            return Convert.ToString(request["Action"]) == "stop" &&
                Convert.ToString(request["WorkerToken"]) == token;
        }
        catch { return false; }
    }

    private static void RunNativeWatcher(string[] args)
    {
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch { }

        var configPath = GetArgValue(args, "--config", ConfigPath);
        var diagnosticOutputRoot = GetArgValue(args, "--diagnostic-output-root", null);
        WatcherInstanceId = Guid.NewGuid().ToString("N");
        WatcherStartTimeUnix = (Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks -
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) / TimeSpan.TicksPerSecond;
        WatcherStopRequested = false;
        WatcherStopError = "";
        WatcherStopErrorCode = "";
        var exitAfterFirstRun = HasArg(args, "--exit-after-first-run");
        var activeMonitors = new Dictionary<string, ActiveMonitor>(StringComparer.OrdinalIgnoreCase);
        var retryPolicy = new FrameScopeWorkerRestartPolicy();
        var completedRuns = 0;
        var lastReport = "";
        var lastStateWrite = DateTime.MinValue;
        var lastStateSignature = "";
        var recoveredStaleRuns = false;
        var lastRetentionCleanup = DateTime.MinValue;

        WriteFrameScopeLog("native-watcher-start config=" + configPath);
        WriteNativeWatcherState(configPath, "starting", activeMonitors, completedRuns, lastReport);

        while (true)
        {
            var pollStarted = Stopwatch.StartNew();
            if (PollWatcherStop(configPath))
            {
                WriteNativeWatcherState(configPath, "stopping", activeMonitors, completedRuns, lastReport);
                SignalMonitorStops(activeMonitors);
            }
            var config = LoadConfigFromPath(configPath);
            var dataRoot = ResolveDataRoot(config.DataRoot);
            Directory.CreateDirectory(dataRoot);
            if (!recoveredStaleRuns && !WatcherStopRequested)
            {
                RecoverStaleMissingReports(dataRoot, config, delegate(string runDir) {
                    WriteNativeWatcherState(configPath, runDir == null ? "starting" : "generating", activeMonitors, completedRuns, lastReport, runDir);
                });
                recoveredStaleRuns = true;
            }
            if (!WatcherStopRequested && (DateTime.Now - lastRetentionCleanup).TotalHours >= 6)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        FrameScopeDiagnostics.ApplyRetentionPolicy(Root, config, diagnosticOutputRoot);
                        ApplyRunAndHistoryRetention(dataRoot, config);
                    }
                    catch { }
                });
                lastRetentionCleanup = DateTime.Now;
            }

            // R-B3: a worker dying on startup would be re-spawned every poll
            // tick; back off after three rapid failures.
            foreach (var key in activeMonitors.Keys.ToArray())
            {
                var item = activeMonitors[key];
                if (!MonitorHasExited(item))
                {
                    if ((DateTime.Now - item.StartedAt).TotalSeconds >= 5) retryPolicy.Reset(key);
                    continue;
                }
                retryPolicy.RecordExit(key, DateTime.Now - item.StartedAt, DateTime.UtcNow);
                if (!retryPolicy.CanStart(key, DateTime.UtcNow)) WriteFrameScopeLog("watcher-backoff target=" + key);

                var run = !string.IsNullOrWhiteSpace(item.RunDir) && Directory.Exists(item.RunDir)
                    ? new DirectoryInfo(item.RunDir)
                    : null;
                var status = run != null ? ReadStatusDictionary(run.FullName) : null;
                var exitCode = GetMonitorExitCode(item, status);
                if (status != null && status.ContainsKey("StoppedBeforeCapture") &&
                    Convert.ToBoolean(status["StoppedBeforeCapture"]))
                {
                    DisposeProcess(item.Process);
                    activeMonitors.Remove(key);
                    continue;
                }
                if (run != null)
                {
                    if (PollWatcherStop(configPath)) SignalMonitorStops(activeMonitors);
                    WriteNativeWatcherState(configPath, "generating", activeMonitors, completedRuns, lastReport, run.FullName);
                    status = EnsureReportForCompletedRun(run.FullName, status, exitCode, config);
                    var entry = AddHistoryEntry(item.Target, run.FullName, status, exitCode);
                    lastReport = entry.ReportHtml ?? "";
                    FrameScopeReportArtifactState reportArtifacts = FrameScopeReportArtifacts.Inspect(run.FullName);
                    bool reportAvailable = reportArtifacts.IsComplete;
                    if (!reportAvailable)
                    {
                        RecordWatcherStopError("report-finalization-failed", "Report finalization failed for " + item.Target.Name);
                    }
                    if (reportAvailable) lastReport = reportArtifacts.HtmlPath;
                    WriteFrameScopeLog("monitor-complete game=" + item.Target.Name + " report=" + lastReport);
                    if (config.AutoGenerateDiagnosticReport && FrameScopeLoggingPolicy.ShouldAutoGenerateDiagnosticReport(status, exitCode))
                    {
                        FrameScopeDiagnostics.QueueGenerateReport(config, Root, dataRoot, "auto-monitor-complete", run.FullName, diagnosticOutputRoot);
                        WriteFrameScopeLog("diagnostic-report-queued run=" + run.FullName);
                    }
                    else if (config.AutoGenerateDiagnosticReport)
                    {
                        WriteVerboseFrameScopeLog(config, delegate { return "diagnostic-report-skip healthy-run=" + run.FullName; });
                    }
                    if (ShouldOpenReport(item.Target, config) && reportAvailable && ShouldAutoOpenCompletedReport(status))
                    {
                        if (TryOpenReport(lastReport, run.FullName))
                        {
                            MarkReportOpened(run.FullName, status);
                        }
                    }
                    else if (ShouldOpenReport(item.Target, config) && reportAvailable && !ShouldAutoOpenCompletedReport(status))
                    {
                        WriteFrameScopeLog("report-open-skip diagnostic-or-error report=" + lastReport + " error=" + StatusString(status, "ReportError", ""));
                    }
                    else if (ShouldOpenReport(item.Target, config))
                    {
                        WriteFrameScopeLog("report-open-skip missing=" + lastReport);
                    }
                }
                else
                {
                    RecordWatcherStopError("worker-run-missing", "Worker exited without a run directory: " + item.Target.Name);
                    WriteFrameScopeLog("monitor-complete-no-run game=" + item.Target.Name);
                }

                DisposeProcess(item.Process);
                activeMonitors.Remove(key);
                completedRuns++;
            }
            foreach (var target in config.Targets.Where(t => t != null && t.Enabled && !string.IsNullOrWhiteSpace(t.ProcessName)))
            {
                if (PollWatcherStop(configPath))
                {
                    SignalMonitorStops(activeMonitors);
                    break;
                }
                var processBases = BuildTargetProcessBaseNames(target.ProcessName, target.Name);
                if (processBases.Count == 0) continue;
                var processBase = processBases[0];
                var normalizedAliases = FrameScopeTargetLifecycle.NormalizeBaseNames(processBases);
                var key = FrameScopeTargetLifecycle.CanonicalBaseNameTargetKey(normalizedAliases);
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (activeMonitors.ContainsKey(key)) continue;
                if (!FrameScopeTargetLifecycle.CanClaimAliases(
                    normalizedAliases,
                    activeMonitors.Values.Select(item => (IEnumerable<string>)item.TargetAliases))) continue;
                var detected = FindBestTargetProcess(processBases, 0);
                var detectedText = detected == null ? "none" : detected.BaseName + ":" + detected.ProcessId.ToString(CultureInfo.InvariantCulture);
                WriteRateLimitedVerboseFrameScopeLog(
                    config,
                    "target-scan:" + key,
                    detectedText,
                    WatcherTargetScanLogInterval,
                    delegate { return "target-scan name=" + target.Name + " aliases=" + string.Join(";", processBases.ToArray()) + " detected=" + detectedText; });
                if (detected == null) { retryPolicy.Reset(key); continue; }
                if (!retryPolicy.CanStart(key, DateTime.UtcNow)) continue;

                var runRoot = Path.Combine(dataRoot, SafeName(target.Name, processBase));
                Directory.CreateDirectory(runRoot);
                var monitor = StartMonitorProcess(config, target, runRoot, processBases, detected);
                if (monitor == null)
                {
                    retryPolicy.RecordExit(key, TimeSpan.Zero, DateTime.UtcNow);
                    continue;
                }

                activeMonitors[key] = monitor;
                WriteFrameScopeLog("monitor-start game=" + target.Name + " process=" + target.ProcessName + " targetPid=" + detected.ProcessId + " targetBase=" + detected.BaseName + " pid=" + monitor.MonitorPid);
            }

            if (WatcherStopRequested && activeMonitors.Count == 0)
            {
                WriteNativeWatcherState(configPath, "stopped", activeMonitors, completedRuns, lastReport);
                WriteFrameScopeLog("native-watcher-graceful-stop-complete");
                break;
            }
            var phase = WatcherStopRequested ? "stopping" : activeMonitors.Count > 0 ? "monitoring" : "idle";
            var stateSignature = phase + "|" + completedRuns + "|" + lastReport + "|" + string.Join(",", activeMonitors.Keys.OrderBy(v => v).ToArray());
            var stateIntervalMs = activeMonitors.Count > 0 ? 2000 : 5000;
            var now = DateTime.Now;
            if (stateSignature != lastStateSignature || (now - lastStateWrite).TotalMilliseconds >= stateIntervalMs)
            {
                WriteNativeWatcherState(configPath, phase, activeMonitors, completedRuns, lastReport);
                lastStateWrite = now;
                lastStateSignature = stateSignature;
            }

            if (exitAfterFirstRun && completedRuns >= 1 && activeMonitors.Count == 0)
            {
                WriteFrameScopeLog("native-watcher-exit-after-first-run");
                break;
            }

            pollStarted.Stop();
            long watcherPollMs = pollStarted.ElapsedMilliseconds;
            // The poll interval is a real user setting now (clamped by
            // ConfigStore.Normalize); the old hardcoded internal constant
            // made the "目标扫描间隔" setting a decoration.
            int pollIntervalMs = config.PollIntervalMs;
            if (pollIntervalMs <= 0) pollIntervalMs = FrameScopeConfigStore.InternalPollIntervalMs;
            string watcherPollState = "active=" + activeMonitors.Count.ToString(CultureInfo.InvariantCulture) +
                "|completed=" + completedRuns.ToString(CultureInfo.InvariantCulture) +
                "|slow=" + (watcherPollMs >= 100 ? "1" : "0");
            WriteRateLimitedPerformanceFrameScopeLog(
                config,
                "watcher-poll",
                watcherPollState,
                WatcherPerformancePollLogInterval,
                delegate
                {
                    return "watcher-poll-ms=" + watcherPollMs.ToString(CultureInfo.InvariantCulture) +
                        " interval=" + pollIntervalMs.ToString(CultureInfo.InvariantCulture) +
                        " active=" + activeMonitors.Count.ToString(CultureInfo.InvariantCulture) +
                        " completed=" + completedRuns.ToString(CultureInfo.InvariantCulture);
                });
            System.Threading.Thread.Sleep(pollIntervalMs);
        }
    }

    private static bool IsProcessRunning(string processBaseName)
    {
        return FindBestTargetProcess(new List<string> { processBaseName }, 0) != null;
    }

    private static bool IsAnyTargetProcessRunning(List<string> processBaseNames)
    {
        return FindBestTargetProcess(processBaseNames, 0) != null;
    }

    private static ActiveMonitor StartMonitorProcess(FrameScopeConfig config, FrameScopeTarget target, string runRoot, List<string> processBaseNames, TargetProcessSnapshot detected)
    {
        var telemetrySampleMs = config != null && config.TelemetrySampleIntervalMs > 0
            ? config.TelemetrySampleIntervalMs
            : FrameScopeConfigStore.DefaultTelemetrySampleIntervalMs;
        if (telemetrySampleMs < FrameScopeConfigStore.TelemetrySampleIntervalMinMs) telemetrySampleMs = FrameScopeConfigStore.TelemetrySampleIntervalMinMs;
        if (telemetrySampleMs > FrameScopeConfigStore.TelemetrySampleIntervalMaxMs) telemetrySampleMs = FrameScopeConfigStore.TelemetrySampleIntervalMaxMs;
        var sampleMs = telemetrySampleMs;
        var processSampleMs = telemetrySampleMs;
        var slowSampleMs = telemetrySampleMs;
        var cpuCoreTelemetry = config == null ? null : config.CpuTelemetry;
        var enableCpuCoreTelemetry = cpuCoreTelemetry != null && cpuCoreTelemetry.CollectPerCoreFrequency;
        var enableCpuVoltageTelemetry = true;
        var enableCpuVidTelemetry = true;
        var cpuCoreSampleMs = telemetrySampleMs;
        var cpuVoltageSampleMs = telemetrySampleMs;
        var cpuVoltageProvider = "auto";
        var cpuVidSampleMs = telemetrySampleMs;
        var cpuVidProvider = "auto";
        var safeName = SafeName(target.Name, processBaseNames.Count > 0 ? processBaseNames[0] : GetTargetBaseName(target.ProcessName));
        string reservedRunDir;
        try
        {
            reservedRunDir = FrameScopeTargetLifecycle.ReserveUniqueRunDirectory(
                runRoot,
                safeName,
                DateTime.Now,
                Process.GetCurrentProcess().Id,
                Guid.NewGuid().ToString("N"));
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("native-monitor-run-reservation-failed game=" + target.Name + " error=" + ex.Message);
            return null;
        }
        var aliasArg = string.Join(";", processBaseNames.ToArray());
        var stopToken = Guid.NewGuid().ToString("N");
        var args =
            "--monitor-session" +
            " --TargetProcessName " + Quote(target.ProcessName) +
            " --TargetProcessAliases " + Quote(aliasArg) +
            " --TargetDisplayName " + Quote(target.Name) +
            " --InitialTargetPid " + (detected == null ? "0" : detected.ProcessId.ToString(CultureInfo.InvariantCulture)) +
            " --WaitSeconds 15 --CaptureSeconds 0" +
            " --SampleIntervalMs " + sampleMs.ToString(CultureInfo.InvariantCulture) +
            " --ProcessSampleIntervalMs " + processSampleMs.ToString(CultureInfo.InvariantCulture) +
            " --SlowSampleIntervalMs " + slowSampleMs.ToString(CultureInfo.InvariantCulture) +
            " --EnableCpuCoreTelemetry " + (enableCpuCoreTelemetry ? "true" : "false") +
            " --CpuCoreSampleIntervalMs " + cpuCoreSampleMs.ToString(CultureInfo.InvariantCulture) +
            " --EnableCpuVoltageTelemetry " + (enableCpuVoltageTelemetry ? "true" : "false") +
            " --CpuVoltageSampleIntervalMs " + cpuVoltageSampleMs.ToString(CultureInfo.InvariantCulture) +
            " --CpuVoltageProvider " + Quote(cpuVoltageProvider) +
            " --EnableCpuVidTelemetry " + (enableCpuVidTelemetry ? "true" : "false") +
            " --CpuVidSampleIntervalMs " + cpuVidSampleMs.ToString(CultureInfo.InvariantCulture) +
            " --CpuVidProvider " + Quote(cpuVidProvider) +
            " --MonitorProcessRole monitor-session-worker" +
            " --StopRequestToken " + stopToken +
            " --EnableVerboseLogs " + (FrameScopeLoggingPolicy.IsVerboseEnabled(config) ? "true" : "false") +
            " --EnablePerformanceDiagnosticsLogs " + (FrameScopeLoggingPolicy.IsPerformanceDiagnosticsEnabled(config) ? "true" : "false") +
            " --ControlPollIntervalMs 3000" +
            " --RunRoot " + Quote(runRoot) +
            " --RunNamePrefix " + Quote(safeName) +
            " --RunDirectory " + Quote(reservedRunDir);

        WriteVerboseFrameScopeLog(config, delegate
        {
            return "monitor-process-args target=" + target.Name +
                " telemetrySampleMs=" + telemetrySampleMs.ToString(CultureInfo.InvariantCulture) +
                " sampleMs=" + sampleMs.ToString(CultureInfo.InvariantCulture) +
                " processSampleMs=" + processSampleMs.ToString(CultureInfo.InvariantCulture) +
                " slowSampleMs=" + slowSampleMs.ToString(CultureInfo.InvariantCulture) +
                " cpuCore=" + enableCpuCoreTelemetry +
                " cpuVoltage=" + enableCpuVoltageTelemetry +
                " cpuVid=" + enableCpuVidTelemetry;
        });

        try
        {
            if (WatcherStopRequested) return null;
            var psi = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = args,
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var process = Process.Start(psi);
            if (process == null) return null;
            try { process.PriorityClass = ProcessPriorityClass.Idle; }
            catch { }
            WriteFrameScopeLog("monitor-worker-start role=monitor-session-worker process=FrameScopeMonitor.exe game=" + target.Name + " pid=" + process.Id.ToString(CultureInfo.InvariantCulture));
            return new ActiveMonitor
            {
                Target = target,
                TargetAliases = FrameScopeTargetLifecycle.NormalizeBaseNames(processBaseNames),
                Process = process,
                MonitorPid = process.Id,
                RunRoot = runRoot,
                RunDir = reservedRunDir,
                StopToken = stopToken,
                StartedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(reservedRunDir)) Directory.Delete(reservedRunDir, true); }
            catch { }
            WriteFrameScopeLog("native-monitor-start-failed game=" + target.Name + " error=" + ex.Message);
            return null;
        }
    }

    private static bool MonitorHasExited(ActiveMonitor item)
    {
        if (item == null) return true;
        try
        {
            if (item.Process != null)
            {
                item.Process.Refresh();
                return item.Process.HasExited;
            }
        }
        catch { }

        try
        {
            using (Process.GetProcessById(item.MonitorPid)) { return false; }
        }
        catch
        {
            return true;
        }
    }

    private static int GetMonitorExitCode(ActiveMonitor item, Dictionary<string, object> status)
    {
        try
        {
            if (item != null && item.Process != null) return item.Process.ExitCode;
        }
        catch { }

        object value;
        if (status != null && status.TryGetValue("ExitCode", out value))
        {
            try { return Convert.ToInt32(value); }
            catch { }
        }
        return -1;
    }

    private static void WriteNativeWatcherState(string configPath, string phase, Dictionary<string, ActiveMonitor> activeMonitors, int completedRuns, string lastReport, string generatingRunDir = null)
    {
        var active = activeMonitors.Select(entry => new
        {
            Key = entry.Key,
            Aliases = entry.Value.TargetAliases,
            Game = entry.Value.Target.Name,
            ProcessName = entry.Value.Target.ProcessName,
            WorkerRole = "monitor-session-worker",
            WorkerProcessName = "FrameScopeMonitor.exe",
            WorkerExplanation = "任务管理器中可能显示一个 FrameScopeMonitor.exe 子进程，这是监控 worker，不是重复打开软件。",
            MonitorPid = MonitorHasExited(entry.Value) ? (int?)null : entry.Value.MonitorPid,
            RunRoot = entry.Value.RunRoot,
            RunDir = entry.Value.RunDir
        }).ToArray();

        var state = new
        {
            Time = DateTime.Now.ToString("o"),
            Phase = phase,
            GeneratingRunDir = generatingRunDir,
            ConfigPath = configPath,
            WatcherPid = Process.GetCurrentProcess().Id,
            WatcherStartTimeUnix = WatcherStartTimeUnix,
            WatcherInstanceId = WatcherInstanceId,
            StopProtocolVersion = 1,
            StopRequested = WatcherStopRequested,
            StopError = WatcherStopError,
            StopErrorCode = WatcherStopErrorCode,
            WatcherMode = "native",
            CompletedRuns = completedRuns,
            LastReport = lastReport,
            HistoryPath = FrameScopeNativeMonitor.HistoryPath,
            LogPath = Path.Combine(Root, "framescope-watcher.log"),
            ActiveMonitors = active
        };
        FrameScopeJsonFile.WriteTransient(StatePath, Json.Serialize(state));
    }

    private static void WriteFrameScopeLog(string message)
    {
        FrameScopeDiagnostics.AppendLogAsync(Path.Combine(Root, "framescope-watcher.log"), message);
    }

    private static void WriteVerboseFrameScopeLog(FrameScopeConfig config, string message)
    {
        WriteVerboseFrameScopeLog(FrameScopeLoggingPolicy.IsVerboseEnabled(config), message);
    }

    private static void WriteVerboseFrameScopeLog(FrameScopeConfig config, Func<string> messageFactory)
    {
        WriteVerboseFrameScopeLog(FrameScopeLoggingPolicy.IsVerboseEnabled(config), messageFactory);
    }

    private static void WriteVerboseFrameScopeLog(bool enabled, string message)
    {
        if (enabled) WriteFrameScopeLog("verbose " + message);
    }

    private static void WriteVerboseFrameScopeLog(bool enabled, Func<string> messageFactory)
    {
        if (!enabled || messageFactory == null) return;
        WriteFrameScopeLog("verbose " + (messageFactory() ?? ""));
    }

    private static void WriteRateLimitedVerboseFrameScopeLog(FrameScopeConfig config, string key, string state, TimeSpan minInterval, Func<string> messageFactory)
    {
        if (!FrameScopeLoggingPolicy.IsVerboseEnabled(config)) return;
        if (!VerboseWatcherLogRateLimiter.ShouldWrite(key, state, DateTime.UtcNow, minInterval)) return;
        WriteVerboseFrameScopeLog(true, messageFactory);
    }

    private static void WritePerformanceFrameScopeLog(FrameScopeConfig config, string message)
    {
        WritePerformanceFrameScopeLog(FrameScopeLoggingPolicy.IsPerformanceDiagnosticsEnabled(config), message);
    }

    private static void WritePerformanceFrameScopeLog(FrameScopeConfig config, Func<string> messageFactory)
    {
        WritePerformanceFrameScopeLog(FrameScopeLoggingPolicy.IsPerformanceDiagnosticsEnabled(config), messageFactory);
    }

    private static void WritePerformanceFrameScopeLog(bool enabled, string message)
    {
        if (enabled) WriteFrameScopeLog("perf " + message);
    }

    private static void WritePerformanceFrameScopeLog(bool enabled, Func<string> messageFactory)
    {
        if (!enabled || messageFactory == null) return;
        WriteFrameScopeLog("perf " + (messageFactory() ?? ""));
    }

    private static void WriteRateLimitedPerformanceFrameScopeLog(FrameScopeConfig config, string key, string state, TimeSpan minInterval, Func<string> messageFactory)
    {
        if (!FrameScopeLoggingPolicy.IsPerformanceDiagnosticsEnabled(config)) return;
        if (!PerformanceWatcherLogRateLimiter.ShouldWrite(key, state, DateTime.UtcNow, minInterval)) return;
        WritePerformanceFrameScopeLog(true, messageFactory);
    }
}
