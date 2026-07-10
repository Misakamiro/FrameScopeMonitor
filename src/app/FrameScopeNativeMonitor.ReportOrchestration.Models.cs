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

public sealed class FrameScopeHistoryEntry
{
    public FrameScopeHistoryEntry()
    {
        Time = "";
        Game = "";
        ProcessName = "";
        RunDir = "";
        ReportHtml = "";
        PresentMonCsv = "";
        ProcessCsv = "";
        SystemCsv = "";
        SummaryPath = "";
        ReportKind = "";
        ProcessSamplerExe = "";
        ProcessSamplerStartedAt = "";
        ProcessSamplerExitedAt = "";
        ProcessSamplerCsvPath = "";
        ProcessSamplerStatus = "";
        ProcessSamplerErrorTail = "";
        SystemSamplerExe = "";
        SystemSamplerStartedAt = "";
        SystemSamplerExitedAt = "";
        SystemSamplerCsvPath = "";
        SystemSamplerStatus = "";
        SystemSamplerErrorTail = "";
        ReportGenerationStartedAt = "";
        ReportGenerationEndedAt = "";
        MonitorExitCode = 0;
    }

    public string Time { get; set; }
    public string Game { get; set; }
    public string ProcessName { get; set; }
    public string RunDir { get; set; }
    public string ReportHtml { get; set; }
    public string PresentMonCsv { get; set; }
    public string ProcessCsv { get; set; }
    public string SystemCsv { get; set; }
    public string SummaryPath { get; set; }
    public string ReportKind { get; set; }
    public bool ReportHasFrameData { get; set; }
    public int ReportFrameCount { get; set; }
    public int ReportProcessSampleCount { get; set; }
    public int ReportSystemSampleCount { get; set; }
    public bool ProcessSamplerRequired { get; set; }
    public string ProcessSamplerExe { get; set; }
    public bool ProcessSamplerExecutableAvailable { get; set; }
    public bool ProcessSamplerStarted { get; set; }
    public int? ProcessSamplerPid { get; set; }
    public string ProcessSamplerStartedAt { get; set; }
    public string ProcessSamplerExitedAt { get; set; }
    public int? ProcessSamplerExitCode { get; set; }
    public bool ProcessSamplerExitedEarly { get; set; }
    public bool ProcessSamplerStopRequested { get; set; }
    public bool ProcessSamplerForcedStop { get; set; }
    public string ProcessSamplerCsvPath { get; set; }
    public bool ProcessSamplerCsvExists { get; set; }
    public long ProcessSamplerCsvBytes { get; set; }
    public int ProcessSamplerValidRows { get; set; }
    public string ProcessSamplerStatus { get; set; }
    public string ProcessSamplerErrorTail { get; set; }
    public bool SystemSamplerRequired { get; set; }
    public string SystemSamplerExe { get; set; }
    public bool SystemSamplerExecutableAvailable { get; set; }
    public bool SystemSamplerStarted { get; set; }
    public int? SystemSamplerPid { get; set; }
    public string SystemSamplerStartedAt { get; set; }
    public string SystemSamplerExitedAt { get; set; }
    public int? SystemSamplerExitCode { get; set; }
    public bool SystemSamplerExitedEarly { get; set; }
    public bool SystemSamplerStopRequested { get; set; }
    public bool SystemSamplerForcedStop { get; set; }
    public string SystemSamplerCsvPath { get; set; }
    public bool SystemSamplerCsvExists { get; set; }
    public long SystemSamplerCsvBytes { get; set; }
    public int SystemSamplerValidRows { get; set; }
    public string SystemSamplerStatus { get; set; }
    public string SystemSamplerErrorTail { get; set; }
    public string ReportGenerationStartedAt { get; set; }
    public string ReportGenerationEndedAt { get; set; }
    public bool ReportGenerationTimedOut { get; set; }
    public bool ReportCanRetry { get; set; }
    public int ReportGenerationExitCode { get; set; }
    public int MonitorExitCode { get; set; }
}

internal sealed class ReportGenerationResult
{
    public ReportGenerationResult()
    {
        SamplerEvidenceFields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    public bool Attempted;
    public int ExitCode;
    public string ReportHtml;
    public string LogPath;
    public string ProgressPath;
    public string Error;
    public int FrameCount;
    public int ProcessSampleCount;
    public int SystemSampleCount;
    public bool HasFrameData;
    public string ReportKind;
    public DateTime GenerationStartedAt;
    public DateTime GenerationEndedAt;
    public bool TimedOut;
    public bool CanRetry;
    public Dictionary<string, object> SamplerEvidenceFields;
}
