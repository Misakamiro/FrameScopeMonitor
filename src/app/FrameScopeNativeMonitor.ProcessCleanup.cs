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

    internal static int StopFrameScopeBackgroundProcessesAndWait(int waitMs)
    {
        var pids = EnumerateFrameScopeBackgroundPids();
        if (pids.Count == 0) return 0;

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

        return WaitForFrameScopeBackgroundProcessesToExit(waitMs);
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

        var rootLower = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        var name = info.Name ?? "";
        var commandLower = (info.CommandLine ?? "").ToLowerInvariant();
        var exeLower = (info.ExecutablePath ?? "").ToLowerInvariant();

        if (name.Equals("FrameScopeMonitor.exe", StringComparison.OrdinalIgnoreCase) &&
            commandLower.Contains("--monitor-session") &&
            exeLower.StartsWith(rootLower, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ((name.Equals("FrameScopeProcessSampler.exe", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("FrameScopeSystemSampler.exe", StringComparison.OrdinalIgnoreCase)) &&
            exeLower.StartsWith(rootLower, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase) &&
            exeLower.StartsWith(Path.Combine(rootLower, "tools"), StringComparison.OrdinalIgnoreCase);
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
