using System;
using System.Collections.Generic;

public static class FrameScopeTargetLifecycle
{
    public static bool ShouldStopCapture(bool untilTargetExit, bool selectedPidExited, bool anyAliasRunning, bool deadlineReached)
    {
        if (!untilTargetExit) return deadlineReached;
        return selectedPidExited && !anyAliasRunning;
    }

    public static string CanonicalTargetKey(IEnumerable<string> aliases)
    {
        if (aliases == null) return "";

        var normalized = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string alias in aliases)
        {
            string value = (alias ?? "").Trim();
            if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 4).Trim();
            }
            if (value.Length == 0) continue;
            normalized.Add(value.ToLowerInvariant());
        }

        return string.Join(";", new List<string>(normalized).ToArray());
    }
}
