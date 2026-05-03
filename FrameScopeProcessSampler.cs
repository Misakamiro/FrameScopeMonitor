using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class FrameScopeProcessSampler
{
    private sealed class ProcRow
    {
        public int Id;
        public string ProcessName;
        public double? CpuPct;
        public double WorkingSet;
        public double? ReadMBps;
        public double? WriteMBps;
    }

    private sealed class GroupStats
    {
        public int Count;
        public double CpuPct;
        public bool HasCpu;
        public double WorkingSet;
        public double ReadMBps;
        public double WriteMBps;
        public readonly List<int> Pids = new List<int>();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr processHandle, out IoCounters counters);

    private static int Main(string[] args)
    {
        try
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle; }
            catch { }

            string target = BaseName(Arg(args, "--target", "cs2"));
            int intervalMs = ParseInt(Arg(args, "--interval", "100"), 100);
            if (intervalMs < 100) intervalMs = 100;
            int parentPid = ParseInt(Arg(args, "--parent-pid", "0"), 0);

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

                RunLoop(target, intervalMs, parentPid, processWriter, topWriter, topIoWriter, alertsWriter);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void RunLoop(string target, int intervalMs, int parentPid, StreamWriter processWriter, StreamWriter topWriter, StreamWriter topIoWriter, StreamWriter alertsWriter)
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
            if (parentPid > 0 && !IsProcessRunning(parentPid)) break;

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
                    if (StringComparer.OrdinalIgnoreCase.Equals(name, target)) targetRunning = true;

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

            if (!targetRunning) break;

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

            WriteAlerts(alertsWriter, rows, topCpu, topIo, target, nowText, sampleIndex, elapsedMs);

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

    private static void WriteAlerts(StreamWriter writer, List<ProcRow> allRows, List<ProcRow> topCpu, List<ProcRow> topIo, string target, string nowText, int sampleIndex, double elapsedMs)
    {
        double totalCpu = 0.0;
        foreach (ProcRow row in allRows) totalCpu += Value(row.CpuPct);
        if (totalCpu > 100.0) totalCpu = 100.0;

        ProcRow topCpuRow = topCpu.FirstOrDefault();
        ProcRow topIoRow = topIo.FirstOrDefault();
        List<string> alerts = new List<string>();
        if (totalCpu > 85.0) alerts.Add("high-total-cpu");
        if (topCpuRow != null && !StringComparer.OrdinalIgnoreCase.Equals(topCpuRow.ProcessName, target) && Value(topCpuRow.CpuPct) > 25.0) alerts.Add("background-cpu-spike");
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

    private static string Arg(string[] args, string name, string fallback)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(args[i], name) && i + 1 < args.Length) return args[i + 1];
        }
        return fallback;
    }

    private static int ParseInt(string text, int fallback)
    {
        int value;
        if (Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return value;
        return fallback;
    }

    private static string BaseName(string processName)
    {
        if (String.IsNullOrWhiteSpace(processName)) return "cs2";
        try { return Path.GetFileNameWithoutExtension(processName); }
        catch { return processName; }
    }

    private static void EnsureParent(string path)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    private static StreamWriter Writer(string path)
    {
        FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024);
        StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 1024 * 1024);
        writer.NewLine = "\r\n";
        return writer;
    }

    private static void WriteCsv(StreamWriter writer, IEnumerable<object> values)
    {
        writer.WriteLine(String.Join(",", values.Select(Csv)));
    }

    private static string Csv(object value)
    {
        if (value == null) return "";
        string text;
        IFormattable formattable = value as IFormattable;
        if (formattable != null) text = formattable.ToString(null, CultureInfo.InvariantCulture);
        else text = value.ToString();
        if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0) return "\"" + text.Replace("\"", "\"\"") + "\"";
        return text;
    }

    private static object Round(double? value, int digits)
    {
        if (!value.HasValue) return "";
        return Math.Round(value.Value, digits);
    }

    private static double Round(double value, int digits)
    {
        return Math.Round(value, digits);
    }

    private static double Value(double? value)
    {
        return value.HasValue ? value.Value : 0.0;
    }
}
