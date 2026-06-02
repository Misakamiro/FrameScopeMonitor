using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

internal static partial class FrameScopeNativeMonitor
{
    private static readonly object NativeMonitorPipeLock = new object();
    private static readonly Dictionary<int, List<Thread>> NativeMonitorPipeThreads = new Dictionary<int, List<Thread>>();

    private static Process StartNativeMonitorChild(string fileName, string arguments, string workingDirectory, string stdoutPath = null, string stderrPath = null)
    {
        return StartNativeMonitorChild(fileName, arguments, workingDirectory, stdoutPath, stderrPath, ProcessPriorityClass.Idle);
    }

    private static Process StartNativeMonitorChild(string fileName, string arguments, string workingDirectory, string stdoutPath, string stderrPath, ProcessPriorityClass priority)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !string.IsNullOrWhiteSpace(stdoutPath),
            RedirectStandardError = !string.IsNullOrWhiteSpace(stderrPath)
        };
        var process = Process.Start(psi);
        if (process != null)
        {
            try { process.PriorityClass = priority; }
            catch { }
            var pipeThreads = new List<Thread>();
            if (!string.IsNullOrWhiteSpace(stdoutPath)) pipeThreads.Add(BeginCopyPipe(process.StandardOutput, stdoutPath));
            if (!string.IsNullOrWhiteSpace(stderrPath)) pipeThreads.Add(BeginCopyPipe(process.StandardError, stderrPath));
            RegisterNativeMonitorPipeThreads(process, pipeThreads);
        }
        return process;
    }

    private static Thread BeginCopyPipe(StreamReader reader, string path)
    {
        if (reader == null || string.IsNullOrWhiteSpace(path)) return null;
        var thread = new Thread(() =>
        {
            try
            {
                var text = reader.ReadToEnd();
                FrameScopePresentMonDiagnostics.WriteAllText(path, text ?? "", Encoding.UTF8);
            }
            catch { }
        });
        thread.IsBackground = true;
        thread.Start();
        return thread;
    }

    private static void RegisterNativeMonitorPipeThreads(Process process, List<Thread> pipeThreads)
    {
        if (process == null || pipeThreads == null || pipeThreads.Count == 0) return;
        pipeThreads.RemoveAll(thread => thread == null);
        if (pipeThreads.Count == 0) return;
        try
        {
            lock (NativeMonitorPipeLock)
            {
                NativeMonitorPipeThreads[process.Id] = pipeThreads;
            }
        }
        catch { }
    }

    private static bool WaitForNativeMonitorChildExit(Process process, int waitMs)
    {
        return WaitForNativeMonitorChildExit(process, waitMs, 15000);
    }

    private static bool WaitForNativeMonitorChildExit(Process process, int waitMs, int outputWaitMs)
    {
        if (process == null) return true;
        var exited = false;
        try
        {
            exited = waitMs <= 0 ? process.HasExited : process.WaitForExit(waitMs);
            if (exited) WaitForNativeMonitorChildOutput(process, outputWaitMs);
        }
        catch { }
        return exited;
    }

    private static void WaitForNativeMonitorChildOutput(Process process, int waitMs)
    {
        if (process == null) return;
        List<Thread> pipeThreads = null;
        try
        {
            lock (NativeMonitorPipeLock)
            {
                if (!NativeMonitorPipeThreads.TryGetValue(process.Id, out pipeThreads)) return;
                NativeMonitorPipeThreads.Remove(process.Id);
            }
        }
        catch
        {
            return;
        }

        if (pipeThreads == null || pipeThreads.Count == 0) return;
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(1, waitMs));
        foreach (var thread in pipeThreads)
        {
            if (thread == null) continue;
            try
            {
                var remainingMs = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                if (waitMs <= 0) thread.Join();
                else thread.Join(remainingMs);
            }
            catch { }
        }
    }

    private static void StopMonitorChild(Process process, int waitMs, bool force)
    {
        if (process == null) return;
        try
        {
            if (process.HasExited)
            {
                WaitForNativeMonitorChildOutput(process, 3000);
                return;
            }
            if (waitMs > 0 && WaitForNativeMonitorChildExit(process, waitMs)) return;
            if (force && !process.HasExited)
            {
                process.Kill();
                WaitForNativeMonitorChildExit(process, 3000);
            }
        }
        catch { }
    }

    private static bool ProcessExited(Process process)
    {
        if (process == null) return true;
        try { return process.HasExited; }
        catch { return true; }
    }
}
