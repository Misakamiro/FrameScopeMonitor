using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal static partial class FrameScopeNativeMonitor
{
    // Contract: each redirected child-process log is capped at 1 MiB on disk.
    private const int ExpectedNativeMonitorChildLogMaxBytes = 1024 * 1024;
    private const string BoundedStdoutFinalMarker = "FRAMESCOPE_STDOUT_FINAL_MARKER";
    private const string BoundedStderrFinalMarker = "FRAMESCOPE_STDERR_FINAL_MARKER";

    public static int Main(string[] args)
    {
        if (args != null && args.Length > 0 && string.Equals(args[0], "--bounded-pipe-child", StringComparison.OrdinalIgnoreCase))
        {
            string stdoutLine = "stdout payload " + new string('o', 4096);
            string stderrLine = "stderr payload " + new string('e', 4096);
            for (int i = 0; i < 600; i++)
            {
                Console.Out.WriteLine(stdoutLine);
                Console.Error.WriteLine(stderrLine);
            }
            Console.Out.WriteLine(BoundedStdoutFinalMarker);
            Console.Error.WriteLine(BoundedStderrFinalMarker);
            return 17;
        }
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
        if (HasTestArg(args, "--terminate_existing_session") && IsSamplerFailurePresentMonInvocation(args))
        {
            File.WriteAllText(SamplerFailurePresentMonStopPath(args), DateTime.Now.ToString("o"), Encoding.UTF8);
            return 0;
        }
        if (LooksLikePresentMonInvocation(args) && IsSamplerFailurePresentMonInvocation(args))
        {
            return RunSamplerFailureFakePresentMon(args);
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
        if (HasTestArg(args, "--pipe-join-regression-only"))
        {
            TimedOutPipeThreadRemainsRegisteredUntilDrainCompletes();
            NativeMonitorChildDisposeDrainsOutputFirst();
            Console.WriteLine("FrameScopeNativeMonitorChildProcessTests pipe join: PASS");
            return 0;
        }
        if (HasTestArg(args, "--long-path-regression-only"))
        {
            ShortLivedChildStderrWorksAtWindowsPathLimit();
            Console.WriteLine("FrameScopeNativeMonitorChildProcessTests long path: PASS");
            return 0;
        }
        TimedOutPipeThreadRemainsRegisteredUntilDrainCompletes();
        NativeMonitorChildDisposeDrainsOutputFirst();
        ChildPipeLogsAreBoundedAndKeepFinalOutput();
        ShortLivedChildStderrIsAvailableAfterExit();
        ShortLivedChildStderrWorksAtWindowsPathLimit();
        SamplerEvidenceBuilderUsesStartExitStopAndFileEvidence();
        SamplerExitTimeDeterminesWhetherExitWasEarly();
        MonitorSessionClassifiesShortLivedPresentMonStderr();
        MonitorSessionClassifiesSilentNoCsvPresentMonExitZero();
        MonitorSessionClassifiesEarlySystemSamplerAsPartial();
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

    private static void NativeMonitorChildDisposeDrainsOutputFirst()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-child-dispose-drain-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string stderr = Path.Combine(dir, "sampler.stderr.log");
        const string finalMarker = "PIPE_DISPOSE_FINAL_MARKER";
        Process process = Process.GetCurrentProcess();
        int processId = process.Id;
        using (var started = new ManualResetEvent(false))
        using (var release = new ManualResetEvent(false))
        {
            Thread outputThread = new Thread(() =>
            {
                started.Set();
                if (!release.WaitOne(5000)) return;
                FrameScopePresentMonDiagnostics.WriteAllText(stderr, finalMarker, Encoding.UTF8);
            });
            outputThread.IsBackground = true;
            outputThread.Start();

            try
            {
                AssertTrue(started.WaitOne(2000), "dispose output thread started");
                RegisterNativeMonitorPipeThreads(process, new List<Thread> { outputThread });
                release.Set();

                DisposeNativeMonitorChild(process);
                process = null;

                AssertTrue(!outputThread.IsAlive, "child dispose drains output thread first");
                lock (NativeMonitorPipeLock)
                {
                    AssertTrue(!NativeMonitorPipeThreads.ContainsKey(processId), "child dispose leaves no pipe registration");
                }
                AssertEqual(finalMarker, ReadNativeMonitorSamplerTail(stderr, 4096), "child dispose persists final log tail");
            }
            finally
            {
                release.Set();
                outputThread.Join(2000);
                lock (NativeMonitorPipeLock)
                {
                    NativeMonitorPipeThreads.Remove(processId);
                }
                if (process != null) process.Dispose();
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }

    private static void TimedOutPipeThreadRemainsRegisteredUntilDrainCompletes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-child-pipe-join-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string stderr = Path.Combine(dir, "sampler.stderr.log");
        const string finalMarker = "PIPE_JOIN_FINAL_MARKER";
        Process process = Process.GetCurrentProcess();
        int processId = process.Id;
        using (var started = new ManualResetEvent(false))
        using (var release = new ManualResetEvent(false))
        {
            Thread outputThread = new Thread(() =>
            {
                started.Set();
                if (!release.WaitOne(5000)) return;
                FrameScopePresentMonDiagnostics.WriteAllText(stderr, finalMarker, Encoding.UTF8);
            });
            outputThread.IsBackground = true;
            outputThread.Start();

            try
            {
                AssertTrue(started.WaitOne(2000), "blocking output thread started");
                RegisterNativeMonitorPipeThreads(process, new List<Thread> { outputThread });

                Stopwatch wait = Stopwatch.StartNew();
                WaitForNativeMonitorChildOutput(process, 25);
                wait.Stop();
                AssertTrue(wait.ElapsedMilliseconds < 1000, "short pipe join timeout stays bounded");
                lock (NativeMonitorPipeLock)
                {
                    AssertTrue(NativeMonitorPipeThreads.ContainsKey(processId), "timed-out output thread remains registered");
                }

                release.Set();
                WaitForNativeMonitorChildOutput(process, 2000);

                AssertTrue(!outputThread.IsAlive, "released output thread is drained by the second wait");
                lock (NativeMonitorPipeLock)
                {
                    AssertTrue(!NativeMonitorPipeThreads.ContainsKey(processId), "completed output thread registration is removed");
                }
                AssertEqual(finalMarker, ReadNativeMonitorSamplerTail(stderr, 4096), "drained output log tail");
            }
            finally
            {
                release.Set();
                outputThread.Join(2000);
                lock (NativeMonitorPipeLock)
                {
                    NativeMonitorPipeThreads.Remove(processId);
                }
                process.Dispose();
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }

    private static void ChildPipeLogsAreBoundedAndKeepFinalOutput()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-bounded-child-pipe-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string stdout = Path.Combine(dir, "child.stdout.log");
            string stderr = Path.Combine(dir, "child.stderr.log");
            string currentExe = Assembly.GetExecutingAssembly().Location;

            Process child = StartNativeMonitorChild(
                currentExe,
                "--bounded-pipe-child",
                dir,
                stdout,
                stderr,
                ProcessPriorityClass.Normal);

            AssertTrue(child != null, "bounded-output child process started");
            AssertTrue(WaitForNativeMonitorChildExit(child, 30000, 30000), "bounded-output child drains stdout and stderr without blocking");
            AssertEqual(17, child.ExitCode, "bounded-output child exit code");

            AssertTrue(File.Exists(stdout), "bounded stdout log exists");
            AssertTrue(File.Exists(stderr), "bounded stderr log exists");
            long stdoutBytes = new FileInfo(stdout).Length;
            long stderrBytes = new FileInfo(stderr).Length;
            AssertTrue(stdoutBytes <= ExpectedNativeMonitorChildLogMaxBytes, "stdout log stays within the documented 1 MiB cap; actual bytes: " + stdoutBytes);
            AssertTrue(stderrBytes <= ExpectedNativeMonitorChildLogMaxBytes, "stderr log stays within the documented 1 MiB cap; actual bytes: " + stderrBytes);

            string stdoutText = File.ReadAllText(stdout, Encoding.UTF8);
            string stderrText = File.ReadAllText(stderr, Encoding.UTF8);
            AssertTrue(stdoutText.Contains(BoundedStdoutFinalMarker), "bounded stdout retains the final marker");
            AssertTrue(stderrText.Contains(BoundedStderrFinalMarker), "bounded stderr retains the final marker");

            FrameScopeSamplerEvidence evidence = BuildNativeMonitorSamplerEvidence(
                new NativeMonitorSamplerState
                {
                    Required = true,
                    ExecutablePath = currentExe,
                    StderrPath = stderr
                },
                new string[0]);
            AssertTrue(evidence.ErrorTail.Length <= 4096, "sampler ErrorTail stays within 4096 characters");
            AssertTrue(evidence.ErrorTail.EndsWith(BoundedStderrFinalMarker, StringComparison.Ordinal), "sampler ErrorTail retains the final stderr marker");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
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

    private static void SamplerEvidenceBuilderUsesStartExitStopAndFileEvidence()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-sampler-evidence-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string csv = Path.Combine(dir, "process-samples.csv");
            string stderr = Path.Combine(dir, "process-sampler.stderr.log");
            File.WriteAllText(csv,
                "Time,SampleIndex,ProcessName,CpuPct\r\n"
                + "2026-07-11T10:00:00Z,0,game,12.5\r\n",
                Encoding.UTF8);
            File.WriteAllText(stderr, new string('x', 5000) + " sampler-tail", Encoding.UTF8);
            DateTime started = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
            NativeMonitorSamplerState state = new NativeMonitorSamplerState
            {
                Required = true,
                ExecutablePath = Assembly.GetExecutingAssembly().Location,
                CsvPath = csv,
                StderrPath = stderr,
                Started = true,
                Pid = 4242,
                StartedAt = started,
                ExitedAt = started.AddSeconds(2),
                ExitCode = 0,
                ExitedEarly = false,
                StopRequested = true,
                ForcedStop = false
            };

            FrameScopeSamplerEvidence evidence = BuildNativeMonitorSamplerEvidence(state, new[] { "Time", "ProcessName" });

            AssertEqual(true, evidence.ExecutableAvailable, "sampler executable evidence");
            AssertEqual(true, evidence.Started, "sampler started evidence");
            AssertEqual(4242, evidence.Pid.Value, "sampler pid evidence");
            AssertEqual(started, evidence.StartedAt.Value, "sampler start timestamp evidence");
            AssertEqual(started.AddSeconds(2), evidence.ExitedAt.Value, "sampler exit timestamp evidence");
            AssertEqual(0, evidence.ExitCode.Value, "sampler exit code evidence");
            AssertEqual(true, evidence.StopRequested, "sampler owner stop evidence");
            AssertEqual(true, evidence.CsvExists, "sampler CSV existence evidence");
            AssertTrue(evidence.CsvBytes > 0, "sampler CSV byte evidence");
            AssertEqual(1, evidence.ValidRows, "sampler valid row evidence");
            AssertTrue(evidence.ErrorTail.EndsWith("sampler-tail", StringComparison.Ordinal), "sampler bounded error tail content");
            AssertTrue(evidence.ErrorTail.Length <= 4096, "sampler error tail is bounded");
            AssertEqual("healthy", evidence.Status, "sampler normalized status");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void SamplerExitTimeDeterminesWhetherExitWasEarly()
    {
        DateTime stopRequestedAt = new DateTime(2026, 7, 11, 10, 0, 5, DateTimeKind.Utc);

        AssertEqual(true, IsNativeMonitorSamplerEarlyExit(stopRequestedAt.AddMilliseconds(-1), stopRequestedAt, true), "exit immediately before owner stop");
        AssertEqual(false, IsNativeMonitorSamplerEarlyExit(stopRequestedAt, stopRequestedAt, true), "exit at owner stop boundary");
        AssertEqual(false, IsNativeMonitorSamplerEarlyExit(stopRequestedAt.AddMilliseconds(1), stopRequestedAt, true), "exit after owner stop");
        AssertEqual(true, IsNativeMonitorSamplerEarlyExit(stopRequestedAt.AddMilliseconds(1), null, false), "exit without owner stop request");
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

    private static void MonitorSessionClassifiesEarlySystemSamplerAsPartial()
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        string currentExe = Assembly.GetExecutingAssembly().Location;
        string reportGeneratorExe = Path.Combine(root, "FrameScopeReportGenerator.exe");
        AssertTrue(File.Exists(reportGeneratorExe), "FrameScopeReportGenerator.exe exists");
        string runDir = RunSyntheticMonitorSession("SamplerEarlyFailure", false, currentExe);
        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> status = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "status.json"), Encoding.UTF8));
            Dictionary<string, object> summary = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, "summary.json"), Encoding.UTF8));

            AssertEqual("healthy", Convert.ToString(status["ProcessSamplerStatus"]), "process sampler final health");
            AssertEqual("exited-early", Convert.ToString(status["SystemSamplerStatus"]), "system sampler final health");
            AssertEqual("exited-early", Convert.ToString(summary["SystemSamplerStatus"]), "summary system sampler final health");
            AssertEqual(true, Convert.ToBoolean(status["SystemSamplerStarted"]), "failed system sampler started");
            AssertEqual(7, Convert.ToInt32(status["SystemSamplerExitCode"]), "failed system sampler exit code");
            AssertEqual(true, Convert.ToBoolean(status["SystemSamplerExitedEarly"]), "failed system sampler early exit");
            AssertEqual(0, Convert.ToInt32(status["SystemSamplerValidRows"]), "failed system sampler valid rows");

            using (Process report = Process.Start(new ProcessStartInfo
            {
                FileName = reportGeneratorExe,
                Arguments = QuoteTestArgument(runDir),
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                AssertTrue(report != null, "report generator started");
                AssertTrue(report.WaitForExit(30000), "report generator exited");
                AssertEqual(0, report.ExitCode, "report generator exit code");
            }

            Dictionary<string, object> manifest = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(
                Path.Combine(runDir, "charts", "framescope-interactive-manifest.json"), Encoding.UTF8));
            AssertTrue(Convert.ToInt32(manifest["frames"]) > 0, "integration fixture has valid frames");
            AssertEqual("partial", Convert.ToString(manifest["reportKind"]), "failed auxiliary sampler report kind");
            AssertEqual("exited-early", Convert.ToString(manifest["systemSamplerStatus"]), "manifest system sampler status");
        }
        finally
        {
            TryDeleteTopTempRun(runDir);
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
        string stdout = Path.Combine(runDir, "presentmon.stdout.log");
        string stderr = Path.Combine(runDir, "presentmon.stderr.log");
        Process child = null;
        try
        {
            AssertTrue(stderr.Length >= 260, "stderr path length reaches Windows legacy limit");
            FrameScopePresentMonDiagnostics.WriteAllText(stderr, "direct long path access denied", Encoding.UTF8);
            AssertTrue(FrameScopePresentMonDiagnostics.FileExists(stderr), "direct long-path write exists");
            AssertTrue(FrameScopePresentMonDiagnostics.ReadAllText(stderr, Encoding.UTF8).Contains("access denied"), "direct long-path read");
            DeleteLongPathTestFile(stderr);
            string currentExe = Assembly.GetExecutingAssembly().Location;

            child = StartNativeMonitorChild(
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
            try
            {
                if (child != null)
                {
                    WaitForNativeMonitorChildOutput(child, 5000);
                    child.Dispose();
                }
            }
            finally
            {
                DeleteLongPathTestDirectory(baseDir, runDir, stdout, stderr);
            }
        }
    }

    private static void DeleteLongPathTestFile(string path)
    {
        if (FrameScopePresentMonDiagnostics.FileExists(path)
            && !DeleteFileW(ToLongPathTestPath(path)))
        {
            throw new IOException("Unable to delete long-path test file: " + path + " (Win32 " + Marshal.GetLastWin32Error() + ")");
        }
        AssertTrue(!FrameScopePresentMonDiagnostics.FileExists(path), "long-path test file was deleted");
    }

    private static void DeleteLongPathTestDirectory(string rootPath, string leafPath, params string[] filePaths)
    {
        foreach (string filePath in filePaths) DeleteLongPathTestFile(filePath);

        string root = GetFullPathTestPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        string current = GetFullPathTestPath(leafPath).TrimEnd(Path.DirectorySeparatorChar);
        while (true)
        {
            bool isRoot = string.Equals(current, root, StringComparison.OrdinalIgnoreCase);
            if (!isRoot && !current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Refusing to delete a directory outside the long-path test fixture: " + current);
            }
            if (!RemoveDirectoryW(ToLongPathTestPath(current)))
            {
                throw new IOException("Unable to delete long-path test directory: " + current + " (Win32 " + Marshal.GetLastWin32Error() + ")");
            }
            if (isRoot) break;
            current = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(current)) throw new IOException("Long-path test fixture root was not reached.");
        }

        AssertTrue(!Directory.Exists(root), "long-path test fixture was deleted");
    }

    private static string ToLongPathTestPath(string path)
    {
        string fullPath = GetFullPathTestPath(path);
        if (Path.DirectorySeparatorChar != '\\' || fullPath.StartsWith(@"\\?\", StringComparison.Ordinal)) return fullPath;
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + fullPath.Substring(2);
        return @"\\?\" + fullPath;
    }

    private static string GetFullPathTestPath(string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFileW(string fileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveDirectoryW(string pathName);

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

    private static bool IsSamplerFailurePresentMonInvocation(string[] args)
    {
        return GetTestArg(args, "--session_name", "").IndexOf("SamplerEarlyFailure", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string SamplerFailurePresentMonStopPath(string[] args)
    {
        string session = GetTestArg(args, "--session_name", "SamplerEarlyFailure");
        foreach (char invalid in Path.GetInvalidFileNameChars()) session = session.Replace(invalid, '_');
        return Path.Combine(Path.GetTempPath(), session + ".fake-presentmon-stop");
    }

    private static int RunSamplerFailureFakePresentMon(string[] args)
    {
        string output = GetTestArg(args, "--output_file", "");
        if (string.IsNullOrWhiteSpace(output)) return 8;
        string stopPath = SamplerFailurePresentMonStopPath(args);
        try { if (File.Exists(stopPath)) File.Delete(stopPath); }
        catch { }
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("TimeInDateTime,MsBetweenPresents,Application,ProcessID,SwapChainAddress,PresentMode,AllowsTearing");
        DateTime start = DateTime.Now;
        for (int i = 0; i < 120; i++)
        {
            csv.Append(start.AddMilliseconds(i * 16.667).ToString("o"));
            csv.Append(",16.667,FrameScopeFakeTarget.exe,4242,0x1,Hardware Composed: Independent Flip,true\r\n");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output, csv.ToString(), Encoding.UTF8);
        DateTime deadline = DateTime.Now.AddSeconds(15);
        while (DateTime.Now < deadline && !File.Exists(stopPath)) Thread.Sleep(50);
        try { if (File.Exists(stopPath)) File.Delete(stopPath); }
        catch { }
        return 0;
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
        if (systemCsv.IndexOf("SamplerEarlyFailure", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Console.Error.WriteLine("synthetic system sampler early failure");
            return 7;
        }
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
