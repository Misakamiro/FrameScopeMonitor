using System;
using System.IO;
using System.Linq;

internal static partial class FrameScopeReportGenerator
{
    private static string GetArgValue(string[] args, string name, string fallback)
    {
        if (args == null) return fallback;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return fallback;
    }

    private static string FindLatestRun(string baseDir)
    {
        string runs = Path.Combine(baseDir, "cs2-monitor-runs");
        if (!Directory.Exists(runs)) throw new DirectoryNotFoundException(runs);
        DirectoryInfo latest = new DirectoryInfo(runs).GetDirectories()
            .Where(d => File.Exists(Path.Combine(d.FullName, "presentmon.csv")))
            .OrderByDescending(d => d.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latest == null) throw new FileNotFoundException("No monitor runs found in " + runs);
        return latest.FullName;
    }
}
