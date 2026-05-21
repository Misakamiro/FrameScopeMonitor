using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public sealed class FrameScopeProcessPickerItem
{
    public string ProcessName { get; set; }
    public int ProcessId { get; set; }
    public string WindowTitle { get; set; }
    public string Path { get; set; }

    public string DisplayText
    {
        get { return FrameScopeProcessPicker.FormatDisplayText(ProcessName, WindowTitle); }
    }

    public override string ToString()
    {
        return DisplayText;
    }
}

public static class FrameScopeProcessPicker
{
    public const string SortRecent = "最近使用";
    public const string SortName = "按名称";
    public const string SortProcessName = "按进程名";

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

    private static bool ContainsIgnoreCase(string value, string query)
    {
        return (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
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
