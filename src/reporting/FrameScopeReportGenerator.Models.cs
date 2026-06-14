using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

internal static partial class FrameScopeReportGenerator
{
    private struct PresentFrame
    {
        public DateTime Time;
        public double FrameMs;
        public bool IsHardware;
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
        public List<PresentFrame> Frames = new List<PresentFrame>();

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

    private sealed class FrameStatsSummary
    {
        public int Count;
        public double SumMs;
        public double MinMs = Double.MaxValue;
        public double MaxMs = Double.MinValue;
        public int FramesOver20;
        public int FramesOver33;
        public int FramesOver100;
        public double? Low1Fps;
        public double? Low01Fps;
    }

    private sealed class FpsBucketAccumulator
    {
        public double SumMs;
        public int Count;
    }

    private sealed class CpuCoreBucketValue
    {
        public double Sum;
        public int Count;

        public void Add(double value)
        {
            Sum += value;
            Count++;
        }

        public double? Average()
        {
            return Count > 0 ? Sum / Count : (double?)null;
        }
    }

    private sealed class CpuVoltageCsvCounts
    {
        public int Total;
        public int Vcore;
        public int NonPerCore;
        public int Rejected;
    }

    private sealed class CpuVidCsvCounts
    {
        public int Total;
        public int CoreCount;
        public int Rejected;
    }

    private sealed class ProcessMatrixResult
    {
        public string Codec = "";
        public List<double> Times = new List<double>();
        public List<string> Names = new List<string>();
        public List<string> Cpu = new List<string>();
        public List<string> Mem = new List<string>();
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

    private sealed class ProcessRleSeriesBuilder
    {
        private readonly StringBuilder builder = new StringBuilder();
        private string currentToken = null;
        private int currentCount;

        public int Length { get; private set; }

        public void AppendAt(int index, double? value, int digits)
        {
            if (index < Length) return;
            PadTo(index);
            Append(value, digits);
        }

        public void PadTo(int count)
        {
            AppendToken("n", count - Length);
        }

        public string Finish(int count)
        {
            PadTo(count);
            Flush();
            return builder.ToString();
        }

        private void Append(double? value, int digits)
        {
            AppendToken(value.HasValue ? RoundDouble(value.Value, digits).ToString("0.###", CultureInfo.InvariantCulture) : "n");
        }

        private void AppendToken(string token)
        {
            AppendToken(token, 1);
        }

        private void AppendToken(string token, int repeat)
        {
            if (repeat <= 0) return;
            if (currentToken == token)
            {
                currentCount += repeat;
            }
            else
            {
                Flush();
                currentToken = token;
                currentCount = repeat;
            }
            Length += repeat;
        }

        private void Flush()
        {
            if (currentToken == null || currentCount <= 0) return;
            if (builder.Length > 0) builder.Append(';');
            if (currentCount == 1) builder.Append(currentToken);
            else
            {
                builder.Append(currentCount.ToString(CultureInfo.InvariantCulture));
                builder.Append('*');
                builder.Append(currentToken);
            }
            currentToken = null;
            currentCount = 0;
        }
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
