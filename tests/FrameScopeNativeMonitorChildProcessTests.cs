using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal static partial class FrameScopeNativeMonitor
{
    public static int Main(string[] args)
    {
        if (args != null && args.Length > 0 && string.Equals(args[0], "--short-lived-stderr-child", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("error: failed to start trace session: access denied.");
            Console.Error.WriteLine("       PresentMon requires either administrative privileges or to be run by a user in the");
            Console.Error.WriteLine("       \"Performance Log Users\" user group.  View the readme for more details.");
            for (int i = 0; i < 200000; i++)
            {
                Console.Error.WriteLine("stderr filler line " + i.ToString("D6") + " access denied pipe drain regression payload");
            }
            Console.Error.WriteLine("error: failed to start trace session: access denied.");
            Console.Error.WriteLine("       PresentMon requires either administrative privileges or to be run by a user in the");
            Console.Error.WriteLine("       \"Performance Log Users\" user group.  View the readme for more details.");
            return 6;
        }
        if (args != null && args.Length > 0 && string.Equals(args[0], "--fake-target", StringComparison.OrdinalIgnoreCase))
        {
            int seconds = args.Length > 1 ? int.Parse(args[1]) : 8;
            Thread.Sleep(seconds * 1000);
            return 0;
        }
        if (LooksLikePresentMonInvocation(args) && IsSilentNoCsvPresentMonInvocation(args))
        {
            Console.Out.WriteLine("Started recording.");
            return 0;
        }
        if (LooksLikePresentMonInvocation(args))
        {
            Console.Error.WriteLine("error: failed to start trace session: access denied.");
            Console.Error.WriteLine("       PresentMon requires either administrative privileges or to be run by a user in the");
            Console.Error.WriteLine("       \"Performance Log Users\" user group.  View the readme for more details.");
            return 6;
        }
        if (HasTestArg(args, "--system-csv"))
        {
            return RunFakeUnavailableSystemSampler(args);
        }

        try
        {
        ShortLivedChildStderrIsAvailableAfterExit();
        ShortLivedChildStderrWorksAtWindowsPathLimit();
        MonitorSessionClassifiesShortLivedPresentMonStderr();
        MonitorSessionClassifiesSilentNoCsvPresentMonExitZero();
        MonitorSessionCanWriteCpuCoreTelemetryArtifact();
        MonitorSessionWritesCpuVidTelemetryWithoutCpuVoltageArtifacts();
        MonitorSessionKeepsSuccessWhenCpuCoreCounterUnavailable();
        MonitorSessionCpuCoreTelemetryDisabledDoesNotCreateNoiseFile();
        Console.WriteLine("FrameScopeNativeMonitorChildProcessTests: PASS");
        return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace ?? "");
            return 1;
        }
    }

    private static void ShortLivedChildStderrIsAvailableAfterExit()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-child-pipe-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string stdout = Path.Combine(dir, "presentmon.stdout.log");
            string stderr = Path.Combine(dir, "presentmon.stderr.log");
            string currentExe = Assembly.GetExecutingAssembly().Location;

            Process child = StartNativeMonitorChild(
                currentExe,
                "--short-lived-stderr-child",
                dir,
                stdout,
                stderr,
                ProcessPriorityClass.Normal);

            AssertTrue(child != null, "child process started");
            AssertTrue(WaitForNativeMonitorChildExit(child, 30000, 30000), "short-lived child exited");
            AssertEqual(6, child.ExitCode, "child exit code");

            AssertTrue(File.Exists(stderr), "stderr log exists after child exit");
            string text = File.ReadAllText(stderr, Encoding.UTF8);
            AssertTrue(text.Contains("failed to start trace session: access denied"), "stderr contains ETW access denied");
            AssertTrue(text.Contains("Performance Log Users"), "stderr contains permission guidance");

            Dictionary<string, object> diagnostics = FrameScopePresentMonDiagnostics.BuildCaptureDiagnostics(
                Path.Combine(dir, "presentmon.csv"),
                stdout,
                stderr,
                6,
                true,
                false);

            AssertEqual("presentmon-etw-access-denied", Convert.ToString(diagnostics["FrameCaptureStatus"]), "capture status");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void MonitorSessionClassifiesShortLivedPresentMonStderr()
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        string monitorExe = Path.Combine(root, "FrameScopeMonitor.exe");
        string processSamplerExe = Path.Combine(root, "FrameScopeProcessSampler.exe");
        string systemSamplerExe = Path.Combine(root, "FrameScopeSystemSampler.exe");
        string currentExe = Assembly.GetExecutingAssembly().Location;
        AssertTrue(File.Exists(monitorExe), "FrameScopeMonitor.exe exists");
        AssertTrue(File.Exists(processSamplerExe), "FrameScopeProcessSampler.exe exists");
        AssertTrue(File.Exists(systemSamplerExe), "FrameScopeSystemSampler.exe exists");

        string dir = Path.Combine(Path.GetTempPath(), "framescope-monitor-session-stderr-tests-" + Guid.NewGuid().ToString("N"));
        string runRoot = Path.Combine(dir, "runs");
        Directory.CreateDirectory(runRoot);
        Process target = null;
        try
        {
            target = Process.Start(new ProcessStartInfo
            {
                FileName = currentExe,
                Arguments = "--fake-target 8",
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            AssertTrue(target != null, "fake target started");
            Thread.Sleep(250);

            string arguments = JoinTestArguments(new[]
            {
                "--monitor-session",
                "--WaitSeconds", "8",
                "--CaptureSeconds", "2",
                "--SampleIntervalMs", "100",
                "--ProcessSampleIntervalMs", "100",
                "--ControlPollIntervalMs", "1000",
                "--RunRoot", runRoot,
                "--RunNamePrefix", "StderrRegression",
                "--TargetProcessName", Path.GetFileName(currentExe),
                "--TargetProcessAliases", Path.GetFileNameWithoutExtension(currentExe),
                "--InitialTargetPid", target.Id.ToString(),
                "--PresentMonExe", currentExe,
                "--ProcessSamplerExe", processSamplerExe,
                "--SystemSamplerExe", systemSamplerExe
            });

            Process monitor = Process.Start(new ProcessStartInfo
            {
                FileName = monitorExe,
                Arguments = arguments,
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            AssertTrue(monitor != null, "monitor process started");
            AssertTrue(monitor.WaitForExit(30000), "monitor process exited");
            AssertEqual(0, monitor.ExitCode, "monitor exit code");

            string runDir = FindNewestDirectory(runRoot);
            AssertTrue(!string.IsNullOrWhiteSpace(runDir), "monitor session run directory");
            string stderr = Path.Combine(runDir, "presentmon.stderr.log");
            AssertTrue(File.Exists(stderr), "presentmon stderr log exists");
            string stderrText = File.ReadAllText(stderr, Encoding.UTF8);
            AssertTrue(stderrText.Contains("failed to start trace session: access denied"), "presentmon stderr access denied text");

            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> status = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "status.json"), Encoding.UTF8));
            Dictionary<string, object> summary = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "summary.json"), Encoding.UTF8));

            AssertEqual("presentmon-etw-access-denied", Convert.ToString(status["FrameCaptureStatus"]), "status capture status");
            AssertEqual("presentmon-etw-access-denied", Convert.ToString(summary["FrameCaptureStatus"]), "summary capture status");
            AssertEqual("presentmon-etw-access-denied", Convert.ToString(status["PresentMonFailureCategory"]), "status failure category");
            AssertEqual("presentmon-etw-access-denied", Convert.ToString(summary["PresentMonFailureCategory"]), "summary failure category");
            AssertEqual(true, Convert.ToBoolean(status["PresentMonEtwAccessDenied"]), "status ETW flag");
            AssertEqual(true, Convert.ToBoolean(summary["PresentMonEtwAccessDenied"]), "summary ETW flag");
            AssertTrue(Convert.ToString(status["PresentMonStderrTail"]).Contains("failed to start trace session: access denied"), "status stderr tail");
            AssertTrue(Convert.ToString(summary["PresentMonStderrTail"]).Contains("failed to start trace session: access denied"), "summary stderr tail");
        }
        finally
        {
            try
            {
                if (target != null && !target.HasExited)
                {
                    target.Kill();
                    target.WaitForExit(3000);
                }
            }
            catch { }
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void MonitorSessionClassifiesSilentNoCsvPresentMonExitZero()
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        string monitorExe = Path.Combine(root, "FrameScopeMonitor.exe");
        string processSamplerExe = Path.Combine(root, "FrameScopeProcessSampler.exe");
        string systemSamplerExe = Path.Combine(root, "FrameScopeSystemSampler.exe");
        string currentExe = Assembly.GetExecutingAssembly().Location;
        AssertTrue(File.Exists(monitorExe), "FrameScopeMonitor.exe exists");
        AssertTrue(File.Exists(processSamplerExe), "FrameScopeProcessSampler.exe exists");
        AssertTrue(File.Exists(systemSamplerExe), "FrameScopeSystemSampler.exe exists");

        string dir = Path.Combine(Path.GetTempPath(), "framescope-monitor-session-silent-no-csv-tests-" + Guid.NewGuid().ToString("N"));
        string runRoot = Path.Combine(dir, "runs");
        Directory.CreateDirectory(runRoot);
        Process target = null;
        try
        {
            target = Process.Start(new ProcessStartInfo
            {
                FileName = currentExe,
                Arguments = "--fake-target 8",
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            AssertTrue(target != null, "fake target started");
            Thread.Sleep(250);

            string arguments = JoinTestArguments(new[]
            {
                "--monitor-session",
                "--WaitSeconds", "8",
                "--CaptureSeconds", "2",
                "--SampleIntervalMs", "100",
                "--ProcessSampleIntervalMs", "100",
                "--ControlPollIntervalMs", "1000",
                "--RunRoot", runRoot,
                "--RunNamePrefix", "SilentNoCsvRegression",
                "--TargetProcessName", Path.GetFileName(currentExe),
                "--TargetProcessAliases", Path.GetFileNameWithoutExtension(currentExe),
                "--InitialTargetPid", target.Id.ToString(),
                "--PresentMonExe", currentExe,
                "--ProcessSamplerExe", processSamplerExe,
                "--SystemSamplerExe", systemSamplerExe
            });

            Process monitor = Process.Start(new ProcessStartInfo
            {
                FileName = monitorExe,
                Arguments = arguments,
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            AssertTrue(monitor != null, "monitor process started");
            AssertTrue(monitor.WaitForExit(30000), "monitor process exited");
            AssertEqual(0, monitor.ExitCode, "monitor exit code");

            string runDir = FindNewestDirectory(runRoot);
            AssertTrue(!string.IsNullOrWhiteSpace(runDir), "monitor session run directory");
            AssertTrue(!File.Exists(Path.Combine(runDir, "presentmon.csv")), "silent fake PresentMon should not create CSV");
            string stdout = File.ReadAllText(Path.Combine(runDir, "presentmon.stdout.log"), Encoding.UTF8);
            string stderr = File.ReadAllText(Path.Combine(runDir, "presentmon.stderr.log"), Encoding.UTF8);
            AssertTrue(stdout.Contains("Started recording"), "silent no-csv stdout");
            AssertEqual("", stderr.Trim(), "silent no-csv stderr");

            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> status = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "status.json"), Encoding.UTF8));
            Dictionary<string, object> summary = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "summary.json"), Encoding.UTF8));

            AssertEqual("presentmon-no-csv-silent", Convert.ToString(status["FrameCaptureStatus"]), "status silent no-csv capture status");
            AssertEqual("presentmon-no-csv-silent", Convert.ToString(summary["FrameCaptureStatus"]), "summary silent no-csv capture status");
            AssertEqual("presentmon-no-csv-silent", Convert.ToString(status["PresentMonFailureCategory"]), "status silent no-csv category");
            AssertEqual(false, Convert.ToBoolean(status["PresentMonEtwAccessDenied"]), "status silent no-csv ETW flag");
            AssertEqual(0, Convert.ToInt32(status["PresentMonExitCode"]), "status PresentMon exit code");
            AssertTrue(Convert.ToInt64(status["PresentMonRuntimeMs"]) >= 0, "status PresentMon runtime should be recorded");
            AssertTrue(Convert.ToString(status["PresentMonCsvPath"]).EndsWith("presentmon.csv", StringComparison.OrdinalIgnoreCase), "status CSV path");
            AssertTrue(!String.IsNullOrWhiteSpace(Convert.ToString(status["PresentMonCsvLastCheckTime"])), "status CSV last check time");
            AssertTrue(status.ContainsKey("TargetRunningAtPresentMonExitCheck"), "status target running at PresentMon exit check");
            AssertTrue(Convert.ToString(status["PresentMonArgs"]).Contains("--output_file"), "status PresentMon args");
            AssertTrue(Convert.ToString(status["FrameCaptureMessage"]).IndexOf("PUBG", StringComparison.OrdinalIgnoreCase) < 0, "silent no-csv message must not mention PUBG");
        }
        finally
        {
            try
            {
                if (target != null && !target.HasExited)
                {
                    target.Kill();
                    target.WaitForExit(3000);
                }
            }
            catch { }
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void MonitorSessionCanWriteCpuCoreTelemetryArtifact()
    {
        string runDir = RunSyntheticMonitorSession("CpuCoreEnabled", true);
        try
        {
            string cpuCoreCsv = Path.Combine(runDir, "cpu-core-samples.csv");
            AssertTrue(File.Exists(cpuCoreCsv), "cpu-core-samples.csv exists");
            string[] lines = File.ReadAllLines(cpuCoreCsv, Encoding.UTF8);
            AssertTrue(lines.Length >= 2, "cpu-core-samples.csv should contain data rows");
            AssertTrue(lines[0].StartsWith("Time,SampleIndex,ElapsedMs,Source,ProcessorGroup,LogicalProcessor", StringComparison.Ordinal), "cpu-core header shape");

            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> status = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "status.json"), Encoding.UTF8));
            Dictionary<string, object> summary = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "summary.json"), Encoding.UTF8));

            AssertEqual(true, Convert.ToBoolean(status["CpuCoreTelemetryEnabled"]), "status cpu telemetry enabled");
            AssertEqual(true, Convert.ToBoolean(summary["CpuCoreTelemetryEnabled"]), "summary cpu telemetry enabled");
            AssertTrue(Convert.ToInt32(status["CpuCoreSampleCount"]) > 0, "status cpu core sample count");
            AssertTrue(Convert.ToInt32(summary["CpuCoreSampleCount"]) > 0, "summary cpu core sample count");
            AssertEqual(false, Convert.ToBoolean(status["CpuVoltageAvailable"]), "status voltage availability");
            AssertEqual(false, Convert.ToBoolean(summary["CpuVoltageAvailable"]), "summary voltage availability");
            AssertEqual("unavailable", Convert.ToString(status["CpuVoltageStatus"]), "status voltage status");
            AssertEqual("unavailable", Convert.ToString(summary["CpuVoltageStatus"]), "summary voltage status");
        }
        finally
        {
            TryDeleteTopTempRun(runDir);
        }
    }

    private static void MonitorSessionCpuCoreTelemetryDisabledDoesNotCreateNoiseFile()
    {
        string runDir = RunSyntheticMonitorSession("CpuCoreDisabled", false);
        try
        {
            AssertTrue(!File.Exists(Path.Combine(runDir, "cpu-core-samples.csv")), "disabled session should not create cpu-core-samples.csv");
            AssertTrue(!File.Exists(Path.Combine(runDir, "cpu-core-telemetry-status.json")), "disabled session should not create cpu core status sidecar");

            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> status = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "status.json"), Encoding.UTF8));
            Dictionary<string, object> summary = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "summary.json"), Encoding.UTF8));
            AssertEqual(false, Convert.ToBoolean(status["CpuCoreTelemetryEnabled"]), "status cpu telemetry disabled");
            AssertEqual(false, Convert.ToBoolean(summary["CpuCoreTelemetryEnabled"]), "summary cpu telemetry disabled");
            AssertEqual(0, Convert.ToInt32(status["CpuCoreSampleCount"]), "status disabled sample count");
            AssertEqual(0, Convert.ToInt32(summary["CpuCoreSampleCount"]), "summary disabled sample count");
        }
        finally
        {
            TryDeleteTopTempRun(runDir);
        }
    }

    private static void MonitorSessionWritesCpuVidTelemetryWithoutCpuVoltageArtifacts()
    {
        string runDir = RunSyntheticMonitorSession("CpuVidEnabled", true, Assembly.GetExecutingAssembly().Location, true);
        try
        {
            string cpuVoltageCsv = Path.Combine(runDir, "cpu-voltage-samples.csv");
            string cpuVidCsv = Path.Combine(runDir, "cpu-vid-samples.csv");
            AssertTrue(!File.Exists(cpuVoltageCsv), "new monitor session should not create cpu-voltage-samples.csv");
            AssertTrue(!File.Exists(Path.Combine(runDir, "cpu-voltage-telemetry-status.json")), "new monitor session should not create cpu-voltage-telemetry-status.json");
            AssertTrue(File.Exists(cpuVidCsv), "cpu-vid-samples.csv exists");
            string[] vidLines = File.ReadAllLines(cpuVidCsv, Encoding.UTF8);
            AssertTrue(vidLines.Length >= 2, "cpu-vid-samples.csv should contain data rows");
            AssertTrue(vidLines[0].StartsWith("Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreIndex", StringComparison.Ordinal), "cpu-vid header shape");

            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> status = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "status.json"), Encoding.UTF8));
            Dictionary<string, object> summary = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "summary.json"), Encoding.UTF8));

            AssertEqual(false, Convert.ToBoolean(status["CpuVoltageTelemetryEnabled"]), "status real voltage telemetry disabled");
            AssertEqual(false, Convert.ToBoolean(summary["CpuVoltageTelemetryEnabled"]), "summary real voltage telemetry disabled");
            AssertEqual(false, Convert.ToBoolean(status["CpuVoltageAvailable"]), "status voltage unavailable");
            AssertEqual(false, Convert.ToBoolean(summary["CpuVoltageAvailable"]), "summary voltage unavailable");
            AssertEqual(0, Convert.ToInt32(status["CpuVoltageSampleCount"]), "status voltage sample count");
            AssertEqual(true, Convert.ToBoolean(status["CpuVidAvailable"]), "status vid available");
            AssertEqual(true, Convert.ToBoolean(summary["CpuVidAvailable"]), "summary vid available");
            AssertEqual(true, Convert.ToBoolean(status["CpuVidTelemetryEnabled"]), "status vid telemetry enabled");
            AssertEqual(true, Convert.ToBoolean(summary["CpuVidTelemetryEnabled"]), "summary vid telemetry enabled");
            AssertEqual("core-vid-available", Convert.ToString(status["CpuVidStatus"]), "status vid status");
            AssertEqual("synthetic-vid", Convert.ToString(status["CpuVidSource"]), "status vid source");
            AssertTrue(Convert.ToInt32(status["CpuVidSampleCount"]) > 0, "status vid sample count");
            AssertEqual(2, Convert.ToInt32(status["CpuVidCoreCount"]), "status vid core count");
        }
        finally
        {
            TryDeleteTopTempRun(runDir);
        }
    }

    private static void MonitorSessionKeepsSuccessWhenCpuCoreCounterUnavailable()
    {
        string runDir = RunSyntheticMonitorSession("CpuCoreUnavailable", true, Assembly.GetExecutingAssembly().Location);
        try
        {
            AssertTrue(!File.Exists(Path.Combine(runDir, "cpu-core-samples.csv")), "unavailable counter should not create cpu-core-samples.csv");

            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> status = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "status.json"), Encoding.UTF8));
            Dictionary<string, object> summary = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "summary.json"), Encoding.UTF8));
            AssertEqual(false, Convert.ToBoolean(status["CpuCoreTelemetryAvailable"]), "status cpu telemetry unavailable");
            AssertEqual(false, Convert.ToBoolean(summary["CpuCoreTelemetryAvailable"]), "summary cpu telemetry unavailable");
            AssertEqual("Actual Frequency unavailable for integration test", Convert.ToString(status["CpuCoreTelemetryUnavailableReason"]), "status unavailable reason");
            AssertEqual("Actual Frequency unavailable for integration test", Convert.ToString(summary["CpuCoreTelemetryUnavailableReason"]), "summary unavailable reason");
            AssertEqual(false, Convert.ToBoolean(status["CpuVoltageAvailable"]), "status voltage unavailable");
            AssertEqual(false, Convert.ToBoolean(summary["CpuVoltageAvailable"]), "summary voltage unavailable");
        }
        finally
        {
            TryDeleteTopTempRun(runDir);
        }
    }

    private static string RunSyntheticMonitorSession(string prefix, bool enableCpuCoreTelemetry)
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        return RunSyntheticMonitorSession(prefix, enableCpuCoreTelemetry, Path.Combine(root, "FrameScopeSystemSampler.exe"));
    }

    private static string RunSyntheticMonitorSession(string prefix, bool enableCpuCoreTelemetry, string systemSamplerExe)
    {
        return RunSyntheticMonitorSession(prefix, enableCpuCoreTelemetry, systemSamplerExe, false);
    }

    private static string RunSyntheticMonitorSession(string prefix, bool enableCpuCoreTelemetry, string systemSamplerExe, bool enableCpuVidTelemetry)
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        string monitorExe = Path.Combine(root, "FrameScopeMonitor.exe");
        string processSamplerExe = Path.Combine(root, "FrameScopeProcessSampler.exe");
        string currentExe = Assembly.GetExecutingAssembly().Location;
        AssertTrue(File.Exists(monitorExe), "FrameScopeMonitor.exe exists");
        AssertTrue(File.Exists(processSamplerExe), "FrameScopeProcessSampler.exe exists");
        AssertTrue(File.Exists(systemSamplerExe), "FrameScopeSystemSampler.exe exists");

        string dir = Path.Combine(Path.GetTempPath(), "framescope-monitor-session-cpu-core-tests-" + Guid.NewGuid().ToString("N"));
        string runRoot = Path.Combine(dir, "runs");
        Directory.CreateDirectory(runRoot);
        Process target = null;
        try
        {
            target = Process.Start(new ProcessStartInfo
            {
                FileName = currentExe,
                Arguments = "--fake-target 7",
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            AssertTrue(target != null, "fake target started");
            Thread.Sleep(250);

            string arguments = JoinTestArguments(new[]
            {
                "--monitor-session",
                "--WaitSeconds", "8",
                "--CaptureSeconds", "2",
                "--SampleIntervalMs", "100",
                "--ProcessSampleIntervalMs", "100",
                "--SlowSampleIntervalMs", "1000",
                "--EnableCpuCoreTelemetry", enableCpuCoreTelemetry ? "true" : "false",
                "--CpuCoreSampleIntervalMs", "500",
                "--EnableCpuVidTelemetry", enableCpuVidTelemetry ? "true" : "false",
                "--CpuVidSampleIntervalMs", "500",
                "--ControlPollIntervalMs", "1000",
                "--RunRoot", runRoot,
                "--RunNamePrefix", prefix,
                "--TargetProcessName", Path.GetFileName(currentExe),
                "--TargetProcessAliases", Path.GetFileNameWithoutExtension(currentExe),
                "--InitialTargetPid", target.Id.ToString(),
                "--PresentMonExe", currentExe,
                "--ProcessSamplerExe", processSamplerExe,
                "--SystemSamplerExe", systemSamplerExe
            });

            Process monitor = Process.Start(new ProcessStartInfo
            {
                FileName = monitorExe,
                Arguments = arguments,
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            AssertTrue(monitor != null, "monitor process started");
            AssertTrue(monitor.WaitForExit(30000), "monitor process exited");
            AssertEqual(0, monitor.ExitCode, "monitor exit code");

            string runDir = FindNewestDirectory(runRoot);
            AssertTrue(!string.IsNullOrWhiteSpace(runDir), "monitor session run directory");
            return runDir;
        }
        catch
        {
            try { Directory.Delete(dir, true); }
            catch { }
            throw;
        }
        finally
        {
            try
            {
                if (target != null && !target.HasExited)
                {
                    target.Kill();
                    target.WaitForExit(3000);
                }
            }
            catch { }
        }
    }

    private static void ShortLivedChildStderrWorksAtWindowsPathLimit()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "framescope-child-long-path-tests-" + Guid.NewGuid().ToString("N"));
        string runDir = baseDir;
        while (Path.Combine(runDir, "presentmon.stderr.log").Length < 260)
        {
            runDir = Path.Combine(runDir, "segment");
        }

        Directory.CreateDirectory(runDir);
        try
        {
            string stdout = Path.Combine(runDir, "presentmon.stdout.log");
            string stderr = Path.Combine(runDir, "presentmon.stderr.log");
            AssertTrue(stderr.Length >= 260, "stderr path length reaches Windows legacy limit");
            FrameScopePresentMonDiagnostics.WriteAllText(stderr, "direct long path access denied", Encoding.UTF8);
            AssertTrue(FrameScopePresentMonDiagnostics.FileExists(stderr), "direct long-path write exists");
            AssertTrue(FrameScopePresentMonDiagnostics.ReadAllText(stderr, Encoding.UTF8).Contains("access denied"), "direct long-path read");
            try { File.Delete(@"\\?\" + stderr); } catch { }
            string currentExe = Assembly.GetExecutingAssembly().Location;

            Process child = StartNativeMonitorChild(
                currentExe,
                "--short-lived-stderr-child",
                runDir,
                stdout,
                stderr,
                ProcessPriorityClass.Normal);

            AssertTrue(child != null, "long-path child process started");
            AssertTrue(WaitForNativeMonitorChildExit(child, 30000, 30000), "long-path child exited");
            AssertEqual(6, child.ExitCode, "long-path child exit code");

            AssertTrue(FrameScopePresentMonDiagnostics.FileExists(stderr), "long-path stderr log exists after child exit");
            string text = FrameScopePresentMonDiagnostics.ReadAllText(stderr, Encoding.UTF8);
            AssertTrue(text.Contains("failed to start trace session: access denied"), "long-path stderr contains ETW access denied");

            Dictionary<string, object> diagnostics = FrameScopePresentMonDiagnostics.BuildCaptureDiagnostics(
                Path.Combine(runDir, "presentmon.csv"),
                stdout,
                stderr,
                6,
                true,
                false);

            AssertEqual("presentmon-etw-access-denied", Convert.ToString(diagnostics["FrameCaptureStatus"]), "long-path capture status");
        }
        finally
        {
            try { Directory.Delete(baseDir, true); }
            catch { }
        }
    }

    private static bool LooksLikePresentMonInvocation(string[] args)
    {
        if (args == null) return false;
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--process_id", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(arg, "--process_name", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(arg, "--output_file", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsSilentNoCsvPresentMonInvocation(string[] args)
    {
        if (args == null) return false;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--session_name", StringComparison.OrdinalIgnoreCase)
                && args[i + 1].IndexOf("SilentNoCsv", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasTestArg(string[] args, string name)
    {
        if (args == null) return false;
        foreach (string arg in args)
        {
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string GetTestArg(string[] args, string name, string fallback)
    {
        if (args == null) return fallback;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return fallback;
    }

    private static int RunFakeUnavailableSystemSampler(string[] args)
    {
        string systemCsv = GetTestArg(args, "--system-csv", "system-samples.csv");
        string cpuCoreStatus = GetTestArg(args, "--cpu-core-status", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(systemCsv)) ?? "", "cpu-core-telemetry-status.json"));
        string cpuVoltageCsv = GetTestArg(args, "--cpu-voltage-csv", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(systemCsv)) ?? "", "cpu-voltage-samples.csv"));
        string cpuVidCsv = GetTestArg(args, "--cpu-vid-csv", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(systemCsv)) ?? "", "cpu-vid-samples.csv"));
        string cpuVidStatus = GetTestArg(args, "--cpu-vid-status", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(systemCsv)) ?? "", "cpu-vid-telemetry-status.json"));
        bool enableCpuVid = string.Equals(GetTestArg(args, "--enable-cpu-vid-telemetry", "false"), "true", StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(systemCsv)));
        File.WriteAllText(systemCsv,
            "Time,SampleIndex,Cs2Running,TargetRunning,TotalCpuPct,CpuFrequencyMHz,CpuPerformancePct,AvailableMB,DiskAvgSecPerTransfer,DiskBytesPerSec,NetBytesPerSec,GpuUtilPct,GpuMemUtilPct,GpuTempC,GpuPState,GpuClockMHz,MemClockMHz,PowerW,VramUsedMiB,VramTotalMiB,ProcessCount\r\n"
            + DateTime.Now.ToString("o") + ",0,False,True,1,4200,100,16000,,,,,,,,,,,,,1\r\n",
            Encoding.UTF8);
        if (enableCpuVid)
        {
            File.WriteAllText(cpuVidCsv,
                "Time,SampleIndex,ElapsedMs,Source,Provider,SensorName,ProcessorGroup,LogicalProcessor,CoreIndex,PhysicalCoreId,ThreadIndex,VidVolts,Status,Reason,SensorIdentifier\r\n"
                + DateTime.Now.ToString("o") + ",0,0,synthetic-vid,synthetic,Core #1 VID,0,0,0,0,,1.112,core-vid,VID is request voltage not real Vcore,/test/vid/0\r\n"
                + DateTime.Now.ToString("o") + ",0,0,synthetic-vid,synthetic,Core #2 VID,0,1,1,1,,1.087,core-vid,VID is request voltage not real Vcore,/test/vid/1\r\n",
                Encoding.UTF8);
            File.WriteAllText(cpuVidStatus,
                "{\"CpuVidTelemetryEnabled\":true,\"CpuVidAvailable\":true,\"CpuVidStatus\":\"core-vid-available\",\"CpuVidSource\":\"synthetic-vid\",\"CpuVidProviderKind\":\"synthetic\",\"CpuVidSampleIntervalMs\":500,\"CpuVidSampleCount\":2,\"CpuVidCoreCount\":2,\"CpuVidNote\":\"VID is request/target voltage, not real per-core Vcore.\"}",
                Encoding.UTF8);
            File.WriteAllText(cpuCoreStatus,
                "{\"Enabled\":true,\"CpuCoreTelemetryAvailable\":false,\"CpuCoreTelemetryUnavailableReason\":\"Actual Frequency unavailable for integration test\",\"CpuCoreSampleIntervalMs\":500,\"CpuCoreSampleCount\":0,\"CpuCoreLogicalProcessorCount\":0,\"CpuVoltageTelemetryEnabled\":false,\"CpuVoltageAvailable\":false,\"CpuVoltageStatus\":\"unavailable\",\"CpuVoltageSampleCount\":0}",
                Encoding.UTF8);
        }
        else
        {
            File.WriteAllText(cpuCoreStatus,
                "{\"Enabled\":true,\"CpuCoreTelemetryAvailable\":false,\"CpuCoreTelemetryUnavailableReason\":\"Actual Frequency unavailable for integration test\",\"CpuCoreSampleIntervalMs\":500,\"CpuCoreSampleCount\":0,\"CpuCoreLogicalProcessorCount\":0,\"CpuVoltageTelemetryEnabled\":false,\"CpuVoltageAvailable\":false,\"CpuVoltageStatus\":\"unavailable\"}",
                Encoding.UTF8);
        }
        Thread.Sleep(3000);
        return 0;
    }

    private static string FindNewestDirectory(string root)
    {
        DirectoryInfo newest = null;
        foreach (DirectoryInfo directory in new DirectoryInfo(root).GetDirectories())
        {
            if (newest == null || directory.LastWriteTimeUtc > newest.LastWriteTimeUtc) newest = directory;
        }
        return newest == null ? "" : newest.FullName;
    }

    private static void TryDeleteTopTempRun(string runDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(runDir)) return;
            DirectoryInfo dir = new DirectoryInfo(runDir);
            DirectoryInfo runs = dir.Parent;
            DirectoryInfo root = runs == null ? null : runs.Parent;
            if (root != null && root.Name.StartsWith("framescope-monitor-session-cpu-core-tests-", StringComparison.OrdinalIgnoreCase))
            {
                root.Delete(true);
            }
        }
        catch { }
    }

    private static string JoinTestArguments(string[] args)
    {
        StringBuilder builder = new StringBuilder();
        foreach (string arg in args)
        {
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(QuoteTestArgument(arg));
        }
        return builder.ToString();
    }

    private static string QuoteTestArgument(string value)
    {
        if (value == null) value = "";
        if (value.Length == 0) return "\"\"";
        if (value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) < 0) return value;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
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
