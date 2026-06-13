using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Web.Script.Serialization;

internal static partial class FrameScopeReportGenerator
{
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

    private static Dictionary<string, object> ReadCpuCoreCharts(string runDir, DateTime start, Dictionary<string, object> metadata)
    {
        string path = Path.Combine(runDir, "cpu-core-samples.csv");
        Dictionary<string, object> frequency = EmptyCpuCoreChart("MHz", "本次报告没有 cpu-core-samples.csv 或 Actual Frequency 数据。");
        Dictionary<string, object> voltage = ReadCpuVoltageChart(runDir, start, metadata);
        Dictionary<string, object> vid = ReadCpuVidChart(runDir, start, metadata);
        if (!File.Exists(path))
        {
            return new Dictionary<string, object> { { "frequency", frequency }, { "voltage", voltage }, { "vid", vid } };
        }

        SortedDictionary<double, Dictionary<string, CpuCoreBucketValue>> frequencyPoints = new SortedDictionary<double, Dictionary<string, CpuCoreBucketValue>>();
        SortedSet<string> frequencyCores = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int rowCount = 0;

        using (CsvTable table = CsvTable.Open(path))
        {
            Dictionary<string, int> h = table.Headers;
            List<string> row;
            while ((row = table.ReadRow()) != null)
            {
                double pointTime;
                if (!TryCpuCorePointTime(row, h, start, out pointTime)) continue;
                string coreKey = CpuCoreKey(Get(row, h, "ProcessorGroup"), Get(row, h, "LogicalProcessor"));
                if (String.IsNullOrWhiteSpace(coreKey)) continue;

                double? actual = ParseNullableDouble(Get(row, h, "ActualFrequencyMHz"));
                if (actual.HasValue)
                {
                    AddCpuCorePoint(frequencyPoints, pointTime, coreKey, actual.Value);
                    frequencyCores.Add(coreKey);
                    rowCount++;
                }
            }
        }

        if (frequencyCores.Count > 0)
        {
            frequency = BuildCpuCoreChart("MHz", frequencyPoints, frequencyCores, 0, true, "");
            frequency["sampleCount"] = Math.Max(rowCount, GetIntDiagnostic(metadata, "cpuCoreSampleCount", rowCount));
        }

        return new Dictionary<string, object> { { "frequency", frequency }, { "voltage", voltage }, { "vid", vid } };
    }

    private static Dictionary<string, object> ReadCpuVoltageChart(string runDir, DateTime start, Dictionary<string, object> metadata)
    {
        string dedicatedPath = Path.Combine(runDir, "cpu-voltage-samples.csv");
        if (File.Exists(dedicatedPath))
        {
            Dictionary<string, object> chart = ReadCpuVoltageChartFromCsv(dedicatedPath, start, metadata);
            if (Convert.ToBoolean(chart["available"], CultureInfo.InvariantCulture)) return chart;
        }

        string legacyPath = Path.Combine(runDir, "cpu-core-samples.csv");
        if (File.Exists(legacyPath))
        {
            Dictionary<string, object> legacy = ReadCpuVoltageChartFromCsv(legacyPath, start, metadata);
            if (Convert.ToBoolean(legacy["available"], CultureInfo.InvariantCulture)) return legacy;
        }

        Dictionary<string, object> empty = EmptyCpuCoreChart("V", CpuVoltageUnavailableReason(metadata));
        AddCpuVoltageChartMetadata(empty, metadata);
        return empty;
    }

