using System;
using System.Collections.Generic;
using System.IO;

public static class FrameScopeConfigStoreTests
{
    public static int Main()
    {
        try
        {
            CreateDefaultConfigUsesNativeModeAndSeparateDataRoot();
            CreateDefaultConfigIncludesThemeWindowAndCpuTelemetryDefaults();
            CreateDefaultConfigUsesOneGlobalTelemetrySampleInterval();
            CreateDefaultTargetsUseNormalProcessSamplingProfile();
            NormalizeKeepsSlowSamplerAtLeastAsSlowAsFrameSampler();
            NormalizePinsLegacyPollIntervalToInternalValue();
            NormalizeMigratesLegacyHighPrecisionProcessSamplingToGlobalInterval();
            NormalizeUsesGlobalTelemetrySampleIntervalForFrameScopeOwnedSamplers();
            NormalizeClampsGlobalTelemetrySampleInterval();
            NormalizeClampsThemeWindowAndCpuTelemetryFields();
            BuildConfigFromEditableTargetsMigratesHiddenIntervalsToGlobalInterval();
            BuildConfigFromEditableTargetsUsesNormalProcessSamplingForNewTargets();
            BuildConfigFromEditableTargetsPreservesThemeWindowAndCpuTelemetryFields();
            SaveAndLoadRoundTripsNormalizedConfig();
            LoadLegacyConfigWithPollInterval();
            LoadLegacyPerTargetSamplingDoesNotPolluteGlobalInterval();
            Console.WriteLine("FrameScopeConfigStoreTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
    }

    private static void CreateDefaultConfigUsesNativeModeAndSeparateDataRoot()
    {
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
        AssertEqual("native-csharp", config.MonitorScript, "default monitor mode");
        AssertTrue(config.DataRoot.EndsWith(Path.Combine("FrameScopeMonitorData", "framescope-runs"), StringComparison.OrdinalIgnoreCase), "default data root should not be under app directory");
        AssertEqual(9, config.Targets.Count, "default target count");
        AssertEqual(14, config.LogRetentionDays, "default log retention");
        AssertEqual(100, config.MaxLogDiskMb, "default log disk cap");
    }

    private static void CreateDefaultConfigIncludesThemeWindowAndCpuTelemetryDefaults()
    {
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();

        AssertEqual("system", config.ThemeMode, "default theme mode");
        AssertEqual("minimize-to-tray", config.CloseWindowBehavior, "default close behavior");
        AssertEqual(true, config.TrayEnabled, "default tray enabled");
        AssertTrue(config.CpuTelemetry != null, "default cpu telemetry should be present");
        AssertEqual(true, config.CpuTelemetry.CollectPerCoreFrequency, "default per-core frequency collection");
        AssertEqual(false, config.CpuTelemetry.CollectCpuVoltage, "default deprecated real Vcore collection");
        AssertEqual(1000, config.CpuTelemetry.PerCoreSampleIntervalMs, "default per-core sample interval");
        AssertEqual(1000, config.CpuTelemetry.PerCoreVoltageSampleIntervalMs, "default per-core voltage sample interval");
        AssertEqual("auto", config.CpuTelemetry.VoltageProvider, "default voltage provider");
    }

    private static void CreateDefaultConfigUsesOneGlobalTelemetrySampleInterval()
    {
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();

        AssertEqual(1000, FrameScopeConfigStore.DefaultTelemetrySampleIntervalMs, "default global telemetry interval constant");
        AssertEqual(500, FrameScopeConfigStore.TelemetrySampleIntervalMinMs, "global telemetry interval lower bound");
        AssertEqual(5000, FrameScopeConfigStore.TelemetrySampleIntervalMaxMs, "global telemetry interval upper bound");
        AssertEqual(1000, config.TelemetrySampleIntervalMs, "default global telemetry interval");
        AssertEqual(1000, config.CpuTelemetry.PerCoreSampleIntervalMs, "default CPU core interval follows global telemetry interval");
        AssertEqual(1000, config.CpuTelemetry.PerCoreVoltageSampleIntervalMs, "default CPU voltage interval follows global telemetry interval");
        foreach (FrameScopeTarget target in config.Targets)
        {
            AssertEqual(1000, target.ProcessSampleIntervalMs, "default process interval follows global telemetry interval for " + target.Name);
            AssertEqual(1000, target.SlowSampleIntervalMs, "default system interval follows global telemetry interval for " + target.Name);
        }
    }

    private static void CreateDefaultTargetsUseNormalProcessSamplingProfile()
    {
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();

        foreach (FrameScopeTarget target in config.Targets)
        {
            AssertEqual("normal", target.ProcessSamplingMode, "default process sampling mode for " + target.Name);
            AssertEqual(1000, target.ProcessSampleIntervalMs, "default normal process interval for " + target.Name);
            AssertEqual(1000, target.SlowSampleIntervalMs, "default slow sampler interval for " + target.Name);
        }
    }

    private static void NormalizeKeepsSlowSamplerAtLeastAsSlowAsFrameSampler()
    {
        FrameScopeConfig config = new FrameScopeConfig
        {
            PollIntervalMs = 0,
            DataRoot = "",
            MonitorScript = "Monitor-CS2-HighFreq.ps1",
            LogRetentionDays = -9,
            MaxLogDiskMb = 0,
            Targets = new List<FrameScopeTarget>
            {
                new FrameScopeTarget
                {
                    Enabled = true,
                    Name = "High interval",
                    ProcessName = "High.exe",
                    SampleIntervalMs = 1500,
                    ProcessSampleIntervalMs = 10,
                    SlowSampleIntervalMs = 200
                }
            }
        };

        FrameScopeConfigStore.Normalize(config);

        AssertEqual(FrameScopeConfigStore.InternalPollIntervalMs, config.PollIntervalMs, "poll interval fallback");
        AssertEqual("native-csharp", config.MonitorScript, "legacy monitor mode normalization");
        AssertEqual(14, config.LogRetentionDays, "log retention fallback");
        AssertEqual(100, config.MaxLogDiskMb, "log disk cap fallback");
        AssertEqual(1000, config.Targets[0].ProcessSampleIntervalMs, "invalid normal process sampler fallback");
        AssertEqual(1000, config.Targets[0].SampleIntervalMs, "target sample interval follows missing-global default");
        AssertEqual(1000, config.Targets[0].SlowSampleIntervalMs, "slow sampler follows missing-global default");
    }

    private static void NormalizePinsLegacyPollIntervalToInternalValue()
    {
        FrameScopeConfig config = new FrameScopeConfig
        {
            PollIntervalMs = 333,
            Targets = new List<FrameScopeTarget>()
        };

        FrameScopeConfigStore.Normalize(config);

        AssertEqual(1000, FrameScopeConfigStore.InternalPollIntervalMs, "internal watcher poll interval");
        AssertEqual(FrameScopeConfigStore.InternalPollIntervalMs, config.PollIntervalMs, "legacy poll interval should be normalized to internal value");
    }

    private static void NormalizeMigratesLegacyHighPrecisionProcessSamplingToGlobalInterval()
    {
        FrameScopeConfig config = new FrameScopeConfig
        {
            Targets = new List<FrameScopeTarget>
            {
                new FrameScopeTarget
                {
                    Enabled = true,
                    Name = "Legacy high precision",
                    ProcessName = "Legacy.exe",
                    SampleIntervalMs = 100,
                    ProcessSampleIntervalMs = 100,
                    SlowSampleIntervalMs = 1000
                },
                new FrameScopeTarget
                {
                    Enabled = true,
                    Name = "Normal explicit",
                    ProcessName = "Normal.exe",
                    SampleIntervalMs = 100,
                    ProcessSamplingMode = "normal",
                    ProcessSampleIntervalMs = 250,
                    SlowSampleIntervalMs = 1000
                }
            }
        };

        FrameScopeConfigStore.Normalize(config);

        AssertEqual(1000, config.TelemetrySampleIntervalMs, "missing global telemetry interval should default");
        AssertEqual("normal", config.Targets[0].ProcessSamplingMode, "legacy 100ms mode should migrate to normal global sampling");
        AssertEqual(1000, config.Targets[0].ProcessSampleIntervalMs, "legacy 100ms process interval should migrate to global interval");
        AssertEqual("normal", config.Targets[1].ProcessSamplingMode, "normal process sampling mode should be preserved");
        AssertEqual(1000, config.Targets[1].ProcessSampleIntervalMs, "normal process interval should migrate to global interval");
    }

    private static void NormalizeUsesGlobalTelemetrySampleIntervalForFrameScopeOwnedSamplers()
    {
        FrameScopeConfig config = new FrameScopeConfig
        {
            PollIntervalMs = 100,
            TelemetrySampleIntervalMs = 1500,
            CpuTelemetry = new FrameScopeCpuTelemetryConfig
            {
                CollectPerCoreFrequency = true,
                CollectCpuVoltage = true,
                PerCoreSampleIntervalMs = 750,
                PerCoreVoltageSampleIntervalMs = 1750,
                VoltageProvider = "built-in"
            },
            Targets = new List<FrameScopeTarget>
            {
                new FrameScopeTarget
                {
                    Enabled = true,
                    Name = "Sampling Game",
                    ProcessName = "Sampling.exe",
                    SampleIntervalMs = 100,
                    ProcessSamplingMode = "normal",
                    ProcessSampleIntervalMs = 250,
                    SlowSampleIntervalMs = 1250,
                    OpenReportOnComplete = true
                }
            }
        };

        FrameScopeConfigStore.Normalize(config);

        AssertEqual(FrameScopeConfigStore.InternalPollIntervalMs, config.PollIntervalMs, "poll interval should be compatibility-only");
        AssertEqual(1500, config.TelemetrySampleIntervalMs, "global telemetry interval should be preserved");
        AssertEqual(1500, config.Targets[0].SampleIntervalMs, "legacy target sample interval should be normalized to global telemetry interval");
        AssertEqual(1500, config.Targets[0].ProcessSampleIntervalMs, "process sampler interval should follow global telemetry interval");
        AssertEqual(1500, config.Targets[0].SlowSampleIntervalMs, "system sampler interval should follow global telemetry interval");
        AssertEqual(1500, config.CpuTelemetry.PerCoreSampleIntervalMs, "cpu core sample interval should follow global telemetry interval");
        AssertEqual(1500, config.CpuTelemetry.PerCoreVoltageSampleIntervalMs, "cpu voltage and VID sample interval should follow global telemetry interval");
    }

    private static void NormalizeClampsGlobalTelemetrySampleInterval()
    {
        FrameScopeConfig config = new FrameScopeConfig
        {
            TelemetrySampleIntervalMs = 25,
            CpuTelemetry = new FrameScopeCpuTelemetryConfig(),
            Targets = new List<FrameScopeTarget>
            {
                new FrameScopeTarget { Enabled = true, Name = "Low", ProcessName = "Low.exe", SampleIntervalMs = 100, ProcessSampleIntervalMs = 100, SlowSampleIntervalMs = 100 }
            }
        };

        FrameScopeConfigStore.Normalize(config);

        AssertEqual(500, config.TelemetrySampleIntervalMs, "global telemetry lower bound");
        AssertEqual(500, config.Targets[0].ProcessSampleIntervalMs, "process lower bound follows global telemetry lower bound");
        AssertEqual(500, config.Targets[0].SlowSampleIntervalMs, "system lower bound follows global telemetry lower bound");
        AssertEqual(500, config.CpuTelemetry.PerCoreSampleIntervalMs, "cpu core lower bound follows global telemetry lower bound");
        AssertEqual(500, config.CpuTelemetry.PerCoreVoltageSampleIntervalMs, "cpu voltage lower bound follows global telemetry lower bound");

        config.TelemetrySampleIntervalMs = 6000;
        FrameScopeConfigStore.Normalize(config);

        AssertEqual(5000, config.TelemetrySampleIntervalMs, "global telemetry upper bound");
        AssertEqual(5000, config.Targets[0].ProcessSampleIntervalMs, "process upper bound follows global telemetry upper bound");
        AssertEqual(5000, config.Targets[0].SlowSampleIntervalMs, "system upper bound follows global telemetry upper bound");
        AssertEqual(5000, config.CpuTelemetry.PerCoreSampleIntervalMs, "cpu core upper bound follows global telemetry upper bound");
        AssertEqual(5000, config.CpuTelemetry.PerCoreVoltageSampleIntervalMs, "cpu voltage upper bound follows global telemetry upper bound");
    }

    private static void NormalizeClampsThemeWindowAndCpuTelemetryFields()
    {
        FrameScopeConfig config = new FrameScopeConfig
        {
            ThemeMode = "sepia",
            CloseWindowBehavior = "hide",
            TrayEnabled = false,
            TelemetrySampleIntervalMs = 25,
            CpuTelemetry = new FrameScopeCpuTelemetryConfig
            {
                CollectPerCoreFrequency = true,
                CollectCpuVoltage = true,
                PerCoreSampleIntervalMs = 25,
                PerCoreVoltageSampleIntervalMs = 25,
                VoltageProvider = "unknown"
            }
        };

        FrameScopeConfigStore.Normalize(config);

        AssertEqual("system", config.ThemeMode, "invalid theme fallback");
        AssertEqual("minimize-to-tray", config.CloseWindowBehavior, "invalid close behavior fallback");
        AssertEqual(false, config.TrayEnabled, "tray enabled should preserve explicit false");
        AssertTrue(config.CpuTelemetry != null, "cpu telemetry object should be normalized");
        AssertEqual(true, config.CpuTelemetry.CollectPerCoreFrequency, "per-core frequency toggle should be preserved");
        AssertEqual(true, config.CpuTelemetry.CollectCpuVoltage, "voltage toggle should be preserved");
        AssertEqual(500, config.CpuTelemetry.PerCoreSampleIntervalMs, "per-core sample interval lower bound");
        AssertEqual(500, config.CpuTelemetry.PerCoreVoltageSampleIntervalMs, "per-core voltage sample interval lower bound");
        AssertEqual("auto", config.CpuTelemetry.VoltageProvider, "invalid voltage provider fallback");

        config.ThemeMode = "DARK";
        config.CloseWindowBehavior = "EXIT";
        config.TelemetrySampleIntervalMs = 6000;
        config.CpuTelemetry.PerCoreSampleIntervalMs = 6000;
        config.CpuTelemetry.PerCoreVoltageSampleIntervalMs = 6000;
        config.CpuTelemetry.VoltageProvider = "DISABLED";
        FrameScopeConfigStore.Normalize(config);

        AssertEqual("dark", config.ThemeMode, "theme mode should normalize case");
        AssertEqual("exit", config.CloseWindowBehavior, "close behavior should normalize case");
        AssertEqual(5000, config.CpuTelemetry.PerCoreSampleIntervalMs, "per-core sample interval upper bound");
        AssertEqual(5000, config.CpuTelemetry.PerCoreVoltageSampleIntervalMs, "per-core voltage sample interval upper bound");
        AssertEqual("disabled", config.CpuTelemetry.VoltageProvider, "voltage provider should normalize case");
    }

    private static void BuildConfigFromEditableTargetsMigratesHiddenIntervalsToGlobalInterval()
    {
        FrameScopeConfig existing = new FrameScopeConfig
        {
            PollIntervalMs = 2222,
            TelemetrySampleIntervalMs = 1500,
            DataRoot = @"C:\OldDataRoot",
            OpenReportOnComplete = true,
            EnableVerboseLogs = true,
            EnablePerformanceDiagnosticsLogs = true,
            AutoGenerateDiagnosticReport = true,
            LogRetentionDays = 30,
            MaxLogDiskMb = 250,
            MonitorScript = "native-csharp",
            Targets = new List<FrameScopeTarget>
            {
                new FrameScopeTarget
                {
                    Enabled = true,
                    Name = "Example Game",
                    ProcessName = "Example.exe",
                    SampleIntervalMs = 120,
                    ProcessSampleIntervalMs = 250,
                    SlowSampleIntervalMs = 3000,
                    OpenReportOnComplete = true
                }
            }
        };

        FrameScopeConfig merged = FrameScopeConfigStore.BuildConfigFromEditableTargets(
            existing,
            @"C:\NewDataRoot",
            false,
            new[]
            {
                new FrameScopeTarget
                {
                    Enabled = true,
                    Name = "Example Game",
                    ProcessName = "Example.exe",
                    SampleIntervalMs = 120,
                    OpenReportOnComplete = false
                }
            });

        AssertEqual(FrameScopeConfigStore.InternalPollIntervalMs, merged.PollIntervalMs, "poll interval should be internal compatibility value");
        AssertEqual(@"C:\NewDataRoot", merged.DataRoot, "editable data root should be used");
        AssertEqual(false, merged.OpenReportOnComplete, "editable global auto-open should be used");
        AssertEqual(true, merged.EnableVerboseLogs, "verbose setting should be preserved");
        AssertEqual(true, merged.EnablePerformanceDiagnosticsLogs, "perf setting should be preserved");
        AssertEqual(true, merged.AutoGenerateDiagnosticReport, "auto diagnostic setting should be preserved");
        AssertEqual(30, merged.LogRetentionDays, "retention setting should be preserved");
        AssertEqual(250, merged.MaxLogDiskMb, "disk cap setting should be preserved");
        AssertEqual(1500, merged.TelemetrySampleIntervalMs, "global telemetry interval should be preserved");
        AssertEqual(1500, merged.Targets[0].SampleIntervalMs, "editable target sample interval should migrate to global interval");
        AssertEqual(1500, merged.Targets[0].ProcessSampleIntervalMs, "hidden process interval should migrate to global interval");
        AssertEqual("normal", merged.Targets[0].ProcessSamplingMode, "hidden process sampling mode should be inferred");
        AssertEqual(1500, merged.Targets[0].SlowSampleIntervalMs, "hidden slow interval should migrate to global interval");
        AssertEqual(false, merged.Targets[0].OpenReportOnComplete, "editable target auto-open should be used");
    }

    private static void BuildConfigFromEditableTargetsUsesNormalProcessSamplingForNewTargets()
    {
        FrameScopeConfig existing = FrameScopeConfigStore.CreateDefaultConfig();
        existing.Targets.Clear();

        FrameScopeConfig merged = FrameScopeConfigStore.BuildConfigFromEditableTargets(
            existing,
            @"C:\NewDataRoot",
            true,
            new[]
            {
                new FrameScopeTarget
                {
                    Enabled = true,
                    Name = "New normal game",
                    ProcessName = "NewNormal.exe",
                    SampleIntervalMs = 100,
                    ProcessSamplingMode = "normal",
                    ProcessSampleIntervalMs = 1000,
                    SlowSampleIntervalMs = 1000,
                    OpenReportOnComplete = true
                }
            });

        AssertEqual("normal", merged.Targets[0].ProcessSamplingMode, "new target process sampling mode");
        AssertEqual(1000, merged.TelemetrySampleIntervalMs, "new target default global telemetry interval");
        AssertEqual(1000, merged.Targets[0].SampleIntervalMs, "new target sample interval follows global telemetry interval");
        AssertEqual(1000, merged.Targets[0].ProcessSampleIntervalMs, "new target normal process interval");
    }

    private static void BuildConfigFromEditableTargetsPreservesThemeWindowAndCpuTelemetryFields()
    {
        FrameScopeConfig existing = new FrameScopeConfig
        {
            PollIntervalMs = 1000,
            TelemetrySampleIntervalMs = 1500,
            DataRoot = @"C:\OldDataRoot",
            ThemeMode = "dark",
            CloseWindowBehavior = "exit",
            TrayEnabled = false,
            CpuTelemetry = new FrameScopeCpuTelemetryConfig
            {
                CollectPerCoreFrequency = true,
                CollectCpuVoltage = true,
                PerCoreSampleIntervalMs = 1500,
                PerCoreVoltageSampleIntervalMs = 1750,
                VoltageProvider = "disabled"
            },
            Targets = new List<FrameScopeTarget>
            {
                new FrameScopeTarget
                {
                    Enabled = true,
                    Name = "Example Game",
                    ProcessName = "Example.exe",
                    SampleIntervalMs = 120,
                    ProcessSampleIntervalMs = 250,
                    SlowSampleIntervalMs = 3000,
                    OpenReportOnComplete = true
                }
            }
        };

        FrameScopeConfig merged = FrameScopeConfigStore.BuildConfigFromEditableTargets(
            existing,
            @"C:\NewDataRoot",
            false,
            new[]
            {
                new FrameScopeTarget
                {
                    Enabled = true,
                    Name = "Example Game",
                    ProcessName = "Example.exe",
                    SampleIntervalMs = 120,
                    OpenReportOnComplete = false
                }
            });

        AssertEqual("dark", merged.ThemeMode, "theme mode should be preserved");
        AssertEqual("exit", merged.CloseWindowBehavior, "close behavior should be preserved");
        AssertEqual(false, merged.TrayEnabled, "tray enabled should be preserved");
        AssertTrue(merged.CpuTelemetry != null, "cpu telemetry should be preserved");
        AssertEqual(true, merged.CpuTelemetry.CollectPerCoreFrequency, "per-core frequency toggle should be preserved");
        AssertEqual(true, merged.CpuTelemetry.CollectCpuVoltage, "voltage toggle should be preserved");
        AssertEqual(1500, merged.TelemetrySampleIntervalMs, "global telemetry interval should be preserved");
        AssertEqual(1500, merged.CpuTelemetry.PerCoreSampleIntervalMs, "per-core interval should follow global interval");
        AssertEqual(1500, merged.CpuTelemetry.PerCoreVoltageSampleIntervalMs, "per-core voltage interval should follow global interval");
        AssertEqual("disabled", merged.CpuTelemetry.VoltageProvider, "voltage provider should be preserved");
    }

    private static void SaveAndLoadRoundTripsNormalizedConfig()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-config-store-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "framescope-config.json");
        try
        {
            FrameScopeConfig config = new FrameScopeConfig
            {
                PollIntervalMs = 333,
                TelemetrySampleIntervalMs = 0,
                DataRoot = Path.Combine(dir, "runs"),
                OpenReportOnComplete = true,
                MonitorScript = "native-csharp",
                Targets = new List<FrameScopeTarget>
                {
                    new FrameScopeTarget { Enabled = true, Name = "Roundtrip", ProcessName = "Roundtrip.exe", SampleIntervalMs = 60, ProcessSampleIntervalMs = 80, SlowSampleIntervalMs = 40, OpenReportOnComplete = true }
                }
            };

            FrameScopeConfigStore.Save(path, config);
            FrameScopeConfig loaded = FrameScopeConfigStore.Load(path);

            AssertEqual(FrameScopeConfigStore.InternalPollIntervalMs, loaded.PollIntervalMs, "saved poll interval");
            AssertEqual(1000, loaded.TelemetrySampleIntervalMs, "missing global telemetry interval default");
            AssertEqual(1000, loaded.Targets[0].ProcessSampleIntervalMs, "saved process interval normalization");
            AssertEqual(1000, loaded.Targets[0].SlowSampleIntervalMs, "saved slow interval normalization");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void LoadLegacyConfigWithPollInterval()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-config-store-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "framescope-config.json");
        try
        {
            File.WriteAllText(
                path,
                "{\"PollIntervalMs\":333,\"DataRoot\":\"" + EscapeJson(Path.Combine(dir, "runs")) + "\",\"MonitorScript\":\"native-csharp\",\"Targets\":[{\"Enabled\":true,\"Name\":\"Legacy\",\"ProcessName\":\"Legacy.exe\",\"SampleIntervalMs\":100,\"ProcessSamplingMode\":\"normal\",\"ProcessSampleIntervalMs\":250,\"SlowSampleIntervalMs\":1250,\"OpenReportOnComplete\":true}]}");

            FrameScopeConfig loaded = FrameScopeConfigStore.Load(path);

            AssertEqual(FrameScopeConfigStore.InternalPollIntervalMs, loaded.PollIntervalMs, "legacy poll interval should load as internal value");
            AssertEqual(1000, loaded.TelemetrySampleIntervalMs, "legacy missing global telemetry interval should default");
            AssertEqual(1000, loaded.Targets[0].SampleIntervalMs, "legacy target sample interval should migrate to global default");
            AssertEqual(1000, loaded.Targets[0].ProcessSampleIntervalMs, "legacy process sampler interval should migrate to global default");
            AssertEqual(1000, loaded.Targets[0].SlowSampleIntervalMs, "legacy slow sampler interval should migrate to global default");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void LoadLegacyPerTargetSamplingDoesNotPolluteGlobalInterval()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-config-store-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "framescope-config.json");
        try
        {
            File.WriteAllText(
                path,
                "{\"DataRoot\":\"" + EscapeJson(Path.Combine(dir, "runs")) + "\",\"MonitorScript\":\"native-csharp\",\"Targets\":[{\"Enabled\":true,\"Name\":\"Legacy100\",\"ProcessName\":\"Legacy100.exe\",\"SampleIntervalMs\":100,\"ProcessSampleIntervalMs\":100,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}]}");

            FrameScopeConfig loaded = FrameScopeConfigStore.Load(path);

            AssertEqual(1000, loaded.TelemetrySampleIntervalMs, "legacy per-target 100ms should not become the global interval");
            AssertEqual(1000, loaded.Targets[0].SampleIntervalMs, "legacy target SampleIntervalMs should normalize to global interval");
            AssertEqual(1000, loaded.Targets[0].ProcessSampleIntervalMs, "legacy target ProcessSampleIntervalMs should normalize to global interval");
            AssertEqual(1000, loaded.Targets[0].SlowSampleIntervalMs, "legacy target SlowSampleIntervalMs should normalize to global interval");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static string EscapeJson(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
    }
}
