using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

internal static partial class FrameScopeSystemSampler
{
    private static PerfCounters CreateCounters()
    {
        PerfCounters counters = new PerfCounters();
        string driveInstance = Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\');

        counters.TotalCpu = Counter("Processor", "% Processor Time", "_Total");
        counters.CpuFrequency = Counter("Processor Information", "Processor Frequency", "_Total");
        counters.CpuPerformance = Counter("Processor Information", "% Processor Performance", "_Total");
        counters.AvailableMemory = Counter("Memory", "Available MBytes", null);
        counters.DiskLatency = Counter("LogicalDisk", "Avg. Disk sec/Transfer", driveInstance);
        counters.DiskBytes = Counter("LogicalDisk", "Disk Bytes/sec", driveInstance);

        try
        {
            PerformanceCounterCategory category = new PerformanceCounterCategory("Network Interface");
            foreach (string instance in category.GetInstanceNames())
            {
                if (String.IsNullOrWhiteSpace(instance)) continue;
                string lower = instance.ToLowerInvariant();
                if (lower.Contains("loopback") || lower.Contains("isatap") || lower.Contains("teredo")) continue;
                PerformanceCounter counter = Counter("Network Interface", "Bytes Total/sec", instance);
                if (counter != null) counters.NetworkBytes.Add(counter);
            }
        }
        catch { }

        Prime(counters.TotalCpu);
        Prime(counters.CpuFrequency);
        Prime(counters.CpuPerformance);
        Prime(counters.AvailableMemory);
        Prime(counters.DiskLatency);
        Prime(counters.DiskBytes);
        foreach (PerformanceCounter counter in counters.NetworkBytes) Prime(counter);

        return counters;
    }

    private static PerformanceCounter Counter(string category, string name, string instance)
    {
        try
        {
            if (String.IsNullOrEmpty(instance)) return new PerformanceCounter(category, name, true);
            return new PerformanceCounter(category, name, instance, true);
        }
        catch
        {
            return null;
        }
    }

    private static void Prime(PerformanceCounter counter)
    {
        if (counter == null) return;
        try { counter.NextValue(); }
        catch { }
    }

    private static double? NextValue(PerformanceCounter counter)
    {
        if (counter == null) return null;
        try { return counter.NextValue(); }
        catch { return null; }
    }

    private static double? SumNetwork(IEnumerable<PerformanceCounter> counters)
    {
        double total = 0.0;
        bool found = false;
        foreach (PerformanceCounter counter in counters)
        {
            double? value = NextValue(counter);
            if (!value.HasValue) continue;
            total += value.Value;
            found = true;
        }
        return found ? (double?)total : null;
    }
}
