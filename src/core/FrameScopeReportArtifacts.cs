using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class FrameScopeReportArtifactState
{
    internal string RunDirectory = "";
    internal string HtmlPath = "";
    internal string DataPath = "";
    internal string ManifestPath = "";
    internal bool HtmlExists;
    internal bool DataExists;
    internal bool ManifestValid;
    internal bool PathsMatchRun;
    internal string ManifestInputFingerprint = "";
    internal string CurrentInputFingerprint = "";
    internal bool InputFingerprintMatches;
    internal int FingerprintVersion;
    internal string ArtifactStatus = "incomplete";
    internal bool LegacyReadable;
    internal string ReportKind = "error";
    internal double? DurationSeconds;
    internal string Error = "";

    internal bool IsReadable { get { return IsComplete || ArtifactStatus == "legacy-readable"; } }

    internal bool IsComplete
    {
        get { return HtmlExists && DataExists && ManifestValid && PathsMatchRun && InputFingerprintMatches && !LegacyReadable; }
    }
}

internal sealed class FrameScopeReportInputFingerprint
{
    internal string Value = "";
    internal bool Stable;
}

internal static class FrameScopeReportArtifacts
{
    internal const string ReportFileName = "framescope-interactive-report.html";
    internal const string DataFileName = "framescope-interactive-data.js";
    internal const string ManifestFileName = "framescope-interactive-manifest.json";
    internal const string InputFingerprintFieldName = "inputFingerprint";

    private static readonly string[] MonitorCsvNames =
    {
        "presentmon.csv",
        "process-samples.csv",
        "system-samples.csv",
        "hardware-samples.csv",
        "cpu-core-samples.csv",
        "cpu-voltage-samples.csv",
        "cpu-vid-samples.csv"
    };

    internal static FrameScopeReportArtifactState Inspect(string runDirectory)
    {
        return InspectDirectory(runDirectory, null);
    }

    internal static FrameScopeReportArtifactState InspectGeneratedDirectory(string runDirectory, string generatedDirectory)
    {
        return InspectDirectory(runDirectory, generatedDirectory);
    }

