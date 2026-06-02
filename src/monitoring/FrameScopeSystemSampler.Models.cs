using System;
using System.Collections.Generic;
using System.Diagnostics;

internal static partial class FrameScopeSystemSampler
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

    internal sealed class CpuCoreTelemetryOptions
    {
        public bool Enabled;
        public string CsvPath;
        public string StatusPath;
        public int SampleIntervalMs;
    }

    internal sealed class CpuVoltageTelemetryOptions
    {
        public bool Enabled;
        public string CsvPath;
        public string StatusPath;
        public int SampleIntervalMs;
        public string Provider;
    }

    internal sealed class CpuVidTelemetryOptions
    {
        public bool Enabled;
        public string CsvPath;
        public string StatusPath;
        public int SampleIntervalMs;
        public string Provider;
    }

    internal sealed class CpuCoreProcessorIdentity
    {
        public string ProcessorGroup = "";
        public string LogicalProcessor = "";
        public string PhysicalCoreId = "";
        public string ThreadIndex = "";
    }

    internal sealed class CpuCoreCounterSample
    {
        public string InstanceName = "";
        public string ProcessorGroup = "";
        public string LogicalProcessor = "";
        public string PhysicalCoreId = "";
        public string ThreadIndex = "";
        public double? ActualFrequencyMHz;
        public double? ProcessorFrequencyMHz;
        public double? ProcessorPerformancePct;
        public double? PercentOfMaximumFrequency;
        public double? ProcessorUtilityPct;
        public double? PerformanceLimitFlags;
    }

    internal sealed class CpuVoltageSample
    {
        public string Source = "";
        public string ProviderKind = "";
        public string SensorName = "";
        public string ProcessorGroup = "";
        public string LogicalProcessor = "";
        public string CoreId = "";
        public string PhysicalCoreId = "";
        public string ThreadIndex = "";
        public double? VoltageV;
        public string Status = "";
        public string Reason = "";
        public string SensorIdentifier = "";
    }

    internal sealed class CpuVoltageSensorClassification
    {
        public bool Accepted;
        public string Status = "";
        public string Reason = "";
    }

    internal sealed class CpuVidSample
    {
        public string Source = "";
        public string ProviderKind = "";
        public string SensorName = "";
        public string ProcessorGroup = "";
        public string LogicalProcessor = "";
        public string CoreIndex = "";
        public string PhysicalCoreId = "";
        public string ThreadIndex = "";
        public double? VidV;
        public string Status = "";
        public string Reason = "";
        public string SensorIdentifier = "";
    }

    internal interface ICpuCoreTelemetryProvider
    {
        bool Available { get; }
        string UnavailableReason { get; }
        IReadOnlyList<CpuCoreCounterSample> ReadSamples();
    }

    internal interface ICpuVoltageTelemetryProvider
    {
        bool Available { get; }
        string UnavailableReason { get; }
        string SourceLabel { get; }
        string ProviderKind { get; }
        IReadOnlyList<CpuVoltageSample> ReadSamples();
    }

    internal interface ICpuVidTelemetryProvider
    {
        bool Available { get; }
        string UnavailableReason { get; }
        string SourceLabel { get; }
        string ProviderKind { get; }
        IReadOnlyList<CpuVidSample> ReadVidSamples();
    }
}
