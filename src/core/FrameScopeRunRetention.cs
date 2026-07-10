using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed class FrameScopeRunRetentionCandidate
{
    internal string RunDirectory = "";
    internal string TargetKey = "";
    internal string Phase = "";
    internal DateTime LastWriteTimeUtc;
    internal long SizeBytes;
    internal bool ReportComplete;
    internal bool HasUsableMonitorData;
    internal bool ReportGenerationInProgress;
}

internal static class FrameScopeRunRetention
{
    internal static List<FrameScopeRunRetentionCandidate> Select(
        string dataRoot,
        IEnumerable<FrameScopeRunRetentionCandidate> candidates,
        DateTime nowUtc,
        int retentionDays,
        long maxBytes)
    {
        string root = Path.GetFullPath(dataRoot ?? "");
        List<FrameScopeRunRetentionCandidate> valid = (candidates ?? Enumerable.Empty<FrameScopeRunRetentionCandidate>())
            .Where(candidate => candidate != null && IsPathInside(candidate.RunDirectory, root))
            .ToList();

        var newest = new HashSet<FrameScopeRunRetentionCandidate>();
        foreach (IGrouping<string, FrameScopeRunRetentionCandidate> group in valid.GroupBy(TargetIdentity, StringComparer.OrdinalIgnoreCase))
        {
            FrameScopeRunRetentionCandidate item = group.OrderByDescending(candidate => candidate.LastWriteTimeUtc).FirstOrDefault();
            if (item != null) newest.Add(item);
        }

        List<FrameScopeRunRetentionCandidate> eligible = valid
            .Where(candidate => !newest.Contains(candidate) && IsTerminalPhase(candidate.Phase))
            .Where(candidate => !candidate.ReportGenerationInProgress)
            .Where(candidate => candidate.ReportComplete || !candidate.HasUsableMonitorData)
            .OrderBy(candidate => candidate.LastWriteTimeUtc)
            .ToList();

        DateTime cutoff = nowUtc.AddDays(-Math.Max(0, retentionDays));
        List<FrameScopeRunRetentionCandidate> selected = eligible
            .Where(candidate => candidate.LastWriteTimeUtc < cutoff)
            .ToList();
        var selectedSet = new HashSet<FrameScopeRunRetentionCandidate>(selected);

        long remainingBytes = valid.Sum(candidate => Math.Max(0L, candidate.SizeBytes));
        remainingBytes -= selected.Sum(candidate => Math.Max(0L, candidate.SizeBytes));
        long limit = Math.Max(0L, maxBytes);
        foreach (FrameScopeRunRetentionCandidate candidate in eligible)
        {
            if (remainingBytes <= limit) break;
            if (!selectedSet.Add(candidate)) continue;
            selected.Add(candidate);
            remainingBytes -= Math.Max(0L, candidate.SizeBytes);
        }
        return selected;
    }

    internal static bool IsTerminalPhase(string phase)
    {
        string value = (phase ?? "").Trim().ToLowerInvariant();
        return value == "done" || value == "error" || value == "timeout-waiting-for-target";
    }

    internal static bool IsPathInside(string path, string root)
    {
        try
        {
            string fullPath = Path.GetFullPath(path ?? "");
            string fullRoot = Path.GetFullPath(root ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string TargetIdentity(FrameScopeRunRetentionCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.TargetKey)) return candidate.TargetKey;
        try
        {
            DirectoryInfo run = Directory.GetParent(Path.GetFullPath(candidate.RunDirectory));
            return run == null ? candidate.RunDirectory : run.FullName;
        }
        catch { return candidate.RunDirectory ?? ""; }
    }
}
