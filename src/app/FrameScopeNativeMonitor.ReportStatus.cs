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
    private static Dictionary<string, object> UpdateStatusAfterReportGeneration(string runDir, Dictionary<string, object> status, ReportGenerationResult result, int monitorExitCode)
    {
        var statusPath = Path.Combine(runDir, "status.json");
        var map = status != null
            ? new Dictionary<string, object>(status, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        map["Time"] = DateTime.Now.ToString("o");
        map["Phase"] = "done";
        map["ExitCode"] = monitorExitCode;
        map["ReportHtml"] = result.ReportHtml;
        map["ReportLog"] = result.LogPath;
        map["ReportProgressPath"] = result.ProgressPath;
        map["ReportError"] = result.Error;
        map["ReportGeneratedByWatcher"] = true;
        map["ReportGenerationAttempted"] = result.Attempted;
        map["ReportGenerationExitCode"] = result.ExitCode;
        map["ReportFrameCount"] = result.FrameCount;
        map["ReportHasFrameData"] = result.HasFrameData;
        map["ReportProcessSampleCount"] = result.ProcessSampleCount;
        map["ReportSystemSampleCount"] = result.SystemSampleCount;
        map["ReportKind"] = result.ReportKind;
        ApplyReportSamplerEvidence(map, result);
        FrameScopeReportProgress.AddTo(map, FrameScopeReportProgress.Read(result.ProgressPath));

        try { FrameScopeJsonFile.Write(statusPath, Json.Serialize(map)); }
        catch (Exception ex) { WriteFrameScopeLog("status-update-failed run=" + runDir + " error=" + ex.Message); }
        UpdateSummaryAfterReportGeneration(runDir, result);
        return map;
    }

    private static void UpdateSummaryAfterReportGeneration(string runDir, ReportGenerationResult result)
    {
        try
        {
            string path = Path.Combine(runDir, "summary.json");
            Dictionary<string, object> map = File.Exists(path)
                ? Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8))
                : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (map == null) map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            map["ReportHtml"] = result.ReportHtml;
            map["ReportLog"] = result.LogPath;
            map["ReportProgressPath"] = result.ProgressPath;
            map["ReportError"] = result.Error;
            map["ReportGenerationAttempted"] = result.Attempted;
            map["ReportGenerationExitCode"] = result.ExitCode;
            map["ReportFrameCount"] = result.FrameCount;
            map["ReportHasFrameData"] = result.HasFrameData;
            map["ReportProcessSampleCount"] = result.ProcessSampleCount;
            map["ReportSystemSampleCount"] = result.SystemSampleCount;
            map["ReportKind"] = result.ReportKind;
            ApplyReportSamplerEvidence(map, result);
            object reportsValue;
            Dictionary<string, object> reports = map.TryGetValue("Reports", out reportsValue)
                ? reportsValue as Dictionary<string, object>
                : null;
            if (reports == null) reports = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            reports["Attempted"] = result.Attempted;
            reports["ExitCode"] = result.ExitCode;
            reports["ReportHtml"] = result.ReportHtml;
            reports["LogPath"] = result.LogPath;
            reports["Error"] = result.Error;
            reports["ReportKind"] = result.ReportKind;
            reports["HasFrameData"] = result.HasFrameData;
            reports["FrameCount"] = result.FrameCount;
            reports["ProcessSampleCount"] = result.ProcessSampleCount;
            reports["SystemSampleCount"] = result.SystemSampleCount;
            ApplyReportSamplerEvidence(reports, result);
            map["Reports"] = reports;
            FrameScopeReportProgress.AddTo(map, FrameScopeReportProgress.Read(result.ProgressPath));
            FrameScopeJsonFile.Write(path, Json.Serialize(map));
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("summary-update-failed run=" + runDir + " error=" + ex.Message);
        }
    }

    private static void ApplyReportSamplerEvidence(Dictionary<string, object> target, ReportGenerationResult result)
    {
        if (target == null || result == null || result.SamplerEvidenceFields == null) return;
        foreach (KeyValuePair<string, object> pair in result.SamplerEvidenceFields) target[pair.Key] = pair.Value;
    }

    private static void UpdateStatusFromReportProgress(string runDir, string progressPath, string reportHtml, string logPath)
    {
        try
        {
            var statusPath = Path.Combine(runDir, "status.json");
            var map = File.Exists(statusPath)
                ? Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(statusPath, Encoding.UTF8))
                : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (map == null) map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            map["Time"] = DateTime.Now.ToString("o");
            map["ReportHtml"] = reportHtml;
            map["ReportLog"] = logPath;
            map["ReportProgressPath"] = progressPath;
            map["ReportGenerationAttempted"] = true;
            FrameScopeReportProgress.AddTo(map, FrameScopeReportProgress.Read(progressPath));
            FrameScopeJsonFile.Write(statusPath, Json.Serialize(map));
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("status-progress-update-failed run=" + runDir + " error=" + ex.Message);
        }
    }

    private static DirectoryInfo LatestRunDirectory(string runRoot)
    {
        try
        {
            if (!Directory.Exists(runRoot)) return null;
            return new DirectoryInfo(runRoot).GetDirectories()
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object> ReadStatusDictionary(string runDir)
    {
        try
        {
            var path = Path.Combine(runDir, "status.json");
            if (!File.Exists(path)) return null;
            return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static string StatusString(Dictionary<string, object> status, string key, string fallback)
    {
        object value;
        if (status != null && status.TryGetValue(key, out value) && value != null)
        {
            return Convert.ToString(value);
        }
        return fallback;
    }

    private static int StatusInt(Dictionary<string, object> status, string key, int fallback)
    {
        object value;
        if (status != null && status.TryGetValue(key, out value) && value != null)
        {
            try { return Convert.ToInt32(value); }
            catch { }
        }
        return fallback;
    }

    private static bool StatusBool(Dictionary<string, object> status, string key, bool fallback)
    {
        object value;
        if (status != null && status.TryGetValue(key, out value) && value != null)
        {
            try { return Convert.ToBoolean(value); }
            catch { }
        }
        return fallback;
    }

    private static int? StatusNullableInt(Dictionary<string, object> status, string key)
    {
        object value;
        if (status != null && status.TryGetValue(key, out value) && value != null)
        {
            try { return Convert.ToInt32(value); }
            catch { }
        }
        return null;
    }

    private static long StatusLong(Dictionary<string, object> status, string key, long fallback)
    {
        object value;
        if (status != null && status.TryGetValue(key, out value) && value != null)
        {
            try { return Convert.ToInt64(value); }
            catch { }
        }
        return fallback;
    }

    private static FrameScopeHistoryEntry AddHistoryEntry(FrameScopeTarget target, string runDir, Dictionary<string, object> status, int monitorExitCode)
    {
        var entry = new FrameScopeHistoryEntry
        {
            Time = DateTime.Now.ToString("o"),
            Game = target.Name,
            ProcessName = target.ProcessName,
            RunDir = runDir,
            ReportHtml = StatusString(status, "ReportHtml", Path.Combine(runDir, "charts", "framescope-interactive-report.html")),
            PresentMonCsv = StatusString(status, "PresentMonCsv", Path.Combine(runDir, "presentmon.csv")),
            ProcessCsv = StatusString(status, "ProcessCsv", Path.Combine(runDir, "process-samples.csv")),
            SystemCsv = StatusString(status, "SamplesCsv", Path.Combine(runDir, "system-samples.csv")),
            SummaryPath = StatusString(status, "SummaryPath", Path.Combine(runDir, "summary.json")),
            ReportKind = StatusString(status, "ReportKind", "error"),
            ReportHasFrameData = StatusBool(status, "ReportHasFrameData", false),
            ReportFrameCount = StatusInt(status, "ReportFrameCount", 0),
            ReportProcessSampleCount = StatusInt(status, "ReportProcessSampleCount", 0),
            ReportSystemSampleCount = StatusInt(status, "ReportSystemSampleCount", 0),
            ProcessSamplerRequired = StatusBool(status, "ProcessSamplerRequired", true),
            ProcessSamplerExe = StatusString(status, "ProcessSamplerExe", ""),
            ProcessSamplerExecutableAvailable = StatusBool(status, "ProcessSamplerExecutableAvailable", false),
            ProcessSamplerStarted = StatusBool(status, "ProcessSamplerStarted", false),
            ProcessSamplerPid = StatusNullableInt(status, "ProcessSamplerPid"),
            ProcessSamplerStartedAt = StatusString(status, "ProcessSamplerStartedAt", ""),
            ProcessSamplerExitedAt = StatusString(status, "ProcessSamplerExitedAt", ""),
            ProcessSamplerExitCode = StatusNullableInt(status, "ProcessSamplerExitCode"),
            ProcessSamplerExitedEarly = StatusBool(status, "ProcessSamplerExitedEarly", false),
            ProcessSamplerStopRequested = StatusBool(status, "ProcessSamplerStopRequested", false),
            ProcessSamplerForcedStop = StatusBool(status, "ProcessSamplerForcedStop", false),
            ProcessSamplerCsvPath = StatusString(status, "ProcessSamplerCsvPath", Path.Combine(runDir, "process-samples.csv")),
            ProcessSamplerCsvExists = StatusBool(status, "ProcessSamplerCsvExists", false),
            ProcessSamplerCsvBytes = StatusLong(status, "ProcessSamplerCsvBytes", 0),
            ProcessSamplerValidRows = StatusInt(status, "ProcessSamplerValidRows", 0),
            ProcessSamplerStatus = StatusString(status, "ProcessSamplerStatus", "missing"),
            ProcessSamplerErrorTail = StatusString(status, "ProcessSamplerErrorTail", ""),
            SystemSamplerRequired = StatusBool(status, "SystemSamplerRequired", true),
            SystemSamplerExe = StatusString(status, "SystemSamplerExe", ""),
            SystemSamplerExecutableAvailable = StatusBool(status, "SystemSamplerExecutableAvailable", false),
            SystemSamplerStarted = StatusBool(status, "SystemSamplerStarted", false),
            SystemSamplerPid = StatusNullableInt(status, "SystemSamplerPid"),
            SystemSamplerStartedAt = StatusString(status, "SystemSamplerStartedAt", ""),
            SystemSamplerExitedAt = StatusString(status, "SystemSamplerExitedAt", ""),
            SystemSamplerExitCode = StatusNullableInt(status, "SystemSamplerExitCode"),
            SystemSamplerExitedEarly = StatusBool(status, "SystemSamplerExitedEarly", false),
            SystemSamplerStopRequested = StatusBool(status, "SystemSamplerStopRequested", false),
            SystemSamplerForcedStop = StatusBool(status, "SystemSamplerForcedStop", false),
            SystemSamplerCsvPath = StatusString(status, "SystemSamplerCsvPath", Path.Combine(runDir, "system-samples.csv")),
            SystemSamplerCsvExists = StatusBool(status, "SystemSamplerCsvExists", false),
            SystemSamplerCsvBytes = StatusLong(status, "SystemSamplerCsvBytes", 0),
            SystemSamplerValidRows = StatusInt(status, "SystemSamplerValidRows", 0),
            SystemSamplerStatus = StatusString(status, "SystemSamplerStatus", "missing"),
            SystemSamplerErrorTail = StatusString(status, "SystemSamplerErrorTail", ""),
            MonitorExitCode = monitorExitCode
        };

        File.AppendAllText(HistoryPath, Json.Serialize(entry) + Environment.NewLine);
        return entry;
    }

    private static bool ShouldOpenReport(FrameScopeTarget target, FrameScopeConfig config)
    {
        return target.OpenReportOnComplete && config.OpenReportOnComplete;
    }

    private static bool ShouldAutoOpenCompletedReport(Dictionary<string, object> status)
    {
        if (StatusBool(status, "ReportHasFrameData", false)) return true;
        if (StatusInt(status, "ReportProcessSampleCount", 0) > 0) return true;
        if (StatusInt(status, "ReportSystemSampleCount", 0) > 0) return true;
        return false;
    }

    private static FrameScopeHistoryEntry LatestHistory()
    {
        if (!File.Exists(HistoryPath)) return null;
        var line = File.ReadLines(HistoryPath).LastOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (line == null) return null;
        try { return Json.Deserialize<FrameScopeHistoryEntry>(line); }
        catch { return null; }
    }
}
