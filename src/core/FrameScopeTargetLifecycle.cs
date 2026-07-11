using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public static class FrameScopeTargetLifecycle
{
    public const int DefaultAliasQuiescenceMilliseconds = 2000;
    public const int AliasProbeIntervalMilliseconds = 250;
    private const int LegacyMaximumPathCharacters = 259;
    private const int ReportPublicationPathHeadroom = 89;
    private const int MaximumRunDirectoryPathCharacters = LegacyMaximumPathCharacters - ReportPublicationPathHeadroom;
    private const int MaximumRunNonceCharacters = 8;
    private const int MinimumRunNonceCharacters = 4;

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

    public static string ReserveUniqueRunDirectory(string runRoot, string prefix, DateTime timestamp, int ownerPid, string nonce)
    {
        if (string.IsNullOrWhiteSpace(runRoot)) throw new ArgumentException("Run root is empty.", "runRoot");
        if (ownerPid <= 0) throw new ArgumentOutOfRangeException("ownerPid");
        string fullRunRoot = Path.GetFullPath(runRoot);
        Directory.CreateDirectory(fullRunRoot);

        string safePrefix = SafeRunComponent(prefix, "run");
        string safeNonce = SafeRunComponent(nonce, Guid.NewGuid().ToString("N"));
        string ownerIdentity = timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
            "-p" + ownerPid.ToString(CultureInfo.InvariantCulture) + "-";
        int maximumNameLength = MaximumRunDirectoryPathCharacters - fullRunRoot.Length - 1;
        for (int suffix = 0; suffix < 10000; suffix++)
        {
            string collisionSuffix = suffix == 0 ? "" : "-" + suffix.ToString(CultureInfo.InvariantCulture);
            int nonceLength = Math.Min(
                MaximumRunNonceCharacters,
                maximumNameLength - ownerIdentity.Length - collisionSuffix.Length);
            if (nonceLength < MinimumRunNonceCharacters)
            {
                throw new PathTooLongException(
                    "The FrameScope run root is too long to reserve a legacy-compatible run directory: " + fullRunRoot);
            }

            string nonceToken = safeNonce.Substring(0, Math.Min(nonceLength, safeNonce.Length));
            if (nonceToken.Length < MinimumRunNonceCharacters)
            {
                nonceToken = Guid.NewGuid().ToString("N").Substring(0, nonceLength);
            }
            string identity = ownerIdentity + nonceToken;
            int prefixLength = Math.Min(
                safePrefix.Length,
                Math.Max(0, maximumNameLength - identity.Length - collisionSuffix.Length - 1));
            string name = (prefixLength > 0 ? safePrefix.Substring(0, prefixLength) + "-" : "") +
                identity + collisionSuffix;
            string candidate = Path.Combine(fullRunRoot, name);
            Directory.CreateDirectory(candidate);
            string marker = Path.Combine(candidate, ".framescope-run-owner");
            try
            {
                using (FileStream stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(ownerPid.ToString(CultureInfo.InvariantCulture));
                    writer.Write('|');
                    writer.Write(safeNonce);
                }
                return candidate;
            }
            catch (IOException)
            {
                if (!File.Exists(marker)) throw;
            }
        }
        throw new IOException("Unable to reserve a unique FrameScope run directory.");
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

    private static string SafeRunComponent(string value, string fallback)
    {
        string input = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        StringBuilder result = new StringBuilder(Math.Min(80, input.Length));
        foreach (char ch in input)
        {
            if (result.Length >= 80) break;
            bool safe = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') ||
                (ch >= '0' && ch <= '9') || ch == '.' || ch == '_' || ch == '-';
            result.Append(safe ? ch : '-');
        }
        string text = result.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static string JoinAliases(List<string> normalized)
    {
        return normalized == null || normalized.Count == 0
            ? ""
            : string.Join(";", normalized.ToArray());
    }
}
