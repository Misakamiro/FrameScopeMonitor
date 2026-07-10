using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static partial class FrameScopeNativeMonitor
{
    private static void ApplyRunAndHistoryRetention(string dataRoot, FrameScopeConfig config)
    {
        if (config == null || string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(dataRoot)) return;
        try
        {
            var runDirectories = FrameScopeDataRootScanner.FindStatusFiles(dataRoot, new FrameScopeDataRootScanStats())
                .Select(Path.GetDirectoryName)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var candidates = new List<FrameScopeRunRetentionCandidate>();
            foreach (string runDir in runDirectories)
            {
                Dictionary<string, object> status = ReadStatusDictionary(runDir);
                FrameScopeReportArtifactState artifacts = FrameScopeReportArtifacts.Inspect(runDir);
                DirectoryInfo directory = new DirectoryInfo(runDir);
                candidates.Add(new FrameScopeRunRetentionCandidate
                {
                    RunDirectory = directory.FullName,
                    TargetKey = directory.Parent == null ? "" : directory.Parent.FullName,
                    Phase = StatusString(status, "Phase", ""),
                    LastWriteTimeUtc = directory.LastWriteTimeUtc,
                    SizeBytes = DirectorySize(directory.FullName),
                    ReportComplete = artifacts.IsComplete,
                    HasUsableMonitorData = FrameScopeReportArtifacts.HasUsableMonitorData(directory.FullName),
                    ReportGenerationInProgress = IsReportGenerationInProgress(status)
                });
            }

            long maxBytes = Math.Max(1L, config.MaxLogDiskMb) * 1024L * 1024L;
            List<FrameScopeRunRetentionCandidate> selected = FrameScopeRunRetention.Select(
                dataRoot, candidates, DateTime.UtcNow, config.LogRetentionDays, maxBytes);
            foreach (FrameScopeRunRetentionCandidate candidate in selected)
            {
                if (!FrameScopeRunRetention.IsPathInside(candidate.RunDirectory, dataRoot)) continue;
                try
                {
                    Directory.Delete(candidate.RunDirectory, true);
                    WriteFrameScopeLog("run-retention-deleted run=" + candidate.RunDirectory);
                }
                catch (Exception ex)
                {
                    WriteFrameScopeLog("run-retention-delete-failed run=" + candidate.RunDirectory + " error=" + ex.Message);
                }
            }

            FrameScopeHistoryFile.Compact(
                HistoryPath,
                ResolveHistoryRunDirectory,
                delegate(string run)
                {
                    return FrameScopeRunRetention.IsPathInside(run, dataRoot) && Directory.Exists(run);
                },
                500);
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("run-retention-failed error=" + ex.Message);
        }
    }

    private static bool IsReportGenerationInProgress(Dictionary<string, object> status)
    {
        if (!StatusBool(status, "ReportGenerationAttempted", false)) return false;
        if (StatusBool(status, "ReportGenerationTimedOut", false)) return false;
        int percent = StatusInt(status, "ReportProgressPercent", -1);
        return percent >= 0 && percent < 100;
    }

    private static long DirectorySize(string directory)
    {
        long total = 0;
        try
        {
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                try { total += Math.Max(0L, new FileInfo(file).Length); }
                catch { }
            }
        }
        catch { }
        return total;
    }

    private static string ResolveHistoryRunDirectory(string line)
    {
        Dictionary<string, object> map = Json.Deserialize<Dictionary<string, object>>(line);
        object value;
        return map != null && map.TryGetValue("RunDir", out value) && value != null ? Convert.ToString(value) : "";
    }
}
