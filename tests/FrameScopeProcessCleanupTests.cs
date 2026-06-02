using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;

public static class FrameScopeProcessCleanupTests
{
    public static int Main(string[] args)
    {
        if (args != null && Array.IndexOf(args, "--watcher-sleep") >= 0)
        {
            Thread.Sleep(30000);
            return 0;
        }

        try
        {
            StopAndWaitTerminatesFrameScopeWatcherProcess();
            Console.WriteLine("FrameScopeProcessCleanupTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
    }

    private static void StopAndWaitTerminatesFrameScopeWatcherProcess()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string childExe = Path.Combine(root, "FrameScopeMonitor.exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, childExe, true);

        Process child = null;
        try
        {
            child = Process.Start(new ProcessStartInfo
            {
                FileName = childExe,
                Arguments = "--watcher --watcher-sleep",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            AssertTrue(child != null, "fake watcher process started");
            Thread.Sleep(500);

            int remaining = FrameScopeNativeMonitor.StopFrameScopeBackgroundProcessesAndWait(5000);

            AssertEqual(0, remaining, "remaining FrameScope background process count");
            AssertTrue(child.WaitForExit(5000), "fake watcher process exited");
        }
        finally
        {
            try
            {
                if (child != null && !child.HasExited)
                {
                    child.Kill();
                    child.WaitForExit(3000);
                }
            }
            catch { }
            try { if (File.Exists(childExe)) File.Delete(childExe); }
            catch { }
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
    }
}

internal static partial class FrameScopeNativeMonitor
{
    private static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string StatePath = Path.Combine(Root, "framescope-watcher-state.json");
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
}
