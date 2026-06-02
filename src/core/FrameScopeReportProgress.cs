using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

public static class FrameScopeReportProgress
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private static readonly TimeSpan ActiveProgressFreshness = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CompletedProgressFreshness = TimeSpan.FromMinutes(30);

    public static Dictionary<string, object> CreateFields(string phase, int percent, string message, DateTime startedAt, string error, bool canRetry)
    {
        int clamped = Math.Max(0, Math.Min(100, percent));
        int eta = 0;
        if (clamped > 0 && clamped < 100)
        {
            double elapsed = Math.Max(0, (DateTime.Now - startedAt).TotalSeconds);
            eta = Math.Max(0, (int)Math.Round(elapsed * (100.0 - clamped) / clamped));
        }

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            { "ReportProgressPhase", phase ?? "" },
            { "ReportProgressPercent", clamped },
            { "ReportProgressMessage", message ?? "" },
            { "ReportProgressEtaSeconds", eta },
            { "ReportProgressError", error },
            { "ReportCanRetry", canRetry },
            { "ReportProgressUpdatedAt", DateTime.Now.ToString("o") }
        };
    }

    public static void Write(string path, string phase, int percent, string message, DateTime startedAt, string error, bool canRetry)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        Dictionary<string, object> fields = CreateFields(phase, percent, message, startedAt, error, canRetry);
        File.WriteAllText(path, Json.Serialize(fields), Encoding.UTF8);
    }

    public static Dictionary<string, object> Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Dictionary<string, object> fields = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            return fields ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void AddTo(Dictionary<string, object> target, Dictionary<string, object> progress)
    {
        if (target == null || progress == null) return;
        foreach (KeyValuePair<string, object> pair in progress)
        {
            target[pair.Key] = pair.Value;
        }
    }

    public static Dictionary<string, object> FindLatestEffectiveStatus(string dataRoot)
    {
        return FindLatestEffectiveStatus(dataRoot, DateTime.Now);
    }

    public static Dictionary<string, object> FindLatestEffectiveStatus(string dataRoot, DateTime now)
    {
        Dictionary<string, object> empty = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(dataRoot)) return empty;

        Dictionary<string, object> best = null;
        string bestStatusPath = "";
        DateTime bestUpdatedAt = DateTime.MinValue;
        try
        {
            FrameScopeDataRootScanStats scanStats = new FrameScopeDataRootScanStats();
            foreach (string statusPath in FrameScopeDataRootScanner.FindStatusFiles(dataRoot, scanStats))
            {
                Dictionary<string, object> fields = Read(statusPath);
                if (fields == null || !fields.ContainsKey("ReportProgressPercent")) continue;

                DateTime updatedAt;
                if (!TryGetProgressUpdatedAt(fields, out updatedAt))
                {
                    updatedAt = File.GetLastWriteTime(statusPath);
                }

                if (!IsEffectiveProgress(fields, updatedAt, now)) continue;
                if (best == null || updatedAt > bestUpdatedAt)
                {
                    best = new Dictionary<string, object>(fields, StringComparer.OrdinalIgnoreCase);
                    bestStatusPath = statusPath;
                    bestUpdatedAt = updatedAt;
                }
            }
        }
        catch
        {
            return empty;
        }

        if (best == null) return empty;
        string runDir = Path.GetDirectoryName(Path.GetFullPath(bestStatusPath)) ?? "";
        best["ReportProgressStatusPath"] = bestStatusPath;
        best["ReportProgressRunDir"] = runDir;
        return best;
    }

    private static bool IsEffectiveProgress(Dictionary<string, object> fields, DateTime updatedAt, DateTime now)
    {
        int percent = ProgressInt(fields, "ReportProgressPercent", -1);
        if (percent < 0) return false;

        TimeSpan age = updatedAt > now ? TimeSpan.Zero : now - updatedAt;
        bool completedOrFailed = percent >= 100 ||
            !string.IsNullOrWhiteSpace(ProgressString(fields, "ReportProgressError", "")) ||
            ProgressBool(fields, "ReportCanRetry", false);
        TimeSpan freshness = completedOrFailed ? CompletedProgressFreshness : ActiveProgressFreshness;
        return age <= freshness;
    }

    private static bool TryGetProgressUpdatedAt(Dictionary<string, object> fields, out DateTime updatedAt)
    {
        updatedAt = DateTime.MinValue;
        string value = ProgressString(fields, "ReportProgressUpdatedAt", "");
        if (string.IsNullOrWhiteSpace(value)) return false;

        DateTimeOffset offset;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out offset))
        {
            updatedAt = offset.LocalDateTime;
            return true;
        }

        DateTime parsed;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
        {
            updatedAt = parsed.Kind == DateTimeKind.Utc ? parsed.ToLocalTime() : parsed;
            return true;
        }

        return false;
    }

    private static string ProgressString(Dictionary<string, object> fields, string key, string fallback)
    {
        object value;
        if (fields != null && fields.TryGetValue(key, out value) && value != null) return Convert.ToString(value);
        return fallback;
    }

    private static int ProgressInt(Dictionary<string, object> fields, string key, int fallback)
    {
        object value;
        if (fields != null && fields.TryGetValue(key, out value) && value != null)
        {
            try { return Convert.ToInt32(value); }
            catch { }
        }
        return fallback;
    }

    private static bool ProgressBool(Dictionary<string, object> fields, string key, bool fallback)
    {
        object value;
        if (fields != null && fields.TryGetValue(key, out value) && value != null)
        {
            try { return Convert.ToBoolean(value); }
            catch { }
        }
        return fallback;
    }
}

