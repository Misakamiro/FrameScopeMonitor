using System;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;

internal static class FrameScopeInstallationShutdownTests
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static string fixture;
    private static int assertions;
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--fixture-capture") { Console.WriteLine("READY"); Thread.Sleep(3000); return 0; }
        if (args.Length > 0 && args[0] == "--fixture-pipe") return PipeChild(args);
        try
        {
            Type helper = Assembly.GetExecutingAssembly().GetType("FrameScopeInstallationShutdown");
            if (helper == null) throw new Exception("Installation shutdown must share a stop-only production helper before destructive operations.");
            Console.WriteLine("PASS shared installation shutdown helper exists");
            fixture = Path.Combine(Path.GetTempPath(), "framescope-shutdown-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            Console.WriteLine("fixture=" + fixture);
            NativeCapture(); Selection(); Watchers(); Hosts(); Services(); Legacy(); Pipes(); Reparse();
            Console.WriteLine("PASS assertions=" + assertions);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void NativeCapture()
    {
        string root = Path.Combine(fixture, "native 中文 空格");
        Directory.CreateDirectory(root);
        string image = Path.Combine(root, "FrameScopeMonitor.exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, image);
        using (var child = Process.Start(new ProcessStartInfo(image, "--fixture-capture \"中文 空格 & %\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
        {
            try
            {
                Check(child.StandardOutput.ReadLine() == "READY", "native capture fixture ready");
                var processes = new FrameScopeInstallationShutdown.WindowsEnvironment().Capture(root);
                var captured = processes.Find(p => p.Pid == child.Id);
                Check(captured != null && FrameScopeWatcherShutdown.SamePath(captured.ExecutablePath, image), "native process image is scoped to fixture");
                Check(captured.CommandLine.Contains("中文 空格 & %"), "native capture preserves Unicode command line without WMI");
                Check(!child.HasExited, "capture does not stop the fixture producer");
            }
            finally { Check(child.WaitForExit(5000), "capture fixture exits by itself"); }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++; Console.WriteLine("PASS " + message);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception) { Check(true, message); return; }
        throw new Exception("Expected refusal: " + message);
    }
    private static string Root(string name)
    {
        string root = Path.Combine(fixture, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return root;
    }
    private static FrameScopeWatcherShutdown.Identity Identity(string root, string name, int pid)
    {
        return new FrameScopeWatcherShutdown.Identity { Pid = pid, StartTime = 123, ExecutablePath = Path.Combine(root, name), CommandLine = "" };
    }
    private static void Selection()
    {
        string root = Root("selection");
        Check(FrameScopeInstallationShutdown.WindowsEnvironment.Selected(root, "FrameScopeMonitor.exe", Path.Combine(root, "FrameScopeMonitor.exe"), ""), "target selected");
        Check(!FrameScopeInstallationShutdown.WindowsEnvironment.Selected(root, "FrameScopeMonitor.exe", Path.Combine(root + "-sibling", "FrameScopeMonitor.exe"), ""), "prefix sibling excluded");
        Check(!FrameScopeInstallationShutdown.WindowsEnvironment.Selected(root, "cs2.exe", Path.Combine(root, "cs2.exe"), ""), "game excluded");
        Check(!FrameScopeInstallationShutdown.WindowsEnvironment.Selected(root, "python.exe", Path.Combine(fixture, "python.exe"), "python framescope-runs"), "keyword is not ownership");
        Check(FrameScopeWatcherShutdown.Budget(0) == 45000 && FrameScopeWatcherShutdown.Budget(3) == 405000, "worker report communications budget");
        Reject(delegate { FrameScopeInstallationShutdown.NormalizeRoot("relative"); }, "relative root rejected");
    }
    private sealed class Fake : FrameScopeInstallationShutdown.Environment
    {
        internal readonly List<FrameScopeWatcherShutdown.Identity> Processes = new List<FrameScopeWatcherShutdown.Identity>();
        internal Action OnWait;
        internal bool Timeout, Denied, HardwareFailure, HardwareRemains;
        internal int Requests, WaitBudget;
        public List<FrameScopeWatcherShutdown.Identity> Capture(string root)
        {
            if (Denied) throw new UnauthorizedAccessException("fixture denied");
            return Processes.FindAll(p => FrameScopeInstallationShutdown.WindowsEnvironment.Selected(root, Path.GetFileName(p.ExecutablePath), p.ExecutablePath, p.CommandLine ?? ""));
        }
        public FrameScopeWatcherShutdown.Identity Inspect(int pid) { return Processes.Find(p => p.Pid == pid); }
        public bool Wait(FrameScopeWatcherShutdown.Identity identity, int budget)
        {
            Requests++; WaitBudget = budget; if (Timeout) return false;
            if (OnWait != null) OnWait(); return Inspect(identity.Pid) == null;
        }
        public void RequestHostExit(string root, FrameScopeWatcherShutdown.Identity identity, int budget) { Requests++; Processes.Remove(identity); }
        public void StopHardware(string root, FrameScopeWatcherShutdown.Identity identity)
        {
            Requests++; if (HardwareFailure) throw new InvalidOperationException("fixture foreign pipe/invalid ACK");
            if (!HardwareRemains) Processes.RemoveAll(p => p.Pid == identity.Pid || p.Pid == 42);
        }
    }
    private static void Save(string root, Dictionary<string, object> state)
    {
        FrameScopeJsonFile.Write(Path.Combine(root, "framescope-watcher-state.json"), Json.Serialize(state));
    }
    private static void Watchers()
    {
        foreach (string scenario in new[] { "normal", "warning", "worker", "unknown", "timeout", "start", "config", "config-existing", "token", "protocol", "active", "phase", "missing" })
        {
            string root = Root(scenario), token = Guid.NewGuid().ToString("N"), config = Path.Combine(root, "config.json");
            File.WriteAllText(config, "{}");
            var state = new Dictionary<string, object> { { "WatcherPid", 11 }, { "WatcherStartTimeUnix", 123L }, { "WatcherInstanceId", token },
                { "StopProtocolVersion", 1 }, { "ConfigPath", config }, { "Phase", "monitoring" }, { "ActiveMonitors", new object[] { "pending report" } } };
            var env = new Fake(); env.Processes.Add(Identity(root, "FrameScopeMonitor.exe", 11)); env.Processes.Add(Identity(root, "FrameScopeProcessSampler.exe", 12));
            env.Processes[0].CommandLine = "FrameScopeMonitor.exe --watcher --config \"" + config + "\"";
            env.Processes.Add(Identity(root + "-other", "FrameScopeMonitor.exe", 21)); env.Processes.Add(Identity(root, "cs2.exe", 22));
            env.Timeout = scenario == "timeout";
            if (scenario == "start") state["WatcherStartTimeUnix"] = 124L;
            if (scenario == "config") state["ConfigPath"] = Path.Combine(root, "missing-config.json");
            if (scenario == "config-existing") { state["ConfigPath"] = Path.Combine(root, "another-config.json"); File.WriteAllText(Convert.ToString(state["ConfigPath"]), "{}"); }
            if (scenario == "token") state["WatcherInstanceId"] = "bad-token";
            if (scenario == "protocol") state["StopProtocolVersion"] = 0;
            Save(root, state); if (scenario == "missing") File.Delete(Path.Combine(root, "framescope-watcher-state.json"));
            env.OnWait = delegate
            {
                Check(File.Exists(Path.Combine(root, "framescope-watcher-stop-" + token + ".json")), "token request " + scenario);
                state["Phase"] = scenario == "phase" ? "generating" : "stopped";
                state["ActiveMonitors"] = scenario == "active" ? new object[] { "unclosed" } : new object[0];
                if (scenario == "warning" || scenario == "worker" || scenario == "unknown")
                {
                    state["StopErrorCode"] = scenario == "warning" ? "report-finalization-failed" : scenario == "worker" ? "worker-run-missing" : "unknown";
                    state["StopError"] = "fixture retained evidence";
                }
                Save(root, state); env.Processes.RemoveAll(p => p.Pid == 11 || p.Pid == 12);
            };
            bool changed = false;
            Action run = delegate { string warning = FrameScopeInstallationShutdown.Stop(root, env); changed = true;
                Check((scenario == "warning") == (warning == "report-finalization-failed"), "warning surfaced " + scenario); };
            if (scenario == "normal" || scenario == "warning") run();
            else { Reject(run, "refuse " + scenario); Check(!changed, "no destructive continuation " + scenario); }
            Check(env.Inspect(21) != null && env.Inspect(22) != null, "external and game survive " + scenario);
            if (scenario == "timeout") Check(env.Inspect(11) != null && File.Exists(Path.Combine(root, "framescope-watcher-stop-" + token + ".json")), "timeout preserves process/evidence");
            if (scenario == "normal") Check(env.WaitBudget == 165000, "pending report full budget");
            if (scenario == "warning") Check(File.ReadAllText(Path.Combine(root, "framescope-watcher-state.json")).Contains("fixture retained evidence"), "report error remains");
        }
        Reject(delegate { FrameScopeInstallationShutdown.Stop(Root("denied"), new Fake { Denied = true }); }, "permission failure blocks mutation");
    }
    private static void Marker(string root)
    {
        string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(root, "FrameScope.exe")))).Replace("-", "");
        File.WriteAllText(Path.Combine(root, "FrameScopeHostCapabilities.json"), Json.Serialize(new { schema = 1, requestExitProtocol = 1, hostSha256 = hash }));
    }
    private static void Hosts()
    {
        string root = Root("host"); File.WriteAllText(Path.Combine(root, "FrameScope.exe"), "synthetic image");
        var env = new Fake(); env.Processes.Add(Identity(root, "FrameScope.exe", 30));
        Reject(delegate { FrameScopeInstallationShutdown.Stop(root, env); }, "unknown host not probed"); Check(env.Requests == 0, "unknown host no request");
        Marker(root); File.AppendAllText(Path.Combine(root, "FrameScope.exe"), "tamper");
        Reject(delegate { FrameScopeInstallationShutdown.Stop(root, env); }, "hash mismatch"); Check(env.Requests == 0, "mismatch no request");
        Marker(root); Check(FrameScopeInstallationShutdown.Stop(root, env) == "" && env.Inspect(30) == null, "verified host exits normally");
        Check(env.Requests == 2, "original host exit separately checked");
    }
    private static void Services()
    {
        foreach (string scenario in new[] { "normal", "foreign", "ack-alive" })
        {
            string root = Root("service-" + scenario); var env = new Fake { HardwareFailure = scenario == "foreign", HardwareRemains = scenario == "ack-alive" };
            env.Processes.Add(Identity(root, "FrameScopeHardwareService.exe", 40)); env.Processes.Add(Identity(root + "-borrowed", "FrameScopeHardwareService.exe", 41));
            env.Processes.Add(Identity(root, "FrameScopeHardwareProvider.exe", 42));
            if (scenario == "normal") Check(FrameScopeInstallationShutdown.Stop(root, env) == "", "idle service owns provider without watcher state");
            else Reject(delegate { FrameScopeInstallationShutdown.Stop(root, env); }, "service rejects " + scenario);
            Check(env.Inspect(41) != null, "borrowed service survives " + scenario);
            if (scenario != "normal") Check(env.Inspect(40) != null, "failed service preserved " + scenario);
        }
        string orphanRoot = Root("orphan"); var orphan = new Fake(); orphan.Processes.Add(Identity(orphanRoot, "FrameScopeHardwareProvider.exe", 42));
        Reject(delegate { FrameScopeInstallationShutdown.Stop(orphanRoot, orphan); }, "orphan provider refused"); Check(orphan.Requests == 0, "orphan never killed");
    }
    private static void Legacy()
    {
        string root = Root("legacy"); var plan = new FrameScopeLegacyCleanup.CleanupPlan(); plan.ProgramDirectories.Add(root);
        var env = new Fake(); env.Processes.Add(Identity(root, "FrameScopeMonitor.exe", 50));
        FrameScopeLegacyCleanup.StopPlan(plan, true, root, env); Check(env.Requests == 0 && env.Inspect(50) != null, "legacy dry-run sends no stop");
        Reject(delegate { FrameScopeLegacyCleanup.StopPlan(plan, false, root, env); }, "legacy unknown ownership blocks deletion");
        Check(plan.ProgramDirectories.Count == 1 && plan.ProgramDirectories[0] == root, "legacy deletion plan preserved");
        string other = Root("legacy-other"); plan.ProgramDirectories.Add(other); env.Processes.Add(Identity(other, "FrameScopeMonitor.exe", 51));
        Reject(delegate { FrameScopeLegacyCleanup.StopPlan(plan, false, root, env); }, "legacy other deletion root active refuses");
        Check(env.Requests == 0 && env.Inspect(51) != null, "legacy external root no stop");
    }
    private static int PipeChild(string[] args)
    {
        using (var pipe = new NamedPipeServerStream(args[1], PipeDirection.InOut, 1, PipeTransmissionMode.Message))
        {
            File.WriteAllText(args[2], "ready"); pipe.WaitForConnection();
            using (var reader = new StreamReader(pipe)) using (var writer = new StreamWriter(pipe) { AutoFlush = true })
            for (int i = 0; i < 2; i++)
            {
                string line = reader.ReadLine(); if (line == null) return 0;
                var request = Json.Deserialize<Dictionary<string, object>>(line); string method = Convert.ToString(request["method"]);
                File.AppendAllText(args[3], method + "\n");
                object result = method == "handshake" ? (object)new { protocol = "framescope-hardware-service/1", capabilities = new[] { "stop" } } : new { status = "stopped", shutdownComplete = args[4] != "bad-ack" };
                writer.WriteLine(Json.Serialize(new { id = request["id"], ok = true, result = result }));
            }
        }
        return 0;
    }
    private static void Pipes()
    {
        foreach (string scenario in new[] { "normal", "foreign", "server-pid", "start-identity", "bad-ack" })
        {
            string root = Root("pipe-" + scenario), actual = scenario == "foreign" ? Root("foreign-pipe") : root;
            string exe = Path.Combine(actual, "FrameScopeHardwareService.exe"); File.Copy(Assembly.GetExecutingAssembly().Location, exe);
            string pipe = "framescope-shutdown-fixture-" + Guid.NewGuid().ToString("N"), ready = Path.Combine(actual, "ready"), log = Path.Combine(actual, "requests.log");
            using (var child = Process.Start(new ProcessStartInfo(exe, "--fixture-pipe " + pipe + " \"" + ready + "\" \"" + log + "\" " + scenario) { UseShellExecute = false, CreateNoWindow = true }))
            {
                try
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!File.Exists(ready) && DateTime.UtcNow < deadline && !child.HasExited) Thread.Sleep(20);
                    Check(File.Exists(ready), "fixture pipe ready " + scenario);
                    var identity = FrameScopeWatcherShutdown.Inspect(child.Id);
                    if (scenario == "foreign") identity.ExecutablePath = Path.Combine(root, "FrameScopeHardwareService.exe");
                    if (scenario == "server-pid") identity.Pid = -1;
                    if (scenario == "start-identity") identity.StartTime++;
                    var environment = new FrameScopeInstallationShutdown.WindowsEnvironment(pipe);
                    if (scenario == "normal") { environment.StopHardware(root, identity); Check(child.WaitForExit(5000), "native ACK then actual fixture exit"); }
                    else Reject(delegate { environment.StopHardware(root, identity); }, "native pipe rejects " + scenario);
                    if (scenario == "foreign" || scenario == "server-pid" || scenario == "start-identity") Check(!File.Exists(log) && !child.HasExited, "unverified pipe no handshake/stop and remains alive " + scenario);
                    else Check(File.ReadAllText(log).Contains("handshake\nstop\n"), "native handshake before stop " + scenario);
                }
                finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
            }
        }
    }

    private static void Reparse()
    {
        string root = Root("reparse"), external = Root("reparse-external"), link = Path.Combine(root, "linked");
        File.WriteAllText(Path.Combine(external, "FrameScopeMonitor.exe"), "synthetic external image");
        using (var create = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + external + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        {
            if (!create.WaitForExit(5000)) throw new Exception("Fixture junction creation timeout");
            Check(create.ExitCode == 0, "TEMP junction fixture created");
        }
        Reject(delegate { FrameScopeWatcherShutdown.RequirePlainPath(Path.Combine(link, "FrameScopeMonitor.exe")); }, "shared watcher rejects reparse image ancestor");
        Reject(delegate { FrameScopeInstallationShutdown.NormalizeRoot(link); }, "installation rejects reparse root");
        var env = new Fake(); env.Processes.Add(Identity(link, "FrameScopeMonitor.exe", 61));
        Reject(delegate { FrameScopeInstallationShutdown.Stop(root, env); }, "installation rejects reparse image before stop");
        Check(env.Requests == 0 && File.ReadAllText(Path.Combine(external, "FrameScopeMonitor.exe")) == "synthetic external image", "foreign image unchanged and no stop");
    }
}
