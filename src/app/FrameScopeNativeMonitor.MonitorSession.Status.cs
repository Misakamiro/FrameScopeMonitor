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
    private static void WriteNativeMonitorStatus(MonitorSessionPaths paths, string phase, string targetProcess, string captureMode, int sampleIntervalMs, int processSampleIntervalMs, int slowSampleIntervalMs, int controlPollIntervalMs, string presentMonPath, string processSamplerPath, string systemSamplerPath, Dictionary<string, object> extra)
    {
        FrameScopeMonitorOwnerIdentity monitorIdentity = FrameScopeReportRecoveryPolicy.CaptureCurrentProcessIdentity();
        var status = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            { "Time", DateTime.Now.ToString("o") },
            { "Phase", phase },
            { "RunDir", paths.RunDir },
            { "MonitorScript", NativeMonitorMode },
            { "MonitorMode", NativeMonitorMode },
            { "MonitorPid", monitorIdentity.ProcessId },
            { "MonitorProcessPath", monitorIdentity.ExecutablePath },
            { "MonitorStartedAtUtc", monitorIdentity.StartedAtUtcText },
            { "PresentMonCsv", paths.PresentMonCsv },
            { "PresentMonExe", presentMonPath },
            { "PresentMonOut", paths.PresentMonStdout },
            { "PresentMonErr", paths.PresentMonStderr },
            { "PresentMonInfo", paths.PresentMonInfoPath },
            { "SamplesCsv", paths.SamplesCsv },
            { "CpuCoreSamplesCsv", paths.CpuCoreSamplesCsv },
            { "CpuVoltageSamplesCsv", paths.CpuVoltageSamplesCsv },
            { "CpuVidSamplesCsv", paths.CpuVidSamplesCsv },
            { "ProcessCsv", paths.ProcessCsv },
            { "TopCpuCsv", paths.TopCpuCsv },
            { "TopIoCsv", paths.TopIoCsv },
            { "AlertsCsv", paths.AlertsCsv },
            { "EventsCsv", paths.EventsCsv },
            { "SummaryPath", paths.SummaryPath },
            { "ReportLog", paths.ReportLogPath },
            { "SlowSamplerLog", paths.SlowSamplerLogPath },
            { "TargetProcess", targetProcess },
            { "TargetDisplayName", targetProcess },
            { "CaptureMode", captureMode },
            { "SampleIntervalMs", sampleIntervalMs },
            { "ProcessSampleIntervalMs", processSampleIntervalMs },
            { "ControlPollIntervalMs", controlPollIntervalMs },
            { "SlowSampleIntervalMs", slowSampleIntervalMs },
            { "ProcessSamplingMode", "native-all-process-groups" },
            { "ProcessSamplerExe", processSamplerPath },
            { "SystemSamplingMode", string.IsNullOrWhiteSpace(systemSamplerPath) ? "native-system-missing" : "native-system-slow" },
            { "SystemSamplerExe", systemSamplerPath },
            { "CpuCoreTelemetryStatusPath", paths.CpuCoreTelemetryStatusPath },
            { "CpuVoltageTelemetryStatusPath", paths.CpuVoltageTelemetryStatusPath },
            { "CpuVidTelemetryStatusPath", paths.CpuVidTelemetryStatusPath }
        };

        AddDictionary(status, BuildNativeMonitorSamplerDiagnostics(
            CreateNativeMonitorSamplerState(true, processSamplerPath, paths.ProcessCsv, paths.ProcessSamplerStdout, paths.ProcessSamplerStderr),
            CreateNativeMonitorSamplerState(true, systemSamplerPath, paths.SamplesCsv, paths.SystemSamplerStdout, paths.SystemSamplerStderr)));

        if (extra != null)
        {
            foreach (var pair in extra) status[pair.Key] = pair.Value;
        }

        FrameScopeJsonFile.Write(paths.StatusPath, Json.Serialize(status));
    }

    private static void WriteNativeMonitorSummary(MonitorSessionPaths paths, string targetProcess, string captureMode, int sampleIntervalMs, int processSampleIntervalMs, int slowSampleIntervalMs, int controlPollIntervalMs, string presentMonPath, int? presentMonExitCode, bool presentMonExitedEarly, bool presentMonForcedStop, string reportHtml, string presentMonCaptureMode, string presentMonCaptureTarget, string presentMonArguments, Dictionary<string, object> captureDiagnostics)
    {
        var reports = new Dictionary<string, object>
        {
            { "Attempted", false },
            { "ExitCode", null },
            { "ReportHtml", reportHtml },
            { "PreviewPng", null },
            { "LogPath", paths.ReportLogPath },
            { "Error", null }
        };
        var summary = new Dictionary<string, object>
        {
            { "RunDir", paths.RunDir },
            { "MonitorScript", NativeMonitorMode },
            { "MonitorMode", NativeMonitorMode },
            { "PresentMonCsv", paths.PresentMonCsv },
            { "PresentMonExe", presentMonPath },
            { "PresentMonStdout", paths.PresentMonStdout },
            { "PresentMonStderr", paths.PresentMonStderr },
            { "PresentMonInfo", paths.PresentMonInfoPath },
            { "PresentMonCaptureMode", presentMonCaptureMode },
            { "PresentMonCaptureTarget", presentMonCaptureTarget },
            { "PresentMonArgs", presentMonArguments },
            { "PresentMonExitCode", presentMonExitCode },
            { "PresentMonExitedEarly", presentMonExitedEarly },
            { "PresentMonForcedStop", presentMonForcedStop },
            { "SamplesCsv", paths.SamplesCsv },
            { "CpuCoreSamplesCsv", paths.CpuCoreSamplesCsv },
            { "CpuVoltageSamplesCsv", paths.CpuVoltageSamplesCsv },
            { "CpuVidSamplesCsv", paths.CpuVidSamplesCsv },
            { "ProcessCsv", paths.ProcessCsv },
            { "TopCpuCsv", paths.TopCpuCsv },
            { "TopIoCsv", paths.TopIoCsv },
            { "AlertsCsv", paths.AlertsCsv },
            { "EventsCsv", paths.EventsCsv },
            { "TargetProcess", targetProcess },
            { "CaptureMode", captureMode },
            { "SampleIntervalMs", sampleIntervalMs },
            { "ProcessSampleIntervalMs", processSampleIntervalMs },
            { "ControlPollIntervalMs", controlPollIntervalMs },
            { "SlowSampleIntervalMs", slowSampleIntervalMs },
            { "Reports", reports },
            { "CpuCoreTelemetryStatusPath", paths.CpuCoreTelemetryStatusPath },
            { "CpuVoltageTelemetryStatusPath", paths.CpuVoltageTelemetryStatusPath },
            { "CpuVidTelemetryStatusPath", paths.CpuVidTelemetryStatusPath },
            { "Notes", new[] { "Monitor session was captured by FrameScopeMonitor.exe native C# mode. Report generation is handled by the native watcher after capture." } }
        };
        AddDictionary(summary, captureDiagnostics);
        FrameScopeJsonFile.Write(paths.SummaryPath, Json.Serialize(summary));
    }
}
