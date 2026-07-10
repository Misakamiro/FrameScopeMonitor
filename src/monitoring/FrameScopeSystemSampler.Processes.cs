using System;
using System.Collections.Generic;
using System.Diagnostics;

internal static partial class FrameScopeSystemSampler
{
    private sealed class SystemProcessSnapshot
    {
        public bool TargetRunning;
        public bool Cs2Running;
        public int ProcessCount;
    }

    private static SystemProcessSnapshot SnapshotSystemProcesses(List<string> targetProcessNames)
    {
        SystemProcessSnapshot snapshot = new SystemProcessSnapshot();
        Process[] processes = null;
        try
        {
            processes = Process.GetProcesses();
            snapshot.ProcessCount = processes.Length;
            foreach (Process process in processes)
            {
                string name = "";
                try { name = process.ProcessName; }
                catch { }
                if (FrameScopeTargetLifecycle.MatchesAnyAlias(name, targetProcessNames)) snapshot.TargetRunning = true;
                if (String.Equals(name, "cs2", System.StringComparison.OrdinalIgnoreCase)) snapshot.Cs2Running = true;
            }
        }
        catch
        {
            snapshot.ProcessCount = 0;
        }
        finally
        {
            if (processes != null)
            {
                foreach (Process process in processes) process.Dispose();
            }
        }
        return snapshot;
    }

    private static int CountProcesses()
    {
        Process[] processes = null;
        try
        {
            processes = Process.GetProcesses();
            return processes.Length;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (processes != null)
            {
                foreach (Process process in processes) process.Dispose();
            }
        }
    }

    private static bool ProcessNameRunning(string processName)
    {
        Process[] processes = null;
        try
        {
            processes = Process.GetProcessesByName(processName);
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
                foreach (Process process in processes) process.Dispose();
            }
        }
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using (Process process = Process.GetProcessById(pid))
            {
                return !process.HasExited;
            }
        }
        catch
        {
            return false;
        }
    }
}
