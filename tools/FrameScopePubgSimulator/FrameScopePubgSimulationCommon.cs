using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public static class FrameScopePubgSimulationCommon
{
    public const string DisplayName = "PUBG: BATTLEGROUNDS";
    public const string PrimaryProcessName = "TslGame.exe";
    public const string ShippingProcessName = "TslGame-Win64-Shipping.exe";
    public const string WindowTitle = "PLAYERUNKNOWN'S BATTLEGROUNDS  -  PUBG: BATTLEGROUNDS";
    public const string DefaultRunNamePrefix = "SyntheticPUBG";

    public static FrameScopeTarget CreateTarget()
    {
        return new FrameScopeTarget
        {
            Enabled = true,
            Name = DisplayName,
            ProcessName = PrimaryProcessName,
            SampleIntervalMs = 100,
            ProcessSampleIntervalMs = 100,
            SlowSampleIntervalMs = 1000,
            OpenReportOnComplete = true
        };
    }

    public static string BuildMonitorSessionArguments(string runRoot, string presentMonExe, int initialTargetPid, int captureSeconds)
    {
        List<string> args = new List<string>
        {
            "--monitor-session",
            "--TargetProcessName", PrimaryProcessName,
            "--TargetProcessAliases", "TslGame;TslGame-Win64-Shipping",
            "--TargetDisplayName", DisplayName,
            "--InitialTargetPid", initialTargetPid.ToString(CultureInfo.InvariantCulture),
            "--WaitSeconds", "8",
            "--CaptureSeconds", Math.Max(1, captureSeconds).ToString(CultureInfo.InvariantCulture),
            "--SampleIntervalMs", "100",
            "--ProcessSampleIntervalMs", "100",
            "--SlowSampleIntervalMs", "1000",
            "--ControlPollIntervalMs", "1000",
            "--RunRoot", runRoot ?? "",
            "--RunNamePrefix", DefaultRunNamePrefix,
            "--PresentMonExe", presentMonExe ?? ""
        };
        return JoinArguments(args);
    }

    public static void WritePresentMonCsv(string path, string scenario, int rows)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("CSV path is empty.", "path");
        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string mode = (scenario ?? "stable").Trim().ToLowerInvariant();
        int count = mode == "no-data" ? 0 : Math.Max(1, rows);
        DateTime start = DateTime.Now;

        using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            writer.WriteLine("Application,ProcessID,SwapChainAddress,PresentMode,AllowsTearing,TimeInDateTime,MsBetweenPresents");
            for (int i = 0; i < count; i++)
            {
                double frameMs = FrameMsFor(mode, i);
                DateTime timestamp = start.AddMilliseconds(i * 16.667);
                writer.Write(PrimaryProcessName);
                writer.Write(",4242,0x1234,Hardware: Independent Flip,true,");
                writer.Write(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
                writer.Write(",");
                writer.WriteLine(frameMs.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }
    }

    public static string GetArgValue(string[] args, string name, string fallback)
    {
        if (args == null) return fallback;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return fallback;
    }

    public static bool HasFlag(string[] args, string name)
    {
        if (args == null) return false;
        foreach (string arg in args)
        {
            if (arg.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static int ParseInt(string text, int fallback)
    {
        int value;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
    }

    public static string StopSentinelPath(string sessionName)
    {
        string safe = string.IsNullOrWhiteSpace(sessionName) ? "FrameScopeSynthetic" : sessionName.Trim();
        foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '-');
        return Path.Combine(Path.GetTempPath(), "framescope-fake-presentmon-" + safe + ".stop");
    }

    private static double FrameMsFor(string mode, int index)
    {
        if (mode == "spikes")
        {
            if (index % 90 == 30) return 112.0;
            if (index % 90 == 60) return 4.2;
            return 12.5 + (index % 12) * 0.35;
        }
        if (mode == "fluctuating" || mode == "wave")
        {
            return 16.667 + Math.Sin(index / 9.0) * 5.5 + (index % 7) * 0.22;
        }
        return 16.667;
    }

    private static string JoinArguments(IEnumerable<string> args)
    {
        StringBuilder builder = new StringBuilder();
        foreach (string arg in args)
        {
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(Quote(arg ?? ""));
        }
        return builder.ToString();
    }

    private static string Quote(string value)
    {
        if (value.Length == 0) return "\"\"";
        bool needsQuote = value.IndexOfAny(new[] { ' ', '\t', '"', ';', '\'' }) >= 0;
        string escaped = value.Replace("\"", "\\\"");
        return needsQuote ? "\"" + escaped + "\"" : escaped;
    }
}
