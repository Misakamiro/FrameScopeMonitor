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
}
