using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public sealed class FrameScopeConfig
{
    public FrameScopeConfig()
    {
        PollIntervalMs = 1000;
        DataRoot = "";
        OpenReportOnComplete = true;
        MonitorScript = "";
        Targets = new List<FrameScopeTarget>();
    }

    public int PollIntervalMs { get; set; }
    public string DataRoot { get; set; }
    public bool OpenReportOnComplete { get; set; }
    public string MonitorScript { get; set; }
    public List<FrameScopeTarget> Targets { get; set; }
}

public sealed class FrameScopeTarget
{
    public FrameScopeTarget()
    {
        Name = "";
        ProcessName = "";
        SampleIntervalMs = 100;
        ProcessSampleIntervalMs = 100;
        SlowSampleIntervalMs = 1000;
        OpenReportOnComplete = true;
    }

    public bool Enabled { get; set; }
    public string Name { get; set; }
    public string ProcessName { get; set; }
    public int SampleIntervalMs { get; set; }
    public int ProcessSampleIntervalMs { get; set; }
    public int SlowSampleIntervalMs { get; set; }
    public bool OpenReportOnComplete { get; set; }
}

public sealed class FrameScopeHistoryEntry
{
    public FrameScopeHistoryEntry()
    {
        Time = "";
        Game = "";
        ProcessName = "";
        RunDir = "";
        ReportHtml = "";
        PresentMonCsv = "";
        ProcessCsv = "";
        SystemCsv = "";
        SummaryPath = "";
        MonitorExitCode = 0;
    }

    public string Time { get; set; }
    public string Game { get; set; }
    public string ProcessName { get; set; }
    public string RunDir { get; set; }
    public string ReportHtml { get; set; }
    public string PresentMonCsv { get; set; }
    public string ProcessCsv { get; set; }
    public string SystemCsv { get; set; }
    public string SummaryPath { get; set; }
    public int MonitorExitCode { get; set; }
}

internal sealed class ActiveMonitor
{
    public FrameScopeTarget Target;
    public Process Process;
    public int MonitorPid;
    public string RunRoot;
    public DateTime StartedAt;
}

internal sealed class ReportGenerationResult
{
    public bool Attempted;
    public int ExitCode;
    public string ReportHtml;
    public string LogPath;
    public string Error;
    public int FrameCount;
    public bool HasFrameData;
    public string ReportKind;
}

internal sealed class MonitorSessionPaths
{
    public string RunDir;
    public string StatusPath;
    public string PresentMonCsv;
    public string PresentMonStdout;
    public string PresentMonStderr;
    public string PresentMonInfoPath;
    public string SamplesCsv;
    public string ProcessCsv;
    public string TopCpuCsv;
    public string TopIoCsv;
    public string AlertsCsv;
    public string EventsCsv;
    public string SummaryPath;
    public string ReportLogPath;
    public string SlowSamplerLogPath;
    public string ErrorPath;
}

