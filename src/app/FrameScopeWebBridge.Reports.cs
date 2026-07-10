using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

internal sealed partial class FrameScopeWebBridge
{
    private string OpenReport(FrameScopeWebBridgeRequest request)
    {
        if (PayloadContainsPathAuthority(request.Payload))
        {
            return ErrorResponse(request.RequestId, "path_not_allowed", "reports.open accepts only a host-generated reportId.");
        }

        FrameScopeWebReportEntry entry;
        string error = ResolveReportFromRequest(request, true, out entry);
        if (!string.IsNullOrWhiteSpace(error)) return ErrorResponse(request.RequestId, "report_not_found", error);
        if (options.HostAdapter == null) return ErrorResponse(request.RequestId, "host_adapter_missing", "reports.open is not safely connected to the C# host.");

        FrameScopeWebBridgeHostResult result = options.HostAdapter.OpenReport(BuildHostContext(), entry.ReportHtml, entry.RunDir);
        if (!result.Ok) return ErrorResponse(request.RequestId, result.Code, result.Message);

        Dictionary<string, object> payload = BuildReportActionPayload(entry, result);
        payload["status"] = "opened";
        PublishEvent("event.reportsChanged", payload);
        return OkResponse(request.RequestId, payload);
    }

    private string OpenReportDirectory(FrameScopeWebBridgeRequest request)
    {
        if (PayloadContainsPathAuthority(request.Payload))
        {
            return ErrorResponse(request.RequestId, "path_not_allowed", "reports.openDirectory accepts only a host-generated reportId.");
        }

        FrameScopeWebReportEntry entry;
        string error = ResolveReportFromRequest(request, false, out entry);
        if (!string.IsNullOrWhiteSpace(error)) return ErrorResponse(request.RequestId, "report_not_found", error);
        if (options.HostAdapter == null) return ErrorResponse(request.RequestId, "host_adapter_missing", "reports.openDirectory is not safely connected to the C# host.");

        FrameScopeWebBridgeHostResult result = options.HostAdapter.OpenDirectory(BuildHostContext(), entry.RunDir);
        if (!result.Ok) return ErrorResponse(request.RequestId, result.Code, result.Message);

        Dictionary<string, object> payload = BuildReportActionPayload(entry, result);
        payload["status"] = "directory_opened";
        PublishEvent("event.reportsChanged", payload);
        return OkResponse(request.RequestId, payload);
    }

