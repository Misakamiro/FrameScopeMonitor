using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;

internal static partial class FrameScopeSystemSampler
{
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

    private static CpuCoreCounterSet CreateCpuCoreCounterSet()
    {
        return CpuCoreCounterSet.Create();
    }

    private static void CreateCpuHardwareTelemetryProviders(
        bool cpuVoltageEnabled,
        string cpuVoltageProvider,
        bool cpuVidEnabled,
        string cpuVidProvider,
        out ICpuVoltageTelemetryProvider voltageProvider,
        out ICpuVidTelemetryProvider vidProvider)
    {
        voltageProvider = cpuVoltageEnabled
            ? null
            : new StaticCpuVoltageTelemetryProvider(new CpuVoltageSample[0], "", "disabled");
        vidProvider = cpuVidEnabled
            ? null
            : new StaticCpuVidTelemetryProvider(new CpuVidSample[0], "", "disabled");

        if (cpuVoltageEnabled && cpuVidEnabled && ShouldShareBuiltInCpuHardwareProvider(cpuVoltageProvider, cpuVidProvider))
        {
            ICpuVoltageTelemetryProvider builtInVoltage = BuiltInLibreHardwareMonitorCpuVoltageTelemetryProvider.Create(AppDomain.CurrentDomain.BaseDirectory);
            ICpuVidTelemetryProvider builtInVid = builtInVoltage as ICpuVidTelemetryProvider;
            if (builtInVid != null)
            {
                if (builtInVoltage.Available)
                {
                    voltageProvider = builtInVoltage;
                    vidProvider = builtInVid;
                    return;
                }

                if (IsAutoCpuHardwareProviderRequest(cpuVoltageProvider))
                {
                    ICpuVoltageTelemetryProvider wmi = WmiCpuVoltageTelemetryProvider.Create("wmi");
                    if (wmi.Available)
                    {
                        voltageProvider = wmi;
                    }
                    else
                    {
                        string reason = builtInVoltage.UnavailableReason;
                        if (!String.IsNullOrWhiteSpace(wmi.UnavailableReason))
                        {
                            reason = String.IsNullOrWhiteSpace(reason) ? wmi.UnavailableReason : reason + " | WMI fallback: " + wmi.UnavailableReason;
                        }
                        voltageProvider = new StaticCpuVoltageTelemetryProvider(new CpuVoltageSample[0], reason, "builtin-librehardwaremonitor", "unavailable");
                    }
                }
                else
                {
                    voltageProvider = builtInVoltage;
                }

                vidProvider = builtInVid;
                return;
            }

            IDisposable disposable = builtInVoltage as IDisposable;
            if (disposable != null) disposable.Dispose();
        }

        if (voltageProvider == null) voltageProvider = CreateCpuVoltageProvider(cpuVoltageProvider);
        if (vidProvider == null) vidProvider = CreateCpuVidProvider(cpuVidProvider);
    }

    internal static bool ShouldShareBuiltInCpuHardwareProviderForTests(string cpuVoltageProvider, string cpuVidProvider)
    {
        return ShouldShareBuiltInCpuHardwareProvider(cpuVoltageProvider, cpuVidProvider);
    }

    private static bool ShouldShareBuiltInCpuHardwareProvider(string cpuVoltageProvider, string cpuVidProvider)
    {
        return IsBuiltInCpuHardwareProviderRequest(cpuVoltageProvider) &&
               IsBuiltInCpuHardwareProviderRequest(cpuVidProvider);
    }

    private static bool IsBuiltInCpuHardwareProviderRequest(string provider)
    {
        string normalized = NormalizeCpuHardwareProvider(provider);
        return normalized == "auto" ||
               normalized == "built-in" ||
               normalized == "builtin" ||
               normalized == "sensor";
    }

    private static bool IsAutoCpuHardwareProviderRequest(string provider)
    {
        return NormalizeCpuHardwareProvider(provider) == "auto";
    }

