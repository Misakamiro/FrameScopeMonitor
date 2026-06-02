using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

internal static partial class FrameScopeSystemSampler
{
    internal const string CpuCoreCsvHeaderLine = "Time,SampleIndex,ElapsedMs,Source,ProcessorGroup,LogicalProcessor,PhysicalCoreId,ThreadIndex,ActualFrequencyMHz,ProcessorFrequencyMHz,ProcessorPerformancePct,PercentOfMaximumFrequency,ProcessorUtilityPct,PerformanceLimitFlags";
    internal const string CpuVoltageCsvHeaderLine = "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreId,PhysicalCoreId,ThreadIndex,VoltageVolts,Status,Reason,SensorIdentifier";
    internal const string CpuVidCsvHeaderLine = "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreIndex,PhysicalCoreId,ThreadIndex,VidVolts,Status,Reason,SensorIdentifier";

    internal static CpuCoreTelemetrySession CreateCpuCoreTelemetrySession(CpuCoreTelemetryOptions options, ICpuCoreTelemetryProvider provider)
    {
        return new CpuCoreTelemetrySession(options, provider);
    }

    internal static CpuCoreTelemetrySession CreateCpuCoreTelemetrySessionForTests(CpuCoreTelemetryOptions options, ICpuCoreTelemetryProvider provider)
    {
        return CreateCpuCoreTelemetrySession(options, provider);
    }

    internal static CpuVoltageTelemetrySession CreateCpuVoltageTelemetrySession(CpuVoltageTelemetryOptions options, ICpuVoltageTelemetryProvider provider)
    {
        return new CpuVoltageTelemetrySession(options, provider);
    }

    internal static CpuVoltageTelemetrySession CreateCpuVoltageTelemetrySessionForTests(CpuVoltageTelemetryOptions options, ICpuVoltageTelemetryProvider provider)
    {
        return CreateCpuVoltageTelemetrySession(options, provider);
    }

    internal static CpuVidTelemetrySession CreateCpuVidTelemetrySession(CpuVidTelemetryOptions options, ICpuVidTelemetryProvider provider)
    {
        return new CpuVidTelemetrySession(options, provider);
    }

    internal static CpuVidTelemetrySession CreateCpuVidTelemetrySessionForTests(CpuVidTelemetryOptions options, ICpuVidTelemetryProvider provider)
    {
        return CreateCpuVidTelemetrySession(options, provider);
    }

    internal static CpuCoreProcessorIdentity ParseCpuCoreInstanceNameForTests(string instanceName)
    {
        return ParseCpuCoreInstanceName(instanceName);
    }

    private static CpuCoreProcessorIdentity ParseCpuCoreInstanceName(string instanceName)
    {
        CpuCoreProcessorIdentity identity = new CpuCoreProcessorIdentity();
        string text = (instanceName ?? "").Trim();
        if (text.Length == 0) return identity;

        string[] parts = text.Split(',');
        if (parts.Length == 2)
        {
            identity.ProcessorGroup = parts[0].Trim();
            identity.LogicalProcessor = parts[1].Trim();
            return identity;
        }

        identity.ProcessorGroup = "0";
        identity.LogicalProcessor = text;
        return identity;
    }

    internal sealed class CpuCoreTelemetrySession : IDisposable
    {
        private readonly CpuCoreTelemetryOptions options;
        private readonly ICpuCoreTelemetryProvider provider;
        private StreamWriter writer;
        private int rowCount;
        private int logicalProcessorCount;
        private long nextDueElapsedMs;
        private bool statusWritten;

        internal CpuCoreTelemetrySession(CpuCoreTelemetryOptions options, ICpuCoreTelemetryProvider provider)
        {
            this.options = options ?? new CpuCoreTelemetryOptions();
            this.provider = provider ?? new StaticCpuCoreTelemetryProvider(new CpuCoreCounterSample[0], "CPU core telemetry provider is missing.");
            if (this.options.SampleIntervalMs <= 0) this.options.SampleIntervalMs = 1000;
            if (this.options.SampleIntervalMs < 500) this.options.SampleIntervalMs = 500;
            if (String.IsNullOrWhiteSpace(this.options.CsvPath)) this.options.CsvPath = "cpu-core-samples.csv";
            if (String.IsNullOrWhiteSpace(this.options.StatusPath)) this.options.StatusPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(this.options.CsvPath)) ?? "", "cpu-core-telemetry-status.json");

            if (!this.options.Enabled) return;

