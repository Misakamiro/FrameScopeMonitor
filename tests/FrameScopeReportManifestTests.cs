using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

public static class FrameScopeReportManifestTests
{
    public static int Main()
    {
        FailedPublicationAfterBackupRestoresPreviousReport();
        InterruptedBackupIsRecovered();
        SuccessfulPublicationCleansStagingDirectories();
        ManifestJsonSurvivesPowerShellDefaultRead();
        AccessDeniedCaptureDiagnosticsFlowIntoManifestAndReportData();
        SilentNoCsvDiagnosticsStayTargetNeutralAndDiagnostic();
        FramesWithFailedSystemSamplerArePartial();
        FramesWithHealthySamplersAreFull();
        ProcessOnlyReportUsesValidProcessTimeRange();
        TargetOnlyProcessRowCountsSamplingInstant();
        RowsSharingSampleIndexCountOneSamplingInstant();
        LegacyMissingOrEmptySampleIndexUsesTimeInstants();
        NoUsableRowsAreError();
        ReportTargetDisplayNamePrefersConfiguredNameAndKeepsProcessName();
        ReportChartDataUsesBucketedFpsDisplayAndRawFrameStats();
        PresentMonPrimaryHardwareTrackSelectionKeepsRawDiagnostics();
        ProcessChartDataUsesLosslessRleCodec();
        CpuCoreTelemetryFlowsIntoChartDataWithoutFakeVoltage();
        CpuVoltageTelemetryFlowsFromDedicatedCsvIntoManifestAndChartData();
        CpuVoltageNonPerCoreTelemetryDoesNotCreateChartSeries();
        CpuVoltageSidecarOverridesStaleRunStatus();
        CpuVidTelemetryFlowsFromDedicatedCsvIntoManifestAndChartData();
        InvalidVoltageFrequencyAndVidSamplesBecomeNullGaps();
        AmdLibreHardwareMonitorCoreVidSamplesAreRejectedAsUnreliable();
        CpuVidZeroBasedAndOneBasedNamesRemainIndependentWhenValuesMatch();
        CpuVidOnlyDoesNotMakeCpuVoltagePerCoreAvailable();
        CpuPackageSocAndAggregateVcoreDoNotEnterCpuVidChart();
        NoCpuVidSensorUsesChineseReasonAndNoFakeData();
        Console.WriteLine("FrameScopeReportManifestTests: PASS");
        return 0;
    }

    private static void FailedPublicationAfterBackupRestoresPreviousReport()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-publish-rollback-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string charts = Path.Combine(dir, "charts");
            WritePublisherArtifactSet(dir, charts, "old-marker");
            bool failed = false;
            try
            {
                FrameScopeReportPublisher.PublishForTests(
                    dir,
                    delegate(string output) { WritePublisherArtifactSet(dir, output, "new-marker"); },
                    delegate(string source, string destination)
                    {
                        string name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar));
                        if (name.StartsWith(".framescope-report-", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith(".framescope-report-backup-", StringComparison.OrdinalIgnoreCase) &&
                            Path.GetFileName(destination).Equals("charts", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException("synthetic publish move failure");
                        }
                        Directory.Move(source, destination);
                    });
            }
            catch (IOException)
            {
                failed = true;
            }

            AssertTrue(failed, "publish failure was injected after backup");
            AssertTrue(File.ReadAllText(Path.Combine(charts, FrameScopeReportArtifacts.ReportFileName)).Contains("old-marker"), "previous report restored");
            AssertTrue(FrameScopeReportArtifacts.Inspect(dir).IsComplete, "restored report remains complete");
            AssertNoPublisherDirectories(dir, "rollback cleanup");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void InterruptedBackupIsRecovered()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-publish-recovery-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string charts = Path.Combine(dir, "charts");
            string backup = Path.Combine(dir, ".framescope-report-backup-interrupted");
            WritePublisherArtifactSet(dir, charts, "backup-marker");
            Directory.Move(charts, backup);

            FrameScopeReportPublisher.RecoverInterruptedPublication(dir);

            AssertTrue(FrameScopeReportArtifacts.Inspect(dir).IsComplete, "backup restored after interrupted publication");
            AssertTrue(File.ReadAllText(Path.Combine(charts, FrameScopeReportArtifacts.ReportFileName)).Contains("backup-marker"), "restored backup content");
            AssertNoPublisherDirectories(dir, "interrupted backup cleanup");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void SuccessfulPublicationCleansStagingDirectories()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-publish-success-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            FrameScopeReportPublisher.Publish(dir, delegate(string output)
            {
                WritePublisherArtifactSet(dir, output, "published-marker");
            });

