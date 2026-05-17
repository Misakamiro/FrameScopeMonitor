using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

public sealed class FrameScopeLiveRuntimeMonitor
{
    public string Game { get; set; }
    public string ProcessName { get; set; }
    public string RunRoot { get; set; }
    public string RunDir { get; set; }
    public bool ProcessRunning { get; set; }
    public bool HasReadableRun { get; set; }
}

public sealed class FrameScopeLiveRuntimeResult
{
    public bool RefreshEnabled { get; set; }
    public bool HasActiveTarget { get; set; }
    public bool ShouldClearCharts { get; set; }
    public string Game { get; set; }
    public string ProcessName { get; set; }
    public string RunDir { get; set; }
    public string Message { get; set; }
}

public static class FrameScopeVisiblePageRules
{
    public static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "overview";
        if (string.Equals(key, "live", StringComparison.OrdinalIgnoreCase)) return "overview";
        return key;
    }
}

public static class FrameScopeLiveRuntime
{
    public static FrameScopeLiveRuntimeResult Resolve(string activePageKey, IEnumerable<FrameScopeLiveRuntimeMonitor> monitors)
    {
        activePageKey = FrameScopeVisiblePageRules.NormalizeKey(activePageKey);
        FrameScopeLiveRuntimeResult result = new FrameScopeLiveRuntimeResult
        {
            RefreshEnabled = string.Equals(activePageKey, "live", StringComparison.OrdinalIgnoreCase),
            ShouldClearCharts = true,
            Game = "",
            ProcessName = "",
            RunDir = "",
            Message = "实时监控未开启。"
        };

        if (!result.RefreshEnabled) return result;

        List<FrameScopeLiveRuntimeMonitor> list = (monitors ?? Enumerable.Empty<FrameScopeLiveRuntimeMonitor>())
            .Where(item => item != null)
            .ToList();
        if (list.Count == 0)
        {
            result.Message = "未捕获：监测器当前没有活动目标。";
            return result;
        }

        FrameScopeLiveRuntimeMonitor active =
            list.FirstOrDefault(item => item.ProcessRunning && item.HasReadableRun && !string.IsNullOrWhiteSpace(item.RunDir)) ??
            list.FirstOrDefault(item => item.ProcessRunning);
        if (active == null)
        {
            result.Message = "未捕获：目标进程未运行或游戏已退出。";
            return result;
        }

        result.HasActiveTarget = true;
        result.Game = active.Game ?? "";
        result.ProcessName = active.ProcessName ?? "";
        result.RunDir = active.RunDir ?? "";

        if (!active.HasReadableRun || string.IsNullOrWhiteSpace(active.RunDir))
        {
            result.ShouldClearCharts = true;
            result.Message = "等待数据：已找到目标进程，但还没有可读取的实时采样文件。";
            return result;
        }

        result.ShouldClearCharts = false;
        result.Message = "实时监控：读取当前活动目标。";
        return result;
    }
}

public sealed class FrameScopeAddProcessPlan
{
    public bool ShouldStopWatcherFirst { get; set; }
    public bool ShouldAutoRestartWatcher { get; set; }
}

public sealed class FrameScopeReportActionAvailability
{
    public bool CanOpenFolder { get; set; }
    public bool CanOpenReport { get; set; }
    public bool CanExportSupportBundle { get; set; }
    public bool CanOpenDetailedReport { get; set; }
    public bool CanRegenerateReport { get; set; }
    public string Reason { get; set; }
}

public static class FrameScopeReportActionRules
{
    public static FrameScopeReportActionAvailability ResolveAvailability(bool hasSelectedReport, bool reportHtmlExists, bool runDirExists)
    {
        if (!hasSelectedReport)
        {
            return new FrameScopeReportActionAvailability
            {
                CanOpenFolder = false,
                CanOpenReport = false,
                CanExportSupportBundle = false,
                CanOpenDetailedReport = false,
                CanRegenerateReport = false,
                Reason = "请先选择一个报告。"
            };
        }

        return new FrameScopeReportActionAvailability
        {
            CanOpenFolder = reportHtmlExists || runDirExists,
            CanOpenReport = reportHtmlExists,
            CanExportSupportBundle = true,
            CanOpenDetailedReport = runDirExists,
            CanRegenerateReport = runDirExists,
            Reason = reportHtmlExists ? "" : "选中的 HTML 报告文件不存在。"
        };
    }
}

public static class FrameScopeTargetEditRules
{
    public static bool TryParseSampleInterval(string value, out int sampleMs, out string error)
    {
        sampleMs = 0;
        error = "";
        if (!int.TryParse((value ?? "").Trim(), out sampleMs))
        {
            error = "采样率必须是数字，单位 ms。";
            return false;
        }
        if (sampleMs < 50)
        {
            error = "采样率不能低于 50 ms。";
            return false;
        }
        return true;
    }

