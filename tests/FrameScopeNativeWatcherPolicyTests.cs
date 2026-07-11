using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class FrameScopeNativeWatcherPolicyTests
{
    public static int Main()
    {
        try
        {
            WatcherLoopUsesFixedInternalPollInterval();
            WatcherStartArgumentsKeepSamplerIntervalsSeparateFromPollInterval();
            WorkerProcessRoleIsVisibleInSources();
            WatcherPublishesRunningStateBeforeRecoveryAndBlocksDuplicateStarts();
            Console.WriteLine("FrameScopeNativeWatcherPolicyTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
    }

    private static void WatcherLoopUsesFixedInternalPollInterval()
    {
        AssertEqual(1000, FrameScopeConfigStore.InternalPollIntervalMs, "internal watcher poll interval");

        string source = ReadWatcherSource();
        AssertContains(source, "Thread.Sleep(FrameScopeConfigStore.InternalPollIntervalMs)", "watcher sleep should use internal fixed poll interval");
        AssertContains(source, "FrameScopeConfigStore.InternalPollIntervalMs.ToString(CultureInfo.InvariantCulture)", "watcher performance log should report fixed internal poll interval");
        AssertDoesNotContain(source, "config.PollIntervalMs > 0 ? config.PollIntervalMs", "watcher should not derive sleep from config PollIntervalMs");
        AssertDoesNotContain(source, "Thread.Sleep(config.PollIntervalMs", "watcher should not sleep from config PollIntervalMs");
    }

    private static void WatcherStartArgumentsKeepSamplerIntervalsSeparateFromPollInterval()
    {
        string source = ReadWatcherSource();
        string monitorStartArgumentsSource = ExtractStartMonitorArgumentsSource(source);
        AssertContains(source, "config.TelemetrySampleIntervalMs", "watcher should read the global telemetry sample interval");
        AssertContains(source, "var processSampleMs = telemetrySampleMs", "process sampler should use global telemetry interval");
        AssertContains(source, "var slowSampleMs = telemetrySampleMs", "system sampler should use global telemetry interval");
        AssertContains(source, "var cpuCoreSampleMs = telemetrySampleMs", "CPU core sampler should use global telemetry interval");
        AssertContains(source, "var cpuVoltageSampleMs = telemetrySampleMs", "CPU Voltage / Vcore sampler should use global telemetry interval");
        AssertContains(source, "var cpuVidSampleMs = telemetrySampleMs", "CPU Core VID sampler should use global telemetry interval");
        AssertContains(monitorStartArgumentsSource, "\" --SampleIntervalMs \" + sampleMs", "PresentMon/session sample interval argument");
        AssertContains(monitorStartArgumentsSource, "\" --ProcessSampleIntervalMs \" + processSampleMs", "process sampler interval argument");
        AssertContains(monitorStartArgumentsSource, "\" --SlowSampleIntervalMs \" + slowSampleMs", "system slow sampler interval argument");
        AssertContains(monitorStartArgumentsSource, "\" --CpuCoreSampleIntervalMs \" + cpuCoreSampleMs", "CPU core sampler interval argument");
        AssertContains(monitorStartArgumentsSource, "\" --EnableCpuVoltageTelemetry \" + (enableCpuVoltageTelemetry ? \"true\" : \"false\")", "new watcher runs should actively probe CPU Voltage / Vcore telemetry");
        AssertContains(monitorStartArgumentsSource, "\" --CpuVoltageSampleIntervalMs \" + cpuVoltageSampleMs", "CPU Voltage / Vcore sampler interval argument");
        AssertContains(monitorStartArgumentsSource, "\" --CpuVoltageProvider \" + Quote(cpuVoltageProvider)", "CPU Voltage / Vcore provider argument");
        AssertContains(monitorStartArgumentsSource, "\" --CpuVidSampleIntervalMs \" + cpuVidSampleMs", "CPU Core VID sampler interval argument");
        AssertDoesNotContain(monitorStartArgumentsSource, "PollIntervalMs.ToString(CultureInfo.InvariantCulture)", "sampler arguments should not use PollIntervalMs");
        AssertDoesNotContain(monitorStartArgumentsSource, "config.PollIntervalMs", "sampler arguments should not read legacy PollIntervalMs");
    }

    private static void WorkerProcessRoleIsVisibleInSources()
    {
        string watcherSource = ReadSource("src", "app", "FrameScopeNativeMonitor.Watcher.cs");
        string webHostSource = ReadSource("src", "app", "FrameScopeNativeMonitor.WebHost.cs");
        string cleanupSource = ReadSource("src", "app", "FrameScopeNativeMonitor.ProcessCleanup.cs");

        AssertContains(webHostSource, "FileName = Application.ExecutablePath", "UI start should launch the same executable as watcher worker");
        AssertContains(webHostSource, "--watcher --config", "UI start should mark the worker with watcher mode");
        AssertContains(webHostSource, "monitor-worker-start role=watcher-worker", "start log should name watcher worker role");
        AssertContains(webHostSource, "任务管理器中可能显示一个 FrameScopeMonitor.exe 子进程", "start message should explain the Task Manager child process");

        AssertContains(watcherSource, "FileName = Application.ExecutablePath", "watcher should launch the same executable for monitor-session worker");
        AssertContains(watcherSource, "\" --MonitorProcessRole monitor-session-worker\"", "monitor-session launch should carry a readable worker role");
        AssertContains(watcherSource, "monitor-worker-start role=monitor-session-worker", "watcher log should name monitor-session worker role");

        AssertContains(cleanupSource, "commandLower.Contains(\"--watcher\") || commandLower.Contains(\"--monitor-session\")", "stop policy should include watcher and monitor-session workers");
    }

    private static void WatcherPublishesRunningStateBeforeRecoveryAndBlocksDuplicateStarts()
    {
        string watcherSource = ReadSource("src", "app", "FrameScopeNativeMonitor.Watcher.cs");
        string webHostSource = ReadSource("src", "app", "FrameScopeNativeMonitor.WebHost.cs");
        int initialStateWrite = watcherSource.IndexOf("WriteNativeWatcherState(configPath, \"starting\"", StringComparison.Ordinal);
        int recovery = watcherSource.IndexOf("RecoverStaleMissingReports(dataRoot, config)", StringComparison.Ordinal);

        AssertTrue(initialStateWrite >= 0 && initialStateWrite < recovery, "watcher must publish PID before stale report recovery");
        AssertContains(webHostSource, "HasFrameScopeBackgroundProcesses()", "start action must detect watcher processes before state file exists");
    }

    private static string ReadWatcherSource()
    {
        return ReadSource("src", "app", "FrameScopeNativeMonitor.Watcher.cs");
    }

    private static string ReadSource(params string[] parts)
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        string path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        if (!File.Exists(path)) throw new Exception("Source not found: " + path);
        return File.ReadAllText(path);
    }

    private static string ExtractStartMonitorArgumentsSource(string source)
    {
        const string startMarker = "var args =";
        const string endMarker = "WriteVerboseFrameScopeLog";
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) throw new Exception("monitor start argument block start not found");
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0) throw new Exception("monitor start argument block end not found");
        return source.Substring(start, end - start);
    }

    private static void AssertContains(string text, string expected, string label)
    {
        if (text == null || text.IndexOf(expected, StringComparison.Ordinal) < 0)
        {
            throw new Exception(label + ": missing <" + expected + ">");
        }
    }

    private static void AssertDoesNotContain(string text, string unexpected, string label)
    {
        if (text != null && text.IndexOf(unexpected, StringComparison.Ordinal) >= 0)
        {
            throw new Exception(label + ": unexpected <" + unexpected + ">");
        }
    }

    private static void AssertTrue(bool value, string label)
    {
        if (!value) throw new Exception(label);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }
}
