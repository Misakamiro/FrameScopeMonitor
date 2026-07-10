using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

internal static class FrameScopeRunRetentionTests
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    private static int Main()
    {
        try
        {
            SelectionProtectsActiveNewestRecoverableAndOutsidePaths();
            DiskCapSelectsOldestEligibleRuns();
            ConcurrentAppendAndCompactionKeepValidHistory();
            Console.WriteLine("FrameScopeRunRetentionTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
    }

    private static void SelectionProtectsActiveNewestRecoverableAndOutsidePaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "framescope-retention-root");
        DateTime now = new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc);
        FrameScopeRunRetentionCandidate oldDone = Candidate(root, "GameA", "old", "done", now.AddDays(-30), 100);
        FrameScopeRunRetentionCandidate active = Candidate(root, "GameA", "active", "capturing", now.AddDays(-25), 100);
        FrameScopeRunRetentionCandidate newest = Candidate(root, "GameA", "newest", "done", now.AddDays(-1), 100);
        FrameScopeRunRetentionCandidate recoverable = Candidate(root, "GameB", "recoverable", "done", now.AddDays(-30), 100);
        recoverable.HasUsableMonitorData = true;
        recoverable.ReportComplete = false;
        FrameScopeRunRetentionCandidate newestB = Candidate(root, "GameB", "newest", "done", now.AddDays(-1), 100);
        FrameScopeRunRetentionCandidate generating = Candidate(root, "GameC", "generating", "done", now.AddDays(-30), 100);
        generating.ReportGenerationInProgress = true;
        FrameScopeRunRetentionCandidate newestC = Candidate(root, "GameC", "newest", "done", now.AddDays(-1), 100);
        FrameScopeRunRetentionCandidate outside = new FrameScopeRunRetentionCandidate
        {
            RunDirectory = Path.Combine(Path.GetTempPath(), "outside-retention-run"),
            TargetKey = "outside",
            Phase = "done",
            LastWriteTimeUtc = now.AddDays(-40),
            SizeBytes = 100,
            ReportComplete = true
        };

        List<FrameScopeRunRetentionCandidate> selected = FrameScopeRunRetention.Select(
            root,
            new[] { oldDone, active, newest, recoverable, newestB, generating, newestC, outside },
            now,
            14,
            10000);

        AssertContains(selected, oldDone, "expired completed run");
        AssertNotContains(selected, active, "active run");
        AssertNotContains(selected, newest, "newest run");
        AssertNotContains(selected, recoverable, "recoverable incomplete run");
        AssertNotContains(selected, generating, "report generation in progress");
        AssertNotContains(selected, outside, "outside root");
    }

    private static void DiskCapSelectsOldestEligibleRuns()
    {
        string root = Path.Combine(Path.GetTempPath(), "framescope-retention-cap-root");
        DateTime now = DateTime.UtcNow;
        FrameScopeRunRetentionCandidate oldest = Candidate(root, "Game", "oldest", "done", now.AddDays(-5), 600);
        FrameScopeRunRetentionCandidate middle = Candidate(root, "Game", "middle", "done", now.AddDays(-3), 600);
        FrameScopeRunRetentionCandidate newest = Candidate(root, "Game", "newest", "done", now.AddDays(-1), 600);

        List<FrameScopeRunRetentionCandidate> selected = FrameScopeRunRetention.Select(
            root, new[] { oldest, middle, newest }, now, 30, 1200);

        AssertContains(selected, oldest, "oldest selected to meet cap");
        AssertNotContains(selected, middle, "one deletion is sufficient");
        AssertNotContains(selected, newest, "newest remains protected");
    }

    private static void ConcurrentAppendAndCompactionKeepValidHistory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-history-concurrency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string history = Path.Combine(dir, "history.jsonl");
        try
        {
            ManualResetEvent start = new ManualResetEvent(false);
            Thread first = AppendThread(history, dir, "a", start);
            Thread second = AppendThread(history, dir, "b", start);
            Thread compact = new Thread(delegate()
            {
                start.WaitOne();
                FrameScopeHistoryFile.Compact(history, ResolveRunDir, delegate(string run) { return run.StartsWith(dir, StringComparison.OrdinalIgnoreCase); }, 500);
            });
            compact.Start();
            start.Set();
            first.Join();
            second.Join();
            compact.Join();

            string[] lines = File.ReadAllLines(history).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            AssertEqual(100, lines.Length, "history line count");
            AssertEqual(100, lines.Select(ResolveRunDir).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "history entries remain valid and unique");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static Thread AppendThread(string history, string root, string prefix, ManualResetEvent start)
    {
        Thread thread = new Thread(delegate()
        {
            start.WaitOne();
            for (int i = 0; i < 50; i++)
            {
                string run = Path.Combine(root, prefix + i);
                FrameScopeHistoryFile.Append(history, Json.Serialize(new Dictionary<string, object> { { "RunDir", run } }));
            }
        });
        thread.Start();
        return thread;
    }

    private static string ResolveRunDir(string line)
    {
        Dictionary<string, object> map = Json.Deserialize<Dictionary<string, object>>(line);
        return Convert.ToString(map["RunDir"]);
    }

    private static FrameScopeRunRetentionCandidate Candidate(string root, string target, string name, string phase, DateTime time, long bytes)
    {
        return new FrameScopeRunRetentionCandidate
        {
            RunDirectory = Path.Combine(root, target, name),
            TargetKey = target,
            Phase = phase,
            LastWriteTimeUtc = time,
            SizeBytes = bytes,
            ReportComplete = true
        };
    }

    private static void AssertContains(List<FrameScopeRunRetentionCandidate> values, FrameScopeRunRetentionCandidate value, string label)
    {
        if (!values.Contains(value)) throw new Exception("Missing: " + label);
    }

    private static void AssertNotContains(List<FrameScopeRunRetentionCandidate> values, FrameScopeRunRetentionCandidate value, string label)
    {
        if (values.Contains(value)) throw new Exception("Unexpected: " + label);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!object.Equals(expected, actual)) throw new Exception(label + ": expected " + expected + ", actual " + actual);
    }
}