    private static Dictionary<string, object> ReadCpuVoltageChartFromCsv(string path, DateTime start, Dictionary<string, object> metadata)
    {
        Dictionary<string, object> voltage = EmptyCpuCoreChart("V", CpuVoltageUnavailableReason(metadata));
        SortedDictionary<double, CpuCoreBucketValue> voltagePoints = new SortedDictionary<double, CpuCoreBucketValue>();
        string voltageHeader = "";
        string source = GetStringDiagnostic(metadata, "cpuVoltageSource", "");
        int rowCount = 0;

        using (CsvTable table = CsvTable.Open(path))
        {
            Dictionary<string, int> h = table.Headers;
            voltageHeader = FindVoltageHeader(h);
            if (String.IsNullOrWhiteSpace(voltageHeader)) return voltage;

            List<string> row;
            while ((row = table.ReadRow()) != null)
            {
                double pointTime;
                if (!TryCpuCorePointTime(row, h, start, out pointTime)) continue;
                string status = (Get(row, h, "Status") ?? "").Trim();
                string sensorName = Get(row, h, "SensorName");
                string sensorIdentifier = Get(row, h, "SensorIdentifier");
                if (!IsCpuVcoreVoltageCsvRow(sensorName, sensorIdentifier, status)) continue;
                double? volts = ParseNullableDouble(Get(row, h, voltageHeader));
                if (!volts.HasValue || volts.Value <= 0 || volts.Value >= 5) continue;

                AddCpuVoltagePoint(voltagePoints, pointTime, volts.Value);
                rowCount++;
                if (String.IsNullOrWhiteSpace(source)) source = Get(row, h, "Source");
            }
        }

        if (voltagePoints.Count == 0) return voltage;

        voltage = BuildCpuVoltageVcoreChart(voltagePoints, "");
        voltage["sourceField"] = voltageHeader;
        voltage["source"] = source;
        voltage["sampleCount"] = Math.Max(rowCount, GetIntDiagnostic(metadata, "cpuVoltageVcoreSampleCount", rowCount));
        AddCpuVoltageChartMetadata(voltage, metadata);
        return voltage;
    }

