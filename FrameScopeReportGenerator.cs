using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Web.Script.Serialization;

internal static class FrameScopeReportGenerator
{
    private const string Brand = "FrameScope";
    private static readonly string[] Colors = new[]
    {
        "#29e6ff", "#a9ff47", "#ffd35b", "#ff5d7d", "#65a7ff", "#d77cff",
        "#45ff9a", "#ff9f43", "#70e1f5", "#f8f871", "#38ef7d", "#ff6bcb",
        "#a4b0be", "#ffa502", "#2ed573", "#ff7675", "#7bed9f", "#5352ed"
    };

    private sealed class PresentRecord
    {
        public int RowIndex;
        public DateTime Time;
        public double FrameMs;
        public string Application = "";
        public string ProcessId = "";
        public string SwapChain = "";
        public string PresentMode = "";
        public string AllowsTearing = "";
    }

    private sealed class PresentTrack
    {
        public string ProcessId = "";
        public string SwapChain = "";
        public string Application = "";
        public int Rows;
        public int HardwareRows;
        public int AllowsTearingRows;
        public int ArtifactRowsOver1000ms;
        public double? MedianFps;
        public double? P99FrameMs;
        public double Score;
        public List<PresentRecord> Records = new List<PresentRecord>();

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>
            {
                { "processId", ProcessId },
                { "swapChain", SwapChain },
                { "application", Application },
                { "rows", Rows },
                { "hardwareRows", HardwareRows },
                { "allowsTearingRows", AllowsTearingRows },
                { "artifactRowsOver1000ms", ArtifactRowsOver1000ms },
                { "medianFps", Round(MedianFps, 2) },
                { "p99FrameMs", Round(P99FrameMs, 3) },
                { "score", Round(Score, 3) }
            };
        }
    }

    private sealed class PresentReadResult
    {
        public List<KeyValuePair<DateTime, double>> Frames = new List<KeyValuePair<DateTime, double>>();
        public Dictionary<string, object> Diagnostics = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SystemRow
    {
        public DateTime Time;
        public double? Cpu;
        public double? CpuFrequency;
        public double? CpuPerformancePct;
        public double? AvailableMb;
        public double? DiskAvgSecPerTransfer;
        public double? DiskBytesPerSec;
        public double? NetBytesPerSec;
        public double? Gpu;
        public double? GpuMem;
        public double? GpuTemp;
        public double? GpuClock;
        public double? MemClock;
        public double? Power;
        public double? VramUsedMiB;
        public double? VramTotalMiB;
    }

    private sealed class ProcessMatrixResult
    {
        public List<double> Times = new List<double>();
        public List<string> Names = new List<string>();
        public List<List<double?>> Cpu = new List<List<double?>>();
        public List<List<double?>> Mem = new List<List<double?>>();
        public List<Dictionary<string, object>> Stats = new List<Dictionary<string, object>>();
    }

    private sealed class ProcessStat
    {
        public string Name = "";
        public double MaxCpu;
        public double CpuSum;
        public int CpuSamples;
        public double MaxMem;
        public int Samples;
    }

    private sealed class Fenwick
    {
        private readonly int[] tree;
        public int Count { get; private set; }

        public Fenwick(int size)
        {
            tree = new int[size + 2];
        }

        public void Add(int index, int delta)
        {
            if (index < 1) index = 1;
            if (index >= tree.Length) index = tree.Length - 1;
            Count += delta;
            for (int i = index; i < tree.Length; i += i & -i) tree[i] += delta;
        }

        public int FindByRank(int rank)
        {
            int idx = 0;
            int bit = 1;
            while ((bit << 1) < tree.Length) bit <<= 1;
            for (; bit != 0; bit >>= 1)
            {
                int next = idx + bit;
                if (next < tree.Length && tree[next] < rank)
                {
                    idx = next;
                    rank -= tree[next];
                }
            }
            return Math.Min(idx + 1, tree.Length - 1);
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            string runDir = args != null && args.Length > 0 ? Path.GetFullPath(args[0]) : FindLatestRun(Directory.GetCurrentDirectory());
            Generate(runDir);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void Generate(string runDir)
    {
        Directory.CreateDirectory(Path.Combine(runDir, "charts"));

        PresentReadResult present = ReadPresentMon(Path.Combine(runDir, "presentmon.csv"));
        List<SystemRow> systemRows = ReadSystem(Path.Combine(runDir, "system-samples.csv"));
        int timeShiftHours;
        List<KeyValuePair<DateTime, double>> frames = AlignPresentMonTime(present.Frames, systemRows, out timeShiftHours);

        DateTime start;
        DateTime end;
        if (frames.Count > 0)
        {
            start = frames[0].Key;
            end = frames[frames.Count - 1].Key;
        }
        else if (systemRows.Count > 0)
        {
            start = systemRows[0].Time;
            end = systemRows[systemRows.Count - 1].Time;
        }
        else
        {
            start = end = DateTime.Now;
        }

        int durationSeconds = Math.Max(1, (int)Math.Round((end - start).TotalSeconds));
        Dictionary<string, object> hardware = LoadHardware();
        Dictionary<string, string> metadata = LoadRunMetadata(runDir);
        string targetProcess = metadata.ContainsKey("targetProcess") ? metadata["targetProcess"] : "cs2.exe";

        double? totalMemoryMb = GetDoubleFromHardware(hardware, "TotalMemoryMB");
        List<double> availableValues = systemRows.Where(r => r.AvailableMb.HasValue).Select(r => r.AvailableMb.Value).ToList();
        if (!totalMemoryMb.HasValue && availableValues.Count > 0) totalMemoryMb = availableValues.Max();
        double? totalMemoryGb = totalMemoryMb.HasValue ? totalMemoryMb.Value / 1024.0 : (double?)null;

        ProcessMatrixResult process = ReadProcessMatrix(Path.Combine(runDir, "process-samples.csv"), start, targetProcess);
        Dictionary<string, object> systemSeries = SeriesFromSystem(systemRows, start, totalMemoryMb);

        List<double> frameMs = frames.Select(f => f.Value).ToList();
        double? p99 = PercentileHigh(frameMs, 0.99);
        double? p999 = PercentileHigh(frameMs, 0.999);
        Dictionary<string, object> frameStats = new Dictionary<string, object>
        {
            { "average", frameMs.Count > 0 ? Round(1000.0 / frameMs.Average(), 2) : null },
            { "low1", p99.HasValue ? Round(1000.0 / p99.Value, 2) : null },
            { "low01", p999.HasValue ? Round(1000.0 / p999.Value, 2) : null },
            { "minInstant", frameMs.Count > 0 ? Round(1000.0 / frameMs.Max(), 3) : null },
            { "maxFrameMs", frameMs.Count > 0 ? Round(frameMs.Max(), 3) : null },
            { "framesOver20", frameMs.Count(ms => ms > 20.0) },
            { "framesOver33", frameMs.Count(ms => ms > 33.3) },
            { "framesOver100", frameMs.Count(ms => ms > 100.0) }
        };

        List<double> vramTotalValues = systemRows.Where(r => r.VramTotalMiB.HasValue).Select(r => r.VramTotalMiB.Value / 1024.0).ToList();
        double? vramTotalGb = vramTotalValues.Count > 0 ? vramTotalValues.Max() : (double?)null;
        List<double> vramUsedValues = systemRows.Where(r => r.VramUsedMiB.HasValue).Select(r => r.VramUsedMiB.Value / 1024.0).ToList();
        List<double> cpuValues = systemRows.Where(r => r.Cpu.HasValue).Select(r => r.Cpu.Value).ToList();
        List<double> gpuValues = systemRows.Where(r => r.Gpu.HasValue).Select(r => r.Gpu.Value).ToList();
        List<double> gpuTempValues = systemRows.Where(r => r.GpuTemp.HasValue).Select(r => r.GpuTemp.Value).ToList();
        List<double> gpuClockValues = systemRows.Where(r => r.GpuClock.HasValue).Select(r => r.GpuClock.Value).ToList();
        List<double> powerValues = systemRows.Where(r => r.Power.HasValue).Select(r => r.Power.Value).ToList();
        double? availableAvgGb = availableValues.Count > 0 ? availableValues.Average() / 1024.0 : (double?)null;
        double? memUsedAvgGb = totalMemoryGb.HasValue && availableAvgGb.HasValue ? totalMemoryGb.Value - availableAvgGb.Value : (double?)null;
        double? memUsedPctAvg = totalMemoryGb.HasValue && memUsedAvgGb.HasValue ? memUsedAvgGb.Value / totalMemoryGb.Value * 100.0 : (double?)null;
        double? vramUsedAvg = vramUsedValues.Count > 0 ? vramUsedValues.Average() : (double?)null;
        double? vramUsedPctAvg = vramTotalGb.HasValue && vramUsedAvg.HasValue ? vramUsedAvg.Value / vramTotalGb.Value * 100.0 : (double?)null;
        Dictionary<string, object> systemStats = new Dictionary<string, object>
        {
            { "cpuAvg", Round(AverageOrNull(cpuValues), 2) },
            { "cpuMax", Round(MaxOrNull(cpuValues), 2) },
            { "gpuAvg", Round(AverageOrNull(gpuValues), 2) },
            { "gpuTempAvg", Round(AverageOrNull(gpuTempValues), 2) },
            { "gpuClockAvg", Round(AverageOrNull(gpuClockValues), 0) },
            { "powerAvg", Round(AverageOrNull(powerValues), 2) },
            { "vramUsedAvg", Round(vramUsedAvg, 2) },
            { "vramUsedPctAvg", Round(vramUsedPctAvg, 2) },
            { "memUsedAvgGb", Round(memUsedAvgGb, 2) },
            { "memUsedPctAvg", Round(memUsedPctAvg, 2) }
        };

        Dictionary<string, object> fps = BucketFps(frames, start, 0.1, 2.0);
        Dictionary<string, object> notes = new Dictionary<string, object>
        {
            { "frameDataCaptured", frames.Count > 0 },
            { "cpuFrequencyCaptured", ListHasValue(((Dictionary<string, object>)((Dictionary<string, object>)systemSeries["perf"]))["cpuFreq"]) },
            { "presentMonSelectionMode", present.Diagnostics.ContainsKey("selectionMode") ? present.Diagnostics["selectionMode"] : null },
            { "generator", "native-csharp" }
        };

        Dictionary<string, object> data = new Dictionary<string, object>
        {
            { "brand", Brand },
            { "colors", Colors },
            { "run", new Dictionary<string, object>
                {
                    { "dir", runDir },
                    { "startLabel", start.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "endLabel", end.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "durationLabel", FormatDuration(durationSeconds) },
                    { "timeShiftHours", timeShiftHours }
                }
            },
            { "target", new Dictionary<string, object> { { "processName", targetProcess }, { "displayName", targetProcess } } },
            { "hardware", hardware },
            { "hardwareDerived", new Dictionary<string, object> { { "totalMemoryGb", Round(totalMemoryGb, 2) }, { "vramTotalGb", Round(vramTotalGb, 2) } } },
            { "counts", new Dictionary<string, object> { { "frames", frames.Count }, { "hasFrameData", frames.Count > 0 }, { "processSamples", process.Times.Count }, { "processes", process.Names.Count }, { "systemSamples", systemRows.Count } } },
            { "presentMon", present.Diagnostics },
            { "frameStats", frameStats },
            { "systemStats", systemStats },
            { "fps", fps },
            { "system", systemSeries },
            { "process", new Dictionary<string, object> { { "t", process.Times }, { "names", process.Names }, { "cpu", process.Cpu }, { "mem", process.Mem }, { "stats", process.Stats } } },
            { "notes", notes }
        };

        string chartsDir = Path.Combine(runDir, "charts");
        string dataPath = Path.Combine(chartsDir, "framescope-interactive-data.js");
        string htmlPath = Path.Combine(chartsDir, "framescope-interactive-report.html");
        string manifestPath = Path.Combine(chartsDir, "framescope-interactive-manifest.json");

        JavaScriptSerializer serializer = new JavaScriptSerializer();
        serializer.MaxJsonLength = int.MaxValue;
        serializer.RecursionLimit = 256;

        File.WriteAllText(dataPath, "window.FRAMESCOPE_DATA = " + serializer.Serialize(data) + ";" + Environment.NewLine, new UTF8Encoding(false));
        File.WriteAllText(htmlPath, MakeHtml(), new UTF8Encoding(false));

        Dictionary<string, object> manifest = new Dictionary<string, object>
        {
            { "report", htmlPath },
            { "data", dataPath },
            { "frames", frames.Count },
            { "rawPresentMonRows", GetDiagnostic(present.Diagnostics, "rawRows") },
            { "validPresentMonRows", GetDiagnostic(present.Diagnostics, "validRows") },
            { "presentMonSelectionMode", GetDiagnostic(present.Diagnostics, "selectionMode") },
            { "presentMonSelectedTrack", GetDiagnostic(present.Diagnostics, "selectedTrack") },
            { "hasFrameData", frames.Count > 0 },
            { "reportKind", frames.Count > 0 ? "full" : "diagnostic" },
            { "processes", process.Names.Count },
            { "processSamples", process.Times.Count },
            { "systemSamples", systemRows.Count },
            { "cpuFrequencyCaptured", notes["cpuFrequencyCaptured"] },
            { "generator", "native-csharp" }
        };
        string manifestJson = serializer.Serialize(manifest);
        File.WriteAllText(manifestPath, manifestJson, new UTF8Encoding(false));
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Console.WriteLine(manifestJson);
    }

    private static object GetDiagnostic(Dictionary<string, object> map, string key)
    {
        return map.ContainsKey(key) ? map[key] : null;
    }

    private static string FindLatestRun(string baseDir)
    {
        string runs = Path.Combine(baseDir, "cs2-monitor-runs");
        if (!Directory.Exists(runs)) throw new DirectoryNotFoundException(runs);
        DirectoryInfo latest = new DirectoryInfo(runs).GetDirectories()
            .Where(d => File.Exists(Path.Combine(d.FullName, "presentmon.csv")))
            .OrderByDescending(d => d.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latest == null) throw new FileNotFoundException("No monitor runs found in " + runs);
        return latest.FullName;
    }

    private static PresentReadResult ReadPresentMon(string path)
    {
        PresentReadResult result = new PresentReadResult();
        int totalRows = 0;
        int invalidRows = 0;
        int outOfRangeRows = 0;
        if (!File.Exists(path))
        {
            result.Diagnostics["rawRows"] = 0;
            result.Diagnostics["validRows"] = 0;
            result.Diagnostics["selectedRows"] = 0;
            result.Diagnostics["selectionMode"] = "missing";
            return result;
        }

        List<PresentRecord> records = new List<PresentRecord>();
        using (CsvTable table = CsvTable.Open(path))
        {
            Dictionary<string, int> h = table.Headers;
            List<string> row;
            while ((row = table.ReadRow()) != null)
            {
                totalRows++;
                DateTime t;
                double? ms = ParseNullableDouble(Get(row, h, "MsBetweenPresents"));
                if (!TryParseDate(Get(row, h, "TimeInDateTime"), out t) || !ms.HasValue)
                {
                    invalidRows++;
                    continue;
                }
                if (!(ms.Value > 0 && ms.Value < 10000))
                {
                    outOfRangeRows++;
                    continue;
                }
                records.Add(new PresentRecord
                {
                    RowIndex = totalRows - 1,
                    Time = t,
                    FrameMs = ms.Value,
                    Application = Get(row, h, "Application").Trim(),
                    ProcessId = Get(row, h, "ProcessID").Trim(),
                    SwapChain = Get(row, h, "SwapChainAddress").Trim(),
                    PresentMode = Get(row, h, "PresentMode").Trim(),
                    AllowsTearing = Get(row, h, "AllowsTearing").Trim()
                });
            }
        }

        SelectPresentMonFrames(records, result);
        result.Diagnostics["rawRows"] = totalRows;
        result.Diagnostics["validRows"] = records.Count;
        result.Diagnostics["invalidRows"] = invalidRows;
        result.Diagnostics["outOfRangeRows"] = outOfRangeRows;
        return result;
    }

    private static void SelectPresentMonFrames(List<PresentRecord> records, PresentReadResult result)
    {
        if (records.Count == 0)
        {
            result.Diagnostics["selectedRows"] = 0;
            result.Diagnostics["selectionMode"] = "empty";
            return;
        }

        Dictionary<string, PresentTrack> tracks = new Dictionary<string, PresentTrack>();
        foreach (PresentRecord r in records)
        {
            string key = r.ProcessId + "|" + r.SwapChain + "|" + r.Application;
            PresentTrack track;
            if (!tracks.TryGetValue(key, out track))
            {
                track = new PresentTrack { ProcessId = r.ProcessId, SwapChain = r.SwapChain, Application = r.Application };
                tracks[key] = track;
            }
            track.Records.Add(r);
        }

        List<PresentTrack> summaries = new List<PresentTrack>();
        foreach (PresentTrack track in tracks.Values)
        {
            track.Rows = track.Records.Count;
            List<double> values = new List<double>();
            List<double> hardware = new List<double>();
            foreach (PresentRecord r in track.Records)
            {
                values.Add(r.FrameMs);
                if (IsHardwarePresent(r))
                {
                    hardware.Add(r.FrameMs);
                    track.HardwareRows++;
                }
                if (r.AllowsTearing == "1" || r.AllowsTearing.Equals("true", StringComparison.OrdinalIgnoreCase)) track.AllowsTearingRows++;
                if (r.FrameMs > 1000) track.ArtifactRowsOver1000ms++;
            }
            List<double> scoring = hardware.Count > 0 ? hardware : values;
            double? medianMs = PercentileHigh(scoring, 0.5);
            double? p99 = PercentileHigh(scoring, 0.99);
            track.MedianFps = medianMs.HasValue && medianMs.Value > 0 ? 1000.0 / medianMs.Value : (double?)null;
            track.P99FrameMs = p99;
            track.Score = track.HardwareRows * 3.0 + track.Rows;
            if (track.MedianFps.HasValue) track.Score += Math.Min(240.0, track.MedianFps.Value) * 20.0;
            if (track.P99FrameMs.HasValue) track.Score -= Math.Min(1000.0, track.P99FrameMs.Value) * 2.0;
            summaries.Add(track);
        }
        summaries.Sort(delegate(PresentTrack a, PresentTrack b) { return b.Score.CompareTo(a.Score); });
        PresentTrack selected = summaries[0];
        bool multiTrack = summaries.Count > 1;
        bool useHardwareOnly = multiTrack && selected.HardwareRows > 0;

        List<PresentRecord> selectedRecords = new List<PresentRecord>();
        int droppedModeRows = 0;
        foreach (PresentRecord r in selected.Records)
        {
            if (useHardwareOnly && !IsHardwarePresent(r))
            {
                droppedModeRows++;
                continue;
            }
            if (r.FrameMs > 1000)
            {
                droppedModeRows++;
                continue;
            }
            selectedRecords.Add(r);
        }
        selectedRecords.Sort(delegate(PresentRecord a, PresentRecord b) { return a.Time.CompareTo(b.Time); });
        foreach (PresentRecord r in selectedRecords) result.Frames.Add(new KeyValuePair<DateTime, double>(r.Time, r.FrameMs));

        int droppedTrackRows = records.Count - selected.Records.Count;
        result.Diagnostics["selectedRows"] = selectedRecords.Count;
        result.Diagnostics["selectionMode"] = multiTrack ? "primary-hardware-track" : "all";
        result.Diagnostics["selectedTrack"] = selected.ToJson();
        result.Diagnostics["tracks"] = summaries.Select(t => t.ToJson()).ToList();
        result.Diagnostics["trackCount"] = summaries.Count;
        result.Diagnostics["droppedTrackRows"] = Math.Max(0, droppedTrackRows);
        result.Diagnostics["droppedModeRows"] = Math.Max(0, droppedModeRows);
        result.Diagnostics["droppedResumeArtifactRows"] = selected.Records.Count(r => r.FrameMs > 1000);
    }

    private static bool IsHardwarePresent(PresentRecord record)
    {
        return record.PresentMode != null && record.PresentMode.StartsWith("Hardware:", StringComparison.OrdinalIgnoreCase);
    }

    private static List<SystemRow> ReadSystem(string path)
    {
        List<SystemRow> rows = new List<SystemRow>();
        if (!File.Exists(path)) return rows;
        using (CsvTable table = CsvTable.Open(path))
        {
            Dictionary<string, int> h = table.Headers;
            List<string> row;
            while ((row = table.ReadRow()) != null)
            {
                DateTime t;
                if (!TryParseDate(Get(row, h, "Time"), out t)) continue;
                rows.Add(new SystemRow
                {
                    Time = t,
                    Cpu = ParseNullableDouble(Get(row, h, "TotalCpuPct")),
                    CpuFrequency = ParseNullableDouble(Get(row, h, "CpuFrequencyMHz")),
                    CpuPerformancePct = ParseNullableDouble(Get(row, h, "CpuPerformancePct")),
                    AvailableMb = ParseNullableDouble(Get(row, h, "AvailableMB")),
                    DiskAvgSecPerTransfer = ParseNullableDouble(Get(row, h, "DiskAvgSecPerTransfer")),
                    DiskBytesPerSec = ParseNullableDouble(Get(row, h, "DiskBytesPerSec")),
                    NetBytesPerSec = ParseNullableDouble(Get(row, h, "NetBytesPerSec")),
                    Gpu = ParseNullableDouble(Get(row, h, "GpuUtilPct")),
                    GpuMem = ParseNullableDouble(Get(row, h, "GpuMemUtilPct")),
                    GpuTemp = ParseNullableDouble(Get(row, h, "GpuTempC")),
                    GpuClock = ParseNullableDouble(Get(row, h, "GpuClockMHz")),
                    MemClock = ParseNullableDouble(Get(row, h, "MemClockMHz")),
                    Power = ParseNullableDouble(Get(row, h, "PowerW")),
                    VramUsedMiB = ParseNullableDouble(Get(row, h, "VramUsedMiB")),
                    VramTotalMiB = ParseNullableDouble(Get(row, h, "VramTotalMiB"))
                });
            }
        }
        rows.Sort(delegate(SystemRow a, SystemRow b) { return a.Time.CompareTo(b.Time); });
        return rows;
    }

    private static ProcessMatrixResult ReadProcessMatrix(string path, DateTime start, string targetProcess)
    {
        ProcessMatrixResult result = new ProcessMatrixResult();
        if (!File.Exists(path)) return result;

        string targetBase = Path.GetFileNameWithoutExtension(targetProcess ?? "").ToLowerInvariant();
        Dictionary<string, int> sampleMap = new Dictionary<string, int>();
        Dictionary<string, ProcessStat> stats = new Dictionary<string, ProcessStat>(StringComparer.OrdinalIgnoreCase);

        using (CsvTable table = CsvTable.Open(path))
        {
            Dictionary<string, int> h = table.Headers;
            List<string> row;
            while ((row = table.ReadRow()) != null)
            {
                string name = Get(row, h, "ProcessName").Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Equals(targetBase, StringComparison.OrdinalIgnoreCase) || name.Equals("cs2", StringComparison.OrdinalIgnoreCase)) continue;
                DateTime t;
                if (!TryParseDate(Get(row, h, "Time"), out t)) continue;
                string sampleIndex = Get(row, h, "SampleIndex");
                if (!sampleMap.ContainsKey(sampleIndex))
                {
                    sampleMap[sampleIndex] = result.Times.Count;
                    result.Times.Add(RoundDouble((t - start).TotalSeconds, 3));
                }
                double? cpu = ParseNullableDouble(Get(row, h, "CpuPct"));
                double? mem = ParseNullableDouble(Get(row, h, "WorkingSetMB"));
                ProcessStat stat;
                if (!stats.TryGetValue(name, out stat))
                {
                    stat = new ProcessStat { Name = name };
                    stats[name] = stat;
                }
                stat.Samples++;
                if (cpu.HasValue)
                {
                    stat.MaxCpu = Math.Max(stat.MaxCpu, cpu.Value);
                    stat.CpuSum += cpu.Value;
                    stat.CpuSamples++;
                }
                if (mem.HasValue) stat.MaxMem = Math.Max(stat.MaxMem, mem.Value);
            }
        }

        List<ProcessStat> ordered = stats.Values.OrderByDescending(s => s.MaxCpu).ThenByDescending(s => s.MaxMem).ThenByDescending(s => s.Samples).ToList();
        result.Names = ordered.Select(s => s.Name).ToList();
        int nTimes = result.Times.Count;
        Dictionary<string, int> nameMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ordered.Count; i++)
        {
            nameMap[ordered[i].Name] = i;
            result.Cpu.Add(NewNullableList(nTimes));
            result.Mem.Add(NewNullableList(nTimes));
            result.Stats.Add(new Dictionary<string, object>
            {
                { "name", ordered[i].Name },
                { "maxCpu", Round(ordered[i].MaxCpu, 2) },
                { "avgCpu", ordered[i].CpuSamples > 0 ? Round(ordered[i].CpuSum / ordered[i].CpuSamples, 2) : 0 },
                { "maxMem", Round(ordered[i].MaxMem, 1) },
                { "samples", ordered[i].Samples }
            });
        }

        using (CsvTable table = CsvTable.Open(path))
        {
            Dictionary<string, int> h = table.Headers;
            List<string> row;
            while ((row = table.ReadRow()) != null)
            {
                string name = Get(row, h, "ProcessName").Trim();
                int procIndex;
                if (!nameMap.TryGetValue(name, out procIndex)) continue;
                int pos;
                if (!sampleMap.TryGetValue(Get(row, h, "SampleIndex"), out pos)) continue;
                double? cpu = ParseNullableDouble(Get(row, h, "CpuPct"));
                double? mem = ParseNullableDouble(Get(row, h, "WorkingSetMB"));
                if (cpu.HasValue) result.Cpu[procIndex][pos] = RoundDouble(cpu.Value, 2);
                if (mem.HasValue) result.Mem[procIndex][pos] = RoundDouble(mem.Value, 1);
            }
        }

        return result;
    }

    private static List<double?> NewNullableList(int count)
    {
        List<double?> list = new List<double?>(count);
        for (int i = 0; i < count; i++) list.Add(null);
        return list;
    }

    private static List<KeyValuePair<DateTime, double>> AlignPresentMonTime(List<KeyValuePair<DateTime, double>> frames, List<SystemRow> systemRows, out int timeShiftHours)
    {
        timeShiftHours = 0;
        if (frames.Count == 0 || systemRows.Count == 0) return frames;
        double hours = (systemRows[0].Time - frames[0].Key).TotalHours;
        int rounded = (int)Math.Round(hours);
        if (Math.Abs(hours) >= 1.0 && Math.Abs(hours - rounded) < 0.25 && Math.Abs(rounded) <= 14)
        {
            timeShiftHours = rounded;
            return frames.Select(f => new KeyValuePair<DateTime, double>(f.Key.AddHours(rounded), f.Value)).OrderBy(f => f.Key).ToList();
        }
        return frames;
    }

    private static Dictionary<string, object> BucketFps(List<KeyValuePair<DateTime, double>> frames, DateTime start, double bucketSeconds, double lowWindowSeconds)
    {
        Dictionary<string, object> empty = new Dictionary<string, object>
        {
            { "bucketMs", (int)Math.Round(bucketSeconds * 1000) },
            { "lowWindowMs", (int)Math.Round(lowWindowSeconds * 1000) },
            { "t", new List<double>() },
            { "avg", new List<double?>() },
            { "low1", new List<double?>() },
            { "low01", new List<double?>() },
            { "min", new List<double?>() }
        };
        if (frames.Count == 0) return empty;

        SortedDictionary<int, List<double>> buckets = new SortedDictionary<int, List<double>>();
        List<double> secs = new List<double>(frames.Count);
        List<double> msValues = new List<double>(frames.Count);
        foreach (KeyValuePair<DateTime, double> f in frames)
        {
            double sec = (f.Key - start).TotalSeconds;
            if (sec < 0) continue;
            int bucket = (int)Math.Floor(sec / bucketSeconds);
            List<double> list;
            if (!buckets.TryGetValue(bucket, out list))
            {
                list = new List<double>();
                buckets[bucket] = list;
            }
            list.Add(f.Value);
            secs.Add(sec);
            msValues.Add(f.Value);
        }

        List<double> times = new List<double>();
        List<double?> avg = new List<double?>();
        List<double?> low1 = new List<double?>();
        List<double?> low01 = new List<double?>();
        List<double?> min = new List<double?>();
        Fenwick fenwick = new Fenwick(100001);
        Queue<int> windowBins = new Queue<int>();
        Queue<double> windowSecs = new Queue<double>();
        int frameIndex = 0;

        foreach (KeyValuePair<int, List<double>> bucket in buckets)
        {
            double t = RoundDouble(bucket.Key * bucketSeconds, 3);
            double windowStart = t - lowWindowSeconds;
            while (frameIndex < secs.Count && secs[frameIndex] <= t + bucketSeconds)
            {
                int bin = MsToBin(msValues[frameIndex]);
                fenwick.Add(bin, 1);
                windowBins.Enqueue(bin);
                windowSecs.Enqueue(secs[frameIndex]);
                frameIndex++;
            }
            while (windowSecs.Count > 0 && windowSecs.Peek() < windowStart)
            {
                windowSecs.Dequeue();
                fenwick.Add(windowBins.Dequeue(), -1);
            }

            double meanMs = bucket.Value.Average();
            double maxMs = bucket.Value.Max();
            times.Add(t);
            avg.Add(RoundDouble(1000.0 / meanMs, 2));
            min.Add(RoundDouble(1000.0 / maxMs, 3));
            low1.Add(FpsFromFenwick(fenwick, 0.99));
            low01.Add(FpsFromFenwick(fenwick, 0.999));
        }

        empty["t"] = times;
        empty["avg"] = avg;
        empty["low1"] = low1;
        empty["low01"] = low01;
        empty["min"] = min;
        return empty;
    }

    private static int MsToBin(double ms)
    {
        int bin = (int)Math.Round(ms * 10.0);
        if (bin < 1) bin = 1;
        if (bin > 100000) bin = 100000;
        return bin;
    }

    private static double? FpsFromFenwick(Fenwick fenwick, double quantile)
    {
        if (fenwick.Count <= 0) return null;
        int rank = Math.Max(1, (int)Math.Ceiling(quantile * fenwick.Count));
        int bin = fenwick.FindByRank(rank);
        double ms = bin / 10.0;
        return ms > 0 ? RoundDouble(1000.0 / ms, 2) : (double?)null;
    }

    private static Dictionary<string, object> SeriesFromSystem(List<SystemRow> rows, DateTime start, double? totalMemoryMb)
    {
        List<double> t = new List<double>();
        List<double?> cpu = new List<double?>(), gpu = new List<double?>(), gpuMem = new List<double?>(), mem = new List<double?>(), vram = new List<double?>();
        List<double?> cpuFreq = new List<double?>(), gpuClock = new List<double?>(), memClock = new List<double?>();
        List<double?> disk = new List<double?>(), net = new List<double?>(), diskLatency = new List<double?>(), power = new List<double?>(), temp = new List<double?>();

        foreach (SystemRow row in rows)
        {
            t.Add(RoundDouble((row.Time - start).TotalSeconds, 3));
            cpu.Add(RoundNullable(row.Cpu, 2));
            gpu.Add(RoundNullable(row.Gpu, 2));
            gpuMem.Add(RoundNullable(row.GpuMem, 2));
            double? memPct = null;
            if (totalMemoryMb.HasValue && row.AvailableMb.HasValue) memPct = Math.Max(0, Math.Min(100, (totalMemoryMb.Value - row.AvailableMb.Value) / totalMemoryMb.Value * 100.0));
            mem.Add(RoundNullable(memPct, 2));
            double? vramPct = null;
            if (row.VramTotalMiB.HasValue && row.VramTotalMiB.Value > 0 && row.VramUsedMiB.HasValue) vramPct = row.VramUsedMiB.Value / row.VramTotalMiB.Value * 100.0;
            vram.Add(RoundNullable(vramPct, 2));
            cpuFreq.Add(RoundNullable(EffectiveCpuFrequency(row), 0));
            gpuClock.Add(RoundNullable(row.GpuClock, 0));
            memClock.Add(RoundNullable(row.MemClock, 0));
            disk.Add(RoundNullable(row.DiskBytesPerSec.HasValue ? row.DiskBytesPerSec.Value / 1024.0 / 1024.0 : (double?)null, 3));
            net.Add(RoundNullable(row.NetBytesPerSec.HasValue ? row.NetBytesPerSec.Value / 1024.0 / 1024.0 : (double?)null, 3));
            diskLatency.Add(RoundNullable(row.DiskAvgSecPerTransfer.HasValue ? row.DiskAvgSecPerTransfer.Value * 1000.0 : (double?)null, 3));
            power.Add(RoundNullable(row.Power, 2));
            temp.Add(RoundNullable(row.GpuTemp, 1));
        }

        return new Dictionary<string, object>
        {
            { "t", t },
            { "usage", new Dictionary<string, object> { { "cpu", cpu }, { "gpu", gpu }, { "gpuMem", gpuMem }, { "mem", mem }, { "vram", vram } } },
            { "perf", new Dictionary<string, object> { { "cpuFreq", cpuFreq }, { "gpuClock", gpuClock }, { "memClock", memClock } } },
            { "io", new Dictionary<string, object> { { "disk", disk }, { "net", net }, { "diskLatency", diskLatency }, { "power", power }, { "temp", temp } } }
        };
    }

    private static double? EffectiveCpuFrequency(SystemRow row)
    {
        if (!row.CpuFrequency.HasValue) return null;
        if (row.CpuPerformancePct.HasValue && row.CpuPerformancePct.Value > 0) return row.CpuFrequency.Value * row.CpuPerformancePct.Value / 100.0;
        return row.CpuFrequency.Value;
    }

    private static Dictionary<string, string> LoadRunMetadata(string runDir)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string summary = Path.Combine(runDir, "summary.json");
        string status = Path.Combine(runDir, "status.json");
        foreach (string path in new[] { summary, status })
        {
            if (!File.Exists(path)) continue;
            string text = File.ReadAllText(path, Encoding.UTF8);
            string target = ExtractJsonString(text, "TargetProcessName");
            if (string.IsNullOrEmpty(target)) target = ExtractJsonString(text, "TargetProcess");
            if (string.IsNullOrEmpty(target)) target = ExtractJsonString(text, "Target");
            if (!string.IsNullOrEmpty(target))
            {
                result["targetProcess"] = target;
                return result;
            }
        }
        result["targetProcess"] = "cs2.exe";
        return result;
    }

    private static string ExtractJsonString(string text, string key)
    {
        string pattern = "\"" + key + "\"";
        int keyPos = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (keyPos < 0) return "";
        int colon = text.IndexOf(':', keyPos + pattern.Length);
        if (colon < 0) return "";
        int quote = text.IndexOf('"', colon + 1);
        if (quote < 0) return "";
        StringBuilder sb = new StringBuilder();
        bool escape = false;
        for (int i = quote + 1; i < text.Length; i++)
        {
            char ch = text[i];
            if (escape)
            {
                sb.Append(ch);
                escape = false;
            }
            else if (ch == '\\')
            {
                escape = true;
            }
            else if (ch == '"')
            {
                break;
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static Dictionary<string, object> LoadHardware()
    {
        Dictionary<string, object> h = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    h["CpuName"] = Convert.ToString(obj["Name"]);
                    h["CpuCores"] = SafeInt(obj["NumberOfCores"]);
                    h["CpuThreads"] = SafeInt(obj["NumberOfLogicalProcessors"]);
                    h["CpuMaxClockMHz"] = SafeInt(obj["MaxClockSpeed"]);
                    break;
                }
            }
        }
        catch { }
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption,Version,OSArchitecture FROM Win32_OperatingSystem"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    h["OsCaption"] = Convert.ToString(obj["Caption"]);
                    h["OsVersion"] = Convert.ToString(obj["Version"]);
                    h["OsArch"] = Convert.ToString(obj["OSArchitecture"]);
                    break;
                }
            }
        }
        catch { }
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,DriverVersion FROM Win32_VideoController"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = Convert.ToString(obj["Name"]);
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    h["GpuName"] = name;
                    h["GpuDriver"] = Convert.ToString(obj["DriverVersion"]);
                    break;
                }
            }
        }
        catch { }
        try
        {
            h["TotalMemoryMB"] = RoundDouble(new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / 1024.0 / 1024.0, 0);
        }
        catch { }
        return h;
    }

    private static int? SafeInt(object value)
    {
        if (value == null) return null;
        int parsed;
        if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed)) return parsed;
        return null;
    }

    private static double? GetDoubleFromHardware(Dictionary<string, object> hardware, string key)
    {
        object value;
        if (!hardware.TryGetValue(key, out value) || value == null) return null;
        double parsed;
        if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return parsed;
        return null;
    }

    private static string FormatDuration(int seconds)
    {
        return (seconds / 60).ToString(CultureInfo.InvariantCulture) + "分" + (seconds % 60).ToString(CultureInfo.InvariantCulture) + "秒";
    }

    private static bool TryParseDate(string text, out DateTime value)
    {
        text = (text ?? "").Trim();
        value = DateTime.MinValue;
        if (string.IsNullOrEmpty(text) || text.Equals("NA", StringComparison.OrdinalIgnoreCase)) return false;
        DateTimeOffset dto;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dto))
        {
            value = dto.LocalDateTime;
            return true;
        }
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value)) return true;
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out value);
    }

    private static double? ParseNullableDouble(string text)
    {
        text = (text ?? "").Trim();
        if (string.IsNullOrEmpty(text) || text.Equals("NA", StringComparison.OrdinalIgnoreCase)) return null;
        double value;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return null;
        if (double.IsNaN(value) || double.IsInfinity(value)) return null;
        return value;
    }

    private static double? PercentileHigh(List<double> values, double quantile)
    {
        if (values == null || values.Count == 0) return null;
        List<double> sorted = new List<double>(values);
        sorted.Sort();
        int index = Math.Max(0, Math.Min(sorted.Count - 1, (int)Math.Ceiling(quantile * sorted.Count) - 1));
        return sorted[index];
    }

    private static double? AverageOrNull(List<double> values)
    {
        return values.Count > 0 ? values.Average() : (double?)null;
    }

    private static double? MaxOrNull(List<double> values)
    {
        return values.Count > 0 ? values.Max() : (double?)null;
    }

    private static object Round(double? value, int digits)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return null;
        return Math.Round(value.Value, digits, MidpointRounding.AwayFromZero);
    }

    private static double? RoundNullable(double? value, int digits)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return null;
        return Math.Round(value.Value, digits, MidpointRounding.AwayFromZero);
    }

    private static object Round(double value, int digits)
    {
        return Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }

    private static double RoundDouble(double value, int digits)
    {
        return Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }

    private static string Get(List<string> row, Dictionary<string, int> headers, string name)
    {
        int index;
        if (!headers.TryGetValue(name, out index) || index < 0 || index >= row.Count) return "";
        return row[index] ?? "";
    }

    private static bool ListHasValue(object value)
    {
        IEnumerable<double?> doubles = value as IEnumerable<double?>;
        if (doubles == null) return false;
        foreach (double? d in doubles) if (d.HasValue) return true;
        return false;
    }

    private sealed class CsvTable : IDisposable
    {
        private readonly StreamReader reader;
        public readonly Dictionary<string, int> Headers;

        private CsvTable(string path)
        {
            reader = new StreamReader(path, Encoding.UTF8, true, 1024 * 1024);
            List<string> headers = ParseLine(reader.ReadLine() ?? "");
            Headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++) Headers[headers[i]] = i;
        }

        public static CsvTable Open(string path)
        {
            return new CsvTable(path);
        }

        public List<string> ReadRow()
        {
            string line = reader.ReadLine();
            if (line == null) return null;
            return ParseLine(line);
        }

        public void Dispose()
        {
            reader.Dispose();
        }

        private static List<string> ParseLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
                else
                {
                    if (ch == ',')
                    {
                        fields.Add(sb.ToString());
                        sb.Length = 0;
                    }
                    else if (ch == '"')
                    {
                        quoted = true;
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }

    private static string MakeHtml()
    {
        return @"<!doctype html>
<html lang='zh-CN'>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
  <title>FrameScope 性能报告</title>
  <style>
    :root{--bg:#0c1118;--panel:#141d28;--line:#385467;--text:#eff7ff;--muted:#9fb4c4;--cyan:#29e6ff;--green:#a9ff47;--yellow:#ffd35b;--red:#ff5d7d}
    *{box-sizing:border-box} body{margin:0;background:#0c1118;color:var(--text);font-family:'Segoe UI','Microsoft YaHei UI',Arial,sans-serif}
    .shell{width:min(1900px,calc(100vw - 20px));margin:10px auto;display:grid;grid-template-columns:320px minmax(0,1fr);gap:12px}
    .left,.main{border:1px solid rgba(94,137,160,.45);background:rgba(20,29,40,.98);border-radius:8px}
    .left{padding:16px}.main{padding:16px 18px 18px;min-width:0}.brand{color:var(--yellow);font-size:30px;font-weight:900}.brand-sub{color:var(--cyan);font-size:14px;margin:4px 0 14px}
    .block{border-top:1px solid rgba(143,185,210,.18);padding:12px 0}.block h3{margin:0 0 8px;color:var(--cyan);font-size:15px}.line{display:flex;justify-content:space-between;gap:12px;line-height:1.65;font-size:13px}.line span:first-child{color:var(--muted)}.line b{text-align:right;color:#fff1d0;overflow-wrap:anywhere}.note{color:var(--muted);font-size:13px;line-height:1.55}
    .topbar{display:flex;align-items:center;justify-content:space-between;gap:14px;border-bottom:1px solid rgba(143,185,210,.18);padding-bottom:12px}.game{display:flex;align-items:center;gap:12px;min-width:0}.game-icon{width:42px;height:42px;display:grid;place-items:center;border-radius:8px;background:#b51e2d;color:white;font-weight:900;font-size:22px}.game-name{font-size:22px;font-weight:900}.meta{display:flex;flex-wrap:wrap;gap:14px;color:var(--muted);font-size:13px;margin-top:4px}
    .tabs{display:flex;gap:8px;flex-wrap:wrap;justify-content:flex-end}.tab{height:34px;padding:0 12px;border-radius:5px;border:1px solid rgba(75,213,236,.42);background:#213145;color:#d9eef7;font-weight:800;cursor:pointer;transition:.18s}.tab:hover{transform:translateY(-1px);border-color:rgba(41,230,255,.72)}.tab.active{color:#06141a;background:var(--cyan);box-shadow:0 0 16px rgba(41,230,255,.32)}
    .title{display:flex;align-items:baseline;justify-content:space-between;gap:12px;margin:14px 0 8px}.title h2{margin:0;color:var(--cyan);font-size:21px}.title span{color:var(--muted);font-size:13px;text-align:right}
    .gauges{display:grid;grid-template-columns:170px repeat(6,minmax(112px,1fr));gap:14px;align-items:start;margin:8px 0 12px}.gauge{text-align:center;min-width:0}.gauge h3{margin:0 0 8px;font-size:18px;color:#ffe8bd}.ring{--p:0;--c:var(--cyan);width:104px;height:104px;margin:auto;border-radius:50%;display:grid;place-items:center;background:radial-gradient(circle at center,#132032 0 54%,transparent 56%),conic-gradient(var(--c) calc(var(--p)*1%),rgba(85,122,145,.35) 0);box-shadow:inset 0 0 0 2px rgba(138,181,207,.25)}.ring.big{width:126px;height:126px}.ring b{font-size:27px;line-height:1;text-shadow:0 0 12px rgba(41,230,255,.36)}.ring.big b{font-size:38px;color:var(--cyan)}.ring small{display:block;color:#d8e6ef;font-size:12px;margin-top:5px}.foot{color:var(--yellow);font-size:12px;margin-top:7px;min-height:16px}
    .toolbar{display:flex;align-items:center;justify-content:space-between;gap:12px;margin:8px 0;flex-wrap:wrap}.left-tools,.right-tools{display:flex;align-items:center;gap:10px;flex-wrap:wrap}select,input,button.tool{background:#26384b;color:#fff;border:1px solid rgba(75,213,236,.45);height:38px;border-radius:5px;padding:0 10px;font-weight:800;outline:none}.tool{cursor:pointer}input.search{min-width:240px}.range{display:flex;align-items:center;gap:8px;color:var(--muted);font-size:12px}.range input{min-width:160px;padding:0}
    .chart-scroll{overflow-x:auto;overflow-y:hidden;border:1px solid rgba(96,147,179,.52);background:#111a24}.chartbox{position:relative;height:560px;width:100%;min-width:900px;resize:both;overflow:hidden;transition:border-color .2s ease,box-shadow .2s ease}.chartbox.switching{box-shadow:inset 0 0 18px rgba(41,230,255,.08)}canvas{position:absolute;inset:0;width:100%;height:100%;display:block;opacity:1;transform:translateY(0);transition:opacity .16s ease,transform .16s ease}#overlay{pointer-events:none}.chartbox.switching canvas{opacity:.46;transform:translateY(5px)}
    .tooltip{position:absolute;pointer-events:none;opacity:0;min-width:260px;max-width:460px;background:rgba(24,33,46,.96);border:1px solid rgba(196,226,241,.25);box-shadow:0 12px 36px rgba(0,0,0,.38);border-radius:5px;padding:10px 12px;font-size:13px;line-height:1.55;z-index:5}.legend{display:flex;gap:10px 12px;flex-wrap:wrap;max-height:76px;overflow:auto;color:#dceef7;font-size:12px}.dot{width:12px;height:12px;border-radius:3px;display:inline-block;margin-right:5px;vertical-align:-2px}
    .panelgrid{margin-top:13px;display:grid;grid-template-columns:1fr 1fr;gap:12px}.card{border:1px solid rgba(87,132,159,.42);background:rgba(27,40,54,.9);border-radius:8px;padding:12px 14px;min-width:0}.card h3{margin:0 0 10px;color:var(--yellow);font-size:15px}.rows{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:7px 16px;font-size:13px}.row{display:grid;grid-template-columns:minmax(90px,1fr) 78px 88px;gap:8px;align-items:center;min-width:0}.row span:first-child{overflow:hidden;white-space:nowrap;text-overflow:ellipsis}.bar{height:5px;border-radius:99px;background:#2e455b;overflow:hidden}.bar i{display:block;height:100%;background:linear-gradient(90deg,var(--cyan),var(--green))}
    @media(max-width:1200px){.shell{grid-template-columns:1fr}.gauges{grid-template-columns:repeat(2,minmax(0,1fr))}.panelgrid{grid-template-columns:1fr}.title{display:block}.title span{text-align:left;display:block;margin-top:5px}}
  </style>
</head>
<body>
<div class='shell'>
  <aside class='left'>
    <div class='brand'>FrameScope</div><div class='brand-sub'>原生 C# 生成的单页性能报告</div>
    <div class='block'><h3>处理器</h3><div class='line'><span id='hwCpu'>-</span></div><div class='line'><span>核心/线程</span><b id='hwCore'>-</b></div><div class='line'><span>标称最大频率</span><b id='hwCpuClock'>-</b></div></div>
    <div class='block'><h3>显卡</h3><div class='line'><span id='hwGpu'>-</span></div><div class='line'><span>驱动版本</span><b id='hwDriver'>-</b></div><div class='line'><span>记录显存</span><b id='hwVram'>-</b></div></div>
    <div class='block'><h3>系统</h3><div class='line'><span id='hwOs'>-</span></div><div class='line'><span>内存</span><b id='hwMem'>-</b></div><div class='line'><span>采样点</span><b id='hwSamples'>-</b></div></div>
    <div class='block'><h3>图表操作</h3><div class='note'>可以用图表宽度滑块横向展开长时间记录；保留尖峰使用 min/max 包络，趋势易读使用平均和平滑，原始密集尽量按原始点绘制。</div></div>
  </aside>
  <main class='main'>
    <div class='topbar'><div class='game'><div class='game-icon' id='gameIcon'>FS</div><div><div class='game-name' id='gameName'>-</div><div class='meta'><span id='runStart'>-</span><span id='runEnd'>-</span><span id='runDuration'>-</span></div></div></div>
      <div class='tabs'><button class='tab active' data-view='fps'>帧率</button><button class='tab' data-view='perf'>性能图表</button><button class='tab' data-view='system'>系统占用</button><button class='tab' data-view='process'>后台进程</button><button class='tab' data-view='io'>IO/温度</button></div>
    </div>
    <div class='title'><h2 id='viewTitle'>帧率波动</h2><span id='viewNote'>完整数据保留在本地 data.js，图表按宽度自适应绘制。</span></div>
    <div class='gauges' id='gauges'></div>
    <div class='toolbar'>
      <div class='left-tools'><select id='metricSelect'></select><select id='readMode'><option value='spike'>保留尖峰</option><option value='trend'>趋势易读</option><option value='raw'>原始密集</option></select><input id='processSearch' class='search' placeholder='搜索进程，留空显示全部后台进程'></div>
      <div class='right-tools'><div class='range'>图表宽度 <input id='widthScale' type='range' min='1' max='8' step='0.25' value='1'><b id='widthText'>1.00x</b></div><button class='tool' id='fitWidth'>适合窗口</button><div class='legend' id='legend'></div></div>
    </div>
    <div class='chart-scroll' id='chartScroll'><div class='chartbox' id='chartBox'><canvas id='chart'></canvas><canvas id='overlay'></canvas><div class='tooltip' id='tooltip'></div></div></div>
    <div class='panelgrid'><div class='card'><h3>后台进程峰值</h3><div class='rows' id='processRows'></div></div><div class='card'><h3>本次帧率摘要</h3><div class='rows' id='summaryRows'></div></div></div>
  </main>
</div>
<script src='framescope-interactive-data.js'></script>
<script>
const DATA=window.FRAMESCOPE_DATA; const COLORS=(DATA&&DATA.colors)||['#29e6ff','#a9ff47','#ffd35b','#ff5d7d','#65a7ff'];
let view='fps',fpsMetric='all',perfMetric='all',systemMetric='all',ioMetric='all',processMetric='cpu',legendKey='',hoverFrame=0,pendingHoverEvent=null;
const canvas=document.getElementById('chart'),ctx=canvas.getContext('2d'),overlay=document.getElementById('overlay'),octx=overlay.getContext('2d'),tooltip=document.getElementById('tooltip'),metricSelect=document.getElementById('metricSelect'),processSearch=document.getElementById('processSearch'),legend=document.getElementById('legend'),chartBox=document.getElementById('chartBox'),chartScroll=document.getElementById('chartScroll'),viewTitle=document.getElementById('viewTitle'),viewNote=document.getElementById('viewNote'),widthScale=document.getElementById('widthScale'),widthText=document.getElementById('widthText'),readMode=document.getElementById('readMode');
const PAD={l:58,r:22,t:28,b:42}; let currentSeries=[],currentTimes=[],currentUnit=''; const renderCache=new Map(),yMaxCache=new Map();
function esc(v){return String(v??'').replace(/[&<>\u0022']/g,ch=>{if(ch==='&')return '&amp;';if(ch==='<')return '&lt;';if(ch==='>')return '&gt;';if(ch==='\u0022')return '&quot;';return '&#39;';});}
function n(v,d=1){const num=Number(v);return v===null||v===undefined||!Number.isFinite(num)?'N/A':num.toFixed(d)}
function mmss(sec){sec=Math.max(0,Math.round(Number(sec)||0));return `${Math.floor(sec/60)}:${String(sec%60).padStart(2,'0')}`}
function setText(id,v){const e=document.getElementById(id);if(e)e.textContent=v} function pct(v){return Math.max(0,Math.min(100,Number(v)||0))}
function maxFinite(arr,fallback=1){let m=fallback;for(let i=0;i<arr.length;i++){const v=Number(arr[i]);if(Number.isFinite(v)&&v>m)m=v}return m}
function seriesMaxValue(s,fallback=1){const d=s.data||[],key=`${s.key||s.name}|${d.length}|max|${fallback}`;if(yMaxCache.has(key))return yMaxCache.get(key);let m=fallback;for(let i=0;i<d.length;i++){const v=Number(d[i]);if(Number.isFinite(v)&&v>m)m=v}yMaxCache.set(key,m);return m}
function maxSeriesValue(series,fallback=1){let m=fallback;for(const s of series)m=Math.max(m,seriesMaxValue(s,fallback));return m}
function axisLabel(v,maxY){if(maxY>=100)return v.toFixed(0);if(maxY>=10)return v.toFixed(1);if(maxY>=1)return v.toFixed(2);return v.toPrecision(2)}
function lineWidth(si){const mode=readMode.value;if(view==='process')return mode==='trend'?.95:mode==='raw'?.38:.62;if(view==='fps')return mode==='trend'?1.15:mode==='raw'?.52:(si===0?.9:.7);return mode==='trend'?1.08:mode==='raw'?.52:.82}
function seriesAlpha(si){const mode=readMode.value;if(view==='process')return mode==='trend'?(si<18?.82:.28):mode==='raw'?(si<18?.34:.1):(si<18?.68:.22);return mode==='trend'?.95:mode==='raw'?.55:.76}
function clearOverlay(){octx.clearRect(0,0,overlay.width,overlay.height)}
function applyWidth(){const scale=Number(widthScale.value)||1;widthText.textContent=scale.toFixed(2)+'x';const base=chartScroll.clientWidth||900;chartBox.style.width=Math.max(900,Math.round(base*scale))+'px';renderCache.clear();resizeCanvas()}
function resizeCanvas(){const r=chartBox.getBoundingClientRect(),d=window.devicePixelRatio||1;canvas.width=overlay.width=Math.max(1,Math.round(r.width*d));canvas.height=overlay.height=Math.max(1,Math.round(r.height*d));ctx.setTransform(d,0,0,d,0,0);octx.setTransform(d,0,0,d,0,0);renderCache.clear();draw()}
function chartDims(){const r=chartBox.getBoundingClientRect();return{w:r.width,h:r.height,pw:r.width-PAD.l-PAD.r,ph:r.height-PAD.t-PAD.b}}
function samplingProfile(len,pixelWidth){const mode=readMode.value,width=Math.max(1,Math.round(pixelWidth)),seriesCount=Math.max(1,currentSeries.length);if(mode==='raw'){const totalCap=view==='process'?220000:650000,perCap=Math.max(view==='process'?900:2400,Math.floor(totalCap/seriesCount));return{mode,maxPoints:Math.min(len,perCap)}}if(mode==='trend'){const factor=view==='process'?.22:.45,buckets=Math.max(80,Math.min(len,Math.ceil(width*factor))),smooth=view==='fps'?7:5;return{mode,buckets,smooth}}const factor=view==='process'?.75:1.25,buckets=Math.max(180,Math.min(len,Math.ceil(width*factor)));return{mode,buckets}}
function getRenderablePoints(series,pixelWidth){const data=series.data||[],len=Math.min(currentTimes.length,data.length);if(len<=0)return{t:[],y:[]};const mode=readMode.value,widthBucket=Math.max(1,Math.round(pixelWidth)),profile=samplingProfile(len,pixelWidth),cacheKey=`${series.key||series.name}|${len}|${widthBucket}|${view}|${mode}|${currentSeries.length}`;if(renderCache.has(cacheKey))return renderCache.get(cacheKey);if(mode==='raw'){const maxPoints=Math.max(2,profile.maxPoints||len);if(len<=maxPoints){const raw={t:currentTimes.slice(0,len),y:data.slice(0,len)};renderCache.set(cacheKey,raw);return raw}const t=[],y=[],step=(len-1)/(maxPoints-1);let last=-1;for(let p=0;p<maxPoints;p++){let idx=Math.round(p*step);if(idx<=last)idx=last+1;if(idx>=len)idx=len-1;last=idx;t.push(currentTimes[idx]);y.push(data[idx])}const result={t,y};renderCache.set(cacheKey,result);return result}const bucketCount=Math.max(1,Math.min(len,profile.buckets||len)),t=[],y=[],step=len/bucketCount;for(let b=0;b<bucketCount;b++){const start=Math.floor(b*step),end=Math.min(len,Math.max(start+1,Math.floor((b+1)*step)));let first=-1,last=-1,minI=-1,maxI=-1,minV=Infinity,maxV=-Infinity,sum=0,count=0;for(let i=start;i<end;i++){const v=Number(data[i]);if(!Number.isFinite(v))continue;if(first<0)first=i;last=i;if(v<minV){minV=v;minI=i}if(v>maxV){maxV=v;maxI=i}sum+=v;count++}if(first<0){const gap=Math.min(len-1,Math.floor((start+end)/2));t.push(Number(currentTimes[gap])||0);y.push(null);continue}if(mode==='trend'){const mid=Math.min(len-1,Math.floor((start+end)/2));t.push(currentTimes[mid]);y.push(count?sum/count:null)}else{const indexes=[first,minI,maxI,last].filter((v,i,a)=>v>=0&&a.indexOf(v)===i).sort((a,b)=>a-b);for(const idx of indexes){t.push(currentTimes[idx]);y.push(data[idx])}}}if(mode==='trend'&&y.length>2){const radius=Math.max(1,profile.smooth||5),smoothed=[];for(let i=0;i<y.length;i++){if(y[i]===null){smoothed.push(null);continue}let sum=0,count=0;for(let j=Math.max(0,i-radius);j<=Math.min(y.length-1,i+radius);j++){const v=Number(y[j]);if(Number.isFinite(v)){sum+=v;count++}}smoothed.push(count?sum/count:y[i])}const result={t,y:smoothed};renderCache.set(cacheKey,result);return result}const result={t,y};renderCache.set(cacheKey,result);return result}
function updateLegend(){const key=`${view}|${currentUnit}|${currentSeries.map(s=>s.key||s.name).join(';')}`;if(key===legendKey)return;legendKey=key;legend.innerHTML=currentSeries.slice(0,80).map(s=>`<span><i class='dot' style='background:${s.color}'></i>${esc(s.name)}</span>`).join('')+(currentSeries.length>80?`<span>另外 ${currentSeries.length-80} 条曲线已绘制</span>`:'')}
function ring(title,value,sub,p,color,foot){return `<div class='gauge'><h3>${esc(title)}</h3><div class='ring ${title==='FPS'?'big':''}' style='--p:${pct(p)};--c:${color}'><div><b>${esc(value)}</b><small>${esc(sub)}</small></div></div><div class='foot'>${esc(foot||'')}</div></div>`}
function initStatic(){const h=DATA.hardware||{},hd=DATA.hardwareDerived||{},fs=DATA.frameStats||{},ss=DATA.systemStats||{},targetName=(DATA.target&&(DATA.target.displayName||DATA.target.processName))||'game.exe';setText('gameName',targetName);setText('gameIcon',targetName.replace(/\.exe$/i,'').slice(0,2).toUpperCase()||'FS');document.title=`FrameScope - ${targetName} 性能报告`;setText('hwCpu',h.CpuName||'N/A');setText('hwCore',h.CpuCores?`${h.CpuCores} / ${h.CpuThreads}`:'N/A');setText('hwCpuClock',h.CpuMaxClockMHz?`${h.CpuMaxClockMHz} MHz`:'N/A');setText('hwGpu',h.GpuName||'N/A');setText('hwDriver',h.GpuDriver||'N/A');setText('hwVram',hd.vramTotalGb?`${n(hd.vramTotalGb,1)} GB`:'N/A');setText('hwOs',`${h.OsCaption||'Windows'} ${h.OsArch||''}`.trim());setText('hwMem',hd.totalMemoryGb?`${n(hd.totalMemoryGb,1)} GB`:'N/A');setText('hwSamples',`${DATA.counts.frames} 帧 / ${DATA.counts.processSamples} 次进程采样`);setText('runStart',`开始时间：${DATA.run.startLabel}`);setText('runEnd',`结束时间：${DATA.run.endLabel}`);setText('runDuration',`记录时长：${DATA.run.durationLabel}`);document.getElementById('gauges').innerHTML=[ring('FPS',n(fs.average,0),'平均帧',100,'#29e6ff',''),ring('处理器',`${n(ss.cpuAvg,0)}%`,'占用率',ss.cpuAvg,'#a9ff47',`峰值 ${n(ss.cpuMax,0)}%`),ring('GPU',`${n(ss.gpuAvg,0)}%`,'占用率',ss.gpuAvg,'#29e6ff',`功耗 ${n(ss.powerAvg,0)}W`),ring('显卡温度',`${n(ss.gpuTempAvg,0)}°C`,'温度',ss.gpuTempAvg,'#ffd35b',`GPU频率 ${n(ss.gpuClockAvg,0)}MHz`),ring('显存',`${n(ss.vramUsedPctAvg,0)}%`,`${n(ss.vramUsedAvg,2)}/${n(hd.vramTotalGb,1)} GB`,ss.vramUsedPctAvg,'#a9ff47',''),ring('内存',`${n(ss.memUsedPctAvg,0)}%`,`${n(ss.memUsedAvgGb,1)}/${n(hd.totalMemoryGb,1)} GB`,ss.memUsedPctAvg,'#ffd35b',''),ring('卡顿帧',String(fs.framesOver33??0),'>33ms',Math.min(100,(fs.framesOver33||0)*2),'#ff5d7d',`最大帧时 ${n(fs.maxFrameMs,1)}ms`)].join('');const top=(DATA.process.stats||[]).slice(0,16),max=maxFinite(top.map(p=>p.maxCpu),1);document.getElementById('processRows').innerHTML=top.map(p=>`<div class='row'><span title='${esc(p.name)}'>${esc(p.name)}</span><span>${n(p.maxCpu,1)}%</span><div class='bar'><i style='width:${Math.max(3,Number(p.maxCpu||0)/max*100)}%'></i></div></div>`).join('');const rows=[['平均 FPS',n(fs.average,2)],['1% Low',n(fs.low1,2)],['0.1% Low',n(fs.low01,2)],['最低瞬时 FPS',n(fs.minInstant,3)],['>20ms 帧',fs.framesOver20??0],['>33ms 帧',fs.framesOver33??0],['>100ms 帧',fs.framesOver100??0],['最大帧时间',`${n(fs.maxFrameMs,3)} ms`]];document.getElementById('summaryRows').innerHTML=rows.map(r=>`<div class='row'><span>${esc(r[0])}</span><span>${esc(r[1])}</span><div class='bar'><i style='width:64%'></i></div></div>`).join('')}
function visibleProcessIndexes(){const q=processSearch.value.trim().toLowerCase(),names=DATA.process.names||[],idx=[];for(let i=0;i<names.length;i++)if(!q||String(names[i]).toLowerCase().includes(q))idx.push(i);return idx}
function setOptions(){processSearch.style.display=view==='process'?'':'none';if(view==='process'){metricSelect.innerHTML='<option value=cpu>后台进程 CPU</option><option value=mem>后台进程内存</option>';metricSelect.value=processMetric}else if(view==='fps'){metricSelect.innerHTML='<option value=all>平均 FPS / 1% Low / 0.1% Low</option><option value=avg>只看平均 FPS</option><option value=low1>只看 1% Low</option><option value=low01>只看 0.1% Low</option><option value=min>只看最低瞬时 FPS</option>';metricSelect.value=fpsMetric}else if(view==='perf'){metricSelect.innerHTML='<option value=all>CPU / GPU / 显存频率</option><option value=cpu>CPU 频率</option><option value=gpu>GPU 频率</option><option value=mem>显存频率</option>';metricSelect.value=perfMetric}else if(view==='system'){metricSelect.innerHTML='<option value=all>CPU / GPU / 内存 / 显存占用</option><option value=cpu>CPU 占用</option><option value=gpu>GPU 占用</option><option value=gpuMem>显存控制器</option><option value=mem>内存占用</option><option value=vram>显存占用</option>';metricSelect.value=systemMetric}else{metricSelect.innerHTML='<option value=all>磁盘 / 网络 / 功耗 / 温度</option><option value=diskNet>磁盘 + 网络</option><option value=diskLatency>磁盘延迟</option><option value=powerTemp>GPU 功耗 + 温度</option>';metricSelect.value=ioMetric}}
function setTitle(){const countText=`${DATA.counts.processes} 个后台进程，${DATA.counts.processSamples} 次进程采样`;if(view==='fps'){const pm=DATA.presentMon||{},track=Number(pm.trackCount||0)>1?` 已检测到 ${pm.trackCount} 条 PresentMon 渲染轨道，已自动选择主渲染轨道。`:'';viewTitle.textContent='帧率波动';viewNote.textContent=`平均 FPS 使用 ${DATA.fps.bucketMs||100}ms 采样桶，1% Low / 0.1% Low 使用 ${(DATA.fps.lowWindowMs||2000)/1000}s 滚动窗口。${track}`}if(view==='perf'){viewTitle.textContent='性能图表';viewNote.textContent=DATA.notes.cpuFrequencyCaptured?'每个时间点的 CPU 有效频率、GPU 频率和显存频率。':'本次旧记录未采集 CPU 频率；GPU/显存频率可用。'}if(view==='system'){viewTitle.textContent='系统占用';viewNote.textContent='CPU、GPU、显存控制器、内存和显存占用率。'}if(view==='process'){viewTitle.textContent='后台进程监测';viewNote.textContent=`${countText}。留空搜索框会绘制全部进程，悬停显示该时间点占用最高的进程。`}if(view==='io'){viewTitle.textContent='IO / 温度';viewNote.textContent='磁盘、网络、磁盘延迟、GPU 功耗和温度。'}if(view==='fps'&&!DATA.notes.frameDataCaptured){viewTitle.textContent='帧率数据未捕获';viewNote.textContent='PresentMon 本次没有写入帧数据；本页只保留系统和后台进程诊断数据。'}}
function buildSeries(){if(view==='fps'){currentTimes=DATA.fps.t||[];currentUnit='FPS';const all=[{key:'fps:avg',name:'平均 FPS',color:'#29e6ff',data:DATA.fps.avg||[]},{key:'fps:low1',name:'1% Low',color:'#a9ff47',data:DATA.fps.low1||[]},{key:'fps:low01',name:'0.1% Low',color:'#ffd35b',data:DATA.fps.low01||[]},{key:'fps:min',name:'最低瞬时 FPS',color:'#ff5d7d',data:DATA.fps.min||[]}],map={avg:0,low1:1,low01:2,min:3};currentSeries=fpsMetric==='all'?all:[all[map[fpsMetric]||0]];return}if(view==='perf'){currentTimes=DATA.system.t||[];currentUnit='MHz';const all=[{key:'perf:cpu',name:'CPU 频率',color:'#29e6ff',data:DATA.system.perf.cpuFreq||[]},{key:'perf:gpu',name:'GPU 频率',color:'#a9ff47',data:DATA.system.perf.gpuClock||[]},{key:'perf:mem',name:'显存频率',color:'#ffd35b',data:DATA.system.perf.memClock||[]}],map={cpu:0,gpu:1,mem:2};currentSeries=perfMetric==='all'?all:[all[map[perfMetric]||0]];return}if(view==='system'){currentTimes=DATA.system.t||[];currentUnit='%';const all=[{key:'system:cpu',name:'CPU 占用',color:'#29e6ff',data:DATA.system.usage.cpu||[]},{key:'system:gpu',name:'GPU 占用',color:'#a9ff47',data:DATA.system.usage.gpu||[]},{key:'system:gpuMem',name:'显存控制器',color:'#ffd35b',data:DATA.system.usage.gpuMem||[]},{key:'system:mem',name:'内存占用',color:'#ff5d7d',data:DATA.system.usage.mem||[]},{key:'system:vram',name:'显存占用',color:'#65a7ff',data:DATA.system.usage.vram||[]}],map={cpu:0,gpu:1,gpuMem:2,mem:3,vram:4};currentSeries=systemMetric==='all'?all:[all[map[systemMetric]||0]];return}if(view==='io'){currentTimes=DATA.system.t||[];currentUnit='混合单位';const all=[{key:'io:disk',name:'磁盘 MB/s',color:'#29e6ff',data:DATA.system.io.disk||[]},{key:'io:net',name:'网络 MB/s',color:'#a9ff47',data:DATA.system.io.net||[]},{key:'io:latency',name:'磁盘延迟 ms',color:'#ffd35b',data:DATA.system.io.diskLatency||[]},{key:'io:power',name:'GPU 功耗 W',color:'#ff5d7d',data:DATA.system.io.power||[]},{key:'io:temp',name:'GPU 温度 °C',color:'#65a7ff',data:DATA.system.io.temp||[]}];currentSeries=ioMetric==='diskNet'?[all[0],all[1]]:ioMetric==='diskLatency'?[all[2]]:ioMetric==='powerTemp'?[all[3],all[4]]:all;return}const idxs=visibleProcessIndexes();currentTimes=DATA.process.t||[];currentUnit=processMetric==='cpu'?'CPU %':'MB';const matrix=processMetric==='cpu'?(DATA.process.cpu||[]):DATA.process.mem||[];currentSeries=idxs.map((idx,n)=>({key:`process:${processMetric}:${idx}`,name:DATA.process.names[idx],color:COLORS[n%COLORS.length],data:matrix[idx]||[]}))}
function draw(){if(!DATA)return;setTitle();buildSeries();const{w,h,pw,ph}=chartDims();clearOverlay();ctx.clearRect(0,0,w,h);ctx.fillStyle='#111a24';ctx.fillRect(0,0,w,h);if(!currentTimes.length||!currentSeries.length){ctx.fillStyle='#9fb4c4';ctx.font='15px Segoe UI';ctx.fillText('本视图没有可绘制的数据。',PAD.l,PAD.t+24);legend.innerHTML='';return}const maxT=maxFinite(currentTimes,1);let maxY=maxSeriesValue(currentSeries,0);if(!Number.isFinite(Number(maxY))||Number(maxY)<=0)maxY=1;function x(sec){return PAD.l+(Number(sec)/maxT)*pw}function y(v){return PAD.t+ph-(Number(v)/maxY)*ph}ctx.strokeStyle='rgba(155,205,235,.24)';ctx.lineWidth=1;ctx.fillStyle='#9fb4c4';ctx.font='12px Segoe UI';for(let i=0;i<=6;i++){const yy=PAD.t+ph/6*i;ctx.beginPath();ctx.moveTo(PAD.l,yy);ctx.lineTo(PAD.l+pw,yy);ctx.stroke();ctx.fillText(axisLabel(maxY-(maxY/6)*i,maxY),8,yy+4)}for(let i=0;i<=10;i++){const xx=PAD.l+pw/10*i;ctx.beginPath();ctx.moveTo(xx,PAD.t);ctx.lineTo(xx,PAD.t+ph);ctx.stroke();ctx.fillText(mmss(maxT/10*i),xx-14,h-16)}for(let si=0;si<currentSeries.length;si++){const s=currentSeries[si],alpha=seriesAlpha(si);ctx.strokeStyle=s.color;ctx.globalAlpha=alpha;ctx.lineWidth=lineWidth(si);ctx.beginPath();let started=false;const points=getRenderablePoints(s,pw),len=Math.min(points.t.length,points.y.length);for(let i=0;i<len;i++){const v=points.y[i];if(v===null||!Number.isFinite(Number(v))){started=false;continue}const xx=x(points.t[i]),yy=y(v);if(!started){ctx.moveTo(xx,yy);started=true}else ctx.lineTo(xx,yy)}ctx.stroke();ctx.globalAlpha=1}ctx.strokeStyle='rgba(96,147,179,.78)';ctx.strokeRect(PAD.l,PAD.t,pw,ph);updateLegend()}
function redraw(){tooltip.style.opacity=0;chartBox.classList.add('switching');window.setTimeout(()=>{draw();window.requestAnimationFrame(()=>chartBox.classList.remove('switching'))},70)}
function nearestIndex(times,sec){if(!times.length)return 0;if(sec<=Number(times[0]))return 0;let lo=0,hi=times.length-1;if(sec>=Number(times[hi]))return hi;while(hi-lo>1){const mid=(lo+hi)>>1;if(Number(times[mid])<sec)lo=mid;else hi=mid}return Math.abs(Number(times[lo])-sec)<=Math.abs(Number(times[hi])-sec)?lo:hi}
function hover(evt){const{pw,ph}=chartDims(),rect=chartBox.getBoundingClientRect(),mx=evt.clientX-rect.left,my=evt.clientY-rect.top;if(mx<PAD.l||mx>PAD.l+pw||my<PAD.t||my>PAD.t+ph){tooltip.style.opacity=0;clearOverlay();return}const maxT=maxFinite(currentTimes,1),sec=(mx-PAD.l)/pw*maxT,idx=nearestIndex(currentTimes,sec);clearOverlay();octx.strokeStyle='rgba(255,255,255,.62)';octx.lineWidth=1;octx.beginPath();octx.moveTo(mx,PAD.t);octx.lineTo(mx,PAD.t+ph);octx.stroke();let rows=[];if(view==='process'){for(const s of currentSeries){const v=s.data[idx];if(v!==null&&Number.isFinite(Number(v)))rows.push({name:s.name,value:v,color:s.color})}rows.sort((a,b)=>Number(b.value)-Number(a.value));rows=rows.slice(0,26)}else rows=currentSeries.map(s=>({name:s.name,value:s.data[idx],color:s.color})).filter(r=>r.value!==null&&Number.isFinite(Number(r.value)));tooltip.innerHTML=`<b>${mmss(currentTimes[idx])}</b><br>`+rows.map(r=>`<span style='color:${r.color}'>■</span> ${esc(r.name)}: ${n(r.value,view==='process'&&processMetric==='mem'?1:2)} ${currentUnit}`).join('<br>');tooltip.style.left=Math.min(rect.width-470,Math.max(8,mx+14))+'px';tooltip.style.top=Math.max(8,my+14)+'px';tooltip.style.opacity=1}
function scheduleHover(evt){pendingHoverEvent=evt;if(hoverFrame)return;hoverFrame=window.requestAnimationFrame(()=>{hoverFrame=0;const e=pendingHoverEvent;pendingHoverEvent=null;if(e)hover(e)})}
if(DATA){document.querySelectorAll('.tab').forEach(btn=>btn.addEventListener('click',()=>{document.querySelectorAll('.tab').forEach(b=>b.classList.remove('active'));btn.classList.add('active');view=btn.dataset.view;setOptions();redraw()}));metricSelect.addEventListener('change',()=>{if(view==='fps')fpsMetric=metricSelect.value;else if(view==='perf')perfMetric=metricSelect.value;else if(view==='system')systemMetric=metricSelect.value;else if(view==='io')ioMetric=metricSelect.value;else processMetric=metricSelect.value;redraw()});processSearch.addEventListener('input',()=>{renderCache.clear();legendKey='';draw()});readMode.addEventListener('change',()=>{renderCache.clear();redraw()});widthScale.addEventListener('input',applyWidth);document.getElementById('fitWidth').addEventListener('click',()=>{widthScale.value=1;applyWidth()});chartBox.addEventListener('mousemove',scheduleHover);chartBox.addEventListener('mouseleave',()=>{tooltip.style.opacity=0;clearOverlay()});window.addEventListener('resize',()=>applyWidth());initStatic();setOptions();applyWidth()}
</script>
</body>
</html>";
    }
}
