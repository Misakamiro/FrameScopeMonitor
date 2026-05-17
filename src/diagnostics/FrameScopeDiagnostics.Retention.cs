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
    public static FrameScopeDiagnosticCleanupResult CleanupDiagnosticReports(string directory, int retentionDays, int maxMegabytes)
    {
        FrameScopeDiagnosticCleanupResult result = new FrameScopeDiagnosticCleanupResult();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return result;
        if (retentionDays <= 0) retentionDays = 14;
        if (maxMegabytes <= 0) maxMegabytes = 100;
        DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        foreach (string file in SafeEnumerateFiles(directory))
        {
            try
            {
                FileInfo info = new FileInfo(file);
                if (info.LastWriteTimeUtc < cutoff)
                {
                    long bytes = info.Length;
                    info.Delete();
                    result.FilesDeleted++;
                    result.BytesDeleted += bytes;
                }
            }
            catch { }
        }

        DeleteEmptyDirectories(directory);
        long limit = (long)maxMegabytes * 1024L * 1024L;
        List<FileInfo> files = SafeEnumerateFiles(directory)
            .Select(path =>
            {
                try { return new FileInfo(path); }
                catch { return null; }
            })
            .Where(file => file != null && file.Exists)
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        long total = files.Sum(file => file.Length);
        foreach (FileInfo file in files)
        {
            if (total <= limit) break;
            try
            {
                long bytes = file.Length;
                file.Delete();
                total -= bytes;
                result.FilesDeleted++;
                result.BytesDeleted += bytes;
            }
            catch { }
        }
        DeleteEmptyDirectories(directory);
        return result;
    }

    private static void DeleteEmptyDirectories(string directory)
    {
        try
        {
            foreach (string child in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            {
                try
                {
                    if (Directory.GetFileSystemEntries(child).Length == 0) Directory.Delete(child, false);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void TrimLogFile(string logPath, int maxMegabytes)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath)) return;
            long maxBytes = Math.Max(256L * 1024L, (long)Math.Max(1, maxMegabytes) * 1024L * 1024L / 4L);
            FileInfo info = new FileInfo(logPath);
            if (info.Length <= maxBytes) return;
            string[] lines = File.ReadAllLines(logPath, Encoding.UTF8);
            List<string> kept = new List<string>();
            long bytes = 0;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                bytes += Encoding.UTF8.GetByteCount(lines[i]) + 2;
                if (bytes > maxBytes) break;
                kept.Add(lines[i]);
            }
            kept.Reverse();
            File.WriteAllLines(logPath, kept.ToArray(), Encoding.UTF8);
        }
        catch { }
    }
}
