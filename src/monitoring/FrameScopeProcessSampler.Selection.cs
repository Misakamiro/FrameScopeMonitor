using System;
using System.Collections.Generic;
using System.Diagnostics;

internal static partial class FrameScopeProcessSampler
{
    private static bool TryGetIoCounters(Process process, out IoCounters counters)
    {
        counters = new IoCounters();
        try
        {
            return GetProcessIoCounters(process.Handle, out counters);
        }
        catch
        {
            return false;
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

    private static List<ProcRow> TopCpuRows(List<ProcRow> rows, int limit)
    {
        List<ProcRow> top = new List<ProcRow>(limit);
        foreach (ProcRow row in rows)
        {
            if (!row.CpuPct.HasValue || StringComparer.OrdinalIgnoreCase.Equals(row.ProcessName, "Idle")) continue;
            double score = row.CpuPct.Value;
            int index = 0;
            while (index < top.Count && Value(top[index].CpuPct) >= score) index++;
            if (index >= limit) continue;
            top.Insert(index, row);
            if (top.Count > limit) top.RemoveAt(top.Count - 1);
        }
        return top;
    }

    private static List<ProcRow> TopIoRows(List<ProcRow> rows, int limit)
    {
        List<ProcRow> top = new List<ProcRow>(limit);
        foreach (ProcRow row in rows)
        {
            double score = Value(row.ReadMBps) + Value(row.WriteMBps);
            if (score <= 0.01) continue;
            int index = 0;
            while (index < top.Count && (Value(top[index].ReadMBps) + Value(top[index].WriteMBps)) >= score) index++;
            if (index >= limit) continue;
            top.Insert(index, row);
            if (top.Count > limit) top.RemoveAt(top.Count - 1);
        }
        return top;
    }

    private static void Prune<T>(Dictionary<int, T> values, HashSet<int> seenPids)
    {
        List<int> remove = null;
        foreach (int pid in values.Keys)
        {
            if (!seenPids.Contains(pid))
            {
                if (remove == null) remove = new List<int>();
                remove.Add(pid);
            }
        }
        if (remove == null) return;
        foreach (int pid in remove) values.Remove(pid);
    }
}
