using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

internal static partial class FrameScopeNativeMonitor
{
    private static MonitorSessionPaths CreateMonitorSessionPaths(string runDir)
    {
        return new MonitorSessionPaths
        {
            RunDir = runDir,
            StatusPath = Path.Combine(runDir, "status.json"),
            PresentMonCsv = Path.Combine(runDir, "presentmon.csv"),
            PresentMonStdout = Path.Combine(runDir, "presentmon.stdout.log"),
            PresentMonStderr = Path.Combine(runDir, "presentmon.stderr.log"),
            PresentMonInfoPath = Path.Combine(runDir, "presentmon-info.json"),
            SamplesCsv = Path.Combine(runDir, "system-samples.csv"),
            ProcessCsv = Path.Combine(runDir, "process-samples.csv"),
            TopCpuCsv = Path.Combine(runDir, "topcpu-samples.csv"),
            TopIoCsv = Path.Combine(runDir, "topio-samples.csv"),
            AlertsCsv = Path.Combine(runDir, "sample-alerts.csv"),
            EventsCsv = Path.Combine(runDir, "event-samples.csv"),
            SummaryPath = Path.Combine(runDir, "summary.json"),
            ReportLogPath = Path.Combine(runDir, "report-generation.log"),
            SlowSamplerLogPath = Path.Combine(runDir, "system-slow-sampler.log"),
            ErrorPath = Path.Combine(runDir, "monitor-error.txt")
        };
    }

    private static int ParseIntArgument(string[] args, string name, int fallback)
    {
        var text = GetArgValue(args, name, fallback.ToString(CultureInfo.InvariantCulture));
        int value;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
    }

    private static string JoinArguments(IEnumerable<string> args)
    {
        return string.Join(" ", args.Select(QuoteCommandArgument).ToArray());
    }

    private static string QuoteCommandArgument(string value)
    {
        if (value == null) value = "";
        if (value.Length == 0) return "\"\"";
        if (value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) < 0) return value;

        var builder = new StringBuilder();
        builder.Append('"');
        var backslashes = 0;
        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }
            if (ch == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }
            if (backslashes > 0)
            {
                builder.Append('\\', backslashes);
                backslashes = 0;
            }
            builder.Append(ch);
        }
        if (backslashes > 0) builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }

    private static int CountCsvDataRows(string path, int maxRows)
    {
        try
        {
            var count = 0;
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                if (reader.ReadLine() == null) return 0;
                while (count < maxRows && reader.ReadLine() != null) count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static string TailText(string path, int maxChars)
    {
        try
        {
            if (!File.Exists(path)) return "";
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Length <= maxChars) return text.Trim();
            return text.Substring(text.Length - maxChars).Trim();
        }
        catch
        {
            return "";
        }
    }

    private static void AddDictionary(Dictionary<string, object> target, Dictionary<string, object> source)
    {
        if (target == null || source == null) return;
        foreach (var pair in source) target[pair.Key] = pair.Value;
    }

    private static void WriteEventCsvHeader(string path)
    {
        try
        {
            File.WriteAllText(path, "TimeCreated,ProviderName,Id,LevelDisplayName,Message\r\n", new UTF8Encoding(false));
        }
        catch { }
    }
}
