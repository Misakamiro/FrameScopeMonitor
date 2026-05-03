using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

internal static class FrameScopeSystemSampler
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

    private static GpuSnapshot QueryGpu(string nvidiaSmi)
    {
        if (String.IsNullOrWhiteSpace(nvidiaSmi) || !File.Exists(nvidiaSmi)) return null;

        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = nvidiaSmi,
                Arguments = "--query-gpu=utilization.gpu,utilization.memory,temperature.gpu,pstate,clocks.gr,clocks.mem,power.draw,memory.used,memory.total --format=csv,noheader,nounits",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(psi))
            {
                if (process == null) return null;
                string line = process.StandardOutput.ReadLine();
                if (!process.WaitForExit(2500))
                {
                    try { process.Kill(); }
                    catch { }
                    return null;
                }
                if (String.IsNullOrWhiteSpace(line)) return null;
                string[] parts = line.Split(',').Select(part => part.Trim()).ToArray();
                if (parts.Length < 9) return null;

                return new GpuSnapshot
                {
                    GpuUtilPct = ParseDouble(parts[0]),
                    GpuMemUtilPct = ParseDouble(parts[1]),
                    GpuTempC = ParseDouble(parts[2]),
                    GpuPState = parts[3],
                    GpuClockMHz = ParseDouble(parts[4]),
                    MemClockMHz = ParseDouble(parts[5]),
                    PowerW = ParseDouble(parts[6]),
                    VramUsedMiB = ParseDouble(parts[7]),
                    VramTotalMiB = ParseDouble(parts[8])
                };
            }
        }
        catch
        {
            return null;
        }
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

    private static double? ParseDouble(string text)
    {
        double value;
        if (Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
        return null;
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
}
