using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed class FrameScopePresentMonSessionStopResult
{
    internal FrameScopePresentMonSessionStopResult(
        string sessionName,
        bool primarySucceeded,
        bool fallbackAttempted,
        bool fallbackSucceeded)
    {
        SessionName = sessionName;
        PrimarySucceeded = primarySucceeded;
        FallbackAttempted = fallbackAttempted;
        FallbackSucceeded = fallbackSucceeded;
    }

    public string SessionName { get; private set; }
    public bool PrimarySucceeded { get; private set; }
    public bool FallbackAttempted { get; private set; }
    public bool FallbackSucceeded { get; private set; }
    public bool Succeeded { get { return PrimarySucceeded || FallbackSucceeded; } }
}

public static class FrameScopePresentMonSessionPolicy
{
    public const string SessionPrefix = "FrameScopeNativePresentMon_";

    private const int MaximumLabelLength = 48;
    private const int MaximumNonceLength = 32;
    private const string OwnerMarker = "_p";

    public static string CreateSessionName(string label, int ownerPid, string nonce)
    {
        if (ownerPid <= 0) throw new ArgumentOutOfRangeException("ownerPid");
        if (!IsValidNonce(nonce)) throw new ArgumentException("Nonce must contain 1-32 ASCII letters or digits.", "nonce");

        string safeLabel = SanitizeLabel(label);
        return SessionPrefix
            + safeLabel
            + OwnerMarker
            + ownerPid.ToString(CultureInfo.InvariantCulture)
            + "_"
            + nonce;
    }

    public static bool TryGetOwnerPid(string sessionName, out int ownerPid)
    {
        ownerPid = 0;
        if (string.IsNullOrEmpty(sessionName)) return false;
        if (!sessionName.StartsWith(SessionPrefix, StringComparison.Ordinal)) return false;

        int markerIndex = sessionName.LastIndexOf(OwnerMarker, StringComparison.Ordinal);
        if (markerIndex <= SessionPrefix.Length) return false;

        int nonceSeparatorIndex = sessionName.IndexOf('_', markerIndex + OwnerMarker.Length);
        if (nonceSeparatorIndex < 0 || nonceSeparatorIndex == sessionName.Length - 1) return false;

        string label = sessionName.Substring(SessionPrefix.Length, markerIndex - SessionPrefix.Length);
        if (!IsValidLabel(label)) return false;

        string pidText = sessionName.Substring(
            markerIndex + OwnerMarker.Length,
            nonceSeparatorIndex - markerIndex - OwnerMarker.Length);
        int parsedPid;
        if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out parsedPid) || parsedPid <= 0) return false;
        if (!string.Equals(pidText, parsedPid.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)) return false;

        string nonce = sessionName.Substring(nonceSeparatorIndex + 1);
        if (!IsValidNonce(nonce)) return false;

        ownerPid = parsedPid;
        return true;
    }

    public static IEnumerable<string> SelectStaleOwnedSessions(
        IEnumerable<string> sessions,
        Func<int, bool> isOwnerAlive)
    {
        if (isOwnerAlive == null) throw new ArgumentNullException("isOwnerAlive");

        var staleSessions = new List<string>();
        if (sessions == null) return staleSessions;

        foreach (string session in sessions)
        {
            int ownerPid;
            if (!TryGetOwnerPid(session, out ownerPid)) continue;

            bool ownerAlive;
            try
            {
                ownerAlive = isOwnerAlive(ownerPid);
            }
            catch
            {
                continue;
            }

            if (!ownerAlive) staleSessions.Add(session);
        }

        return staleSessions;
    }

    public static IList<FrameScopePresentMonSessionStopResult> StopStaleOwnedSessions(
        IEnumerable<string> sessions,
        Func<int, bool> isOwnerAlive,
        Func<string, bool> primaryStop,
        Func<string, bool> fallbackStop)
    {
        if (primaryStop == null) throw new ArgumentNullException("primaryStop");
        if (fallbackStop == null) throw new ArgumentNullException("fallbackStop");

        var results = new List<FrameScopePresentMonSessionStopResult>();
        foreach (string session in SelectStaleOwnedSessions(sessions, isOwnerAlive))
        {
            results.Add(StopOwnedSession(session, primaryStop, fallbackStop));
        }
        return results;
    }

    public static FrameScopePresentMonSessionStopResult StopOwnedSession(
        string sessionName,
        Func<string, bool> primaryStop,
        Func<string, bool> fallbackStop)
    {
        int ownerPid;
        if (!TryGetOwnerPid(sessionName, out ownerPid)) throw new ArgumentException("Session name is not in the owned FrameScope format.", "sessionName");
        if (primaryStop == null) throw new ArgumentNullException("primaryStop");
        if (fallbackStop == null) throw new ArgumentNullException("fallbackStop");

        bool primarySucceeded = primaryStop(sessionName);
        bool fallbackAttempted = !primarySucceeded;
        bool fallbackSucceeded = fallbackAttempted && fallbackStop(sessionName);
        return new FrameScopePresentMonSessionStopResult(
            sessionName,
            primarySucceeded,
            fallbackAttempted,
            fallbackSucceeded);
    }

    private static string SanitizeLabel(string label)
    {
        string value = string.IsNullOrWhiteSpace(label) ? "session" : label;
        var builder = new StringBuilder(Math.Min(value.Length, MaximumLabelLength));
        foreach (char ch in value)
        {
            if (builder.Length >= MaximumLabelLength) break;
            builder.Append(IsSafeLabelCharacter(ch) ? ch : '-');
        }

        string result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "session" : result;
    }

    private static bool IsValidLabel(string label)
    {
        if (string.IsNullOrEmpty(label) || label.Length > MaximumLabelLength) return false;
        foreach (char ch in label)
        {
            if (!IsSafeLabelCharacter(ch)) return false;
        }
        return string.Equals(label, SanitizeLabel(label), StringComparison.Ordinal);
    }

    private static bool IsSafeLabelCharacter(char ch)
    {
        return
            (ch >= 'A' && ch <= 'Z') ||
            (ch >= 'a' && ch <= 'z') ||
            (ch >= '0' && ch <= '9') ||
            ch == '.' || ch == '_' || ch == '-';
    }

    private static bool IsValidNonce(string nonce)
    {
        if (string.IsNullOrEmpty(nonce) || nonce.Length > MaximumNonceLength) return false;
        foreach (char ch in nonce)
        {
            bool isAsciiLetterOrDigit =
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= 'a' && ch <= 'z') ||
                (ch >= '0' && ch <= '9');
            if (!isAsciiLetterOrDigit) return false;
        }
        return true;
    }
}
