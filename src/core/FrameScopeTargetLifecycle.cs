using System;
using System.Collections.Generic;

public static class FrameScopeTargetLifecycle
{
    public const int DefaultAliasQuiescenceMilliseconds = 2000;
    public const int AliasProbeIntervalMilliseconds = 250;

    public static bool ShouldStopCapture(bool untilTargetExit, bool selectedPidExited, bool anyAliasRunning, bool deadlineReached)
    {
        if (!untilTargetExit) return deadlineReached;
        return selectedPidExited && !anyAliasRunning;
    }

    public static string CanonicalTargetKey(IEnumerable<string> aliases)
    {
        return JoinAliases(NormalizeRawAliases(aliases));
    }

    public static string CanonicalBaseNameTargetKey(IEnumerable<string> baseNames)
    {
        return JoinAliases(NormalizeBaseNames(baseNames));
    }

    public static List<string> NormalizeRawAliases(IEnumerable<string> aliases)
    {
        return NormalizeAliases(aliases, true);
    }

    public static List<string> NormalizeBaseNames(IEnumerable<string> baseNames)
    {
        return NormalizeAliases(baseNames, false);
    }

    public static List<string> ParseBaseNameAliases(string aliasText, string fallbackBaseName)
    {
        IEnumerable<string> values = string.IsNullOrWhiteSpace(aliasText)
            ? new[] { fallbackBaseName }
            : aliasText.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
        return NormalizeBaseNames(values);
    }

    public static bool MatchesAnyAlias(string processBaseName, IEnumerable<string> normalizedBaseNames)
    {
        string candidate = (processBaseName ?? "").Trim();
        if (candidate.Length == 0 || normalizedBaseNames == null) return false;
        foreach (string alias in normalizedBaseNames)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(candidate, (alias ?? "").Trim())) return true;
        }
        return false;
    }

    public static bool CanClaimAliases(IEnumerable<string> candidateBaseNames, IEnumerable<IEnumerable<string>> activeBaseNameSets)
    {
        List<string> candidate = NormalizeBaseNames(candidateBaseNames);
        if (candidate.Count == 0) return false;

        var candidateSet = new HashSet<string>(candidate, StringComparer.OrdinalIgnoreCase);
        if (activeBaseNameSets == null) return true;
        foreach (IEnumerable<string> activeBaseNames in activeBaseNameSets)
        {
            foreach (string active in NormalizeBaseNames(activeBaseNames))
            {
                if (candidateSet.Contains(active)) return false;
            }
        }
        return true;
    }

    public static bool ShouldStopSampler(bool parentOwned, bool parentRunning, bool anyAliasRunning)
    {
        return parentOwned ? !parentRunning : !anyAliasRunning;
    }

    public static long UpdateQuiescenceStartMilliseconds(long currentStartMilliseconds, bool anyAliasRunning, long elapsedMilliseconds)
    {
        if (anyAliasRunning) return -1;
        return currentStartMilliseconds >= 0 ? currentStartMilliseconds : elapsedMilliseconds;
    }

    public static bool IsQuiescenceConfirmed(long startMilliseconds, long elapsedMilliseconds, int requiredMilliseconds)
    {
        if (startMilliseconds < 0 || elapsedMilliseconds < startMilliseconds) return false;
        return elapsedMilliseconds - startMilliseconds >= Math.Max(0, requiredMilliseconds);
    }

    private static List<string> NormalizeAliases(IEnumerable<string> aliases, bool stripExeSuffix)
    {
        var normalized = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (aliases == null) return new List<string>();

        foreach (string alias in aliases)
        {
            string value = (alias ?? "").Trim();
            if (stripExeSuffix && value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 4).Trim();
            }
            if (value.Length == 0) continue;
            normalized.Add(value.ToLowerInvariant());
        }

        return new List<string>(normalized);
    }

    private static string JoinAliases(List<string> normalized)
    {
        return normalized == null || normalized.Count == 0
            ? ""
            : string.Join(";", normalized.ToArray());
    }
}
