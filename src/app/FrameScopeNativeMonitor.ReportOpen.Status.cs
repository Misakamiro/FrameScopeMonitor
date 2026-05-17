using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal static partial class FrameScopeNativeMonitor
{
    private static void MarkReportOpened(string runDir, Dictionary<string, object> status)
    {
        try
        {
            var statusPath = Path.Combine(runDir, "status.json");
            var map = status != null
                ? new Dictionary<string, object>(status, StringComparer.OrdinalIgnoreCase)
                : ReadStatusDictionary(runDir);
            if (map == null) map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            map["ReportOpened"] = true;
            map["ReportOpenedAt"] = DateTime.Now.ToString("o");
            File.WriteAllText(statusPath, Json.Serialize(map), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("status-report-opened-update-failed run=" + runDir + " error=" + ex.Message);
        }
    }
}
