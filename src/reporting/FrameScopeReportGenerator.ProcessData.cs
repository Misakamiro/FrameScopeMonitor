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
                if (!string.IsNullOrEmpty(targetBase) && name.Equals(targetBase, StringComparison.OrdinalIgnoreCase)) continue;
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
}
