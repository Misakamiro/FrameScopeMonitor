using System;
using System.Collections.Generic;
using System.Globalization;
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
            foreach (string statusPath in Directory.GetFiles(dataRoot, "status.json", SearchOption.AllDirectories))
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
