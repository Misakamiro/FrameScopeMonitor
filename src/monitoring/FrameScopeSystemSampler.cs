using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

internal static partial class FrameScopeSystemSampler
{
    private static int Main(string[] args)
    {
        try
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle; }
            catch { }

            string target = BaseName(Arg(args, "--target", "cs2"));
            int intervalMs = ParseInt(Arg(args, "--interval", "1000"), 1000);
            if (intervalMs < 500) intervalMs = 500;
            int parentPid = ParseInt(Arg(args, "--parent-pid", "0"), 0);
            string systemCsv = Arg(args, "--system-csv", "system-samples.csv");
            string nvidiaSmi = Arg(args, "--nvidia-smi", "");

            EnsureParent(systemCsv);

            using (PerfCounters counters = CreateCounters())
            using (StreamWriter writer = Writer(systemCsv))
            {
                WriteCsv(writer, new object[]
                {
                    "Time",
                    "SampleIndex",
                    "Cs2Running",
                    "TargetRunning",
                    "TotalCpuPct",
                    "CpuFrequencyMHz",
                    "CpuPerformancePct",
                    "AvailableMB",
                    "DiskAvgSecPerTransfer",
                    "DiskBytesPerSec",
                    "NetBytesPerSec",
                    "GpuUtilPct",
                    "GpuMemUtilPct",
                    "GpuTempC",
                    "GpuPState",
                    "GpuClockMHz",
                    "MemClockMHz",
                    "PowerW",
                    "VramUsedMiB",
                    "VramTotalMiB",
                    "ProcessCount"
                });

                RunLoop(target, intervalMs, parentPid, nvidiaSmi, counters, writer);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void RunLoop(string target, int intervalMs, int parentPid, string nvidiaSmi, PerfCounters counters, StreamWriter writer)
    {
        int sampleIndex = 0;

        while (true)
        {
            if (parentPid > 0 && !IsProcessRunning(parentPid)) break;

            Stopwatch loop = Stopwatch.StartNew();
            bool targetRunning = ProcessNameRunning(target);
            if (!targetRunning) break;

            GpuSnapshot gpu = QueryGpu(nvidiaSmi);
            WriteCsv(writer, new object[]
            {
                DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                sampleIndex,
                ProcessNameRunning("cs2"),
                targetRunning,
                Round(NextValue(counters.TotalCpu), 2),
                Round(NextValue(counters.CpuFrequency), 0),
                Round(NextValue(counters.CpuPerformance), 2),
                Round(NextValue(counters.AvailableMemory), 1),
                NextValue(counters.DiskLatency),
                NextValue(counters.DiskBytes),
                SumNetwork(counters.NetworkBytes),
                Round(gpu == null ? null : gpu.GpuUtilPct, 2),
                Round(gpu == null ? null : gpu.GpuMemUtilPct, 2),
                Round(gpu == null ? null : gpu.GpuTempC, 1),
                gpu == null ? "" : gpu.GpuPState,
                Round(gpu == null ? null : gpu.GpuClockMHz, 0),
                Round(gpu == null ? null : gpu.MemClockMHz, 0),
                Round(gpu == null ? null : gpu.PowerW, 2),
                Round(gpu == null ? null : gpu.VramUsedMiB, 1),
                Round(gpu == null ? null : gpu.VramTotalMiB, 1),
                CountProcesses()
            });

            if (sampleIndex % 5 == 0) writer.Flush();
            sampleIndex++;

            int sleepMs = Math.Max(1, intervalMs - (int)loop.ElapsedMilliseconds);
            Thread.Sleep(sleepMs);
        }

        writer.Flush();
    }

}
