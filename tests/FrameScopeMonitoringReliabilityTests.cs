using System;
using System.Collections.Generic;
using System.IO;

public static class FrameScopeMonitoringReliabilityTests
{
    private const string SessionPrefix = "FrameScopeNativePresentMon_";

    public static int Main()
    {
        try
        {
            SessionNamesEncodeOwnerAndNonce();
            SessionNamesSanitizeAndBoundLabels();
            OwnerPidParsingAcceptsOnlyOwnedFormat();
            StaleSelectionStopsOnlyProvenDeadOwners();
            StartupStopTargetsOnlyStaleOwnedSessions();
            OwnedShutdownTargetsOnlyTheExplicitSession();
            FailedOwnedShutdownReportsFailure();
            UntilTargetExitRequiresAllAliasesToExit();
            TimedCaptureStopsOnlyAtDeadline();
            CanonicalTargetKeyNormalizesAliasSets();
            CanonicalTargetKeyDistinguishesAliasSets();
            CanonicalTargetKeyHandlesEmptyInput();
            WatcherUsesCanonicalAliasIdentity();
            CaptureLoopUsesFullAliasLifetimePolicy();
            Console.WriteLine("FrameScopeMonitoringReliabilityTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
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

    private static void StartupStopTargetsOnlyStaleOwnedSessions()
    {
        string active = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4100, "a1");
        string stale = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4200, "b2");
        var primaryCalls = new List<string>();
        var fallbackCalls = new List<string>();

        IList<FrameScopePresentMonSessionStopResult> results =
            FrameScopePresentMonSessionPolicy.StopStaleOwnedSessions(
                new[]
                {
                    active,
                    stale,
                    "ThirdPartyPresentMon",
                    SessionPrefix + "legacy",
                    SessionPrefix + "PUBG_pnot-a-pid_x"
                },
                delegate(int pid) { return pid == 4100; },
                delegate(string session)
                {
                    primaryCalls.Add(session);
                    return false;
                },
                delegate(string session)
                {
                    fallbackCalls.Add(session);
                    return true;
                });

        AssertSequence(new[] { stale }, primaryCalls, "startup primary receives only stale owned session");
        AssertSequence(new[] { stale }, fallbackCalls, "startup fallback receives only the same stale owned session");
        AssertEqual(1, results.Count, "startup stop result count");
        AssertEqual(stale, results[0].SessionName, "startup stop result session");
        AssertFalse(results[0].PrimarySucceeded, "startup primary result");
        AssertTrue(results[0].FallbackAttempted, "startup fallback attempted");
        AssertTrue(results[0].FallbackSucceeded, "startup fallback result");
        AssertTrue(results[0].Succeeded, "fallback success makes startup stop successful");
    }

    private static void OwnedShutdownTargetsOnlyTheExplicitSession()
    {
        string owned = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 5100, "owner1");
        string peer = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 5200, "peer1");
        var primaryCalls = new List<string>();
        var fallbackCalls = new List<string>();

        FrameScopePresentMonSessionStopResult result = FrameScopePresentMonSessionPolicy.StopOwnedSession(
            owned,
            delegate(string session)
            {
                primaryCalls.Add(session);
                return false;
            },
            delegate(string session)
            {
                fallbackCalls.Add(session);
                return true;
            });

