using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

internal static class FrameScopeReportProcessTests
{
    private static int Main(string[] args)
    {
        if (args != null && args.Length > 0) return RunFixture(args);
        try
        {
            LargeOutputIsBoundedAndKeepsTail();
            HangingChildHonorsTotalTimeout();
            TimeoutKillsDescendantProcess();
            Console.WriteLine("FrameScopeReportProcessTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int RunFixture(string[] args)
    {
        if (args[0] == "--emit-bytes")
        {
            int count = Int32.Parse(args[1], CultureInfo.InvariantCulture);
            string chunk = new string('x', 8192);
            while (count > 0)
            {
                int length = Math.Min(count, chunk.Length);
                Console.Out.Write(chunk.Substring(0, length));
                count -= length;
            }
            Console.Out.Write("TAIL-MARKER");
            return 0;
        }
        if (args[0] == "--hang")
        {
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }
        if (args[0] == "--spawn-grandchild")
        {
            string self = Process.GetCurrentProcess().MainModule.FileName;
            Process.Start(new ProcessStartInfo
            {
                FileName = self,
                Arguments = "--grandchild " + Quote(args[1]),
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Stopwatch wait = Stopwatch.StartNew();
            while (!File.Exists(args[1]) && wait.ElapsedMilliseconds < 3000) Thread.Sleep(20);
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }
        if (args[0] == "--grandchild")
        {
            File.WriteAllText(args[1], Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }
        return 2;
    }

    private static void LargeOutputIsBoundedAndKeepsTail()
    {
        FrameScopeProcessResult result = FrameScopeBoundedProcessRunner.Run(
            SelfPath(), "--emit-bytes 2097152", Environment.CurrentDirectory, 10000, null);
        AssertFalse(result.TimedOut, "large output timed out");
        AssertEqual(0, result.ExitCode, "large output exit code");
        AssertTrue(result.StandardOutput.Length <= 1024 * 1024, "stdout tail is bounded");
        AssertTrue(result.StandardOutput.EndsWith("TAIL-MARKER", StringComparison.Ordinal), "stdout tail marker preserved");
    }

    private static void HangingChildHonorsTotalTimeout()
    {
        Stopwatch timer = Stopwatch.StartNew();
        FrameScopeProcessResult result = FrameScopeBoundedProcessRunner.Run(
            SelfPath(), "--hang", Environment.CurrentDirectory, 500, null);
        timer.Stop();
        AssertTrue(result.TimedOut, "timeout flag");
        AssertTrue(timer.ElapsedMilliseconds < 5000, "bounded wall clock");
        AssertTrue(result.CanRetry, "timed-out generation can retry");
    }

    private static void TimeoutKillsDescendantProcess()
    {
        string pidFile = Path.Combine(Path.GetTempPath(), "framescope-report-grandchild-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            FrameScopeProcessResult result = FrameScopeBoundedProcessRunner.Run(
                SelfPath(), "--spawn-grandchild " + Quote(pidFile), Environment.CurrentDirectory, 1200, null);
            AssertTrue(result.TimedOut, "tree timeout flag");
            AssertTrue(File.Exists(pidFile), "grandchild pid recorded");
            int pid = Int32.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture);
            Stopwatch wait = Stopwatch.StartNew();
            while (IsAlive(pid) && wait.ElapsedMilliseconds < 3000) Thread.Sleep(50);
            AssertFalse(IsAlive(pid), "grandchild process was terminated");
        }
        finally
        {
            try { if (File.Exists(pidFile)) File.Delete(pidFile); }
            catch { }
        }
    }

    private static string SelfPath()
    {
        return Process.GetCurrentProcess().MainModule.FileName;
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using (Process process = Process.GetProcessById(pid)) return !process.HasExited;
        }
        catch { return false; }
    }

    private static string Quote(string value)
    {
        return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
    }

    private static void AssertTrue(bool value, string message)
    {
        if (!value) throw new Exception("ASSERT TRUE FAILED: " + message);
    }

    private static void AssertFalse(bool value, string message)
    {
        if (value) throw new Exception("ASSERT FALSE FAILED: " + message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!object.Equals(expected, actual)) throw new Exception(message + ": expected " + expected + ", actual " + actual);
    }
}
