using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

internal sealed class FrameScopeProcessResult
{
    internal bool Started;
    internal int ProcessId;
    internal int ExitCode = -1;
    internal bool TimedOut;
    internal bool CanRetry;
    internal DateTime StartedAtUtc;
    internal DateTime EndedAtUtc;
    internal string StandardOutput = "";
    internal string StandardError = "";
    internal string Error = "";
}

internal static class FrameScopeBoundedProcessRunner
{
    internal const int MaxCapturedOutputBytes = 1024 * 1024;
    private const int ReadBufferChars = 4096;

    internal static FrameScopeProcessResult Run(
        string fileName,
        string arguments,
        string workingDirectory,
        int timeoutMs,
        Action waitingCallback)
    {
        var result = new FrameScopeProcessResult();
        var output = new RollingTextTail(MaxCapturedOutputBytes);
        var error = new RollingTextTail(MaxCapturedOutputBytes);
        Process process = null;
        Thread outputThread = null;
        Thread errorThread = null;
        try
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? "",
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            result.StartedAtUtc = DateTime.UtcNow;
            if (!process.Start())
            {
                result.Error = "Failed to start process.";
                result.CanRetry = true;
                return result;
            }

            result.Started = true;
            result.ProcessId = process.Id;
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
            catch { }
            outputThread = StartDrain(process.StandardOutput, output);
            errorThread = StartDrain(process.StandardError, error);

            int totalTimeoutMs = Math.Max(1, timeoutMs);
            Stopwatch timer = Stopwatch.StartNew();
            while (true)
            {
                int remaining = totalTimeoutMs - (int)Math.Min(Int32.MaxValue, timer.ElapsedMilliseconds);
                if (remaining <= 0)
                {
                    result.TimedOut = true;
                    result.CanRetry = true;
                    result.Error = "Process timed out after " + totalTimeoutMs + " ms.";
                    KillProcessTree(process);
                    break;
                }

                if (process.WaitForExit(Math.Min(250, remaining))) break;
                if (waitingCallback != null)
                {
                    try { waitingCallback(); }
                    catch { }
                }
            }

            try { if (!process.HasExited) process.WaitForExit(5000); }
            catch { }
            JoinDrain(outputThread, 5000);
            JoinDrain(errorThread, 5000);
            try { if (process.HasExited) result.ExitCode = process.ExitCode; }
            catch { }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.CanRetry = true;
            if (process != null) KillProcessTree(process);
        }
        finally
        {
            result.StandardOutput = output.GetText();
            result.StandardError = error.GetText();
            result.EndedAtUtc = DateTime.UtcNow;
            if (process != null) process.Dispose();
        }
        return result;
    }

    private static Thread StartDrain(StreamReader reader, RollingTextTail target)
    {
        var thread = new Thread(delegate()
        {
            try
            {
                char[] buffer = new char[ReadBufferChars];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) target.Append(buffer, read);
            }
            catch { }
        });
        thread.IsBackground = true;
        thread.Start();
        return thread;
    }

    private static void JoinDrain(Thread thread, int waitMs)
    {
        if (thread == null) return;
        try { thread.Join(waitMs); }
        catch { }
    }

    private static void KillProcessTree(Process process)
    {
        if (process == null) return;
        int pid;
        try
        {
            if (process.HasExited) return;
            pid = process.Id;
        }
        catch { return; }

        try
        {
            string taskKill = Path.Combine(Environment.SystemDirectory, "taskkill.exe");
            using (Process killer = Process.Start(new ProcessStartInfo
            {
                FileName = taskKill,
                Arguments = "/PID " + pid + " /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }))
            {
                if (killer != null) killer.WaitForExit(5000);
            }
        }
        catch { }

        try
        {
            if (!process.HasExited) process.Kill();
        }
        catch { }
    }

    private sealed class RollingTextTail
    {
        private readonly int maxChars;
        private readonly StringBuilder value = new StringBuilder();

        internal RollingTextTail(int maxBytes)
        {
            maxChars = Math.Max(1, maxBytes);
        }

        internal void Append(char[] buffer, int count)
        {
            if (buffer == null || count <= 0) return;
            value.Append(buffer, 0, count);
            int overflow = value.Length - maxChars;
            if (overflow > 0) value.Remove(0, overflow);
        }

        internal string GetText()
        {
            string text = value.ToString();
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length <= MaxCapturedOutputBytes) return text;
            int offset = bytes.Length - MaxCapturedOutputBytes;
            while (offset < bytes.Length && (bytes[offset] & 0xC0) == 0x80) offset++;
            return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        }
    }
}
