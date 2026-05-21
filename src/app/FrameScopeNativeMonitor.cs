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
    private static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;

    private static readonly string ConfigPath = Path.Combine(Root, "framescope-config.json");

    private const string NativeMonitorMode = FrameScopeConfigStore.NativeMonitorMode;

    private static readonly string ReportGeneratorExe = Path.Combine(Root, "FrameScopeReportGenerator.exe");

    private static readonly string StatePath = Path.Combine(Root, "framescope-watcher-state.json");

    private static readonly string HistoryPath = Path.Combine(Root, "framescope-history.jsonl");

    private const string PresentMonSessionPrefix = "FrameScopeNativePresentMon_";

    private static readonly string DefaultDataRoot = FrameScopeConfigStore.DefaultDataRoot;

    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    [STAThread]
    private static void Main(string[] args)
    {
        if (args != null && args.Any(a => a.Equals("--monitor-session", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RunNativeMonitorSession(args));
            return;
        }

        if (args != null && args.Any(a => a.Equals("--generate-diagnostic-report", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(GenerateDiagnosticReportFromCommandLine(args));
            return;
        }

        if (args != null && args.Any(a => a.Equals("--watcher", StringComparison.OrdinalIgnoreCase)))
        {
            RunNativeWatcher(args);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args != null && args.Any(a => a.Equals("--web-ui", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RunWebUi(args));
            return;
        }

        var sidebarScreenshotPath = GetArgValue(args, "--sidebar-screenshot", "");
        if (!string.IsNullOrWhiteSpace(sidebarScreenshotPath))
        {
            CaptureSidebarScreenshot(sidebarScreenshotPath, GetArgValue(args, "--sidebar-active", "live"));
            return;
        }

        var config = LoadConfig();
        BuildUi(config);
        var uiScreenshotPath = GetArgValue(args, "--ui-screenshot", "");
        var uiScreenshotPage = GetArgValue(args, "--ui-page", "overview");
        if (!string.IsNullOrWhiteSpace(uiScreenshotPath))
        {
            CaptureUiScreenshot(uiScreenshotPath, uiScreenshotPage);
            return;
        }
        RefreshProcessList();
        StartWatcher();
        Application.Run(form);
    }

    private static FrameScopeConfig LoadConfig()
    {
        return FrameScopeConfigStore.Load(ConfigPath);
    }

    private static void SaveConfig(FrameScopeConfig config)
    {
        FrameScopeConfigStore.Save(ConfigPath, config);
    }

    private static int GenerateDiagnosticReportFromCommandLine(string[] args)
    {
        try
        {
            var configPath = GetArgValue(args, "--config", ConfigPath);
            var config = LoadConfigFromPath(configPath);
            var dataRoot = ResolveDataRoot(config.DataRoot);
            var runDir = GetArgValue(args, "--run-dir", "");
            var reason = GetArgValue(args, "--reason", "manual-cli");
            var result = FrameScopeDiagnostics.GenerateReport(config, Root, dataRoot, reason, runDir);
            WriteFrameScopeLog("diagnostic-report-generated path=" + result.MarkdownPath);
            return File.Exists(result.MarkdownPath) && File.Exists(result.JsonPath) ? 0 : 2;
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("diagnostic-report-failed " + ex.Message);
            return 1;
        }
    }

    private static string GetArgValue(string[] args, string name, string fallback)
    {
        if (args == null) return fallback;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return fallback;
    }

    private static bool HasArg(string[] args, string name)
    {
        return args != null && args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static FrameScopeConfig LoadConfigFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = ConfigPath;
        return FrameScopeConfigStore.Load(path);
    }

    private static string ResolveDataRoot(string configuredRoot)
    {
        var dataRoot = configuredRoot;
        if (string.IsNullOrWhiteSpace(dataRoot)) dataRoot = DefaultDataRoot;
        if (!Path.IsPathRooted(dataRoot)) dataRoot = Path.Combine(Root, dataRoot);
        return dataRoot;
    }

    private static string GetTargetBaseName(string processName)
    {
        var baseName = Path.GetFileNameWithoutExtension(processName ?? "");
        return string.IsNullOrWhiteSpace(baseName) ? processName : baseName;
    }

    private static string SafeName(string value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('-');
            }
        }
        var safe = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static void DisposeProcess(Process process)
    {
        if (process == null) return;
        try { process.Dispose(); }
        catch { }
    }

    private static string StatusIconForTitle(string title)
    {
        if ((title ?? "").IndexOf("监测", StringComparison.OrdinalIgnoreCase) >= 0) return "\uE7F4";
        if ((title ?? "").IndexOf("目标", StringComparison.OrdinalIgnoreCase) >= 0) return "\uE919";
        if ((title ?? "").IndexOf("软件", StringComparison.OrdinalIgnoreCase) >= 0) return "\uE83D";
        return "\uE946";
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