    private static string NormalizeCpuHardwareProvider(string provider)
    {
        return (provider ?? "auto").Trim().ToLowerInvariant();
    }

    private static ICpuVoltageTelemetryProvider CreateCpuVoltageProvider(string provider)
    {
        string normalized = NormalizeCpuHardwareProvider(provider);
        if (normalized == "disabled")
        {
            return new StaticCpuVoltageTelemetryProvider(new CpuVoltageSample[0], "CPU voltage telemetry is disabled.", "disabled", "disabled");
        }
        if (normalized == "synthetic")
        {
            return new StaticCpuVoltageTelemetryProvider(new CpuVoltageSample[0], "Synthetic CPU voltage provider is only available in tests.", "synthetic-sensor", "synthetic");
        }
        if (normalized == "built-in" || normalized == "builtin" || normalized == "sensor")
        {
            return BuiltInLibreHardwareMonitorCpuVoltageTelemetryProvider.Create(AppDomain.CurrentDomain.BaseDirectory);
        }
        if (normalized == "wmi")
        {
            return WmiCpuVoltageTelemetryProvider.Create(normalized);
        }

        ICpuVoltageTelemetryProvider builtIn = BuiltInLibreHardwareMonitorCpuVoltageTelemetryProvider.Create(AppDomain.CurrentDomain.BaseDirectory);
        if (builtIn.Available)
        {
            return builtIn;
        }

        ICpuVoltageTelemetryProvider wmi = WmiCpuVoltageTelemetryProvider.Create("wmi");
        if (wmi.Available)
        {
            return wmi;
        }

        string reason = builtIn.UnavailableReason;
        if (!String.IsNullOrWhiteSpace(wmi.UnavailableReason))
        {
            reason = String.IsNullOrWhiteSpace(reason) ? wmi.UnavailableReason : reason + " | WMI fallback: " + wmi.UnavailableReason;
        }
        return new StaticCpuVoltageTelemetryProvider(new CpuVoltageSample[0], reason, "builtin-librehardwaremonitor", "unavailable");
    }

    private static ICpuVidTelemetryProvider CreateCpuVidProvider(string provider)
    {
        string normalized = NormalizeCpuHardwareProvider(provider);
        if (normalized == "disabled")
        {
            return new StaticCpuVidTelemetryProvider(new CpuVidSample[0], "CPU Core VID telemetry is disabled.", "disabled", "disabled");
        }
        if (normalized == "synthetic")
        {
            return new StaticCpuVidTelemetryProvider(new CpuVidSample[0], "Synthetic CPU Core VID provider is only available in tests.", "synthetic-vid", "synthetic");
        }

        ICpuVidTelemetryProvider builtIn = BuiltInLibreHardwareMonitorCpuVoltageTelemetryProvider.Create(AppDomain.CurrentDomain.BaseDirectory) as ICpuVidTelemetryProvider;
        if (builtIn != null) return builtIn;
        return new StaticCpuVidTelemetryProvider(new CpuVidSample[0], NoCpuVidReason(), "builtin-librehardwaremonitor", "unavailable");
    }

    internal static ICpuVoltageTelemetryProvider CreateBuiltInCpuVoltageProviderForTests(string baseDirectory)
    {
        return BuiltInLibreHardwareMonitorCpuVoltageTelemetryProvider.Create(baseDirectory);
    }

