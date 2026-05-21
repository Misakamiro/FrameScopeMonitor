using System;
using System.Linq;

public sealed class FrameScopeAddProcessPlan
{
    public bool ShouldStopWatcherFirst { get; set; }
    public bool ShouldAutoRestartWatcher { get; set; }
}

public static class FrameScopeTargetEditRules
{
    public static bool TryParseSampleInterval(string value, out int sampleMs, out string error)
    {
        sampleMs = 0;
        error = "";
        if (!int.TryParse((value ?? "").Trim(), out sampleMs))
        {
            error = "采样率必须是数字，单位 ms。";
            return false;
        }
        if (sampleMs < 50)
        {
            error = "采样率不能低于 50 ms。";
            return false;
        }
        return true;
    }

    public static string NormalizeProcessName(string value)
    {
        string[] parts = (value ?? "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        return string.Join(";", parts);
    }

    public static string NormalizeSingleProcessForAdd(string value)
    {
        string normalized = NormalizeProcessName(value);
        if (string.IsNullOrWhiteSpace(normalized)) return "";
        if (normalized.IndexOf(';') < 0 && !normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".exe";
        }
        return normalized;
    }

    public static FrameScopeAddProcessPlan PlanAddProcess(bool watcherRunning)
    {
        return new FrameScopeAddProcessPlan
        {
            ShouldStopWatcherFirst = watcherRunning,
            ShouldAutoRestartWatcher = false
        };
    }
}