    private string OpenLogsDirectory(FrameScopeWebBridgeRequest request)
    {
        if (PayloadContainsPathAuthority(request.Payload))
        {
            return ErrorResponse(request.RequestId, "path_not_allowed", "logs.openDirectory does not accept frontend paths.");
        }

        if (options.HostAdapter == null) return ErrorResponse(request.RequestId, "host_adapter_missing", "logs.openDirectory is not safely connected to the C# host.");

        FrameScopeWebBridgeHostContext context = BuildHostContext();
        FrameScopeWebBridgeHostResult result = options.HostAdapter.OpenLogsDirectory(context);
        if (!result.Ok) return ErrorResponse(request.RequestId, result.Code, result.Message);

        Dictionary<string, object> payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            { "status", "directory_opened" },
            { "message", result.Message ?? "" },
            { "directory", context.LogDirectory },
            { "logFile", Path.Combine(context.LogDirectory, "framescope-watcher.log") }
        };
        if (result.Payload != null)
        {
            foreach (KeyValuePair<string, object> pair in result.Payload)
            {
                payload[pair.Key] = pair.Value;
            }
        }
        PublishEvent("event.status", new Dictionary<string, object>
        {
            { "requestId", request.RequestId },
            { "status", "logs.directoryOpened" },
            { "action", "logs.openDirectory" },
            { "message", result.Message ?? "" },
            { "directory", payload["directory"] }
        });
        return OkResponse(request.RequestId, payload);
    }

    private string RegenerateReport(FrameScopeWebBridgeRequest request)
    {
        if (PayloadContainsPathAuthority(request.Payload))
        {
            return ErrorResponse(request.RequestId, "path_not_allowed", "reports.regenerate accepts only a host-generated reportId.");
        }

        FrameScopeWebReportEntry entry;
        string error = ResolveReportFromRequest(request, false, out entry);
        if (!string.IsNullOrWhiteSpace(error)) return ErrorResponse(request.RequestId, "report_not_found", error);
        if (!HasMonitorCsv(entry.RunDir)) return ErrorResponse(request.RequestId, "missing_monitor_data", "The selected run has no monitor CSV data to regenerate.");

        return StartHostAction(
            request,
            "reports.regenerate",
            "report.regenerating",
            "report.in_flight",
            "event.reportProgress",
            delegate { return Interlocked.CompareExchange(ref reportRegenerateInFlight, 1, 0) == 0; },
            delegate { Interlocked.Exchange(ref reportRegenerateInFlight, 0); },
            delegate(FrameScopeWebBridgeHostContext context)
            {
                FrameScopeWebBridgeHostResult result = RequireHostAdapter().RegenerateReport(context, entry.RunDir);
                if (result.Payload == null) result.Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                result.Payload["reportId"] = entry.ReportId;
                result.Payload["runDir"] = entry.RunDir;
                result.Payload["reportHtml"] = entry.ReportHtml;
                return result;
            });
    }

    private Dictionary<string, object> BuildReportsListPayload()
    {
        List<FrameScopeWebReportEntry> reports = LoadReports();
        List<Dictionary<string, object>> payload = new List<Dictionary<string, object>>();
        foreach (FrameScopeWebReportEntry entry in reports)
        {
            payload.Add(entry.ToPayload());
        }

        return new Dictionary<string, object>
        {
            { "status", "loaded" },
            { "historyPath", options.HistoryPath },
            { "dataRoot", ResolveCurrentConfigDataRoot() },
            { "count", payload.Count },
            { "reports", payload },
            { "loadedAt", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) }
        };
    }

    private List<FrameScopeWebReportEntry> LoadReports()
    {
        string dataRoot = ResolveCurrentConfigDataRoot();
        List<FrameScopeWebReportEntry> reports = new List<FrameScopeWebReportEntry>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Dictionary<string, object>> statusCache = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(options.HistoryPath))
        {
            foreach (string line in File.ReadLines(options.HistoryPath).Reverse().Take(100))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    Dictionary<string, object> map = json.Deserialize<Dictionary<string, object>>(line);
                    FrameScopeWebReportEntry entry = BuildReportEntryFromHistory(map, dataRoot, statusCache);
                    AddReportIfValid(reports, seen, entry);
                }
                catch
                {
                }
            }
        }

        if (Directory.Exists(dataRoot))
        {
            try
            {
                FrameScopeDataRootScanStats scanStats = new FrameScopeDataRootScanStats();
                foreach (string reportHtml in FrameScopeDataRootScanner.FindReportHtmlFiles(dataRoot, scanStats)
                    .OrderByDescending(SafeLastWriteTimeUtc)
                    .Take(50))
                {
                    FrameScopeWebReportEntry entry = BuildReportEntryFromReportHtml(reportHtml, dataRoot, statusCache);
                    AddReportIfValid(reports, seen, entry);
                }
            }
            catch
            {
            }
        }

        return reports
            .OrderByDescending(delegate(FrameScopeWebReportEntry entry) { return entry.SortTimeUtc; })
            .Take(50)
            .ToList();
    }

    private void AddReportIfValid(List<FrameScopeWebReportEntry> reports, HashSet<string> seen, FrameScopeWebReportEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.ReportId)) return;
        if (seen.Add(entry.ReportId)) reports.Add(entry);
    }

    private FrameScopeWebReportEntry BuildReportEntryFromHistory(Dictionary<string, object> map, string dataRoot, Dictionary<string, Dictionary<string, object>> statusCache)
    {
        if (map == null) return null;
        string runDir = ReadString(map, "RunDir");
        string reportHtml = ReadString(map, "ReportHtml");
        if (string.IsNullOrWhiteSpace(runDir) && !string.IsNullOrWhiteSpace(reportHtml))
        {
            runDir = GuessRunDirFromReportHtml(reportHtml);
        }
        if (string.IsNullOrWhiteSpace(reportHtml) && !string.IsNullOrWhiteSpace(runDir))
        {
            reportHtml = Path.Combine(runDir, "charts", "framescope-interactive-report.html");
        }

        return CreateValidatedReportEntry(
            dataRoot,
            runDir,
            reportHtml,
            ReadString(map, "Game"),
            ReadString(map, "ProcessName"),
            ReadString(map, "Time"),
            ReadInt(map, "MonitorExitCode", 0),
            statusCache);
    }

    private FrameScopeWebReportEntry BuildReportEntryFromReportHtml(string reportHtml, string dataRoot, Dictionary<string, Dictionary<string, object>> statusCache)
    {
        string runDir = GuessRunDirFromReportHtml(reportHtml);
        string game = "";
        try
        {
            DirectoryInfo run = new DirectoryInfo(runDir);
            game = run.Parent == null ? run.Name : run.Parent.Name;
        }
        catch
        {
        }

        return CreateValidatedReportEntry(dataRoot, runDir, reportHtml, game, "", SafeLastWriteTime(reportHtml).ToString("O", CultureInfo.InvariantCulture), 0, statusCache);
    }

    private FrameScopeWebReportEntry CreateValidatedReportEntry(string dataRoot, string runDir, string reportHtml, string game, string processName, string time, int monitorExitCode, Dictionary<string, Dictionary<string, object>> statusCache)
    {
        try
        {
            string fullDataRoot = Path.GetFullPath(dataRoot);
            string fullRunDir = Path.GetFullPath(runDir ?? "");
            string fullReportHtml = Path.GetFullPath(reportHtml ?? "");

            if (!IsPathInside(fullRunDir, fullDataRoot)) return null;
            if (!IsPathInside(fullReportHtml, fullRunDir)) return null;
            if (!Path.GetExtension(fullReportHtml).Equals(".html", StringComparison.OrdinalIgnoreCase)) return null;
            if (!Directory.Exists(fullRunDir)) return null;

            Dictionary<string, object> status = ReadCachedStatus(fullRunDir, statusCache);
            DateTime sortTime = Directory.GetLastWriteTimeUtc(fullRunDir);
            DateTime parsed;
            if (DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
            {
                sortTime = parsed.ToUniversalTime();
            }

            FileInfo reportInfo = File.Exists(fullReportHtml) ? new FileInfo(fullReportHtml) : null;
            return new FrameScopeWebReportEntry
            {
                ReportId = ComputeReportId(fullRunDir),
                Game = game ?? "",
                ProcessName = processName ?? "",
                Time = time ?? "",
                RunDir = fullRunDir,
                ReportHtml = fullReportHtml,
                MonitorExitCode = monitorExitCode,
                ReportExists = reportInfo != null,
                RunDirExists = true,
                ReportSizeBytes = reportInfo == null ? 0 : reportInfo.Length,
                LastWriteTime = reportInfo == null ? Directory.GetLastWriteTime(fullRunDir).ToString("O", CultureInfo.InvariantCulture) : reportInfo.LastWriteTime.ToString("O", CultureInfo.InvariantCulture),
                ReportKind = ReadString(status, "ReportKind"),
                FrameCount = ReadInt(status, "ReportFrameCount", 0),
                HasFrameData = ReadBool(status, "ReportHasFrameData", false),
                ProcessSamplerStatus = ReadString(status, "ProcessSamplerStatus"),
                ProcessSamplerValidRows = ReadInt(status, "ProcessSamplerValidRows", 0),
                SystemSamplerStatus = ReadString(status, "SystemSamplerStatus"),
                SystemSamplerValidRows = ReadInt(status, "SystemSamplerValidRows", 0),
                SortTimeUtc = sortTime
            };
        }
        catch
        {
            return null;
        }
    }

    private Dictionary<string, object> ReadCachedStatus(string runDir, Dictionary<string, Dictionary<string, object>> statusCache)
    {
        string fullRunDir;
        try { fullRunDir = Path.GetFullPath(runDir ?? ""); }
        catch { return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); }
        Dictionary<string, object> cached;
        if (statusCache != null && statusCache.TryGetValue(fullRunDir, out cached)) return cached;
        Dictionary<string, object> status = ReadJsonFile(Path.Combine(fullRunDir, "status.json"));
        if (statusCache != null) statusCache[fullRunDir] = status;
        return status;
    }

    private static DateTime SafeLastWriteTime(string path)
    {
        try { return File.GetLastWriteTime(path); }
        catch { return DateTime.MinValue; }
    }

    private static DateTime SafeLastWriteTimeUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private string ResolveReportFromRequest(FrameScopeWebBridgeRequest request, bool requireReportHtml, out FrameScopeWebReportEntry entry)
    {
        entry = null;
        string reportId = ReadString(request.Payload, "reportId");
        if (string.IsNullOrWhiteSpace(reportId))
        {
            return "A host-generated reportId is required.";
        }

        foreach (FrameScopeWebReportEntry candidate in LoadReports())
        {
            if (!string.Equals(candidate.ReportId, reportId, StringComparison.OrdinalIgnoreCase)) continue;
            if (requireReportHtml && !candidate.ReportExists)
            {
                return "The selected report HTML does not exist.";
            }
            entry = candidate;
            return "";
        }

        return "The reportId was not found in validated report history.";
    }

    private Dictionary<string, object> BuildReportActionPayload(FrameScopeWebReportEntry entry, FrameScopeWebBridgeHostResult result)
    {
        Dictionary<string, object> payload = entry.ToPayload();
        payload["message"] = result == null ? "" : result.Message;
        if (result != null && result.Payload != null)
        {
            foreach (KeyValuePair<string, object> pair in result.Payload)
            {
                payload[pair.Key] = pair.Value;
            }
        }
        return payload;
    }

    private bool PayloadContainsPathAuthority(Dictionary<string, object> payload)
    {
        if (payload == null) return false;
        return payload.ContainsKey("path") ||
            payload.ContainsKey("reportHtml") ||
            payload.ContainsKey("runDir") ||
            payload.ContainsKey("directory") ||
            payload.ContainsKey("file");
    }

    private string ResolveCurrentConfigDataRoot()
    {
        FrameScopeConfig config = FrameScopeConfigStore.Load(options.ConfigPath);
        return ResolveDataRoot(config.DataRoot);
    }

    private static bool HasMonitorCsv(string runDir)
    {
        return !string.IsNullOrWhiteSpace(runDir) &&
            (File.Exists(Path.Combine(runDir, "presentmon.csv")) ||
             File.Exists(Path.Combine(runDir, "process-samples.csv")) ||
             File.Exists(Path.Combine(runDir, "system-samples.csv")));
    }

    private static string GuessRunDirFromReportHtml(string reportHtml)
    {
        if (string.IsNullOrWhiteSpace(reportHtml)) return "";
        try
        {
            DirectoryInfo charts = Directory.GetParent(Path.GetFullPath(reportHtml));
            if (charts == null) return "";
            DirectoryInfo run = charts.Parent;
            return run == null ? "" : run.FullName;
        }
        catch
        {
            return "";
        }
    }

    private static bool IsPathInside(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        string fullRoot = EnsureTrailingSlash(Path.GetFullPath(root));
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeReportId(string runDir)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(runDir).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant());
            string id = Convert.ToBase64String(sha.ComputeHash(bytes));
            return id.Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }
    }

    private sealed class FrameScopeWebReportEntry
    {
        public string ReportId = "";
        public string Game = "";
        public string ProcessName = "";
        public string Time = "";
        public string RunDir = "";
        public string ReportHtml = "";
        public int MonitorExitCode;
        public bool ReportExists;
        public bool RunDirExists;
        public long ReportSizeBytes;
        public string LastWriteTime = "";
        public string ReportKind = "";
        public int FrameCount;
        public bool HasFrameData;
        public string ProcessSamplerStatus = "";
        public int ProcessSamplerValidRows;
        public string SystemSamplerStatus = "";
        public int SystemSamplerValidRows;
        public DateTime SortTimeUtc;

        public Dictionary<string, object> ToPayload()
        {
            return new Dictionary<string, object>
            {
                { "reportId", ReportId },
                { "game", Game },
                { "processName", ProcessName },
                { "time", Time },
                { "runDir", RunDir },
                { "reportHtml", ReportHtml },
                { "monitorExitCode", MonitorExitCode },
                { "reportExists", ReportExists },
                { "runDirExists", RunDirExists },
                { "canOpenReport", ReportExists },
                { "canOpenDirectory", RunDirExists },
                { "canRegenerate", RunDirExists },
                { "reportSizeBytes", ReportSizeBytes },
                { "lastWriteTime", LastWriteTime },
                { "reportKind", ReportKind },
                { "frameCount", FrameCount },
                { "hasFrameData", HasFrameData },
                { "processSamplerStatus", ProcessSamplerStatus },
                { "processSamplerValidRows", ProcessSamplerValidRows },
                { "systemSamplerStatus", SystemSamplerStatus },
                { "systemSamplerValidRows", SystemSamplerValidRows }
            };
        }
    }
}
