using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal sealed class FrameScopePresentMonCaptureDiagnosticContext
{
    public FrameScopePresentMonCaptureDiagnosticContext()
    {
        TargetProcessName = "";
        TargetResolvedProcess = "";
        PresentMonArgs = "";
        PresentMonStartedAt = "";
        PresentMonExitedAt = "";
    }

    public string TargetProcessName { get; set; }
    public string TargetResolvedProcess { get; set; }
    public int? TargetPid { get; set; }
    public string PresentMonArgs { get; set; }
    public long? PresentMonRuntimeMs { get; set; }
    public string PresentMonStartedAt { get; set; }
    public string PresentMonExitedAt { get; set; }
    public bool? TargetRunningAtPresentMonExitCheck { get; set; }
    public bool? TargetPidRunningAtPresentMonExitCheck { get; set; }
    public long? PresentMonCsvPostExitWaitMs { get; set; }
}

internal static class FrameScopePresentMonDiagnostics
{
    internal const string EtwAccessDeniedStatus = "presentmon-etw-access-denied";
    internal const string EtwAccessDeniedMessage = "PresentMon 无法启动 ETW trace，需要管理员/Performance Log Users/系统 ETW 权限检查。";
    internal const string SilentNoCsvStatus = "presentmon-no-csv-silent";
    private const string PerformanceLogUsersSid = "S-1-5-32-559";
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint CreateAlways = 2;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint InvalidFileAttributes = 0xFFFFFFFF;

    internal static Dictionary<string, object> BuildCaptureDiagnostics(
        string presentMonCsv,
        string presentMonStdout,
        string presentMonStderr,
        int? presentMonExitCode,
        bool presentMonExitedEarly,
        bool presentMonForcedStop)
    {
        return BuildCaptureDiagnostics(
            presentMonCsv,
            presentMonStdout,
            presentMonStderr,
            presentMonExitCode,
            presentMonExitedEarly,
            presentMonForcedStop,
            null);
    }

    internal static Dictionary<string, object> BuildCaptureDiagnostics(
        string presentMonCsv,
        string presentMonStdout,
        string presentMonStderr,
        int? presentMonExitCode,
        bool presentMonExitedEarly,
        bool presentMonForcedStop,
        FrameScopePresentMonCaptureDiagnosticContext context)
    {
        Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        bool exists = FileExists(presentMonCsv);
        long bytes = exists ? FileLength(presentMonCsv) : 0;
        int rows = exists ? CountCsvDataRows(presentMonCsv, 2000000) : 0;
        string csvCheckTime = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
        string stdoutTail = TailText(presentMonStdout, 2000);
        string stderrTail = TailText(presentMonStderr, 4000);
        bool etwAccessDenied = IsEtwAccessDenied(stderrTail);
        bool silentNoCsv = IsSilentNoCsvResult(exists, presentMonExitCode, stdoutTail, stderrTail);
        string status = "captured";
        string message = "PresentMon 已成功写入帧数据。";
        string failureCategory = "";

        if (etwAccessDenied)
        {
            status = EtwAccessDeniedStatus;
            message = EtwAccessDeniedMessage;
            failureCategory = "presentmon-etw-access-denied";
        }
        else if (silentNoCsv)
        {
            status = SilentNoCsvStatus;
            message = BuildSilentNoCsvMessage(context);
            failureCategory = SilentNoCsvStatus;
        }
        else if (!exists)
        {
            status = "no-presentmon-csv";
            message = BuildGenericNoCsvMessage(context);
            failureCategory = "missing-presentmon-csv";
        }
        else if (bytes <= 0)
        {
            status = "empty-presentmon-csv";
            message = BuildEmptyCsvMessage(context);
            failureCategory = "empty-presentmon-csv";
        }
        else if (rows <= 0)
        {
            status = "no-presentmon-rows";
            message = "PresentMon 创建了 presentmon.csv，但没有写入帧记录。FrameScope 仍会保留进程和系统采样数据用于诊断。";
            failureCategory = "no-presentmon-rows";
        }
        else if (presentMonExitCode.HasValue && presentMonExitCode.Value != 0)
        {
            status = "presentmon-exit-error";
            message = "PresentMon 写入了部分输出，但以非 0 代码退出。请查看 presentmon.stderr.log 获取具体采集错误。";
            failureCategory = "presentmon-exit-error";
        }
        else if (presentMonExitedEarly)
        {
            status = "presentmon-exited-early";
            message = BuildExitedEarlyMessage(context);
            failureCategory = "presentmon-exited-early";
        }

        result["PresentMonCsvPath"] = presentMonCsv ?? "";
        result["PresentMonCsvExists"] = exists;
        result["PresentMonCsvBytes"] = bytes;
        result["PresentMonCsvRows"] = rows;
        result["PresentMonCsvLastCheckTime"] = csvCheckTime;
        result["PresentMonStdoutTail"] = stdoutTail;
        result["PresentMonStderrTail"] = stderrTail;
        result["FrameCaptureStatus"] = status;
        result["FrameCaptureMessage"] = message;
        result["PresentMonFailureCategory"] = failureCategory;
        result["PresentMonEtwAccessDenied"] = etwAccessDenied;
        result["PresentMonExitCode"] = presentMonExitCode;
        result["PresentMonExitedEarly"] = presentMonExitedEarly;
        result["PresentMonForcedStop"] = presentMonForcedStop;
        AddContext(result, context);
        return result;
    }