public sealed class FrameScopeDataRootScanOptions
{
    public FrameScopeDataRootScanOptions()
    {
        MaxDepth = 6;
        MaxDirectories = 4096;
        MaxFiles = 20000;
        MaxMatches = 5000;
        MaxElapsedMilliseconds = 1500;
        IncludeExpectedRunLayout = true;
    }

    public int MaxDepth { get; set; }
    public int MaxDirectories { get; set; }
    public int MaxFiles { get; set; }
    public int MaxMatches { get; set; }
    public int MaxElapsedMilliseconds { get; set; }
    public bool IncludeExpectedRunLayout { get; set; }
}

public sealed class FrameScopeDataRootScanStats
{
    public int DirectoriesVisited { get; set; }
    public int DirectoriesSkipped { get; set; }
    public int FilesVisited { get; set; }
    public int MatchesFound { get; set; }
    public int EnumerationErrors { get; set; }
    public int ReparseDirectoriesSkipped { get; set; }
    public int DepthLimitHits { get; set; }
    public bool DirectoryLimitHit { get; set; }
    public bool FileLimitHit { get; set; }
    public bool MatchLimitHit { get; set; }
    public bool TimeLimitHit { get; set; }

    private readonly Dictionary<string, int> skipReasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IDictionary<string, int> SkipReasons
    {
        get { return skipReasons; }
    }

    public void RecordSkip(string reason)
    {
        DirectoriesSkipped++;
        if (string.IsNullOrWhiteSpace(reason)) reason = "unknown";
        int count;
        skipReasons.TryGetValue(reason, out count);
        skipReasons[reason] = count + 1;
    }
}

