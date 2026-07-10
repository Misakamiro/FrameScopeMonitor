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
            CpuCoreSamplesCsv = Path.Combine(runDir, "cpu-core-samples.csv"),
            CpuCoreTelemetryStatusPath = Path.Combine(runDir, "cpu-core-telemetry-status.json"),
            CpuVoltageSamplesCsv = Path.Combine(runDir, "cpu-voltage-samples.csv"),
            CpuVoltageTelemetryStatusPath = Path.Combine(runDir, "cpu-voltage-telemetry-status.json"),
            CpuVidSamplesCsv = Path.Combine(runDir, "cpu-vid-samples.csv"),
            CpuVidTelemetryStatusPath = Path.Combine(runDir, "cpu-vid-telemetry-status.json"),
            ProcessCsv = Path.Combine(runDir, "process-samples.csv"),
            ProcessSamplerStdout = Path.Combine(runDir, "process-sampler.stdout.log"),
            ProcessSamplerStderr = Path.Combine(runDir, "process-sampler.stderr.log"),
            SystemSamplerStdout = Path.Combine(runDir, "system-sampler.stdout.log"),
            SystemSamplerStderr = Path.Combine(runDir, "system-sampler.stderr.log"),
            TopCpuCsv = Path.Combine(runDir, "topcpu-samples.csv"),
            TopIoCsv = Path.Combine(runDir, "topio-samples.csv"),
            AlertsCsv = Path.Combine(runDir, "sample-alerts.csv"),
            SamplerStopPath = Path.Combine(runDir, "sampler-stop.signal"),
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

    private static bool ParseBoolArgument(string[] args, string name, bool fallback)
    {
        var text = GetArgValue(args, name, fallback ? "true" : "false");
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        bool value;
        if (bool.TryParse(text, out value)) return value;
        if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return fallback;
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

    private static Dictionary<string, object> ReadJsonDictionary(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var map = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            return map ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, object> BuildCpuCoreTelemetryDiagnostics(MonitorSessionPaths paths, bool enabled, bool voltageEnabled, bool vidEnabled)
    {
        var diagnostics = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            { "CpuCoreTelemetryEnabled", enabled },
            { "CpuCoreSamplesCsv", paths.CpuCoreSamplesCsv },
            { "CpuCoreSampleCount", 0 },
            { "CpuCoreTelemetryAvailable", false },
            { "CpuCoreTelemetryUnavailableReason", enabled ? "CPU core telemetry status was not recorded." : "CPU core telemetry is disabled." },
            { "CpuVoltageTelemetryEnabled", voltageEnabled },
            { "CpuVoltageSamplesCsv", paths.CpuVoltageSamplesCsv },
            { "CpuVoltageSampleCount", 0 },
            { "CpuVoltageVcoreSampleCount", 0 },
            { "CpuVoltagePerCoreSampleCount", 0 },
            { "CpuVoltageNonPerCoreSampleCount", 0 },
            { "CpuVoltageRejectedSampleCount", 0 },
            { "CpuVoltageAvailable", false },
            { "CpuVoltageVcoreAvailable", false },
            { "CpuVoltagePerCoreAvailable", false },
            { "CpuVoltageNonPerCoreAvailable", false },
            { "CpuVoltageStatus", "unavailable" },
            { "CpuVoltageUnavailableReason", voltageEnabled ? "CPU Voltage / Vcore telemetry status was not recorded." : "CPU Voltage / Vcore telemetry is disabled." },
            { "CpuVidTelemetryEnabled", vidEnabled },
            { "CpuVidSamplesCsv", paths.CpuVidSamplesCsv },
            { "CpuVidSampleCount", 0 },
            { "CpuVidCoreCount", 0 },
            { "CpuVidAvailable", false },
            { "CpuVidStatus", "unavailable" },
            { "CpuVidUnavailableReason", vidEnabled ? "CPU Core VID telemetry status was not recorded." : "CPU Core VID telemetry is disabled." }
        };

        AddDictionary(diagnostics, ReadJsonDictionary(paths.CpuCoreTelemetryStatusPath));
        if (File.Exists(paths.CpuVoltageTelemetryStatusPath)) AddDictionary(diagnostics, ReadJsonDictionary(paths.CpuVoltageTelemetryStatusPath));
        AddDictionary(diagnostics, ReadJsonDictionary(paths.CpuVidTelemetryStatusPath));

        diagnostics["CpuCoreSampleCount"] = CountCsvDataRows(paths.CpuCoreSamplesCsv, Int32.MaxValue);
        if (File.Exists(paths.CpuCoreSamplesCsv))
        {
            diagnostics["CpuCoreTelemetryAvailable"] = Convert.ToInt32(diagnostics["CpuCoreSampleCount"], CultureInfo.InvariantCulture) > 0;
        }
        int voltageCsvRows = CountCsvDataRows(paths.CpuVoltageSamplesCsv, Int32.MaxValue);
        diagnostics["CpuVoltageSampleCount"] = Math.Max(Convert.ToInt32(diagnostics["CpuVoltageSampleCount"], CultureInfo.InvariantCulture), voltageCsvRows);
        diagnostics["CpuVoltageVcoreSampleCount"] = Math.Max(Convert.ToInt32(diagnostics["CpuVoltageVcoreSampleCount"], CultureInfo.InvariantCulture), voltageCsvRows);
        if (File.Exists(paths.CpuVoltageSamplesCsv))
        {
            int vcoreRows = Convert.ToInt32(diagnostics["CpuVoltageVcoreSampleCount"], CultureInfo.InvariantCulture);
            diagnostics["CpuVoltageAvailable"] = vcoreRows > 0;
            diagnostics["CpuVoltageVcoreAvailable"] = vcoreRows > 0;
            diagnostics["CpuVoltageStatus"] = vcoreRows > 0 ? "vcore-available" : "unavailable";
        }
        if (!diagnostics.ContainsKey("CpuVoltageAvailable") || diagnostics["CpuVoltageAvailable"] == null)
        {
            diagnostics["CpuVoltageAvailable"] = false;
        }
        if (!diagnostics.ContainsKey("CpuVoltageStatus") || diagnostics["CpuVoltageStatus"] == null)
        {
            diagnostics["CpuVoltageStatus"] = "unavailable";
        }
        int vidCsvRows = CountCsvDataRows(paths.CpuVidSamplesCsv, Int32.MaxValue);
        diagnostics["CpuVidSampleCount"] = Math.Max(Convert.ToInt32(diagnostics["CpuVidSampleCount"], CultureInfo.InvariantCulture), vidCsvRows);
        if (File.Exists(paths.CpuVidSamplesCsv))
        {
            diagnostics["CpuVidAvailable"] = vidCsvRows > 0;
            diagnostics["CpuVidStatus"] = vidCsvRows > 0 ? "core-vid-available" : "unavailable";
        }
        if (!diagnostics.ContainsKey("CpuVidAvailable") || diagnostics["CpuVidAvailable"] == null)
        {
            diagnostics["CpuVidAvailable"] = false;
        }
        if (!diagnostics.ContainsKey("CpuVidStatus") || diagnostics["CpuVidStatus"] == null)
        {
            diagnostics["CpuVidStatus"] = "unavailable";
        }
        return diagnostics;
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