    internal static Dictionary<string, object> BuildPreflightDiagnostics(string presentMonPath)
    {
        Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        result["PresentMonPreflightIsElevated"] = IsCurrentProcessElevated();
        result["PresentMonPreflightInPerformanceLogUsers"] = IsCurrentUserInPerformanceLogUsers();
        result["PresentMonPreflightToolExists"] = !string.IsNullOrWhiteSpace(presentMonPath) && File.Exists(presentMonPath);
        result["PresentMonPreflightToolPath"] = presentMonPath ?? "";
        result["PresentMonPreflightEtwProbeAttempted"] = false;
        result["PresentMonPreflightEtwProbeReason"] = "Skipped to avoid opening an extra ETW trace session before capture.";
        return result;
    }

    private static bool IsEtwAccessDenied(string stderrTail)
    {
        if (string.IsNullOrWhiteSpace(stderrTail)) return false;
        return stderrTail.IndexOf("failed to start trace session", StringComparison.OrdinalIgnoreCase) >= 0
            && stderrTail.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool IsSilentNoCsvResult(bool csvExists, int? presentMonExitCode, string stdoutTail, string stderrTail)
    {
        if (csvExists) return false;
        if (!presentMonExitCode.HasValue || presentMonExitCode.Value != 0) return false;
        if (!string.IsNullOrWhiteSpace(stderrTail)) return false;
        return (stdoutTail ?? "").IndexOf("Started recording", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static string BuildSilentNoCsvMessage(FrameScopePresentMonCaptureDiagnosticContext context)
    {
        string target = ContextTargetLabel(context);
        return "PresentMon 已对" + target + "报告开始 recording，并以 0 退出且 stderr 为空，但没有创建 presentmon.csv。FrameScope 不会伪造 FPS；这表示当前目标没有产生可写入的帧数据。可能原因包括目标/渲染进程生命周期、全屏或覆盖层采集限制、ETW/反作弊静默阻断，或 PresentMon 没收到该目标的 present events。";
    }

    internal static string BuildGenericNoCsvMessage(FrameScopePresentMonCaptureDiagnosticContext context)
    {
        string target = ContextTargetLabel(context);
        return "PresentMon 已启动，但没有为" + target + "创建 presentmon.csv。FrameScope 不会伪造 FPS；请查看 PresentMon stdout/stderr、目标进程生命周期、权限级别、全屏/覆盖层和 ETW/反作弊限制。";
    }

    private static string BuildEmptyCsvMessage(FrameScopePresentMonCaptureDiagnosticContext context)
    {
        string target = ContextTargetLabel(context);
        return "PresentMon 为" + target + "创建了 presentmon.csv，但文件为空。FrameScope 会生成 diagnostic 报告，并保留系统与进程采样用于排查。";
    }

    private static string BuildExitedEarlyMessage(FrameScopePresentMonCaptureDiagnosticContext context)
    {
        string target = ContextTargetLabel(context);
        return "PresentMon 在 FrameScope 请求停止前提前退出。" + target + "可能发生了目标进程、渲染进程或 capture 会话生命周期变化；FrameScope 会保留已采集的诊断信息。";
    }

    private static string ContextTargetLabel(FrameScopePresentMonCaptureDiagnosticContext context)
    {
        string target = "";
        if (context != null)
        {
            target = FirstNonEmpty(context.TargetResolvedProcess, context.TargetProcessName);
            if (string.IsNullOrWhiteSpace(target) && context.TargetPid.HasValue)
            {
                target = "PID " + context.TargetPid.Value.ToString(CultureInfo.InvariantCulture);
            }
        }
        if (string.IsNullOrWhiteSpace(target)) return "当前目标";
        return "当前目标 " + target + " ";
    }

    private static void AddContext(Dictionary<string, object> result, FrameScopePresentMonCaptureDiagnosticContext context)
    {
        if (result == null || context == null) return;
        result["PresentMonArgs"] = context.PresentMonArgs ?? "";
        result["PresentMonRuntimeMs"] = context.PresentMonRuntimeMs;
        result["PresentMonStartedAt"] = context.PresentMonStartedAt ?? "";
        result["PresentMonExitedAt"] = context.PresentMonExitedAt ?? "";
        result["PresentMonCsvPostExitWaitMs"] = context.PresentMonCsvPostExitWaitMs;
        result["TargetProcess"] = context.TargetProcessName ?? "";
        result["TargetResolvedProcess"] = context.TargetResolvedProcess ?? "";
        result["TargetPid"] = context.TargetPid;
        result["TargetRunningAtPresentMonExitCheck"] = context.TargetRunningAtPresentMonExitCheck;
        result["TargetPidRunningAtPresentMonExitCheck"] = context.TargetPidRunningAtPresentMonExitCheck;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return "";
    }

    private static long FileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static bool IsCurrentProcessElevated()
    {
        try
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity == null) return false;
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCurrentUserInPerformanceLogUsers()
    {
        try
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity == null) return false;
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            SecurityIdentifier sid = new SecurityIdentifier(PerformanceLogUsersSid);
            return principal.IsInRole(sid) || principal.IsInRole("Performance Log Users");
        }
        catch
        {
            return false;
        }
    }

    private static int CountCsvDataRows(string path, int maxRows)
    {
        try
        {
            int count = 0;
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
            {
                if (reader.ReadLine() == null) return 0;
                while (count < maxRows && reader.ReadLine() != null) count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static string TailText(string path, int maxChars)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !FileExists(path)) return "";
            string text = ReadAllText(path, Encoding.UTF8);
            if (text.Length <= maxChars) return text.Trim();
            return text.Substring(text.Length - maxChars).Trim();
        }
        catch
        {
            return "";
        }
    }

    internal static void WriteAllText(string path, string text, Encoding encoding)
    {
        string directory = GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(ToLongPath(directory));
        using (FileStream stream = OpenFileStream(path, true))
        using (StreamWriter writer = new StreamWriter(stream, encoding ?? Encoding.UTF8))
        {
            writer.Write(text ?? "");
        }
    }

    internal static string ReadAllText(string path, Encoding encoding)
    {
        using (FileStream stream = OpenFileStream(path, false))
        using (StreamReader reader = new StreamReader(stream, encoding ?? Encoding.UTF8, true))
        {
            return reader.ReadToEnd();
        }
    }

    internal static bool FileExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (File.Exists(path)) return true;
        if (Path.DirectorySeparatorChar != '\\') return false;
        try { return GetFileAttributesW(ToLongPath(path)) != InvalidFileAttributes; }
        catch { return false; }
    }

