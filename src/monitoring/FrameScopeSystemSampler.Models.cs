using System;
using System.Collections.Generic;
using System.Diagnostics;

internal static partial class FrameScopeSystemSampler
{
    private sealed class GpuSnapshot
    {
        public double? GpuUtilPct;
        public double? GpuMemUtilPct;
        public double? GpuTempC;
        public string GpuPState;
        public double? GpuClockMHz;
        public double? MemClockMHz;
        public double? PowerW;
        public double? VramUsedMiB;
        public double? VramTotalMiB;
    }

    private sealed class PerfCounters : IDisposable
    {
        public PerformanceCounter TotalCpu;
        public PerformanceCounter CpuFrequency;
        public PerformanceCounter CpuPerformance;
        public PerformanceCounter AvailableMemory;
        public PerformanceCounter DiskLatency;
        public PerformanceCounter DiskBytes;
        public readonly List<PerformanceCounter> NetworkBytes = new List<PerformanceCounter>();

        public void Dispose()
        {
            DisposeCounter(TotalCpu);
            DisposeCounter(CpuFrequency);
            DisposeCounter(CpuPerformance);
            DisposeCounter(AvailableMemory);
            DisposeCounter(DiskLatency);
            DisposeCounter(DiskBytes);
            foreach (PerformanceCounter counter in NetworkBytes) DisposeCounter(counter);
        }

        private static void DisposeCounter(PerformanceCounter counter)
        {
            if (counter == null) return;
            try { counter.Dispose(); }
            catch { }
        }
    }
}
