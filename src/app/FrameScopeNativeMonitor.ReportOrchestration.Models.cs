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
    public int MonitorExitCode { get; set; }
}

internal sealed class ReportGenerationResult
{
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
}
