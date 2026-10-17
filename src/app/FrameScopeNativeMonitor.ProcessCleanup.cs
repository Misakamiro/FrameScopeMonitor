using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;

internal static partial class FrameScopeNativeMonitor
{
    private static bool IsWatcherRunning(out int pid)
    {
        pid = 0;
        if (!File.Exists(StatePath)) return false;
        try
        {
            var state = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath));
            if (!state.ContainsKey("WatcherPid")) return false;
            pid = Convert.ToInt32(state["WatcherPid"]);
            if (pid <= 0) return false;
            using (var process = Process.GetProcessById(pid))
            {
                object start;
                if (!state.TryGetValue("WatcherStartTimeUnix", out start) || ProcessStartUnix(process) != Convert.ToInt64(start) ||
                    !string.Equals(process.MainModule.FileName, Path.Combine(Root, "FrameScopeMonitor.exe"), StringComparison.OrdinalIgnoreCase))
                {
                    pid = 0;
                    return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasFrameScopeBackgroundProcesses()
    {
        return EnumerateFrameScopeBackgroundPids().Count > 0;
    }

    internal static bool HasActiveFrameScopeMonitoring()
    {
        if (WatcherStateHasActiveMonitor()) return true;
        foreach (var info in ReadProcessInfo())
        {
            if (IsActiveMonitoringProcess(info)) return true;
        }
        return false;
    }

    internal static void StopFrameScopeBackgroundProcesses()
    {
        StopFrameScopeBackgroundProcessesAndWait(3000);
    }

    private sealed class WatcherOwnership : FrameScopeWatcherShutdown.Ownership { }

    internal sealed class HostMonitorLifecycle
    {
        private readonly object gate = new object();
        private int closing;

        public void Start(Action start)
        {
            if (Volatile.Read(ref closing) != 0) throw new InvalidOperationException("Host is closing; new monitor starts are disabled.");
            lock (gate)
            {
                if (Volatile.Read(ref closing) != 0) throw new InvalidOperationException("Host is closing; new monitor starts are disabled.");
                start();
            }
        }

        public void Stop(Action stop)
        {
            lock (gate) stop();
        }

        public System.Threading.Tasks.Task ExitAsync(Action stop)
        {
            if (Interlocked.CompareExchange(ref closing, 1, 0) != 0)
                throw new InvalidOperationException("Host exit is already in progress.");
            return System.Threading.Tasks.Task.Run(delegate
            {
                try { lock (gate) stop(); }
                catch { Interlocked.Exchange(ref closing, 0); throw; }
            });
        }
    }

    private static readonly HostMonitorLifecycle MonitorLifecycle = new HostMonitorLifecycle();
    private static WatcherOwnership OwnedWatcher;
    internal static string LastStopWarning { get; private set; }

    internal static System.Threading.Tasks.Task RunOwnedStopAsync(Action stop)
    {
        return System.Threading.Tasks.Task.Run(stop);
    }

    internal static System.Threading.Tasks.Task RunOwnedExitAsync(Action stop)
    {
        return MonitorLifecycle.ExitAsync(stop);
    }

    internal static void StopOwnedWatcherForHostExit()
    {
        RunHostExitStop(delegate { StopOwnedWatcher(OwnedWatcher, 0); });
    }

    internal static void RunHostExitStop(Action stopOwned)
    {
        MonitorLifecycle.Stop(delegate
        {
            LastStopWarning = "";
            if (OwnedWatcher != null) stopOwned();
        });
    }

    internal static bool IsPathWithinRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        string boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(boundary, StringComparison.OrdinalIgnoreCase);
    }

    private static long ProcessStartUnix(Process process)
    {
        return (process.StartTime.ToUniversalTime().Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) / TimeSpan.TicksPerSecond;
    }

    private static WatcherOwnership ReadWatcherOwnership(string configPath, int expectedPid)
    {
        var value = FrameScopeWatcherShutdown.Read(Root, StatePath, configPath, expectedPid, FrameScopeWatcherShutdown.Inspect);
        if (value == null) return null;
        return new WatcherOwnership { Pid = value.Pid, StartTime = value.StartTime, ExecutablePath = value.ExecutablePath,
            ConfigPath = value.ConfigPath, InstanceId = value.InstanceId, ReportCount = value.ReportCount };
    }
    internal static void RecordOwnedWatcher(int pid, string configPath)
    {
        using (var process = Process.GetProcessById(pid)) RecordOwnedWatcher(process, configPath);
    }

    internal static void RecordOwnedWatcher(Process launchedProcess, string configPath)
    {
        MonitorLifecycle.Stop(delegate { RecordOwnedWatcherCore(launchedProcess, configPath); });
    }

    private static void RecordOwnedWatcherCore(Process launchedProcess, string configPath)
    {
        int pid = launchedProcess.Id;
        long launchedStart = ProcessStartUnix(launchedProcess);
        string launchedExecutable = launchedProcess.MainModule.FileName;
        PublishPendingWatcher(pid, launchedStart, launchedExecutable, configPath);
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var ownership = ReadWatcherOwnership(configPath, pid);
                if (ownership != null && ownership.StartTime == launchedStart &&
                    string.Equals(ownership.ExecutablePath, launchedExecutable, StringComparison.OrdinalIgnoreCase))
                { OwnedWatcher = ownership; return; }
            }
            catch { }
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("Watcher ownership was not published within 15 seconds; process was not terminated.");
    }