public static class FrameScopeDataRootScanner
{
    private static readonly HashSet<string> SkippedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".hg",
        ".svn",
        ".vs",
        ".vscode",
        ".cache",
        ".pytest_cache",
        "__pycache__",
        "node_modules",
        "dist",
        "build",
        "out",
        "coverage",
        "test-results",
        "playwright-report",
        "cache",
        "tmp",
        "temp",
        "bin",
        "obj"
    };

    public static IEnumerable<string> FindStatusFiles(string dataRoot, FrameScopeDataRootScanStats stats)
    {
        return FindFiles(dataRoot, "status.json", stats, null);
    }

    public static IEnumerable<string> FindReportHtmlFiles(string dataRoot, FrameScopeDataRootScanStats stats)
    {
        return FindFiles(dataRoot, "framescope-interactive-report.html", stats, null);
    }

    public static IEnumerable<string> FindFiles(string dataRoot, string fileName, FrameScopeDataRootScanStats stats, FrameScopeDataRootScanOptions options)
    {
        if (stats == null) stats = new FrameScopeDataRootScanStats();
        if (options == null) options = new FrameScopeDataRootScanOptions();
        List<string> results = new List<string>();
        if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(dataRoot)) return results;

        ScanState state = new ScanState(dataRoot, fileName, stats, options, results);
        if (options.IncludeExpectedRunLayout)
        {
            AddExpectedRunLayoutMatches(state);
        }
        AddGuardedRecursiveMatches(state);
        return results;
    }

    private static void AddExpectedRunLayoutMatches(ScanState state)
    {
        TryAddExpectedRunFile(state, state.Root);
        string[] targetDirs = SafeGetDirectories(state.Root, state.Stats);
        foreach (string targetDir in targetDirs)
        {
            if (ShouldStop(state)) return;
            if (ShouldSkipDirectory(targetDir, state.Stats)) continue;
            if (!TryRecordDirectory(state, targetDir)) return;
            TryAddExpectedRunFile(state, targetDir);

            string[] runDirs = SafeGetDirectories(targetDir, state.Stats);
            foreach (string runDir in runDirs)
            {
                if (ShouldStop(state)) return;
                if (ShouldSkipDirectory(runDir, state.Stats)) continue;
                if (!TryRecordDirectory(state, runDir)) return;
                TryAddExpectedRunFile(state, runDir);
            }
        }
    }

    private static void AddGuardedRecursiveMatches(ScanState state)
    {
        Queue<ScanDirectory> pending = new Queue<ScanDirectory>();
        if (!TryRecordDirectory(state, state.Root)) return;
        pending.Enqueue(new ScanDirectory(state.Root, 0));

        while (pending.Count > 0)
        {
            if (ShouldStop(state)) return;
            ScanDirectory current = pending.Dequeue();
            if (current.Depth > state.Options.MaxDepth)
            {
                state.Stats.DepthLimitHits++;
                continue;
            }

            string[] files = SafeGetFiles(current.Path, state.Stats);
            foreach (string file in files)
            {
                if (ShouldStop(state)) return;
                state.Stats.FilesVisited++;
                if (state.Stats.FilesVisited > state.Options.MaxFiles)
                {
                    state.Stats.FileLimitHit = true;
                    return;
                }
                if (Path.GetFileName(file).Equals(state.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    TryAddMatch(state, file);
                }
            }

            if (current.Depth >= state.Options.MaxDepth)
            {
                state.Stats.DepthLimitHits++;
                continue;
            }

            string[] childDirs = SafeGetDirectories(current.Path, state.Stats);
            foreach (string child in childDirs)
            {
                if (ShouldStop(state)) return;
                if (ShouldSkipDirectory(child, state.Stats)) continue;
                if (!TryRecordDirectory(state, child)) return;
                pending.Enqueue(new ScanDirectory(child, current.Depth + 1));
            }
        }
    }

    private static void TryAddExpectedRunFile(ScanState state, string runDir)
    {
        string candidate = state.FileName.Equals("framescope-interactive-report.html", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(runDir, "charts", "framescope-interactive-report.html")
            : Path.Combine(runDir, state.FileName);
        state.Stats.FilesVisited++;
        if (state.Stats.FilesVisited > state.Options.MaxFiles)
        {
            state.Stats.FileLimitHit = true;
            return;
        }
        try
        {
            if (File.Exists(candidate)) TryAddMatch(state, candidate);
        }
        catch
        {
            state.Stats.EnumerationErrors++;
        }
    }

    private static void TryAddMatch(ScanState state, string path)
    {
        if (ShouldStop(state)) return;
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch
        {
            state.Stats.EnumerationErrors++;
            return;
        }

        if (!state.SeenFiles.Add(fullPath)) return;
        state.Results.Add(fullPath);
        state.Stats.MatchesFound++;
        if (state.Stats.MatchesFound >= state.Options.MaxMatches)
        {
            state.Stats.MatchLimitHit = true;
        }
    }

    private static bool TryRecordDirectory(ScanState state, string directory)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(directory); }
        catch
        {
            state.Stats.EnumerationErrors++;
            return false;
        }

        if (!state.SeenDirectories.Add(fullPath)) return true;
        if (state.Stats.DirectoriesVisited >= state.Options.MaxDirectories)
        {
            state.Stats.DirectoryLimitHit = true;
            return false;
        }
        state.Stats.DirectoriesVisited++;
        return true;
    }

    private static bool ShouldStop(ScanState state)
    {
        if (state.Stats.DirectoryLimitHit || state.Stats.FileLimitHit || state.Stats.MatchLimitHit) return true;
        if (state.Options.MaxElapsedMilliseconds > 0 && state.Stopwatch.ElapsedMilliseconds > state.Options.MaxElapsedMilliseconds)
        {
            state.Stats.TimeLimitHit = true;
            return true;
        }
        return false;
    }

    private static bool ShouldSkipDirectory(string directory, FrameScopeDataRootScanStats stats)
    {
        try
        {
            DirectoryInfo info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                stats.ReparseDirectoriesSkipped++;
                stats.RecordSkip("reparse-point");
                return true;
            }
            if (SkippedDirectoryNames.Contains(info.Name))
            {
                stats.RecordSkip("name:" + info.Name);
                return true;
            }
        }
        catch
        {
            stats.EnumerationErrors++;
            stats.RecordSkip("metadata-error");
            return true;
        }
        return false;
    }

    private static string[] SafeGetDirectories(string directory, FrameScopeDataRootScanStats stats)
    {
        try { return Directory.GetDirectories(directory); }
        catch
        {
            stats.EnumerationErrors++;
            return new string[0];
        }
    }

    private static string[] SafeGetFiles(string directory, FrameScopeDataRootScanStats stats)
    {
        try { return Directory.GetFiles(directory); }
        catch
        {
            stats.EnumerationErrors++;
            return new string[0];
        }
    }

    private sealed class ScanState
    {
        public ScanState(string root, string fileName, FrameScopeDataRootScanStats stats, FrameScopeDataRootScanOptions options, List<string> results)
        {
            Root = Path.GetFullPath(root);
            FileName = fileName;
            Stats = stats;
            Options = options;
            Results = results;
            SeenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SeenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Stopwatch = Stopwatch.StartNew();
        }

        public string Root;
        public string FileName;
        public FrameScopeDataRootScanStats Stats;
        public FrameScopeDataRootScanOptions Options;
        public List<string> Results;
        public HashSet<string> SeenFiles;
        public HashSet<string> SeenDirectories;
        public Stopwatch Stopwatch;
    }

    private sealed class ScanDirectory
    {
        public ScanDirectory(string path, int depth)
        {
            Path = path;
            Depth = depth;
        }

        public string Path;
        public int Depth;
    }
}
