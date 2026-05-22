using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public static partial class FrameScopeDiagnostics
{
    private const string ProductVersion = "1.1.3";
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private static readonly object LogLock = new object();

    public static string DefaultDiagnosticRoot
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FrameScopeMonitorData",
                "diagnostic-reports");
        }
    }

    public static void AppendLogAsync(string logPath, string message)
    {
        if (string.IsNullOrWhiteSpace(logPath)) return;
        string line = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) + " " + (message ?? "") + Environment.NewLine;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(logPath));
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                lock (LogLock)
                {
                    File.AppendAllText(logPath, line, Encoding.UTF8);
                }
            }
            catch { }
        });
    }

    public static void QueueGenerateReport(FrameScopeConfig config, string appRoot, string dataRoot, string reason, string runDir)
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                GenerateReport(config, appRoot, dataRoot, reason, runDir);
            }
            catch { }
        });
    }

    public static FrameScopeDiagnosticReportResult GenerateReport(FrameScopeConfig config, string appRoot, string dataRoot, string reason, string runDir)
    {
        if (config == null) config = FrameScopeConfigStore.CreateDefaultConfig();
        FrameScopeConfigStore.Normalize(config);
        if (string.IsNullOrWhiteSpace(appRoot)) appRoot = AppDomain.CurrentDomain.BaseDirectory;
        if (string.IsNullOrWhiteSpace(dataRoot)) dataRoot = FrameScopeConfigStore.DefaultDataRoot;
        dataRoot = ResolveRoot(appRoot, dataRoot);

        string diagnosticsRoot = DefaultDiagnosticRoot;
        Directory.CreateDirectory(diagnosticsRoot);
        CleanupDiagnosticReports(diagnosticsRoot, config.LogRetentionDays, config.MaxLogDiskMb);
        TrimLogFile(Path.Combine(appRoot, "framescope-watcher.log"), config.MaxLogDiskMb);

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string outputDir = Path.Combine(diagnosticsRoot, "diagnostic-" + stamp);
        Directory.CreateDirectory(outputDir);

        string recentRun = string.IsNullOrWhiteSpace(runDir) ? FindLatestRun(dataRoot) : runDir;
        Dictionary<string, object> report = BuildReport(config, appRoot, dataRoot, reason, recentRun);
        string jsonPath = Path.Combine(outputDir, "framescope-diagnostic-report.json");
        string markdownPath = Path.Combine(outputDir, "framescope-diagnostic-report.md");
        File.WriteAllText(jsonPath, Json.Serialize(report), Encoding.UTF8);
        File.WriteAllText(markdownPath, BuildMarkdown(report), Encoding.UTF8);

        FrameScopeDiagnosticCleanupResult cleanup = CleanupDiagnosticReports(diagnosticsRoot, config.LogRetentionDays, config.MaxLogDiskMb);
        return new FrameScopeDiagnosticReportResult
        {
            DirectoryPath = outputDir,
            JsonPath = jsonPath,
            MarkdownPath = markdownPath,
            Cleanup = cleanup
        };
    }

    public static Dictionary<string, object> BuildReport(FrameScopeConfig config, string appRoot, string dataRoot, string reason, string runDir)
    {
        Dictionary<string, object> status = LoadJsonMap(Path.Combine(runDir ?? "", "status.json"));
        Dictionary<string, object> summary = LoadJsonMap(Path.Combine(runDir ?? "", "summary.json"));
        Dictionary<string, object> progress = LoadJsonMap(Path.Combine(runDir ?? "", "report-progress.json"));
        Dictionary<string, object> manifest = LoadJsonMap(Path.Combine(runDir ?? "", "charts", "framescope-interactive-manifest.json"));

        Dictionary<string, object> report = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        report["diagnosticReport"] = new Dictionary<string, object>
        {
            { "createdAt", DateTime.Now.ToString("o", CultureInfo.InvariantCulture) },
            { "reason", RedactForPrivacy(reason ?? "") },
            { "appRoot", RedactForPrivacy(appRoot ?? "") },
            { "dataRoot", RedactForPrivacy(dataRoot ?? "") },
            { "recentRunDir", RedactForPrivacy(runDir ?? "") }
        };
        report["software"] = BuildSoftwareSection();
        report["system"] = BuildSystemSection();
        report["settings"] = BuildSettingsSection(config);
        report["targetDetection"] = BuildTargetDetectionSection(config);
        report["recentSession"] = BuildRecentSessionSection(runDir, status, summary, manifest);
        report["fpsSummary"] = BuildFpsSummary(status, manifest);
        report["reportGeneration"] = BuildReportGenerationSection(status, progress);
        report["performance"] = BuildPerformanceSection(runDir);
        report["errors"] = BuildErrorsSection(appRoot, runDir);
        report["captureChain"] = BuildCaptureChainSection(status, summary, manifest);
        return RedactMap(report);
    }

    public static void ApplyRetentionPolicy(string appRoot, FrameScopeConfig config)
    {
        if (config == null) return;
        FrameScopeConfigStore.Normalize(config);
        CleanupDiagnosticReports(DefaultDiagnosticRoot, config.LogRetentionDays, config.MaxLogDiskMb);
        if (!string.IsNullOrWhiteSpace(appRoot))
        {
            TrimLogFile(Path.Combine(appRoot, "framescope-watcher.log"), config.MaxLogDiskMb);
        }
    }
}
