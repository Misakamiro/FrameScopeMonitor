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
        HashSet<string> samplingInstants = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, ProcessStat> stats = new Dictionary<string, ProcessStat>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ProcessRleSeriesBuilder> cpuBuilders = new Dictionary<string, ProcessRleSeriesBuilder>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ProcessRleSeriesBuilder> memBuilders = new Dictionary<string, ProcessRleSeriesBuilder>(StringComparer.OrdinalIgnoreCase);

        using (CsvTable table = CsvTable.Open(path))
        {
            Dictionary<string, int> h = table.Headers;
            int[] columns = new[]
            {
                HeaderIndex(h, "ProcessName"),
                HeaderIndex(h, "Time"),
                HeaderIndex(h, "SampleIndex"),
                HeaderIndex(h, "CpuPct"),
                HeaderIndex(h, "WorkingSetMB")
            };
            string[] row;
            while ((row = table.ReadFields(columns)) != null)
            {
                string name = row[0].Trim();
                if (string.IsNullOrEmpty(name)) continue;
                DateTime t;
                if (!TryParseDate(row[1], out t)) continue;
                string sampleIndex = row[2];
                string normalizedSampleIndex = sampleIndex.Trim();
                string samplingInstantKey = !string.IsNullOrEmpty(normalizedSampleIndex)
                    ? "index:" + normalizedSampleIndex
                    : "time:" + t.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
                samplingInstants.Add(samplingInstantKey);
                if (!string.IsNullOrEmpty(targetBase) && name.Equals(targetBase, StringComparison.OrdinalIgnoreCase)) continue;
                if (!sampleMap.ContainsKey(samplingInstantKey))
                {
                    sampleMap[samplingInstantKey] = result.Times.Count;
                    result.Times.Add(RoundDouble((t - start).TotalSeconds, 3));
                }
                double? cpu = ParseNullableDouble(row[3]);
                double? mem = ParseNullableDouble(row[4]);
                ProcessStat stat;
                if (!stats.TryGetValue(name, out stat))
                {
                    stat = new ProcessStat { Name = name };
                    stats[name] = stat;
                    cpuBuilders[name] = new ProcessRleSeriesBuilder();
                    memBuilders[name] = new ProcessRleSeriesBuilder();
                }
                stat.Samples++;
                if (cpu.HasValue)
                {
                    stat.MaxCpu = Math.Max(stat.MaxCpu, cpu.Value);
                    stat.CpuSum += cpu.Value;
                    stat.CpuSamples++;
                }
                if (mem.HasValue) stat.MaxMem = Math.Max(stat.MaxMem, mem.Value);
                int position = sampleMap[samplingInstantKey];
                cpuBuilders[name].AppendAt(position, cpu, 2);
                memBuilders[name].AppendAt(position, mem, 1);
            }
        }

        result.SamplingInstantCount = samplingInstants.Count;
        List<ProcessStat> ordered = stats.Values.OrderByDescending(s => s.MaxCpu).ThenByDescending(s => s.MaxMem).ThenByDescending(s => s.Samples).ToList();
        result.Codec = "rle-v1";
        result.Names = ordered.Select(s => s.Name).ToList();
        int nTimes = result.Times.Count;
        for (int i = 0; i < ordered.Count; i++)
        {
            ProcessStat stat = ordered[i];
            result.Stats.Add(new Dictionary<string, object>
            {
                { "name", stat.Name },
                { "maxCpu", Round(stat.MaxCpu, 2) },
                { "avgCpu", stat.CpuSamples > 0 ? Round(stat.CpuSum / stat.CpuSamples, 2) : 0 },
                { "maxMem", Round(stat.MaxMem, 1) },
                { "samples", stat.Samples }
            });
            result.Cpu.Add(cpuBuilders[stat.Name].Finish(nTimes));
            result.Mem.Add(memBuilders[stat.Name].Finish(nTimes));
        }

        return result;
    }
}
