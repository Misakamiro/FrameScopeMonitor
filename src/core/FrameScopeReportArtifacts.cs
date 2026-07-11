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
    internal string Error = "";

    internal bool IsComplete
    {
        get { return HtmlExists && DataExists && ManifestValid && PathsMatchRun && InputFingerprintMatches; }
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
        "system-samples.csv"
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

            state.ManifestInputFingerprint = ReadString(manifest, InputFingerprintFieldName);
            FrameScopeReportInputFingerprint currentFingerprint = CaptureInputFingerprint(run);
            state.CurrentInputFingerprint = currentFingerprint.Value;
            state.InputFingerprintMatches = currentFingerprint.Stable &&
                !string.IsNullOrWhiteSpace(state.ManifestInputFingerprint) &&
                string.Equals(state.ManifestInputFingerprint, currentFingerprint.Value, StringComparison.OrdinalIgnoreCase);
            if (!state.InputFingerprintMatches)
            {
                state.Error = "Report input fingerprint does not match the current monitor CSV inputs.";
                return state;
            }

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
        string run = Path.GetFullPath(runDirectory ?? "");
        string first = BuildInputSnapshot(run);
        string second = BuildInputSnapshot(run);
        return new FrameScopeReportInputFingerprint
        {
            Value = HashSnapshot(second),
            Stable = string.Equals(first, second, StringComparison.Ordinal)
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
                bool headerSeen = false;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!headerSeen)
                    {
                        headerSeen = true;
                        continue;
                    }
                    return true;
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

    private static string BuildInputSnapshot(string runDirectory)
    {
        StringBuilder value = new StringBuilder();
        foreach (string name in MonitorCsvNames)
        {
            string path = Path.Combine(runDirectory, name);
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
                        .Append(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
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
