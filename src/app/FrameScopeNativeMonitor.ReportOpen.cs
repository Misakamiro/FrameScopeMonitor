using System;
using System.Diagnostics;
using System.IO;
using System.Text;

internal static partial class FrameScopeNativeMonitor
{
    private static bool TryOpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var fullPath = Path.GetFullPath(path);
        if (Path.GetExtension(fullPath).Equals(".html", StringComparison.OrdinalIgnoreCase) && TryOpenHtmlWithBrowsers(fullPath))
        {
            return true;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true, Verb = "open" });
            return true;
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("open-report-shell-failed path=" + fullPath + " error=" + ex.Message);
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = QuoteCommandArgument(fullPath), UseShellExecute = false, CreateNoWindow = true });
            return true;
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("open-report-explorer-failed path=" + fullPath + " error=" + ex.Message);
            return false;
        }
    }

    private static bool TryOpenReport(string reportHtml, string runDir)
    {
        var markerPath = Path.Combine(runDir, "report-opened.flag");
        if (File.Exists(markerPath))
        {
            WriteFrameScopeLog("report-open-skip already-opened report=" + reportHtml);
            return true;
        }

        if (TryOpenPath(reportHtml))
        {
            try { File.WriteAllText(markerPath, DateTime.Now.ToString("o"), Encoding.UTF8); }
            catch { }
            WriteFrameScopeLog("report-opened report=" + reportHtml);
            return true;
        }

        return false;
    }
}
