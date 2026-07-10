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

internal sealed class MonitorSessionPaths
{
    public string RunDir;
    public string StatusPath;
    public string PresentMonCsv;
    public string PresentMonStdout;
    public string PresentMonStderr;
    public string PresentMonInfoPath;
    public string SamplesCsv;
    public string CpuCoreSamplesCsv;
    public string CpuCoreTelemetryStatusPath;
    public string CpuVoltageSamplesCsv;
    public string CpuVoltageTelemetryStatusPath;
    public string CpuVidSamplesCsv;
    public string CpuVidTelemetryStatusPath;
    public string ProcessCsv;
    public string ProcessSamplerStdout;
    public string ProcessSamplerStderr;
    public string SystemSamplerStdout;
    public string SystemSamplerStderr;
    public string TopCpuCsv;
    public string TopIoCsv;
    public string AlertsCsv;
    public string SamplerStopPath;
    public string EventsCsv;
    public string SummaryPath;
    public string ReportLogPath;
    public string SlowSamplerLogPath;
    public string ErrorPath;
}

internal sealed class TargetProcessSnapshot
{
    public string BaseName;
    public int ProcessId;
    public string WindowTitle;
    public bool HasMainWindow;
    public DateTime? StartTime;
    public int Score;
}