internal static class FrameScopeNativeMonitor
{
    private static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string ConfigPath = Path.Combine(Root, "framescope-config.json");
    private const string NativeMonitorMode = "native-csharp";
    private static readonly string LegacyMonitorScript = Path.Combine(Root, "Monitor-CS2-HighFreq.ps1");
    private static readonly string ReportGeneratorExe = Path.Combine(Root, "FrameScopeReportGenerator.exe");
    private static readonly string StatePath = Path.Combine(Root, "framescope-watcher-state.json");
    private static readonly string HistoryPath = Path.Combine(Root, "framescope-history.jsonl");
    private static readonly string DefaultDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrameScopeMonitorData",
        "framescope-runs");
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    private static Form form;
    private static DataGridView grid;
    private static ComboBox processCombo;
    private static TextBox dataRootText;
    private static CheckBox autoOpenCheck;
    private static Label statusLabel;
    private static Button startButton;
    private static System.Windows.Forms.Timer statusTimer;
    private static bool pulse;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args != null && args.Any(a => a.Equals("--monitor-session", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RunNativeMonitorSession(args));
            return;
        }

        if (args != null && args.Any(a => a.Equals("--watcher", StringComparison.OrdinalIgnoreCase)))
        {
            RunNativeWatcher(args);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var config = LoadConfig();
        BuildUi(config);
        RefreshProcessList();
        Application.Run(form);
    }

    private static FrameScopeConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            var created = CreateDefaultConfig();
            SaveConfig(created);
            return created;
        }

        try
        {
            var config = Json.Deserialize<FrameScopeConfig>(File.ReadAllText(ConfigPath));
            NormalizeConfig(config);
            return config;
        }
        catch
        {
            return CreateDefaultConfig();
        }
    }

    private static FrameScopeConfig CreateDefaultConfig()
    {
        var config = new FrameScopeConfig
        {
            PollIntervalMs = 1000,
            DataRoot = DefaultDataRoot,
            OpenReportOnComplete = true,
            MonitorScript = NativeMonitorMode,
            Targets = new List<FrameScopeTarget>
            {
                Target(true, "Counter-Strike 2", "cs2.exe"),
                Target(true, "PUBG: BATTLEGROUNDS", "TslGame.exe"),
                Target(true, "Delta Force", "DeltaForceClient-Win64-Shipping.exe"),
                Target(true, "Neverness To Everness", "HTGame.exe"),
                Target(false, "Valorant", "VALORANT-Win64-Shipping.exe"),
                Target(false, "Cyberpunk 2077", "Cyberpunk2077.exe"),
                Target(false, "Battlefield 6", "bf6.exe"),
                Target(false, "Hogwarts Legacy", "HogwartsLegacy.exe"),
                Target(false, "OPUS Prism Peak", "OPUS_ Prism Peak.exe")
            }
        };
        return config;
    }

    private static FrameScopeTarget Target(bool enabled, string name, string processName)
    {
        return new FrameScopeTarget
        {
            Enabled = enabled,
            Name = name,
            ProcessName = processName,
            SampleIntervalMs = 100,
            ProcessSampleIntervalMs = 100,
            SlowSampleIntervalMs = 1000,
            OpenReportOnComplete = true
        };
    }

    private static void NormalizeConfig(FrameScopeConfig config)
    {
        if (config == null) throw new InvalidOperationException("FrameScope config is empty.");
        if (config.Targets == null) config.Targets = new List<FrameScopeTarget>();
        if (string.IsNullOrWhiteSpace(config.DataRoot)) config.DataRoot = DefaultDataRoot;
        if (string.IsNullOrWhiteSpace(config.MonitorScript) ||
            config.MonitorScript.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ||
            config.MonitorScript.Equals(LegacyMonitorScript, StringComparison.OrdinalIgnoreCase))
        {
            config.MonitorScript = NativeMonitorMode;
        }
        if (config.PollIntervalMs <= 0) config.PollIntervalMs = 1000;
        foreach (var target in config.Targets)
        {
            if (target == null) continue;
            if (target.SampleIntervalMs < 50) target.SampleIntervalMs = 100;
            if (target.ProcessSampleIntervalMs < 100) target.ProcessSampleIntervalMs = 100;
            if (target.SlowSampleIntervalMs < target.SampleIntervalMs) target.SlowSampleIntervalMs = 1000;
        }
    }

    private static void SaveConfig(FrameScopeConfig config)
    {
        NormalizeConfig(config);
        File.WriteAllText(ConfigPath, Json.Serialize(config));
    }

    private static string GetArgValue(string[] args, string name, string fallback)
    {
        if (args == null) return fallback;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return fallback;
    }

    private static bool HasArg(string[] args, string name)
    {
        return args != null && args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static FrameScopeConfig LoadConfigFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = ConfigPath;
        if (!File.Exists(path))
        {
            var created = CreateDefaultConfig();
            SaveConfigToPath(path, created);
            return created;
        }

        try
        {
            var config = Json.Deserialize<FrameScopeConfig>(File.ReadAllText(path));
            NormalizeConfig(config);
            return config;
        }
        catch
        {
            return CreateDefaultConfig();
        }
    }

    private static void SaveConfigToPath(string path, FrameScopeConfig config)
    {
        NormalizeConfig(config);
        File.WriteAllText(path, Json.Serialize(config));
    }

    private static void RunNativeWatcher(string[] args)
    {
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch { }

        var configPath = GetArgValue(args, "--config", ConfigPath);
        var exitAfterFirstRun = HasArg(args, "--exit-after-first-run");
        var activeMonitors = new Dictionary<string, ActiveMonitor>(StringComparer.OrdinalIgnoreCase);
        var completedRuns = 0;
        var lastReport = "";
        var lastStateWrite = DateTime.MinValue;
        var lastStateSignature = "";
        var recoveredStaleRuns = false;

        WriteFrameScopeLog("native-watcher-start config=" + configPath);

        while (true)
        {
            var config = LoadConfigFromPath(configPath);
            var dataRoot = ResolveDataRoot(config.DataRoot);
            Directory.CreateDirectory(dataRoot);
            if (!recoveredStaleRuns)
            {
                RecoverStaleMissingReports(dataRoot);
                recoveredStaleRuns = true;
            }

            foreach (var key in activeMonitors.Keys.ToArray())
            {
                var item = activeMonitors[key];
                if (!MonitorHasExited(item)) continue;

                var run = LatestRunDirectory(item.RunRoot);
                var status = run != null ? ReadStatusDictionary(run.FullName) : null;
                var exitCode = GetMonitorExitCode(item, status);
                if (run != null)
                {
                    status = EnsureReportForCompletedRun(run.FullName, status, exitCode);
                    var entry = AddHistoryEntry(item.Target, run.FullName, status, exitCode);
                    lastReport = entry.ReportHtml ?? "";
                    WriteFrameScopeLog("monitor-complete game=" + item.Target.Name + " report=" + lastReport);
                    if (ShouldOpenReport(item.Target, config) && File.Exists(lastReport) && ShouldAutoOpenCompletedReport(status))
                    {
                        if (TryOpenReport(lastReport, run.FullName))
                        {
                            MarkReportOpened(run.FullName, status);
                        }
                    }
                    else if (ShouldOpenReport(item.Target, config) && File.Exists(lastReport) && !ShouldAutoOpenCompletedReport(status))
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
                    WriteFrameScopeLog("monitor-complete-no-run game=" + item.Target.Name);
                }

                DisposeProcess(item.Process);
                activeMonitors.Remove(key);
                completedRuns++;
            }

            foreach (var target in config.Targets.Where(t => t != null && t.Enabled && !string.IsNullOrWhiteSpace(t.ProcessName)))
            {
                var processBase = GetTargetBaseName(target.ProcessName);
                if (string.IsNullOrWhiteSpace(processBase)) continue;
                var key = processBase.ToLowerInvariant();
                if (activeMonitors.ContainsKey(key)) continue;
                if (!IsProcessRunning(processBase)) continue;

                var runRoot = Path.Combine(dataRoot, SafeName(target.Name, processBase));
                Directory.CreateDirectory(runRoot);
                var monitor = StartMonitorProcess(target, runRoot);
                if (monitor == null) continue;

                activeMonitors[key] = monitor;
                WriteFrameScopeLog("monitor-start game=" + target.Name + " process=" + target.ProcessName + " pid=" + monitor.MonitorPid);
            }

            var phase = activeMonitors.Count > 0 ? "monitoring" : "idle";
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

            var pollMs = config.PollIntervalMs > 0 ? config.PollIntervalMs : 1000;
            if (pollMs < 500) pollMs = 500;
            System.Threading.Thread.Sleep(pollMs);
        }
    }

    private static void RecoverStaleMissingReports(string dataRoot)
    {
        try
        {
            var root = new DirectoryInfo(dataRoot);
            if (!root.Exists) return;
            var candidates = new List<DirectoryInfo>();
            foreach (var gameDir in root.GetDirectories())
            {
                candidates.AddRange(gameDir.GetDirectories()
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .Take(3)
                    .Where(run =>
                        !File.Exists(Path.Combine(run.FullName, "charts", "framescope-interactive-report.html")) &&
                        HasAnyMonitorCsv(run.FullName) &&
                        DateTime.Now - LatestMonitorCsvWriteTime(run.FullName) > TimeSpan.FromMinutes(2)));
            }

            foreach (var run in candidates.OrderByDescending(d => d.LastWriteTimeUtc).Take(5))
            {
                var status = ReadStatusDictionary(run.FullName);
                var phase = StatusString(status, "Phase", "");
                if (phase.Equals("capturing", StringComparison.OrdinalIgnoreCase) ||
                    phase.Equals("finalizing", StringComparison.OrdinalIgnoreCase) ||
                    phase.Equals("error", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(phase))
                {
                    WriteFrameScopeLog("recover-stale-report run=" + run.FullName + " phase=" + phase);
                    EnsureReportForCompletedRun(run.FullName, status, StatusInt(status, "ExitCode", -1));
                }
            }
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("recover-stale-report-error " + ex.Message);
        }
    }

    private static Dictionary<string, object> EnsureReportForCompletedRun(string runDir, Dictionary<string, object> status, int monitorExitCode)
    {
        var reportHtml = Path.Combine(runDir, "charts", "framescope-interactive-report.html");
        if (File.Exists(reportHtml))
        {
            return status;
        }

        var presentMonCsv = Path.Combine(runDir, "presentmon.csv");
        if (!File.Exists(presentMonCsv) &&
            !File.Exists(Path.Combine(runDir, "process-samples.csv")) &&
            !File.Exists(Path.Combine(runDir, "system-samples.csv")))
        {
            WriteFrameScopeLog("report-generate-skip missing-monitor-data run=" + runDir);
            return UpdateStatusAfterReportGeneration(runDir, status, new ReportGenerationResult
            {
                Attempted = false,
                ExitCode = -1,
                ReportHtml = reportHtml,
                LogPath = Path.Combine(runDir, "report-generation.log"),
                Error = "No monitor CSV data was found."
            }, monitorExitCode);
        }

        if (!File.Exists(presentMonCsv))
        {
            WriteFrameScopeLog("report-generate-partial missing-presentmon run=" + runDir);
        }

        var result = RunReportGeneration(runDir);
        return UpdateStatusAfterReportGeneration(runDir, status, result, monitorExitCode);
    }

    private static bool HasAnyMonitorCsv(string runDir)
    {
        return File.Exists(Path.Combine(runDir, "presentmon.csv")) ||
               File.Exists(Path.Combine(runDir, "process-samples.csv")) ||
               File.Exists(Path.Combine(runDir, "system-samples.csv"));
    }

    private static DateTime LatestMonitorCsvWriteTime(string runDir)
    {
        DateTime latest = DateTime.MinValue;
        foreach (var name in new[] { "presentmon.csv", "process-samples.csv", "system-samples.csv" })
        {
            var path = Path.Combine(runDir, name);
            if (!File.Exists(path)) continue;
            var writeTime = File.GetLastWriteTime(path);
            if (writeTime > latest) latest = writeTime;
        }
        return latest == DateTime.MinValue ? DateTime.Now : latest;
    }

    private static ReportGenerationResult RunReportGeneration(string runDir)
    {
        var result = new ReportGenerationResult
        {
            Attempted = false,
            ExitCode = -1,
            ReportHtml = Path.Combine(runDir, "charts", "framescope-interactive-report.html"),
            LogPath = Path.Combine(runDir, "report-generation.log"),
            Error = null,
            FrameCount = 0,
            HasFrameData = false,
            ReportKind = "unknown"
        };

        if (!File.Exists(ReportGeneratorExe))
        {
            result.Error = "Native report generator not found: " + ReportGeneratorExe;
            WriteReportLog(result.LogPath, result.Error);
            WriteFrameScopeLog("report-generate-failed run=" + runDir + " error=" + result.Error);
            return result;
        }

        result.Attempted = true;
        WriteFrameScopeLog("report-generate-start run=" + runDir);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ReportGeneratorExe,
                Arguments = Quote(runDir),
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(psi))
            {
                if (process == null)
                {
                    result.Error = "Failed to start report generator.";
                    WriteReportLog(result.LogPath, result.Error);
                    return result;
                }
                try { process.PriorityClass = ProcessPriorityClass.AboveNormal; }
                catch { }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                result.ExitCode = process.ExitCode;
                WriteReportLog(result.LogPath, output + (string.IsNullOrWhiteSpace(error) ? "" : Environment.NewLine + error));
                if (result.ExitCode != 0)
                {
                    result.Error = "Report generation failed with exit code " + result.ExitCode + ".";
                    WriteFrameScopeLog("report-generate-failed run=" + runDir + " exit=" + result.ExitCode);
                }
                else if (!File.Exists(result.ReportHtml))
                {
                    result.Error = "Report generator finished but report html was not created.";
                    WriteFrameScopeLog("report-generate-missing-html run=" + runDir);
                }
                else
                {
                    ReadReportManifest(runDir, result);
                    if (!result.HasFrameData)
                    {
                        result.ReportKind = "diagnostic";
                        result.Error = "No frame data was captured by PresentMon; generated diagnostic report from process/system data.";
                        WriteFrameScopeLog("report-generate-diagnostic run=" + runDir + " report=" + result.ReportHtml);
                    }
                    else
                    {
                        result.ReportKind = "full";
                        WriteFrameScopeLog("report-generate-complete run=" + runDir + " report=" + result.ReportHtml + " frames=" + result.FrameCount);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.ExitCode = -1;
            result.Error = ex.Message;
            WriteReportLog(result.LogPath, ex.ToString());
            WriteFrameScopeLog("report-generate-error run=" + runDir + " error=" + ex.Message);
        }

        return result;
    }

    private static void ReadReportManifest(string runDir, ReportGenerationResult result)
    {
        try
        {
            var manifestPath = Path.Combine(runDir, "charts", "framescope-interactive-manifest.json");
            if (!File.Exists(manifestPath)) return;
            var manifest = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifestPath, Encoding.UTF8));
            object framesValue;
            if (manifest != null && manifest.TryGetValue("frames", out framesValue) && framesValue != null)
            {
                result.FrameCount = Convert.ToInt32(framesValue);
                result.HasFrameData = result.FrameCount > 0;
            }
            object kindValue;
            if (manifest != null && manifest.TryGetValue("reportKind", out kindValue) && kindValue != null)
            {
                result.ReportKind = Convert.ToString(kindValue);
            }
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("report-manifest-read-failed run=" + runDir + " error=" + ex.Message);
        }
    }

    private static Dictionary<string, object> UpdateStatusAfterReportGeneration(string runDir, Dictionary<string, object> status, ReportGenerationResult result, int monitorExitCode)
    {
        var statusPath = Path.Combine(runDir, "status.json");
        var map = status != null
            ? new Dictionary<string, object>(status, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        map["Time"] = DateTime.Now.ToString("o");
        map["Phase"] = "done";
        map["ExitCode"] = monitorExitCode;
        map["ReportHtml"] = result.ReportHtml;
        map["ReportLog"] = result.LogPath;
        map["ReportError"] = result.Error;
        map["ReportGeneratedByWatcher"] = true;
        map["ReportGenerationAttempted"] = result.Attempted;
        map["ReportGenerationExitCode"] = result.ExitCode;
        map["ReportFrameCount"] = result.FrameCount;
        map["ReportHasFrameData"] = result.HasFrameData;
        map["ReportKind"] = result.ReportKind;

        try { File.WriteAllText(statusPath, Json.Serialize(map), Encoding.UTF8); }
        catch (Exception ex) { WriteFrameScopeLog("status-update-failed run=" + runDir + " error=" + ex.Message); }
        return map;
    }

    private static void WriteReportLog(string logPath, string text)
    {
        try { File.WriteAllText(logPath, text ?? "", Encoding.UTF8); }
        catch { }
    }

    private static string ResolveDataRoot(string configuredRoot)
    {
        var dataRoot = configuredRoot;
        if (string.IsNullOrWhiteSpace(dataRoot)) dataRoot = DefaultDataRoot;
        if (!Path.IsPathRooted(dataRoot)) dataRoot = Path.Combine(Root, dataRoot);
        return dataRoot;
    }

    private static string GetTargetBaseName(string processName)
    {
        var baseName = Path.GetFileNameWithoutExtension(processName ?? "");
        return string.IsNullOrWhiteSpace(baseName) ? processName : baseName;
    }

    private static string SafeName(string value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('-');
            }
        }
        var safe = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static bool IsProcessRunning(string processBaseName)
    {
        Process[] processes = null;
        try
        {
            processes = Process.GetProcessesByName(processBaseName);
            return processes.Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (processes != null)
            {
                foreach (var process in processes) DisposeProcess(process);
            }
        }
    }

    private static ActiveMonitor StartMonitorProcess(FrameScopeTarget target, string runRoot)
    {
        var sampleMs = target.SampleIntervalMs > 0 ? target.SampleIntervalMs : 100;
        var processSampleMs = target.ProcessSampleIntervalMs >= 100 ? target.ProcessSampleIntervalMs : 100;
        var slowSampleMs = target.SlowSampleIntervalMs >= sampleMs ? target.SlowSampleIntervalMs : 1000;
        var safeName = SafeName(target.Name, GetTargetBaseName(target.ProcessName));
        var args =
            "--monitor-session" +
            " --TargetProcessName " + Quote(target.ProcessName) +
            " --WaitSeconds 15 --CaptureSeconds 0" +
            " --SampleIntervalMs " + sampleMs.ToString(CultureInfo.InvariantCulture) +
            " --ProcessSampleIntervalMs " + processSampleMs.ToString(CultureInfo.InvariantCulture) +
            " --SlowSampleIntervalMs " + slowSampleMs.ToString(CultureInfo.InvariantCulture) +
            " --ControlPollIntervalMs 3000" +
            " --RunRoot " + Quote(runRoot) +
            " --RunNamePrefix " + Quote(safeName);

        try
        {
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
            return new ActiveMonitor
            {
                Target = target,
                Process = process,
                MonitorPid = process.Id,
                RunRoot = runRoot,
                StartedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("native-monitor-start-failed game=" + target.Name + " error=" + ex.Message);
            return null;
        }
    }

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

            if (sampleIntervalMs < 50) sampleIntervalMs = 50;
            if (processSampleIntervalMs < 100) processSampleIntervalMs = 100;
            if (controlPollIntervalMs < 1000) controlPollIntervalMs = 1000;
            if (slowSampleIntervalMs < sampleIntervalMs) slowSampleIntervalMs = sampleIntervalMs;

            var targetBaseName = GetTargetBaseName(targetProcessName);
            if (string.IsNullOrWhiteSpace(targetBaseName)) targetBaseName = "cs2";
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
            var presentMonSessionName = "FrameScopeNativePresentMon_" + SafeName(prefix, targetBaseName) + "_" + stamp;

            var presentMonPath = ResolvePresentMonPath(requestedPresentMon);
            var processSamplerPath = ResolveProcessSamplerPath(requestedProcessSampler);
            var systemSamplerPath = ResolveSystemSamplerPath(requestedSystemSampler);
            var nvidiaSmiPath = ResolveNvidiaSmiPath();
            var captureUntilTargetExit = captureSeconds <= 0;
            var captureMode = captureUntilTargetExit ? "until-target-exit" : "timed";
            var presentMonCaptureMode = "process_name";
            var presentMonCaptureTarget = presentMonProcessName;
            var presentMonArguments = "";

            WriteNativeMonitorStatus(paths, "created", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, new Dictionary<string, object>
            {
                { "PresentMonCaptureMode", presentMonCaptureMode },
                { "PresentMonCaptureTarget", presentMonCaptureTarget },
                { "PresentMonSessionName", presentMonSessionName }
            });

            if (string.IsNullOrWhiteSpace(presentMonPath) || !File.Exists(presentMonPath))
            {
                throw new InvalidOperationException("PresentMon not found. Expected portable copy under tools\\PresentMon-2.4.1-x64.exe or NVIDIA FrameView SDK PresentMon.");
            }
            if (string.IsNullOrWhiteSpace(processSamplerPath) || !File.Exists(processSamplerPath))
            {
                throw new InvalidOperationException("FrameScopeProcessSampler.exe not found beside FrameScopeMonitor.exe.");
            }

            WritePresentMonInfo(paths.PresentMonInfoPath, presentMonPath);

            WriteNativeMonitorStatus(paths, "waiting-for-target", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, new Dictionary<string, object>
            {
                { "WaitSeconds", waitSeconds },
                { "CaptureSeconds", captureSeconds },
                { "CaptureUntilTargetExit", captureUntilTargetExit },
                { "PresentMonCaptureMode", presentMonCaptureMode },
                { "PresentMonCaptureTarget", presentMonCaptureTarget },
                { "PresentMonSessionName", presentMonSessionName }
            });

            var targetProc = WaitForTargetProcess(targetBaseName, waitSeconds);
            if (targetProc == null)
            {
                WriteNativeMonitorStatus(paths, "timeout-waiting-for-target", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, new Dictionary<string, object>());
                return 2;
            }

            using (targetProc)
            {
                var startTime = DateTime.Now;
                var presentMonArgs = new List<string>
                {
                    "--process_name", presentMonCaptureTarget,
                    "--output_file", paths.PresentMonCsv,
                    "--date_time",
                    "--terminate_on_proc_exit",
                    "--no_console_stats",
                    "--stop_existing_session",
                    "--session_name", presentMonSessionName
                };
                if (!captureUntilTargetExit)
                {
                    presentMonArgs.AddRange(new[] { "--timed", captureSeconds.ToString(CultureInfo.InvariantCulture), "--terminate_after_timed" });
                }
                presentMonArguments = JoinArguments(presentMonArgs);

                WriteNativeMonitorStatus(paths, "starting-presentmon", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, new Dictionary<string, object>
                {
                    { "TargetPid", targetProc.Id },
                    { "StartTime", startTime.ToString("o") },
                    { "PresentMonCaptureMode", presentMonCaptureMode },
                    { "PresentMonCaptureTarget", presentMonCaptureTarget },
                    { "PresentMonSessionName", presentMonSessionName },
                    { "PresentMonArgs", presentMonArguments }
                });

                presentMon = StartNativeMonitorChild(
                    presentMonPath,
                    presentMonArguments,
                    Root,
                    paths.PresentMonStdout,
                    paths.PresentMonStderr);

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
                        if (!IsProcessRunning(targetBaseName)) break;
                        Thread.Sleep(remainingMs);
                    }
                }

                StopMonitorChild(processSampler, 5000, true);
                StopMonitorChild(systemSampler, 5000, true);

                if (presentMon != null && !ProcessExited(presentMon))
                {
                    if (!presentMon.WaitForExit(10000))
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

                var endTime = DateTime.Now;
                var reportHtml = Path.Combine(paths.RunDir, "charts", "framescope-interactive-report.html");
                WriteNativeMonitorStatus(paths, "finalizing", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, new Dictionary<string, object>
                {
                    { "TargetPid", targetProc.Id },
                    { "ExitCode", presentMonExitCode },
                    { "SampleCount", sampleIndex },
                    { "PresentMonForcedStop", presentMonForcedStop },
                    { "PresentMonExitedEarly", presentMonExitedEarly },
                    { "EndTime", endTime.ToString("o") },
                    { "SummaryPath", paths.SummaryPath },
                    { "ReportHtml", reportHtml },
                    { "PresentMonCaptureMode", presentMonCaptureMode },
                    { "PresentMonCaptureTarget", presentMonCaptureTarget },
                    { "PresentMonSessionName", presentMonSessionName },
                    { "PresentMonArgs", presentMonArguments }
                });

                WriteEventCsvHeader(paths.EventsCsv);
                WriteNativeMonitorSummary(paths, presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, presentMonExitCode, presentMonExitedEarly, presentMonForcedStop, reportHtml, presentMonCaptureMode, presentMonCaptureTarget, presentMonArguments);

                WriteNativeMonitorStatus(paths, "done", presentMonProcessName, captureMode, sampleIntervalMs, processSampleIntervalMs, slowSampleIntervalMs, controlPollIntervalMs, presentMonPath, processSamplerPath, systemSamplerPath, new Dictionary<string, object>
                {
                    { "TargetPid", targetProc.Id },
                    { "ExitCode", presentMonExitCode },
                    { "SampleCount", sampleIndex },
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
                    { "PresentMonArgs", presentMonArguments }
                });
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

    private static MonitorSessionPaths CreateMonitorSessionPaths(string runDir)
    {
        return new MonitorSessionPaths
        {
            RunDir = runDir,
            StatusPath = Path.Combine(runDir, "status.json"),
            PresentMonCsv = Path.Combine(runDir, "presentmon.csv"),
            PresentMonStdout = Path.Combine(runDir, "presentmon.stdout.log"),
            PresentMonStderr = Path.Combine(runDir, "presentmon.stderr.log"),
            PresentMonInfoPath = Path.Combine(runDir, "presentmon-info.json"),
            SamplesCsv = Path.Combine(runDir, "system-samples.csv"),
            ProcessCsv = Path.Combine(runDir, "process-samples.csv"),
            TopCpuCsv = Path.Combine(runDir, "topcpu-samples.csv"),
            TopIoCsv = Path.Combine(runDir, "topio-samples.csv"),
            AlertsCsv = Path.Combine(runDir, "sample-alerts.csv"),
            EventsCsv = Path.Combine(runDir, "event-samples.csv"),
            SummaryPath = Path.Combine(runDir, "summary.json"),
            ReportLogPath = Path.Combine(runDir, "report-generation.log"),
            SlowSamplerLogPath = Path.Combine(runDir, "system-slow-sampler.log"),
            ErrorPath = Path.Combine(runDir, "monitor-error.txt")
        };
    }

    private static int ParseIntArgument(string[] args, string name, int fallback)
    {
        var text = GetArgValue(args, name, fallback.ToString(CultureInfo.InvariantCulture));
        int value;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
    }

    private static Process WaitForTargetProcess(string processBaseName, int waitSeconds)
    {
        var deadline = DateTime.Now.AddSeconds(waitSeconds);
        while (DateTime.Now < deadline)
        {
            Process[] processes = null;
            try
            {
                processes = Process.GetProcessesByName(processBaseName);
                if (processes.Length > 0)
                {
                    var selected = processes[0];
                    for (var i = 1; i < processes.Length; i++) DisposeProcess(processes[i]);
                    return selected;
                }
            }
            catch { }
            finally
            {
                if (processes != null && processes.Length == 0)
                {
                    foreach (var process in processes) DisposeProcess(process);
                }
            }
            Thread.Sleep(200);
        }
        return null;
    }

    private static string ResolvePresentMonPath(string requestedPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedPath)) candidates.Add(requestedPath);
        candidates.Add(Path.Combine(Root, "tools", "PresentMon-2.4.1-x64.exe"));
        try
        {
            var toolsDir = Path.Combine(Root, "tools");
            if (Directory.Exists(toolsDir))
            {
                candidates.AddRange(Directory.GetFiles(toolsDir, "PresentMon*.exe").OrderBy(path => path));
            }
        }
        catch { }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "FrameViewSDK", "bin", "PresentMon_x64.exe"));
        return FirstExistingPath(candidates);
    }

    private static string ResolveProcessSamplerPath(string requestedPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedPath)) candidates.Add(requestedPath);
        candidates.Add(Path.Combine(Root, "FrameScopeProcessSampler.exe"));
        return FirstExistingPath(candidates);
    }

    private static string ResolveSystemSamplerPath(string requestedPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedPath)) candidates.Add(requestedPath);
        candidates.Add(Path.Combine(Root, "FrameScopeSystemSampler.exe"));
        return FirstExistingPath(candidates);
    }

    private static string ResolveNvidiaSmiPath()
    {
        var known = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        if (File.Exists(known)) return known;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            try
            {
                var candidate = Path.Combine(part.Trim(), "nvidia-smi.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return "";
    }

    private static string FirstExistingPath(IEnumerable<string> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                var full = Path.GetFullPath(candidate);
                if (!seen.Add(full)) continue;
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return "";
    }

    private static Process StartNativeMonitorChild(string fileName, string arguments, string workingDirectory, string stdoutPath = null, string stderrPath = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !string.IsNullOrWhiteSpace(stdoutPath),
            RedirectStandardError = !string.IsNullOrWhiteSpace(stderrPath)
        };
        var process = Process.Start(psi);
        if (process != null)
        {
            try { process.PriorityClass = ProcessPriorityClass.Idle; }
            catch { }
            if (!string.IsNullOrWhiteSpace(stdoutPath)) BeginCopyPipe(process.StandardOutput, stdoutPath);
            if (!string.IsNullOrWhiteSpace(stderrPath)) BeginCopyPipe(process.StandardError, stderrPath);
        }
        return process;
    }

    private static void BeginCopyPipe(StreamReader reader, string path)
    {
        if (reader == null || string.IsNullOrWhiteSpace(path)) return;
        var thread = new Thread(() =>
        {
            try
            {
                var text = reader.ReadToEnd();
                File.WriteAllText(path, text ?? "", Encoding.UTF8);
            }
            catch { }
        });
        thread.IsBackground = true;
        thread.Start();
    }

    private static void StopMonitorChild(Process process, int waitMs, bool force)
    {
        if (process == null) return;
        try
        {
            if (process.HasExited) return;
            if (waitMs > 0 && process.WaitForExit(waitMs)) return;
            if (force && !process.HasExited) process.Kill();
        }
        catch { }
    }

    private static bool ProcessExited(Process process)
    {
        if (process == null) return true;
        try { return process.HasExited; }
        catch { return true; }
    }

    private static string JoinArguments(IEnumerable<string> args)
    {
        return string.Join(" ", args.Select(QuoteCommandArgument).ToArray());
    }

    private static string QuoteCommandArgument(string value)
    {
        if (value == null) value = "";
        if (value.Length == 0) return "\"\"";
        if (value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) < 0) return value;

        var builder = new StringBuilder();
        builder.Append('"');
        var backslashes = 0;
        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }
            if (ch == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }
            if (backslashes > 0)
            {
                builder.Append('\\', backslashes);
                backslashes = 0;
            }
            builder.Append(ch);
        }
        if (backslashes > 0) builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
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

    private static void WriteNativeMonitorStatus(MonitorSessionPaths paths, string phase, string targetProcess, string captureMode, int sampleIntervalMs, int processSampleIntervalMs, int slowSampleIntervalMs, int controlPollIntervalMs, string presentMonPath, string processSamplerPath, string systemSamplerPath, Dictionary<string, object> extra)
    {
        var status = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            { "Time", DateTime.Now.ToString("o") },
            { "Phase", phase },
            { "RunDir", paths.RunDir },
            { "MonitorScript", NativeMonitorMode },
            { "MonitorMode", NativeMonitorMode },
            { "MonitorPid", Process.GetCurrentProcess().Id },
            { "PresentMonCsv", paths.PresentMonCsv },
            { "PresentMonExe", presentMonPath },
            { "PresentMonOut", paths.PresentMonStdout },
            { "PresentMonErr", paths.PresentMonStderr },
            { "PresentMonInfo", paths.PresentMonInfoPath },
            { "SamplesCsv", paths.SamplesCsv },
            { "ProcessCsv", paths.ProcessCsv },
            { "TopCpuCsv", paths.TopCpuCsv },
            { "TopIoCsv", paths.TopIoCsv },
            { "AlertsCsv", paths.AlertsCsv },
            { "EventsCsv", paths.EventsCsv },
            { "SummaryPath", paths.SummaryPath },
            { "ReportLog", paths.ReportLogPath },
            { "SlowSamplerLog", paths.SlowSamplerLogPath },
            { "TargetProcess", targetProcess },
            { "CaptureMode", captureMode },
            { "SampleIntervalMs", sampleIntervalMs },
            { "ProcessSampleIntervalMs", processSampleIntervalMs },
            { "ControlPollIntervalMs", controlPollIntervalMs },
            { "SlowSampleIntervalMs", slowSampleIntervalMs },
            { "ProcessSamplingMode", "native-all-process-groups" },
            { "ProcessSamplerExe", processSamplerPath },
            { "SystemSamplingMode", string.IsNullOrWhiteSpace(systemSamplerPath) ? "native-system-missing" : "native-system-slow" },
            { "SystemSamplerExe", systemSamplerPath }
        };

        if (extra != null)
        {
            foreach (var pair in extra) status[pair.Key] = pair.Value;
        }

        File.WriteAllText(paths.StatusPath, Json.Serialize(status), Encoding.UTF8);
    }

    private static void WriteNativeMonitorSummary(MonitorSessionPaths paths, string targetProcess, string captureMode, int sampleIntervalMs, int processSampleIntervalMs, int slowSampleIntervalMs, int controlPollIntervalMs, string presentMonPath, int? presentMonExitCode, bool presentMonExitedEarly, bool presentMonForcedStop, string reportHtml, string presentMonCaptureMode, string presentMonCaptureTarget, string presentMonArguments)
    {
        var reports = new Dictionary<string, object>
        {
            { "Attempted", false },
            { "ExitCode", null },
            { "ReportHtml", reportHtml },
            { "PreviewPng", null },
            { "LogPath", paths.ReportLogPath },
            { "Error", null }
        };
        var summary = new Dictionary<string, object>
        {
            { "RunDir", paths.RunDir },
            { "MonitorScript", NativeMonitorMode },
            { "MonitorMode", NativeMonitorMode },
            { "PresentMonCsv", paths.PresentMonCsv },
            { "PresentMonExe", presentMonPath },
            { "PresentMonStdout", paths.PresentMonStdout },
            { "PresentMonStderr", paths.PresentMonStderr },
            { "PresentMonInfo", paths.PresentMonInfoPath },
            { "PresentMonCaptureMode", presentMonCaptureMode },
            { "PresentMonCaptureTarget", presentMonCaptureTarget },
            { "PresentMonArgs", presentMonArguments },
            { "PresentMonExitCode", presentMonExitCode },
            { "PresentMonExitedEarly", presentMonExitedEarly },
            { "PresentMonForcedStop", presentMonForcedStop },
            { "SamplesCsv", paths.SamplesCsv },
            { "ProcessCsv", paths.ProcessCsv },
            { "TopCpuCsv", paths.TopCpuCsv },
            { "TopIoCsv", paths.TopIoCsv },
            { "AlertsCsv", paths.AlertsCsv },
            { "EventsCsv", paths.EventsCsv },
            { "TargetProcess", targetProcess },
            { "CaptureMode", captureMode },
            { "SampleIntervalMs", sampleIntervalMs },
            { "ProcessSampleIntervalMs", processSampleIntervalMs },
            { "ControlPollIntervalMs", controlPollIntervalMs },
            { "SlowSampleIntervalMs", slowSampleIntervalMs },
            { "Reports", reports },
            { "Notes", new[] { "Monitor session was captured by FrameScopeMonitor.exe native C# mode. Report generation is handled by the native watcher after capture." } }
        };
        File.WriteAllText(paths.SummaryPath, Json.Serialize(summary), Encoding.UTF8);
    }

    private static void WriteEventCsvHeader(string path)
    {
        try
        {
            File.WriteAllText(path, "TimeCreated,ProviderName,Id,LevelDisplayName,Message\r\n", new UTF8Encoding(false));
        }
        catch { }
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

    private static DirectoryInfo LatestRunDirectory(string runRoot)
    {
        try
        {
            if (!Directory.Exists(runRoot)) return null;
            return new DirectoryInfo(runRoot).GetDirectories()
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object> ReadStatusDictionary(string runDir)
    {
        try
        {
            var path = Path.Combine(runDir, "status.json");
            if (!File.Exists(path)) return null;
            return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static string StatusString(Dictionary<string, object> status, string key, string fallback)
    {
        object value;
        if (status != null && status.TryGetValue(key, out value) && value != null)
        {
            return Convert.ToString(value);
        }
        return fallback;
    }

    private static int StatusInt(Dictionary<string, object> status, string key, int fallback)
    {
        object value;
        if (status != null && status.TryGetValue(key, out value) && value != null)
        {
            try { return Convert.ToInt32(value); }
            catch { }
        }
        return fallback;
    }

    private static bool StatusBool(Dictionary<string, object> status, string key, bool fallback)
    {
        object value;
        if (status != null && status.TryGetValue(key, out value) && value != null)
        {
            try { return Convert.ToBoolean(value); }
            catch { }
        }
        return fallback;
    }

    private static FrameScopeHistoryEntry AddHistoryEntry(FrameScopeTarget target, string runDir, Dictionary<string, object> status, int monitorExitCode)
    {
        var entry = new FrameScopeHistoryEntry
        {
            Time = DateTime.Now.ToString("o"),
            Game = target.Name,
            ProcessName = target.ProcessName,
            RunDir = runDir,
            ReportHtml = StatusString(status, "ReportHtml", Path.Combine(runDir, "charts", "framescope-interactive-report.html")),
            PresentMonCsv = StatusString(status, "PresentMonCsv", Path.Combine(runDir, "presentmon.csv")),
            ProcessCsv = StatusString(status, "ProcessCsv", Path.Combine(runDir, "process-samples.csv")),
            SystemCsv = StatusString(status, "SamplesCsv", Path.Combine(runDir, "system-samples.csv")),
            SummaryPath = StatusString(status, "SummaryPath", Path.Combine(runDir, "summary.json")),
            MonitorExitCode = monitorExitCode
        };

        File.AppendAllText(HistoryPath, Json.Serialize(entry) + Environment.NewLine);
        return entry;
    }

    private static bool ShouldOpenReport(FrameScopeTarget target, FrameScopeConfig config)
    {
        return target.OpenReportOnComplete && config.OpenReportOnComplete;
    }

    private static bool ShouldAutoOpenCompletedReport(Dictionary<string, object> status)
    {
        if (!StatusBool(status, "ReportHasFrameData", false)) return false;
        return string.IsNullOrWhiteSpace(StatusString(status, "ReportError", ""));
    }

    private static void WriteNativeWatcherState(string configPath, string phase, Dictionary<string, ActiveMonitor> activeMonitors, int completedRuns, string lastReport)
    {
        var active = activeMonitors.Values.Select(item => new
        {
            Key = GetTargetBaseName(item.Target.ProcessName).ToLowerInvariant(),
            Game = item.Target.Name,
            ProcessName = item.Target.ProcessName,
            MonitorPid = MonitorHasExited(item) ? (int?)null : item.MonitorPid,
            RunRoot = item.RunRoot
        }).ToArray();

        var state = new
        {
            Time = DateTime.Now.ToString("o"),
            Phase = phase,
            ConfigPath = configPath,
            WatcherPid = Process.GetCurrentProcess().Id,
            WatcherMode = "native",
            CompletedRuns = completedRuns,
            LastReport = lastReport,
            HistoryPath = FrameScopeNativeMonitor.HistoryPath,
            LogPath = Path.Combine(Root, "framescope-watcher.log"),
            ActiveMonitors = active
        };
        File.WriteAllText(StatePath, Json.Serialize(state));
    }

    private static void WriteFrameScopeLog(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Root, "framescope-watcher.log"), DateTime.Now.ToString("o") + " " + message + Environment.NewLine);
        }
        catch { }
    }

    private static bool TryOpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var fullPath = Path.GetFullPath(path);
        if (Path.GetExtension(fullPath).Equals(".html", StringComparison.OrdinalIgnoreCase) && TryOpenHtmlWithEdge(fullPath))
        {
            return true;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true, Verb = "open" });
            return true;
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("open-report-shell-failed path=" + fullPath + " error=" + ex.Message);
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = QuoteCommandArgument(fullPath), UseShellExecute = false, CreateNoWindow = true });
            return true;
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("open-report-explorer-failed path=" + fullPath + " error=" + ex.Message);
            return false;
        }
    }

    private static bool TryOpenHtmlWithEdge(string htmlPath)
    {
        foreach (var edgePath in GetEdgeCandidates())
        {
            if (!File.Exists(edgePath)) continue;
            try
            {
                var uri = new Uri(htmlPath).AbsoluteUri;
                Process.Start(new ProcessStartInfo
                {
                    FileName = edgePath,
                    Arguments = "--new-window " + QuoteCommandArgument(uri),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                WriteFrameScopeLog("open-report-edge report=" + htmlPath + " edge=" + edgePath);
                return true;
            }
            catch (Exception ex)
            {
                WriteFrameScopeLog("open-report-edge-failed path=" + htmlPath + " edge=" + edgePath + " error=" + ex.Message);
            }
        }
        return false;
    }

    private static IEnumerable<string> GetEdgeCandidates()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            yield return Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe");
        if (!string.IsNullOrWhiteSpace(programFiles))
            yield return Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe");
    }

    private static bool TryOpenReport(string reportHtml, string runDir)
    {
        var markerPath = Path.Combine(runDir, "report-opened.flag");
        if (File.Exists(markerPath))
        {
            WriteFrameScopeLog("report-open-skip already-opened report=" + reportHtml);
            return true;
        }

        if (TryOpenPath(reportHtml))
        {
            try { File.WriteAllText(markerPath, DateTime.Now.ToString("o"), Encoding.UTF8); }
            catch { }
            WriteFrameScopeLog("report-opened report=" + reportHtml);
            return true;
        }

        return false;
    }

    private static void MarkReportOpened(string runDir, Dictionary<string, object> status)
    {
        try
        {
            var statusPath = Path.Combine(runDir, "status.json");
            var map = status != null
                ? new Dictionary<string, object>(status, StringComparer.OrdinalIgnoreCase)
                : ReadStatusDictionary(runDir);
            if (map == null) map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            map["ReportOpened"] = true;
            map["ReportOpenedAt"] = DateTime.Now.ToString("o");
            File.WriteAllText(statusPath, Json.Serialize(map), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("status-report-opened-update-failed run=" + runDir + " error=" + ex.Message);
        }
    }

    private static void DisposeProcess(Process process)
    {
        if (process == null) return;
        try { process.Dispose(); }
        catch { }
    }

    private static void BuildUi(FrameScopeConfig config)
    {
        form = new Form
        {
            Text = "FrameScope Monitor",
            StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(1080, 680),
            MinimumSize = new Size(980, 610),
            BackColor = Color.FromArgb(17, 26, 36),
            ForeColor = Color.FromArgb(239, 247, 255),
            Font = new Font("Microsoft YaHei UI", 9f),
            Opacity = 0
        };
        form.Shown += (_, __) => FadeIn(form);
        form.FormClosing += (_, __) => StopFrameScopeBackgroundProcesses();

        var title = new Label
        {
            Text = "FrameScope Monitor",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 211, 91),
            Location = new Point(18, 14),
            Size = new Size(420, 42)
        };
        form.Controls.Add(title);

        var subtitle = new Label
        {
            Text = "选择要监测的游戏进程；游戏退出后自动生成并打开 HTML 报告，同时保存原始 CSV 路径。",
            ForeColor = Color.FromArgb(159, 180, 196),
            Location = new Point(22, 58),
            Size = new Size(920, 24)
        };
        form.Controls.Add(subtitle);

        grid = new DataGridView
        {
            Location = new Point(20, 96),
            Size = new Size(1020, 340),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = true,
            AllowUserToResizeColumns = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BackgroundColor = Color.FromArgb(20, 29, 40),
            GridColor = Color.FromArgb(56, 84, 103),
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeight = 30,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles = false
        };
        grid.RowTemplate.Height = 28;
        grid.RowTemplate.Resizable = DataGridViewTriState.False;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(33, 49, 69);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(33, 49, 69);
        grid.DefaultCellStyle.BackColor = Color.FromArgb(20, 29, 40);
        grid.DefaultCellStyle.ForeColor = Color.FromArgb(239, 247, 255);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(20, 116, 190);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(24, 36, 50);
        grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(239, 247, 255);

        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "启用", FillWeight = 42 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "GameName", HeaderText = "游戏/软件名称", FillWeight = 150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProcessName", HeaderText = "进程名", FillWeight = 230 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SampleMs", HeaderText = "帧监测(ms)", FillWeight = 62 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "AutoOpen", HeaderText = "结束后打开报告", FillWeight = 86 });
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.Resizable = DataGridViewTriState.False;
        }

        foreach (var target in config.Targets)
        {
            grid.Rows.Add(target.Enabled, target.Name, target.ProcessName, target.SampleIntervalMs, target.OpenReportOnComplete);
        }
        form.Controls.Add(grid);

        processCombo = new ComboBox
        {
            Location = new Point(20, 450),
            Size = new Size(300, 28),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            DropDownStyle = ComboBoxStyle.DropDown,
            BackColor = Color.FromArgb(20, 29, 40),
            ForeColor = Color.FromArgb(239, 247, 255)
        };
        form.Controls.Add(processCombo);

        var refreshButton = Button("刷新进程", 330, 449, 90, 30);
        refreshButton.Click += (_, __) => RefreshProcessList();
        form.Controls.Add(refreshButton);

        var addButton = Button("添加进程", 430, 449, 90, 30);
        addButton.Click += (_, __) => AddSelectedProcess();
        form.Controls.Add(addButton);

        form.Controls.Add(new Label { Text = "数据目录", Location = new Point(20, 496), Size = new Size(70, 24), Anchor = AnchorStyles.Left | AnchorStyles.Bottom, ForeColor = Color.FromArgb(239, 247, 255) });
        dataRootText = new TextBox
        {
            Text = config.DataRoot,
            Location = new Point(90, 494),
            Size = new Size(640, 26),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Color.FromArgb(20, 29, 40),
            ForeColor = Color.FromArgb(239, 247, 255),
            BorderStyle = BorderStyle.FixedSingle
        };
        form.Controls.Add(dataRootText);

        var browseButton = Button("选择", 740, 492, 70, 30);
        browseButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        browseButton.Click += (_, __) => BrowseDataRoot();
        form.Controls.Add(browseButton);

        autoOpenCheck = new CheckBox
        {
            Text = "监测结束后自动打开报告",
            Checked = config.OpenReportOnComplete,
            Location = new Point(830, 496),
            Size = new Size(210, 24),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            ForeColor = Color.FromArgb(239, 247, 255)
        };
        form.Controls.Add(autoOpenCheck);

        var saveButton = Button("保存配置", 20, 542, 100, 34);
        saveButton.Click += (_, __) => SaveConfigFromGrid();
        form.Controls.Add(saveButton);

        startButton = Button("启动监测", 130, 542, 100, 34);
        startButton.Click += (_, __) => StartWatcher();
        form.Controls.Add(startButton);

        var stopButton = Button("停止监测", 240, 542, 100, 34);
        stopButton.Click += (_, __) => StopWatcher();
        form.Controls.Add(stopButton);

        var openDataButton = Button("打开数据目录", 350, 542, 115, 34);
        openDataButton.Click += (_, __) => OpenDataRoot();
        form.Controls.Add(openDataButton);

        var openLatestButton = Button("打开最近报告", 475, 542, 115, 34);
        openLatestButton.Click += (_, __) => OpenLatestReport();
        form.Controls.Add(openLatestButton);

        var openHistoryButton = Button("打开历史记录", 600, 542, 115, 34);
        openHistoryButton.Click += (_, __) => OpenHistory();
        form.Controls.Add(openHistoryButton);

        statusLabel = new Label
        {
            Text = "状态：未启动",
            ForeColor = Color.FromArgb(169, 255, 71),
            Location = new Point(20, 592),
            Size = new Size(1000, 28),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        form.Controls.Add(statusLabel);

        statusTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        statusTimer.Tick += (_, __) => UpdateWatcherStatus();
        statusTimer.Start();
    }

    private static Button Button(string text, int x, int y, int width, int height)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(24, 36, 50),
            ForeColor = Color.FromArgb(239, 247, 255)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(113, 166, 197);
        button.MouseEnter += (_, __) => button.BackColor = Color.FromArgb(34, 68, 92);
        button.MouseLeave += (_, __) => button.BackColor = Color.FromArgb(24, 36, 50);
        button.MouseDown += (_, __) => button.BackColor = Color.FromArgb(38, 112, 145);
        button.MouseUp += (_, __) => button.BackColor = Color.FromArgb(34, 68, 92);
        return button;
    }

    private static void FadeIn(Form target)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 15 };
        timer.Tick += (_, __) =>
        {
            if (target.IsDisposed)
            {
                timer.Stop();
                timer.Dispose();
                return;
            }
            target.Opacity = Math.Min(1, target.Opacity + 0.08);
            if (target.Opacity >= 1)
            {
                timer.Stop();
                timer.Dispose();
            }
        };
        timer.Start();
    }

    private static void SetStatus(string text)
    {
        statusLabel.Text = "状态：" + text;
        statusLabel.ForeColor = Color.FromArgb(41, 230, 255);
        var timer = new System.Windows.Forms.Timer { Interval = 180 };
        timer.Tick += (_, __) =>
        {
            statusLabel.ForeColor = Color.FromArgb(169, 255, 71);
            timer.Stop();
            timer.Dispose();
        };
        timer.Start();
    }

    private static FrameScopeConfig ReadGridConfig()
    {
        var targets = new List<FrameScopeTarget>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            var processName = Convert.ToString(row.Cells["ProcessName"].Value) ?? "";
            if (string.IsNullOrWhiteSpace(processName)) continue;
            var name = Convert.ToString(row.Cells["GameName"].Value);
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(processName);
            int sampleMs;
            if (!int.TryParse(Convert.ToString(row.Cells["SampleMs"].Value), out sampleMs)) sampleMs = 100;
            if (sampleMs < 50) sampleMs = 50;
            targets.Add(new FrameScopeTarget
            {
                Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? false),
                Name = name ?? processName,
                ProcessName = processName,
                SampleIntervalMs = sampleMs,
                ProcessSampleIntervalMs = 100,
                SlowSampleIntervalMs = 1000,
                OpenReportOnComplete = Convert.ToBoolean(row.Cells["AutoOpen"].Value ?? true)
            });
        }

        return new FrameScopeConfig
        {
            PollIntervalMs = 1000,
            DataRoot = dataRootText.Text,
            OpenReportOnComplete = autoOpenCheck.Checked,
            MonitorScript = NativeMonitorMode,
            Targets = targets
        };
    }

    private static void SaveConfigFromGrid()
    {
        try
        {
            SaveConfig(ReadGridConfig());
            SetStatus("配置已保存：" + ConfigPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void RefreshProcessList()
    {
        processCombo.Items.Clear();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                names.Add(process.ProcessName + ".exe");
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        foreach (var name in names)
        {
            processCombo.Items.Add(name);
        }
        SetStatus("已刷新当前进程列表");
    }

    private static void AddSelectedProcess()
    {
        var processName = processCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(processName)) return;
        if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) processName += ".exe";
        grid.Rows.Add(true, Path.GetFileNameWithoutExtension(processName), processName, 100, true);
        SetStatus("已添加 " + processName);
    }

    private static void BrowseDataRoot()
    {
        using (var dialog = new FolderBrowserDialog { Description = "选择 FrameScope 数据目录" })
        {
            if (dialog.ShowDialog(form) == DialogResult.OK)
            {
                dataRootText.Text = dialog.SelectedPath;
            }
        }
    }

    private static bool IsWatcherRunning(out int pid)
    {
        pid = 0;
        if (!File.Exists(StatePath)) return false;
        try
        {
            var state = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath));
            if (!state.ContainsKey("WatcherPid")) return false;
            pid = Convert.ToInt32(state["WatcherPid"]);
            using (Process.GetProcessById(pid))
            {
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void StartWatcher()
    {
        try
        {
            SaveConfig(ReadGridConfig());
            int existingPid;
            if (IsWatcherRunning(out existingPid))
            {
                SetStatus("监测已经在运行，Watcher PID=" + existingPid);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = "--watcher --config " + Quote(ConfigPath),
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var proc = Process.Start(psi);
            try
            {
                if (proc != null) proc.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch { }
            SetStatus("监测已启动，Watcher PID=" + (proc != null ? proc.Id.ToString() : "未知") + "。现在启动配置里的游戏就会自动记录。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void StopWatcher()
    {
        if (!HasFrameScopeBackgroundProcesses())
        {
            SetStatus("没有正在运行的 FrameScope watcher");
            return;
        }
        try
        {
            StopFrameScopeBackgroundProcesses();
            SetStatus("监测已停止。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "停止失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool HasFrameScopeBackgroundProcesses()
    {
        return EnumerateFrameScopeBackgroundPids().Count > 0;
    }

    private static void StopFrameScopeBackgroundProcesses()
    {
        var pids = EnumerateFrameScopeBackgroundPids();
        if (pids.Count == 0) return;

        var processMap = ReadProcessMap();
        var all = new HashSet<int>(pids);
        foreach (var pid in pids.ToArray())
        {
            AddChildPids(pid, processMap, all);
        }

        foreach (var pid in all.OrderByDescending(pid => GetTreeDepth(pid, processMap)))
        {
            TryKillProcess(pid);
        }

        try
        {
            if (File.Exists(StatePath)) File.Delete(StatePath);
        }
        catch { }
    }

    private static HashSet<int> EnumerateFrameScopeBackgroundPids()
    {
        var result = new HashSet<int>();
        var rootLower = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        foreach (var info in ReadProcessInfo())
        {
            if (info.ProcessId <= 0 || info.ProcessId == Process.GetCurrentProcess().Id) continue;
            var name = info.Name ?? "";
            var commandLine = info.CommandLine ?? "";
            var exePath = info.ExecutablePath ?? "";
            var exeLower = exePath.ToLowerInvariant();
            var commandLower = commandLine.ToLowerInvariant();

            if (name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) &&
                (commandLower.Contains("framescopewatcher.ps1") ||
                 commandLower.Contains("monitor-cs2-highfreq.ps1")) &&
                commandLower.Contains(rootLower))
            {
                result.Add(info.ProcessId);
                continue;
            }

            if (name.Equals("FrameScopeMonitor.exe", StringComparison.OrdinalIgnoreCase) &&
                (commandLower.Contains("--watcher") || commandLower.Contains("--monitor-session")) &&
                exeLower.StartsWith(rootLower, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(info.ProcessId);
                continue;
            }

            if ((name.Equals("FrameScopeProcessSampler.exe", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("FrameScopeSystemSampler.exe", StringComparison.OrdinalIgnoreCase)) &&
                exeLower.StartsWith(rootLower, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(info.ProcessId);
                continue;
            }

            if (name.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase) &&
                exeLower.StartsWith(Path.Combine(rootLower, "tools"), StringComparison.OrdinalIgnoreCase))
            {
                result.Add(info.ProcessId);
            }
        }
        return result;
    }

    private sealed class ProcessInfo
    {
        public int ProcessId;
        public int ParentProcessId;
        public string Name;
        public string CommandLine;
        public string ExecutablePath;
    }

    private static List<ProcessInfo> ReadProcessInfo()
    {
        var list = new List<ProcessInfo>();
        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, CommandLine, ExecutablePath FROM Win32_Process"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        list.Add(new ProcessInfo
                        {
                            ProcessId = Convert.ToInt32(item["ProcessId"] ?? 0),
                            ParentProcessId = Convert.ToInt32(item["ParentProcessId"] ?? 0),
                            Name = Convert.ToString(item["Name"] ?? ""),
                            CommandLine = Convert.ToString(item["CommandLine"] ?? ""),
                            ExecutablePath = Convert.ToString(item["ExecutablePath"] ?? "")
                        });
                    }
                }
            }
        }
        catch { }
        return list;
    }

    private static Dictionary<int, List<int>> ReadProcessMap()
    {
        var map = new Dictionary<int, List<int>>();
        foreach (var info in ReadProcessInfo())
        {
            if (!map.ContainsKey(info.ParentProcessId)) map[info.ParentProcessId] = new List<int>();
            map[info.ParentProcessId].Add(info.ProcessId);
        }
        return map;
    }

    private static void AddChildPids(int pid, Dictionary<int, List<int>> processMap, HashSet<int> result)
    {
        List<int> children;
        if (!processMap.TryGetValue(pid, out children)) return;
        foreach (var child in children)
        {
            if (result.Add(child)) AddChildPids(child, processMap, result);
        }
    }

    private static int GetTreeDepth(int pid, Dictionary<int, List<int>> processMap)
    {
        List<int> children;
        if (!processMap.TryGetValue(pid, out children) || children.Count == 0) return 0;
        var max = 0;
        foreach (var child in children) max = Math.Max(max, 1 + GetTreeDepth(child, processMap));
        return max;
    }

    private static void TryKillProcess(int pid)
    {
        try
        {
            using (var process = Process.GetProcessById(pid))
            {
                process.Kill();
                process.WaitForExit(3000);
            }
        }
        catch { }
    }

    private static void UpdateWatcherStatus()
    {
        int pid;
        if (IsWatcherRunning(out pid))
        {
            pulse = !pulse;
            startButton.BackColor = pulse ? Color.FromArgb(30, 96, 78) : Color.FromArgb(24, 36, 50);
            string text = "运行中，Watcher PID=" + pid;
            try
            {
                var state = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath));
                if (state.ContainsKey("CompletedRuns")) text += "，已完成 " + state["CompletedRuns"] + " 次";
                if (state.ContainsKey("LastReport") && state["LastReport"] != null && state["LastReport"].ToString() != "") text += "，最近报告：" + state["LastReport"];
            }
            catch { }
            statusLabel.Text = "状态：" + text;
        }
        else if (startButton.BackColor != Color.FromArgb(24, 36, 50))
        {
            startButton.BackColor = Color.FromArgb(24, 36, 50);
        }
    }

    private static void OpenDataRoot()
    {
        var path = dataRootText.Text;
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private static FrameScopeHistoryEntry LatestHistory()
    {
        if (!File.Exists(HistoryPath)) return null;
        var line = File.ReadLines(HistoryPath).LastOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (line == null) return null;
        try { return Json.Deserialize<FrameScopeHistoryEntry>(line); }
        catch { return null; }
    }

    private static string LatestReportPath()
    {
        var entry = LatestHistory();
        if (entry != null && File.Exists(entry.ReportHtml)) return entry.ReportHtml;

        var root = dataRootText != null ? dataRootText.Text : DefaultDataRoot;
        if (string.IsNullOrWhiteSpace(root)) root = DefaultDataRoot;
        if (!Path.IsPathRooted(root)) root = Path.Combine(Root, root);
        if (!Directory.Exists(root)) return "";

        try
        {
            return Directory.GetFiles(root, "framescope-interactive-report.html", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void OpenLatestReport()
    {
        var reportPath = LatestReportPath();
        if (!string.IsNullOrWhiteSpace(reportPath) && File.Exists(reportPath))
        {
            Process.Start(new ProcessStartInfo { FileName = reportPath, UseShellExecute = true });
            SetStatus("已打开最近报告：" + reportPath);
            return;
        }
        SetStatus("没有找到报告。");
    }

    private static void OpenHistory()
    {
        if (!File.Exists(HistoryPath)) File.WriteAllText(HistoryPath, "");
        Process.Start(new ProcessStartInfo { FileName = HistoryPath, UseShellExecute = true });
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}

