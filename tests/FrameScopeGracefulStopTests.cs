using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Compiled with the real Watcher.cs. Only capture/report/system dependencies
// are replaced: the watcher loop and its worker Process ownership are real.
internal static partial class FrameScopeNativeMonitor
{
    private static string Root, ConfigPath, StatePath, HistoryPath;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    internal static bool TestReportFailed { get { return File.Exists(Path.Combine(Root, "report-failed")); } }

    public static int Main(string[] args)
    {
        if (args.Contains("--monitor-session")) return FakeWorker(args);
        if (args.Contains("--watcher"))
        {
            ConfigPath = GetArgValue(args, "--config", "");
            Root = Path.GetDirectoryName(ConfigPath);
            StatePath = Path.Combine(Root, "framescope-watcher-state.json");
            HistoryPath = Path.Combine(Root, "history.json");
            RunNativeWatcher(args);
            return 0;
        }
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "case-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(root);
        string config = Path.Combine(root, "framescope-config.json");
        File.WriteAllText(config, "{}");
        bool idle = args.Contains("--test-idle");
        bool reportFailed = args.Contains("--test-report-failed");
        if (idle) File.WriteAllText(Path.Combine(root, "no-target"), "");
        if (reportFailed) File.WriteAllText(Path.Combine(root, "report-failed"), "");
        Process watcher = Process.Start(new ProcessStartInfo(Application.ExecutablePath,
            "--watcher --exit-after-first-run --config " + Quote(config))
            { UseShellExecute = false, CreateNoWindow = true });
        try
        {
            StopErrorPriorityNeverDowngradesWorkerFailure();
            string statePath = Path.Combine(root, "framescope-watcher-state.json");
            Wait(delegate { return File.Exists(idle ? statePath : Path.Combine(root, "worker-ready")); }, "watcher/worker startup");
            var state = Json.Deserialize<Dictionary<string, object>>(ReadSnapshot(statePath));
            string token = state.ContainsKey("WatcherInstanceId") ? Convert.ToString(state["WatcherInstanceId"]) : "legacy";
            string request = Path.Combine(root, "framescope-watcher-stop-" + token + ".json");
            var fields = new Dictionary<string, object> {
                { "Action", "stop" }, { "WatcherInstanceId", "wrong-token" },
                { "WatcherPid", watcher.Id },
                { "WatcherStartTimeUnix", state.ContainsKey("WatcherStartTimeUnix") ? state["WatcherStartTimeUnix"] : 0 },
                { "ConfigPath", config }
            };
            FrameScopeJsonFile.Write(request, Json.Serialize(fields));
            Thread.Sleep(350);
            Assert(!File.Exists(Path.Combine(root, "worker-drained")), "wrong token must not stop worker");
            fields["WatcherInstanceId"] = token;
            FrameScopeJsonFile.Write(request, Json.Serialize(fields));
            if (idle)
            {
                Assert(watcher.WaitForExit(5000), "idle watcher must acknowledge stop");
                state = Json.Deserialize<Dictionary<string, object>>(ReadSnapshot(statePath));
                Assert(Convert.ToString(state["Phase"]) == "stopped", "idle watcher terminal state");
                Assert(Convert.ToInt32(state["CompletedRuns"]) == 0, "idle stop must not fabricate a report");
                Assert(!File.Exists(Path.Combine(root, "worker-starts")), "idle stop must not start a worker");
                Console.WriteLine("FrameScopeGracefulStopTests: PASS (idle watcher, no report fabricated)");
                Console.WriteLine("Evidence: " + root);
                return 0;
            }
            Wait(delegate { return File.Exists(Path.Combine(root, "worker-drained")); }, "valid stop must drain worker", 4000);
            Wait(delegate { return File.Exists(Path.Combine(root, "report-started")); }, "report generation");
            state = Json.Deserialize<Dictionary<string, object>>(ReadSnapshot(statePath));
            Assert(Convert.ToString(state["Phase"]) == "generating", "watcher must publish generating before blocking on report");
            Assert(!watcher.HasExited, "watcher must wait for report");
            Assert(File.ReadAllLines(Path.Combine(root, "worker-starts")).Length == 1, "stop must not admit another target");
            File.WriteAllText(Path.Combine(root, "release-report"), "");
            Assert(watcher.WaitForExit(5000), "watcher must exit after report finishes");
            Assert(watcher.ExitCode == 0, "watcher exit code");
            state = Json.Deserialize<Dictionary<string, object>>(ReadSnapshot(statePath));
            Assert(Convert.ToString(state["Phase"]) == "stopped", "terminal state must be retained");
            Assert(Convert.ToString(state["StopErrorCode"]) == (reportFailed ? "report-finalization-failed" : ""), "terminal state classifies report failure without losing stop completion");
            Assert(Convert.ToInt32(state["CompletedRuns"]) == 1, "completed report must be counted");
            Assert(File.Exists(Path.Combine(root, "report-finished")), "report must finish before stopped");
            Console.WriteLine("FrameScopeGracefulStopTests: PASS (real watcher, isolated worker/report)");
            Console.WriteLine("Evidence: " + root);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message + "\nEvidence: " + root); return 1; }
        finally
        {
            // Only the disposable child created above is eligible for cleanup.
            File.WriteAllText(Path.Combine(root, "test-exit"), "");
            File.WriteAllText(Path.Combine(root, "release-report"), "");
            if (!watcher.WaitForExit(6000)) { watcher.Kill(); watcher.WaitForExit(); }
            watcher.Dispose();
        }
    }

    private static string ReadSnapshot(string path)
    {
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(file, System.Text.Encoding.UTF8)) return reader.ReadToEnd();
    }

    private static void StopErrorPriorityNeverDowngradesWorkerFailure()
    {
        Type owner = typeof(FrameScopeNativeMonitor);
        var method = owner.GetMethod("RecordWatcherStopError", BindingFlags.NonPublic | BindingFlags.Static);
        Assert(method != null, "watcher stop errors must preserve severity across multiple workers");
        var code = owner.GetField("WatcherStopErrorCode", BindingFlags.NonPublic | BindingFlags.Static);
        var error = owner.GetField("WatcherStopError", BindingFlags.NonPublic | BindingFlags.Static);
        code.SetValue(null, ""); error.SetValue(null, "");
        method.Invoke(null, new object[] { "worker-run-missing", "missing run" });
        method.Invoke(null, new object[] { "report-finalization-failed", "report failure" });
        Assert((string)code.GetValue(null) == "worker-run-missing", "later report warning cannot downgrade a worker failure");
        Assert((string)error.GetValue(null) == "missing run", "severe error evidence is retained");
        code.SetValue(null, ""); error.SetValue(null, "");
        method.Invoke(null, new object[] { "report-finalization-failed", "report failure" });
        method.Invoke(null, new object[] { "worker-run-missing", "missing run" });
        Assert((string)code.GetValue(null) == "worker-run-missing", "worker failure overrides an earlier report warning");
        code.SetValue(null, ""); error.SetValue(null, "");
    }

    private static int FakeWorker(string[] args)
    {
        string run = GetArgValue(args, "--RunDirectory", "");
        string root = Directory.GetParent(Directory.GetParent(GetArgValue(args, "--RunRoot", "")).FullName).FullName;
        string token = GetArgValue(args, "--StopRequestToken", "");
        File.AppendAllText(Path.Combine(root, "worker-starts"), "worker\n");
        File.WriteAllText(Path.Combine(root, "worker-ready"), run);
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 15000 && !File.Exists(Path.Combine(root, "test-exit")))
        {
            if (IsMonitorStopRequested(run, token)) break;
            Thread.Sleep(20);
        }
        File.WriteAllText(Path.Combine(root, "worker-drained"), "samplers closed; PresentMon flushed (test substitutes)");
        File.WriteAllText(Path.Combine(run, "status.json"), "{\"Phase\":\"done\"}");
        return 0;
    }
    private static void Wait(Func<bool> condition, string label, int ms = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < ms) Thread.Sleep(20);
        Assert(condition(), label);
    }
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static string GetArgValue(string[] args, string name, string fallback)
    { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
    private static bool HasArg(string[] args, string name) { return args.Contains(name); }
    private static string Quote(string s) { return "\"" + s + "\""; }
    private static FrameScopeConfig LoadConfigFromPath(string path) { return new FrameScopeConfig(); }
    private static string ResolveDataRoot(string path) { return Path.Combine(Root, "runs"); }
    private static void RecoverStaleMissingReports(string root, FrameScopeConfig config, Action<string> reportState = null) {}
    private static void ApplyRunAndHistoryRetention(string root, FrameScopeConfig config) {}
    private static Dictionary<string, object> ReadStatusDictionary(string run)
    { string path = Path.Combine(run, "status.json"); return File.Exists(path) ? Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)) : null; }
    private static Dictionary<string, object> EnsureReportForCompletedRun(string run, Dictionary<string, object> status, int code, FrameScopeConfig config)
    {
        File.WriteAllText(Path.Combine(Root, "report-started"), "");
        Wait(delegate { return File.Exists(Path.Combine(Root, "release-report")); }, "test report release", 10000);
        File.WriteAllText(Path.Combine(Root, "report-finished"), "");
        return status;
    }
    private static TestHistory AddHistoryEntry(FrameScopeTarget target, string run, Dictionary<string, object> status, int code)
    { return new TestHistory { ReportHtml = Path.Combine(run, "report.html") }; }
    private static bool ShouldOpenReport(FrameScopeTarget target, FrameScopeConfig config) { return false; }
    private static bool ShouldAutoOpenCompletedReport(Dictionary<string, object> status) { return false; }
    private static bool TryOpenReport(string report, string run) { throw new Exception("unexpected report open"); }
    private static void MarkReportOpened(string run, Dictionary<string, object> status) {}
    private static string StatusString(Dictionary<string, object> status, string key, string fallback) { return fallback; }
    private static void DisposeProcess(Process p) { if (p != null) p.Dispose(); }
    private static List<string> BuildTargetProcessBaseNames(string name, string display) { return new List<string> { "synthetic" }; }
    private static string GetTargetBaseName(string name) { return "synthetic"; }
    private static string SafeName(string name, string fallback) { return "synthetic"; }
    private static TargetProcessSnapshot FindBestTargetProcess(List<string> names, int preferred)
    { return File.Exists(Path.Combine(Root, "no-target")) ? null : new TargetProcessSnapshot { BaseName = "synthetic", ProcessId = 999999 }; }
}
internal sealed class TestHistory { public string ReportHtml; }
internal sealed class TargetProcessSnapshot { public string BaseName; public int ProcessId; }
public sealed class FrameScopeTarget { public string Name = "Synthetic"; public string ProcessName = "synthetic.exe"; public bool Enabled = true; }
public sealed class TestCpuTelemetry { public bool CollectPerCoreFrequency; }
public sealed class FrameScopeConfig
{
    public string DataRoot = ""; public bool AutoGenerateDiagnosticReport, EnableVerboseLogs, EnablePerformanceDiagnosticsLogs;
    public int PollIntervalMs = 50, TelemetrySampleIntervalMs = 1000;
    public TestCpuTelemetry CpuTelemetry = new TestCpuTelemetry();
    public List<FrameScopeTarget> Targets = new List<FrameScopeTarget> { new FrameScopeTarget() };
}
internal static class FrameScopeConfigStore
{
    public const int DefaultTelemetrySampleIntervalMs = 1000, TelemetrySampleIntervalMinMs = 500,
        TelemetrySampleIntervalMaxMs = 5000, InternalPollIntervalMs = 50;
}
internal static class FrameScopeDiagnostics
{
    public static void ApplyRetentionPolicy(string root, FrameScopeConfig config, string diagnosticOutputRoot = null) {}
    public static void QueueGenerateReport(FrameScopeConfig config, string root, string data, string reason, string run, string diagnosticOutputRoot = null) {}
    public static void AppendLogAsync(string path, string message) { File.AppendAllText(path, message + "\n"); }
}
internal sealed class FrameScopeReportArtifactState { public bool IsComplete = true; public string HtmlPath = "test-report.html"; }
internal static class FrameScopeReportArtifacts { public static FrameScopeReportArtifactState Inspect(string run) { return new FrameScopeReportArtifactState { IsComplete = !FrameScopeNativeMonitor.TestReportFailed }; } }
