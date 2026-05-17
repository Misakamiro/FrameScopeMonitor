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
    private static string ResolvePresentMonPath(string requestedPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedPath)) candidates.Add(requestedPath);
        candidates.Add(Path.Combine(Root, "tools", "PresentMon-2.4.1-x64.exe"));
        try
        {
            var toolsDir = Path.Combine(Root, "tools");
            if (Directory.Exists(toolsDir))
            {
                candidates.AddRange(Directory.GetFiles(toolsDir, "PresentMon*.exe").OrderBy(path => path));
            }
        }
        catch { }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "FrameViewSDK", "bin", "PresentMon_x64.exe"));
        return FirstExistingPath(candidates);
    }

    private static string ResolveProcessSamplerPath(string requestedPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedPath)) candidates.Add(requestedPath);
        candidates.Add(Path.Combine(Root, "FrameScopeProcessSampler.exe"));
        return FirstExistingPath(candidates);
    }

    private static string ResolveSystemSamplerPath(string requestedPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedPath)) candidates.Add(requestedPath);
        candidates.Add(Path.Combine(Root, "FrameScopeSystemSampler.exe"));
        return FirstExistingPath(candidates);
    }

    private static string ResolveNvidiaSmiPath()
    {
        var known = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        if (File.Exists(known)) return known;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            try
            {
                var candidate = Path.Combine(part.Trim(), "nvidia-smi.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return "";
    }

    private static string FirstExistingPath(IEnumerable<string> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                var full = Path.GetFullPath(candidate);
                if (!seen.Add(full)) continue;
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return "";
    }
}
