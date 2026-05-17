using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Web.Script.Serialization;

internal static partial class FrameScopeReportGenerator
{
    private static Dictionary<string, string> LoadRunMetadata(string runDir)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string summary = Path.Combine(runDir, "summary.json");
        string status = Path.Combine(runDir, "status.json");
        foreach (string path in new[] { summary, status })
        {
            if (!File.Exists(path)) continue;
            string text = File.ReadAllText(path, Encoding.UTF8);
            string target = ExtractJsonString(text, "TargetProcessName");
            if (string.IsNullOrEmpty(target)) target = ExtractJsonString(text, "TargetProcess");
            if (string.IsNullOrEmpty(target)) target = ExtractJsonString(text, "Target");
            if (!string.IsNullOrEmpty(target))
            {
                result["targetProcess"] = target;
                return result;
            }
        }
        result["targetProcess"] = "cs2.exe";
        return result;
    }

    private static Dictionary<string, object> LoadCaptureDiagnostics(string runDir)
    {
        Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        string status = Path.Combine(runDir, "status.json");
        string summary = Path.Combine(runDir, "summary.json");
        foreach (string path in new[] { status, summary })
        {
            if (!File.Exists(path)) continue;
            try
            {
                Dictionary<string, object> map = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                if (map == null) continue;
                CopyDiagnosticValue(map, result, "FrameCaptureStatus");
                CopyDiagnosticValue(map, result, "FrameCaptureMessage");
                CopyDiagnosticValue(map, result, "PresentMonCsvExists");
                CopyDiagnosticValue(map, result, "PresentMonCsvBytes");
                CopyDiagnosticValue(map, result, "PresentMonCsvRows");
                CopyDiagnosticValue(map, result, "PresentMonExitCode");
                CopyDiagnosticValue(map, result, "PresentMonExitedEarly");
                CopyDiagnosticValue(map, result, "PresentMonForcedStop");
                CopyDiagnosticValue(map, result, "PresentMonStdoutTail");
                CopyDiagnosticValue(map, result, "PresentMonStderrTail");
                CopyDiagnosticValue(map, result, "PresentMonCaptureMode");
                CopyDiagnosticValue(map, result, "PresentMonCaptureTarget");
                CopyDiagnosticValue(map, result, "PresentMonArgs");
                CopyDiagnosticValue(map, result, "TargetProcess");
                CopyDiagnosticValue(map, result, "TargetResolvedProcess");
                CopyDiagnosticValue(map, result, "TargetProcessCandidates");
                CopyDiagnosticValue(map, result, "TargetWindowTitle");
                CopyDiagnosticValue(map, result, "TargetHasMainWindow");
            }
            catch { }
        }

        if (!result.ContainsKey("FrameCaptureStatus"))
        {
            string presentMonCsv = Path.Combine(runDir, "presentmon.csv");
            result["PresentMonCsvExists"] = File.Exists(presentMonCsv);
            result["PresentMonCsvBytes"] = File.Exists(presentMonCsv) ? new FileInfo(presentMonCsv).Length : 0;
            result["FrameCaptureStatus"] = File.Exists(presentMonCsv) ? "presentmon-csv-present" : "no-presentmon-csv";
            result["FrameCaptureMessage"] = File.Exists(presentMonCsv)
                ? "PresentMon CSV exists; detailed capture status was not recorded by this older run."
                : "PresentMon CSV is missing; this older run did not record detailed capture diagnostics.";
        }

        return result;
    }

    private static void CopyDiagnosticValue(Dictionary<string, object> source, Dictionary<string, object> target, string key)
    {
        object value;
        if (source.TryGetValue(key, out value) && value != null) target[key] = value;
    }

    private static string ExtractJsonString(string text, string key)
    {
        string pattern = "\"" + key + "\"";
        int keyPos = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (keyPos < 0) return "";
        int colon = text.IndexOf(':', keyPos + pattern.Length);
        if (colon < 0) return "";
        int quote = text.IndexOf('"', colon + 1);
        if (quote < 0) return "";
        StringBuilder sb = new StringBuilder();
        bool escape = false;
        for (int i = quote + 1; i < text.Length; i++)
        {
            char ch = text[i];
            if (escape)
            {
                sb.Append(ch);
                escape = false;
            }
            else if (ch == '\\')
            {
                escape = true;
            }
            else if (ch == '"')
            {
                break;
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static Dictionary<string, object> LoadHardware()
    {
        Dictionary<string, object> h = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    h["CpuName"] = Convert.ToString(obj["Name"]);
                    h["CpuCores"] = SafeInt(obj["NumberOfCores"]);
                    h["CpuThreads"] = SafeInt(obj["NumberOfLogicalProcessors"]);
                    h["CpuMaxClockMHz"] = SafeInt(obj["MaxClockSpeed"]);
                    break;
                }
            }
        }
        catch { }
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption,Version,OSArchitecture FROM Win32_OperatingSystem"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    h["OsCaption"] = Convert.ToString(obj["Caption"]);
                    h["OsVersion"] = Convert.ToString(obj["Version"]);
                    h["OsArch"] = Convert.ToString(obj["OSArchitecture"]);
                    break;
                }
            }
        }
        catch { }
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,DriverVersion FROM Win32_VideoController"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = Convert.ToString(obj["Name"]);
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    h["GpuName"] = name;
                    h["GpuDriver"] = Convert.ToString(obj["DriverVersion"]);
                    break;
                }
            }
        }
        catch { }
        try
        {
            h["TotalMemoryMB"] = RoundDouble(new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / 1024.0 / 1024.0, 0);
        }
        catch { }
        return h;
    }

    private static int? SafeInt(object value)
    {
        if (value == null) return null;
        int parsed;
        if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed)) return parsed;
        return null;
    }

    private static double? GetDoubleFromHardware(Dictionary<string, object> hardware, string key)
    {
        object value;
        if (!hardware.TryGetValue(key, out value) || value == null) return null;
        double parsed;
        if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return parsed;
        return null;
    }
}
