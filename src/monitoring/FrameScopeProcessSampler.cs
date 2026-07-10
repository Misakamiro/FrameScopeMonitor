using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

internal static partial class FrameScopeProcessSampler
{
    private static int Main(string[] args)
    {
        try
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle; }
            catch { }

            List<string> targetAliases = ResolveTargetAliases(args);
            int intervalMs = ParseInt(Arg(args, "--interval", "100"), 100);
            if (intervalMs < 100) intervalMs = 100;
            int parentPid = ParseInt(Arg(args, "--parent-pid", "0"), 0);
            string stopFile = Arg(args, "--stop-file", "");

            string processCsv = Arg(args, "--process-csv", "process-samples.csv");
            string topCpuCsv = Arg(args, "--top-cpu-csv", "topcpu-samples.csv");
            string topIoCsv = Arg(args, "--top-io-csv", "topio-samples.csv");
            string alertsCsv = Arg(args, "--alerts-csv", "sample-alerts.csv");

            EnsureParent(processCsv);
            EnsureParent(topCpuCsv);
            EnsureParent(topIoCsv);
            EnsureParent(alertsCsv);

            using (StreamWriter processWriter = Writer(processCsv))
            using (StreamWriter topWriter = Writer(topCpuCsv))
            using (StreamWriter topIoWriter = Writer(topIoCsv))
            using (StreamWriter alertsWriter = Writer(alertsCsv))
            {
                WriteCsv(processWriter, new object[] { "Time", "SampleIndex", "ElapsedMs", "ProcessName", "Count", "CpuPct", "WorkingSetMB", "ReadMBps", "WriteMBps", "Priorities", "Pids" });
                WriteCsv(topWriter, new object[] { "Time", "SampleIndex", "ElapsedMs", "ProcessName", "Id", "CpuPct", "WorkingSetMB" });
                WriteCsv(topIoWriter, new object[] { "Time", "SampleIndex", "ElapsedMs", "ProcessName", "Id", "CpuPct", "ReadMBps", "WriteMBps", "WorkingSetMB" });
                WriteCsv(alertsWriter, new object[] { "Time", "SampleIndex", "ElapsedMs", "Alerts", "TotalCpuPct", "GpuUtilPct", "GpuClockMHz", "GpuTempC", "AvailableMB", "DiskLatencySec", "TopCpuProcess", "TopCpuPct", "TopIoProcess", "TopIoReadMBps", "TopIoWriteMBps" });

                RunLoop(targetAliases, intervalMs, parentPid, stopFile, processWriter, topWriter, topIoWriter, alertsWriter);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void RunLoop(List<string> targetAliases, int intervalMs, int parentPid, string stopFile, StreamWriter processWriter, StreamWriter topWriter, StreamWriter topIoWriter, StreamWriter alertsWriter)
    {
        Dictionary<int, double> prevCpu = new Dictionary<int, double>();
        Dictionary<int, ulong> prevRead = new Dictionary<int, ulong>();
        Dictionary<int, ulong> prevWrite = new Dictionary<int, ulong>();
        DateTime start = DateTime.Now;
        DateTime prevTime = start;
        int sampleIndex = 0;
        int processorCount = Math.Max(1, Environment.ProcessorCount);

        while (true)
        {
            bool parentOwned = parentPid > 0;
            bool parentRunning = !parentOwned || IsProcessRunning(parentPid);
            if (ShouldStopSampling(parentOwned, parentRunning, true, StopRequested(stopFile))) break;

            Stopwatch loop = Stopwatch.StartNew();
            DateTime now = DateTime.Now;
            double elapsedSeconds = Math.Max(0.001, (now - prevTime).TotalSeconds);
            double elapsedMs = Math.Max(0.0, (now - start).TotalMilliseconds);

            Process[] processes = Process.GetProcesses();
            bool targetRunning = false;
            List<ProcRow> rows = new List<ProcRow>(processes.Length);
            HashSet<int> seenPids = new HashSet<int>();

            foreach (Process process in processes)
            {
                try
                {
                    string name = process.ProcessName;
                    int pid = process.Id;
                    seenPids.Add(pid);
                    if (FrameScopeTargetLifecycle.MatchesAnyAlias(name, targetAliases)) targetRunning = true;

                    double? cpuPct = null;
                    double cpuNow = process.TotalProcessorTime.TotalSeconds;
                    double cpuPrev;
                    if (prevCpu.TryGetValue(pid, out cpuPrev))
                    {
                        double delta = cpuNow - cpuPrev;
                        if (delta >= 0) cpuPct = Math.Max(0.0, (delta / elapsedSeconds / processorCount) * 100.0);
                    }
                    prevCpu[pid] = cpuNow;

                    double workingSet = 0.0;
                    try { workingSet = process.WorkingSet64; }
                    catch { }

                    double? readMBps = null;
                    double? writeMBps = null;
                    IoCounters io;
                    if (TryGetIoCounters(process, out io))
                    {
                        ulong readPrev;
                        ulong writePrev;
                        if (prevRead.TryGetValue(pid, out readPrev) && io.ReadTransferCount >= readPrev)
                        {
                            readMBps = ((double)(io.ReadTransferCount - readPrev) / elapsedSeconds) / 1048576.0;
                        }
                        if (prevWrite.TryGetValue(pid, out writePrev) && io.WriteTransferCount >= writePrev)
                        {
                            writeMBps = ((double)(io.WriteTransferCount - writePrev) / elapsedSeconds) / 1048576.0;
                        }
                        prevRead[pid] = io.ReadTransferCount;
                        prevWrite[pid] = io.WriteTransferCount;
                    }

                    rows.Add(new ProcRow
                    {
                        Id = pid,
                        ProcessName = name,
                        CpuPct = cpuPct,
                        WorkingSet = workingSet,
                        ReadMBps = readMBps,
                        WriteMBps = writeMBps
                    });
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            Prune(prevCpu, seenPids);
            Prune(prevRead, seenPids);
            Prune(prevWrite, seenPids);

            if (ShouldStopSampling(parentOwned, parentRunning, targetRunning, StopRequested(stopFile))) break;

            string nowText = now.ToString("o", CultureInfo.InvariantCulture);
            WriteGroupedRows(processWriter, rows, nowText, sampleIndex, elapsedMs);
            List<ProcRow> topCpu = TopCpuRows(rows, 20);
            foreach (ProcRow row in topCpu)
            {
                WriteCsv(topWriter, new object[] { nowText, sampleIndex, Round(elapsedMs, 1), row.ProcessName, row.Id, Round(row.CpuPct, 2), Round(row.WorkingSet / 1048576.0, 1) });
            }

            List<ProcRow> topIo = TopIoRows(rows, 20);
            foreach (ProcRow row in topIo)
            {
                WriteCsv(topIoWriter, new object[] { nowText, sampleIndex, Round(elapsedMs, 1), row.ProcessName, row.Id, Round(row.CpuPct, 2), Round(row.ReadMBps, 3), Round(row.WriteMBps, 3), Round(row.WorkingSet / 1048576.0, 1) });
            }

            WriteAlerts(alertsWriter, rows, topCpu, topIo, targetAliases, nowText, sampleIndex, elapsedMs);

            if (sampleIndex % 10 == 0)
            {
                processWriter.Flush();
                topWriter.Flush();
                topIoWriter.Flush();
                alertsWriter.Flush();
            }

            sampleIndex++;
            prevTime = now;
            int sleepMs = Math.Max(1, intervalMs - (int)loop.ElapsedMilliseconds);
            Thread.Sleep(sleepMs);
        }
    }

    private static void WriteGroupedRows(StreamWriter writer, List<ProcRow> rows, string nowText, int sampleIndex, double elapsedMs)
    {
        Dictionary<string, GroupStats> groups = new Dictionary<string, GroupStats>(StringComparer.OrdinalIgnoreCase);
        foreach (ProcRow row in rows)
        {
            if (String.IsNullOrEmpty(row.ProcessName) || StringComparer.OrdinalIgnoreCase.Equals(row.ProcessName, "Idle")) continue;
            GroupStats stats;
            if (!groups.TryGetValue(row.ProcessName, out stats))
            {
                stats = new GroupStats();
                groups[row.ProcessName] = stats;
            }
            stats.Count++;
            stats.WorkingSet += row.WorkingSet;
            if (row.CpuPct.HasValue)
            {
                stats.CpuPct += row.CpuPct.Value;
                stats.HasCpu = true;
            }
            if (row.ReadMBps.HasValue) stats.ReadMBps += row.ReadMBps.Value;
            if (row.WriteMBps.HasValue) stats.WriteMBps += row.WriteMBps.Value;
            stats.Pids.Add(row.Id);
        }

        foreach (KeyValuePair<string, GroupStats> item in groups)
        {
            GroupStats stats = item.Value;
            WriteCsv(writer, new object[]
            {
                nowText,
                sampleIndex,
                Round(elapsedMs, 1),
                item.Key,
                stats.Count,
                stats.HasCpu ? (object)Round(stats.CpuPct, 2) : "",
                Round(stats.WorkingSet / 1048576.0, 1),
                Round(stats.ReadMBps, 3),
                Round(stats.WriteMBps, 3),
                "",
                String.Join(";", stats.Pids)
            });
        }
    }

    private static void WriteAlerts(StreamWriter writer, List<ProcRow> allRows, List<ProcRow> topCpu, List<ProcRow> topIo, List<string> targetAliases, string nowText, int sampleIndex, double elapsedMs)
    {
        double totalCpu = 0.0;
        foreach (ProcRow row in allRows) totalCpu += Value(row.CpuPct);
        if (totalCpu > 100.0) totalCpu = 100.0;

        ProcRow topCpuRow = topCpu.FirstOrDefault();
        ProcRow topIoRow = topIo.FirstOrDefault();
        List<string> alerts = new List<string>();
        if (totalCpu > 85.0) alerts.Add("high-total-cpu");
        if (topCpuRow != null && !FrameScopeTargetLifecycle.MatchesAnyAlias(topCpuRow.ProcessName, targetAliases) && Value(topCpuRow.CpuPct) > 25.0) alerts.Add("background-cpu-spike");
        if (topIoRow != null && (Value(topIoRow.ReadMBps) + Value(topIoRow.WriteMBps)) > 100.0) alerts.Add("heavy-process-io");
        if (alerts.Count == 0) return;

        WriteCsv(writer, new object[]
        {
            nowText,
            sampleIndex,
            Round(elapsedMs, 1),
            String.Join(";", alerts),
            Round(totalCpu, 2),
            "",
            "",
            "",
            "",
            "",
            topCpuRow == null ? "" : topCpuRow.ProcessName,
            topCpuRow == null ? "" : (object)Round(topCpuRow.CpuPct, 2),
            topIoRow == null ? "" : topIoRow.ProcessName,
            topIoRow == null ? "" : (object)Round(topIoRow.ReadMBps, 3),
            topIoRow == null ? "" : (object)Round(topIoRow.WriteMBps, 3)
        });
    }

}
