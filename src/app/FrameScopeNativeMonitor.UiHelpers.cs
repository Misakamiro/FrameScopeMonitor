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
    private static IEnumerable<FrameScopeHistoryEntry> RecentHistoryEntries()
    {
        if (!File.Exists(HistoryPath)) return new List<FrameScopeHistoryEntry>();
        var entries = new List<FrameScopeHistoryEntry>();
        foreach (var line in File.ReadLines(HistoryPath).Reverse().Take(30))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = Json.Deserialize<FrameScopeHistoryEntry>(line);
                if (entry != null) entries.Add(entry);
            }
            catch { }
        }
        return entries;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        if (bytes >= 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    private static string DefaultSampleIntervalText(FrameScopeConfig config)
    {
        if (config != null && config.Targets != null && config.Targets.Count > 0)
        {
            return config.Targets[0].SampleIntervalMs.ToString(CultureInfo.InvariantCulture) + " ms";
        }
        return "100 ms";
    }

    private static string ResolveCurrentDataRoot()
    {
        if (dataRootText != null && !string.IsNullOrWhiteSpace(dataRootText.Text)) return ResolveDataRoot(dataRootText.Text);
        try
        {
            var config = LoadConfig();
            return ResolveDataRoot(config.DataRoot);
        }
        catch
        {
            return ResolveDataRoot(DefaultDataRoot);
        }
    }

    private static int EnabledTargetCount(FrameScopeConfig config)
    {
        if (config == null || config.Targets == null) return 0;
        return config.Targets.Count(t => t != null && t.Enabled);
    }

    private static bool IsWatcherRunningQuiet()
    {
        int pid;
        return IsWatcherRunning(out pid);
    }
}