            AssertTrue(FrameScopeReportArtifacts.Inspect(dir).IsComplete, "published artifact set complete");
            AssertNoPublisherDirectories(dir, "successful publish cleanup");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void WritePublisherArtifactSet(string runDir, string outputDirectory, string marker)
    {
        Directory.CreateDirectory(outputDirectory);
        string finalCharts = Path.Combine(runDir, "charts");
        string finalReport = Path.Combine(finalCharts, FrameScopeReportArtifacts.ReportFileName);
        string finalData = Path.Combine(finalCharts, FrameScopeReportArtifacts.DataFileName);
        File.WriteAllText(Path.Combine(outputDirectory, FrameScopeReportArtifacts.ReportFileName), "<html>" + marker + "</html>");
        File.WriteAllText(Path.Combine(outputDirectory, FrameScopeReportArtifacts.DataFileName), "window.FRAMESCOPE_DATA = {};");
        File.WriteAllText(Path.Combine(outputDirectory, FrameScopeReportArtifacts.ManifestFileName),
            new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                { "report", finalReport },
                { "data", finalData },
                { "frames", 1 }
            }));
    }

    private static void AssertNoPublisherDirectories(string runDir, string label)
    {
        AssertEqual(0, Directory.GetDirectories(runDir, ".framescope-report-*", SearchOption.TopDirectoryOnly).Length, label);
    }

    private static void ProcessOnlyReportUsesValidProcessTimeRange()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-process-only-timeline-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Local);
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "game.exe" },
                    { "ProcessSamplerStatus", "healthy" }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + "not-a-time,bad,0,ignored-helper,1,99,999,0,0,,1\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,game,1,3.0,256,0,0,,42\r\n"
                + start.AddMilliseconds(200).ToString("o", CultureInfo.InvariantCulture) + ",1,200,helper,1,1.0,128,0,0,,7\r\n"
                + start.AddSeconds(2).ToString("o", CultureInfo.InvariantCulture) + ",2,2000,helper,1,2.0,130,0,0,,7\r\n"
                + start.AddSeconds(5).ToString("o", CultureInfo.InvariantCulture) + ",3,5000,game,1,4.0,260,0,0,,42\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> run = GetMap(data, "run");
            System.Collections.ArrayList times = GetObjectList(GetMap(data, "process"), "t");

            AssertEqual(start.ToString("yyyy-MM-dd HH:mm:ss"), Convert.ToString(run["startLabel"]), "process-only report start");
            AssertEqual(start.AddSeconds(5).ToString("yyyy-MM-dd HH:mm:ss"), Convert.ToString(run["endLabel"]), "process-only report end");
            AssertEqual("0\u52065\u79d2", Convert.ToString(run["durationLabel"]), "process-only report duration");
            AssertEqual(2, times.Count, "process-only display timeline point count");
            AssertEqual(0.2, Convert.ToDouble(times[0], CultureInfo.InvariantCulture), "process-only timeline starts near zero");
            AssertEqual(2.0, Convert.ToDouble(times[1], CultureInfo.InvariantCulture), "process-only timeline preserves later point");
            AssertTrue(Convert.ToDouble(times[0], CultureInfo.InvariantCulture) <= Convert.ToDouble(times[1], CultureInfo.InvariantCulture), "process-only timeline is monotonic");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void ManifestJsonSurvivesPowerShellDefaultRead()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-manifest-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "framescope-interactive-manifest.json");
        try
        {
            Dictionary<string, object> manifest = new Dictionary<string, object>
            {
                { "hasFrameData", true },
                { "reportKind", "full" },
                { "frames", 240 },
                { "processSamples", 59 },
                { "systemSamples", 2 },
                { "frameCaptureStatus", "captured" },
                { "frameCaptureMessage", "PresentMon 已成功写入帧数据。" }
            };

            string json = FrameScopeReportGenerator.SerializeArtifactJson(manifest);
            AssertAsciiOnly(json, "manifest json should be ASCII-safe for default PowerShell reads");
            File.WriteAllText(path, json, new UTF8Encoding(false));

            string defaultDecoded = File.ReadAllText(path, Encoding.Default);
            Dictionary<string, object> parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(defaultDecoded);

            AssertEqual(true, Convert.ToBoolean(parsed["hasFrameData"]), "has frame data");
            AssertEqual("full", Convert.ToString(parsed["reportKind"]), "report kind");
            AssertEqual(240, Convert.ToInt32(parsed["frames"]), "frame count");
            AssertEqual(59, Convert.ToInt32(parsed["processSamples"]), "process sample count");
            AssertEqual(2, Convert.ToInt32(parsed["systemSamples"]), "system sample count");
            AssertEqual("PresentMon 已成功写入帧数据。", Convert.ToString(parsed["frameCaptureMessage"]), "frame capture message");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void AccessDeniedCaptureDiagnosticsFlowIntoManifestAndReportData()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-access-denied-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string statusMessage = "PresentMon 无法启动 ETW trace，需要管理员/Performance Log Users/系统 ETW 权限检查。";
            string stderr = "error: failed to start trace session: access denied.\r\n"
                + "       PresentMon requires either administrative privileges or to be run by a user in the\r\n"
                + "       \"Performance Log Users\" user group.  View the readme for more details.";
            File.WriteAllText(Path.Combine(dir, "presentmon.stderr.log"), stderr, Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "bf6.exe" },
                    { "FrameCaptureStatus", "presentmon-etw-access-denied" },
                    { "FrameCaptureMessage", statusMessage },
                    { "PresentMonCsvExists", false },
                    { "PresentMonCsvBytes", 0 },
                    { "PresentMonCsvRows", 0 },
                    { "PresentMonExitCode", 6 },
                    { "PresentMonStderrTail", stderr },
                    { "PresentMonPreflightIsElevated", false },
                    { "PresentMonPreflightInPerformanceLogUsers", false },
                    { "PresentMonPreflightToolExists", true }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "summary.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "TargetProcess", "bf6.exe" },
                    { "FrameCaptureStatus", "presentmon-etw-access-denied" },
                    { "FrameCaptureMessage", statusMessage },
                    { "PresentMonExitCode", 6 },
                    { "PresentMonPreflightIsElevated", false },
                    { "PresentMonPreflightInPerformanceLogUsers", false },
                    { "PresentMonPreflightToolExists", true }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + "2026-05-24T00:00:00+08:00,0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + "2026-05-24T00:00:00+08:00,0,0,bf6,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            string manifestPath = Path.Combine(dir, "charts", "framescope-interactive-manifest.json");
            string dataPath = Path.Combine(dir, "charts", "framescope-interactive-data.js");
            string reportPath = Path.Combine(dir, "charts", "framescope-interactive-report.html");
            AssertTrue(File.Exists(manifestPath), "manifest exists");
            AssertTrue(File.Exists(dataPath), "report data exists");
            AssertTrue(File.Exists(reportPath), "report html exists");

            Dictionary<string, object> manifest = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(manifestPath, Encoding.UTF8));
            AssertEqual("diagnostic", Convert.ToString(manifest["reportKind"]), "diagnostic report kind");
            AssertEqual("presentmon-etw-access-denied", Convert.ToString(manifest["frameCaptureStatus"]), "manifest capture status");
            AssertEqual(statusMessage, Convert.ToString(manifest["frameCaptureMessage"]), "manifest capture message");
            AssertEqual("presentmon-etw-access-denied", Convert.ToString(manifest["presentMonFailureCategory"]), "manifest failure category");
            AssertEqual(true, Convert.ToBoolean(manifest["presentMonEtwAccessDenied"]), "manifest etw access denied flag");
            AssertEqual(false, Convert.ToBoolean(manifest["presentMonPreflightIsElevated"]), "manifest elevated preflight");
            AssertEqual(false, Convert.ToBoolean(manifest["presentMonPreflightInPerformanceLogUsers"]), "manifest PLU preflight");
            AssertEqual(true, Convert.ToBoolean(manifest["presentMonPreflightToolExists"]), "manifest tool preflight");

            string data = File.ReadAllText(dataPath, Encoding.UTF8);
            AssertTrue(data.Contains("presentmon-etw-access-denied"), "report data contains access denied status");
            AssertTrue(data.Contains("PresentMon 无法启动 ETW trace"), "report data contains ETW message");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void SilentNoCsvDiagnosticsStayTargetNeutralAndDiagnostic()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-silent-no-csv-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 30, 12, 31, 51, DateTimeKind.Local);
            File.WriteAllText(Path.Combine(dir, "presentmon.stdout.log"), "Started recording.\r\n", Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "presentmon.stderr.log"), "", Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "VALORANT-Win64-Shipping.exe" },
                    { "TargetResolvedProcess", "VALORANT-Win64-Shipping.exe" },
                    { "TargetPid", 27492 },
                    { "InitialTargetPid", 27492 },
                    { "PresentMonCaptureMode", "process_id" },
                    { "PresentMonCaptureTarget", "27492" },
                    { "PresentMonArgs", "--process_id 27492 --output_file " + Path.Combine(dir, "presentmon.csv") + " --date_time --terminate_on_proc_exit --no_console_stats --stop_existing_session --session_name FrameScopeNativePresentMon_Valorant" },
                    { "FrameCaptureStatus", "no-presentmon-csv" },
                    { "FrameCaptureMessage", "PresentMon 已启动，但没有创建 presentmon.csv。PUBG 场景下通常是渲染进程切换。" },
                    { "PresentMonFailureCategory", "missing-presentmon-csv" },
                    { "PresentMonEtwAccessDenied", false },
                    { "PresentMonCsvExists", false },
                    { "PresentMonCsvBytes", 0 },
                    { "PresentMonCsvRows", 0 },
                    { "PresentMonExitCode", 0 },
                    { "PresentMonStdoutTail", "Started recording." },
                    { "PresentMonStderrTail", "" }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,VALORANT-Win64-Shipping,1,1.0,512,0,0,,27492\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            string manifestPath = Path.Combine(dir, "charts", "framescope-interactive-manifest.json");
            string dataPath = Path.Combine(dir, "charts", "framescope-interactive-data.js");
            string reportPath = Path.Combine(dir, "charts", "framescope-interactive-report.html");
            Dictionary<string, object> manifest = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(manifestPath, Encoding.UTF8));
            string dataText = File.ReadAllText(dataPath, Encoding.UTF8);
            string reportText = File.ReadAllText(reportPath, Encoding.UTF8);

            AssertEqual("diagnostic", Convert.ToString(manifest["reportKind"]), "silent no-csv report kind");
            AssertEqual(0, Convert.ToInt32(manifest["frames"]), "silent no-csv frame count");
            AssertEqual("presentmon-no-csv-silent", Convert.ToString(manifest["frameCaptureStatus"]), "silent no-csv manifest status");
            AssertEqual("presentmon-no-csv-silent", Convert.ToString(manifest["presentMonFailureCategory"]), "silent no-csv manifest category");
            AssertEqual(false, Convert.ToBoolean(manifest["presentMonEtwAccessDenied"]), "silent no-csv ETW flag");
            AssertTrue(Convert.ToString(manifest["frameCaptureMessage"]).IndexOf("PUBG", StringComparison.OrdinalIgnoreCase) < 0, "manifest message must not mention PUBG");
            AssertTrue(dataText.IndexOf("PUBG", StringComparison.OrdinalIgnoreCase) < 0, "data payload must not mention PUBG");
            AssertTrue(reportText.IndexOf("PUBG", StringComparison.OrdinalIgnoreCase) < 0, "report shell must not mention PUBG for Valorant");
            AssertTrue(dataText.IndexOf("VALORANT-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase) >= 0, "data payload should keep target process");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void FramesWithFailedSystemSamplerArePartial()
    {
        string dir = CreateSamplerClassificationFixture(true, false);
        try
        {
            FrameScopeReportGenerator.GenerateForTests(dir);
            Dictionary<string, object> manifest = ReadManifest(dir);

            AssertEqual(true, Convert.ToBoolean(manifest["hasFrameData"]), "partial fixture has frame data");
            AssertTrue(Convert.ToInt32(manifest["frames"]) > 0, "partial fixture valid frames");
            AssertEqual(1, Convert.ToInt32(manifest["processSamples"]), "partial fixture actual process rows");
            AssertEqual(0, Convert.ToInt32(manifest["systemSamples"]), "partial fixture actual system rows override optimistic status");
            AssertEqual("partial", Convert.ToString(manifest["reportKind"]), "failed system sampler report kind");
            AssertEqual("healthy", Convert.ToString(manifest["processSamplerStatus"]), "partial fixture process sampler status");
            AssertEqual("failed", Convert.ToString(manifest["systemSamplerStatus"]), "partial fixture system sampler status");
            AssertEqual(0, Convert.ToInt32(manifest["systemSamplerValidRows"]), "manifest system rows come from actual CSV");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void FramesWithHealthySamplersAreFull()
    {
        string dir = CreateSamplerClassificationFixture(true, true);
        try
        {
            FrameScopeReportGenerator.GenerateForTests(dir);
            Dictionary<string, object> manifest = ReadManifest(dir);

            AssertEqual("full", Convert.ToString(manifest["reportKind"]), "healthy sampler report kind");
            AssertEqual("healthy", Convert.ToString(manifest["processSamplerStatus"]), "healthy process sampler status");
            AssertEqual("healthy", Convert.ToString(manifest["systemSamplerStatus"]), "healthy system sampler status");
            AssertEqual(1, Convert.ToInt32(manifest["processSamplerValidRows"]), "healthy process sampler valid rows");
            AssertEqual(1, Convert.ToInt32(manifest["systemSamplerValidRows"]), "healthy system sampler valid rows");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void TargetOnlyProcessRowCountsSamplingInstant()
    {
        string dir = CreateSamplerClassificationFixture(true, true);
        try
        {
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + "2026-07-11T10:00:00.0000000Z,7,0,game,1,3.0,256,0,0,,4242\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);
            Dictionary<string, object> manifest = ReadManifest(dir);
            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> counts = GetMap(data, "counts");

            AssertEqual(1, Convert.ToInt32(manifest["processSamples"]), "target-only row counts as a sampling instant in manifest");
            AssertEqual(1, Convert.ToInt32(counts["processSamples"]), "target-only row counts as a sampling instant in data");
            AssertEqual(1, Convert.ToInt32(manifest["processSamplerValidRows"]), "target-only row remains one physical sampler row");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void RowsSharingSampleIndexCountOneSamplingInstant()
    {
        string dir = CreateSamplerClassificationFixture(true, true);
        try
        {
            File.AppendAllText(Path.Combine(dir, "process-samples.csv"),
                "2026-07-11T10:00:00.0000000Z,0,0,second-helper,1,2.0,64,0,0,,88\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);
            Dictionary<string, object> manifest = ReadManifest(dir);

            AssertEqual(1, Convert.ToInt32(manifest["processSamples"]), "shared SampleIndex remains one sampling instant");
            AssertEqual(2, Convert.ToInt32(manifest["processSamplerValidRows"]), "sampler valid rows retain physical process groups");
            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            AssertEqual(1, Convert.ToInt32(GetMap(data, "counts")["processSamples"]), "shared SampleIndex remains one sampling instant in data");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void LegacyMissingOrEmptySampleIndexUsesTimeInstants()
    {
        AssertLegacyProcessSampleTimeFallback(false);
        AssertLegacyProcessSampleTimeFallback(true);
    }

    private static void AssertLegacyProcessSampleTimeFallback(bool includeSampleIndex)
    {
        string dir = CreateSamplerClassificationFixture(true, true);
        try
        {
            string header = includeSampleIndex
                ? "Time,SampleIndex,ProcessName,CpuPct,WorkingSetMB\r\n"
                : "Time,ProcessName,CpuPct,WorkingSetMB\r\n";
            string emptyIndex = includeSampleIndex ? "," : "";
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                header
                + "2026-07-11T10:00:00.0000000Z," + emptyIndex + "helper-one,1.0,128\r\n"
                + "2026-07-11T10:00:00.0000000Z," + emptyIndex + "helper-two,2.0,192\r\n"
                + "2026-07-11T10:00:01.0000000Z," + emptyIndex + "helper-one,3.0,128\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);
            Dictionary<string, object> manifest = ReadManifest(dir);
            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> process = GetMap(data, "process");
            string format = includeSampleIndex ? "empty SampleIndex" : "missing SampleIndex";

            AssertEqual(2, Convert.ToInt32(manifest["processSamples"]), format + " falls back to two distinct Time instants");
            AssertEqual(2, Convert.ToInt32(GetMap(data, "counts")["processSamples"]), format + " data falls back to Time instants");
            AssertEqual(2, GetObjectList(process, "t").Count, format + " process timeline preserves both Time instants");
            AssertEqual(3, Convert.ToInt32(manifest["processSamplerValidRows"]), format + " retains three physical sampler rows");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void NoUsableRowsAreError()
    {
        string dir = CreateSamplerClassificationFixture(false, false);
        try
        {
            FrameScopeReportGenerator.GenerateForTests(dir);
            Dictionary<string, object> manifest = ReadManifest(dir);

            AssertEqual(false, Convert.ToBoolean(manifest["hasFrameData"]), "error fixture has no frame data");
            AssertEqual(0, Convert.ToInt32(manifest["processSamples"]), "error fixture process rows");
            AssertEqual(0, Convert.ToInt32(manifest["systemSamples"]), "error fixture system rows");
            AssertEqual("error", Convert.ToString(manifest["reportKind"]), "no evidence report kind");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static string CreateSamplerClassificationFixture(bool withFrames, bool healthySystem)
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-sampler-classification-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        DateTime started = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        if (withFrames)
        {
            File.WriteAllText(Path.Combine(dir, "presentmon.csv"),
                "TimeInDateTime,MsBetweenPresents,Application,ProcessID,SwapChainAddress,PresentMode,AllowsTearing\r\n"
                + started.ToString("o") + ",16.667,game.exe,4242,0x1,Hardware Composed: Independent Flip,true\r\n"
                + started.AddMilliseconds(17).ToString("o") + ",16.667,game.exe,4242,0x1,Hardware Composed: Independent Flip,true\r\n",
                Encoding.UTF8);
        }
        File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
            "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
            + (withFrames ? started.ToString("o") + ",0,0,helper,1,1.0,128,0,0,,77\r\n" : ""),
            Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
            "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
            + (healthySystem ? started.ToString("o") + ",0,True,10,16000\r\n" : ""),
            Encoding.UTF8);

        Dictionary<string, object> status = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            { "Phase", "done" },
            { "TargetProcess", "game.exe" }
        };
        AddSamplerFixtureEvidence(status, "ProcessSampler", Path.Combine(dir, "process-samples.csv"), withFrames, 0, false, withFrames ? 1 : 0);
        AddSamplerFixtureEvidence(status, "SystemSampler", Path.Combine(dir, "system-samples.csv"), true, healthySystem ? 0 : 7, false, healthySystem ? 1 : 99);
        File.WriteAllText(Path.Combine(dir, "status.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(status), Encoding.UTF8);
        return dir;
    }

    private static void AddSamplerFixtureEvidence(Dictionary<string, object> status, string prefix, string csvPath, bool started, int exitCode, bool exitedEarly, int optimisticRows)
    {
        DateTime startedAt = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        status[prefix + "Required"] = true;
        status[prefix + "Exe"] = @"C:\FrameScope\" + prefix + ".exe";
        status[prefix + "ExecutableAvailable"] = true;
        status[prefix + "Started"] = started;
        status[prefix + "Pid"] = started ? (object)4242 : null;
        status[prefix + "StartedAt"] = started ? startedAt.ToString("o") : "";
        status[prefix + "ExitedAt"] = started ? startedAt.AddSeconds(2).ToString("o") : "";
        status[prefix + "ExitCode"] = started ? (object)exitCode : null;
        status[prefix + "ExitedEarly"] = exitedEarly;
        status[prefix + "StopRequested"] = true;
        status[prefix + "ForcedStop"] = false;
        status[prefix + "CsvPath"] = csvPath;
        status[prefix + "CsvExists"] = true;
        status[prefix + "CsvBytes"] = 9999;
        status[prefix + "ValidRows"] = optimisticRows;
        status[prefix + "Status"] = "healthy";
        status[prefix + "ErrorTail"] = exitedEarly ? "synthetic sampler failure" : "";
    }

    private static Dictionary<string, object> ReadManifest(string dir)
    {
        return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(
            File.ReadAllText(Path.Combine(dir, "charts", "framescope-interactive-manifest.json"), Encoding.UTF8));
    }

    private static void ReportTargetDisplayNamePrefersConfiguredNameAndKeepsProcessName()
    {
        Dictionary<string, string> targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Counter-Strike 2", "cs2.exe" },
            { "Valorant", "VALORANT-Win64-Shipping.exe" },
            { "Delta Force", "DeltaForceClient-Win64-Shipping.exe" },
            { "Neverness To Everness", "HTGame.exe" },
            { "PUBG: BATTLEGROUNDS", "TslGame.exe" },
            { "Battlefield 6", "bf6.exe" }
        };

        foreach (KeyValuePair<string, string> target in targets)
        {
            string safeName = target.Key.Replace(":", "").Replace(" ", "-");
            string dir = Path.Combine(Path.GetTempPath(), "framescope-target-display-tests-" + safeName + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                DateTime start = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Local);
                File.WriteAllText(Path.Combine(dir, "status.json"),
                    new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                    {
                        { "Phase", "done" },
                        { "TargetDisplayName", target.Key },
                        { "TargetProcess", target.Value },
                        { "TargetResolvedProcess", target.Value },
                        { "TargetPid", 4242 }
                    }),
                    Encoding.UTF8);
                File.WriteAllText(Path.Combine(dir, "summary.json"),
                    new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                    {
                        { "TargetDisplayName", target.Key },
                        { "TargetProcess", target.Value },
                        { "TargetResolvedProcess", target.Value },
                        { "TargetPid", 4242 }
                    }),
                    Encoding.UTF8);
                File.WriteAllText(Path.Combine(dir, "presentmon.csv"),
                    "TimeInDateTime,MsBetweenPresents,Application,ProcessID,SwapChainAddress,PresentMode,AllowsTearing\r\n"
                    + start.ToString("o", CultureInfo.InvariantCulture) + ",16.67," + target.Value + ",4242,0x1,Hardware: Independent Flip,0\r\n",
                    Encoding.UTF8);
                File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                    "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                    + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                    Encoding.UTF8);
                File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                    "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                    + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0," + target.Value + ",1,1.0,512,0,0,,4242\r\n",
                    Encoding.UTF8);

                FrameScopeReportGenerator.GenerateForTests(dir);

                string dataText = File.ReadAllText(Path.Combine(dir, "charts", "framescope-interactive-data.js"), Encoding.UTF8);
                Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
                Dictionary<string, object> targetData = GetMap(data, "target");
                AssertEqual(target.Key, Convert.ToString(targetData["displayName"]), "target display name for " + target.Key);
                AssertEqual(target.Value, Convert.ToString(targetData["processName"]), "target process name for " + target.Key);
                AssertTrue(dataText.IndexOf(target.Key, StringComparison.OrdinalIgnoreCase) >= 0, "data payload should include configured target name for " + target.Key);
                AssertTrue(dataText.IndexOf(target.Value, StringComparison.OrdinalIgnoreCase) >= 0, "data payload should keep process name for " + target.Key);
                if (string.Equals(target.Key, "Valorant", StringComparison.OrdinalIgnoreCase))
                {
                    AssertTrue(dataText.IndexOf("PUBG", StringComparison.OrdinalIgnoreCase) < 0, "Valorant target display payload must not mention PUBG");
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); }
                catch { }
            }
        }
    }

    private static void ReportChartDataUsesBucketedFpsDisplayAndRawFrameStats()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-fps-display-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Local);
            StringBuilder presentMon = new StringBuilder();
            presentMon.AppendLine("TimeInDateTime,MsBetweenPresents,Application,ProcessID,SwapChainAddress,PresentMode,AllowsTearing");
            for (int i = 0; i < 20; i++)
            {
                DateTime t = start.AddMilliseconds(i * 100);
                double frameMs = i == 5 ? 0.25 : (i == 15 ? 40.0 : 10.0);
                presentMon.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1},game.exe,42,0x1,Hardware: Independent Flip,0\r\n",
                    t.ToString("o", CultureInfo.InvariantCulture),
                    frameMs.ToString(CultureInfo.InvariantCulture));
            }
            File.WriteAllText(Path.Combine(dir, "presentmon.csv"), presentMon.ToString(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,helper,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> fps = GetMap(data, "fps");
            Dictionary<string, object> frameStats = GetMap(data, "frameStats");
            Dictionary<string, object> counts = GetMap(data, "counts");

            AssertEqual(20, Convert.ToInt32(counts["frames"]), "raw frame count preserved");
            AssertEqual(1000, Convert.ToInt32(fps["bucketMs"], CultureInfo.InvariantCulture), "fps chart should use one-second display buckets");
            AssertEqual(2000, Convert.ToInt32(fps["lowWindowMs"], CultureInfo.InvariantCulture), "fps chart low series should use the existing two-second rolling window");
            AssertEqual(2, GetObjectList(fps, "t").Count, "fps display point count should be bucketed, not raw frame count");
            AssertEqual(2, GetObjectList(fps, "avg").Count, "fps average display series should be bucketed");
            AssertEqual(2, GetObjectList(fps, "low1").Count, "fps 1% low display series should be bucketed");
            AssertEqual(2, GetObjectList(fps, "low01").Count, "fps 0.1% low display series should be bucketed");
            AssertEqual(2, GetObjectList(fps, "samples").Count, "fps display bucket sample counts should be available for tooltip");
            AssertEqual(10, Convert.ToInt32(GetObjectList(fps, "samples")[0], CultureInfo.InvariantCulture), "first fps bucket sample count");
            AssertEqual(10, Convert.ToInt32(GetObjectList(fps, "samples")[1], CultureInfo.InvariantCulture), "second fps bucket sample count");
            AssertTrue(!fps.ContainsKey("min"), "fps chart data should not expose a minimum instant/anomaly marker series");
            AssertEqual(0.0, Convert.ToDouble(GetObjectList(fps, "t")[0], CultureInfo.InvariantCulture), "first bucket timestamp");
            AssertEqual(1.0, Convert.ToDouble(GetObjectList(fps, "t")[1], CultureInfo.InvariantCulture), "second bucket timestamp");
            AssertEqual(90.81, Convert.ToDouble(frameStats["average"], CultureInfo.InvariantCulture), "average FPS should use raw frames including high spike frame time");
            AssertEqual(25.0, Convert.ToDouble(frameStats["low1"], CultureInfo.InvariantCulture), "1% low should use raw slow frames");
            AssertEqual(25.0, Convert.ToDouble(frameStats["low01"], CultureInfo.InvariantCulture), "0.1% low should use raw slow frames");
            AssertEqual(4000.0, Convert.ToDouble(frameStats["maxInstant"], CultureInfo.InvariantCulture), "max FPS should use raw fastest frame");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void ProcessChartDataUsesLosslessRleCodec()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-process-rle-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Local);
            File.WriteAllText(Path.Combine(dir, "presentmon.csv"),
                "TimeInDateTime,MsBetweenPresents,Application,ProcessID,SwapChainAddress,PresentMode,AllowsTearing\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",16.67,game.exe,42,0x1,Hardware: Independent Flip,0\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,Count,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,Priorities,Pids\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,helper,1,1.5,100,0,0,,101\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,burst,1,0,50,0,0,,102\r\n"
                + start.AddSeconds(1).ToString("o", CultureInfo.InvariantCulture) + ",1,1000,helper,1,1.5,100,0,0,,101\r\n"
                + start.AddSeconds(2).ToString("o", CultureInfo.InvariantCulture) + ",2,2000,helper,1,2.5,101,0,0,,101\r\n"
                + start.AddSeconds(2).ToString("o", CultureInfo.InvariantCulture) + ",2,2000,burst,1,9.25,52,0,0,,102\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> process = GetMap(data, "process");
            AssertEqual("rle-v1", Convert.ToString(process["codec"]), "process payload codec");
            AssertEqual(3, GetObjectList(process, "t").Count, "process raw sample time count");
            AssertEqual(2, GetObjectList(process, "names").Count, "process series count");

            System.Collections.ArrayList cpu = GetObjectList(process, "cpu");
            System.Collections.ArrayList mem = GetObjectList(process, "mem");
            AssertTrue(cpu[0] is string, "process cpu series should be encoded strings");
            AssertTrue(mem[0] is string, "process mem series should be encoded strings");

            System.Collections.ArrayList names = GetObjectList(process, "names");
            int helperIndex = names.IndexOf("helper");
            int burstIndex = names.IndexOf("burst");
            AssertTrue(helperIndex >= 0, "helper series exists");
            AssertTrue(burstIndex >= 0, "burst series exists");

            List<double?> helperCpu = DecodeRleSeries(Convert.ToString(cpu[helperIndex]), 3);
            List<double?> burstCpu = DecodeRleSeries(Convert.ToString(cpu[burstIndex]), 3);
            AssertEqual(1.5, helperCpu[0].Value, "helper sample 0 cpu");
            AssertEqual(1.5, helperCpu[1].Value, "helper sample 1 cpu");
            AssertEqual(2.5, helperCpu[2].Value, "helper sample 2 cpu");
            AssertEqual(0.0, burstCpu[0].Value, "burst sample 0 cpu");
            AssertEqual(null, burstCpu[1], "burst missing sample should remain null");
            AssertEqual(9.25, burstCpu[2].Value, "burst sample 2 cpu");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void PresentMonPrimaryHardwareTrackSelectionKeepsRawDiagnostics()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-presentmon-selection-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 31, 13, 0, 0, DateTimeKind.Local);
            StringBuilder presentMon = new StringBuilder();
            presentMon.AppendLine("TimeInDateTime,MsBetweenPresents,Application,ProcessID,SwapChainAddress,PresentMode,AllowsTearing");
            for (int i = 0; i < 100; i++)
            {
                presentMon.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},16.67,game.exe,100,0xAAA,Hardware: Independent Flip,0\r\n",
                    start.AddMilliseconds(i * 16.67).ToString("o", CultureInfo.InvariantCulture));
            }
            presentMon.AppendFormat(CultureInfo.InvariantCulture,
                "{0},12.00,game.exe,100,0xAAA,Composed: Flip,0\r\n",
                start.AddSeconds(2).ToString("o", CultureInfo.InvariantCulture));
            presentMon.AppendFormat(CultureInfo.InvariantCulture,
                "{0},1201.00,game.exe,100,0xAAA,Hardware: Independent Flip,0\r\n",
                start.AddSeconds(3).ToString("o", CultureInfo.InvariantCulture));
            for (int i = 0; i < 30; i++)
            {
                presentMon.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},16.67,overlay.exe,200,0xBBB,Composed: Copy with GPU GDI,0\r\n",
                    start.AddMilliseconds(i * 20).ToString("o", CultureInfo.InvariantCulture));
            }

            string path = Path.Combine(dir, "presentmon.csv");
            File.WriteAllText(path, presentMon.ToString(), Encoding.UTF8);

            Dictionary<string, object> read = FrameScopeReportGenerator.ReadPresentMonForTests(path);
            Dictionary<string, object> diagnostics = GetMap(read, "diagnostics");

            AssertEqual(100, Convert.ToInt32(read["frames"], CultureInfo.InvariantCulture), "selected hardware frame count");
            AssertEqual(132, Convert.ToInt32(diagnostics["rawRows"], CultureInfo.InvariantCulture), "raw PresentMon row count");
            AssertEqual(132, Convert.ToInt32(diagnostics["validRows"], CultureInfo.InvariantCulture), "valid PresentMon row count");
            AssertEqual(100, Convert.ToInt32(diagnostics["selectedRows"], CultureInfo.InvariantCulture), "selected PresentMon row count");
            AssertEqual("primary-hardware-track", Convert.ToString(diagnostics["selectionMode"]), "multi-track hardware selection mode");
            AssertEqual(2, Convert.ToInt32(diagnostics["trackCount"], CultureInfo.InvariantCulture), "track count");
            AssertEqual(30, Convert.ToInt32(diagnostics["droppedTrackRows"], CultureInfo.InvariantCulture), "dropped non-primary track rows");
            AssertEqual(2, Convert.ToInt32(diagnostics["droppedModeRows"], CultureInfo.InvariantCulture), "dropped non-hardware and resume artifact rows");
            AssertEqual(1, Convert.ToInt32(diagnostics["droppedResumeArtifactRows"], CultureInfo.InvariantCulture), "dropped resume artifact row count");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void CpuCoreTelemetryFlowsIntoChartDataWithoutFakeVoltage()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-cpu-core-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "cs2.exe" },
                    { "CpuCoreSamplesCsv", Path.Combine(dir, "cpu-core-samples.csv") },
                    { "CpuCoreTelemetryAvailable", true },
                    { "CpuCoreSampleCount", 2 },
                    { "CpuCoreLogicalProcessorCount", 2 },
                    { "CpuVoltageAvailable", false },
                    { "CpuVoltageStatus", "unavailable" }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + "2026-05-25T00:00:00+08:00,0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + "2026-05-25T00:00:00+08:00,0,0,cs2,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-core-samples.csv"),
                "Time,SampleIndex,ElapsedMs,Source,ProcessorGroup,LogicalProcessor,PhysicalCoreId,ThreadIndex,ActualFrequencyMHz,ProcessorFrequencyMHz,ProcessorPerformancePct,PercentOfMaximumFrequency,ProcessorUtilityPct,PerformanceLimitFlags\r\n"
                + "2026-05-25T00:00:00+08:00,0,0,windows-perfcounter,0,0,,,4300,4200,102.3,102.3,12.5,0\r\n"
                + "2026-05-25T00:00:00+08:00,0,0,windows-perfcounter,0,1,,,4625,4200,110.1,110.1,8.25,0\r\n"
                + "2026-05-25T00:00:00.5000000+08:00,1,500,windows-perfcounter,0,0,,,4350,4200,103.5,103.5,13.5,0\r\n"
                + "2026-05-25T00:00:00.5000000+08:00,1,500,windows-perfcounter,0,1,,,4675,4200,111.3,111.3,9.25,0\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            string manifestPath = Path.Combine(dir, "charts", "framescope-interactive-manifest.json");
            string dataPath = Path.Combine(dir, "charts", "framescope-interactive-data.js");
            Dictionary<string, object> manifest = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(manifestPath, Encoding.UTF8));

            AssertEqual(4, Convert.ToInt32(manifest["cpuCoreSampleCount"]), "manifest cpu core raw sample row count");
            AssertEqual(true, Convert.ToBoolean(manifest["cpuCoreTelemetryAvailable"]), "manifest cpu core available");
            AssertEqual(false, Convert.ToBoolean(manifest["cpuVoltageAvailable"]), "manifest voltage available");

            Dictionary<string, object> data = LoadReportData(dataPath);
            Dictionary<string, object> cpuCore = GetMap(data, "cpuCore");
            Dictionary<string, object> cpuVoltage = GetMap(data, "cpuVoltage");
            AssertEqual(true, Convert.ToBoolean(cpuCore["available"]), "cpu core chart available");
            AssertEqual("MHz", Convert.ToString(cpuCore["unit"]), "cpu core unit");
            AssertEqual(2, GetObjectList(cpuCore, "t").Count, "cpu core raw sample point count");
            AssertTrue(!cpuCore.ContainsKey("displayBucketMs"), "cpu core chart should not expose one-second display bucket metadata");
            AssertEqual(2, GetObjectList(cpuCore, "series").Count, "cpu core series count");
            AssertEqual(false, Convert.ToBoolean(cpuVoltage["available"]), "cpu voltage unavailable without real source");
            AssertTrue(Convert.ToString(cpuVoltage["reason"]).IndexOf("voltage", StringComparison.OrdinalIgnoreCase) >= 0, "voltage unavailable reason should name missing voltage source");

            string dataText = File.ReadAllText(dataPath, Encoding.UTF8);
            AssertTrue(!dataText.Contains("\"VID\""), "report data should not invent VID fields");
            AssertTrue(!dataText.Contains("\"Vcore\""), "report data should not invent Vcore fields");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void CpuVoltageTelemetryFlowsFromDedicatedCsvIntoManifestAndChartData()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-cpu-voltage-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 27, 10, 0, 0, DateTimeKind.Local);
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "cs2.exe" },
                    { "CpuCoreSamplesCsv", Path.Combine(dir, "cpu-core-samples.csv") },
                    { "CpuCoreTelemetryAvailable", true },
                    { "CpuCoreSampleCount", 2 },
                    { "CpuVoltageSamplesCsv", Path.Combine(dir, "cpu-voltage-samples.csv") },
                    { "CpuVoltageAvailable", true },
                    { "CpuVoltageVcoreAvailable", true },
                    { "CpuVoltagePerCoreAvailable", false },
                    { "CpuVoltageNonPerCoreAvailable", false },
                    { "CpuVoltageStatus", "vcore-available" },
                    { "CpuVoltageSource", "synthetic-sensor" },
                    { "CpuVoltageProviderKind", "synthetic" },
                    { "CpuVoltageSampleIntervalMs", 1000 },
                    { "CpuVoltageSampleCount", 2 },
                    { "CpuVoltageVcoreSampleCount", 2 },
                    { "CpuVoltagePerCoreSampleCount", 0 },
                    { "CpuVoltageNonPerCoreSampleCount", 0 },
                    { "CpuVoltageRejectedSampleCount", 0 }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,cs2,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-core-samples.csv"),
                "Time,SampleIndex,ElapsedMs,Source,ProcessorGroup,LogicalProcessor,PhysicalCoreId,ThreadIndex,ActualFrequencyMHz,ProcessorFrequencyMHz,ProcessorPerformancePct,PercentOfMaximumFrequency,ProcessorUtilityPct,PerformanceLimitFlags\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,windows-perfcounter,0,0,,,4300,4200,102.3,102.3,12.5,0\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,windows-perfcounter,0,1,,,4625,4200,110.1,110.1,8.25,0\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-voltage-samples.csv"),
                "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreId,PhysicalCoreId,ThreadIndex,VoltageVolts,Status,Reason,SensorIdentifier\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,synthetic-sensor,synthetic,CPU VCore,,,,,,1.064,vcore,,/mainboard/superio/voltage/0\r\n"
                + start.AddMilliseconds(500).ToString("o", CultureInfo.InvariantCulture) + ",1,500,synthetic-sensor,synthetic,CPU VCore,,,,,,1.080,vcore,,/mainboard/superio/voltage/0\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> manifest = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(dir, "charts", "framescope-interactive-manifest.json"), Encoding.UTF8));
            AssertEqual(true, Convert.ToBoolean(manifest["cpuVoltageAvailable"]), "manifest voltage available");
            AssertEqual(true, Convert.ToBoolean(manifest["cpuVoltageVcoreAvailable"]), "manifest voltage vcore available");
            AssertEqual(false, Convert.ToBoolean(manifest["cpuVoltagePerCoreAvailable"]), "manifest voltage per-core available");
            AssertEqual(false, Convert.ToBoolean(manifest["cpuVoltageNonPerCoreAvailable"]), "manifest voltage non-per-core available");
            AssertEqual("vcore-available", Convert.ToString(manifest["cpuVoltageStatus"]), "manifest voltage status");
            AssertEqual("synthetic-sensor", Convert.ToString(manifest["cpuVoltageSource"]), "manifest voltage source");
            AssertEqual("synthetic", Convert.ToString(manifest["cpuVoltageProviderKind"]), "manifest voltage provider kind");
            AssertEqual("", Convert.ToString(manifest["cpuVoltageReason"]), "manifest available voltage reason");
            AssertEqual(2, Convert.ToInt32(manifest["cpuVoltageSampleCount"]), "manifest voltage raw sample row count");
            AssertEqual(2, Convert.ToInt32(manifest["cpuVoltageVcoreSampleCount"]), "manifest voltage vcore raw sample row count");
            AssertEqual(0, Convert.ToInt32(manifest["cpuVoltagePerCoreSampleCount"]), "manifest voltage per-core raw sample row count");
            AssertEqual(0, Convert.ToInt32(manifest["cpuVoltageNonPerCoreSampleCount"]), "manifest voltage non-per-core sample count");
            AssertEqual(0, Convert.ToInt32(manifest["cpuVoltageRejectedSampleCount"]), "manifest voltage rejected sample count");

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> cpuVoltage = GetMap(data, "cpuVoltage");
            AssertEqual(true, Convert.ToBoolean(cpuVoltage["available"]), "cpu voltage chart available");
            AssertEqual("V", Convert.ToString(cpuVoltage["unit"]), "cpu voltage unit");
            AssertEqual("synthetic-sensor", Convert.ToString(cpuVoltage["source"]), "cpu voltage chart source");
            AssertEqual("vcore-available", Convert.ToString(cpuVoltage["status"]), "cpu voltage chart status");
            AssertEqual("synthetic", Convert.ToString(cpuVoltage["providerKind"]), "cpu voltage chart provider kind");
            AssertEqual(2, Convert.ToInt32(cpuVoltage["totalSampleCount"]), "cpu voltage chart total raw sample row count");
            AssertEqual(2, Convert.ToInt32(cpuVoltage["vcoreSampleCount"]), "cpu voltage chart vcore raw sample row count");
            AssertEqual(0, Convert.ToInt32(cpuVoltage["perCoreSampleCount"]), "cpu voltage chart per-core raw sample row count");
            AssertEqual(0, Convert.ToInt32(cpuVoltage["nonPerCoreSampleCount"]), "cpu voltage chart non-per-core sample count");
            AssertEqual(0, Convert.ToInt32(cpuVoltage["rejectedSampleCount"]), "cpu voltage chart rejected sample count");
            AssertEqual(2, GetObjectList(cpuVoltage, "t").Count, "cpu voltage raw sample point count");
            AssertTrue(!cpuVoltage.ContainsKey("displayBucketMs"), "cpu voltage chart should not expose one-second display bucket metadata");
            AssertEqual(1, GetObjectList(cpuVoltage, "series").Count, "cpu voltage series count");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void CpuVoltageNonPerCoreTelemetryDoesNotCreateChartSeries()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-cpu-voltage-non-core-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 27, 11, 0, 0, DateTimeKind.Local);
            string reason = "Only non-Vcore voltage sensors were detected; CPU Voltage requires explicit CPU Vcore/CPU Voltage. VID/SOC/Package/VBAT/VIN are not accepted.";
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "cs2.exe" },
                    { "CpuVoltageSamplesCsv", Path.Combine(dir, "cpu-voltage-samples.csv") },
                    { "CpuVoltageAvailable", false },
                    { "CpuVoltagePerCoreAvailable", false },
                    { "CpuVoltageNonPerCoreAvailable", true },
                    { "CpuVoltageStatus", "non-per-core-only" },
                    { "CpuVoltageSource", "builtin-librehardwaremonitor" },
                    { "CpuVoltageProviderKind", "built-in" },
                    { "CpuVoltageUnavailableReason", reason },
                    { "CpuVoltageSampleCount", 0 },
                    { "CpuVoltageVcoreSampleCount", 0 },
                    { "CpuVoltagePerCoreSampleCount", 0 },
                    { "CpuVoltageNonPerCoreSampleCount", 1 },
                    { "CpuVoltageRejectedSampleCount", 1 }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,cs2,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-voltage-samples.csv"),
                "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreId,PhysicalCoreId,ThreadIndex,VoltageVolts,Status,Reason,SensorIdentifier\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,builtin-librehardwaremonitor,built-in,Vcore SoC,,,,,,1.104,non-per-core,SOC voltage is not CPU Vcore,/mainboard/superio/voltage/0\r\n"
                + start.AddMilliseconds(500).ToString("o", CultureInfo.InvariantCulture) + ",1,500,builtin-librehardwaremonitor,built-in,Core #1 Vcore,0,0,0,0,,1.100,non-per-core,Numbered per-core Vcore is not GamePP CPU Voltage,/cpu/0/voltage/4\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> manifest = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(dir, "charts", "framescope-interactive-manifest.json"), Encoding.UTF8));
            AssertEqual(false, Convert.ToBoolean(manifest["cpuVoltageAvailable"]), "manifest aggregate voltage not chart available");
            AssertEqual(false, Convert.ToBoolean(manifest["cpuVoltagePerCoreAvailable"]), "manifest aggregate voltage not per-core available");
            AssertEqual(true, Convert.ToBoolean(manifest["cpuVoltageNonPerCoreAvailable"]), "manifest aggregate voltage recorded");
            AssertEqual("non-per-core-only", Convert.ToString(manifest["cpuVoltageStatus"]), "manifest aggregate voltage status");
            AssertEqual(0, Convert.ToInt32(manifest["cpuVoltageSampleCount"]), "manifest aggregate voltage sample count");
            AssertEqual(0, Convert.ToInt32(manifest["cpuVoltageVcoreSampleCount"]), "manifest aggregate voltage vcore count");
            AssertEqual(0, Convert.ToInt32(manifest["cpuVoltagePerCoreSampleCount"]), "manifest aggregate voltage per-core count");
            AssertEqual(2, Convert.ToInt32(manifest["cpuVoltageNonPerCoreSampleCount"]), "manifest aggregate voltage non-per-core count");
            AssertEqual(2, Convert.ToInt32(manifest["cpuVoltageRejectedSampleCount"]), "manifest aggregate voltage rejected count");

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> cpuVoltage = GetMap(data, "cpuVoltage");
            AssertEqual(false, Convert.ToBoolean(cpuVoltage["available"]), "aggregate voltage chart unavailable");
            AssertEqual("non-per-core-only", Convert.ToString(cpuVoltage["status"]), "aggregate voltage chart status");
            AssertEqual("builtin-librehardwaremonitor", Convert.ToString(cpuVoltage["source"]), "aggregate voltage chart source");
            AssertEqual("built-in", Convert.ToString(cpuVoltage["providerKind"]), "aggregate voltage chart provider kind");
            AssertEqual(0, Convert.ToInt32(cpuVoltage["totalSampleCount"]), "aggregate voltage chart total count");
            AssertEqual(0, Convert.ToInt32(cpuVoltage["vcoreSampleCount"]), "aggregate voltage chart vcore count");
            AssertEqual(0, Convert.ToInt32(cpuVoltage["perCoreSampleCount"]), "aggregate voltage chart per-core count");
            AssertEqual(2, Convert.ToInt32(cpuVoltage["nonPerCoreSampleCount"]), "aggregate voltage chart non-per-core count");
            AssertEqual(2, Convert.ToInt32(cpuVoltage["rejectedSampleCount"]), "aggregate voltage chart rejected count");
            AssertEqual(0, GetObjectList(cpuVoltage, "series").Count, "aggregate voltage should not create series");
            AssertTrue(Convert.ToString(cpuVoltage["reason"]).IndexOf("CPU Vcore", StringComparison.OrdinalIgnoreCase) >= 0, "aggregate voltage chart reason");
            AssertTrue(Convert.ToString(cpuVoltage["reason"]).IndexOf("VID/SOC/Package/VBAT/VIN", StringComparison.OrdinalIgnoreCase) >= 0, "aggregate voltage chart reason should list rejected families");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void CpuVoltageSidecarOverridesStaleRunStatus()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-cpu-voltage-sidecar-precedence-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 28, 0, 0, 0, DateTimeKind.Local);
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(Path.Combine(dir, "status.json"),
                serializer.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "cs2.exe" },
                    { "CpuVoltageSamplesCsv", Path.Combine(dir, "cpu-voltage-samples.csv") },
                    { "CpuVoltageAvailable", false },
                    { "CpuVoltageVcoreAvailable", false },
                    { "CpuVoltagePerCoreAvailable", false },
                    { "CpuVoltageNonPerCoreAvailable", true },
                    { "CpuVoltageStatus", "non-per-core-only" },
                    { "CpuVoltageSource", "stale-status" },
                    { "CpuVoltageProviderKind", "stale" },
                    { "CpuVoltageSampleCount", 0 },
                    { "CpuVoltageVcoreSampleCount", 0 },
                    { "CpuVoltagePerCoreSampleCount", 0 },
                    { "CpuVoltageNonPerCoreSampleCount", 1 },
                    { "CpuVoltageRejectedSampleCount", 1 }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-voltage-telemetry-status.json"),
                serializer.Serialize(new Dictionary<string, object>
                {
                    { "CpuVoltageSamplesCsv", Path.Combine(dir, "cpu-voltage-samples.csv") },
                    { "CpuVoltageAvailable", true },
                    { "CpuVoltageVcoreAvailable", true },
                    { "CpuVoltagePerCoreAvailable", false },
                    { "CpuVoltageNonPerCoreAvailable", false },
                    { "CpuVoltageStatus", "vcore-available" },
                    { "CpuVoltageSource", "builtin-librehardwaremonitor" },
                    { "CpuVoltageProviderKind", "built-in" },
                    { "CpuVoltageProviderRequested", "auto" },
                    { "CpuVoltageUnavailableReason", "" },
                    { "CpuVoltageSampleCount", 1 },
                    { "CpuVoltageVcoreSampleCount", 1 },
                    { "CpuVoltagePerCoreSampleCount", 0 },
                    { "CpuVoltageNonPerCoreSampleCount", 0 },
                    { "CpuVoltageRejectedSampleCount", 0 }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,cs2,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-voltage-samples.csv"),
                "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreId,PhysicalCoreId,ThreadIndex,VoltageVolts,Status,Reason,SensorIdentifier\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,builtin-librehardwaremonitor,built-in,Vcore,,,,,,1.104,vcore,,/lpc/it8689e/0/voltage/0\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> manifest = serializer
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(dir, "charts", "framescope-interactive-manifest.json"), Encoding.UTF8));
            AssertEqual(true, Convert.ToBoolean(manifest["cpuVoltageAvailable"]), "sidecar should override stale status voltage availability");
            AssertEqual(true, Convert.ToBoolean(manifest["cpuVoltageVcoreAvailable"]), "sidecar should preserve vcore availability");
            AssertEqual(false, Convert.ToBoolean(manifest["cpuVoltagePerCoreAvailable"]), "sidecar should override stale status per-core availability");
            AssertEqual(false, Convert.ToBoolean(manifest["cpuVoltageNonPerCoreAvailable"]), "sidecar should preserve non-per-core availability");
            AssertEqual("vcore-available", Convert.ToString(manifest["cpuVoltageStatus"]), "sidecar should override stale status text");
            AssertEqual("builtin-librehardwaremonitor", Convert.ToString(manifest["cpuVoltageSource"]), "sidecar should override stale source");
            AssertEqual("built-in", Convert.ToString(manifest["cpuVoltageProviderKind"]), "sidecar should override stale provider");

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> cpuVoltage = GetMap(data, "cpuVoltage");
            AssertEqual(true, Convert.ToBoolean(cpuVoltage["available"]), "sidecar Vcore voltage chart available");
            AssertEqual("vcore-available", Convert.ToString(cpuVoltage["status"]), "sidecar Vcore voltage chart status");
            AssertEqual("builtin-librehardwaremonitor", Convert.ToString(cpuVoltage["source"]), "sidecar Vcore voltage chart source");
            AssertEqual("built-in", Convert.ToString(cpuVoltage["providerKind"]), "sidecar Vcore voltage chart provider");
            AssertEqual("auto", Convert.ToString(cpuVoltage["providerRequested"]), "sidecar Vcore voltage requested provider");
            AssertEqual(1, Convert.ToInt32(cpuVoltage["totalSampleCount"]), "sidecar Vcore voltage total count");
            AssertEqual(1, Convert.ToInt32(cpuVoltage["vcoreSampleCount"]), "sidecar Vcore voltage vcore count");
            AssertEqual(0, Convert.ToInt32(cpuVoltage["perCoreSampleCount"]), "sidecar Vcore voltage per-core count");
            AssertEqual(0, Convert.ToInt32(cpuVoltage["nonPerCoreSampleCount"]), "sidecar Vcore voltage non-per-core count");
            AssertEqual(1, GetObjectList(cpuVoltage, "series").Count, "sidecar Vcore voltage should create one chart series");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void CpuVidTelemetryFlowsFromDedicatedCsvIntoManifestAndChartData()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-cpu-vid-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 28, 9, 0, 0, DateTimeKind.Local);
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "cs2.exe" },
                    { "CpuCoreSamplesCsv", Path.Combine(dir, "cpu-core-samples.csv") },
                    { "CpuCoreTelemetryAvailable", true },
                    { "CpuCoreSampleCount", 2 },
                    { "CpuVoltageAvailable", false },
                    { "CpuVoltagePerCoreAvailable", false },
                    { "CpuVoltageStatus", "unavailable" },
                    { "CpuVidSamplesCsv", Path.Combine(dir, "cpu-vid-samples.csv") },
                    { "CpuVidAvailable", true },
                    { "CpuVidStatus", "core-vid-available" },
                    { "CpuVidSource", "builtin-librehardwaremonitor" },
                    { "CpuVidProviderKind", "built-in" },
                    { "CpuVidSampleIntervalMs", 1000 },
                    { "CpuVidSampleCount", 8 },
                    { "CpuVidCoreCount", 8 },
                    { "CpuVidNote", "VID 是 CPU 请求/目标电压，不是真实 per-core Vcore。" }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,cs2,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-core-samples.csv"),
                "Time,SampleIndex,ElapsedMs,Source,ProcessorGroup,LogicalProcessor,PhysicalCoreId,ThreadIndex,ActualFrequencyMHz,ProcessorFrequencyMHz,ProcessorPerformancePct,PercentOfMaximumFrequency,ProcessorUtilityPct,PerformanceLimitFlags\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,windows-perfcounter,0,0,,,4300,4200,102.3,102.3,12.5,0\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,windows-perfcounter,0,1,,,4625,4200,110.1,110.1,8.25,0\r\n",
                Encoding.UTF8);

            StringBuilder vid = new StringBuilder();
            vid.AppendLine("Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreIndex,PhysicalCoreId,ThreadIndex,VidVolts,Status,Reason,SensorIdentifier");
            for (int core = 1; core <= 8; core++)
            {
                vid.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},0,0,builtin-librehardwaremonitor,built-in,Core #{1} VID,0,{2},{2},{2},,{3},core-vid,VID is request/target voltage not real per-core Vcore,/intelcpu/0/voltage/{2}\r\n",
                    start.ToString("o", CultureInfo.InvariantCulture),
                    core,
                    core - 1,
                    (1.000 + core * 0.01).ToString("0.000", CultureInfo.InvariantCulture));
                vid.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},1,500,builtin-librehardwaremonitor,built-in,Core #{1} VID,0,{2},{2},{2},,{3},core-vid,VID is request/target voltage not real per-core Vcore,/intelcpu/0/voltage/{2}\r\n",
                    start.AddMilliseconds(500).ToString("o", CultureInfo.InvariantCulture),
                    core,
                    core - 1,
                    (1.100 + core * 0.01).ToString("0.000", CultureInfo.InvariantCulture));
            }
            File.WriteAllText(Path.Combine(dir, "cpu-vid-samples.csv"), vid.ToString(), Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> manifest = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(dir, "charts", "framescope-interactive-manifest.json"), Encoding.UTF8));
            AssertEqual(true, Convert.ToBoolean(manifest["cpuVidAvailable"]), "manifest vid available");
            AssertEqual(16, Convert.ToInt32(manifest["cpuVidSampleCount"]), "manifest vid raw sample row count");
            AssertEqual(8, Convert.ToInt32(manifest["cpuVidCoreCount"]), "manifest vid core count");
            AssertEqual("builtin-librehardwaremonitor", Convert.ToString(manifest["cpuVidSource"]), "manifest vid source");
            AssertEqual("core-vid-available", Convert.ToString(manifest["cpuVidStatus"]), "manifest vid status");
            AssertEqual("", Convert.ToString(manifest["cpuVidReason"]), "manifest available vid reason");

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> cpuVid = GetMap(data, "cpuVid");
            Dictionary<string, object> cpuVoltage = GetMap(data, "cpuVoltage");
            Dictionary<string, object> cpuCore = GetMap(data, "cpuCore");
            AssertEqual(true, Convert.ToBoolean(cpuCore["available"]), "cpu core frequency remains available");
            AssertEqual(true, Convert.ToBoolean(cpuVid["available"]), "cpu vid chart available");
            AssertEqual("V", Convert.ToString(cpuVid["unit"]), "cpu vid unit");
            AssertEqual(2, GetObjectList(cpuVid, "t").Count, "cpu vid raw sample point count");
            AssertTrue(!cpuVid.ContainsKey("displayBucketMs"), "cpu vid chart should not expose one-second display bucket metadata");
            AssertEqual(8, GetObjectList(cpuVid, "series").Count, "cpu vid series count");
            AssertTrue(Convert.ToString(cpuVid["note"]).IndexOf("请求", StringComparison.OrdinalIgnoreCase) >= 0, "cpu vid note should say requested voltage");
            AssertEqual(false, Convert.ToBoolean(cpuVoltage["available"]), "vid data should not make real cpu voltage chart available");
            AssertEqual(0, GetObjectList(cpuVoltage, "series").Count, "cpu voltage should not receive vid series");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void InvalidVoltageFrequencyAndVidSamplesBecomeNullGaps()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-invalid-telemetry-gap-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 6, 13, 14, 0, 0, DateTimeKind.Local);
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "Valorant.exe" },
                    { "CpuVoltageAvailable", true },
                    { "CpuVoltageVcoreAvailable", true },
                    { "CpuVoltageStatus", "vcore-available" },
                    { "CpuVidAvailable", true },
                    { "CpuVidStatus", "core-vid-available" },
                    { "CpuVidSampleCount", 3 },
                    { "CpuVidCoreCount", 1 }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,CpuFrequencyMHz,CpuPerformancePct,AvailableMB,GpuClockMHz,MemClockMHz,PowerW,GpuTempC\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,4200,100,16000,1800,9501,88,55\r\n"
                + start.AddSeconds(1).ToString("o", CultureInfo.InvariantCulture) + ",1,True,11,0,0,15900,0,0,0,0\r\n"
                + start.AddSeconds(2).ToString("o", CultureInfo.InvariantCulture) + ",2,True,12,4300,100,15800,1900,9501,90,56\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,Valorant,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-voltage-samples.csv"),
                "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreId,PhysicalCoreId,ThreadIndex,VoltageVolts,Status,Reason,SensorIdentifier\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,builtin-librehardwaremonitor,built-in,Vcore,,,,,,1.080,vcore,,/lpc/it8689e/0/voltage/0\r\n"
                + start.AddSeconds(1).ToString("o", CultureInfo.InvariantCulture) + ",1,1000,builtin-librehardwaremonitor,built-in,Vcore,,,,,,0,vcore,read failed,/lpc/it8689e/0/voltage/0\r\n"
                + start.AddSeconds(2).ToString("o", CultureInfo.InvariantCulture) + ",2,2000,builtin-librehardwaremonitor,built-in,Vcore,,,,,,1.092,vcore,,/lpc/it8689e/0/voltage/0\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-vid-samples.csv"),
                "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreIndex,PhysicalCoreId,ThreadIndex,VidVolts,Status,Reason,SensorIdentifier\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,builtin-librehardwaremonitor,built-in,Core #1 VID,0,0,0,0,,1.050,core-vid,VID is request/target voltage,/test/cpu/vid/0\r\n"
                + start.AddSeconds(1).ToString("o", CultureInfo.InvariantCulture) + ",1,1000,builtin-librehardwaremonitor,built-in,Core #1 VID,0,0,0,0,,0,core-vid,read failed,/test/cpu/vid/0\r\n"
                + start.AddSeconds(2).ToString("o", CultureInfo.InvariantCulture) + ",2,2000,builtin-librehardwaremonitor,built-in,Core #1 VID,0,0,0,0,,1.060,core-vid,VID is request/target voltage,/test/cpu/vid/0\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            System.Collections.ArrayList cpuFreq = GetObjectList(GetMap(GetMap(data, "system"), "perf"), "cpuFreq");
            System.Collections.ArrayList gpuClock = GetObjectList(GetMap(GetMap(data, "system"), "perf"), "gpuClock");
            System.Collections.ArrayList memClock = GetObjectList(GetMap(GetMap(data, "system"), "perf"), "memClock");
            System.Collections.ArrayList power = GetObjectList(GetMap(GetMap(data, "system"), "io"), "power");
            System.Collections.ArrayList temp = GetObjectList(GetMap(GetMap(data, "system"), "io"), "temp");
            AssertEqual(null, cpuFreq[1], "invalid zero CPU frequency should become a null gap");
            AssertEqual(null, gpuClock[1], "invalid zero GPU clock should become a null gap");
            AssertEqual(null, memClock[1], "invalid zero memory clock should become a null gap");
            AssertEqual(null, power[1], "invalid zero GPU power should become a null gap");
            AssertEqual(null, temp[1], "invalid zero GPU temperature should become a null gap");

            Dictionary<string, object> cpuVoltage = GetMap(data, "cpuVoltage");
            Dictionary<string, object> cpuVid = GetMap(data, "cpuVid");
            System.Collections.ArrayList voltageSeries = GetObjectList(cpuVoltage, "series");
            System.Collections.ArrayList vidSeries = GetObjectList(cpuVid, "series");
            AssertNoNumericZero(GetObjectList((Dictionary<string, object>)voltageSeries[0], "data"), "invalid zero Vcore should be skipped instead of drawn");
            AssertNoNumericZero(GetObjectList((Dictionary<string, object>)vidSeries[0], "data"), "invalid zero VID should be skipped instead of drawn");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void AmdLibreHardwareMonitorCoreVidSamplesAreRejectedAsUnreliable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-low-amd-vid-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 6, 13, 14, 10, 0, DateTimeKind.Local);
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "Valorant.exe" },
                    { "CpuVoltageAvailable", true },
                    { "CpuVoltageVcoreAvailable", true },
                    { "CpuVoltageStatus", "vcore-available" },
                    { "CpuVidAvailable", true },
                    { "CpuVidStatus", "core-vid-available" },
                    { "CpuVidSampleCount", 8 },
                    { "CpuVidCoreCount", 8 }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,Valorant,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-voltage-samples.csv"),
                "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreId,PhysicalCoreId,ThreadIndex,VoltageVolts,Status,Reason,SensorIdentifier\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,builtin-librehardwaremonitor,built-in,Vcore,,,,,,1.080,vcore,,/lpc/it8689e/0/voltage/0\r\n",
                Encoding.UTF8);
            StringBuilder vid = new StringBuilder();
            vid.AppendLine("Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreIndex,PhysicalCoreId,ThreadIndex,VidVolts,Status,Reason,SensorIdentifier");
            for (int core = 0; core < 8; core++)
            {
                vid.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},0,0,builtin-librehardwaremonitor,built-in,Core #{1} VID,0,{2},{2},{2},,0.762,core-vid,VID is request/target voltage,/amdcpu/0/voltage/{3}\r\n",
                    start.ToString("o", CultureInfo.InvariantCulture),
                    core + 1,
                    core,
                    core + 2);
            }
            File.WriteAllText(Path.Combine(dir, "cpu-vid-samples.csv"), vid.ToString(), Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> manifest = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(dir, "charts", "framescope-interactive-manifest.json"), Encoding.UTF8));
            string expectedReason = ExpectedAmdLhmCpuVidUnavailableReason();
            AssertEqual(false, Convert.ToBoolean(manifest["cpuVidAvailable"]), "manifest should not keep stale VID availability");
            AssertEqual("unavailable", Convert.ToString(manifest["cpuVidStatus"]), "manifest should not keep stale AMD LHM Core VID status");
            AssertEqual(0, Convert.ToInt32(manifest["cpuVidSampleCount"]), "manifest should count only accepted VID rows");
            AssertEqual(expectedReason, Convert.ToString(manifest["cpuVidReason"]), "manifest should expose the Chinese AMD LHM VID rejection reason");

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            string reportText = File.ReadAllText(Path.Combine(dir, "charts", "framescope-interactive-report.html"), Encoding.UTF8);
            Dictionary<string, object> cpuVoltage = GetMap(data, "cpuVoltage");
            Dictionary<string, object> cpuVid = GetMap(data, "cpuVid");
            System.Collections.ArrayList voltageSeries = GetObjectList(cpuVoltage, "series");
            AssertEqual(true, Convert.ToBoolean(cpuVoltage["available"]), "Vcore should remain available");
            AssertEqual(1, voltageSeries.Count, "Vcore should remain visible in CPU Voltage / Vcore");
            AssertEqual("cpu-voltage:vcore", Convert.ToString(((Dictionary<string, object>)voltageSeries[0])["key"]), "Vcore should stay in the CPU Voltage chart series");
            AssertEqual(false, Convert.ToBoolean(cpuVid["available"]), "AMD Core VID from LHM should not be shown as accurate VID");
            AssertEqual(0, GetObjectList(cpuVid, "series").Count, "AMD Core VID from LHM should not create series");
            AssertEqual(expectedReason, Convert.ToString(cpuVid["reason"]), "cpu vid chart reason should explain AMD source rejection in Chinese");
            AssertTrue(reportText.IndexOf("data-view='cpuVid'", StringComparison.OrdinalIgnoreCase) >= 0, "unavailable CPU VID report should keep the CPU Core VID tab");
            AssertTrue(reportText.IndexOf("tab-disabled' data-view='cpuVid'", StringComparison.OrdinalIgnoreCase) < 0, "unavailable CPU VID tab should not be disabled");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void CpuVidZeroBasedAndOneBasedNamesRemainIndependentWhenValuesMatch()
    {
        AssertCpuVidNamePatternCreatesEightSeries("zero-based", false);
        AssertCpuVidNamePatternCreatesEightSeries("one-based", true);
    }

    private static void AssertCpuVidNamePatternCreatesEightSeries(string scenario, bool oneBasedHashNames)
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-cpu-vid-" + scenario + "-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Local);
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "cs2.exe" },
                    { "CpuVoltageAvailable", false },
                    { "CpuVoltageVcoreAvailable", false },
                    { "CpuVoltagePerCoreAvailable", false },
                    { "CpuVoltageStatus", "unavailable" },
                    { "CpuVidSamplesCsv", Path.Combine(dir, "cpu-vid-samples.csv") },
                    { "CpuVidAvailable", true },
                    { "CpuVidStatus", "core-vid-available" },
                    { "CpuVidSource", "builtin-librehardwaremonitor" },
                    { "CpuVidProviderKind", "built-in" },
                    { "CpuVidSampleIntervalMs", 1000 },
                    { "CpuVidSampleCount", 8 },
                    { "CpuVidCoreCount", 8 },
                    { "CpuVidNote", "VID is CPU request/target voltage, not real Vcore." }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,cs2,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);

            StringBuilder vid = new StringBuilder();
            vid.AppendLine("Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreIndex,PhysicalCoreId,ThreadIndex,VidVolts,Status,Reason,SensorIdentifier");
            for (int core = 0; core < 8; core++)
            {
                string sensorName = oneBasedHashNames ? "Core #" + (core + 1).ToString(CultureInfo.InvariantCulture) + " VID" : "Core " + core.ToString(CultureInfo.InvariantCulture) + " VID";
                vid.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},0,0,builtin-librehardwaremonitor,built-in,{1},0,{2},{2},{2},,0.975,core-vid,VID is request/target voltage not real Vcore,/intelcpu/0/voltage/{2}\r\n",
                    start.ToString("o", CultureInfo.InvariantCulture),
                    sensorName,
                    core);
            }
            File.WriteAllText(Path.Combine(dir, "cpu-vid-samples.csv"), vid.ToString(), Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            string reportText = File.ReadAllText(Path.Combine(dir, "charts", "framescope-interactive-report.html"), Encoding.UTF8);
            AssertTrue(reportText.IndexOf("data-view='cpuVid'", StringComparison.OrdinalIgnoreCase) >= 0, scenario + " report should include CPU Core VID tab");
            AssertTrue(reportText.IndexOf("tab-disabled' data-view='cpuVid'", StringComparison.OrdinalIgnoreCase) < 0, scenario + " CPU Core VID tab should not be disabled");

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> cpuVid = GetMap(data, "cpuVid");
            Dictionary<string, object> cpuVoltage = GetMap(data, "cpuVoltage");
            System.Collections.ArrayList series = GetObjectList(cpuVid, "series");
            AssertEqual(true, Convert.ToBoolean(cpuVid["available"]), scenario + " cpu vid chart available");
            AssertEqual(8, series.Count, scenario + " cpu vid series count");
            AssertEqual(8, Convert.ToInt32(cpuVid["coreCount"]), scenario + " cpu vid core count");
            AssertEqual(false, Convert.ToBoolean(cpuVoltage["available"]), scenario + " vid should not make CPU Voltage / Vcore available");
            AssertEqual(0, GetObjectList(cpuVoltage, "series").Count, scenario + " CPU Voltage / Vcore should not receive VID series");

            for (int core = 0; core < 8; core++)
            {
                Dictionary<string, object> item = series[core] as Dictionary<string, object>;
                if (item == null) throw new Exception(scenario + " series item " + core.ToString(CultureInfo.InvariantCulture) + " missing");
                string expectedName = oneBasedHashNames ? "\u6838\u5fc3 #" + (core + 1).ToString(CultureInfo.InvariantCulture) + " VID" : "\u6838\u5fc3 " + core.ToString(CultureInfo.InvariantCulture) + " VID";
                AssertEqual(expectedName, Convert.ToString(item["name"]), scenario + " should localize the visible VID series name for core " + core.ToString(CultureInfo.InvariantCulture));
                AssertTrue(Convert.ToString(item["name"]).IndexOf("Core", StringComparison.OrdinalIgnoreCase) < 0, scenario + " visible VID series name should not remain English");
                AssertEqual("cpu-vid:" + core.ToString(CultureInfo.InvariantCulture), Convert.ToString(item["key"]), scenario + " series key for core " + core.ToString(CultureInfo.InvariantCulture));
                System.Collections.ArrayList values = item["data"] as System.Collections.ArrayList;
                if (values == null) throw new Exception(scenario + " series data missing for core " + core.ToString(CultureInfo.InvariantCulture));
                AssertEqual(0.975, Convert.ToDouble(values[0], CultureInfo.InvariantCulture), scenario + " equal VID value should remain attached to core " + core.ToString(CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void CpuVidOnlyDoesNotMakeCpuVoltagePerCoreAvailable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-cpu-vid-only-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 28, 10, 0, 0, DateTimeKind.Local);
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "cs2.exe" },
                    { "CpuVoltageAvailable", false },
                    { "CpuVoltageVcoreAvailable", false },
                    { "CpuVoltagePerCoreAvailable", false },
                    { "CpuVoltageStatus", "unavailable" },
                    { "CpuVidAvailable", true },
                    { "CpuVidStatus", "core-vid-available" },
                    { "CpuVidSampleCount", 1 },
                    { "CpuVidCoreCount", 1 }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,cs2,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-vid-samples.csv"),
                "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreIndex,PhysicalCoreId,ThreadIndex,VidVolts,Status,Reason,SensorIdentifier\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,builtin-librehardwaremonitor,built-in,Core #1 VID,0,0,0,0,,1.112,core-vid,VID is request/target voltage not real per-core Vcore,/intelcpu/0/voltage/0\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> cpuVid = GetMap(data, "cpuVid");
            Dictionary<string, object> cpuVoltage = GetMap(data, "cpuVoltage");
            AssertEqual(true, Convert.ToBoolean(cpuVid["available"]), "vid-only report should show cpu vid");
            AssertEqual(false, Convert.ToBoolean(cpuVoltage["available"]), "vid-only report should not show CPU Voltage / Vcore");
            AssertTrue(Convert.ToString(cpuVoltage["reason"]).IndexOf("CPU Vcore", StringComparison.OrdinalIgnoreCase) >= 0, "voltage no-data reason should require CPU Vcore");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void CpuPackageSocAndAggregateVcoreDoNotEnterCpuVidChart()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-cpu-vid-reject-aggregate-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 28, 11, 0, 0, DateTimeKind.Local);
            File.WriteAllText(Path.Combine(dir, "status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "Phase", "done" },
                    { "TargetProcess", "cs2.exe" },
                    { "CpuVoltageAvailable", false },
                    { "CpuVoltageVcoreAvailable", false },
                    { "CpuVoltageNonPerCoreAvailable", true },
                    { "CpuVoltageStatus", "non-per-core-only" },
                    { "CpuVidAvailable", false },
                    { "CpuVidStatus", "unavailable" },
                    { "CpuVidUnavailableReason", "未检测到 CPU 核心 VID 传感器；不生成假数据。" }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,cs2,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "cpu-voltage-samples.csv"),
                "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreId,PhysicalCoreId,ThreadIndex,VoltageVolts,Status,Reason,SensorIdentifier\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,builtin-librehardwaremonitor,built-in,Vcore,,,,,,1.104,non-per-core,aggregate Vcore is not per-core,/lpc/voltage/0\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,builtin-librehardwaremonitor,built-in,Vcore SoC,,,,,,1.020,non-per-core,SOC voltage is not per-core,/lpc/voltage/1\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,builtin-librehardwaremonitor,built-in,CPU Package,,,,,,1.180,non-per-core,package voltage is not per-core,/lpc/voltage/2\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> cpuVid = GetMap(data, "cpuVid");
            Dictionary<string, object> cpuVoltage = GetMap(data, "cpuVoltage");
            AssertEqual(false, Convert.ToBoolean(cpuVid["available"]), "Vcore/SOC/package voltage should not create cpu vid chart");
            AssertEqual(0, GetObjectList(cpuVid, "series").Count, "Vcore/SOC/package voltage should not create vid series");
            AssertEqual(true, Convert.ToBoolean(cpuVoltage["available"]), "explicit aggregate Vcore should create CPU Voltage chart data");
            AssertEqual("V", Convert.ToString(cpuVoltage["unit"]), "CPU Voltage unit");
            AssertEqual(1, Convert.ToInt32(cpuVoltage["vcoreSampleCount"]), "only explicit Vcore should count as vcore");
            AssertEqual(2, Convert.ToInt32(cpuVoltage["nonPerCoreSampleCount"]), "SOC/package voltage should remain classified separately");
            AssertEqual(1, GetObjectList(cpuVoltage, "series").Count, "CPU Voltage should have one aggregate Vcore series");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void NoCpuVidSensorUsesChineseReasonAndNoFakeData()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-cpu-vid-no-sensor-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 5, 28, 12, 0, 0, DateTimeKind.Local);
            File.WriteAllText(Path.Combine(dir, "cpu-vid-telemetry-status.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
                {
                    { "CpuVidAvailable", false },
                    { "CpuVidStatus", "provider-unavailable" },
                    { "CpuVidUnavailableReason", "未检测到 CPU 核心 VID 传感器；不生成假数据。" },
                    { "CpuVidSampleCount", 0 },
                    { "CpuVidCoreCount", 0 }
                }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "system-samples.csv"),
                "Time,SampleIndex,TargetRunning,TotalCpuPct,AvailableMB\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,True,10,16000\r\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "process-samples.csv"),
                "Time,SampleIndex,ElapsedMs,ProcessName,InstanceCount,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,WindowTitle,Pid\r\n"
                + start.ToString("o", CultureInfo.InvariantCulture) + ",0,0,cs2,1,1.0,512,0,0,,1234\r\n",
                Encoding.UTF8);

            FrameScopeReportGenerator.GenerateForTests(dir);

            Dictionary<string, object> manifest = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(dir, "charts", "framescope-interactive-manifest.json"), Encoding.UTF8));
            AssertEqual(false, Convert.ToBoolean(manifest["cpuVidAvailable"]), "manifest vid unavailable");
            AssertEqual(0, Convert.ToInt32(manifest["cpuVidSampleCount"]), "manifest vid sample count unavailable");
            AssertTrue(Convert.ToString(manifest["cpuVidReason"]).IndexOf("不生成假数据", StringComparison.OrdinalIgnoreCase) >= 0, "manifest vid reason should be Chinese");

            Dictionary<string, object> data = LoadReportData(Path.Combine(dir, "charts", "framescope-interactive-data.js"));
            Dictionary<string, object> cpuVid = GetMap(data, "cpuVid");
            AssertEqual(false, Convert.ToBoolean(cpuVid["available"]), "cpu vid chart unavailable");
            AssertEqual(0, GetObjectList(cpuVid, "series").Count, "cpu vid unavailable should not create fake series");
            AssertTrue(Convert.ToString(cpuVid["reason"]).IndexOf("不生成假数据", StringComparison.OrdinalIgnoreCase) >= 0, "cpu vid chart reason should be Chinese");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static string ExpectedAmdLhmCpuVidUnavailableReason()
    {
        return "\u8fd9\u4e0d\u662f\u8f6f\u4ef6\u6f0f\u753b\u56fe\u3002\u5f53\u524d\u786c\u4ef6\u7684 AMD LibreHardwareMonitor Core VID \u6765\u6e90\u4e0d\u53ef\u4fe1\uff0c\u5df2\u505c\u6b62\u663e\u793a\u8be5\u9519\u8bef VID\uff08\u7ea6 0.5V\uff09\u3002CPU Voltage / Vcore \u4ecd\u53ef\u5728 CPU \u7535\u538b / Vcore \u56fe\u8868\u4e2d\u67e5\u770b\uff1bVcore \u4e0d\u4f1a\u5192\u5145 VID\u3002\u672a\u6765\u68c0\u6d4b\u5230\u5408\u6cd5 VID \u6765\u6e90\u65f6\uff0cCPU Core VID \u56fe\u8868\u4f1a\u6b63\u5e38\u663e\u793a\u3002";
    }

    private static void AssertAsciiOnly(string value, string label)
    {
        foreach (char ch in value)
        {
            if (ch > 0x7f) throw new Exception(label + ": found non-ASCII char U+" + ((int)ch).ToString("X4"));
        }
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

    private static void AssertNoNumericZero(System.Collections.ArrayList values, string label)
    {
        foreach (object value in values)
        {
            if (value == null) continue;
            if (Convert.ToDouble(value, CultureInfo.InvariantCulture) == 0)
            {
                throw new Exception(label + ": found numeric zero");
            }
        }
    }

    private static Dictionary<string, object> LoadReportData(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8).Trim();
        const string prefix = "window.FRAMESCOPE_DATA = ";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)) throw new Exception("report data prefix");
        if (text.EndsWith(";", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
        text = text.Substring(prefix.Length);
        return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(text);
    }

    private static Dictionary<string, object> GetMap(Dictionary<string, object> map, string key)
    {
        Dictionary<string, object> result = map[key] as Dictionary<string, object>;
        if (result == null) throw new Exception(key + " map missing");
        return result;
    }

    private static System.Collections.ArrayList GetObjectList(Dictionary<string, object> map, string key)
    {
        System.Collections.ArrayList result = map[key] as System.Collections.ArrayList;
        if (result == null) throw new Exception(key + " list missing");
        return result;
    }

    private static List<double?> DecodeRleSeries(string encoded, int expectedCount)
    {
        List<double?> values = new List<double?>();
        foreach (string token in (encoded ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = token.Split('*');
            int count = 1;
            string valueText = token;
            if (parts.Length == 2)
            {
                count = Convert.ToInt32(parts[0], CultureInfo.InvariantCulture);
                valueText = parts[1];
            }
            double? value = valueText == "n"
                ? (double?)null
                : Convert.ToDouble(valueText, CultureInfo.InvariantCulture);
            for (int i = 0; i < count; i++) values.Add(value);
        }
        AssertEqual(expectedCount, values.Count, "decoded RLE value count");
        return values;
    }
}