    public static string NormalizeProcessName(string value)
    {
        string[] parts = (value ?? "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        return string.Join(";", parts);
    }

    public static string NormalizeSingleProcessForAdd(string value)
    {
        string normalized = NormalizeProcessName(value);
        if (string.IsNullOrWhiteSpace(normalized)) return "";
        if (normalized.IndexOf(';') < 0 && !normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".exe";
        }
        return normalized;
    }

    public static FrameScopeAddProcessPlan PlanAddProcess(bool watcherRunning)
    {
        return new FrameScopeAddProcessPlan
        {
            ShouldStopWatcherFirst = watcherRunning,
            ShouldAutoRestartWatcher = false
        };
    }
}

public sealed class FrameScopeProcessPickerItem
{
    public string ProcessName { get; set; }
    public int ProcessId { get; set; }
    public string WindowTitle { get; set; }
    public string Path { get; set; }

    public string DisplayText
    {
        get
        {
            string title = string.IsNullOrWhiteSpace(WindowTitle) ? "无窗口标题" : WindowTitle.Trim();
            return FrameScopeProcessPicker.FormatDisplayText(ProcessName, WindowTitle);
        }
    }

    public override string ToString()
    {
        return DisplayText;
    }
}

public static class FrameScopeProcessPicker
{
    public const string SortRecent = "\u6700\u8fd1\u4f7f\u7528";
    public const string SortName = "\u6309\u540d\u79f0";
    public const string SortProcessName = "\u6309\u8fdb\u7a0b\u540d";

    public static string FormatDisplayText(string processName, string windowTitle)
    {
        processName = (processName ?? "").Trim();
        windowTitle = (windowTitle ?? "").Trim();
        if (string.IsNullOrWhiteSpace(windowTitle)) return processName;
        if (string.Equals(windowTitle, processName, StringComparison.OrdinalIgnoreCase)) return processName;
        if (string.IsNullOrWhiteSpace(processName)) return windowTitle;
        return windowTitle + " (" + processName + ")";
    }

    public static List<FrameScopeProcessPickerItem> FilterAndSortItems(IEnumerable<FrameScopeProcessPickerItem> source, string query, string sortMode)
    {
        string normalizedQuery = (query ?? "").Trim();
        IEnumerable<FrameScopeProcessPickerItem> items = (source ?? Enumerable.Empty<FrameScopeProcessPickerItem>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ProcessName));

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            items = items.Where(item => MatchesSearch(item, normalizedQuery));
        }

        if (string.Equals(sortMode, SortName, StringComparison.OrdinalIgnoreCase))
        {
            items = items
                .OrderBy(item => FormatDisplayText(item.ProcessName, item.WindowTitle), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProcessId);
        }
        else if (string.Equals(sortMode, SortProcessName, StringComparison.OrdinalIgnoreCase))
        {
            items = items
                .OrderBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => FormatDisplayText(item.ProcessName, item.WindowTitle), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProcessId);
        }
        else
        {
            items = items
                .OrderByDescending(item => string.IsNullOrWhiteSpace(item.WindowTitle) ? 0 : 1)
                .ThenBy(item => FormatDisplayText(item.ProcessName, item.WindowTitle), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProcessId);
        }

        return items.ToList();
    }

    public static bool MatchesSearch(FrameScopeProcessPickerItem item, string query)
    {
        if (item == null) return false;
        query = (query ?? "").Trim();
        if (string.IsNullOrWhiteSpace(query)) return true;
        return ContainsIgnoreCase(item.ProcessName, query) ||
            ContainsIgnoreCase(item.WindowTitle, query) ||
            ContainsIgnoreCase(FormatDisplayText(item.ProcessName, item.WindowTitle), query);
    }

    private static bool ContainsIgnoreCase(string value, string query)
    {
        return (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static List<FrameScopeProcessPickerItem> EnumerateRunningProcesses()
    {
        return EnumerateRunningProcesses(false);
    }

    public static List<FrameScopeProcessPickerItem> EnumerateRunningProcesses(bool includeProcessPath)
    {
        List<FrameScopeProcessPickerItem> items = new List<FrameScopeProcessPickerItem>();
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string name = process.ProcessName;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
                items.Add(new FrameScopeProcessPickerItem
                {
                    ProcessName = name,
                    ProcessId = process.Id,
                    WindowTitle = SafeMainWindowTitle(process),
                    Path = includeProcessPath ? SafeProcessPath(process) : ""
                });
            }
            catch
            {
            }
            finally
            {
                try { process.Dispose(); }
                catch { }
            }
        }

        return items
            .GroupBy(item => item.ProcessName + "|" + item.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ProcessId)
            .ToList();
    }

    private static string SafeMainWindowTitle(Process process)
    {
        try { return process.MainWindowTitle ?? ""; }
        catch { return ""; }
    }

    private static string SafeProcessPath(Process process)
    {
        try { return process.MainModule == null ? "" : process.MainModule.FileName; }
        catch { return ""; }
    }
}
