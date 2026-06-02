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
            string displayName = ExtractJsonString(text, "TargetDisplayName");
            if (string.IsNullOrEmpty(displayName)) displayName = ExtractJsonString(text, "ConfiguredTargetName");
            if (string.IsNullOrEmpty(displayName)) displayName = ExtractJsonString(text, "TargetName");
            string target = ExtractJsonString(text, "TargetProcessName");
            if (string.IsNullOrEmpty(target)) target = ExtractJsonString(text, "TargetProcess");
            if (string.IsNullOrEmpty(target)) target = ExtractJsonString(text, "Target");
            if (!string.IsNullOrEmpty(target))
            {
                result["targetProcess"] = target;
                result["targetDisplayName"] = string.IsNullOrWhiteSpace(displayName) ? target : displayName;
                return result;
            }
        }
        result["targetProcess"] = "cs2.exe";
        result["targetDisplayName"] = "cs2.exe";
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
                CopyDiagnosticValue(map, result, "PresentMonFailureCategory");
                CopyDiagnosticValue(map, result, "PresentMonEtwAccessDenied");
                CopyDiagnosticValue(map, result, "PresentMonCsvExists");
                CopyDiagnosticValue(map, result, "PresentMonCsvBytes");
                CopyDiagnosticValue(map, result, "PresentMonCsvRows");
                CopyDiagnosticValue(map, result, "PresentMonCsvPath");
                CopyDiagnosticValue(map, result, "PresentMonCsvLastCheckTime");
                CopyDiagnosticValue(map, result, "PresentMonCsvPostExitWaitMs");
                CopyDiagnosticValue(map, result, "PresentMonRuntimeMs");
                CopyDiagnosticValue(map, result, "PresentMonStartedAt");
                CopyDiagnosticValue(map, result, "PresentMonExitedAt");
                CopyDiagnosticValue(map, result, "PresentMonExitCode");
                CopyDiagnosticValue(map, result, "PresentMonExitedEarly");
                CopyDiagnosticValue(map, result, "PresentMonForcedStop");
                CopyDiagnosticValue(map, result, "PresentMonStdoutTail");
                CopyDiagnosticValue(map, result, "PresentMonStderrTail");
                CopyDiagnosticValue(map, result, "PresentMonCaptureMode");
                CopyDiagnosticValue(map, result, "PresentMonCaptureTarget");
                CopyDiagnosticValue(map, result, "PresentMonArgs");
                CopyDiagnosticValue(map, result, "PresentMonPreflightIsElevated");
                CopyDiagnosticValue(map, result, "PresentMonPreflightInPerformanceLogUsers");
                CopyDiagnosticValue(map, result, "PresentMonPreflightToolExists");
                CopyDiagnosticValue(map, result, "PresentMonPreflightToolPath");
                CopyDiagnosticValue(map, result, "PresentMonPreflightEtwProbeAttempted");
                CopyDiagnosticValue(map, result, "PresentMonPreflightEtwProbeReason");
                CopyDiagnosticValue(map, result, "TargetProcess");
                CopyDiagnosticValue(map, result, "TargetDisplayName");
                CopyDiagnosticValue(map, result, "TargetResolvedProcess");
                CopyDiagnosticValue(map, result, "TargetPid");
                CopyDiagnosticValue(map, result, "InitialTargetPid");
                CopyDiagnosticValue(map, result, "TargetRunningAtPresentMonExitCheck");
                CopyDiagnosticValue(map, result, "TargetPidRunningAtPresentMonExitCheck");
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
        if (!result.ContainsKey("PresentMonCsvPath"))
        {
            result["PresentMonCsvPath"] = Path.Combine(runDir, "presentmon.csv");
        }
        if (!result.ContainsKey("PresentMonCsvLastCheckTime"))
        {
            result["PresentMonCsvLastCheckTime"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
        }

        string frameCaptureStatus = Convert.ToString(result.ContainsKey("FrameCaptureStatus") ? result["FrameCaptureStatus"] : null, CultureInfo.InvariantCulture);
        if (string.Equals(frameCaptureStatus, FrameScopePresentMonDiagnostics.EtwAccessDeniedStatus, StringComparison.OrdinalIgnoreCase))
        {
            result["FrameCaptureStatus"] = FrameScopePresentMonDiagnostics.EtwAccessDeniedStatus;
            result["FrameCaptureMessage"] = FrameScopePresentMonDiagnostics.EtwAccessDeniedMessage;
            result["PresentMonFailureCategory"] = FrameScopePresentMonDiagnostics.EtwAccessDeniedStatus;
            result["PresentMonEtwAccessDenied"] = true;
        }
        else if (string.Equals(frameCaptureStatus, "no-presentmon-csv", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(frameCaptureStatus, "missing-presentmon-csv", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(frameCaptureStatus, FrameScopePresentMonDiagnostics.SilentNoCsvStatus, StringComparison.OrdinalIgnoreCase))
        {
            bool csvExists = GetBoolDiagnostic(result, "PresentMonCsvExists", File.Exists(Path.Combine(runDir, "presentmon.csv")));
            int? exitCode = result.ContainsKey("PresentMonExitCode") && result["PresentMonExitCode"] != null
                ? (int?)GetIntDiagnostic(result, "PresentMonExitCode", -9999)
                : null;
            string stdoutTail = GetStringDiagnostic(result, "PresentMonStdoutTail", "");
            string stderrTail = GetStringDiagnostic(result, "PresentMonStderrTail", "");
            FrameScopePresentMonCaptureDiagnosticContext context = BuildPresentMonContextFromDiagnostics(result);
            if (stderrTail.IndexOf("failed to start trace session", StringComparison.OrdinalIgnoreCase) >= 0
                && stderrTail.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result["FrameCaptureStatus"] = FrameScopePresentMonDiagnostics.EtwAccessDeniedStatus;
                result["FrameCaptureMessage"] = FrameScopePresentMonDiagnostics.EtwAccessDeniedMessage;
                result["PresentMonFailureCategory"] = FrameScopePresentMonDiagnostics.EtwAccessDeniedStatus;
                result["PresentMonEtwAccessDenied"] = true;
            }
            else if (FrameScopePresentMonDiagnostics.IsSilentNoCsvResult(csvExists, exitCode, stdoutTail, stderrTail))
            {
                result["FrameCaptureStatus"] = FrameScopePresentMonDiagnostics.SilentNoCsvStatus;
                result["FrameCaptureMessage"] = FrameScopePresentMonDiagnostics.BuildSilentNoCsvMessage(context);
                result["PresentMonFailureCategory"] = FrameScopePresentMonDiagnostics.SilentNoCsvStatus;
                result["PresentMonEtwAccessDenied"] = false;
            }
            else
            {
                result["FrameCaptureMessage"] = FrameScopePresentMonDiagnostics.BuildGenericNoCsvMessage(context);
                if (!result.ContainsKey("PresentMonFailureCategory") || String.IsNullOrWhiteSpace(Convert.ToString(result["PresentMonFailureCategory"], CultureInfo.InvariantCulture)))
                {
                    result["PresentMonFailureCategory"] = "missing-presentmon-csv";
                }
            }
        }
        else
        {
            object messageValue;
            if (result.TryGetValue("FrameCaptureMessage", out messageValue) && messageValue != null)
            {
                string message = Convert.ToString(messageValue, CultureInfo.InvariantCulture);
                if (message.IndexOf("PUBG", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result["FrameCaptureMessage"] = FrameScopePresentMonDiagnostics.BuildGenericNoCsvMessage(BuildPresentMonContextFromDiagnostics(result));
                }
            }
        }

        return result;
    }

    private static FrameScopePresentMonCaptureDiagnosticContext BuildPresentMonContextFromDiagnostics(Dictionary<string, object> diagnostics)
    {
        return new FrameScopePresentMonCaptureDiagnosticContext
        {
            TargetProcessName = GetStringDiagnostic(diagnostics, "TargetProcess", ""),
            TargetResolvedProcess = GetStringDiagnostic(diagnostics, "TargetResolvedProcess", ""),
            TargetPid = diagnostics != null && diagnostics.ContainsKey("TargetPid") ? (int?)GetIntDiagnostic(diagnostics, "TargetPid", 0) : null,
            PresentMonArgs = GetStringDiagnostic(diagnostics, "PresentMonArgs", ""),
            PresentMonRuntimeMs = diagnostics != null && diagnostics.ContainsKey("PresentMonRuntimeMs") ? (long?)GetIntDiagnostic(diagnostics, "PresentMonRuntimeMs", 0) : null,
            PresentMonStartedAt = GetStringDiagnostic(diagnostics, "PresentMonStartedAt", ""),
            PresentMonExitedAt = GetStringDiagnostic(diagnostics, "PresentMonExitedAt", ""),
            TargetRunningAtPresentMonExitCheck = diagnostics != null && diagnostics.ContainsKey("TargetRunningAtPresentMonExitCheck") ? (bool?)GetBoolDiagnostic(diagnostics, "TargetRunningAtPresentMonExitCheck", false) : null,
            TargetPidRunningAtPresentMonExitCheck = diagnostics != null && diagnostics.ContainsKey("TargetPidRunningAtPresentMonExitCheck") ? (bool?)GetBoolDiagnostic(diagnostics, "TargetPidRunningAtPresentMonExitCheck", false) : null,
            PresentMonCsvPostExitWaitMs = diagnostics != null && diagnostics.ContainsKey("PresentMonCsvPostExitWaitMs") ? (long?)GetIntDiagnostic(diagnostics, "PresentMonCsvPostExitWaitMs", 0) : null
        };
    }

    private static Dictionary<string, object> LoadCpuCoreTelemetryMetadata(string runDir, Dictionary<string, object> captureDiagnostics)
    {
        int csvRows = CountCsvDataRows(Path.Combine(runDir, "cpu-core-samples.csv"));
        int voltageCsvRows = CountCsvDataRows(Path.Combine(runDir, "cpu-voltage-samples.csv"));
        Dictionary<string, object> telemetryStatus = LoadCpuCoreTelemetryStatus(runDir);
        int statusRows = GetIntDiagnostic(telemetryStatus, "CpuCoreSampleCount", 0);
        int sampleCount = Math.Max(csvRows, statusRows);
        bool telemetryAvailable = sampleCount > 0 || GetBoolDiagnostic(telemetryStatus, "CpuCoreTelemetryAvailable", false);
        int voltageStatusRows = GetIntDiagnostic(telemetryStatus, "CpuVoltageSampleCount", 0);
        int voltageSampleCount = Math.Max(voltageCsvRows, voltageStatusRows);
        bool voltageAvailable = voltageSampleCount > 0 || GetBoolDiagnostic(telemetryStatus, "CpuVoltageAvailable", false);

        return new Dictionary<string, object>
        {
            { "cpuCoreSampleCount", sampleCount },
            { "cpuCoreTelemetryAvailable", telemetryAvailable },
            { "cpuVoltageAvailable", voltageAvailable },
            { "cpuVoltageStatus", GetStringDiagnostic(telemetryStatus, "CpuVoltageStatus", voltageAvailable ? "available" : "unavailable") },
            { "cpuVoltageReason", GetStringDiagnostic(telemetryStatus, "CpuVoltageUnavailableReason", "当前 run 未包含真实 per-core voltage 字段；不会显示伪造 VID/Vcore。") },
            { "cpuVoltageSource", GetStringDiagnostic(telemetryStatus, "CpuVoltageSource", GetStringDiagnostic(telemetryStatus, "CpuVoltageTelemetrySource", "")) },
            { "cpuVoltageSampleCount", voltageSampleCount },
            { "cpuVoltageSampleIntervalMs", GetIntDiagnostic(telemetryStatus, "CpuVoltageSampleIntervalMs", 0) },
            { "cpuVoltageSamplesCsv", GetStringDiagnostic(telemetryStatus, "CpuVoltageSamplesCsv", Path.Combine(runDir, "cpu-voltage-samples.csv")) }
        };
    }

    private static Dictionary<string, object> LoadCpuCoreTelemetryMetadataStrict(string runDir, Dictionary<string, object> captureDiagnostics)
    {
        int csvRows = CountCsvDataRows(Path.Combine(runDir, "cpu-core-samples.csv"));
        CpuVoltageCsvCounts voltageCsvRows = CountCpuVoltageCsvRows(Path.Combine(runDir, "cpu-voltage-samples.csv"));
        CpuVidCsvCounts vidCsvRows = CountCpuVidCsvRows(Path.Combine(runDir, "cpu-vid-samples.csv"));
        Dictionary<string, object> telemetryStatus = LoadCpuCoreTelemetryStatusStrict(runDir);
        int statusRows = GetIntDiagnostic(telemetryStatus, "CpuCoreSampleCount", 0);
        int sampleCount = Math.Max(csvRows, statusRows);
        bool telemetryAvailable = sampleCount > 0 || GetBoolDiagnostic(telemetryStatus, "CpuCoreTelemetryAvailable", false);
        int voltageSampleCount = Math.Max(voltageCsvRows.Vcore, GetIntDiagnostic(telemetryStatus, "CpuVoltageVcoreSampleCount", GetIntDiagnostic(telemetryStatus, "CpuVoltageSampleCount", 0)));
        int vcoreVoltageSampleCount = Math.Max(voltageCsvRows.Vcore, GetIntDiagnostic(telemetryStatus, "CpuVoltageVcoreSampleCount", voltageSampleCount));
        int perCoreVoltageSampleCount = 0;
        int nonPerCoreVoltageSampleCount = Math.Max(voltageCsvRows.NonPerCore, GetIntDiagnostic(telemetryStatus, "CpuVoltageNonPerCoreSampleCount", 0));
        int rejectedVoltageSampleCount = Math.Max(voltageCsvRows.Rejected, GetIntDiagnostic(telemetryStatus, "CpuVoltageRejectedSampleCount", nonPerCoreVoltageSampleCount));
        nonPerCoreVoltageSampleCount = Math.Max(nonPerCoreVoltageSampleCount, rejectedVoltageSampleCount);
        bool voltageVcoreAvailable = vcoreVoltageSampleCount > 0 || GetBoolDiagnostic(telemetryStatus, "CpuVoltageVcoreAvailable", false);
        bool voltagePerCoreAvailable = false;
        bool voltageNonPerCoreAvailable = nonPerCoreVoltageSampleCount > 0 || GetBoolDiagnostic(telemetryStatus, "CpuVoltageNonPerCoreAvailable", false);
        bool voltageAvailable = voltageVcoreAvailable;
        string voltageStatus = voltageAvailable
            ? "vcore-available"
            : GetStringDiagnostic(telemetryStatus, "CpuVoltageStatus", voltageNonPerCoreAvailable ? "non-per-core-only" : "unavailable");
        string voltageReason = voltageAvailable
            ? ""
            : GetStringDiagnostic(telemetryStatus, "CpuVoltageUnavailableReason", "No explicit CPU Vcore/CPU Voltage sensor was recorded; VID/SOC/Package/VBAT/VIN are not used as CPU Voltage.");
        int vidSampleCount = Math.Max(vidCsvRows.Total, GetIntDiagnostic(telemetryStatus, "CpuVidSampleCount", 0));
        int vidCoreCount = Math.Max(vidCsvRows.CoreCount, GetIntDiagnostic(telemetryStatus, "CpuVidCoreCount", 0));
        bool vidAvailable = vidSampleCount > 0 || GetBoolDiagnostic(telemetryStatus, "CpuVidAvailable", false);
        string vidReason = vidAvailable
            ? ""
            : GetStringDiagnostic(telemetryStatus, "CpuVidUnavailableReason", "\u672a\u68c0\u6d4b\u5230 CPU \u6838\u5fc3 VID \u4f20\u611f\u5668\uff1b\u4e0d\u751f\u6210\u5047\u6570\u636e\u3002");

        return new Dictionary<string, object>
        {
            { "cpuCoreSampleCount", sampleCount },
            { "cpuCoreTelemetryAvailable", telemetryAvailable },
            { "cpuVoltageAvailable", voltageAvailable },
            { "cpuVoltageVcoreAvailable", voltageVcoreAvailable },
            { "cpuVoltagePerCoreAvailable", voltagePerCoreAvailable },
            { "cpuVoltageNonPerCoreAvailable", voltageNonPerCoreAvailable },
            { "cpuVoltageStatus", voltageStatus },
            { "cpuVoltageReason", voltageReason },
            { "cpuVoltageSource", GetStringDiagnostic(telemetryStatus, "CpuVoltageSource", GetStringDiagnostic(telemetryStatus, "CpuVoltageTelemetrySource", "")) },
            { "cpuVoltageProviderKind", GetStringDiagnostic(telemetryStatus, "CpuVoltageProviderKind", "") },
            { "cpuVoltageProviderRequested", GetStringDiagnostic(telemetryStatus, "CpuVoltageProviderRequested", "") },
            { "cpuVoltageSampleCount", voltageSampleCount },
            { "cpuVoltageVcoreSampleCount", vcoreVoltageSampleCount },
            { "cpuVoltagePerCoreSampleCount", perCoreVoltageSampleCount },
            { "cpuVoltageNonPerCoreSampleCount", nonPerCoreVoltageSampleCount },
            { "cpuVoltageRejectedSampleCount", rejectedVoltageSampleCount },
            { "cpuVoltageSampleIntervalMs", GetIntDiagnostic(telemetryStatus, "CpuVoltageSampleIntervalMs", 0) },
            { "cpuVoltageSamplesCsv", GetStringDiagnostic(telemetryStatus, "CpuVoltageSamplesCsv", Path.Combine(runDir, "cpu-voltage-samples.csv")) },
            { "cpuVidAvailable", vidAvailable },
            { "cpuVidStatus", GetStringDiagnostic(telemetryStatus, "CpuVidStatus", vidAvailable ? "core-vid-available" : "unavailable") },
            { "cpuVidReason", vidReason },
            { "cpuVidNote", GetStringDiagnostic(telemetryStatus, "CpuVidNote", "VID \u662f CPU \u8bf7\u6c42/\u76ee\u6807\u7535\u538b\uff0c\u4e0d\u662f\u771f\u5b9e per-core Vcore\u3002") },
            { "cpuVidSource", GetStringDiagnostic(telemetryStatus, "CpuVidSource", GetStringDiagnostic(telemetryStatus, "CpuVidTelemetrySource", "")) },
            { "cpuVidProviderKind", GetStringDiagnostic(telemetryStatus, "CpuVidProviderKind", "") },
            { "cpuVidProviderRequested", GetStringDiagnostic(telemetryStatus, "CpuVidProviderRequested", "") },
            { "cpuVidSampleCount", vidSampleCount },
            { "cpuVidCoreCount", vidCoreCount },
            { "cpuVidSampleIntervalMs", GetIntDiagnostic(telemetryStatus, "CpuVidSampleIntervalMs", 0) },
            { "cpuVidSamplesCsv", GetStringDiagnostic(telemetryStatus, "CpuVidSamplesCsv", Path.Combine(runDir, "cpu-vid-samples.csv")) }
        };
    }

    private static Dictionary<string, object> LoadCpuCoreTelemetryStatusStrict(string runDir)
    {
        Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in new[]
        {
            Path.Combine(runDir, "status.json"),
            Path.Combine(runDir, "summary.json")
        })
        {
            if (!File.Exists(path)) continue;
            try
            {
                Dictionary<string, object> map = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                if (map == null) continue;
                CopyCpuCoreTelemetryDiagnosticValues(map, result);
                CopyCpuVoltageTelemetryDiagnosticValuesStrict(map, result);
                CopyCpuVidTelemetryDiagnosticValues(map, result);
            }
            catch { }
        }
        CopyCpuCoreTelemetryStatusFile(Path.Combine(runDir, "cpu-core-telemetry-status.json"), result);
        CopyCpuVoltageTelemetryStatusFileStrict(Path.Combine(runDir, "cpu-voltage-telemetry-status.json"), result);
        CopyCpuVidTelemetryStatusFile(Path.Combine(runDir, "cpu-vid-telemetry-status.json"), result);
        return result;
    }

    private static Dictionary<string, object> LoadCpuCoreTelemetryStatus(string runDir)
    {
        Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in new[]
        {
            Path.Combine(runDir, "status.json"),
            Path.Combine(runDir, "summary.json")
        })
        {
            if (!File.Exists(path)) continue;
            try
            {
                Dictionary<string, object> map = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                if (map == null) continue;
                CopyCpuCoreTelemetryDiagnosticValues(map, result);
                CopyCpuVoltageTelemetryDiagnosticValues(map, result);
            }
            catch { }
        }
        CopyCpuCoreTelemetryStatusFile(Path.Combine(runDir, "cpu-core-telemetry-status.json"), result);
        CopyCpuVoltageTelemetryStatusFile(Path.Combine(runDir, "cpu-voltage-telemetry-status.json"), result);
        return result;
    }

    private static void CopyCpuCoreTelemetryStatusFile(string path, Dictionary<string, object> result)
    {
        if (!File.Exists(path)) return;
        try
        {
            Dictionary<string, object> map = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (map != null) CopyCpuCoreTelemetryDiagnosticValues(map, result);
        }
        catch { }
    }

    private static void CopyCpuVoltageTelemetryStatusFileStrict(string path, Dictionary<string, object> result)
    {
        if (!File.Exists(path)) return;
        try
        {
            Dictionary<string, object> map = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (map != null) CopyCpuVoltageTelemetryDiagnosticValuesStrict(map, result);
        }
        catch { }
    }

    private static void CopyCpuVidTelemetryStatusFile(string path, Dictionary<string, object> result)
    {
        if (!File.Exists(path)) return;
        try
        {
            Dictionary<string, object> map = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (map != null) CopyCpuVidTelemetryDiagnosticValues(map, result);
        }
        catch { }
    }

    private static void CopyCpuVoltageTelemetryStatusFile(string path, Dictionary<string, object> result)
    {
        if (!File.Exists(path)) return;
        try
        {
            Dictionary<string, object> map = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (map != null) CopyCpuVoltageTelemetryDiagnosticValues(map, result);
        }
        catch { }
    }

    private static void CopyCpuCoreTelemetryDiagnosticValues(Dictionary<string, object> source, Dictionary<string, object> target)
    {
        CopyDiagnosticValue(source, target, "CpuCoreTelemetryAvailable");
        CopyDiagnosticValue(source, target, "CpuCoreSampleCount");
    }

    private static void CopyCpuVoltageTelemetryDiagnosticValuesStrict(Dictionary<string, object> source, Dictionary<string, object> target)
    {
        CopyDiagnosticValue(source, target, "CpuVoltageAvailable");
        CopyDiagnosticValue(source, target, "CpuVoltageVcoreAvailable");
        CopyDiagnosticValue(source, target, "CpuVoltagePerCoreAvailable");
        CopyDiagnosticValue(source, target, "CpuVoltageNonPerCoreAvailable");
        CopyDiagnosticValue(source, target, "CpuVoltageStatus");
        CopyDiagnosticValue(source, target, "CpuVoltageUnavailableReason");
        CopyDiagnosticValue(source, target, "CpuVoltageSource");
        CopyDiagnosticValue(source, target, "CpuVoltageProviderKind");
        CopyDiagnosticValue(source, target, "CpuVoltageProviderRequested");
        CopyDiagnosticValue(source, target, "CpuVoltageTelemetrySource");
        CopyDiagnosticValue(source, target, "CpuVoltageSampleCount");
        CopyDiagnosticValue(source, target, "CpuVoltageVcoreSampleCount");
        CopyDiagnosticValue(source, target, "CpuVoltagePerCoreSampleCount");
        CopyDiagnosticValue(source, target, "CpuVoltageNonPerCoreSampleCount");
        CopyDiagnosticValue(source, target, "CpuVoltageRejectedSampleCount");
        CopyDiagnosticValue(source, target, "CpuVoltageSampleIntervalMs");
        CopyDiagnosticValue(source, target, "CpuVoltageSamplesCsv");
    }

    private static void CopyCpuVidTelemetryDiagnosticValues(Dictionary<string, object> source, Dictionary<string, object> target)
    {
        CopyDiagnosticValue(source, target, "CpuVidAvailable");
        CopyDiagnosticValue(source, target, "CpuVidStatus");
        CopyDiagnosticValue(source, target, "CpuVidUnavailableReason");
        CopyDiagnosticValue(source, target, "CpuVidNote");
        CopyDiagnosticValue(source, target, "CpuVidSource");
        CopyDiagnosticValue(source, target, "CpuVidProviderKind");
        CopyDiagnosticValue(source, target, "CpuVidProviderRequested");
        CopyDiagnosticValue(source, target, "CpuVidTelemetrySource");
        CopyDiagnosticValue(source, target, "CpuVidSampleCount");
        CopyDiagnosticValue(source, target, "CpuVidCoreCount");
        CopyDiagnosticValue(source, target, "CpuVidSampleIntervalMs");
        CopyDiagnosticValue(source, target, "CpuVidSamplesCsv");
    }

    private static void CopyCpuVoltageTelemetryDiagnosticValues(Dictionary<string, object> source, Dictionary<string, object> target)
    {
        CopyDiagnosticValue(source, target, "CpuVoltageAvailable");
        CopyDiagnosticValue(source, target, "CpuVoltageVcoreAvailable");
        CopyDiagnosticValue(source, target, "CpuVoltageStatus");
        CopyDiagnosticValue(source, target, "CpuVoltageUnavailableReason");
        CopyDiagnosticValue(source, target, "CpuVoltageSource");
        CopyDiagnosticValue(source, target, "CpuVoltageTelemetrySource");
        CopyDiagnosticValue(source, target, "CpuVoltageSampleCount");
        CopyDiagnosticValue(source, target, "CpuVoltageVcoreSampleCount");
        CopyDiagnosticValue(source, target, "CpuVoltageRejectedSampleCount");
        CopyDiagnosticValue(source, target, "CpuVoltageSampleIntervalMs");
        CopyDiagnosticValue(source, target, "CpuVoltageSamplesCsv");
    }

    private static CpuVoltageCsvCounts CountCpuVoltageCsvRows(string path)
    {
        CpuVoltageCsvCounts result = new CpuVoltageCsvCounts();
        if (!File.Exists(path)) return result;
        try
        {
            using (CsvTable table = CsvTable.Open(path))
            {
                Dictionary<string, int> h = table.Headers;
                string voltageHeader = FindVoltageHeader(h);
                List<string> row;
                while ((row = table.ReadRow()) != null)
                {
                    double? volts = String.IsNullOrWhiteSpace(voltageHeader) ? null : ParseNullableDouble(Get(row, h, voltageHeader));
                    if (!volts.HasValue || volts.Value <= 0 || volts.Value >= 5) continue;
                    string status = (Get(row, h, "Status") ?? "").Trim();
                    string sensorName = Get(row, h, "SensorName");
                    string sensorIdentifier = Get(row, h, "SensorIdentifier");
                    if (IsCpuVcoreVoltageCsvRow(sensorName, sensorIdentifier, status))
                    {
                        result.Total++;
                        result.Vcore++;
                    }
                    else
                    {
                        result.NonPerCore++;
                        result.Rejected++;
                    }
                }
            }
        }
        catch
        {
        }
        return result;
    }

    private static CpuVidCsvCounts CountCpuVidCsvRows(string path)
    {
        CpuVidCsvCounts result = new CpuVidCsvCounts();
        if (!File.Exists(path)) return result;
        try
        {
            HashSet<string> cores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (CsvTable table = CsvTable.Open(path))
            {
                Dictionary<string, int> h = table.Headers;
                string vidHeader = FindVidHeader(h);
                List<string> row;
                while ((row = table.ReadRow()) != null)
                {
                    double? volts = String.IsNullOrWhiteSpace(vidHeader) ? null : ParseNullableDouble(Get(row, h, vidHeader));
                    if (!volts.HasValue || volts.Value <= 0 || volts.Value >= 5) continue;
                    string status = (Get(row, h, "Status") ?? "").Trim();
                    if (!String.IsNullOrWhiteSpace(status) &&
                        !String.Equals(status, "core-vid", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(status, "vid", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string key = CpuVidCoreKey(Get(row, h, "ProcessorGroup"), Get(row, h, "LogicalProcessor"), Get(row, h, "CoreIndex"));
                    if (String.IsNullOrWhiteSpace(key)) continue;
                    result.Total++;
                    cores.Add(key);
                }
            }
            result.CoreCount = cores.Count;
        }
        catch
        {
        }
        return result;
    }

    private static int CountCsvDataRows(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            int count = 0;
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
            {
                if (reader.ReadLine() == null) return 0;
                while (reader.ReadLine() != null) count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static int GetIntDiagnostic(Dictionary<string, object> map, string key, int fallback)
    {
        object value;
        if (map == null || !map.TryGetValue(key, out value) || value == null) return fallback;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    private static bool GetBoolDiagnostic(Dictionary<string, object> map, string key, bool fallback)
    {
        object value;
        if (map == null || !map.TryGetValue(key, out value) || value == null) return fallback;
        try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    private static string GetStringDiagnostic(Dictionary<string, object> map, string key, string fallback)
    {
        object value;
        if (map == null || !map.TryGetValue(key, out value) || value == null) return fallback;
        string text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return String.IsNullOrWhiteSpace(text) ? fallback : text;
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
