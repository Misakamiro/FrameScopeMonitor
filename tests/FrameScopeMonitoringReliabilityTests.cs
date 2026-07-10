using System;
using System.Collections.Generic;

public static class FrameScopeMonitoringReliabilityTests
{
    private const string SessionPrefix = "FrameScopeNativePresentMon_";

    public static int Main()
    {
        SessionNamesEncodeOwnerAndNonce();
        SessionNamesSanitizeAndBoundLabels();
        OwnerPidParsingAcceptsOnlyOwnedFormat();
        StaleSelectionStopsOnlyProvenDeadOwners();
        Console.WriteLine("FrameScopeMonitoringReliabilityTests: PASS");
        return 0;
    }

    private static void SessionNamesEncodeOwnerAndNonce()
    {
        string first = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4100, "a1");
        string differentOwner = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4200, "a1");
        string differentNonce = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4100, "b2");

        AssertTrue(first.StartsWith(SessionPrefix, StringComparison.Ordinal), "session prefix");
        AssertTrue(first.Contains("_p4100_"), "owner PID marker");
        AssertNotEqual(first, differentOwner, "different owner PIDs must create different names");
        AssertNotEqual(first, differentNonce, "different nonces must create different names");
    }

    private static void SessionNamesSanitizeAndBoundLabels()
    {
        string unsafeLabel = new string('A', 160) + " PUBG / ranked:\tmatch?";
        string name = FrameScopePresentMonSessionPolicy.CreateSessionName(unsafeLabel, 5150, "nonce1");
        const string suffix = "_p5150_nonce1";

        AssertTrue(name.EndsWith(suffix, StringComparison.Ordinal), "owned suffix");
        string safeLabel = name.Substring(SessionPrefix.Length, name.Length - SessionPrefix.Length - suffix.Length);
        AssertTrue(safeLabel.Length > 0, "sanitized label should not be empty");
        AssertTrue(safeLabel.Length <= 48, "sanitized label should be bounded");
        foreach (char ch in safeLabel)
        {
            bool allowed =
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= 'a' && ch <= 'z') ||
                (ch >= '0' && ch <= '9') ||
                ch == '.' || ch == '_' || ch == '-';
            AssertTrue(allowed, "sanitized label contains unsafe character: " + ch);
        }

        string fallback = FrameScopePresentMonSessionPolicy.CreateSessionName(" /?\t", 5150, "nonce2");
        AssertTrue(fallback.StartsWith(SessionPrefix + "session_p5150_", StringComparison.Ordinal), "empty sanitized labels use a safe fallback");
    }

    private static void OwnerPidParsingAcceptsOnlyOwnedFormat()
    {
        string owned = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4100, "a1");
        int ownerPid;

        AssertTrue(FrameScopePresentMonSessionPolicy.TryGetOwnerPid(owned, out ownerPid), "generated name should parse");
        AssertEqual(4100, ownerPid, "parsed owner PID");

        string[] rejected =
        {
            null,
            "",
            "ThirdPartyPresentMon_p4100_a1",
            SessionPrefix + "legacy",
            SessionPrefix + "PUBG_p0_a1",
            SessionPrefix + "PUBG_p-1_a1",
            SessionPrefix + "PUBG_pnot-a-pid_a1",
            SessionPrefix + "PUBG_p04100_a1",
            SessionPrefix + "PUBG_p4100_",
            SessionPrefix + "PUBG_p4100_a1$",
            SessionPrefix + "_p4100_a1",
            SessionPrefix + "-PUBG_p4100_a1",
            SessionPrefix + "PUBG-_p4100_a1"
        };

        foreach (string candidate in rejected)
        {
            ownerPid = -1;
            AssertFalse(FrameScopePresentMonSessionPolicy.TryGetOwnerPid(candidate, out ownerPid), "reject unowned name: " + candidate);
            AssertEqual(0, ownerPid, "rejected name resets owner PID");
        }
    }

    private static void StaleSelectionStopsOnlyProvenDeadOwners()
    {
        string active = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4100, "a1");
        string stale = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4200, "b2");
        string unknown = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4300, "c3");
        string[] sessions =
        {
            active,
            stale,
            unknown,
            "ThirdPartyPresentMon",
            SessionPrefix + "legacy",
            SessionPrefix + "PUBG_pnot-a-pid_x"
        };

        List<string> selected = new List<string>(
            FrameScopePresentMonSessionPolicy.SelectStaleOwnedSessions(
                sessions,
                delegate(int pid)
                {
                    if (pid == 4300) throw new InvalidOperationException("owner state unavailable");
                    return pid == 4100;
                }));

        AssertSequence(new[] { stale }, selected, "only dead proven owner is stale");
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }

    private static void AssertNotEqual<T>(T left, T right, string label)
    {
        if (EqualityComparer<T>.Default.Equals(left, right))
        {
            throw new Exception(label + ": both values were <" + left + ">");
        }
    }

    private static void AssertSequence(IList<string> expected, IList<string> actual, string label)
    {
        AssertEqual(expected.Count, actual.Count, label + " count");
        for (int i = 0; i < expected.Count; i++)
        {
            AssertEqual(expected[i], actual[i], label + " item " + i);
        }
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
    }

    private static void AssertFalse(bool condition, string label)
    {
        if (condition) throw new Exception(label);
    }
}