        AssertSequence(new[] { owned }, primaryCalls, "owned shutdown primary receives only explicit session");
        AssertSequence(new[] { owned }, fallbackCalls, "owned shutdown fallback receives only explicit session");
        AssertFalse(primaryCalls.Contains(peer), "owned shutdown must not receive peer session");
        AssertFalse(fallbackCalls.Contains(peer), "owned shutdown fallback must not receive peer session");
        AssertFalse(result.PrimarySucceeded, "owned shutdown primary result");
        AssertTrue(result.FallbackAttempted, "owned shutdown fallback attempted");
        AssertTrue(result.FallbackSucceeded, "owned shutdown fallback result");
        AssertTrue(result.Succeeded, "owned shutdown result");
    }

    private static void FailedOwnedShutdownReportsFailure()
    {
        string owned = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 5300, "owner2");

        FrameScopePresentMonSessionStopResult result = FrameScopePresentMonSessionPolicy.StopOwnedSession(
            owned,
            delegate(string session) { return false; },
            delegate(string session) { return false; });

        AssertFalse(result.PrimarySucceeded, "failed shutdown primary result");
        AssertTrue(result.FallbackAttempted, "failed shutdown fallback attempted");
        AssertFalse(result.FallbackSucceeded, "failed shutdown fallback result");
        AssertFalse(result.Succeeded, "failed shutdown combined result");
    }

    private static void UntilTargetExitRequiresAllAliasesToExit()
    {
        AssertFalse(FrameScopeTargetLifecycle.ShouldStopCapture(true, true, true, false), "replacement alias alive");
        AssertTrue(FrameScopeTargetLifecycle.ShouldStopCapture(true, true, false, false), "all aliases gone");
        AssertFalse(FrameScopeTargetLifecycle.ShouldStopCapture(true, false, false, false), "selected pid still alive");
    }

    private static void TimedCaptureStopsOnlyAtDeadline()
    {
        AssertTrue(FrameScopeTargetLifecycle.ShouldStopCapture(false, true, true, true), "timed deadline");
        AssertFalse(FrameScopeTargetLifecycle.ShouldStopCapture(false, true, false, false), "timed capture before deadline");
    }

    private static void CanonicalTargetKeyNormalizesAliasSets()
    {
        string configured = FrameScopeTargetLifecycle.CanonicalTargetKey(new[] { " TslGame.exe ", "TslGame_BE", "tslgame" });
        string reordered = FrameScopeTargetLifecycle.CanonicalTargetKey(new[] { "tslgame_be.exe", "tslgame.exe" });

        AssertEqual("tslgame;tslgame_be", configured, "canonical key normalization");
        AssertEqual(configured, reordered, "alias order, casing, whitespace, suffix, and duplicates must not affect identity");
    }

    private static void CanonicalTargetKeyDistinguishesAliasSets()
    {
        string first = FrameScopeTargetLifecycle.CanonicalTargetKey(new[] { "TslGame", "TslGame_BE" });
        string second = FrameScopeTargetLifecycle.CanonicalTargetKey(new[] { "TslGame", "TslGame-Win64-Shipping" });

        AssertNotEqual(first, second, "different normalized alias sets must differ");
    }

    private static void CanonicalTargetKeyHandlesEmptyInput()
    {
        AssertEqual("", FrameScopeTargetLifecycle.CanonicalTargetKey(null), "null aliases");
        AssertEqual("", FrameScopeTargetLifecycle.CanonicalTargetKey(new string[] { null, "", "   ", ".exe" }), "empty normalized aliases");
    }

    private static void WatcherUsesCanonicalAliasIdentity()
    {
        string source = ReadSource("src", "app", "FrameScopeNativeMonitor.Watcher.cs");

        AssertContains(source, "var key = FrameScopeTargetLifecycle.CanonicalTargetKey(processBases);", "watcher active-monitor key");
        AssertContains(source, "var active = activeMonitors.Select(entry => new", "watcher state must preserve dictionary identity");
        AssertContains(source, "Key = entry.Key", "watcher state canonical key");
        AssertDoesNotContain(source, "var key = processBase.ToLowerInvariant();", "watcher must not use the first alias as identity");
        AssertDoesNotContain(source, "Key = GetTargetBaseName(item.Target.ProcessName).ToLowerInvariant()", "watcher state must not reconstruct identity from one process name");
    }

    private static void CaptureLoopUsesFullAliasLifetimePolicy()
    {
        string source = ReadSource("src", "app", "FrameScopeNativeMonitor.MonitorSession.cs");

        AssertContains(source, "FrameScopeTargetLifecycle.ShouldStopCapture(", "capture loop lifecycle policy");
        AssertContains(source, "selectedPidExited", "capture loop selected PID state");
        AssertContains(source, "anyAliasRunning = IsAnyTargetProcessRunning(targetProcessBases)", "capture loop alias liveness query");
        AssertDoesNotContain(source, "if (targetProc.WaitForExit(remainingMs)) break;", "selected PID exit must not stop capture directly");
    }

    private static string ReadSource(params string[] parts)
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        string path = Path.Combine(root, Path.Combine(parts));
        if (!File.Exists(path)) throw new Exception("Source not found: " + path);
        return File.ReadAllText(path);
    }

    private static void AssertContains(string text, string expected, string label)
    {
        if (text == null || text.IndexOf(expected, StringComparison.Ordinal) < 0)
        {
            throw new Exception(label + ": missing <" + expected + ">");
        }
    }

    private static void AssertDoesNotContain(string text, string unexpected, string label)
    {
        if (text != null && text.IndexOf(unexpected, StringComparison.Ordinal) >= 0)
        {
            throw new Exception(label + ": unexpected <" + unexpected + ">");
        }
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
