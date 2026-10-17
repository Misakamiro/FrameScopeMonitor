using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;

public static class FrameScopeProcessCleanupTests
{
    public static int Main(string[] args)
    {
        if (args != null && Array.IndexOf(args, "--watcher-sleep") >= 0)
        {
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixture-ready-" + Process.GetCurrentProcess().Id), "ready");
            if (Array.IndexOf(args, "--respond-stop") >= 0)
            {
                string token = args[args.Length - 1];
                string stopPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "framescope-watcher-stop-" + token + ".json");
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (!File.Exists(stopPath) && DateTime.UtcNow < deadline) Thread.Sleep(50);
                if (!File.Exists(stopPath)) return 2;
                Thread.Sleep(300); // simulate report completion after stop
                string statePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "framescope-watcher-state.json");
                var serializer = new JavaScriptSerializer();
                var state = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(statePath));
                state["Phase"] = "stopped";
                state["ActiveMonitors"] = new object[0];
                state["StopErrorCode"] = Array.IndexOf(args, "--report-warning") >= 0 ? "report-finalization-failed" :
                    Array.IndexOf(args, "--worker-missing") >= 0 ? "worker-run-missing" :
                    Array.IndexOf(args, "--unknown-error") >= 0 ? "unknown-shutdown-error" : "";
                state["StopError"] = Convert.ToString(state["StopErrorCode"]);
                File.WriteAllText(statePath, serializer.Serialize(state));
            }
            else Thread.Sleep(30000);
            return 0;
        }

        try
        {
            HostLifecycleSerializesStartAndExit();
            StopClearsOnlyItsOwnOwnership();
            FailedHandshakeRetainsPendingOwnership();
            HostExitStopsOwnedOnly();
            UnknownWatcherIsNotKilled();
            AssertTrue(!FrameScopeNativeMonitor.IsPathWithinRoot(@"C:\RootSibling\FrameScopeMonitor.exe", @"C:\Root"), "sibling prefix excluded");
            VerifiedWatcherStopsNormally(false);
            VerifiedWatcherStopsNormally(true);
            StopTimeoutPreservesWatcher();
            AsyncStopDoesNotBlockAndFailureCanRetry();
            FinalStateControlsStopResult("--worker-missing", true);
            FinalStateControlsStopResult("--unknown-error", true);
            FinalStateControlsStopResult("--report-warning", false);
            Console.WriteLine("FrameScopeProcessCleanupTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void HostLifecycleSerializesStartAndExit()
    {
        var type = typeof(FrameScopeNativeMonitor).GetNestedType("HostMonitorLifecycle", BindingFlags.NonPublic);
        AssertTrue(type != null, "shared host lifecycle gate must exist");
        object gate = Activator.CreateInstance(type, true);
        var start = type.GetMethod("Start");
        var stop = type.GetMethod("Stop");
        var exit = type.GetMethod("ExitAsync");
        using (var entered = new ManualResetEvent(false))
        using (var release = new ManualResetEvent(false))
        {
            bool registered = false;
            bool stopped = false;
            var starting = System.Threading.Tasks.Task.Run(delegate
            {
                start.Invoke(gate, new object[] { (Action)delegate { entered.Set(); release.WaitOne(); registered = true; } });
            });
            AssertTrue(entered.WaitOne(2000), "start entered lifecycle gate");
            var clock = Stopwatch.StartNew();
            var exiting = (System.Threading.Tasks.Task)exit.Invoke(gate, new object[] { (Action)delegate
            {
                AssertTrue(registered, "exit waits for in-flight ownership registration"); stopped = true;
            } });
            AssertTrue(clock.ElapsedMilliseconds < 500 && !exiting.IsCompleted, "UI exit never synchronously waits for start lock");
            var rejected = System.Threading.Tasks.Task.Run(delegate
            {
                try { start.Invoke(gate, new object[] { (Action)delegate { throw new Exception("late start executed"); } }); }
                catch (TargetInvocationException ex) { if (ex.InnerException is InvalidOperationException) return; throw; }
                throw new Exception("closing host accepted a new start");
            });
            AssertTrue(rejected.Wait(1000), "closing rejects new start immediately while start holds gate");
            AssertTrue(!stopped, "stop cannot run before registration");
            release.Set();
            AssertTrue(starting.Wait(2000) && exiting.Wait(2000) && stopped, "registered watcher is stopped before exit completes");
        }
        // A separate host scope must remain usable while the first scope is closed.
        object retryGate = Activator.CreateInstance(type, true);
        bool otherStarted = false;
        start.Invoke(retryGate, new object[] { (Action)delegate { otherStarted = true; } });
        AssertTrue(otherStarted, "closing is scoped to this host gate");
        var failed = (System.Threading.Tasks.Task)exit.Invoke(retryGate, new object[] { (Action)delegate { throw new TimeoutException("controlled failure"); } });
        bool propagated = false;
        try { failed.Wait(2000); } catch (AggregateException ex) { propagated = ex.InnerException is TimeoutException; }
        AssertTrue(propagated, "exit failure propagates");
        bool resumed = false;
        start.Invoke(retryGate, new object[] { (Action)delegate { resumed = true; } });
        AssertTrue(resumed, "failed exit reopens start admission");
        using (var stopping = new ManualResetEvent(false))
        using (var releaseStop = new ManualResetEvent(false))
        {
            var priorStop = System.Threading.Tasks.Task.Run(delegate { stop.Invoke(retryGate, new object[] { (Action)delegate { stopping.Set(); releaseStop.WaitOne(); } }); });
            AssertTrue(stopping.WaitOne(2000), "stop entered lifecycle gate");
            bool secondStop = false;
            var retryExit = (System.Threading.Tasks.Task)exit.Invoke(retryGate, new object[] { (Action)delegate { secondStop = true; } });
            AssertTrue(!retryExit.IsCompleted && !secondStop, "exit also serializes with an in-flight stop");
            releaseStop.Set();
            AssertTrue(priorStop.Wait(2000) && retryExit.Wait(2000) && secondStop, "failed exit can retry successfully");
        }
    }

    private static void StopClearsOnlyItsOwnOwnership()
    {
        var type = typeof(FrameScopeNativeMonitor).GetNestedType("WatcherOwnership", BindingFlags.NonPublic);
        var owned = typeof(FrameScopeNativeMonitor).GetField("OwnedWatcher", BindingFlags.Static | BindingFlags.NonPublic);
        var clear = typeof(FrameScopeNativeMonitor).GetMethod("ClearOwnedWatcher", BindingFlags.Static | BindingFlags.NonPublic);
        object oldInstance = Activator.CreateInstance(type, true);
        object newInstance = Activator.CreateInstance(type, true);
        try
        {
            owned.SetValue(null, newInstance);
            clear.Invoke(null, new object[] { oldInstance });
            AssertTrue(object.ReferenceEquals(newInstance, owned.GetValue(null)), "old stop cannot clear another instance ownership");
            clear.Invoke(null, new object[] { newInstance });
            AssertTrue(owned.GetValue(null) == null, "matching instance ownership clears");
        }
        finally { owned.SetValue(null, null); }
    }

    private static void FailedHandshakeRetainsPendingOwnership()
    {
        var monitor = typeof(FrameScopeNativeMonitor);
        var publish = monitor.GetMethod("PublishPendingWatcher", BindingFlags.Static | BindingFlags.NonPublic);
        AssertTrue(publish != null, "launch identity must be retained before handshake");
        var owned = monitor.GetField("OwnedWatcher", BindingFlags.Static | BindingFlags.NonPublic);
        var type = monitor.GetNestedType("HostMonitorLifecycle", BindingFlags.NonPublic);
        object gate = Activator.CreateInstance(type, true);
        try
        {
            try
            {
                type.GetMethod("Start").Invoke(gate, new object[] { (Action)delegate
                {
                    publish.Invoke(null, new object[] { 12345, 678L, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FrameScopeMonitor.exe"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pending-config.json") });
                    throw new InvalidOperationException("controlled publication timeout");
                } });
            }
            catch (TargetInvocationException ex) { AssertTrue(ex.InnerException is InvalidOperationException, "handshake failure propagated"); }
            object pending = owned.GetValue(null);
            AssertTrue(pending != null, "failed handshake retains launch ownership");
            var ownershipType = pending.GetType();
            AssertEqual(12345, (int)ownershipType.GetField("Pid").GetValue(pending), "pending PID retained");
            AssertEqual(678L, (long)ownershipType.GetField("StartTime").GetValue(pending), "pending start identity retained");
            AssertTrue(string.IsNullOrEmpty((string)ownershipType.GetField("InstanceId").GetValue(pending)), "token remains unverified");
            bool rejected = false;
            try { FrameScopeNativeMonitor.StopFrameScopeBackgroundProcessesAndWait(10); }
            catch (InvalidOperationException) { rejected = true; }
            AssertTrue(rejected && object.ReferenceEquals(pending, owned.GetValue(null)), "unpublished identity blocks successful exit and preserves ownership for retry");
        }
        finally { owned.SetValue(null, null); }
    }

    private static void HostExitStopsOwnedOnly()
    {
        var type = typeof(FrameScopeNativeMonitor);
        var exitStop = type.GetMethod("RunHostExitStop", BindingFlags.Static | BindingFlags.NonPublic);
        AssertTrue(exitStop != null, "host exit must distinguish borrowed from owned watcher");
        var owned = type.GetField("OwnedWatcher", BindingFlags.Static | BindingFlags.NonPublic);
        var ownershipType = type.GetNestedType("WatcherOwnership", BindingFlags.NonPublic);
        try
        {
            owned.SetValue(null, null);
            bool stopCalled = false;
            object gate = Activator.CreateInstance(type.GetNestedType("HostMonitorLifecycle", BindingFlags.NonPublic), true);
            var exiting = (System.Threading.Tasks.Task)gate.GetType().GetMethod("ExitAsync").Invoke(gate,
                new object[] { (Action)delegate { exitStop.Invoke(null, new object[] { (Action)delegate { stopCalled = true; } }); } });
            AssertTrue(exiting.Wait(2000) && !stopCalled, "borrowed watcher host exits without invoking owned stop callback");
            owned.SetValue(null, Activator.CreateInstance(ownershipType, true));
            exitStop.Invoke(null, new object[] { (Action)delegate { stopCalled = true; } });
            AssertTrue(stopCalled, "owned watcher exit invokes stop callback");
            type.GetMethod("PublishPendingWatcher", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new object[] { 12345, 678L, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FrameScopeMonitor.exe"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pending-config.json") });
            object pending = owned.GetValue(null);
            bool rejected = false;
            try { FrameScopeNativeMonitor.StopOwnedWatcherForHostExit(); }
            catch (InvalidOperationException) { rejected = true; }
            AssertTrue(rejected && object.ReferenceEquals(pending, owned.GetValue(null)), "pending ownership failure cannot bypass host exit stop");
        }
        finally { owned.SetValue(null, null); }
    }

    private static void UnknownWatcherIsNotKilled()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string childExe = Path.Combine(root, "FrameScopeMonitor.exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, childExe, true);

        Process child = null;
        try
        {
            child = Process.Start(new ProcessStartInfo
            {
                FileName = childExe,
                Arguments = "--watcher --watcher-sleep",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            AssertTrue(child != null, "fake watcher process started");
            WaitFixtureReady(child);

            bool refused = false;
            try { FrameScopeNativeMonitor.StopFrameScopeBackgroundProcessesAndWait(500); }
            catch (InvalidOperationException) { refused = true; }
            AssertTrue(refused, "unowned process rejected");

            AssertTrue(!child.HasExited, "unknown watcher must survive without ownership protocol");
        }
        finally
        {
            try
            {
                if (child != null && !child.HasExited)
                {
                    child.Kill();
                    child.WaitForExit(3000);
                }
            }
            catch { }
            try { if (File.Exists(childExe)) File.Delete(childExe); }
            catch { }
        }
    }

    private static void VerifiedWatcherStopsNormally(bool staleIdentity)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string childExe = Path.Combine(root, "FrameScopeMonitor.exe");
        string statePath = Path.Combine(root, "framescope-watcher-state.json");
        string configPath = Path.Combine(root, "fixture-config.json");
        string token = Guid.NewGuid().ToString("N");
        File.Copy(Assembly.GetExecutingAssembly().Location, childExe, true);
        using (var child = Process.Start(new ProcessStartInfo { FileName = childExe,
            Arguments = "--watcher --watcher-sleep --respond-stop " + token, UseShellExecute = false, CreateNoWindow = true }))
        {
            try
            {
                WaitFixtureReady(child);
                long start = (child.StartTime.ToUniversalTime().Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) / TimeSpan.TicksPerSecond;
                File.WriteAllText(statePath, new JavaScriptSerializer().Serialize(new { WatcherPid = child.Id,
                    WatcherStartTimeUnix = staleIdentity ? start - 1 : start, WatcherInstanceId = token,
                    StopProtocolVersion = 1, ConfigPath = configPath, ActiveMonitors = new object[0], Phase = "idle" }));
                if (staleIdentity)
                {
                    bool refused = false;
                    try { FrameScopeNativeMonitor.StopVerifiedBackground(configPath, 2000); }
                    catch (InvalidOperationException) { refused = true; }
                    AssertTrue(refused && !child.HasExited, "stale start identity rejected without kill");
                }
                else
                {
                    bool wrongConfigRefused = false;
                    try { FrameScopeNativeMonitor.StopVerifiedBackground(configPath + ".other", 50); }
                    catch (InvalidOperationException) { wrongConfigRefused = true; }
                    AssertTrue(wrongConfigRefused && !child.HasExited, "different config rejected");
                    FrameScopeNativeMonitor.RecordOwnedWatcher(child.Id, configPath);
                    var timer = Stopwatch.StartNew();
                    AssertEqual(0, FrameScopeNativeMonitor.StopFrameScopeBackgroundProcessesAndWait(5000), "normal stop completed");
                    AssertTrue(child.HasExited && child.ExitCode == 0, "fixture acknowledged protocol");
                    AssertTrue(timer.ElapsedMilliseconds >= 250, "wait includes report completion");
                    AssertTrue(File.Exists(statePath), "state evidence preserved");
                    AssertEqual(0, FrameScopeNativeMonitor.StopVerifiedBackground(configPath, 500), "repeated stop with completed state is safe");
                }
            }
            finally
            {
                if (!child.HasExited) { child.Kill(); child.WaitForExit(3000); }
                File.Delete(statePath);
                File.Delete(Path.Combine(root, "framescope-watcher-stop-" + token + ".json"));
            }
        }
        File.Delete(childExe);
    }

    private static void StopTimeoutPreservesWatcher()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string childExe = Path.Combine(root, "FrameScopeMonitor.exe");
        string statePath = Path.Combine(root, "framescope-watcher-state.json");
        string token = Guid.NewGuid().ToString("N");
        string config = Path.Combine(root, "timeout-config.json");
        File.Copy(Assembly.GetExecutingAssembly().Location, childExe, true);
        using (var child = Process.Start(new ProcessStartInfo { FileName = childExe,
            Arguments = "--watcher --watcher-sleep", UseShellExecute = false, CreateNoWindow = true }))
        {
            try
            {
                WaitFixtureReady(child);
                long start = (child.StartTime.ToUniversalTime().Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) / TimeSpan.TicksPerSecond;
                File.WriteAllText(statePath, new JavaScriptSerializer().Serialize(new { WatcherPid = child.Id,
                    WatcherStartTimeUnix = start, WatcherInstanceId = token, StopProtocolVersion = 0, ConfigPath = config }));
                bool refused = false;
                try { FrameScopeNativeMonitor.StopVerifiedBackground(config, 50); }
                catch (InvalidOperationException) { refused = true; }
                AssertTrue(refused && !child.HasExited, "legacy protocol rejected");
                File.WriteAllText(statePath, new JavaScriptSerializer().Serialize(new { WatcherPid = child.Id,
                    WatcherStartTimeUnix = start, WatcherInstanceId = token, StopProtocolVersion = 1, ConfigPath = config }));
                bool timedOut = false;
                try { FrameScopeNativeMonitor.StopVerifiedBackground(config, 100); }
                catch (TimeoutException) { timedOut = true; }
                AssertTrue(timedOut && !child.HasExited && File.Exists(statePath), "timeout preserves watcher and state");
            }
            finally
            {
                if (!child.HasExited) { child.Kill(); child.WaitForExit(3000); }
                File.Delete(statePath);
                File.Delete(Path.Combine(root, "framescope-watcher-stop-" + token + ".json"));
            }
        }
        File.Delete(childExe);
    }

    private static void AsyncStopDoesNotBlockAndFailureCanRetry()
    {
        var method = typeof(FrameScopeNativeMonitor).GetMethod("RunOwnedStopAsync", BindingFlags.Static | BindingFlags.NonPublic);
        AssertTrue(method != null, "async shutdown entry point must exist");
        using (var release = new ManualResetEvent(false))
        {
            var clock = Stopwatch.StartNew();
            var task = (System.Threading.Tasks.Task)method.Invoke(null, new object[] { (Action)delegate { release.WaitOne(); } });
            AssertTrue(clock.ElapsedMilliseconds < 500 && !task.IsCompleted, "pending shutdown must return without blocking caller");
            release.Set();
            AssertTrue(task.Wait(2000), "pending shutdown completes after worker releases");
        }
        var failed = (System.Threading.Tasks.Task)method.Invoke(null, new object[] { (Action)delegate { throw new TimeoutException("fixture shutdown timeout"); } });
        bool propagated = false;
        try { failed.Wait(2000); }
        catch (AggregateException ex) { propagated = ex.InnerException is TimeoutException; }
        AssertTrue(propagated, "shutdown timeout must propagate to UI completion path");
        bool retried = false;
        var retry = (System.Threading.Tasks.Task)method.Invoke(null, new object[] { (Action)delegate { retried = true; } });
        AssertTrue(retry.Wait(2000) && retried, "failed shutdown permits a later successful retry");
    }

    private static void FinalStateControlsStopResult(string fixtureFlag, bool expectFailure)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string childExe = Path.Combine(root, "FrameScopeMonitor.exe");
        string statePath = Path.Combine(root, "framescope-watcher-state.json");
        string config = Path.Combine(root, "final-state-config.json");
        string token = Guid.NewGuid().ToString("N");
        File.Copy(Assembly.GetExecutingAssembly().Location, childExe, true);
        using (var child = Process.Start(new ProcessStartInfo { FileName = childExe,
            Arguments = "--watcher --watcher-sleep --respond-stop " + fixtureFlag + " " + token, UseShellExecute = false, CreateNoWindow = true }))
        {
            try
            {
                WaitFixtureReady(child);
                long start = (child.StartTime.ToUniversalTime().Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) / TimeSpan.TicksPerSecond;
                File.WriteAllText(statePath, new JavaScriptSerializer().Serialize(new { WatcherPid = child.Id,
                    WatcherStartTimeUnix = start, WatcherInstanceId = token, StopProtocolVersion = 1, ConfigPath = config,
                    Phase = "idle", ActiveMonitors = new object[0] }));
                bool failed = false;
                try { FrameScopeNativeMonitor.StopVerifiedBackground(config, 5000); }
                catch (InvalidOperationException) { failed = true; }
                AssertEqual(expectFailure, failed, "final state governs result " + fixtureFlag);
                AssertTrue(child.HasExited && File.Exists(statePath), "final failure evidence retained");
                if (!expectFailure)
                {
                    var property = typeof(FrameScopeNativeMonitor).GetProperty("LastStopWarning", BindingFlags.Static | BindingFlags.NonPublic);
                    AssertTrue(property != null && Convert.ToString(property.GetValue(null, null)) == "report-finalization-failed", "report failure is surfaced as warning");
                }
                else
                {
                    failed = false;
                    try { FrameScopeNativeMonitor.StopVerifiedBackground(config, 500); }
                    catch (InvalidOperationException) { failed = true; }
                    AssertTrue(failed, "repeated CLI must retain worker failure");
                }
            }
            finally
            {
                if (!child.HasExited) { child.Kill(); child.WaitForExit(3000); }
                File.Delete(statePath);
                File.Delete(Path.Combine(root, "framescope-watcher-stop-" + token + ".json"));
            }
        }
        File.Delete(childExe);
    }

    private static void WaitFixtureReady(Process child)
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixture-ready-" + child.Id);
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(path) && !child.HasExited && DateTime.UtcNow < deadline) Thread.Sleep(10);
        AssertTrue(File.Exists(path), "fixture process initialized");
        File.Delete(path);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
    }
}

internal static partial class FrameScopeNativeMonitor
{
    private static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string StatePath = Path.Combine(Root, "framescope-watcher-state.json");
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
}
