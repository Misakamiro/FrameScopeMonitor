using System;
using System.Collections.Generic;
using System.IO;

public static class FrameScopeNativeWatcherPolicyTests
{
    public static int Main()
    {
        try
        {
            WatcherLoopUsesFixedInternalPollInterval();
            WatcherStartArgumentsKeepSamplerIntervalsSeparateFromPollInterval();
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

    private static string ReadWatcherSource()
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        string path = Path.Combine(root, "src", "app", "FrameScopeNativeMonitor.Watcher.cs");
        if (!File.Exists(path)) throw new Exception("Watcher source not found: " + path);
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

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }
}
