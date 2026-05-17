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
            if (!string.IsNullOrWhiteSpace(stdoutPath)) BeginCopyPipe(process.StandardOutput, stdoutPath);
            if (!string.IsNullOrWhiteSpace(stderrPath)) BeginCopyPipe(process.StandardError, stderrPath);
        }
        return process;
    }

    private static void BeginCopyPipe(StreamReader reader, string path)
    {
        if (reader == null || string.IsNullOrWhiteSpace(path)) return;
        var thread = new Thread(() =>
        {
            try
            {
                var text = reader.ReadToEnd();
                File.WriteAllText(path, text ?? "", Encoding.UTF8);
            }
            catch { }
        });
        thread.IsBackground = true;
        thread.Start();
    }

    private static void StopMonitorChild(Process process, int waitMs, bool force)
    {
        if (process == null) return;
        try
        {
            if (process.HasExited) return;
            if (waitMs > 0 && process.WaitForExit(waitMs)) return;
            if (force && !process.HasExited) process.Kill();
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
