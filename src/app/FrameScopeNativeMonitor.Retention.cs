using System;
using System.Collections.Generic;
using System.IO;

internal static partial class FrameScopeNativeMonitor
{
    private static void ApplyRunAndHistoryRetention(string dataRoot, FrameScopeConfig config)
    {
        if (config == null || string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(dataRoot)) return;
        try
        {
            // Missing directories and old reports remain until explicit deletion.
            FrameScopeRunRetention.MaintainHistory(HistoryPath, ResolveHistoryRunDirectory);
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("run-retention-failed error=" + ex.Message);
        }
    }

    private static string ResolveHistoryRunDirectory(string line)
    {
        Dictionary<string, object> map = Json.Deserialize<Dictionary<string, object>>(line);
        object value;
        return map != null && map.TryGetValue("RunDir", out value) && value != null ? Convert.ToString(value) : "";
    }
}