            if (!this.provider.Available)
            {
                string unavailableReason = String.IsNullOrWhiteSpace(this.provider.UnavailableReason)
                    ? "Processor Information(*)\\Actual Frequency counter is unavailable."
                    : this.provider.UnavailableReason;
                WriteStatus(false, unavailableReason);
            }
        }

        public bool Enabled
        {
            get { return options.Enabled && provider.Available; }
        }

        public bool TryWriteSample(int sampleIndex, long elapsedMs)
        {
            if (!Enabled) return false;
            if (elapsedMs < nextDueElapsedMs) return false;
            nextDueElapsedMs = elapsedMs + options.SampleIntervalMs;

            IReadOnlyList<CpuCoreCounterSample> samples;
            try
            {
                samples = provider.ReadSamples();
            }
            catch (Exception ex)
            {
                WriteStatus(false, "Processor Information(*)\\Actual Frequency read failed: " + ex.Message);
                return false;
            }

            if (samples == null || samples.Count == 0)
            {
                WriteStatus(false, "Processor Information(*)\\Actual Frequency returned no logical processor samples.");
                return false;
            }

            EnsureWriter();
            string nowText = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            foreach (CpuCoreCounterSample sample in samples)
            {
                if (sample == null) continue;
                string processorGroup = sample.ProcessorGroup;
                string logicalProcessor = sample.LogicalProcessor;
                string physicalCoreId = sample.PhysicalCoreId;
                string threadIndex = sample.ThreadIndex;
                if (String.IsNullOrWhiteSpace(processorGroup) && String.IsNullOrWhiteSpace(logicalProcessor))
                {
                    CpuCoreProcessorIdentity identity = ParseCpuCoreInstanceName(sample.InstanceName);
                    processorGroup = identity.ProcessorGroup;
                    logicalProcessor = identity.LogicalProcessor;
                    physicalCoreId = identity.PhysicalCoreId;
                    threadIndex = identity.ThreadIndex;
                }
                WriteCsv(writer, new object[]
                {
                    nowText,
                    sampleIndex,
                    elapsedMs,
                    "windows-perfcounter",
                    processorGroup,
                    logicalProcessor,
                    physicalCoreId,
                    threadIndex,
                    Round(sample.ActualFrequencyMHz, 0),
                    Round(sample.ProcessorFrequencyMHz, 0),
                    Round(sample.ProcessorPerformancePct, 2),
                    Round(sample.PercentOfMaximumFrequency, 2),
                    Round(sample.ProcessorUtilityPct, 2),
                    Round(sample.PerformanceLimitFlags, 0)
                });
                rowCount++;
            }
            logicalProcessorCount = Math.Max(logicalProcessorCount, samples.Count);
            if (sampleIndex % 5 == 0) writer.Flush();
            WriteStatusIfDue(sampleIndex, true, "");
            return true;
        }

        public void Dispose()
        {
            if (writer != null)
            {
                writer.Flush();
                writer.Dispose();
                writer = null;
            }
            if (options.Enabled && provider.Available)
            {
                WriteStatus(rowCount > 0, rowCount > 0 ? "" : "Processor Information(*)\\Actual Frequency returned no logical processor samples.");
            }
            IDisposable disposable = provider as IDisposable;
            if (disposable != null) disposable.Dispose();
        }

        private void EnsureWriter()
        {
            if (writer != null) return;
            EnsureParent(options.CsvPath);
            writer = Writer(options.CsvPath);
            writer.WriteLine(CpuCoreCsvHeaderLine);
        }

        private void WriteStatus(bool available, string reason)
        {
            if (String.IsNullOrWhiteSpace(options.StatusPath)) return;
            try
            {
                EnsureParent(options.StatusPath);
                Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Time", DateTime.Now.ToString("o", CultureInfo.InvariantCulture) },
                    { "Enabled", options.Enabled },
                    { "CpuCoreSamplesCsv", options.CsvPath },
                    { "CpuCoreTelemetrySource", "windows-perfcounter" },
                    { "CpuCoreTelemetryAvailable", available },
                    { "CpuCoreTelemetryUnavailableReason", available ? "" : reason },
                    { "CpuCoreSampleIntervalMs", options.SampleIntervalMs },
                    { "CpuCoreSampleCount", rowCount },
                    { "CpuCoreLogicalProcessorCount", logicalProcessorCount },
                    { "CpuVoltageAvailable", false },
                    { "CpuVoltageVcoreAvailable", false },
                    { "CpuVoltagePerCoreAvailable", false },
                    { "CpuVoltageNonPerCoreAvailable", false },
                    { "CpuVoltageStatus", "unavailable" },
                    { "CpuVoltageUnavailableReason", "CPU Voltage / Vcore telemetry was not recorded in this status file." },
                    { "CpuVoltageSampleCount", 0 },
                    { "CpuVoltageVcoreSampleCount", 0 },
                    { "CpuVoltagePerCoreSampleCount", 0 },
                    { "CpuVoltageNonPerCoreSampleCount", 0 },
                    { "CpuVoltageRejectedSampleCount", 0 }
                };
                File.WriteAllText(options.StatusPath, SerializeSimpleJson(map), new UTF8Encoding(false));
                statusWritten = true;
            }
            catch
            {
            }
        }

        private void WriteStatusIfDue(int sampleIndex, bool available, string reason)
        {
            if (!statusWritten || sampleIndex % 5 == 0)
            {
                WriteStatus(available, reason);
            }
        }
    }

    internal sealed class CpuVoltageTelemetrySession : IDisposable
    {
        private readonly CpuVoltageTelemetryOptions options;
        private readonly ICpuVoltageTelemetryProvider provider;
        private StreamWriter writer;
        private int rowCount;
        private int rejectedVoltageRowCount;
        private long nextDueElapsedMs;
        private bool statusWritten;

        internal CpuVoltageTelemetrySession(CpuVoltageTelemetryOptions options, ICpuVoltageTelemetryProvider provider)
        {
            this.options = options ?? new CpuVoltageTelemetryOptions();
            this.provider = provider ?? new StaticCpuVoltageTelemetryProvider(new CpuVoltageSample[0], "CPU voltage telemetry provider is missing.", "missing", "unavailable");
            if (this.options.SampleIntervalMs <= 0) this.options.SampleIntervalMs = 1000;
            if (this.options.SampleIntervalMs < 500) this.options.SampleIntervalMs = 500;
            if (String.IsNullOrWhiteSpace(this.options.CsvPath)) this.options.CsvPath = "cpu-voltage-samples.csv";
            if (String.IsNullOrWhiteSpace(this.options.StatusPath)) this.options.StatusPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(this.options.CsvPath)) ?? "", "cpu-voltage-telemetry-status.json");

            if (!this.options.Enabled) return;

            if (!this.provider.Available)
            {
                string unavailableReason = String.IsNullOrWhiteSpace(this.provider.UnavailableReason)
                    ? NoCpuVcoreReason()
                    : this.provider.UnavailableReason;
                WriteStatus(false, unavailableReason);
            }
        }

        public bool Enabled
        {
            get { return options.Enabled && provider.Available; }
        }

        public bool TryWriteSample(int sampleIndex, long elapsedMs)
        {
            if (!Enabled) return false;
            if (elapsedMs < nextDueElapsedMs) return false;
            nextDueElapsedMs = elapsedMs + options.SampleIntervalMs;

            IReadOnlyList<CpuVoltageSample> samples;
            try
            {
                samples = provider.ReadSamples();
            }
            catch (Exception ex)
            {
                WriteStatus(false, "CPU voltage sensor read failed: " + ex.Message);
                return false;
            }

            if (samples == null || samples.Count == 0)
            {
                WriteStatusIfDue(sampleIndex, false, NoCpuVcoreReason());
                return false;
            }

            int rowsBefore = rowCount;
            int rejectedBefore = rejectedVoltageRowCount;
            string nowText = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            foreach (CpuVoltageSample sample in samples)
            {
                if (sample == null || !sample.VoltageV.HasValue) continue;
                double voltage = sample.VoltageV.Value;
                if (voltage <= 0 || voltage >= 5) continue;
                CpuVoltageSensorClassification classification = ClassifyCpuVoltageSample(sample);
                if (!classification.Accepted)
                {
                    rejectedVoltageRowCount++;
                    continue;
                }
                string providerKind = String.IsNullOrWhiteSpace(sample.ProviderKind) ? provider.ProviderKind : sample.ProviderKind;
                string source = String.IsNullOrWhiteSpace(sample.Source) ? provider.SourceLabel : sample.Source;
                EnsureWriter();
                WriteCsv(writer, new object[]
                {
                    nowText,
                    sampleIndex,
                    elapsedMs,
                    source,
                    providerKind,
                    sample.SensorName,
                    "",
                    "",
                    "",
                    "",
                    "",
                    Round(voltage, 3),
                    "vcore",
                    "",
                    sample.SensorIdentifier
                });
                rowCount++;
            }

            if (rowCount == rowsBefore)
            {
                WriteStatusIfDue(sampleIndex, false, rejectedVoltageRowCount > rejectedBefore ? NonVcoreOnlyReason() : NoCpuVcoreReason());
                return false;
            }

            if (sampleIndex % 5 == 0) writer.Flush();
            WriteStatusIfDue(sampleIndex, rowCount > 0, "");
            return true;
        }

        public void Dispose()
        {
            if (writer != null)
            {
                writer.Flush();
                writer.Dispose();
                writer = null;
            }
            if (options.Enabled && provider.Available)
            {
                WriteStatus(rowCount > 0, rowCount > 0 ? "" : (rejectedVoltageRowCount > 0 ? NonVcoreOnlyReason() : NoCpuVcoreReason()));
            }
            IDisposable disposable = provider as IDisposable;
            if (disposable != null) disposable.Dispose();
        }

        private void EnsureWriter()
        {
            if (writer != null) return;
            EnsureParent(options.CsvPath);
            writer = Writer(options.CsvPath);
            writer.WriteLine(CpuVoltageCsvHeaderLine);
        }

        private void WriteStatus(bool available, string reason)
        {
            if (String.IsNullOrWhiteSpace(options.StatusPath)) return;
            try
            {
                EnsureParent(options.StatusPath);
                Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Time", DateTime.Now.ToString("o", CultureInfo.InvariantCulture) },
                    { "CpuVoltageTelemetryEnabled", options.Enabled },
                    { "CpuVoltageSamplesCsv", options.CsvPath },
                    { "CpuVoltageProviderRequested", String.IsNullOrWhiteSpace(options.Provider) ? "auto" : options.Provider },
                    { "CpuVoltageProviderKind", provider.ProviderKind },
                    { "CpuVoltageTelemetrySource", provider.SourceLabel },
                    { "CpuVoltageSource", provider.SourceLabel },
                    { "CpuVoltageAvailable", available },
                    { "CpuVoltageVcoreAvailable", rowCount > 0 },
                    { "CpuVoltagePerCoreAvailable", false },
                    { "CpuVoltageNonPerCoreAvailable", rejectedVoltageRowCount > 0 },
                    { "CpuVoltageStatus", CpuVoltageStatus(available, reason, provider.Available, rejectedVoltageRowCount) },
                    { "CpuVoltageUnavailableReason", available ? "" : reason },
                    { "CpuVoltageSampleIntervalMs", options.SampleIntervalMs },
                    { "CpuVoltageSampleCount", rowCount },
                    { "CpuVoltageVcoreSampleCount", rowCount },
                    { "CpuVoltagePerCoreSampleCount", 0 },
                    { "CpuVoltageNonPerCoreSampleCount", rejectedVoltageRowCount },
                    { "CpuVoltageRejectedSampleCount", rejectedVoltageRowCount },
                    { "CpuVoltageLogicalProcessorCount", 0 }
                };
                File.WriteAllText(options.StatusPath, SerializeSimpleJson(map), new UTF8Encoding(false));
                statusWritten = true;
            }
            catch
            {
            }
        }

        private void WriteStatusIfDue(int sampleIndex, bool available, string reason)
        {
            if (!statusWritten || sampleIndex % 5 == 0)
            {
                WriteStatus(available, reason);
            }
        }
    }

    internal sealed class CpuVidTelemetrySession : IDisposable
    {
        private readonly CpuVidTelemetryOptions options;
        private readonly ICpuVidTelemetryProvider provider;
        private StreamWriter writer;
        private int rowCount;
        private int coreCount;
        private long nextDueElapsedMs;
        private bool statusWritten;

        internal CpuVidTelemetrySession(CpuVidTelemetryOptions options, ICpuVidTelemetryProvider provider)
        {
            this.options = options ?? new CpuVidTelemetryOptions();
            this.provider = provider ?? new StaticCpuVidTelemetryProvider(new CpuVidSample[0], "CPU Core VID telemetry provider is missing.", "missing", "unavailable");
            if (this.options.SampleIntervalMs <= 0) this.options.SampleIntervalMs = 1000;
            if (this.options.SampleIntervalMs < 500) this.options.SampleIntervalMs = 500;
            if (String.IsNullOrWhiteSpace(this.options.CsvPath)) this.options.CsvPath = "cpu-vid-samples.csv";
            if (String.IsNullOrWhiteSpace(this.options.StatusPath)) this.options.StatusPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(this.options.CsvPath)) ?? "", "cpu-vid-telemetry-status.json");

            if (!this.options.Enabled) return;

            if (!this.provider.Available)
            {
                string unavailableReason = String.IsNullOrWhiteSpace(this.provider.UnavailableReason)
                    ? NoCpuVidReason()
                    : this.provider.UnavailableReason;
                WriteStatus(false, unavailableReason);
            }
        }

        public bool Enabled
        {
            get { return options.Enabled && provider.Available; }
        }

        public bool TryWriteSample(int sampleIndex, long elapsedMs)
        {
            if (!Enabled) return false;
            if (elapsedMs < nextDueElapsedMs) return false;
            nextDueElapsedMs = elapsedMs + options.SampleIntervalMs;

            IReadOnlyList<CpuVidSample> samples;
            try
            {
                samples = provider.ReadVidSamples();
            }
            catch (Exception ex)
            {
                WriteStatus(false, "CPU Core VID sensor read failed: " + ex.Message);
                return false;
            }

            if (samples == null || samples.Count == 0)
            {
                WriteStatusIfDue(sampleIndex, false, NoCpuVidReason());
                return false;
            }

            HashSet<string> cores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int rowsBefore = rowCount;
            string nowText = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            foreach (CpuVidSample sample in samples)
            {
                if (sample == null || !sample.VidV.HasValue) continue;
                double vid = sample.VidV.Value;
                if (vid <= 0 || vid >= 5) continue;
                string status = NormalizeVidSampleStatus(sample);
                if (!String.Equals(status, "core-vid", StringComparison.OrdinalIgnoreCase)) continue;
                string providerKind = String.IsNullOrWhiteSpace(sample.ProviderKind) ? provider.ProviderKind : sample.ProviderKind;
                string source = String.IsNullOrWhiteSpace(sample.Source) ? provider.SourceLabel : sample.Source;
                string coreKey = CpuCoreKeyForStatus(sample.ProcessorGroup, sample.LogicalProcessor, sample.CoreIndex);
                if (!String.IsNullOrWhiteSpace(coreKey)) cores.Add(coreKey);

                EnsureWriter();
                WriteCsv(writer, new object[]
                {
                    nowText,
                    sampleIndex,
                    elapsedMs,
                    source,
                    providerKind,
                    sample.SensorName,
                    sample.ProcessorGroup,
                    sample.LogicalProcessor,
                    sample.CoreIndex,
                    sample.PhysicalCoreId,
                    sample.ThreadIndex,
                    Round(vid, 3),
                    "core-vid",
                    String.IsNullOrWhiteSpace(sample.Reason) ? CpuVidNote() : sample.Reason,
                    sample.SensorIdentifier
                });
                rowCount++;
            }

            if (rowCount == rowsBefore)
            {
                WriteStatusIfDue(sampleIndex, false, NoCpuVidReason());
                return false;
            }

            coreCount = Math.Max(coreCount, cores.Count);
            if (sampleIndex % 5 == 0) writer.Flush();
            WriteStatusIfDue(sampleIndex, true, "");
            return true;
        }

        public void Dispose()
        {
            if (writer != null)
            {
                writer.Flush();
                writer.Dispose();
                writer = null;
            }
            if (options.Enabled && provider.Available)
            {
                WriteStatus(rowCount > 0, rowCount > 0 ? "" : NoCpuVidReason());
            }
            IDisposable disposable = provider as IDisposable;
            if (disposable != null) disposable.Dispose();
        }

        private void EnsureWriter()
        {
            if (writer != null) return;
            EnsureParent(options.CsvPath);
            writer = Writer(options.CsvPath);
            writer.WriteLine(CpuVidCsvHeaderLine);
        }

        private void WriteStatus(bool available, string reason)
        {
            if (String.IsNullOrWhiteSpace(options.StatusPath)) return;
            try
            {
                EnsureParent(options.StatusPath);
                Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Time", DateTime.Now.ToString("o", CultureInfo.InvariantCulture) },
                    { "CpuVidTelemetryEnabled", options.Enabled },
                    { "CpuVidSamplesCsv", options.CsvPath },
                    { "CpuVidProviderRequested", String.IsNullOrWhiteSpace(options.Provider) ? "auto" : options.Provider },
                    { "CpuVidProviderKind", provider.ProviderKind },
                    { "CpuVidTelemetrySource", provider.SourceLabel },
                    { "CpuVidSource", provider.SourceLabel },
                    { "CpuVidAvailable", available },
                    { "CpuVidStatus", CpuVidStatus(available, reason, provider.Available) },
                    { "CpuVidUnavailableReason", available ? "" : reason },
                    { "CpuVidNote", CpuVidNote() },
                    { "CpuVidSampleIntervalMs", options.SampleIntervalMs },
                    { "CpuVidSampleCount", rowCount },
                    { "CpuVidCoreCount", coreCount }
                };
                File.WriteAllText(options.StatusPath, SerializeSimpleJson(map), new UTF8Encoding(false));
                statusWritten = true;
            }
            catch
            {
            }
        }

        private void WriteStatusIfDue(int sampleIndex, bool available, string reason)
        {
            if (!statusWritten || sampleIndex % 5 == 0)
            {
                WriteStatus(available, reason);
            }
        }
    }

    internal sealed class StaticCpuCoreTelemetryProvider : ICpuCoreTelemetryProvider
    {
        private readonly IReadOnlyList<CpuCoreCounterSample> samples;

        public StaticCpuCoreTelemetryProvider(IReadOnlyList<CpuCoreCounterSample> samples, string unavailableReason)
        {
            this.samples = samples ?? new CpuCoreCounterSample[0];
            UnavailableReason = unavailableReason ?? "";
            Available = String.IsNullOrWhiteSpace(UnavailableReason);
        }

        public bool Available { get; private set; }
        public string UnavailableReason { get; private set; }

        public IReadOnlyList<CpuCoreCounterSample> ReadSamples()
        {
            return samples;
        }
    }

    internal sealed class StaticCpuVoltageTelemetryProvider : ICpuVoltageTelemetryProvider
    {
        private readonly IReadOnlyList<CpuVoltageSample> samples;

        public StaticCpuVoltageTelemetryProvider(IReadOnlyList<CpuVoltageSample> samples, string unavailableReason, string sourceLabel)
            : this(samples, unavailableReason, sourceLabel, String.Equals(sourceLabel, "disabled", StringComparison.OrdinalIgnoreCase) ? "disabled" : "synthetic")
        {
        }

        public StaticCpuVoltageTelemetryProvider(IReadOnlyList<CpuVoltageSample> samples, string unavailableReason, string sourceLabel, string providerKind)
        {
            this.samples = samples ?? new CpuVoltageSample[0];
            UnavailableReason = unavailableReason ?? "";
            SourceLabel = String.IsNullOrWhiteSpace(sourceLabel) ? "static-voltage" : sourceLabel;
            ProviderKind = String.IsNullOrWhiteSpace(providerKind) ? "synthetic" : providerKind;
            Available = String.IsNullOrWhiteSpace(UnavailableReason);
        }

        public bool Available { get; private set; }
        public string UnavailableReason { get; private set; }
        public string SourceLabel { get; private set; }
        public string ProviderKind { get; private set; }

        public IReadOnlyList<CpuVoltageSample> ReadSamples()
        {
            return samples;
        }
    }

    internal sealed class StaticCpuVidTelemetryProvider : ICpuVidTelemetryProvider
    {
        private readonly IReadOnlyList<CpuVidSample> samples;

        public StaticCpuVidTelemetryProvider(IReadOnlyList<CpuVidSample> samples, string unavailableReason, string sourceLabel)
            : this(samples, unavailableReason, sourceLabel, String.Equals(sourceLabel, "disabled", StringComparison.OrdinalIgnoreCase) ? "disabled" : "synthetic")
        {
        }

        public StaticCpuVidTelemetryProvider(IReadOnlyList<CpuVidSample> samples, string unavailableReason, string sourceLabel, string providerKind)
        {
            this.samples = samples ?? new CpuVidSample[0];
            UnavailableReason = unavailableReason ?? "";
            SourceLabel = String.IsNullOrWhiteSpace(sourceLabel) ? "static-vid" : sourceLabel;
            ProviderKind = String.IsNullOrWhiteSpace(providerKind) ? "synthetic" : providerKind;
            Available = String.IsNullOrWhiteSpace(UnavailableReason);
        }

        public bool Available { get; private set; }
        public string UnavailableReason { get; private set; }
        public string SourceLabel { get; private set; }
        public string ProviderKind { get; private set; }

        public IReadOnlyList<CpuVidSample> ReadVidSamples()
        {
            return samples;
        }
    }

    private static string CpuCoreKeyForStatus(string group, string logical, string coreId)
    {
        group = (group ?? "").Trim();
        logical = (logical ?? "").Trim();
        coreId = (coreId ?? "").Trim();
        if (String.IsNullOrWhiteSpace(group) && String.IsNullOrWhiteSpace(logical) && String.IsNullOrWhiteSpace(coreId)) return "";
        if (String.IsNullOrWhiteSpace(logical) && !String.IsNullOrWhiteSpace(coreId)) return "core:" + coreId;
        if (String.IsNullOrWhiteSpace(group) || group == "0") return logical;
        return group + ":" + logical;
    }

    private static string NormalizeVoltageSampleStatus(CpuVoltageSample sample)
    {
        return ClassifyCpuVoltageSample(sample).Status;
    }

    private static CpuVoltageSensorClassification ClassifyCpuVoltageSample(CpuVoltageSample sample)
    {
        if (sample == null) return RejectedCpuVoltageSample(NoCpuVcoreReason());
        return ClassifyCpuVoltageSensorText(sample.SensorName, sample.SensorIdentifier, sample.Status);
    }

    private static CpuVoltageSensorClassification ClassifyCpuVoltageSensorText(string sensorName, string sensorIdentifier, string status)
    {
        string normalizedStatus = (status ?? "").Trim().ToLowerInvariant();
        string text = ((sensorName ?? "") + " " + (sensorIdentifier ?? "")).ToLowerInvariant();
        if (normalizedStatus == "core-vid" || normalizedStatus == "cpu-core-vid" || normalizedStatus == "vid" || LooksLikeVidSensor(sensorName, sensorIdentifier))
        {
            return RejectedCpuVoltageSample("VID is CPU request/target voltage, not CPU Voltage / Vcore.");
        }
        if (ContainsRejectedCpuVoltageToken(text))
        {
            return RejectedCpuVoltageSample(RejectedCpuVoltageReason());
        }
        if (normalizedStatus == "vcore" || normalizedStatus == "cpu-vcore" || normalizedStatus == "cpu-voltage" || IsExplicitCpuVcoreSensor(sensorName, sensorIdentifier))
        {
            return AcceptedCpuVoltageSample();
        }
        if (normalizedStatus == "per-core" || normalizedStatus == "per_core" || normalizedStatus == "percore")
        {
            return RejectedCpuVoltageSample("Per-core voltage is not GamePP CPU Voltage / Vcore.");
        }
        return RejectedCpuVoltageSample(RejectedCpuVoltageReason());
    }

    private static CpuVoltageSensorClassification AcceptedCpuVoltageSample()
    {
        return new CpuVoltageSensorClassification
        {
            Accepted = true,
            Status = "vcore",
            Reason = ""
        };
    }

    private static CpuVoltageSensorClassification RejectedCpuVoltageSample(string reason)
    {
        return new CpuVoltageSensorClassification
        {
            Accepted = false,
            Status = "non-per-core",
            Reason = String.IsNullOrWhiteSpace(reason) ? RejectedCpuVoltageReason() : reason
        };
    }

    private static bool IsExplicitCpuVcoreSensor(string sensorName, string sensorIdentifier)
    {
        string sensor = NormalizeCpuVoltageSensorText(sensorName);
        string text = NormalizeCpuVoltageSensorText((sensorName ?? "") + " " + (sensorIdentifier ?? ""));
        if (String.IsNullOrWhiteSpace(sensor) && String.IsNullOrWhiteSpace(text)) return false;
        if (ContainsRejectedCpuVoltageToken(text)) return false;
        if (ExtractCpuVoltageCoreIndex(text).HasValue) return false;
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

    private static bool ContainsRejectedCpuVoltageToken(string normalizedText)
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
        if (ExtractCpuVoltageCoreIndex(text).HasValue) return true;
        return false;
    }

    private static int? ExtractCpuVoltageCoreIndex(string text)
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

    private static string NormalizeVidSampleStatus(CpuVidSample sample)
    {
        string status = (sample.Status ?? "").Trim().ToLowerInvariant();
        if (status == "core-vid" || status == "cpu-core-vid" || status == "vid") return "core-vid";
        bool hasCore = !String.IsNullOrWhiteSpace(sample.LogicalProcessor) || !String.IsNullOrWhiteSpace(sample.CoreIndex) || !String.IsNullOrWhiteSpace(sample.PhysicalCoreId);
        bool looksLikeVid = LooksLikeVidSensor(sample.SensorName, sample.SensorIdentifier);
        return hasCore && looksLikeVid ? "core-vid" : "";
    }

    internal static int? ExtractCpuVidCoreIndexForTests(string text)
    {
        return ExtractCpuVidCoreIndex(text);
    }

    internal static bool IsCpuCoreVidSensorForTests(string text, int? core)
    {
        return IsCpuCoreVidSensorText(text, core);
    }

    private static int? ExtractCpuVidCoreIndex(string text)
    {
        if (String.IsNullOrWhiteSpace(text)) return null;
        string lower = text.ToLowerInvariant();
        int searchStart = 0;
        while (searchStart < lower.Length)
        {
            int coreIndex = lower.IndexOf("core", searchStart, StringComparison.Ordinal);
            if (coreIndex < 0) return null;
            if (coreIndex > 0 && Char.IsLetterOrDigit(lower[coreIndex - 1]))
            {
                searchStart = coreIndex + 4;
                continue;
            }

            bool sawHash = false;
            int digitStart = -1;
            for (int i = coreIndex + 4; i < lower.Length; i++)
            {
                char ch = lower[i];
                if (Char.IsDigit(ch))
                {
                    digitStart = i;
                    break;
                }
                if (ch == '#')
                {
                    sawHash = true;
                    continue;
                }
                if (Char.IsWhiteSpace(ch) || ch == '-' || ch == '_' || ch == ':' || ch == '[' || ch == ']' || ch == '(' || ch == ')' || ch == '/' || ch == '.')
                {
                    continue;
                }
                if (Char.IsLetter(ch)) break;
            }

            if (digitStart >= 0)
            {
                int end = digitStart;
                while (end < lower.Length && Char.IsDigit(lower[end])) end++;
                int parsed;
                if (Int32.TryParse(lower.Substring(digitStart, end - digitStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    if (sawHash && parsed > 0) return parsed - 1;
                    return parsed;
                }
            }

            searchStart = coreIndex + 4;
        }
        return null;
    }

    private static bool IsCpuCoreVidSensorText(string text, int? core)
    {
        if (!core.HasValue) return false;
        string normalized = NormalizeCpuVoltageSensorText(text);
        if (String.IsNullOrWhiteSpace(normalized)) return false;
        if (normalized.IndexOf("vid", StringComparison.Ordinal) < 0) return false;
        if (normalized.IndexOf("core", StringComparison.Ordinal) < 0) return false;
        if (normalized.IndexOf("cpu", StringComparison.Ordinal) < 0) return false;
        if (normalized.IndexOf("soc", StringComparison.Ordinal) >= 0) return false;
        if (normalized.IndexOf("package", StringComparison.Ordinal) >= 0) return false;
        if (normalized.IndexOf("aggregate", StringComparison.Ordinal) >= 0) return false;
        if (normalized.IndexOf("gpu", StringComparison.Ordinal) >= 0) return false;
        if (normalized.IndexOf("graphics", StringComparison.Ordinal) >= 0) return false;
        if (normalized.IndexOf("dram", StringComparison.Ordinal) >= 0) return false;
        if (normalized.IndexOf("ddr", StringComparison.Ordinal) >= 0) return false;
        if (normalized.IndexOf("memory", StringComparison.Ordinal) >= 0) return false;
        if (normalized.IndexOf("chipset", StringComparison.Ordinal) >= 0) return false;
        if (normalized.IndexOf("battery", StringComparison.Ordinal) >= 0) return false;
        if (normalized.IndexOf("vbat", StringComparison.Ordinal) >= 0) return false;
        if (normalized.IndexOf("vin", StringComparison.Ordinal) >= 0) return false;
        return true;
    }

    private static bool LooksLikeVidSensor(string name, string identifier)
    {
        string text = ((name ?? "") + " " + (identifier ?? "")).ToLowerInvariant();
        return text.IndexOf("vid", StringComparison.Ordinal) >= 0;
    }

    private static string CpuVidNote()
    {
        return "VID \u662f CPU \u8bf7\u6c42/\u76ee\u6807\u7535\u538b\uff0c\u4e0d\u662f\u771f\u5b9e per-core Vcore\u3002";
    }

    private static string NoCpuVidReason()
    {
        return "\u672a\u68c0\u6d4b\u5230 CPU \u6838\u5fc3 VID \u4f20\u611f\u5668\uff1b\u4e0d\u751f\u6210\u5047\u6570\u636e\u3002";
    }

    private static string NoCpuVcoreReason()
    {
        return "No explicit CPU Vcore/CPU Voltage sensor was recorded; VID/SOC/Package/VBAT/VIN are not accepted as CPU Voltage.";
    }

    private static string NonVcoreOnlyReason()
    {
        return "Only non-Vcore voltage sensors were detected; CPU Voltage requires explicit CPU Vcore/CPU Voltage. VID/SOC/Package/VBAT/VIN are not accepted.";
    }

    private static string RejectedCpuVoltageReason()
    {
        return "CPU Voltage accepts only explicit CPU Vcore/CPU Voltage sensors; VID/SOC/Package/VBAT/VIN and other rails are rejected.";
    }

    private static string NonPerCoreReason(CpuVoltageSample sample)
    {
        if (sample != null && !String.IsNullOrWhiteSpace(sample.Reason)) return sample.Reason;
        return RejectedCpuVoltageReason();
    }

    private static string NonPerCoreOnlyReason()
    {
        return NonVcoreOnlyReason();
    }

    private static string CpuVoltageStatus(bool available, string reason, bool providerAvailable, int nonPerCoreRows)
    {
        if (available) return "vcore-available";
        if (nonPerCoreRows > 0) return "non-per-core-only";
        if (!providerAvailable)
        {
            if (reason != null && reason.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0) return "permission-failed";
            if (reason != null && (reason.IndexOf("load", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("dll", StringComparison.OrdinalIgnoreCase) >= 0)) return "load-failed";
            return "provider-unavailable";
        }
        return "unavailable";
    }

    private static string CpuVidStatus(bool available, string reason, bool providerAvailable)
    {
        if (available) return "core-vid-available";
        if (!providerAvailable)
        {
            if (reason != null && reason.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0) return "permission-failed";
            if (reason != null && (reason.IndexOf("load", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("dll", StringComparison.OrdinalIgnoreCase) >= 0)) return "load-failed";
            return "provider-unavailable";
        }
        return "unavailable";
    }

    private static string SerializeSimpleJson(Dictionary<string, object> values)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append('{');
        bool first = true;
        foreach (KeyValuePair<string, object> pair in values)
        {
            if (!first) builder.Append(',');
            first = false;
            builder.Append('"');
            builder.Append(JsonEscape(pair.Key));
            builder.Append("\":");
            AppendJsonValue(builder, pair.Value);
        }
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendJsonValue(StringBuilder builder, object value)
    {
        if (value == null)
        {
            builder.Append("null");
            return;
        }

        if (value is bool)
        {
            builder.Append((bool)value ? "true" : "false");
            return;
        }

        IFormattable formattable = value as IFormattable;
        if (formattable != null && !(value is DateTime))
        {
            builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
            return;
        }

        builder.Append('"');
        builder.Append(JsonEscape(Convert.ToString(value, CultureInfo.InvariantCulture)));
        builder.Append('"');
    }

    private static string JsonEscape(string value)
    {
        if (String.IsNullOrEmpty(value)) return "";
        StringBuilder builder = new StringBuilder(value.Length + 8);
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (ch < 32)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }
        return builder.ToString();
    }
}