    internal static ICpuVidTelemetryProvider CreateBuiltInCpuVidProviderForTests(string baseDirectory)
    {
        return BuiltInLibreHardwareMonitorCpuVoltageTelemetryProvider.Create(baseDirectory) as ICpuVidTelemetryProvider;
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

    internal sealed class CpuCoreCounterSet : ICpuCoreTelemetryProvider, IDisposable
    {
        private readonly List<CpuCoreCounterGroup> groups = new List<CpuCoreCounterGroup>();
        private readonly List<CpuCoreCounterSample> samples = new List<CpuCoreCounterSample>();

        private CpuCoreCounterSet()
        {
        }

        public bool Available
        {
            get { return groups.Count > 0; }
        }

        public string UnavailableReason { get; private set; }

        public static CpuCoreCounterSet Create()
        {
            CpuCoreCounterSet set = new CpuCoreCounterSet();
            try
            {
                PerformanceCounterCategory category = new PerformanceCounterCategory("Processor Information");
                foreach (string instance in category.GetInstanceNames().OrderBy(NormalizeProcessorInstanceSortKey))
                {
                    if (String.IsNullOrWhiteSpace(instance)) continue;
                    if (instance.IndexOf("_Total", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    PerformanceCounter actual = Counter("Processor Information", "Actual Frequency", instance);
                    if (actual == null) continue;

                    CpuCoreCounterGroup group = new CpuCoreCounterGroup
                    {
                        InstanceName = instance,
                        ActualFrequency = actual,
                        ProcessorFrequency = Counter("Processor Information", "Processor Frequency", instance),
                        ProcessorPerformance = Counter("Processor Information", "% Processor Performance", instance),
                        PercentOfMaximumFrequency = Counter("Processor Information", "% of Maximum Frequency", instance),
                        ProcessorUtility = Counter("Processor Information", "% Processor Utility", instance),
                        PerformanceLimitFlags = Counter("Processor Information", "Performance Limit Flags", instance)
                    };
                    CpuCoreProcessorIdentity identity = ParseCpuCoreInstanceName(instance);
                    group.Sample.InstanceName = instance;
                    group.Sample.ProcessorGroup = identity.ProcessorGroup;
                    group.Sample.LogicalProcessor = identity.LogicalProcessor;
                    group.Sample.PhysicalCoreId = identity.PhysicalCoreId;
                    group.Sample.ThreadIndex = identity.ThreadIndex;
                    Prime(group.ActualFrequency);
                    Prime(group.ProcessorFrequency);
                    Prime(group.ProcessorPerformance);
                    Prime(group.PercentOfMaximumFrequency);
                    Prime(group.ProcessorUtility);
                    Prime(group.PerformanceLimitFlags);
                    set.groups.Add(group);
                }

                if (set.groups.Count == 0)
                {
                    set.UnavailableReason = "Processor Information(*)\\Actual Frequency counter is unavailable.";
                }
            }
            catch (Exception ex)
            {
                set.UnavailableReason = "Processor Information(*)\\Actual Frequency counter is unavailable: " + ex.Message;
            }

            return set;
        }

        public IReadOnlyList<CpuCoreCounterSample> ReadSamples()
        {
            samples.Clear();
            foreach (CpuCoreCounterGroup group in groups)
            {
                double? actual = NextValue(group.ActualFrequency);
                if (!actual.HasValue) continue;
                group.Sample.ActualFrequencyMHz = actual;
                group.Sample.ProcessorFrequencyMHz = NextValue(group.ProcessorFrequency);
                group.Sample.ProcessorPerformancePct = NextValue(group.ProcessorPerformance);
                group.Sample.PercentOfMaximumFrequency = NextValue(group.PercentOfMaximumFrequency);
                group.Sample.ProcessorUtilityPct = NextValue(group.ProcessorUtility);
                group.Sample.PerformanceLimitFlags = NextValue(group.PerformanceLimitFlags);
                samples.Add(group.Sample);
            }
            return samples;
        }

        public void Dispose()
        {
            foreach (CpuCoreCounterGroup group in groups) group.Dispose();
            groups.Clear();
        }

        private static string NormalizeProcessorInstanceSortKey(string instance)
        {
            if (String.IsNullOrWhiteSpace(instance)) return "";
            string[] parts = instance.Split(',');
            if (parts.Length == 2)
            {
                int group;
                int logical;
                if (Int32.TryParse(parts[0], out group) && Int32.TryParse(parts[1], out logical))
                {
                    return group.ToString("D5") + "," + logical.ToString("D5");
                }
            }
            int single;
            if (Int32.TryParse(instance, out single)) return "00000," + single.ToString("D5");
            return instance;
        }
    }

    private sealed class CpuCoreCounterGroup : IDisposable
    {
        public string InstanceName;
        public PerformanceCounter ActualFrequency;
        public PerformanceCounter ProcessorFrequency;
        public PerformanceCounter ProcessorPerformance;
        public PerformanceCounter PercentOfMaximumFrequency;
        public PerformanceCounter ProcessorUtility;
        public PerformanceCounter PerformanceLimitFlags;
        public readonly CpuCoreCounterSample Sample = new CpuCoreCounterSample();

        public void Dispose()
        {
            DisposeCpuCoreCounter(ActualFrequency);
            DisposeCpuCoreCounter(ProcessorFrequency);
            DisposeCpuCoreCounter(ProcessorPerformance);
            DisposeCpuCoreCounter(PercentOfMaximumFrequency);
            DisposeCpuCoreCounter(ProcessorUtility);
            DisposeCpuCoreCounter(PerformanceLimitFlags);
        }

        private static void DisposeCpuCoreCounter(PerformanceCounter counter)
        {
            if (counter == null) return;
            try { counter.Dispose(); }
            catch { }
        }
    }

    internal sealed class WmiCpuVoltageTelemetryProvider : ICpuVoltageTelemetryProvider
    {
        private readonly string namespacePath;
        private IReadOnlyList<CpuVoltageSample> lastSamples;

        private WmiCpuVoltageTelemetryProvider(string namespacePath, string sourceLabel)
        {
            this.namespacePath = namespacePath;
            SourceLabel = sourceLabel;
            UnavailableReason = "";
            lastSamples = new CpuVoltageSample[0];
        }

        public bool Available { get; private set; }
        public string UnavailableReason { get; private set; }
        public string SourceLabel { get; private set; }
        public string ProviderKind { get { return "wmi"; } }

        public static ICpuVoltageTelemetryProvider Create(string provider)
        {
            List<WmiCpuVoltageTelemetryProvider> candidates = new List<WmiCpuVoltageTelemetryProvider>();
            if (provider == "auto" || provider == "sensor" || provider == "wmi")
            {
                candidates.Add(new WmiCpuVoltageTelemetryProvider(@"root\LibreHardwareMonitor", "wmi-librehardwaremonitor"));
                candidates.Add(new WmiCpuVoltageTelemetryProvider(@"root\OpenHardwareMonitor", "wmi-openhardwaremonitor"));
            }

            List<string> reasons = new List<string>();
            foreach (WmiCpuVoltageTelemetryProvider candidate in candidates)
            {
                candidate.Probe();
                if (candidate.Available) return candidate;
                if (!String.IsNullOrWhiteSpace(candidate.UnavailableReason)) reasons.Add(candidate.UnavailableReason);
            }

            string reason = reasons.Count > 0
                ? String.Join(" | ", reasons.ToArray())
                : "No LibreHardwareMonitor/OpenHardwareMonitor WMI CPU Vcore provider is available; VID/SOC/Package/VBAT/VIN and SMBIOS voltage are not accepted.";
            return new StaticCpuVoltageTelemetryProvider(new CpuVoltageSample[0], reason, "wmi-sensor", "wmi");
        }

        public IReadOnlyList<CpuVoltageSample> ReadSamples()
        {
            IReadOnlyList<CpuVoltageSample> samples = ReadSensorSamples();
            if (samples.Count > 0) lastSamples = samples;
            return samples;
        }

        private void Probe()
        {
            try
            {
                lastSamples = ReadSensorSamples();
                Available = lastSamples.Count > 0;
                if (!Available)
                {
                    UnavailableReason = namespacePath + " did not expose an explicit CPU Vcore/CPU Voltage sensor; VID/SOC/Package/VBAT/VIN are not accepted.";
                }
            }
            catch (Exception ex)
            {
                Available = false;
                UnavailableReason = namespacePath + " unavailable: " + ex.Message;
            }
        }

        private IReadOnlyList<CpuVoltageSample> ReadSensorSamples()
        {
            List<CpuVoltageSample> samples = new List<CpuVoltageSample>();
            ManagementScope scope = new ManagementScope(namespacePath);
            scope.Connect();
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Sensor")))
            using (ManagementObjectCollection collection = searcher.Get())
            {
                foreach (ManagementObject sensor in collection)
                {
                    using (sensor)
                    {
                        string sensorType = ReadWmiString(sensor, "SensorType");
                        if (sensorType.IndexOf("Voltage", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        string name = ReadWmiString(sensor, "Name");
                        string identifier = ReadWmiString(sensor, "Identifier");
                        CpuVoltageSensorClassification classification = ClassifyCpuVoltageSensorText(name, identifier, "");
                        if (!classification.Accepted) continue;

                        double? value = ReadWmiDouble(sensor, "Value");
                        if (!value.HasValue || value.Value <= 0 || value.Value >= 5) continue;

                        samples.Add(new CpuVoltageSample
                        {
                            Source = SourceLabel,
                            ProviderKind = ProviderKind,
                            VoltageV = value.Value,
                            Status = classification.Status,
                            Reason = classification.Reason,
                            SensorName = name,
                            SensorIdentifier = identifier
                        });
                    }
                }
            }
            return samples;
        }

        private static bool LooksLikeRealPerCoreVoltageSensor(string name, string identifier)
        {
            return ClassifyCpuVoltageSensorText(name, identifier, "").Accepted;
        }

        private static int? ExtractCoreIndex(string text)
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
            }
            if (start < 0) return null;

            int end = start;
            while (end < lower.Length && Char.IsDigit(lower[end])) end++;
            int parsed;
            if (!Int32.TryParse(lower.Substring(start, end - start), out parsed)) return null;
            if (parsed > 0 && (lower.IndexOf("#", coreIndex, Math.Max(0, start - coreIndex), StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("core ", StringComparison.Ordinal) >= 0))
            {
                return parsed - 1;
            }
            return parsed;
        }

        private static string ReadWmiString(ManagementBaseObject obj, string name)
        {
            try
            {
                object value = obj[name];
                return value == null ? "" : Convert.ToString(value);
            }
            catch
            {
                return "";
            }
        }

        private static double? ReadWmiDouble(ManagementBaseObject obj, string name)
        {
            try
            {
                object value = obj[name];
                if (value == null) return null;
                return Convert.ToDouble(value);
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class BuiltInLibreHardwareMonitorCpuVoltageTelemetryProvider : ICpuVoltageTelemetryProvider, ICpuVidTelemetryProvider, IDisposable
    {
        private const int SensorCacheWindowMs = 250;
        private readonly string baseDirectory;
        private readonly Stopwatch sensorCacheClock = Stopwatch.StartNew();
        private object computer;
        private Assembly assembly;
        private bool opened;
        private IReadOnlyList<CpuVoltageSample> cachedVoltageSamples = new CpuVoltageSample[0];
        private IReadOnlyList<CpuVidSample> cachedVidSamples = new CpuVidSample[0];
        private long cachedSensorElapsedMs = -1;

        private BuiltInLibreHardwareMonitorCpuVoltageTelemetryProvider(string baseDirectory)
        {
            this.baseDirectory = String.IsNullOrWhiteSpace(baseDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : baseDirectory;
            SourceLabel = "builtin-librehardwaremonitor";
            ProviderKind = "built-in";
            UnavailableReason = "";
        }

        public bool Available { get; private set; }
        public string UnavailableReason { get; private set; }
        public string SourceLabel { get; private set; }
        public string ProviderKind { get; private set; }

        public static ICpuVoltageTelemetryProvider Create(string baseDirectory)
        {
            BuiltInLibreHardwareMonitorCpuVoltageTelemetryProvider provider = new BuiltInLibreHardwareMonitorCpuVoltageTelemetryProvider(baseDirectory);
            provider.Open();
            return provider;
        }

        public IReadOnlyList<CpuVoltageSample> ReadSamples()
        {
            if (!Available || computer == null) return new CpuVoltageSample[0];
            try
            {
                RefreshSensorCacheIfNeeded();
                return cachedVoltageSamples;
            }
            catch (UnauthorizedAccessException ex)
            {
                Available = false;
                UnavailableReason = "LibreHardwareMonitorLib permission failure while reading sensors: " + ex.Message;
                ClearSensorCache();
                return new CpuVoltageSample[0];
            }
            catch (Exception ex)
            {
                Available = false;
                UnavailableReason = "LibreHardwareMonitorLib sensor read failed: " + ex.Message;
                ClearSensorCache();
                return new CpuVoltageSample[0];
            }
        }

        public IReadOnlyList<CpuVidSample> ReadVidSamples()
        {
            if (!Available || computer == null) return new CpuVidSample[0];
            try
            {
                RefreshSensorCacheIfNeeded();
                return cachedVidSamples;
            }
            catch (UnauthorizedAccessException ex)
            {
                Available = false;
                UnavailableReason = "LibreHardwareMonitorLib permission failure while reading CPU Core VID sensors: " + ex.Message;
                ClearSensorCache();
                return new CpuVidSample[0];
            }
            catch (Exception ex)
            {
                Available = false;
                UnavailableReason = "LibreHardwareMonitorLib CPU Core VID sensor read failed: " + ex.Message;
                ClearSensorCache();
                return new CpuVidSample[0];
            }
        }

        public void Dispose()
        {
            if (!opened || computer == null) return;
            try { Invoke(computer, "Close"); }
            catch { }
            opened = false;
            ClearSensorCache();
        }

        private void Open()
        {
            try
            {
                string dllPath = Path.Combine(baseDirectory, "LibreHardwareMonitorLib.dll");
                if (!File.Exists(dllPath))
                {
                    Available = false;
                    UnavailableReason = "Built-in CPU voltage provider load failed: LibreHardwareMonitorLib.dll was not found at " + dllPath + ".";
                    return;
                }

                assembly = Assembly.LoadFrom(dllPath);
                Type computerType = assembly.GetType("LibreHardwareMonitor.Hardware.Computer", true);
                computer = Activator.CreateInstance(computerType);
                SetBoolProperty(computer, "IsCpuEnabled", true);
                SetBoolProperty(computer, "IsMotherboardEnabled", true);
                Invoke(computer, "Open");
                opened = true;
                Available = true;
            }
            catch (UnauthorizedAccessException ex)
            {
                Available = false;
                UnavailableReason = "LibreHardwareMonitorLib permission failure while loading sensors: " + ex.Message;
            }
            catch (ReflectionTypeLoadException ex)
            {
                Available = false;
                UnavailableReason = "LibreHardwareMonitorLib load failed: " + ReflectionLoadMessage(ex);
            }
            catch (TargetInvocationException ex)
            {
                Available = false;
                Exception inner = ex.InnerException ?? ex;
                string prefix = inner is UnauthorizedAccessException ? "LibreHardwareMonitorLib permission failure while opening sensors: " : "LibreHardwareMonitorLib load failed while opening sensors: ";
                UnavailableReason = prefix + inner.Message;
            }
            catch (Exception ex)
            {
                Available = false;
                UnavailableReason = "LibreHardwareMonitorLib load failed: " + ex.Message;
            }
        }

        private void RefreshSensorCacheIfNeeded()
        {
            long now = sensorCacheClock.ElapsedMilliseconds;
            if (cachedSensorElapsedMs >= 0 && now - cachedSensorElapsedMs <= SensorCacheWindowMs)
            {
                return;
            }

            List<CpuVoltageSample> voltageSamples = new List<CpuVoltageSample>();
            List<CpuVidSample> vidSamples = new List<CpuVidSample>();
            foreach (object hardware in EnumerateObjects(GetProperty(computer, "Hardware")))
            {
                ReadHardware(hardware, voltageSamples, vidSamples);
            }
            cachedVoltageSamples = voltageSamples;
            cachedVidSamples = vidSamples;
            cachedSensorElapsedMs = now;
        }

        private void ClearSensorCache()
        {
            cachedVoltageSamples = new CpuVoltageSample[0];
            cachedVidSamples = new CpuVidSample[0];
            cachedSensorElapsedMs = -1;
        }

        private void ReadHardware(object hardware, List<CpuVoltageSample> voltageSamples, List<CpuVidSample> vidSamples)
        {
            if (hardware == null) return;
            try { Invoke(hardware, "Update"); }
            catch { }

            string hardwareName = Convert.ToString(GetProperty(hardware, "Name"));
            string hardwareType = Convert.ToString(GetProperty(hardware, "HardwareType"));
            string hardwareIdentifier = Convert.ToString(GetProperty(hardware, "Identifier"));

            foreach (object sensor in EnumerateObjects(GetProperty(hardware, "Sensors")))
            {
                string sensorType = Convert.ToString(GetProperty(sensor, "SensorType"));
                if (!String.Equals(sensorType, "Voltage", StringComparison.OrdinalIgnoreCase)) continue;

                double? value = ToNullableDouble(GetProperty(sensor, "Value"));
                if (!value.HasValue || value.Value <= 0 || value.Value >= 5) continue;

                string name = Convert.ToString(GetProperty(sensor, "Name"));
                string identifier = Convert.ToString(GetProperty(sensor, "Identifier"));

                CpuVoltageSample sample = ReadVoltageSensor(name, identifier, value.Value, hardwareName, hardwareType, hardwareIdentifier);
                if (sample != null) voltageSamples.Add(sample);

                CpuVidSample vidSample = ReadVidSensor(name, identifier, value.Value, hardwareName, hardwareType, hardwareIdentifier);
                if (vidSample != null) vidSamples.Add(vidSample);
            }

            foreach (object child in EnumerateObjects(GetProperty(hardware, "SubHardware")))
            {
                ReadHardware(child, voltageSamples, vidSamples);
            }
        }

        private CpuVoltageSample ReadVoltageSensor(string name, string identifier, double value, string hardwareName, string hardwareType, string hardwareIdentifier)
        {
            CpuVoltageSensorClassification classification = ClassifyCpuVoltageSensorText(name, identifier + " " + hardwareName + " " + hardwareType + " " + hardwareIdentifier, "");
            if (!classification.Accepted) return null;

            CpuVoltageSample sample = new CpuVoltageSample
            {
                Source = SourceLabel,
                ProviderKind = ProviderKind,
                SensorName = name,
                SensorIdentifier = identifier,
                VoltageV = value,
                Status = classification.Status,
                Reason = classification.Reason
            };

            return sample;
        }

        private CpuVidSample ReadVidSensor(string name, string identifier, double value, string hardwareName, string hardwareType, string hardwareIdentifier)
        {
            string text = ((name ?? "") + " " + (identifier ?? "") + " " + (hardwareName ?? "") + " " + (hardwareType ?? "") + " " + (hardwareIdentifier ?? "")).ToLowerInvariant();
            int? core = ExtractCpuVidCoreIndex(text);
            if (!IsCpuCoreVidSensorText(text, core)) return null;

            return new CpuVidSample
            {
                Source = SourceLabel,
                ProviderKind = ProviderKind,
                SensorName = name,
                SensorIdentifier = identifier,
                ProcessorGroup = "0",
                LogicalProcessor = core.Value.ToString(),
                CoreIndex = core.Value.ToString(),
                PhysicalCoreId = core.Value.ToString(),
                ThreadIndex = "",
                VidV = value,
                Status = "core-vid",
                Reason = CpuVidNote()
            };
        }

        private static bool IsAcceptedPerCoreVoltageSensor(string text, int? core)
        {
            if (!core.HasValue) return false;
            if (text.IndexOf("vid", StringComparison.Ordinal) >= 0) return false;
            if (text.IndexOf("vcore", StringComparison.Ordinal) >= 0) return false;
            if (text.IndexOf("package", StringComparison.Ordinal) >= 0) return false;
            if (text.IndexOf("soc", StringComparison.Ordinal) >= 0) return false;
            if (text.IndexOf("svi2", StringComparison.Ordinal) >= 0) return false;
            if (text.IndexOf("tfn", StringComparison.Ordinal) >= 0) return false;
            return text.IndexOf("core", StringComparison.Ordinal) >= 0;
        }

        private static bool IsCpuRelevantNonPerCoreVoltageSensor(string text)
        {
            if (text.IndexOf("vid", StringComparison.Ordinal) >= 0) return false;
            string[] needles = new[]
            {
                "cpu", "core", "vcore", "soc", "package", "vdd", "vddcr", "svi2", "tfn", "amdcpu", "intelcpu"
            };
            foreach (string needle in needles)
            {
                if (text.IndexOf(needle, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static string NonPerCoreVoltageReason(string text)
        {
            if (text.IndexOf("vid", StringComparison.Ordinal) >= 0) return "VID \u662f\u8bf7\u6c42\u7535\u538b\uff0c\u4e0d\u662f\u5b9e\u6d4b per-core voltage\u3002";
            if (text.IndexOf("vcore", StringComparison.Ordinal) >= 0) return "Aggregate Vcore \u4e0d\u662f\u5b9e\u6d4b per-core voltage\u3002";
            if (text.IndexOf("package", StringComparison.Ordinal) >= 0) return "Package voltage \u4e0d\u662f per-core voltage\u3002";
            if (text.IndexOf("soc", StringComparison.Ordinal) >= 0) return "SOC voltage \u4e0d\u662f CPU per-core voltage\u3002";
            return "\u8be5\u7535\u538b\u4f20\u611f\u5668\u672a\u66b4\u9732\u4e3a CPU per-core voltage\u3002";
        }

        private static IEnumerable<object> EnumerateObjects(object value)
        {
            System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
            if (enumerable == null) yield break;
            foreach (object item in enumerable) yield return item;
        }

        private static object GetProperty(object target, string name)
        {
            if (target == null) return null;
            PropertyInfo property = target.GetType().GetProperty(name);
            if (property == null) return null;
            try { return property.GetValue(target, null); }
            catch { return null; }
        }

        private static void SetBoolProperty(object target, string name, bool value)
        {
            if (target == null) return;
            PropertyInfo property = target.GetType().GetProperty(name);
            if (property == null || !property.CanWrite) return;
            property.SetValue(target, value, null);
        }

        private static object Invoke(object target, string name)
        {
            if (target == null) return null;
            MethodInfo method = target.GetType().GetMethod(name, Type.EmptyTypes);
            if (method == null) return null;
            return method.Invoke(target, null);
        }

        private static double? ToNullableDouble(object value)
        {
            if (value == null) return null;
            try { return Convert.ToDouble(value); }
            catch { return null; }
        }

        private static string ReflectionLoadMessage(ReflectionTypeLoadException ex)
        {
            if (ex == null) return "";
            List<string> messages = new List<string>();
            if (!String.IsNullOrWhiteSpace(ex.Message)) messages.Add(ex.Message);
            foreach (Exception loader in ex.LoaderExceptions ?? new Exception[0])
            {
                if (loader != null && !String.IsNullOrWhiteSpace(loader.Message)) messages.Add(loader.Message);
            }
            return String.Join(" | ", messages.Distinct().ToArray());
        }
    }
}
