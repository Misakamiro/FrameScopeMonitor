using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

// Root-parameterized stop protocol. Never launches or terminates a process.
internal static class FrameScopeWatcherShutdown
{
    internal class Ownership
    {
        public int Pid;
        public long StartTime;
        public string ExecutablePath;
        public string ConfigPath;
        public string InstanceId;
        public int ReportCount;
    }

    internal sealed class Identity
    {
        public int Pid;
        public long StartTime;
        public string ExecutablePath;
        public string CommandLine;
    }

    internal static long StartUnix(Process process)
    {
        return (process.StartTime.ToUniversalTime().Ticks - 621355968000000000L) / TimeSpan.TicksPerSecond;
    }

    internal static Identity Inspect(int pid)
    {
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return null; }
        using (process)
        {
            if (process.HasExited) return null;
            try { return new Identity { Pid = pid, StartTime = StartUnix(process), ExecutablePath = process.MainModule.FileName }; }
            catch { if (process.HasExited || process.WaitForExit(100)) return null; throw; }
        }
    }

    internal static bool SamePath(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
            string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    internal static void RequirePlainPath(string path)
    {
        string full = Path.GetFullPath(path);
        for (string current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Reparse executable/state/config path is not supported: " + current);
    }

    internal static bool SameIdentity(Identity left, Identity right)
    {
        return left != null && right != null && left.Pid == right.Pid && left.StartTime == right.StartTime && SamePath(left.ExecutablePath, right.ExecutablePath);
    }

    internal static Dictionary<string, object> ReadState(string statePath)
    {
        RequirePlainPath(statePath);
        return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(statePath));
    }

    internal static Ownership Read(string root, string statePath, string configPath, int expectedPid, Func<int, Identity> inspect)
    {
        if (!File.Exists(statePath)) return null;
        var state = ReadState(statePath);
        int pid = Convert.ToInt32(state["WatcherPid"]);
        if (pid <= 0 || (expectedPid > 0 && pid != expectedPid)) throw new InvalidOperationException("Watcher PID does not match the launched instance.");
        if (!state.ContainsKey("StopProtocolVersion") || Convert.ToInt32(state["StopProtocolVersion"]) != 1)
            throw new InvalidOperationException("Watcher stop protocol is unknown; refusing forced termination.");
        string token = Convert.ToString(state["WatcherInstanceId"]);
        Guid parsed;
        if (!Guid.TryParseExact(token, "N", out parsed)) throw new InvalidOperationException("Watcher instance token is invalid.");
        string configured = Path.GetFullPath(Convert.ToString(state["ConfigPath"]));
        RequirePlainPath(configured);
        RequirePlainPath(Path.Combine(root, "FrameScopeMonitor.exe"));
        if (!SamePath(configured, configPath)) throw new InvalidOperationException("Watcher config identity does not match.");
        var ownership = new Ownership { Pid = pid, StartTime = Convert.ToInt64(state["WatcherStartTimeUnix"]),
            ExecutablePath = Path.Combine(Path.GetFullPath(root), "FrameScopeMonitor.exe"), ConfigPath = configured, InstanceId = token };
        var process = inspect(pid);
        if (process == null) { ValidateStopped(statePath, ownership); return ownership; }
        if (!SameIdentity(process, ToIdentity(ownership))) throw new InvalidOperationException("Watcher executable or start time does not match.");
        object value;
        IList active;
        ownership.ReportCount = state.TryGetValue("ActiveMonitors", out value) && (active = value as IList) != null ? active.Count : 0;
        if (state.TryGetValue("GeneratingRunDir", out value) && !string.IsNullOrWhiteSpace(Convert.ToString(value))) ownership.ReportCount++;
        return ownership;
    }

    internal static Identity ToIdentity(Ownership ownership)
    {
        return new Identity { Pid = ownership.Pid, StartTime = ownership.StartTime, ExecutablePath = ownership.ExecutablePath };
    }

    internal static int Budget(int reportCount) { return checked(45000 + 120000 * reportCount); }

    internal static string Stop(string root, string statePath, Ownership ownership, int waitMs,
        Func<int, Identity> inspect, Func<Identity, int, bool> wait)
    {
        var process = inspect(ownership.Pid);
        if (process == null) return ValidateStopped(statePath, ownership);
        if (!SameIdentity(process, ToIdentity(ownership))) throw new InvalidOperationException("Watcher identity changed; refusing stop.");
        var current = Read(root, statePath, ownership.ConfigPath, ownership.Pid, inspect);
        if (current == null || current.StartTime != ownership.StartTime || current.InstanceId != ownership.InstanceId ||
            !SamePath(current.ExecutablePath, ownership.ExecutablePath)) throw new InvalidOperationException("Watcher ownership changed; refusing stop.");
        var request = new { Action = "stop", WatcherPid = ownership.Pid, WatcherStartTimeUnix = ownership.StartTime,
            WatcherInstanceId = ownership.InstanceId, ConfigPath = ownership.ConfigPath };
        FrameScopeJsonFile.Write(Path.Combine(root, "framescope-watcher-stop-" + ownership.InstanceId + ".json"), new JavaScriptSerializer().Serialize(request));
        int budget = waitMs > 0 ? waitMs : Budget(Math.Max(ownership.ReportCount, current.ReportCount));
        if (!wait(ToIdentity(ownership), budget)) throw new TimeoutException("Watcher did not finish worker/report shutdown within " + budget + " ms; state was preserved.");
        if (inspect(ownership.Pid) != null) throw new InvalidOperationException("Watcher process exit was not confirmed.");
        return ValidateStopped(statePath, ownership);
    }

    internal static string ValidateStopped(string statePath, Ownership ownership)
    {
        var state = ReadState(statePath);
        object value;
        IList active = state.TryGetValue("ActiveMonitors", out value) ? value as IList : null;
        if (Convert.ToInt32(state["WatcherPid"]) != ownership.Pid || Convert.ToInt64(state["WatcherStartTimeUnix"]) != ownership.StartTime ||
            Convert.ToString(state["WatcherInstanceId"]) != ownership.InstanceId || !SamePath(Convert.ToString(state["ConfigPath"]), ownership.ConfigPath) ||
            Convert.ToString(state["Phase"]) != "stopped" || active == null || active.Count != 0)
            throw new InvalidOperationException("Watcher exited without a matching stopped state and empty active workers; evidence was preserved.");
        string code = state.TryGetValue("StopErrorCode", out value) ? Convert.ToString(value) : "";
        string error = state.TryGetValue("StopError", out value) ? Convert.ToString(value) : "";
        if (code == "report-finalization-failed") return code;
        if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(error)) throw new InvalidOperationException("Watcher shutdown failed: " + code + " " + error);
        return "";
    }

    internal static bool Wait(Identity identity, int milliseconds)
    {
        Process process;
        try { process = Process.GetProcessById(identity.Pid); }
        catch (ArgumentException) { return true; }
        using (process)
        {
            if (process.HasExited) return true;
            if (!SameIdentity(Inspect(identity.Pid), identity)) throw new InvalidOperationException("Process identity changed before waiting.");
            return process.WaitForExit(milliseconds);
        }
    }
}
