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

internal sealed class NativeMonitorSamplerState
{
    public bool Required;
    public string ExecutablePath = "";
    public string CsvPath = "";
    public string StdoutPath = "";
    public string StderrPath = "";
    public string StartError = "";
    public Process Process;
    public bool Started;
    public int? Pid;
    public DateTime? StartedAt;
    public DateTime? ExitedAt;
    public int? ExitCode;
    public bool ExitedEarly;
    public bool StopRequested;
    public DateTime? StopRequestedAt = null;
    public bool ForcedStop;
}

internal static partial class FrameScopeNativeMonitor
{
    // Keep enough diagnostics for failure classification without allowing a noisy child to grow memory or disk without bound.
    private const int NativeMonitorChildLogMaxBytes = 1024 * 1024;
    private const int NativeMonitorPipeReadBufferChars = 4096;
    private static readonly object NativeMonitorPipeLock = new object();
    private static readonly Dictionary<int, List<Thread>> NativeMonitorPipeThreads = new Dictionary<int, List<Thread>>();

    private sealed class NativeMonitorRollingTextBuffer
    {
        private readonly char[] buffer;
        private int start;
        private int count;

        internal NativeMonitorRollingTextBuffer(int capacity)
        {
            buffer = new char[Math.Max(1, capacity)];
        }

        internal void Append(char[] source, int offset, int length)
        {
            if (source == null || length <= 0) return;
            if (length >= buffer.Length)
            {
                Array.Copy(source, offset + length - buffer.Length, buffer, 0, buffer.Length);
                start = 0;
                count = buffer.Length;
                return;
            }

            int overflow = Math.Max(0, count + length - buffer.Length);
            if (overflow > 0)
            {
                start = (start + overflow) % buffer.Length;
                count -= overflow;
            }

            int writeAt = (start + count) % buffer.Length;
            int firstLength = Math.Min(length, buffer.Length - writeAt);
            Array.Copy(source, offset, buffer, writeAt, firstLength);
            if (firstLength < length)
            {
                Array.Copy(source, offset + firstLength, buffer, 0, length - firstLength);
            }
            count += length;
        }

