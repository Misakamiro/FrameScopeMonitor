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
}