    private static FileStream OpenFileStream(string path, bool write)
    {
        if (Path.DirectorySeparatorChar != '\\' || (GetFullPath(path).Length < 248 && !path.StartsWith(@"\\?\", StringComparison.Ordinal)))
        {
            return new FileStream(path, write ? FileMode.Create : FileMode.Open, write ? FileAccess.Write : FileAccess.Read, write ? FileShare.Read : FileShare.ReadWrite);
        }

        SafeFileHandle handle = CreateFileW(
            ToLongPath(path),
            write ? GenericWrite : GenericRead,
            write ? FileShareRead : (FileShareRead | FileShareWrite),
            IntPtr.Zero,
            write ? CreateAlways : OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new IOException("Unable to open file: " + path, Marshal.GetLastWin32Error());
        }
        return new FileStream(handle, write ? FileAccess.Write : FileAccess.Read);
    }

    private static string GetDirectoryName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        int slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return slash <= 0 ? "" : path.Substring(0, slash);
    }

    private static string ToLongPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (Path.DirectorySeparatorChar != '\\') return path;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        string full = GetFullPath(path);
        if (full.Length < 248) return path;
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + full.Substring(2);
        return @"\\?\" + full;
    }

    private static string GetFullPath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        return Path.GetFullPath(path);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileAttributesW(string fileName);
}
