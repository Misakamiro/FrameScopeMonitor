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
            string cpuCoreCsv = Arg(args, "--cpu-core-csv", "cpu-core-samples.csv");
            string cpuCoreStatus = Arg(args, "--cpu-core-status", "");
            bool cpuCoreEnabled = ParseBool(Arg(args, "--enable-cpu-core-telemetry", "false"), false);
            int cpuCoreIntervalMs = ParseInt(Arg(args, "--cpu-core-interval", "1000"), 1000);
            string cpuVoltageCsv = Arg(args, "--cpu-voltage-csv", "cpu-voltage-samples.csv");
            string cpuVoltageStatus = Arg(args, "--cpu-voltage-status", "");
            bool cpuVoltageEnabled = ParseBool(Arg(args, "--enable-cpu-voltage-telemetry", "false"), false);
            int cpuVoltageIntervalMs = ParseInt(Arg(args, "--cpu-voltage-interval", "1000"), 1000);
            string cpuVoltageProvider = Arg(args, "--cpu-voltage-provider", "auto");
            string cpuVidCsv = Arg(args, "--cpu-vid-csv", "cpu-vid-samples.csv");
            string cpuVidStatus = Arg(args, "--cpu-vid-status", "");
            bool cpuVidEnabled = ParseBool(Arg(args, "--enable-cpu-vid-telemetry", cpuVoltageEnabled ? "true" : "false"), cpuVoltageEnabled);
            int cpuVidIntervalMs = ParseInt(Arg(args, "--cpu-vid-interval", cpuVoltageIntervalMs.ToString(CultureInfo.InvariantCulture)), cpuVoltageIntervalMs);
            string cpuVidProvider = Arg(args, "--cpu-vid-provider", cpuVoltageProvider);
            string nvidiaSmi = Arg(args, "--nvidia-smi", "");

            EnsureParent(systemCsv);
            if (String.IsNullOrWhiteSpace(cpuCoreStatus))
            {
                cpuCoreStatus = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cpuCoreCsv)) ?? "", "cpu-core-telemetry-status.json");
            }
            if (String.IsNullOrWhiteSpace(cpuVoltageStatus))
            {
                cpuVoltageStatus = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cpuVoltageCsv)) ?? "", "cpu-voltage-telemetry-status.json");
            }
            if (String.IsNullOrWhiteSpace(cpuVidStatus))
            {
                cpuVidStatus = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cpuVidCsv)) ?? "", "cpu-vid-telemetry-status.json");
            }
            if (cpuCoreIntervalMs <= 0) cpuCoreIntervalMs = 1000;
            if (cpuCoreIntervalMs < 500) cpuCoreIntervalMs = 500;
            if (cpuVoltageIntervalMs <= 0) cpuVoltageIntervalMs = 1000;
            if (cpuVoltageIntervalMs < 500) cpuVoltageIntervalMs = 500;
            if (cpuVidIntervalMs <= 0) cpuVidIntervalMs = 1000;
            if (cpuVidIntervalMs < 500) cpuVidIntervalMs = 500;
            int loopIntervalMs = intervalMs;
            if (cpuCoreEnabled) loopIntervalMs = Math.Min(loopIntervalMs, cpuCoreIntervalMs);
            if (cpuVoltageEnabled) loopIntervalMs = Math.Min(loopIntervalMs, cpuVoltageIntervalMs);
            if (cpuVidEnabled) loopIntervalMs = Math.Min(loopIntervalMs, cpuVidIntervalMs);
            if (loopIntervalMs < 500) loopIntervalMs = 500;

            ICpuVoltageTelemetryProvider cpuVoltageProviderInstance;
            ICpuVidTelemetryProvider cpuVidProviderInstance;
            CreateCpuHardwareTelemetryProviders(
                cpuVoltageEnabled,
                cpuVoltageProvider,
                cpuVidEnabled,
                cpuVidProvider,
                out cpuVoltageProviderInstance,
                out cpuVidProviderInstance);

            using (PerfCounters counters = CreateCounters())
            using (CpuCoreTelemetrySession cpuCore = CreateCpuCoreTelemetrySession(new CpuCoreTelemetryOptions
            {
                Enabled = cpuCoreEnabled,
                CsvPath = cpuCoreCsv,
                StatusPath = cpuCoreStatus,
                SampleIntervalMs = cpuCoreIntervalMs
            }, cpuCoreEnabled ? (ICpuCoreTelemetryProvider)CreateCpuCoreCounterSet() : new StaticCpuCoreTelemetryProvider(new CpuCoreCounterSample[0], "")))
            using (CpuVoltageTelemetrySession cpuVoltage = CreateCpuVoltageTelemetrySession(new CpuVoltageTelemetryOptions
            {
                Enabled = cpuVoltageEnabled,
                CsvPath = cpuVoltageCsv,
                StatusPath = cpuVoltageStatus,
                SampleIntervalMs = cpuVoltageIntervalMs,
                Provider = cpuVoltageProvider
            }, cpuVoltageProviderInstance))
            using (CpuVidTelemetrySession cpuVid = CreateCpuVidTelemetrySession(new CpuVidTelemetryOptions
            {
                Enabled = cpuVidEnabled,
                CsvPath = cpuVidCsv,
                StatusPath = cpuVidStatus,
                SampleIntervalMs = cpuVidIntervalMs,
                Provider = cpuVidProvider
            }, cpuVidProviderInstance))
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

                RunLoop(target, intervalMs, loopIntervalMs, parentPid, nvidiaSmi, counters, writer, cpuCore, cpuVoltage, cpuVid);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void RunLoop(string target, int intervalMs, int loopIntervalMs, int parentPid, string nvidiaSmi, PerfCounters counters, StreamWriter writer, CpuCoreTelemetrySession cpuCore, CpuVoltageTelemetrySession cpuVoltage, CpuVidTelemetrySession cpuVid)
    {
        int sampleIndex = 0;
        int loopIndex = 0;
        long nextSystemDueElapsedMs = 0;
        Stopwatch elapsed = Stopwatch.StartNew();

        while (true)
        {
            if (parentPid > 0 && !IsProcessRunning(parentPid)) break;

            Stopwatch loop = Stopwatch.StartNew();
            long elapsedMs = elapsed.ElapsedMilliseconds;
            SystemProcessSnapshot processSnapshot = SnapshotSystemProcesses(target);
            if (!processSnapshot.TargetRunning) break;

            if (elapsedMs >= nextSystemDueElapsedMs)
            {
                nextSystemDueElapsedMs = elapsedMs + intervalMs;
                GpuSnapshot gpu = QueryGpu(nvidiaSmi);
                WriteCsv(writer, new object[]
                {
                    DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                    sampleIndex,
                    processSnapshot.Cs2Running,
                    processSnapshot.TargetRunning,
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
                    processSnapshot.ProcessCount
                });

                if (sampleIndex % 5 == 0) writer.Flush();
                sampleIndex++;
            }

            if (cpuCore != null) cpuCore.TryWriteSample(loopIndex, elapsedMs);
            if (cpuVoltage != null) cpuVoltage.TryWriteSample(loopIndex, elapsedMs);
            if (cpuVid != null) cpuVid.TryWriteSample(loopIndex, elapsedMs);
            loopIndex++;

            int sleepMs = Math.Max(1, loopIntervalMs - (int)loop.ElapsedMilliseconds);
            Thread.Sleep(sleepMs);
        }

        writer.Flush();
    }

}
