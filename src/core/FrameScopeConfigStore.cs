using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;

public static class FrameScopeConfigStore
{
    public static IDisposable AcquireMutationLock(string path)
    {
        string full = Path.GetFullPath(path ?? "");
        string parent = Path.GetDirectoryName(full);
        Directory.CreateDirectory(parent);
        string normalized;
        using (var directory = CreateFile(parent, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
        {
            if (directory.IsInvalid) throw new IOException("Cannot resolve configuration directory.");
            var buffer = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandle(directory, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity) throw new IOException("Cannot resolve configuration directory.");
            normalized = buffer.ToString();
        }
        if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) normalized = @"\\" + normalized.Substring(8);
        else if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal)) normalized = normalized.Substring(4);
        normalized = Path.Combine(normalized, Path.GetFileName(full)).Replace('/', '\\');
        int size = LCMapStringEx("", 0x200, normalized, normalized.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (size == 0) throw new IOException("Cannot normalize configuration path.");
        var mapped = new char[size];
        if (LCMapStringEx("", 0x200, normalized, normalized.Length, mapped, size, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != size)
            throw new IOException("Cannot normalize configuration path.");
        normalized = new string(mapped);
        byte[] digest;
        using (SHA256 sha = SHA256.Create()) digest = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        Mutex mutex = new Mutex(false, "Local\\FrameScopeConfig-" + BitConverter.ToString(digest).Replace("-", ""));
        bool acquired = false;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
        catch (AbandonedMutexException) { acquired = true; /* The previous writer died; ownership is transferred to this writer. */ }
        catch { mutex.Dispose(); throw; }
        if (!acquired) { mutex.Dispose(); throw new IOException("Timed out waiting for the configuration file lock."); }
        return new MutationLock(mutex);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(string locale, uint flags, string source, int sourceLength, char[] destination, int destinationLength, IntPtr version, IntPtr reserved, IntPtr sortHandle);

    private sealed class MutationLock : IDisposable
    {
        private Mutex mutex;
        internal MutationLock(Mutex value) { mutex = value; }
        public void Dispose() { if (mutex == null) return; try { mutex.ReleaseMutex(); } finally { mutex.Dispose(); mutex = null; } }
    }
    public const string NativeMonitorMode = "native-csharp";
    public const int InternalPollIntervalMs = 1000;
    public const int PollIntervalMinMs = 250;
    public const int PollIntervalMaxMs = 10000;
    public const int DefaultTelemetrySampleIntervalMs = 1000;
    public const int TelemetrySampleIntervalMinMs = 500;
    public const int TelemetrySampleIntervalMaxMs = 5000;
    public const string ProcessSamplingModeNormal = "normal";
    public const string ProcessSamplingModeHighPrecision = "high-precision";
    public const int NormalProcessSampleIntervalMs = DefaultTelemetrySampleIntervalMs;
    public const int HighPrecisionProcessSampleIntervalMs = 100;
    public const int HardwareTelemetryMinSampleIntervalMs = TelemetrySampleIntervalMinMs;
    public const int HardwareTelemetryMaxSampleIntervalMs = TelemetrySampleIntervalMaxMs;

    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    public static string DefaultDataRoot
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FrameScopeMonitorData",
                "framescope-runs");
        }
    }

    public static FrameScopeConfig CreateDefaultConfig()
    {
        return new FrameScopeConfig
        {
            PollIntervalMs = InternalPollIntervalMs,
            TelemetrySampleIntervalMs = DefaultTelemetrySampleIntervalMs,
            DataRoot = DefaultDataRoot,
            OpenReportOnComplete = true,
            EnableVerboseLogs = false,
            EnablePerformanceDiagnosticsLogs = false,
            AutoGenerateDiagnosticReport = false,
            LogRetentionDays = 14,
            MaxLogDiskMb = 100,
            MonitorScript = NativeMonitorMode,
            ThemeMode = "system",
            CloseWindowBehavior = "minimize-to-tray",
            OverlayHotkey = "Shift+F10",
            TrayEnabled = true,
            CpuTelemetry = new FrameScopeCpuTelemetryConfig(),
            Targets = new List<FrameScopeTarget>
            {
                Target(true, "Counter-Strike 2", "cs2.exe"),
                Target(true, "PUBG: BATTLEGROUNDS", "TslGame.exe"),
                Target(true, "Delta Force", "DeltaForceClient-Win64-Shipping.exe"),
                Target(true, "Neverness To Everness", "HTGame.exe"),
                Target(false, "Valorant", "VALORANT-Win64-Shipping.exe"),
                Target(false, "Cyberpunk 2077", "Cyberpunk2077.exe"),
                Target(false, "Battlefield 6", "bf6.exe"),
                Target(false, "Hogwarts Legacy", "HogwartsLegacy.exe"),
                Target(false, "OPUS Prism Peak", "OPUS_ Prism Peak.exe")
            }
        };
    }

    public static FrameScopeConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Config path is empty.", "path");
        using (AcquireMutationLock(path))
        {
        if (!File.Exists(path))
        {
            FrameScopeConfig created = CreateDefaultConfig();
            Save(path, created);
            return created;
        }

        try
        {
            FrameScopeConfig config = Deserialize(File.ReadAllText(path));
            Normalize(config);
            return config;
        }
        catch
        {
            return CreateDefaultConfig();
        }
        }
    }

    public static void Save(string path, FrameScopeConfig config)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Config path is empty.", "path");
        using (AcquireMutationLock(path))
        {
        Normalize(config);
        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        FrameScopeJsonFile.Write(path, Json.Serialize(config));
        }
    }

    public static FrameScopeConfig Deserialize(string text)
    {
        // JSON has one number type; other writers may encode an integer as 1000.0.
        return Json.ConvertToType<FrameScopeConfig>(NormalizeIntegralJsonNumbers(Json.DeserializeObject(text)));
    }

    private static object NormalizeIntegralJsonNumbers(object value)
    {
        if (value is decimal)
        {
            decimal number = (decimal)value;
            if (number >= int.MinValue && number <= int.MaxValue && decimal.Truncate(number) == number)
                return decimal.ToInt32(number);
        }
        else if (value is double)
        {
            double number = Convert.ToDouble(value);
            if (number >= int.MinValue && number <= int.MaxValue && Math.Truncate(number) == number)
                return Convert.ToInt32(value);
        }
        var map = value as Dictionary<string, object>;
        if (map != null)
        {
            foreach (string key in map.Keys.ToArray()) map[key] = NormalizeIntegralJsonNumbers(map[key]);
        }
        var items = value as object[];
        if (items != null)
        {
            for (int i = 0; i < items.Length; i++) items[i] = NormalizeIntegralJsonNumbers(items[i]);
        }
        return value;
    }

    public static void Normalize(FrameScopeConfig config)
    {
        if (config == null) throw new InvalidOperationException("FrameScope config is empty.");
        if (config.Targets == null) config.Targets = new List<FrameScopeTarget>();
        if (string.IsNullOrWhiteSpace(config.DataRoot)) config.DataRoot = DefaultDataRoot;
        if (string.IsNullOrWhiteSpace(config.MonitorScript) ||
            config.MonitorScript.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            config.MonitorScript = NativeMonitorMode;
        }
        // The poll interval is a real user setting (settings page): honor an
        // in-range value instead of forcing the internal default back.
        if (config.PollIntervalMs <= 0) config.PollIntervalMs = InternalPollIntervalMs;
        if (config.PollIntervalMs < PollIntervalMinMs) config.PollIntervalMs = PollIntervalMinMs;
        if (config.PollIntervalMs > PollIntervalMaxMs) config.PollIntervalMs = PollIntervalMaxMs;
        config.TelemetrySampleIntervalMs = NormalizeTelemetrySampleInterval(config.TelemetrySampleIntervalMs);
        if (config.LogRetentionDays <= 0) config.LogRetentionDays = 14;
        if (config.LogRetentionDays > 365) config.LogRetentionDays = 365;
        if (config.MaxLogDiskMb <= 0) config.MaxLogDiskMb = 100;
        if (config.MaxLogDiskMb > 2048) config.MaxLogDiskMb = 2048;
        config.ThemeMode = NormalizeChoice(config.ThemeMode, "system", "light", "dark", "system");
        // "ask" is a UI-only-but-legitimate value: the frontend close-confirm
        // dialog writes it and the Tauri host honors it on CloseRequested.
        config.CloseWindowBehavior = NormalizeChoice(config.CloseWindowBehavior, "minimize-to-tray", "exit", "minimize-to-tray", "ask");
        config.OverlayHotkey = string.IsNullOrWhiteSpace(config.OverlayHotkey) ? "Shift+F10" : config.OverlayHotkey.Trim();
        NormalizeCpuTelemetry(config);
        foreach (FrameScopeTarget target in config.Targets)
        {
            NormalizeTarget(target, config.TelemetrySampleIntervalMs);
        }
    }

    public static FrameScopeConfig BuildConfigFromEditableTargets(FrameScopeConfig existing, string dataRoot, bool openReportOnComplete, IEnumerable<FrameScopeTarget> editableTargets)
    {
        if (existing == null) existing = CreateDefaultConfig();
        Normalize(existing);

        List<FrameScopeTarget> targets = new List<FrameScopeTarget>();
        foreach (FrameScopeTarget editable in editableTargets ?? Enumerable.Empty<FrameScopeTarget>())
        {
            if (editable == null) continue;
            string processName = (editable.ProcessName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(processName)) continue;

            string name = string.IsNullOrWhiteSpace(editable.Name)
                ? Path.GetFileNameWithoutExtension(processName)
                : editable.Name.Trim();
            int sampleMs = existing.TelemetrySampleIntervalMs;
            FrameScopeTarget preserved = FindExistingTarget(existing, processName, name);

            string processSamplingMode = preserved != null && !string.IsNullOrWhiteSpace(preserved.ProcessSamplingMode)
                ? preserved.ProcessSamplingMode
                : editable.ProcessSamplingMode;

            targets.Add(new FrameScopeTarget
            {
                Enabled = editable.Enabled,
                Name = name ?? processName,
                ProcessName = processName,
                SampleIntervalMs = sampleMs,
                ProcessSamplingMode = processSamplingMode,
                ProcessSampleIntervalMs = existing.TelemetrySampleIntervalMs,
                SlowSampleIntervalMs = existing.TelemetrySampleIntervalMs,
                OpenReportOnComplete = editable.OpenReportOnComplete
            });
        }

        FrameScopeConfig merged = new FrameScopeConfig
        {
            PollIntervalMs = existing.PollIntervalMs > 0 ? existing.PollIntervalMs : InternalPollIntervalMs,
            TelemetrySampleIntervalMs = existing.TelemetrySampleIntervalMs,
            DataRoot = (dataRoot ?? "").Trim(),
            OpenReportOnComplete = openReportOnComplete,
            AutoStartMonitoring = existing.AutoStartMonitoring,
            AutoStartOverlay = existing.AutoStartOverlay,
            StartMinimized = existing.StartMinimized,
            EnableVerboseLogs = existing.EnableVerboseLogs,
            EnablePerformanceDiagnosticsLogs = existing.EnablePerformanceDiagnosticsLogs,
            AutoGenerateDiagnosticReport = existing.AutoGenerateDiagnosticReport,
            LogRetentionDays = existing.LogRetentionDays,
            MaxLogDiskMb = existing.MaxLogDiskMb,
            MonitorScript = NativeMonitorMode,
            ThemeMode = existing.ThemeMode,
            CloseWindowBehavior = existing.CloseWindowBehavior,
            OverlayHotkey = existing.OverlayHotkey,
            TrayEnabled = existing.TrayEnabled,
            CpuTelemetry = CloneCpuTelemetry(existing.CpuTelemetry),
            Targets = targets
        };
        Normalize(merged);
        return merged;
    }

    public static FrameScopeTarget FindExistingTarget(FrameScopeConfig config, string processName, string name)
    {
        if (config == null || config.Targets == null) return null;
        string processBase = GetTargetBaseName(processName);
        foreach (FrameScopeTarget target in config.Targets)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.ProcessName)) continue;
            if (target.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)) return target;
        }
        foreach (FrameScopeTarget target in config.Targets)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.ProcessName)) continue;
            if (GetTargetBaseName(target.ProcessName).Equals(processBase, StringComparison.OrdinalIgnoreCase)) return target;
        }
        foreach (FrameScopeTarget target in config.Targets)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.Name) || string.IsNullOrWhiteSpace(name)) continue;
            if (target.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return target;
        }
        return null;
    }

    private static FrameScopeTarget Target(bool enabled, string name, string processName)
    {
        return new FrameScopeTarget
        {
            Enabled = enabled,
            Name = name,
            ProcessName = processName,
            SampleIntervalMs = DefaultTelemetrySampleIntervalMs,
            ProcessSamplingMode = ProcessSamplingModeNormal,
            ProcessSampleIntervalMs = DefaultTelemetrySampleIntervalMs,
            SlowSampleIntervalMs = DefaultTelemetrySampleIntervalMs,
            OpenReportOnComplete = true
        };
    }

    private static void NormalizeTarget(FrameScopeTarget target, int telemetrySampleIntervalMs)
    {
        if (target == null) return;
        target.SampleIntervalMs = telemetrySampleIntervalMs;
        // R-B6: keep a configured high-precision mode instead of silently
        // overwriting it - the "high-precision" option was unreachable because
        // every normalize pass forced "normal" back.
        target.ProcessSamplingMode = string.IsNullOrWhiteSpace(target.ProcessSamplingMode)
            ? ProcessSamplingModeNormal
            : NormalizeProcessSamplingMode(target.ProcessSamplingMode, telemetrySampleIntervalMs);
        target.ProcessSampleIntervalMs = telemetrySampleIntervalMs;
        target.SlowSampleIntervalMs = telemetrySampleIntervalMs;
    }

    private static string NormalizeProcessSamplingMode(string value, int processSampleIntervalMs)
    {
        string normalized = (value ?? "").Trim().ToLowerInvariant();
        if (string.Equals(normalized, ProcessSamplingModeHighPrecision, StringComparison.Ordinal) ||
            string.Equals(normalized, "highprecision", StringComparison.Ordinal) ||
            string.Equals(normalized, "high_precision", StringComparison.Ordinal))
        {
            return ProcessSamplingModeHighPrecision;
        }
        if (string.Equals(normalized, ProcessSamplingModeNormal, StringComparison.Ordinal))
        {
            return ProcessSamplingModeNormal;
        }
        return processSampleIntervalMs == HighPrecisionProcessSampleIntervalMs
            ? ProcessSamplingModeHighPrecision
            : ProcessSamplingModeNormal;
    }

    private static void NormalizeCpuTelemetry(FrameScopeConfig config)
    {
        if (config.CpuTelemetry == null) config.CpuTelemetry = new FrameScopeCpuTelemetryConfig();
        config.CpuTelemetry.PerCoreSampleIntervalMs = config.TelemetrySampleIntervalMs;
        config.CpuTelemetry.PerCoreVoltageSampleIntervalMs = config.TelemetrySampleIntervalMs;
        config.CpuTelemetry.VoltageProvider = NormalizeChoice(config.CpuTelemetry.VoltageProvider, "auto", "auto", "built-in", "disabled", "wmi", "sensor");
    }

    private static int NormalizeTelemetrySampleInterval(int value)
    {
        if (value <= 0) value = DefaultTelemetrySampleIntervalMs;
        if (value < TelemetrySampleIntervalMinMs) return TelemetrySampleIntervalMinMs;
        if (value > TelemetrySampleIntervalMaxMs) return TelemetrySampleIntervalMaxMs;
        return value;
    }

    private static FrameScopeCpuTelemetryConfig CloneCpuTelemetry(FrameScopeCpuTelemetryConfig config)
    {
        if (config == null) return new FrameScopeCpuTelemetryConfig();
        return new FrameScopeCpuTelemetryConfig
        {
            CollectPerCoreFrequency = config.CollectPerCoreFrequency,
            CollectCpuVoltage = config.CollectCpuVoltage,
            PerCoreSampleIntervalMs = config.PerCoreSampleIntervalMs,
            PerCoreVoltageSampleIntervalMs = config.PerCoreVoltageSampleIntervalMs,
            VoltageProvider = config.VoltageProvider
        };
    }

    private static string NormalizeChoice(string value, string fallback, params string[] allowed)
    {
        string normalized = (value ?? "").Trim().ToLowerInvariant();
        foreach (string candidate in allowed)
        {
            if (string.Equals(normalized, candidate, StringComparison.Ordinal)) return candidate;
        }
        return fallback;
    }

    private static string GetTargetBaseName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return "";
        try { return Path.GetFileNameWithoutExtension(processName.Trim()); }
        catch { return processName.Trim(); }
    }
}

