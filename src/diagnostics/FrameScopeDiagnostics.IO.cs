using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public static partial class FrameScopeDiagnostics
{
    private static string ResolveRoot(string appRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return FrameScopeConfigStore.DefaultDataRoot;
        if (Path.IsPathRooted(path)) return path;
        return Path.Combine(appRoot, path);
    }

    private static string FindLatestRun(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(dataRoot)) return "";
        try
        {
            FrameScopeDataRootScanStats scanStats = new FrameScopeDataRootScanStats();
            return FrameScopeDataRootScanner.FindStatusFiles(dataRoot, scanStats)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.DirectoryName)
                .FirstOrDefault() ?? "";
        }
        catch { return ""; }
    }

    private static Dictionary<string, object> LoadJsonMap(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Dictionary<string, object> map = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            return map ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); }
    }

    private static Dictionary<string, object> GetMap(Dictionary<string, object> map, string key)
    {
        if (map == null || !map.ContainsKey(key) || map[key] == null) return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, object> nested = map[key] as Dictionary<string, object>;
        if (nested != null) return nested;
        IDictionary<string, object> generic = map[key] as IDictionary<string, object>;
        if (generic != null) return new Dictionary<string, object>(generic, StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    private static object GetObject(Dictionary<string, object> map, string key)
    {
        if (map == null || !map.ContainsKey(key)) return null;
        return map[key];
    }

    private static string GetString(Dictionary<string, object> map, string key)
    {
        object value = GetObject(map, key);
        return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static object FirstNonNull(params object[] values)
    {
        foreach (object value in values)
        {
            if (value != null) return value;
        }
        return null;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    private static long FileSize(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            return new FileInfo(path).Length;
        }
        catch { return 0; }
    }

    private static IEnumerable<Process> SafeGetProcesses(string baseName)
    {
        try { return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(baseName)); }
        catch { return new Process[0]; }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory)
    {
        try { return Directory.GetFiles(directory, "*", SearchOption.AllDirectories); }
        catch { return new string[0]; }
    }

    private static void AddFilteredTail(List<string> errors, string path)
    {
        if (errors == null || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            IEnumerable<string> lines = File.ReadLines(path, Encoding.UTF8)
                .Where(line => line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("no-frame", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("no-presentmon", StringComparison.OrdinalIgnoreCase) >= 0)
                .Reverse()
                .Take(40)
                .Reverse();
            foreach (string line in lines)
            {
                errors.Add(RedactForPrivacy(line));
            }
        }
        catch { }
    }
}
