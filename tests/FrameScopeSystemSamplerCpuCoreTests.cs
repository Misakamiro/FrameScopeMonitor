using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

public static class FrameScopeSystemSamplerCpuCoreTests
{
    public static int Main()
    {
        try
        {
            CpuCoreHeaderMatchesPhaseOneSchema();
            CpuVidHeaderMatchesDedicatedSchema();
            CpuCoreInstanceParserKeepsLogicalProcessorSemantics();
            DisabledCpuCoreTelemetryDoesNotCreateCsvOrStatusNoise();
            CpuCoreSampleIntervalDefaultsTo1000AndClampsTo500();
            CpuVoltageSampleIntervalDefaultsTo1000AndClampsTo500();
            CpuVidSampleIntervalDefaultsTo1000AndClampsTo500();
            UnavailableCpuCoreTelemetryWritesStatusWithoutCsv();
            UnavailableCpuVoltageTelemetryWritesStatusWithoutCsv();
            UnavailableCpuVidTelemetryWritesChineseStatusWithoutCsv();
            CpuCoreStatusCountsRowsAndMarksVoltageUnavailable();
            SyntheticCpuVoltageTelemetryWritesVcoreCsvAndStatus();
            NonVcoreCpuVoltageTelemetryDoesNotCreateCpuVoltageData();
            SyntheticCpuVidTelemetryWritesDedicatedCsvAndStatus();
            CpuVidCoreIndexParserKeepsZeroBasedAndOneBasedNamesDistinct();
            CpuVoltageTelemetryDoesNotRecordVidAsVoltage();
            BuiltInCpuVoltageProviderMissingDependencyIsUnavailable();
            AutoCpuVoltageAndVidProvidersShareBuiltInHardwareProvider();
            Console.WriteLine("FrameScopeSystemSamplerCpuCoreTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace ?? "");
            return 1;
        }
    }

    private static void CpuCoreHeaderMatchesPhaseOneSchema()
    {
        string expected = "Time,SampleIndex,ElapsedMs,Source,ProcessorGroup,LogicalProcessor,PhysicalCoreId,ThreadIndex,ActualFrequencyMHz,ProcessorFrequencyMHz,ProcessorPerformancePct,PercentOfMaximumFrequency,ProcessorUtilityPct,PerformanceLimitFlags";
        AssertEqual(expected, FrameScopeSystemSampler.CpuCoreCsvHeaderLine, "cpu-core header");
        AssertEqual("Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreId,PhysicalCoreId,ThreadIndex,VoltageVolts,Status,Reason,SensorIdentifier", FrameScopeSystemSampler.CpuVoltageCsvHeaderLine, "cpu-voltage header");
    }

    private static void CpuVidHeaderMatchesDedicatedSchema()
    {
        AssertEqual(
            "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreIndex,PhysicalCoreId,ThreadIndex,VidVolts,Status,Reason,SensorIdentifier",
            FrameScopeSystemSampler.CpuVidCsvHeaderLine,
            "cpu-vid header");
    }

    private static void CpuCoreInstanceParserKeepsLogicalProcessorSemantics()
    {
        FrameScopeSystemSampler.CpuCoreProcessorIdentity identity = FrameScopeSystemSampler.ParseCpuCoreInstanceNameForTests("0,15");

        AssertEqual("0", identity.ProcessorGroup, "processor group");
        AssertEqual("15", identity.LogicalProcessor, "logical processor");
        AssertEqual("", identity.PhysicalCoreId, "physical core id should be empty in phase one");
        AssertEqual("", identity.ThreadIndex, "thread index should be empty in phase one");
    }

    private static void DisabledCpuCoreTelemetryDoesNotCreateCsvOrStatusNoise()
    {
        string dir = CreateTempDir("framescope-cpu-core-disabled-tests-");
        try
        {
            string csv = Path.Combine(dir, "cpu-core-samples.csv");
            string status = Path.Combine(dir, "cpu-core-telemetry-status.json");
            FrameScopeSystemSampler.CpuCoreTelemetryOptions options = new FrameScopeSystemSampler.CpuCoreTelemetryOptions
            {
                Enabled = false,
                CsvPath = csv,
                StatusPath = status,
                SampleIntervalMs = 1000
            };

            using (FrameScopeSystemSampler.CpuCoreTelemetrySession session = FrameScopeSystemSampler.CreateCpuCoreTelemetrySessionForTests(options, new FrameScopeSystemSampler.StaticCpuCoreTelemetryProvider(new FrameScopeSystemSampler.CpuCoreCounterSample[0], "")))
            {
                AssertTrue(!session.Enabled, "disabled session should stay disabled");
            }

            AssertTrue(!File.Exists(csv), "disabled telemetry should not create cpu-core csv");
            AssertTrue(!File.Exists(status), "disabled telemetry should not create status sidecar");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void UnavailableCpuCoreTelemetryWritesStatusWithoutCsv()
    {
        string dir = CreateTempDir("framescope-cpu-core-unavailable-tests-");
        try
        {
            string csv = Path.Combine(dir, "cpu-core-samples.csv");
            string status = Path.Combine(dir, "cpu-core-telemetry-status.json");
            FrameScopeSystemSampler.CpuCoreTelemetryOptions options = new FrameScopeSystemSampler.CpuCoreTelemetryOptions
            {
                Enabled = true,
                CsvPath = csv,
                StatusPath = status,
                SampleIntervalMs = 250
            };

            using (FrameScopeSystemSampler.CpuCoreTelemetrySession session = FrameScopeSystemSampler.CreateCpuCoreTelemetrySessionForTests(options, new FrameScopeSystemSampler.StaticCpuCoreTelemetryProvider(new FrameScopeSystemSampler.CpuCoreCounterSample[0], "Actual Frequency unavailable for tests")))
            {
                session.TryWriteSample(0, 0);
            }

            AssertTrue(!File.Exists(csv), "unavailable telemetry should not create data csv");
            Dictionary<string, object> map = ReadJson(status);
            AssertEqual(true, Convert.ToBoolean(map["Enabled"]), "status enabled");
            AssertEqual(false, Convert.ToBoolean(map["CpuCoreTelemetryAvailable"]), "status availability");
            AssertEqual("Actual Frequency unavailable for tests", Convert.ToString(map["CpuCoreTelemetryUnavailableReason"]), "status unavailable reason");
            AssertEqual(500, Convert.ToInt32(map["CpuCoreSampleIntervalMs"]), "status interval lower bound");
            AssertEqual(0, Convert.ToInt32(map["CpuCoreSampleCount"]), "status sample count");
            AssertEqual(false, Convert.ToBoolean(map["CpuVoltageAvailable"]), "voltage availability");
            AssertEqual("unavailable", Convert.ToString(map["CpuVoltageStatus"]), "voltage status");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void CpuCoreSampleIntervalDefaultsTo1000AndClampsTo500()
    {
        string defaultDir = CreateTempDir("framescope-cpu-core-default-interval-tests-");
        string clampDir = CreateTempDir("framescope-cpu-core-clamp-interval-tests-");
        try
        {
            string defaultCsv = Path.Combine(defaultDir, "cpu-core-samples.csv");
            string defaultStatus = Path.Combine(defaultDir, "cpu-core-telemetry-status.json");
            using (FrameScopeSystemSampler.CpuCoreTelemetrySession session = FrameScopeSystemSampler.CreateCpuCoreTelemetrySessionForTests(new FrameScopeSystemSampler.CpuCoreTelemetryOptions
            {
                Enabled = true,
                CsvPath = defaultCsv,
                StatusPath = defaultStatus,
                SampleIntervalMs = 0
            }, new FrameScopeSystemSampler.StaticCpuCoreTelemetryProvider(new FrameScopeSystemSampler.CpuCoreCounterSample[0], "Actual Frequency unavailable for tests")))
            {
                session.TryWriteSample(0, 0);
            }
            AssertEqual(1000, Convert.ToInt32(ReadJson(defaultStatus)["CpuCoreSampleIntervalMs"]), "default per-core interval");

            string clampCsv = Path.Combine(clampDir, "cpu-core-samples.csv");
            string clampStatus = Path.Combine(clampDir, "cpu-core-telemetry-status.json");
            using (FrameScopeSystemSampler.CpuCoreTelemetrySession session = FrameScopeSystemSampler.CreateCpuCoreTelemetrySessionForTests(new FrameScopeSystemSampler.CpuCoreTelemetryOptions
            {
                Enabled = true,
                CsvPath = clampCsv,
                StatusPath = clampStatus,
                SampleIntervalMs = 25
            }, new FrameScopeSystemSampler.StaticCpuCoreTelemetryProvider(new FrameScopeSystemSampler.CpuCoreCounterSample[0], "Actual Frequency unavailable for tests")))
            {
                session.TryWriteSample(0, 0);
            }
            AssertEqual(500, Convert.ToInt32(ReadJson(clampStatus)["CpuCoreSampleIntervalMs"]), "minimum per-core interval");
        }
        finally
        {
            TryDelete(defaultDir);
            TryDelete(clampDir);
        }
    }

    private static void CpuVoltageSampleIntervalDefaultsTo1000AndClampsTo500()
    {
        string defaultDir = CreateTempDir("framescope-cpu-voltage-default-interval-tests-");
        string clampDir = CreateTempDir("framescope-cpu-voltage-clamp-interval-tests-");
        try
        {
            string defaultCsv = Path.Combine(defaultDir, "cpu-voltage-samples.csv");
            string defaultStatus = Path.Combine(defaultDir, "cpu-core-telemetry-status.json");
            using (FrameScopeSystemSampler.CpuVoltageTelemetrySession session = FrameScopeSystemSampler.CreateCpuVoltageTelemetrySessionForTests(new FrameScopeSystemSampler.CpuVoltageTelemetryOptions
            {
                Enabled = true,
                CsvPath = defaultCsv,
                StatusPath = defaultStatus,
                SampleIntervalMs = 0
            }, new FrameScopeSystemSampler.StaticCpuVoltageTelemetryProvider(new FrameScopeSystemSampler.CpuVoltageSample[0], "No per-core voltage sensors for tests", "synthetic-voltage")))
            {
                session.TryWriteSample(0, 0);
            }
            AssertEqual(1000, Convert.ToInt32(ReadJson(defaultStatus)["CpuVoltageSampleIntervalMs"]), "default voltage interval");

            string clampCsv = Path.Combine(clampDir, "cpu-voltage-samples.csv");
            string clampStatus = Path.Combine(clampDir, "cpu-core-telemetry-status.json");
            using (FrameScopeSystemSampler.CpuVoltageTelemetrySession session = FrameScopeSystemSampler.CreateCpuVoltageTelemetrySessionForTests(new FrameScopeSystemSampler.CpuVoltageTelemetryOptions
            {
                Enabled = true,
                CsvPath = clampCsv,
                StatusPath = clampStatus,
                SampleIntervalMs = 25
            }, new FrameScopeSystemSampler.StaticCpuVoltageTelemetryProvider(new FrameScopeSystemSampler.CpuVoltageSample[0], "No per-core voltage sensors for tests", "synthetic-voltage")))
            {
                session.TryWriteSample(0, 0);
            }
            AssertEqual(500, Convert.ToInt32(ReadJson(clampStatus)["CpuVoltageSampleIntervalMs"]), "minimum voltage interval");
        }
        finally
        {
            TryDelete(defaultDir);
            TryDelete(clampDir);
        }
    }

    private static void CpuVidSampleIntervalDefaultsTo1000AndClampsTo500()
    {
        string defaultDir = CreateTempDir("framescope-cpu-vid-default-interval-tests-");
        string clampDir = CreateTempDir("framescope-cpu-vid-clamp-interval-tests-");
        try
        {
            string defaultCsv = Path.Combine(defaultDir, "cpu-vid-samples.csv");
            string defaultStatus = Path.Combine(defaultDir, "cpu-vid-telemetry-status.json");
            using (FrameScopeSystemSampler.CpuVidTelemetrySession session = FrameScopeSystemSampler.CreateCpuVidTelemetrySessionForTests(new FrameScopeSystemSampler.CpuVidTelemetryOptions
            {
                Enabled = true,
                CsvPath = defaultCsv,
                StatusPath = defaultStatus,
                SampleIntervalMs = 0
            }, new FrameScopeSystemSampler.StaticCpuVidTelemetryProvider(new FrameScopeSystemSampler.CpuVidSample[0], "No Core VID sensors for tests", "synthetic-vid")))
            {
                session.TryWriteSample(0, 0);
            }
            AssertEqual(1000, Convert.ToInt32(ReadJson(defaultStatus)["CpuVidSampleIntervalMs"]), "default vid interval");

            string clampCsv = Path.Combine(clampDir, "cpu-vid-samples.csv");
            string clampStatus = Path.Combine(clampDir, "cpu-vid-telemetry-status.json");
            using (FrameScopeSystemSampler.CpuVidTelemetrySession session = FrameScopeSystemSampler.CreateCpuVidTelemetrySessionForTests(new FrameScopeSystemSampler.CpuVidTelemetryOptions
            {
                Enabled = true,
                CsvPath = clampCsv,
                StatusPath = clampStatus,
                SampleIntervalMs = 25
            }, new FrameScopeSystemSampler.StaticCpuVidTelemetryProvider(new FrameScopeSystemSampler.CpuVidSample[0], "No Core VID sensors for tests", "synthetic-vid")))
            {
                session.TryWriteSample(0, 0);
            }
            AssertEqual(500, Convert.ToInt32(ReadJson(clampStatus)["CpuVidSampleIntervalMs"]), "minimum vid interval");
        }
        finally
        {
            TryDelete(defaultDir);
            TryDelete(clampDir);
        }
    }

    private static void UnavailableCpuVoltageTelemetryWritesStatusWithoutCsv()
    {
        string dir = CreateTempDir("framescope-cpu-voltage-unavailable-tests-");
        try
        {
            string csv = Path.Combine(dir, "cpu-voltage-samples.csv");
            string status = Path.Combine(dir, "cpu-core-telemetry-status.json");
            using (FrameScopeSystemSampler.CpuVoltageTelemetrySession session = FrameScopeSystemSampler.CreateCpuVoltageTelemetrySessionForTests(new FrameScopeSystemSampler.CpuVoltageTelemetryOptions
            {
                Enabled = true,
                CsvPath = csv,
                StatusPath = status,
                SampleIntervalMs = 1000
            }, new FrameScopeSystemSampler.StaticCpuVoltageTelemetryProvider(new FrameScopeSystemSampler.CpuVoltageSample[0], "No real per-core voltage sensors; VID/Vcore is not accepted.", "synthetic-voltage")))
            {
                session.TryWriteSample(0, 0);
            }

            AssertTrue(!File.Exists(csv), "unavailable voltage telemetry should not create data csv");
            Dictionary<string, object> map = ReadJson(status);
            AssertEqual(true, Convert.ToBoolean(map["CpuVoltageTelemetryEnabled"]), "voltage status enabled");
            AssertEqual(false, Convert.ToBoolean(map["CpuVoltageAvailable"]), "voltage availability");
            AssertEqual("provider-unavailable", Convert.ToString(map["CpuVoltageStatus"]), "voltage status");
            AssertTrue(Convert.ToString(map["CpuVoltageUnavailableReason"]).IndexOf("VID/Vcore", StringComparison.OrdinalIgnoreCase) >= 0, "voltage reason should reject fake VID/Vcore");
            AssertEqual(0, Convert.ToInt32(map["CpuVoltageSampleCount"]), "voltage sample count");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void UnavailableCpuVidTelemetryWritesChineseStatusWithoutCsv()
    {
        string dir = CreateTempDir("framescope-cpu-vid-unavailable-tests-");
        try
        {
            string csv = Path.Combine(dir, "cpu-vid-samples.csv");
            string status = Path.Combine(dir, "cpu-vid-telemetry-status.json");
            using (FrameScopeSystemSampler.CpuVidTelemetrySession session = FrameScopeSystemSampler.CreateCpuVidTelemetrySessionForTests(new FrameScopeSystemSampler.CpuVidTelemetryOptions
            {
                Enabled = true,
                CsvPath = csv,
                StatusPath = status,
                SampleIntervalMs = 1000
            }, new FrameScopeSystemSampler.StaticCpuVidTelemetryProvider(new FrameScopeSystemSampler.CpuVidSample[0], "未检测到 CPU 核心 VID 传感器；不生成假数据。", "synthetic-vid")))
            {
                session.TryWriteSample(0, 0);
            }

            AssertTrue(!File.Exists(csv), "unavailable vid telemetry should not create data csv");
            Dictionary<string, object> map = ReadJson(status);
            AssertEqual(true, Convert.ToBoolean(map["CpuVidTelemetryEnabled"]), "vid status enabled");
            AssertEqual(false, Convert.ToBoolean(map["CpuVidAvailable"]), "vid availability");
            AssertEqual("provider-unavailable", Convert.ToString(map["CpuVidStatus"]), "vid status");
            AssertTrue(Convert.ToString(map["CpuVidUnavailableReason"]).IndexOf("核心 VID", StringComparison.OrdinalIgnoreCase) >= 0, "vid reason should be Chinese and mention Core VID");
            AssertEqual(0, Convert.ToInt32(map["CpuVidSampleCount"]), "vid sample count");
            AssertEqual(0, Convert.ToInt32(map["CpuVidCoreCount"]), "vid core count");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void CpuCoreStatusCountsRowsAndMarksVoltageUnavailable()
    {
        string dir = CreateTempDir("framescope-cpu-core-status-tests-");
        try
        {
            string csv = Path.Combine(dir, "cpu-core-samples.csv");
            string status = Path.Combine(dir, "cpu-core-telemetry-status.json");
            FrameScopeSystemSampler.CpuCoreCounterSample[] samples = new[]
            {
                new FrameScopeSystemSampler.CpuCoreCounterSample
                {
                    InstanceName = "0,0",
                    ActualFrequencyMHz = 4300,
                    ProcessorFrequencyMHz = 4200,
                    ProcessorPerformancePct = 102.3,
                    PercentOfMaximumFrequency = 102.3,
                    ProcessorUtilityPct = 12.5,
                    PerformanceLimitFlags = 0
                },
                new FrameScopeSystemSampler.CpuCoreCounterSample
                {
                    InstanceName = "0,1",
                    ActualFrequencyMHz = 4625,
                    ProcessorFrequencyMHz = 4200,
                    ProcessorPerformancePct = 110.1,
                    PercentOfMaximumFrequency = 110.1,
                    ProcessorUtilityPct = 8.25,
                    PerformanceLimitFlags = 0
                }
            };
            FrameScopeSystemSampler.CpuCoreTelemetryOptions options = new FrameScopeSystemSampler.CpuCoreTelemetryOptions
            {
                Enabled = true,
                CsvPath = csv,
                StatusPath = status,
                SampleIntervalMs = 1000
            };

            using (FrameScopeSystemSampler.CpuCoreTelemetrySession session = FrameScopeSystemSampler.CreateCpuCoreTelemetrySessionForTests(options, new FrameScopeSystemSampler.StaticCpuCoreTelemetryProvider(samples, "")))
            {
                session.TryWriteSample(3, 1234);
            }

            string[] lines = File.ReadAllLines(csv, Encoding.UTF8);
            AssertEqual(3, lines.Length, "csv line count");
            AssertEqual(FrameScopeSystemSampler.CpuCoreCsvHeaderLine, lines[0], "csv header line");
            AssertTrue(lines[1].Contains(",windows-perfcounter,0,0,,,"), "first row should keep logical processor identity and empty physical fields");
            AssertTrue(lines[1].Contains(",4300,4200,102.3,102.3,12.5,0"), "first row should contain counter values");

            Dictionary<string, object> map = ReadJson(status);
            AssertEqual(true, Convert.ToBoolean(map["CpuCoreTelemetryAvailable"]), "status availability");
            AssertEqual(2, Convert.ToInt32(map["CpuCoreSampleCount"]), "status sample count");
            AssertEqual(2, Convert.ToInt32(map["CpuCoreLogicalProcessorCount"]), "logical processor count");
            AssertEqual(false, Convert.ToBoolean(map["CpuVoltageAvailable"]), "voltage availability");
            AssertEqual("unavailable", Convert.ToString(map["CpuVoltageStatus"]), "voltage status");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void SyntheticCpuVoltageTelemetryWritesVcoreCsvAndStatus()
    {
        string dir = CreateTempDir("framescope-cpu-voltage-status-tests-");
        try
        {
            string csv = Path.Combine(dir, "cpu-voltage-samples.csv");
            string status = Path.Combine(dir, "cpu-core-telemetry-status.json");
            FrameScopeSystemSampler.CpuVoltageSample[] samples = new[]
            {
                new FrameScopeSystemSampler.CpuVoltageSample
                {
                    VoltageV = 1.064,
                    Source = "synthetic-sensor",
                    ProviderKind = "synthetic",
                    SensorName = "CPU VCore",
                    SensorIdentifier = "/mainboard/superio/voltage/0"
                }
            };

            using (FrameScopeSystemSampler.CpuVoltageTelemetrySession session = FrameScopeSystemSampler.CreateCpuVoltageTelemetrySessionForTests(new FrameScopeSystemSampler.CpuVoltageTelemetryOptions
            {
                Enabled = true,
                CsvPath = csv,
                StatusPath = status,
                SampleIntervalMs = 1000
            }, new FrameScopeSystemSampler.StaticCpuVoltageTelemetryProvider(samples, "", "synthetic-sensor")))
            {
                session.TryWriteSample(7, 1234);
            }

            string[] lines = File.ReadAllLines(csv, Encoding.UTF8);
            AssertEqual(2, lines.Length, "voltage csv line count");
            AssertEqual(FrameScopeSystemSampler.CpuVoltageCsvHeaderLine, lines[0], "voltage csv header line");
            AssertTrue(lines[1].Contains(",synthetic-sensor,synthetic,CPU VCore,,,,,,1.064,vcore,,"), "voltage row should contain aggregate CPU Vcore without inventing a core id");

            Dictionary<string, object> map = ReadJson(status);
            AssertEqual(true, Convert.ToBoolean(map["CpuVoltageAvailable"]), "voltage status availability");
            AssertEqual(true, Convert.ToBoolean(map["CpuVoltageVcoreAvailable"]), "voltage vcore availability");
            AssertEqual(false, Convert.ToBoolean(map["CpuVoltagePerCoreAvailable"]), "voltage per-core availability");
            AssertEqual(false, Convert.ToBoolean(map["CpuVoltageNonPerCoreAvailable"]), "voltage non-per-core availability");
            AssertEqual("vcore-available", Convert.ToString(map["CpuVoltageStatus"]), "voltage status available");
            AssertEqual("synthetic-sensor", Convert.ToString(map["CpuVoltageSource"]), "voltage source label");
            AssertEqual("synthetic", Convert.ToString(map["CpuVoltageProviderKind"]), "voltage provider kind");
            AssertEqual(1, Convert.ToInt32(map["CpuVoltageSampleCount"]), "voltage sample count");
            AssertEqual(1, Convert.ToInt32(map["CpuVoltageVcoreSampleCount"]), "voltage vcore sample count");
            AssertEqual(0, Convert.ToInt32(map["CpuVoltagePerCoreSampleCount"]), "voltage per-core sample count");
            AssertEqual(0, Convert.ToInt32(map["CpuVoltageNonPerCoreSampleCount"]), "voltage non-per-core sample count");
            AssertEqual(0, Convert.ToInt32(map["CpuVoltageRejectedSampleCount"]), "voltage rejected sample count");
            AssertEqual(0, Convert.ToInt32(map["CpuVoltageLogicalProcessorCount"]), "voltage logical processor count");
            AssertEqual(1000, Convert.ToInt32(map["CpuVoltageSampleIntervalMs"]), "voltage interval");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void NonVcoreCpuVoltageTelemetryDoesNotCreateCpuVoltageData()
    {
        string dir = CreateTempDir("framescope-cpu-voltage-non-per-core-tests-");
        try
        {
            string csv = Path.Combine(dir, "cpu-voltage-samples.csv");
            string status = Path.Combine(dir, "cpu-voltage-telemetry-status.json");
            FrameScopeSystemSampler.CpuVoltageSample[] samples = new[]
            {
                new FrameScopeSystemSampler.CpuVoltageSample
                {
                    VoltageV = 1.020,
                    Source = "synthetic-sensor",
                    ProviderKind = "synthetic",
                    SensorName = "Vcore SoC",
                    SensorIdentifier = "/mainboard/superio/voltage/0",
                    Status = "non-per-core"
                },
                new FrameScopeSystemSampler.CpuVoltageSample
                {
                    VoltageV = 1.180,
                    Source = "synthetic-sensor",
                    ProviderKind = "synthetic",
                    SensorName = "CPU Package",
                    SensorIdentifier = "/mainboard/superio/voltage/1"
                },
                new FrameScopeSystemSampler.CpuVoltageSample
                {
                    VoltageV = 3.280,
                    Source = "synthetic-sensor",
                    ProviderKind = "synthetic",
                    SensorName = "VBAT",
                    SensorIdentifier = "/mainboard/superio/voltage/2"
                },
                new FrameScopeSystemSampler.CpuVoltageSample
                {
                    VoltageV = 1.500,
                    Source = "synthetic-sensor",
                    ProviderKind = "synthetic",
                    SensorName = "VIN1",
                    SensorIdentifier = "/mainboard/superio/voltage/3"
                },
                new FrameScopeSystemSampler.CpuVoltageSample
                {
                    VoltageV = 1.100,
                    Source = "synthetic-sensor",
                    ProviderKind = "synthetic",
                    SensorName = "Core #1 Vcore",
                    SensorIdentifier = "/cpu/0/voltage/4"
                }
            };

            using (FrameScopeSystemSampler.CpuVoltageTelemetrySession session = FrameScopeSystemSampler.CreateCpuVoltageTelemetrySessionForTests(new FrameScopeSystemSampler.CpuVoltageTelemetryOptions
            {
                Enabled = true,
                CsvPath = csv,
                StatusPath = status,
                SampleIntervalMs = 1000
            }, new FrameScopeSystemSampler.StaticCpuVoltageTelemetryProvider(samples, "", "synthetic-sensor", "synthetic")))
            {
                session.TryWriteSample(3, 1000);
            }

            AssertTrue(!File.Exists(csv), "non-Vcore voltage sensors should not be written to cpu-voltage-samples.csv");

            Dictionary<string, object> map = ReadJson(status);
            AssertEqual(false, Convert.ToBoolean(map["CpuVoltageAvailable"]), "non-Vcore voltage should not make CPU Voltage available");
            AssertEqual(false, Convert.ToBoolean(map["CpuVoltageVcoreAvailable"]), "non-Vcore voltage should not make Vcore available");
            AssertEqual(false, Convert.ToBoolean(map["CpuVoltagePerCoreAvailable"]), "non-Vcore voltage should not be per-core available");
            AssertEqual(true, Convert.ToBoolean(map["CpuVoltageNonPerCoreAvailable"]), "rejected voltage sensors should be recorded as non-per-core-only evidence");
            AssertEqual("non-per-core-only", Convert.ToString(map["CpuVoltageStatus"]), "non-Vcore voltage status");
            AssertEqual(0, Convert.ToInt32(map["CpuVoltageSampleCount"]), "non-Vcore accepted sample count");
            AssertEqual(0, Convert.ToInt32(map["CpuVoltageVcoreSampleCount"]), "non-Vcore vcore sample count");
            AssertEqual(0, Convert.ToInt32(map["CpuVoltagePerCoreSampleCount"]), "non-Vcore per-core sample count");
            AssertEqual(5, Convert.ToInt32(map["CpuVoltageNonPerCoreSampleCount"]), "non-Vcore rejected sample count via compatibility field");
            AssertEqual(5, Convert.ToInt32(map["CpuVoltageRejectedSampleCount"]), "non-Vcore rejected sample count");
            AssertTrue(Convert.ToString(map["CpuVoltageUnavailableReason"]).IndexOf("CPU Vcore", StringComparison.OrdinalIgnoreCase) >= 0, "non-Vcore unavailable reason should mention CPU Vcore requirement");
            AssertTrue(Convert.ToString(map["CpuVoltageUnavailableReason"]).IndexOf("VID/SOC/Package/VBAT/VIN", StringComparison.OrdinalIgnoreCase) >= 0, "non-Vcore unavailable reason should list rejected sensor families");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void SyntheticCpuVidTelemetryWritesDedicatedCsvAndStatus()
    {
        string dir = CreateTempDir("framescope-cpu-vid-status-tests-");
        try
        {
            string csv = Path.Combine(dir, "cpu-vid-samples.csv");
            string status = Path.Combine(dir, "cpu-vid-telemetry-status.json");
            FrameScopeSystemSampler.CpuVidSample[] samples = new[]
            {
                new FrameScopeSystemSampler.CpuVidSample
                {
                    ProcessorGroup = "0",
                    LogicalProcessor = "0",
                    CoreIndex = "0",
                    PhysicalCoreId = "0",
                    ThreadIndex = "",
                    VidV = 1.112,
                    Source = "builtin-librehardwaremonitor",
                    ProviderKind = "built-in",
                    SensorName = "Core #1 VID",
                    SensorIdentifier = "/amdcpu/0/voltage/0"
                },
                new FrameScopeSystemSampler.CpuVidSample
                {
                    ProcessorGroup = "0",
                    LogicalProcessor = "1",
                    CoreIndex = "1",
                    PhysicalCoreId = "1",
                    ThreadIndex = "",
                    VidV = 1.087,
                    Source = "builtin-librehardwaremonitor",
                    ProviderKind = "built-in",
                    SensorName = "Core #2 VID",
                    SensorIdentifier = "/amdcpu/0/voltage/1"
                }
            };

            using (FrameScopeSystemSampler.CpuVidTelemetrySession session = FrameScopeSystemSampler.CreateCpuVidTelemetrySessionForTests(new FrameScopeSystemSampler.CpuVidTelemetryOptions
            {
                Enabled = true,
                CsvPath = csv,
                StatusPath = status,
                SampleIntervalMs = 1000,
                Provider = "built-in"
            }, new FrameScopeSystemSampler.StaticCpuVidTelemetryProvider(samples, "", "builtin-librehardwaremonitor", "built-in")))
            {
                session.TryWriteSample(7, 1234);
            }

            string[] lines = File.ReadAllLines(csv, Encoding.UTF8);
            AssertEqual(3, lines.Length, "vid csv line count");
            AssertEqual(FrameScopeSystemSampler.CpuVidCsvHeaderLine, lines[0], "vid csv header line");
            AssertTrue(lines[1].Contains(",builtin-librehardwaremonitor,built-in,Core #1 VID,0,0,0,0,,1.112,core-vid,"), "first vid row should contain dedicated core VID");

            Dictionary<string, object> map = ReadJson(status);
            AssertEqual(true, Convert.ToBoolean(map["CpuVidAvailable"]), "vid status availability");
            AssertEqual("core-vid-available", Convert.ToString(map["CpuVidStatus"]), "vid status available");
            AssertEqual("builtin-librehardwaremonitor", Convert.ToString(map["CpuVidSource"]), "vid source label");
            AssertEqual("built-in", Convert.ToString(map["CpuVidProviderKind"]), "vid provider kind");
            AssertEqual(2, Convert.ToInt32(map["CpuVidSampleCount"]), "vid sample count");
            AssertEqual(2, Convert.ToInt32(map["CpuVidCoreCount"]), "vid core count");
            AssertTrue(Convert.ToString(map["CpuVidNote"]).IndexOf("请求", StringComparison.OrdinalIgnoreCase) >= 0, "vid status note should say VID is requested voltage");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void CpuVidCoreIndexParserKeepsZeroBasedAndOneBasedNamesDistinct()
    {
        HashSet<int> zeroBased = new HashSet<int>();
        HashSet<int> oneBased = new HashSet<int>();

        for (int core = 0; core < 8; core++)
        {
            string text = "Core " + core.ToString() + " VID /amdcpu/0/voltage/" + core.ToString() + " Cpu";
            int? parsed = FrameScopeSystemSampler.ExtractCpuVidCoreIndexForTests(text);
            AssertEqual(true, parsed.HasValue, "zero-based Core " + core.ToString() + " VID should parse");
            AssertEqual(core, parsed.Value, "zero-based Core " + core.ToString() + " VID should keep its number");
            AssertTrue(FrameScopeSystemSampler.IsCpuCoreVidSensorForTests(text, parsed), "zero-based Core " + core.ToString() + " VID should be accepted");
            zeroBased.Add(parsed.Value);

            string oneBasedText = "Core #" + (core + 1).ToString() + " VID /amdcpu/0/voltage/" + core.ToString() + " Cpu";
            int? oneBasedParsed = FrameScopeSystemSampler.ExtractCpuVidCoreIndexForTests(oneBasedText);
            AssertEqual(true, oneBasedParsed.HasValue, "one-based Core #" + (core + 1).ToString() + " VID should parse");
            AssertEqual(core, oneBasedParsed.Value, "one-based Core #" + (core + 1).ToString() + " VID should map to zero-based core");
            AssertTrue(FrameScopeSystemSampler.IsCpuCoreVidSensorForTests(oneBasedText, oneBasedParsed), "one-based Core #" + (core + 1).ToString() + " VID should be accepted");
            oneBased.Add(oneBasedParsed.Value);
        }

        AssertEqual(8, zeroBased.Count, "Core 0 VID through Core 7 VID should remain eight distinct cores");
        AssertEqual(8, oneBased.Count, "Core #1 VID through Core #8 VID should remain eight distinct cores");

        string gpuText = "GPU Core 0 VID /gpu/0/voltage/0";
        AssertTrue(!FrameScopeSystemSampler.IsCpuCoreVidSensorForTests(gpuText, FrameScopeSystemSampler.ExtractCpuVidCoreIndexForTests(gpuText)), "non-CPU GPU VID should be rejected");

        string socText = "CPU SOC VID /amdcpu/0/voltage/9 Cpu";
        AssertTrue(!FrameScopeSystemSampler.IsCpuCoreVidSensorForTests(socText, FrameScopeSystemSampler.ExtractCpuVidCoreIndexForTests(socText)), "SOC VID should be rejected");

        string aggregateText = "CPU Core VID /amdcpu/0/voltage/10 Cpu";
        AssertTrue(!FrameScopeSystemSampler.IsCpuCoreVidSensorForTests(aggregateText, FrameScopeSystemSampler.ExtractCpuVidCoreIndexForTests(aggregateText)), "aggregate Core VID without a core number should be rejected");
    }

    private static void CpuVoltageTelemetryDoesNotRecordVidAsVoltage()
    {
        string dir = CreateTempDir("framescope-cpu-voltage-reject-vid-tests-");
        try
        {
            string csv = Path.Combine(dir, "cpu-voltage-samples.csv");
            string status = Path.Combine(dir, "cpu-voltage-telemetry-status.json");
            FrameScopeSystemSampler.CpuVoltageSample[] samples = new[]
            {
                new FrameScopeSystemSampler.CpuVoltageSample
                {
                    ProcessorGroup = "0",
                    LogicalProcessor = "0",
                    CoreId = "0",
                    VoltageV = 1.104,
                    Source = "builtin-librehardwaremonitor",
                    ProviderKind = "built-in",
                    SensorName = "Core #1 VID",
                    SensorIdentifier = "/amdcpu/0/voltage/0",
                    Status = "core-vid",
                    Reason = "VID is request voltage, not real Vcore."
                }
            };

            using (FrameScopeSystemSampler.CpuVoltageTelemetrySession session = FrameScopeSystemSampler.CreateCpuVoltageTelemetrySessionForTests(new FrameScopeSystemSampler.CpuVoltageTelemetryOptions
            {
                Enabled = true,
                CsvPath = csv,
                StatusPath = status,
                SampleIntervalMs = 1000
            }, new FrameScopeSystemSampler.StaticCpuVoltageTelemetryProvider(samples, "", "builtin-librehardwaremonitor", "built-in")))
            {
                session.TryWriteSample(3, 1000);
            }

            AssertTrue(!File.Exists(csv), "VID samples should not be written to cpu-voltage-samples.csv");
            Dictionary<string, object> map = ReadJson(status);
            AssertEqual(false, Convert.ToBoolean(map["CpuVoltageAvailable"]), "vid should not make real voltage available");
            AssertEqual(0, Convert.ToInt32(map["CpuVoltageSampleCount"]), "vid should not count as voltage sample");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void BuiltInCpuVoltageProviderMissingDependencyIsUnavailable()
    {
        string dir = CreateTempDir("framescope-cpu-voltage-missing-built-in-tests-");
        try
        {
            FrameScopeSystemSampler.ICpuVoltageTelemetryProvider provider = FrameScopeSystemSampler.CreateBuiltInCpuVoltageProviderForTests(dir);
            AssertEqual(false, provider.Available, "missing built-in dependency should be unavailable");
            AssertEqual("built-in", provider.ProviderKind, "missing built-in dependency provider kind");
            AssertEqual("builtin-librehardwaremonitor", provider.SourceLabel, "missing built-in dependency source");
            AssertTrue(provider.UnavailableReason.IndexOf("LibreHardwareMonitorLib.dll", StringComparison.OrdinalIgnoreCase) >= 0, "missing dependency reason");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void AutoCpuVoltageAndVidProvidersShareBuiltInHardwareProvider()
    {
        AssertTrue(FrameScopeSystemSampler.ShouldShareBuiltInCpuHardwareProviderForTests("auto", "auto"), "auto voltage and auto VID should share built-in hardware provider");
        AssertTrue(FrameScopeSystemSampler.ShouldShareBuiltInCpuHardwareProviderForTests("built-in", "auto"), "explicit built-in voltage and auto VID should share built-in hardware provider");
        AssertTrue(FrameScopeSystemSampler.ShouldShareBuiltInCpuHardwareProviderForTests("auto", "built-in"), "auto voltage and explicit built-in VID should share built-in hardware provider");
        AssertTrue(!FrameScopeSystemSampler.ShouldShareBuiltInCpuHardwareProviderForTests("wmi", "auto"), "WMI voltage should not share the built-in VID provider");
        AssertTrue(!FrameScopeSystemSampler.ShouldShareBuiltInCpuHardwareProviderForTests("auto", "disabled"), "disabled VID should not share a provider");
        AssertTrue(!FrameScopeSystemSampler.ShouldShareBuiltInCpuHardwareProviderForTests("synthetic", "auto"), "synthetic voltage should not share the built-in provider");
    }

    private static Dictionary<string, object> ReadJson(string path)
    {
        AssertTrue(File.Exists(path), "json file exists: " + path);
        return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
            .Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
    }

    private static string CreateTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); }
        catch { }
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