public sealed class FrameScopeConfig
{
    public FrameScopeConfig()
    {
        PollIntervalMs = FrameScopeConfigStore.InternalPollIntervalMs;
        TelemetrySampleIntervalMs = FrameScopeConfigStore.DefaultTelemetrySampleIntervalMs;
        DataRoot = "";
        OpenReportOnComplete = true;
        AutoStartMonitoring = true;
        AutoStartOverlay = false;
        StartMinimized = false;
        EnableVerboseLogs = false;
        EnablePerformanceDiagnosticsLogs = false;
        AutoGenerateDiagnosticReport = false;
        LogRetentionDays = 14;
        MaxLogDiskMb = 100;
        MonitorScript = "";
        ThemeMode = "system";
        CloseWindowBehavior = "minimize-to-tray";
        OverlayHotkey = "Shift+F10";
        TrayEnabled = true;
        CpuTelemetry = new FrameScopeCpuTelemetryConfig();
        Targets = new List<FrameScopeTarget>();
    }

    public int PollIntervalMs { get; set; }
    public int TelemetrySampleIntervalMs { get; set; }
    public string DataRoot { get; set; }
    public bool OpenReportOnComplete { get; set; }
    public bool AutoStartMonitoring { get; set; }
    public bool AutoStartOverlay { get; set; }
    public bool StartMinimized { get; set; }
    public bool EnableVerboseLogs { get; set; }
    public bool EnablePerformanceDiagnosticsLogs { get; set; }
    public bool AutoGenerateDiagnosticReport { get; set; }
    public int LogRetentionDays { get; set; }
    public int MaxLogDiskMb { get; set; }
    public string MonitorScript { get; set; }
    public string ThemeMode { get; set; }
    public string CloseWindowBehavior { get; set; }
    public string OverlayHotkey { get; set; }
    public bool TrayEnabled { get; set; }
    public FrameScopeCpuTelemetryConfig CpuTelemetry { get; set; }
    public List<FrameScopeTarget> Targets { get; set; }
}

