using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

internal static class FrameScopeHistoryFile
{
    private static readonly object Sync = new object();

    internal static void Append(string path, string jsonLine)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(jsonLine)) return;
        lock (Sync)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(fullPath, jsonLine.TrimEnd('\r', '\n') + Environment.NewLine, new UTF8Encoding(false));
        }
    }

    internal static void Compact(
        string path,
        Func<string, string> resolveRunDirectory,
        Func<string, bool> runExists,
        int maxEntries)
    {
        if (string.IsNullOrWhiteSpace(path) || resolveRunDirectory == null || runExists == null) return;
        lock (Sync)
        {
            if (!File.Exists(path)) return;
            var kept = new List<string>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    string run = resolveRunDirectory(line);
                    if (!string.IsNullOrWhiteSpace(run) && runExists(run)) kept.Add(line);
                }
                catch { }
            }

            int limit = Math.Max(0, maxEntries);
            if (kept.Count > limit) kept = kept.Skip(kept.Count - limit).ToList();
            string text = kept.Count == 0 ? "" : string.Join(Environment.NewLine, kept) + Environment.NewLine;
            FrameScopeJsonFile.Write(path, text);
        }
    }
}