    private static Dictionary<string, object> ReadCpuVidChart(string runDir, DateTime start, Dictionary<string, object> metadata)
    {
        string path = Path.Combine(runDir, "cpu-vid-samples.csv");
        Dictionary<string, object> vid = EmptyCpuCoreChart("V", CpuVidUnavailableReason(metadata));
        AddCpuVidChartMetadata(vid, metadata);
        if (!File.Exists(path)) return vid;

        SortedDictionary<double, Dictionary<string, CpuCoreBucketValue>> points = new SortedDictionary<double, Dictionary<string, CpuCoreBucketValue>>();
        SortedSet<string> cores = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string source = GetStringDiagnostic(metadata, "cpuVidSource", "");
        int rowCount = 0;

        using (CsvTable table = CsvTable.Open(path))
        {
            Dictionary<string, int> h = table.Headers;
            string vidHeader = FindVidHeader(h);
            if (String.IsNullOrWhiteSpace(vidHeader)) return vid;

            List<string> row;
            while ((row = table.ReadRow()) != null)
            {
                double pointTime;
                if (!TryCpuCorePointTime(row, h, start, out pointTime)) continue;
                string status = (Get(row, h, "Status") ?? "").Trim();
                if (!String.IsNullOrWhiteSpace(status) &&
                    !String.Equals(status, "core-vid", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(status, "vid", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string coreKey = CpuVidCoreKey(Get(row, h, "ProcessorGroup"), Get(row, h, "LogicalProcessor"), Get(row, h, "CoreIndex"));
                if (String.IsNullOrWhiteSpace(coreKey)) continue;
                double? volts = ParseNullableDouble(Get(row, h, vidHeader));
                if (!volts.HasValue || volts.Value <= 0 || volts.Value >= 5) continue;

                AddCpuCorePoint(points, pointTime, coreKey, volts.Value);
                cores.Add(coreKey);
                string sensorName = Get(row, h, "SensorName");
                names[coreKey] = CpuVidDisplayName(sensorName, coreKey);
                rowCount++;
                if (String.IsNullOrWhiteSpace(source)) source = Get(row, h, "Source");
            }
        }

        if (cores.Count == 0) return vid;

        vid = BuildCpuVidChart(points, cores, names, "");
        vid["sourceField"] = "VidVolts";
        vid["source"] = source;
        vid["sampleCount"] = Math.Max(rowCount, GetIntDiagnostic(metadata, "cpuVidSampleCount", rowCount));
        vid["coreCount"] = Math.Max(cores.Count, GetIntDiagnostic(metadata, "cpuVidCoreCount", cores.Count));
        AddCpuVidChartMetadata(vid, metadata);
        return vid;
    }

    private static void AddCpuVoltageChartMetadata(Dictionary<string, object> voltage, Dictionary<string, object> metadata)
    {
        if (voltage == null) return;
        voltage["status"] = GetStringDiagnostic(metadata, "cpuVoltageStatus", "");
        string metadataSource = GetStringDiagnostic(metadata, "cpuVoltageSource", "");
        if (!String.IsNullOrWhiteSpace(metadataSource) || !voltage.ContainsKey("source"))
        {
            voltage["source"] = metadataSource;
        }
        voltage["providerKind"] = GetStringDiagnostic(metadata, "cpuVoltageProviderKind", "");
        voltage["providerRequested"] = GetStringDiagnostic(metadata, "cpuVoltageProviderRequested", "");
        voltage["totalSampleCount"] = GetIntDiagnostic(metadata, "cpuVoltageSampleCount", 0);
        voltage["vcoreSampleCount"] = GetIntDiagnostic(metadata, "cpuVoltageVcoreSampleCount", GetIntDiagnostic(metadata, "cpuVoltageSampleCount", 0));
        voltage["perCoreSampleCount"] = GetIntDiagnostic(metadata, "cpuVoltagePerCoreSampleCount", 0);
        voltage["nonPerCoreSampleCount"] = GetIntDiagnostic(metadata, "cpuVoltageNonPerCoreSampleCount", 0);
        voltage["rejectedSampleCount"] = GetIntDiagnostic(metadata, "cpuVoltageRejectedSampleCount", GetIntDiagnostic(metadata, "cpuVoltageNonPerCoreSampleCount", 0));
        voltage["sampleIntervalMs"] = GetIntDiagnostic(metadata, "cpuVoltageSampleIntervalMs", 0);
        voltage["samplesCsv"] = GetStringDiagnostic(metadata, "cpuVoltageSamplesCsv", "");
        if (!voltage.ContainsKey("sampleCount"))
        {
            voltage["sampleCount"] = GetIntDiagnostic(metadata, "cpuVoltageSampleCount", 0);
        }
    }

    private static void AddCpuVidChartMetadata(Dictionary<string, object> vid, Dictionary<string, object> metadata)
    {
        if (vid == null) return;
        vid["status"] = GetStringDiagnostic(metadata, "cpuVidStatus", "");
        string metadataSource = GetStringDiagnostic(metadata, "cpuVidSource", "");
        if (!String.IsNullOrWhiteSpace(metadataSource) || !vid.ContainsKey("source"))
        {
            vid["source"] = metadataSource;
        }
        vid["providerKind"] = GetStringDiagnostic(metadata, "cpuVidProviderKind", "");
        vid["providerRequested"] = GetStringDiagnostic(metadata, "cpuVidProviderRequested", "");
        vid["note"] = LocalizeCpuVidNote(GetStringDiagnostic(metadata, "cpuVidNote", ""));
        vid["sampleIntervalMs"] = GetIntDiagnostic(metadata, "cpuVidSampleIntervalMs", 0);
        vid["samplesCsv"] = GetStringDiagnostic(metadata, "cpuVidSamplesCsv", "");
        if (!vid.ContainsKey("sampleCount"))
        {
            vid["sampleCount"] = GetIntDiagnostic(metadata, "cpuVidSampleCount", 0);
        }
        if (!vid.ContainsKey("coreCount"))
        {
            vid["coreCount"] = GetIntDiagnostic(metadata, "cpuVidCoreCount", 0);
        }
    }

    private static Dictionary<string, object> EmptyCpuCoreChart(string unit, string reason)
    {
        return new Dictionary<string, object>
        {
            { "available", false },
            { "unit", unit },
            { "t", new List<double>() },
            { "series", new List<Dictionary<string, object>>() },
            { "reason", reason }
        };
    }

    private static Dictionary<string, object> BuildCpuCoreChart(string unit, SortedDictionary<double, Dictionary<string, CpuCoreBucketValue>> points, SortedSet<string> cores, int digits, bool available, string reason)
    {
        List<double> orderedTimes = points.Keys.OrderBy(v => v).ToList();
        List<double> times = orderedTimes.Select(v => RoundDouble(v, 3)).ToList();
        List<Dictionary<string, object>> series = new List<Dictionary<string, object>>();
        int colorIndex = 0;
        foreach (string core in cores.OrderBy(CpuCoreSortKey))
        {
            List<double?> data = new List<double?>();
            foreach (double pointTime in orderedTimes)
            {
                CpuCoreBucketValue value;
                Dictionary<string, CpuCoreBucketValue> perCore;
                double? avg = points.TryGetValue(pointTime, out perCore) && perCore.TryGetValue(core, out value)
                    ? value.Average()
                    : (double?)null;
                data.Add(RoundNullable(avg, digits));
            }
            series.Add(new Dictionary<string, object>
            {
                { "key", "cpu-core:" + core },
                { "name", "CPU 核心 " + core },
                { "color", Colors[colorIndex % Colors.Length] },
                { "data", data }
            });
            colorIndex++;
        }

        return new Dictionary<string, object>
        {
            { "available", available && series.Count > 0 },
            { "unit", unit },
            { "t", times },
            { "series", series },
            { "reason", reason ?? "" }
        };
    }

    private static Dictionary<string, object> BuildCpuVidChart(SortedDictionary<double, Dictionary<string, CpuCoreBucketValue>> points, SortedSet<string> cores, Dictionary<string, string> names, string reason)
    {
        List<double> orderedTimes = points.Keys.OrderBy(v => v).ToList();
        List<double> times = orderedTimes.Select(v => RoundDouble(v, 3)).ToList();
        List<Dictionary<string, object>> series = new List<Dictionary<string, object>>();
        int colorIndex = 0;
        foreach (string core in cores.OrderBy(CpuCoreSortKey))
        {
            List<double?> data = new List<double?>();
            foreach (double pointTime in orderedTimes)
            {
                CpuCoreBucketValue value;
                Dictionary<string, CpuCoreBucketValue> perCore;
                double? avg = points.TryGetValue(pointTime, out perCore) && perCore.TryGetValue(core, out value)
                    ? value.Average()
                    : (double?)null;
                data.Add(RoundNullable(avg, 3));
            }

            string name;
            if (names == null || !names.TryGetValue(core, out name) || String.IsNullOrWhiteSpace(name)) name = CpuVidDisplayName(core);
            series.Add(new Dictionary<string, object>
            {
                { "key", "cpu-vid:" + core },
                { "name", name },
                { "color", Colors[colorIndex % Colors.Length] },
                { "data", data }
            });
            colorIndex++;
        }

        return new Dictionary<string, object>
        {
            { "available", series.Count > 0 },
            { "unit", "V" },
            { "t", times },
            { "series", series },
            { "reason", reason ?? "" }
        };
    }

    private static Dictionary<string, object> BuildCpuVoltageVcoreChart(SortedDictionary<double, CpuCoreBucketValue> points, string reason)
    {
        List<double> orderedTimes = points.Keys.OrderBy(v => v).ToList();
        List<double> times = orderedTimes.Select(v => RoundDouble(v, 3)).ToList();
        List<double?> data = new List<double?>();
        foreach (double pointTime in orderedTimes)
        {
            CpuCoreBucketValue value;
            data.Add(points.TryGetValue(pointTime, out value) ? RoundNullable(value.Average(), 3) : null);
        }

        return new Dictionary<string, object>
        {
            { "available", data.Count > 0 },
            { "unit", "V" },
            { "t", times },
            { "series", new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>
                    {
                        { "key", "cpu-voltage:vcore" },
                        { "name", "CPU 电压 / Vcore" },
                        { "color", "#009dff" },
                        { "data", data }
                    }
                }
            },
            { "reason", reason ?? "" }
        };
    }

    private static void AddCpuCorePoint(SortedDictionary<double, Dictionary<string, CpuCoreBucketValue>> points, double pointTime, string coreKey, double value)
    {
        Dictionary<string, CpuCoreBucketValue> perCore;
        if (!points.TryGetValue(pointTime, out perCore))
        {
            perCore = new Dictionary<string, CpuCoreBucketValue>(StringComparer.OrdinalIgnoreCase);
            points[pointTime] = perCore;
        }

        CpuCoreBucketValue bucketValue;
        if (!perCore.TryGetValue(coreKey, out bucketValue))
        {
            bucketValue = new CpuCoreBucketValue();
            perCore[coreKey] = bucketValue;
        }
        bucketValue.Add(value);
    }

    private static void AddCpuVoltagePoint(SortedDictionary<double, CpuCoreBucketValue> points, double pointTime, double value)
    {
        CpuCoreBucketValue bucketValue;
        if (!points.TryGetValue(pointTime, out bucketValue))
        {
            bucketValue = new CpuCoreBucketValue();
            points[pointTime] = bucketValue;
        }
        bucketValue.Add(value);
    }

    private static bool TryCpuCorePointTime(List<string> row, Dictionary<string, int> headers, DateTime start, out double pointTime)
    {
        pointTime = 0;
        double? elapsedMs = ParseNullableDouble(Get(row, headers, "ElapsedMs"));
        if (elapsedMs.HasValue)
        {
            pointTime = RoundDouble(Math.Max(0, elapsedMs.Value / 1000.0), 3);
            return true;
        }

        DateTime time;
        if (TryParseDate(Get(row, headers, "Time"), out time))
        {
            pointTime = RoundDouble(Math.Max(0, (time - start).TotalSeconds), 3);
            return true;
        }

        return false;
    }

    private static string CpuCoreKey(string group, string logical)
    {
        group = (group ?? "").Trim();
        logical = (logical ?? "").Trim();
        if (String.IsNullOrWhiteSpace(group) && String.IsNullOrWhiteSpace(logical)) return "";
        if (String.IsNullOrWhiteSpace(group) || group == "0") return logical;
        return group + ":" + logical;
    }

    private static string CpuVoltageCoreKey(string group, string logical, string coreId)
    {
        group = (group ?? "").Trim();
        logical = (logical ?? "").Trim();
        coreId = (coreId ?? "").Trim();
        if (String.IsNullOrWhiteSpace(logical) && !String.IsNullOrWhiteSpace(coreId)) return "core:" + coreId;
        return CpuCoreKey(group, logical);
    }

    private static string CpuVidCoreKey(string group, string logical, string coreIndex)
    {
        group = (group ?? "").Trim();
        logical = (logical ?? "").Trim();
        coreIndex = (coreIndex ?? "").Trim();
        if (String.IsNullOrWhiteSpace(logical) && !String.IsNullOrWhiteSpace(coreIndex)) return "core:" + coreIndex;
        return CpuCoreKey(group, logical);
    }

    private static string CpuVidDisplayName(string core)
    {
        if (String.IsNullOrWhiteSpace(core)) return "核心 VID";
        string value = core;
        if (value.StartsWith("core:", StringComparison.OrdinalIgnoreCase)) value = value.Substring(5);
        int parsed;
        if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return "核心 #" + (parsed + 1).ToString(CultureInfo.InvariantCulture) + " VID";
        }
        return "核心 " + core + " VID";
    }

    private static string CpuVidDisplayName(string sensorName, string core)
    {
        string name = (sensorName ?? "").Trim();
        if (String.IsNullOrWhiteSpace(name)) return CpuVidDisplayName(core);
        if (name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase))
        {
            return "CPU 核心" + name.Substring("CPU Core".Length);
        }
        if (name.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
        {
            return "核心" + name.Substring("Core".Length);
        }
        return name;
    }

    private static string CpuCoreSortKey(string core)
    {
        if (String.IsNullOrWhiteSpace(core)) return "";
        string[] parts = core.Split(':');
        int group = 0;
        int logical = 0;
        if (parts.Length == 2)
        {
            Int32.TryParse(parts[0], out group);
            Int32.TryParse(parts[1], out logical);
        }
        else
        {
            Int32.TryParse(core, out logical);
        }
        return group.ToString("D5", CultureInfo.InvariantCulture) + ":" + logical.ToString("D5", CultureInfo.InvariantCulture);
    }

    private static string FindVoltageHeader(Dictionary<string, int> headers)
    {
        foreach (string name in new[] { "VoltageVolts", "CoreVoltageV", "CpuCoreVoltageV", "ActualVoltageV", "SensorVoltageV", "VoltageV" })
        {
            if (headers.ContainsKey(name)) return name;
        }
        return "";
    }

    private static bool IsCpuVcoreVoltageCsvRow(string sensorName, string sensorIdentifier, string status)
    {
        string normalizedStatus = (status ?? "").Trim().ToLowerInvariant();
        string text = NormalizeCpuVoltageSensorText((sensorName ?? "") + " " + (sensorIdentifier ?? ""));
        if (normalizedStatus == "core-vid" || normalizedStatus == "cpu-core-vid" || normalizedStatus == "vid") return false;
        if (text.IndexOf("vid", StringComparison.Ordinal) >= 0) return false;
        if (ContainsRejectedCpuVoltageCsvToken(text)) return false;
        if (normalizedStatus == "vcore" || normalizedStatus == "cpu-vcore" || normalizedStatus == "cpu-voltage") return true;
        return IsExplicitCpuVcoreCsvSensor(sensorName, sensorIdentifier);
    }

    private static bool IsRejectedCpuVoltageCsvRow(string sensorName, string sensorIdentifier, string status)
    {
        string normalizedStatus = (status ?? "").Trim().ToLowerInvariant();
        if (normalizedStatus == "core-vid" || normalizedStatus == "cpu-core-vid" || normalizedStatus == "vid") return true;
        return !IsCpuVcoreVoltageCsvRow(sensorName, sensorIdentifier, status);
    }

    private static bool IsExplicitCpuVcoreCsvSensor(string sensorName, string sensorIdentifier)
    {
        string sensor = NormalizeCpuVoltageSensorText(sensorName);
        string text = NormalizeCpuVoltageSensorText((sensorName ?? "") + " " + (sensorIdentifier ?? ""));
        if (String.IsNullOrWhiteSpace(sensor) && String.IsNullOrWhiteSpace(text)) return false;
        if (ContainsRejectedCpuVoltageCsvToken(text)) return false;
        if (ExtractCpuVoltageCsvCoreIndex(text).HasValue) return false;
        if (sensor == "vcore" || sensor == "cpu vcore" || sensor == "cpu core") return true;
        if (sensor.IndexOf("vcore", StringComparison.Ordinal) >= 0) return true;
        if (sensor.IndexOf("cpu voltage", StringComparison.Ordinal) >= 0) return true;
        if (sensor.IndexOf("cpu core voltage", StringComparison.Ordinal) >= 0) return true;
        if (sensor.IndexOf("cpu core svi2", StringComparison.Ordinal) >= 0) return true;
        if (sensor.IndexOf("vddcr cpu", StringComparison.Ordinal) >= 0) return true;
        return text.IndexOf("vddcr cpu", StringComparison.Ordinal) >= 0;
    }

    private static string NormalizeCpuVoltageSensorText(string text)
    {
        if (String.IsNullOrWhiteSpace(text)) return "";
        StringBuilder builder = new StringBuilder(text.Length);
        bool previousSpace = false;
        string lower = text.ToLowerInvariant();
        for (int i = 0; i < lower.Length; i++)
        {
            char ch = lower[i];
            if (Char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousSpace = false;
            }
            else if (!previousSpace)
            {
                builder.Append(' ');
                previousSpace = true;
            }
        }
        return builder.ToString().Trim();
    }

    private static bool ContainsRejectedCpuVoltageCsvToken(string normalizedText)
    {
        string text = NormalizeCpuVoltageSensorText(normalizedText);
        if (String.IsNullOrWhiteSpace(text)) return false;
        if (text.IndexOf("vid", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("soc", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("package", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("vbat", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("vin", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("battery", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("gpu", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("dram", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("ddr", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("memory", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("chipset", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("misc", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("3 3v", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("5v", StringComparison.Ordinal) >= 0) return true;
        if (text.IndexOf("12v", StringComparison.Ordinal) >= 0) return true;
        if (ExtractCpuVoltageCsvCoreIndex(text).HasValue) return true;
        return false;
    }

    private static int? ExtractCpuVoltageCsvCoreIndex(string text)
    {
        if (String.IsNullOrWhiteSpace(text)) return null;
        string lower = text.ToLowerInvariant();
        int coreIndex = lower.IndexOf("core", StringComparison.Ordinal);
        if (coreIndex < 0) return null;

        int start = -1;
        for (int i = coreIndex + 4; i < lower.Length; i++)
        {
            if (Char.IsDigit(lower[i]))
            {
                start = i;
                break;
            }
            if (Char.IsLetter(lower[i])) return null;
        }
        if (start < 0) return null;

        int end = start;
        while (end < lower.Length && Char.IsDigit(lower[end])) end++;
        int value;
        return Int32.TryParse(lower.Substring(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            ? (int?)value
            : null;
    }

    private static string FindVidHeader(Dictionary<string, int> headers)
    {
        foreach (string name in new[] { "VidVolts", "VidV", "CoreVidV", "CpuCoreVidV" })
        {
            if (headers.ContainsKey(name)) return name;
        }
        return "";
    }

    private static string CpuVidUnavailableReason(Dictionary<string, object> metadata)
    {
        object reason;
        if (metadata != null && metadata.TryGetValue("cpuVidReason", out reason) && reason != null)
        {
            string text = Convert.ToString(reason, CultureInfo.InvariantCulture);
            if (!String.IsNullOrWhiteSpace(text)) return LocalizeCpuVidReason(text);
        }
        return "\u672a\u68c0\u6d4b\u5230 CPU \u6838\u5fc3 VID \u4f20\u611f\u5668\uff1b\u4e0d\u751f\u6210\u5047\u6570\u636e\u3002";
    }

    private static string CpuVoltageUnavailableReason(Dictionary<string, object> metadata)
    {
        object reason;
        if (metadata != null && metadata.TryGetValue("cpuVoltageReason", out reason) && reason != null)
        {
            string text = Convert.ToString(reason, CultureInfo.InvariantCulture);
            if (!String.IsNullOrWhiteSpace(text)) return LocalizeCpuVoltageReason(text);
        }
        return "未记录明确的 CPU Vcore/CPU Voltage 传感器；VID/SOC/Package/VBAT/VIN 不会作为 CPU 电压使用。";
    }

    private static string LocalizeCpuVoltageReason(string reason)
    {
        string text = reason ?? "";
        if (String.IsNullOrWhiteSpace(text)) return "未记录明确的 CPU Vcore/CPU Voltage 传感器；VID/SOC/Package/VBAT/VIN 不会作为 CPU 电压使用。";
        if (text.IndexOf("No explicit CPU Vcore/CPU Voltage sensor was recorded", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "未记录明确的 CPU Vcore/CPU Voltage 传感器；VID/SOC/Package/VBAT/VIN 不会作为 CPU 电压使用。";
        }
        if (text.IndexOf("CPU Voltage / Vcore telemetry status was not recorded", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("CPU Voltage / Vcore telemetry was not recorded in this status file", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "未记录 CPU 电压 / Vcore 遥测状态；VID 不会作为 Vcore 使用。";
        }
        if (text.IndexOf("CPU Voltage / Vcore telemetry is disabled", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "CPU 电压 / Vcore 遥测已禁用。";
        }
        return text;
    }

    private static string LocalizeCpuVidReason(string reason)
    {
        string text = reason ?? "";
        if (String.IsNullOrWhiteSpace(text)) return "\u672a\u68c0\u6d4b\u5230 CPU \u6838\u5fc3 VID \u4f20\u611f\u5668\uff1b\u4e0d\u751f\u6210\u5047\u6570\u636e\u3002";
        if (text.IndexOf("No Core VID sensors", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("CPU Core VID telemetry provider is missing", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("CPU Core VID telemetry is disabled", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("CPU Core VID telemetry status was not recorded", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "\u672a\u68c0\u6d4b\u5230 CPU \u6838\u5fc3 VID \u4f20\u611f\u5668\uff1b\u4e0d\u751f\u6210\u5047\u6570\u636e\u3002";
        }
        return text;
    }

    private static string LocalizeCpuVidNote(string note)
    {
        string text = note ?? "";
        if (String.IsNullOrWhiteSpace(text) ||
            text.IndexOf("VID is CPU request/target voltage", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("VID \u662f CPU \u8bf7\u6c42/\u76ee\u6807\u7535\u538b", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "VID 是 CPU 每核心请求/目标电压，不是真实 Vcore；它与 CPU 电压 / Vcore 分开显示。";
        }
        return text;
    }
}
