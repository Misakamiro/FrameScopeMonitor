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
    private static List<string> BuildTargetProcessBaseNames(string processNames, string displayName)
    {
        return FrameScopeCapturePlanner.BuildTargetProcessBaseNames(processNames, displayName);
    }

    private static bool ShouldUseProcessNameCapture(List<string> processBaseNames, string configuredProcessName, string displayName)
    {
        return FrameScopeCapturePlanner.ShouldUseProcessNameCapture(processBaseNames, configuredProcessName, displayName);
    }

    private static TargetProcessSnapshot FindBestTargetProcess(List<string> processBaseNames, int preferredPid)
    {
        TargetProcessSnapshot best = null;
        if (processBaseNames == null || processBaseNames.Count == 0) return null;

        foreach (var processBaseName in processBaseNames)
        {
            if (string.IsNullOrWhiteSpace(processBaseName)) continue;
            Process[] processes = null;
            try
            {
                processes = Process.GetProcessesByName(processBaseName);
                foreach (var process in processes)
                {
                    TargetProcessSnapshot snapshot = SnapshotProcess(process, processBaseName, preferredPid);
                    if (snapshot == null) continue;
                    if (best == null || snapshot.Score > best.Score) best = snapshot;
                }
            }
            catch { }
            finally
            {
                if (processes != null)
                {
                    foreach (var process in processes) DisposeProcess(process);
                }
            }
        }

        return best;
    }

    private static TargetProcessSnapshot SnapshotProcess(Process process, string processBaseName, int preferredPid)
    {
        if (process == null) return null;
        try
        {
            var title = "";
            var hasMainWindow = false;
            DateTime? startTime = null;
            try { title = process.MainWindowTitle ?? ""; } catch { }
            try { hasMainWindow = process.MainWindowHandle != IntPtr.Zero; } catch { }
            try { startTime = process.StartTime; } catch { }

            var score = 0;
            if (preferredPid > 0 && process.Id == preferredPid) score += 100000;
            if (hasMainWindow) score += 2000;
            if (!string.IsNullOrWhiteSpace(title)) score += 500;
            if (startTime.HasValue)
            {
                var ageSeconds = Math.Max(0, (DateTime.Now - startTime.Value).TotalSeconds);
                score += Math.Min(300, (int)Math.Round(ageSeconds));
            }

            return new TargetProcessSnapshot
            {
                BaseName = processBaseName,
                ProcessId = process.Id,
                WindowTitle = title,
                HasMainWindow = hasMainWindow,
                StartTime = startTime,
                Score = score
            };
        }
        catch
        {
            return null;
        }
    }

    private static Process WaitForTargetProcess(List<string> processBaseNames, int waitSeconds, int preferredPid, out TargetProcessSnapshot selectedSnapshot)
    {
        selectedSnapshot = null;
        var deadline = DateTime.Now.AddSeconds(waitSeconds);
        TargetProcessSnapshot fallbackSnapshot = null;
        DateTime firstFallbackSeen = DateTime.MinValue;
        while (DateTime.Now < deadline)
        {
            if (preferredPid > 0)
            {
                try
                {
                    var preferred = Process.GetProcessById(preferredPid);
                    selectedSnapshot = SnapshotProcess(preferred, preferred.ProcessName, preferredPid);
                    return preferred;
                }
                catch { }
            }

            var snapshot = FindBestTargetProcess(processBaseNames, preferredPid);
            if (snapshot != null)
            {
                if (preferredPid > 0 && snapshot.ProcessId == preferredPid)
                {
                    try
                    {
                        var process = Process.GetProcessById(snapshot.ProcessId);
                        if (processBaseNames.Any(name => name.Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase)))
                        {
                            selectedSnapshot = SnapshotProcess(process, process.ProcessName, preferredPid) ?? snapshot;
                            return process;
                        }
                        DisposeProcess(process);
                    }
                    catch { }
                }

                if (!snapshot.HasMainWindow && fallbackSnapshot == null)
                {
                    fallbackSnapshot = snapshot;
                    firstFallbackSeen = DateTime.Now;
                }

                if (snapshot.HasMainWindow || (fallbackSnapshot != null && (DateTime.Now - firstFallbackSeen).TotalSeconds >= 4.0))
                {
                    try
                    {
                        var selected = snapshot.HasMainWindow || fallbackSnapshot == null ? snapshot : fallbackSnapshot;
                        var process = Process.GetProcessById(selected.ProcessId);
                        if (processBaseNames.Any(name => name.Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase)))
                        {
                            selectedSnapshot = SnapshotProcess(process, process.ProcessName, preferredPid) ?? selected;
                            return process;
                        }
                        DisposeProcess(process);
                    }
                    catch { }
                }
            }
            Thread.Sleep(200);
        }
        return null;
    }
}