        internal string GetText()
        {
            if (count == 0) return "";
            char[] result = new char[count];
            int firstLength = Math.Min(count, buffer.Length - start);
            Array.Copy(buffer, start, result, 0, firstLength);
            if (firstLength < count)
            {
                Array.Copy(buffer, 0, result, firstLength, count - firstLength);
            }
            return new string(result);
        }
    }

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
            var rolling = new NativeMonitorRollingTextBuffer(NativeMonitorChildLogMaxBytes);
            try
            {
                char[] readBuffer = new char[NativeMonitorPipeReadBufferChars];
                int read;
                while ((read = reader.Read(readBuffer, 0, readBuffer.Length)) > 0)
                {
                    rolling.Append(readBuffer, 0, read);
                }
            }
            catch { }
            try { WriteBoundedNativeMonitorChildLog(path, rolling.GetText(), NativeMonitorChildLogMaxBytes); }
            catch { }
        });
        thread.IsBackground = true;
        thread.Start();
        return thread;
    }

    private static void WriteBoundedNativeMonitorChildLog(string path, string text, int maxBytes)
    {
        int byteLimit = Math.Max(1, maxBytes);
        int preambleBytes = Encoding.UTF8.GetPreamble().Length;
        int payloadLimit = Math.Max(0, byteLimit - preambleBytes);
        var payloadEncoding = new UTF8Encoding(false);
        byte[] payload = payloadEncoding.GetBytes(text ?? "");
        int offset = Math.Max(0, payload.Length - payloadLimit);
        while (offset < payload.Length && (payload[offset] & 0xC0) == 0x80) offset++;
        string boundedText = payloadEncoding.GetString(payload, offset, payload.Length - offset);
        FrameScopePresentMonDiagnostics.WriteAllText(path, boundedText, Encoding.UTF8);
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
        int processId;
        try
        {
            processId = process.Id;
            lock (NativeMonitorPipeLock)
            {
                if (!NativeMonitorPipeThreads.TryGetValue(processId, out pipeThreads)) return;
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

        bool completed = true;
        foreach (var thread in pipeThreads)
        {
            if (thread == null) continue;
            try
            {
                if (thread.IsAlive)
                {
                    completed = false;
                    break;
                }
            }
            catch
            {
                completed = false;
                break;
            }
        }
        if (!completed) return;

        RemoveNativeMonitorPipeRegistration(processId, pipeThreads);
    }

    private static void RemoveNativeMonitorPipeRegistration(int processId, List<Thread> pipeThreads)
    {
        lock (NativeMonitorPipeLock)
        {
            List<Thread> registeredThreads;
            if (NativeMonitorPipeThreads.TryGetValue(processId, out registeredThreads)
                && object.ReferenceEquals(registeredThreads, pipeThreads))
            {
                NativeMonitorPipeThreads.Remove(processId);
            }
        }
    }

    private static void BeginFinalNativeMonitorPipeCleanup(int processId, List<Thread> pipeThreads)
    {
        if (processId <= 0 || pipeThreads == null || pipeThreads.Count == 0) return;
        var cleanupThread = new Thread(() =>
        {
            foreach (var pipeThread in pipeThreads)
            {
                if (pipeThread == null) continue;
                try { pipeThread.Join(); }
                catch { return; }
            }
            RemoveNativeMonitorPipeRegistration(processId, pipeThreads);
        });
        cleanupThread.IsBackground = true;
        cleanupThread.Start();
    }

    private static void DisposeNativeMonitorChild(Process process)
    {
        DisposeNativeMonitorChild(process, 5000);
    }

    private static void DisposeNativeMonitorChild(Process process, int outputWaitMs)
    {
        if (process == null) return;
        int processId = 0;
        try { processId = process.Id; }
        catch { }
        WaitForNativeMonitorChildOutput(process, outputWaitMs);
        List<Thread> remainingPipeThreads = null;
        if (processId > 0)
        {
            lock (NativeMonitorPipeLock)
            {
                NativeMonitorPipeThreads.TryGetValue(processId, out remainingPipeThreads);
            }
        }
        if (remainingPipeThreads != null) BeginFinalNativeMonitorPipeCleanup(processId, remainingPipeThreads);
        try { process.Dispose(); }
        catch { }
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

    private static NativeMonitorSamplerState CreateNativeMonitorSamplerState(bool required, string executablePath, string csvPath, string stdoutPath, string stderrPath)
    {
        return new NativeMonitorSamplerState
        {
            Required = required,
            ExecutablePath = executablePath ?? "",
            CsvPath = csvPath ?? "",
            StdoutPath = stdoutPath ?? "",
            StderrPath = stderrPath ?? ""
        };
    }

    private static Process StartNativeMonitorSampler(NativeMonitorSamplerState state, string arguments, string workingDirectory)
    {
        if (state == null) return null;
        if (string.IsNullOrWhiteSpace(state.ExecutablePath) || !File.Exists(state.ExecutablePath))
        {
            state.StartError = "Sampler executable was not found: " + (state.ExecutablePath ?? "");
            return null;
        }
        try
        {
            state.Process = StartNativeMonitorChild(
                state.ExecutablePath,
                arguments,
                workingDirectory,
                state.StdoutPath,
                state.StderrPath,
                ProcessPriorityClass.Idle);
            if (state.Process != null)
            {
                state.Started = true;
                state.Pid = state.Process.Id;
                state.StartedAt = DateTime.Now;
            }
            else
            {
                state.StartError = "Process.Start returned null.";
            }
        }
        catch (Exception ex)
        {
            state.StartError = ex.ToString();
        }
        return state.Process;
    }

    private static void RecordNativeMonitorSamplerExit(NativeMonitorSamplerState state, bool beforeOwnerStopRequest)
    {
        if (state == null || state.Process == null || state.ExitedAt.HasValue) return;
        bool exited;
        try { exited = state.Process.HasExited; }
        catch { exited = true; }
        if (!exited) return;

        try { state.ExitCode = state.Process.ExitCode; }
        catch { }
        try { state.ExitedAt = state.Process.ExitTime; }
        catch { state.ExitedAt = DateTime.Now; }
        if (beforeOwnerStopRequest || IsNativeMonitorSamplerEarlyExit(state.ExitedAt.Value, state.StopRequestedAt, state.StopRequested)) state.ExitedEarly = true;
        WaitForNativeMonitorChildOutput(state.Process, 5000);
    }

    private static bool IsNativeMonitorSamplerEarlyExit(DateTime exitedAt, DateTime? stopRequestedAt, bool stopRequested)
    {
        if (!stopRequested || !stopRequestedAt.HasValue) return true;
        return exitedAt < stopRequestedAt.Value;
    }

    private static void StopNativeMonitorSampler(NativeMonitorSamplerState state, int waitMs)
    {
        if (state == null || state.Process == null) return;
        RecordNativeMonitorSamplerExit(state, !state.StopRequested);
        if (state.ExitedAt.HasValue) return;

        bool exited = WaitForNativeMonitorChildExit(state.Process, waitMs, 5000);
        if (!exited)
        {
            try
            {
                if (!state.Process.HasExited)
                {
                    state.ForcedStop = true;
                    state.Process.Kill();
                }
            }
            catch { }
            WaitForNativeMonitorChildExit(state.Process, 3000, 5000);
        }
        RecordNativeMonitorSamplerExit(state, false);
    }

    private static FrameScopeSamplerEvidence BuildNativeMonitorSamplerEvidence(NativeMonitorSamplerState state, string[] requiredCsvColumns)
    {
        state = state ?? new NativeMonitorSamplerState { Required = true };
        bool csvExists = false;
        long csvBytes = 0;
        try
        {
            csvExists = !string.IsNullOrWhiteSpace(state.CsvPath) && File.Exists(state.CsvPath);
            if (csvExists) csvBytes = new FileInfo(state.CsvPath).Length;
        }
        catch { }

        string errorTail = ReadNativeMonitorSamplerTail(state.StderrPath, 4096);
        if (!string.IsNullOrWhiteSpace(state.StartError))
        {
            errorTail = string.IsNullOrWhiteSpace(errorTail)
                ? state.StartError.Trim()
                : state.StartError.Trim() + Environment.NewLine + errorTail;
            if (errorTail.Length > 4096) errorTail = errorTail.Substring(errorTail.Length - 4096);
        }

        FrameScopeSamplerEvidence evidence = new FrameScopeSamplerEvidence
        {
            Required = state.Required,
            ExecutablePath = state.ExecutablePath ?? "",
            ExecutableAvailable = !string.IsNullOrWhiteSpace(state.ExecutablePath) && File.Exists(state.ExecutablePath),
            Started = state.Started,
            Pid = state.Pid,
            StartedAt = state.StartedAt,
            ExitedAt = state.ExitedAt,
            ExitCode = state.ExitCode,
            ExitedEarly = state.ExitedEarly,
            StopRequested = state.StopRequested,
            ForcedStop = state.ForcedStop,
            CsvPath = state.CsvPath ?? "",
            CsvExists = csvExists,
            CsvBytes = csvBytes,
            ValidRows = FrameScopeRunContract.CountValidCsvRows(state.CsvPath, requiredCsvColumns),
            ErrorTail = errorTail
        };
        evidence.Status = FrameScopeRunContract.NormalizeSamplerStatus(evidence);
        return evidence;
    }

    private static Dictionary<string, object> BuildNativeMonitorSamplerDiagnostics(NativeMonitorSamplerState processSampler, NativeMonitorSamplerState systemSampler)
    {
        Dictionary<string, object> diagnostics = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        AddNativeMonitorSamplerFields(diagnostics, "ProcessSampler", BuildNativeMonitorSamplerEvidence(processSampler, new[] { "Time", "ProcessName" }));
        AddNativeMonitorSamplerFields(diagnostics, "SystemSampler", BuildNativeMonitorSamplerEvidence(systemSampler, new[] { "Time" }));
        return diagnostics;
    }

    private static void AddNativeMonitorSamplerFields(Dictionary<string, object> target, string prefix, FrameScopeSamplerEvidence evidence)
    {
        FrameScopeRunContract.AddStatusFields(target, prefix, evidence);
    }

    private static string ReadNativeMonitorSamplerTail(string path, int maxChars)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !FrameScopePresentMonDiagnostics.FileExists(path)) return "";
            int limit = Math.Max(1, maxChars);
            string streamPath = ToNativeMonitorStreamPath(path);
            using (FileStream stream = new FileStream(streamPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long readBytes = Math.Min(stream.Length, ((long)limit * 4L) + 4L);
                if (readBytes < stream.Length) stream.Seek(-readBytes, SeekOrigin.End);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var text = new StringBuilder();
                    char[] buffer = new char[Math.Min(4096, limit)];
                    int read;
                    while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) text.Append(buffer, 0, read);
                    string tail = text.ToString().Trim();
                    return tail.Length <= limit ? tail : tail.Substring(tail.Length - limit);
                }
            }
        }
        catch
        {
            return "";
        }
    }

    private static string ToNativeMonitorStreamPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.DirectorySeparatorChar != '\\' || path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        string fullPath = Path.GetFullPath(path);
        if (fullPath.Length < 248) return path;
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + fullPath.Substring(2);
        return @"\\?\" + fullPath;
    }
}