    private static void PublishPendingWatcher(int pid, long startTime, string executablePath, string configPath)
    {
        OwnedWatcher = new WatcherOwnership { Pid = pid, StartTime = startTime,
            ExecutablePath = executablePath, ConfigPath = Path.GetFullPath(configPath), InstanceId = "" };
    }

    internal static void StopFrameScopeBackgroundProcesses(bool includeHardwareService)
    {
        StopFrameScopeBackgroundProcessesAndWait(0, includeHardwareService);
    }

    internal static int StopFrameScopeBackgroundProcessesAndWait(int waitMs)
    {
        return StopFrameScopeBackgroundProcessesAndWait(waitMs, false);
    }

    internal static int StopFrameScopeBackgroundProcessesAndWait(int waitMs, bool includeHardwareService)
    {
        int result = 0;
        MonitorLifecycle.Stop(delegate { result = StopOwnedWatcher(OwnedWatcher, waitMs); });
        return result;
    }

    internal static int StopVerifiedBackground(string configPath, int waitMs)
    {
        int result = 0;
        MonitorLifecycle.Stop(delegate { result = StopOwnedWatcher(ReadWatcherOwnership(configPath, 0), waitMs); });
        return result;
    }

    private static int StopOwnedWatcher(WatcherOwnership ownership, int waitMs)
    {
        LastStopWarning = "";
        if (ownership == null)
        {
            if (HasFrameScopeBackgroundProcesses()) throw new InvalidOperationException("Background processes are not owned by this UI; refusing forced termination.");
            return 0;
        }
        if (string.IsNullOrEmpty(ownership.InstanceId))
        {
            var verified = ReadWatcherOwnership(ownership.ConfigPath, ownership.Pid);
            if (verified == null || verified.StartTime != ownership.StartTime ||
                !string.Equals(verified.ExecutablePath, ownership.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Launched watcher ownership is not yet verified; exit was cancelled and identity preserved for retry.");
            if (object.ReferenceEquals(OwnedWatcher, ownership)) OwnedWatcher = verified;
            ownership = verified;
        }
        LastStopWarning = FrameScopeWatcherShutdown.Stop(Root, StatePath, ownership, waitMs,
            FrameScopeWatcherShutdown.Inspect, FrameScopeWatcherShutdown.Wait);
        ClearOwnedWatcher(ownership);
        return 0;
    }
    private static void ClearOwnedWatcher(WatcherOwnership ownership)
    {
        if (object.ReferenceEquals(OwnedWatcher, ownership)) OwnedWatcher = null;
    }

    private static void ValidateStoppedWatcher(WatcherOwnership ownership)
    {
        LastStopWarning = FrameScopeWatcherShutdown.ValidateStopped(StatePath, ownership);
    }
    private static HashSet<int> EnumerateFrameScopeBackgroundPids()
    {
        return EnumerateFrameScopeBackgroundPids(false);
    }

    private static HashSet<int> EnumerateFrameScopeBackgroundPids(bool includeHardwareService)
    {
        var result = new HashSet<int>();
        foreach (var info in ReadProcessInfo())
        {
            if (info.ProcessId <= 0 || info.ProcessId == Process.GetCurrentProcess().Id) continue;
            var name = info.Name ?? "";
            var commandLine = info.CommandLine ?? "";
            var exePath = info.ExecutablePath ?? "";
            var commandLower = commandLine.ToLowerInvariant();

            if (name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) &&
                (commandLower.Contains("framescopewatcher.ps1") ||
                 commandLower.Contains("monitor-cs2-highfreq.ps1")) &&
                (commandLower.Contains("\"" + Path.Combine(Root, "FrameScopeWatcher.ps1").ToLowerInvariant() + "\"") ||
                 commandLower.Contains("\"" + Path.Combine(Root, "monitor-cs2-highfreq.ps1").ToLowerInvariant() + "\"")))
            {
                result.Add(info.ProcessId);
                continue;
            }

            if (name.Equals("FrameScopeMonitor.exe", StringComparison.OrdinalIgnoreCase) &&
                (commandLower.Contains("--watcher") || commandLower.Contains("--monitor-session")) &&
                IsPathWithinRoot(exePath, Root))
            {
                result.Add(info.ProcessId);
                continue;
            }

            if ((name.Equals("FrameScopeProcessSampler.exe", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("FrameScopeSystemSampler.exe", StringComparison.OrdinalIgnoreCase)) &&
                IsPathWithinRoot(exePath, Root))
            {
                result.Add(info.ProcessId);
                continue;
            }

            if (name.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase) &&
                IsPathWithinRoot(exePath, Path.Combine(Root, "tools")))
            {
                result.Add(info.ProcessId);
                continue;
            }

            if (includeHardwareService &&
                (name.Equals("FrameScopeHardwareService.exe", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("FrameScopeHardwareProvider.exe", StringComparison.OrdinalIgnoreCase)) &&
                IsPathWithinRoot(exePath, Root))
            {
                result.Add(info.ProcessId);
            }
        }
        return result;
    }

    private static bool WatcherStateHasActiveMonitor()
    {
        if (!File.Exists(StatePath)) return false;
        try
        {
            var state = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath));
            if (state == null) return false;

            object phaseValue;
            if (state.TryGetValue("Phase", out phaseValue) &&
                string.Equals(Convert.ToString(phaseValue), "monitoring", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            object activeValue;
            if (state.TryGetValue("ActiveMonitors", out activeValue))
            {
                IList active = activeValue as IList;
                if (active != null && active.Count > 0) return true;
            }
        }
        catch { }
        return false;
    }

    private static bool IsActiveMonitoringProcess(ProcessInfo info)
    {
        if (info == null || info.ProcessId <= 0 || info.ProcessId == Process.GetCurrentProcess().Id) return false;

        var name = info.Name ?? "";
        var commandLower = (info.CommandLine ?? "").ToLowerInvariant();

        if (name.Equals("FrameScopeMonitor.exe", StringComparison.OrdinalIgnoreCase) &&
            commandLower.Contains("--monitor-session") &&
            IsPathWithinRoot(info.ExecutablePath, Root))
        {
            return true;
        }

        if ((name.Equals("FrameScopeProcessSampler.exe", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("FrameScopeSystemSampler.exe", StringComparison.OrdinalIgnoreCase)) &&
            IsPathWithinRoot(info.ExecutablePath, Root))
        {
            return true;
        }

        return name.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase) &&
            IsPathWithinRoot(info.ExecutablePath, Path.Combine(Root, "tools"));
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

    internal static int WaitForFrameScopeBackgroundProcessesToExit(int waitMs)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(1, waitMs));
        int remaining = 0;
        do
        {
            remaining = EnumerateFrameScopeBackgroundPids().Count;
            if (remaining == 0) return 0;
            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        return EnumerateFrameScopeBackgroundPids().Count;
    }
}