public sealed class FrameScopeCpuTelemetryConfig
{
    public FrameScopeCpuTelemetryConfig()
    {
        CollectPerCoreFrequency = true;
        CollectCpuVoltage = false;
        PerCoreSampleIntervalMs = FrameScopeConfigStore.DefaultTelemetrySampleIntervalMs;
        PerCoreVoltageSampleIntervalMs = FrameScopeConfigStore.DefaultTelemetrySampleIntervalMs;
        VoltageProvider = "auto";
    }

    public bool CollectPerCoreFrequency { get; set; }
    public bool CollectCpuVoltage { get; set; }
    public int PerCoreSampleIntervalMs { get; set; }
    public int PerCoreVoltageSampleIntervalMs { get; set; }
    public string VoltageProvider { get; set; }
}

public sealed class FrameScopeTarget
{
    public FrameScopeTarget()
    {
        Name = "";
        ProcessName = "";
        SampleIntervalMs = FrameScopeConfigStore.DefaultTelemetrySampleIntervalMs;
        ProcessSamplingMode = "";
        ProcessSampleIntervalMs = FrameScopeConfigStore.NormalProcessSampleIntervalMs;
        SlowSampleIntervalMs = FrameScopeConfigStore.DefaultTelemetrySampleIntervalMs;
        OpenReportOnComplete = true;
    }

    public bool Enabled { get; set; }
    public string Name { get; set; }
    public string ProcessName { get; set; }
    public int SampleIntervalMs { get; set; }
    public string ProcessSamplingMode { get; set; }
    public int ProcessSampleIntervalMs { get; set; }
    public int SlowSampleIntervalMs { get; set; }
    public bool OpenReportOnComplete { get; set; }
}