    private static FrameScopeReportArtifactState InspectDirectory(string runDirectory, string generatedDirectory)
    {
        var state = new FrameScopeReportArtifactState();
        try
        {
            string run = Path.GetFullPath(runDirectory ?? "");
            string finalCharts = Path.Combine(run, "charts");
            string actualCharts = string.IsNullOrWhiteSpace(generatedDirectory)
                ? finalCharts
                : Path.GetFullPath(generatedDirectory);
            state.RunDirectory = run;
            state.DurationSeconds = ReadDuration(run, null);
            state.HtmlPath = Path.Combine(finalCharts, ReportFileName);
            state.DataPath = Path.Combine(finalCharts, DataFileName);
            state.ManifestPath = Path.Combine(actualCharts, ManifestFileName);
            string actualHtmlPath = Path.Combine(actualCharts, ReportFileName);
            string actualDataPath = Path.Combine(actualCharts, DataFileName);
            state.HtmlExists = File.Exists(actualHtmlPath);
            state.DataExists = File.Exists(actualDataPath);

            if (!state.HtmlExists)
            {
                state.Error = "Report HTML is missing.";
                return state;
            }
            if (!state.DataExists)
            {
                state.Error = "Report data.js is missing.";
                return state;
            }
            if (!File.Exists(state.ManifestPath))
            {
                state.Error = "Report manifest is missing.";
                return state;
            }

            Dictionary<string, object> manifest;
            try
            {
                manifest = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(state.ManifestPath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                state.Error = "Report manifest is invalid: " + ex.Message;
                return state;
            }

            state.DurationSeconds = ReadDuration(run, manifest);
            string kind = ReadString(manifest, "reportKind");
            if (kind == "full" || kind == "partial" || kind == "diagnostic" || kind == "error") state.ReportKind = kind;
            else state.ReportKind = ReadInt(manifest, "frames") > 0 ? "partial" : (HasUsableMonitorData(run) ? "diagnostic" : "error");
            string manifestReport = ReadString(manifest, "report");
            string manifestData = ReadString(manifest, "data");
            state.ManifestValid = manifest != null &&
                !string.IsNullOrWhiteSpace(manifestReport) &&
                !string.IsNullOrWhiteSpace(manifestData);
            if (!state.ManifestValid)
            {
                state.Error = "Report manifest does not identify the report and data artifacts.";
                return state;
            }

            string canonicalReport = ResolveManifestPath(run, manifestReport);
            string canonicalData = ResolveManifestPath(run, manifestData);
            state.PathsMatchRun = IsPathInside(canonicalReport, run) &&
                IsPathInside(canonicalData, run) &&
                PathEquals(canonicalReport, state.HtmlPath) &&
                PathEquals(canonicalData, state.DataPath);
            if (!state.PathsMatchRun)
            {
                state.Error = "Report manifest paths do not match the supplied run.";
                return state;
            }

            state.FingerprintVersion = ReadInt(manifest, "inputFingerprintVersion");
            if (state.FingerprintVersion > 2 || state.FingerprintVersion < 0) { state.Error = "Unsupported input fingerprint version."; return state; }
            state.ManifestInputFingerprint = ReadString(manifest, InputFingerprintFieldName);
            bool hasFingerprint = !string.IsNullOrWhiteSpace(state.ManifestInputFingerprint);
            int inspectVersion = state.FingerprintVersion <= 1 ? 1 : 2;
            FrameScopeReportInputFingerprint currentFingerprint = CaptureInputFingerprint(run, inspectVersion);
            state.CurrentInputFingerprint = currentFingerprint.Value;
            state.InputFingerprintMatches = currentFingerprint.Stable && hasFingerprint &&
                string.Equals(state.ManifestInputFingerprint, currentFingerprint.Value, StringComparison.OrdinalIgnoreCase);
            state.LegacyReadable = state.FingerprintVersion <= 1;
            if (state.InputFingerprintMatches && !state.LegacyReadable) state.ArtifactStatus = "complete";
            else if (state.LegacyReadable && currentFingerprint.Stable && (state.InputFingerprintMatches || !hasFingerprint))
            {
                state.ArtifactStatus = "legacy-readable";
                state.Error = "Report is readable, but its input fingerprint does not cover the current versioned CSV contract.";
                return state;
            }
            else if (!currentFingerprint.Stable || (hasFingerprint && !state.InputFingerprintMatches))
            {
                state.ArtifactStatus = "inputs-changed";
                state.Error = "Report input fingerprint does not match the current monitor CSV inputs.";
                return state;
            }

            if (!hasFingerprint && !state.LegacyReadable) state.Error = "Report input fingerprint is missing.";
            return state;
        }
        catch (Exception ex)
        {
            state.Error = "Report artifacts could not be inspected: " + ex.Message;
            return state;
        }
    }

    internal static FrameScopeReportInputFingerprint CaptureInputFingerprint(string runDirectory)
    {
        return CaptureInputFingerprint(runDirectory, 2);
    }

    internal static FrameScopeReportInputFingerprint CaptureInputFingerprint(string runDirectory, int version)
    {
        string run = Path.GetFullPath(runDirectory ?? "");
        string first = BuildInputSnapshot(run, version);
        string second = BuildInputSnapshot(run, version);
        return new FrameScopeReportInputFingerprint
        {
            Value = HashSnapshot(second),
            Stable = first.IndexOf("|error|", StringComparison.Ordinal) < 0 && string.Equals(first, second, StringComparison.Ordinal)
        };
    }

    internal static bool HasUsableMonitorData(string runDirectory)
    {
        if (string.IsNullOrWhiteSpace(runDirectory)) return false;
        foreach (string name in MonitorCsvNames)
        {
            if (HasCsvDataRow(Path.Combine(runDirectory, name))) return true;
        }
        return false;
    }

    internal static bool HasCsvDataRow(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                string header = reader.ReadLine();
                if (header == null) return false;
                List<string> headers = ParseCsvFields(header);
                int timeIndex = headers.FindIndex(h => h.Equals("Time", StringComparison.OrdinalIgnoreCase) || h.Equals("TimeInDateTime", StringComparison.OrdinalIgnoreCase) || h.Equals("TimeInSeconds", StringComparison.OrdinalIgnoreCase));
                if (timeIndex < 0) return false;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    List<string> fields = ParseCsvFields(line);
                    DateTime time;
                    if (fields.Count == headers.Count && DateTime.TryParse(fields[timeIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private static string ReadString(Dictionary<string, object> map, string key)
    {
        object value;
        return map != null && map.TryGetValue(key, out value) && value != null
            ? Convert.ToString(value)
            : "";
    }

    private static List<string> ParseCsvFields(string line)
    {
        var fields = new List<string>();
        var text = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { text.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { fields.Add(text.ToString().Trim()); text.Length = 0; }
            else text.Append(c);
        }
        if (quoted) return new List<string>();
        fields.Add(text.ToString().Trim());
        return fields;
    }

    private static string BuildInputSnapshot(string runDirectory, int version)
    {
        StringBuilder value = new StringBuilder();
        foreach (string name in MonitorCsvNames)
        {
            string path = Path.Combine(runDirectory, name);
            if (version <= 1 && name.StartsWith("cpu-", StringComparison.OrdinalIgnoreCase)) continue;
            // Preserve fingerprints of legacy runs which never recorded hardware samples.
            if (version <= 1 && name == "hardware-samples.csv" && !File.Exists(path)) continue;
            value.Append(name).Append('|');
            try
            {
                FileInfo file = new FileInfo(path);
                file.Refresh();
                if (!file.Exists)
                {
                    value.Append("missing");
                }
                else
                {
                    value.Append(file.Length.ToString(CultureInfo.InvariantCulture))
                        .Append('|')
                        .Append(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));                    if (version >= 2)
                    {
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (SHA256 hash = SHA256.Create())
                            value.Append('|').Append(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant());
                    }
                }
            }
            catch (Exception ex)
            {
                value.Append("error|").Append(ex.GetType().FullName);
            }
            value.Append('\n');
        }
        return value.ToString();
    }

    internal static double? ReadDuration(string run, Dictionary<string, object> manifest)
    {
        object raw;
        double seconds;
        if (manifest != null && manifest.TryGetValue("durationSeconds", out raw) && raw != null &&
            double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) &&
            !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds > 0) return seconds;
        foreach (string name in new[] { "summary.json", "status.json" })
        {
            try
            {
                var map = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(run, name), Encoding.UTF8));
                DateTime start, end;
                if (DateTime.TryParse(ReadString(map, "StartTime"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start) &&
                    DateTime.TryParse(ReadString(map, "EndTime"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out end) && end.ToUniversalTime() > start.ToUniversalTime())
                    return (end.ToUniversalTime() - start.ToUniversalTime()).TotalSeconds;
            }
            catch (IOException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        DateTime? first = null, last = null;
        foreach (string name in MonitorCsvNames)
        {
            string path = Path.Combine(run, name);
            if (!File.Exists(path)) continue;
            try
            {
                using (var reader = new StreamReader(path, Encoding.UTF8, true))
                {
                    string header = reader.ReadLine();
                    if (header == null) continue;
                    List<string> headers = ParseCsvFields(header);
                    int index = headers.FindIndex(h => h.Equals("Time", StringComparison.OrdinalIgnoreCase) || h.Equals("TimeInDateTime", StringComparison.OrdinalIgnoreCase) || h.Equals("TimeInSeconds", StringComparison.OrdinalIgnoreCase));
                    if (index < 0) continue;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        List<string> values = ParseCsvFields(line);
                        DateTime time;
                        if (values.Count != headers.Count || !DateTime.TryParse(values[index], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) continue;
                        time = time.ToUniversalTime();
                        if (!first.HasValue || time < first.Value) first = time;
                        if (!last.HasValue || time > last.Value) last = time;
                    }
                }
            }
            catch (IOException) { }
        }
        return first.HasValue && last.HasValue && last.Value > first.Value ? (double?)(last.Value - first.Value).TotalSeconds : null;
    }

    private static int ReadInt(Dictionary<string, object> map, string key)
    {
        object value;
        int parsed;
        return map != null && map.TryGetValue(key, out value) && value != null && Int32.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
    }

    private static string HashSnapshot(string snapshot)
    {
        using (SHA256 hash = SHA256.Create())
        {
            byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(snapshot ?? ""));
            StringBuilder text = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }

    private static string ResolveManifestPath(string runDirectory, string value)
    {
        string path = value ?? "";
        if (!Path.IsPathRooted(path)) path = Path.Combine(runDirectory, path);
        return Path.GetFullPath(path);
    }

    private static bool IsPathInside(string path, string root)
    {
        string fullPath = Path.GetFullPath(path ?? "");
        string fullRoot = Path.GetFullPath(root ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathEquals(string left, string right)
    {
        return Path.GetFullPath(left ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }
}
