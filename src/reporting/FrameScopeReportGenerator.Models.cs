using System;
using System.Collections.Generic;

internal static partial class FrameScopeReportGenerator
{
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
}
