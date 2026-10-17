using System;
using System.IO;
using System.Reflection;

public static class FrameScopeCaptureControlTests
{
    public static int Main()
    {
        try
        {
            Type policy = typeof(FrameScopeTargetLifecycle).Assembly.GetType("FrameScopeWorkerRestartPolicy");
            Type counter = typeof(FrameScopeRunContract).GetNestedType("CsvRowCounter", BindingFlags.Public);
            Assert(policy != null && counter != null, "persistent worker retry policy and incremental CSV counter are missing");
            object retry = Activator.CreateInstance(policy);
            DateTime now = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 3; i++) policy.GetMethod("RecordExit").Invoke(retry, new object[] { "game", TimeSpan.FromSeconds(1), now.AddSeconds(i) });
            Assert(!(bool)policy.GetMethod("CanStart").Invoke(retry, new object[] { "game", now.AddSeconds(3) }), "three failures must delay the same target");
            Assert((bool)policy.GetMethod("CanStart").Invoke(retry, new object[] { "other", now.AddSeconds(3) }), "one target must not delay other targets");
            Assert((bool)policy.GetMethod("CanStart").Invoke(retry, new object[] { "game", now.AddSeconds(7) }), "retry delay must expire");
            policy.GetMethod("RecordExit").Invoke(retry, new object[] { "game", TimeSpan.FromSeconds(5), now.AddSeconds(8) });
            Assert((bool)policy.GetMethod("CanStart").Invoke(retry, new object[] { "game", now.AddSeconds(8) }), "stable worker resets failures");
            string root = Path.Combine(Path.GetTempPath(), "fs-csv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string file = Path.Combine(root, "samples.csv");
                File.WriteAllText(file, "Time,ProcessName\n2026-10-13T00:00:00Z,game.exe\n");
                object rows = Activator.CreateInstance(counter, new object[] { new string[] { "Time", "ProcessName" } });
                MethodInfo poll = counter.GetMethod("Poll");
                Assert((int)poll.Invoke(rows, new object[] { file }) == 1, "initial valid row");
                long read = (long)counter.GetProperty("BytesRead").GetValue(rows, null);
                Assert((int)poll.Invoke(rows, new object[] { file }) == 1, "same rows without append");
                Assert((long)counter.GetProperty("BytesRead").GetValue(rows, null) == read, "unchanged CSV must not be rescanned");
                File.AppendAllText(file, "2026-10-13T00:00:01Z,game.exe\ninvalid,game.exe\n2026-10-13T00:00:02Z,");
                Assert((int)poll.Invoke(rows, new object[] { file }) == 2, "count only complete valid appended rows");
                File.AppendAllText(file, "game.exe\n");
                Assert((int)poll.Invoke(rows, new object[] { file }) == 3, "partial row completes once");
                File.WriteAllText(file, "Time,ProcessName\n");
                Assert((int)poll.Invoke(rows, new object[] { file }) == 0, "truncate resets row count");
                File.Move(file, file + ".old");
                File.WriteAllText(file, "Time,ProcessName\n2026-10-13T00:00:03Z,new.exe\n");
                Assert((int)poll.Invoke(rows, new object[] { file }) == 1, "replacement resets file identity");
                Assert(FrameScopeRunContract.CountValidCsvRows(file, new[] { "Time", "ProcessName" }) == 1, "final full verification agrees");
                File.WriteAllText(file, "Time,ProcessName\ninvalid," + new string('x', 200) + ".exe\n");
                Assert((int)poll.Invoke(rows, new object[] { file }) == 0, "truncate and rewrite beyond the previous offset resets rows");
                File.AppendAllText(file, "2026-10-13T00:00:05Z,new.exe\n");
                long beforeAppend = (long)counter.GetProperty("BytesRead").GetValue(rows, null);
                Assert((int)poll.Invoke(rows, new object[] { file }) == 1, "append after rewrite remains incremental");
                Assert((long)counter.GetProperty("BytesRead").GetValue(rows, null) - beforeAppend < 1024, "append verification reads a bounded anchor and new bytes");
            }
            finally { Directory.Delete(root, true); }
            Console.WriteLine("FrameScopeCaptureControlTests: PASS");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
