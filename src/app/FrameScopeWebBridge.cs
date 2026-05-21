using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

internal sealed partial class FrameScopeWebBridge
{
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private readonly FrameScopeWebBridgeOptions options;
    private readonly Action<string> eventSink;
    private int processRefreshInFlight;
    private int monitorActionInFlight;
    private int reportRegenerateInFlight;
    private int diagnosticsGenerateInFlight;

    public FrameScopeWebBridge(FrameScopeWebBridgeOptions options, Action<string> eventSink)
    {
        this.options = NormalizeOptions(options);
        this.eventSink = eventSink;
    }

    public string HandleJsonMessage(string messageJson)
    {
        FrameScopeWebBridgeRequest request;
        try
        {
            request = ParseRequest(messageJson);
        }
        catch (Exception ex)
        {
            return ErrorResponse("", "invalid_json", "Bridge request JSON could not be parsed: " + ex.Message);
        }

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            return ErrorResponse("", "missing_request_id", "Every bridge request must include requestId.");
        }

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return ErrorResponse(request.RequestId, "missing_type", "Every bridge request must include type.");
        }

        try
        {
            if (string.Equals(request.Type, "state.snapshot", StringComparison.Ordinal))
            {
                return OkResponse(request.RequestId, BuildStateSnapshotPayload());
            }

            if (string.Equals(request.Type, "config.get", StringComparison.Ordinal))
            {
                return OkResponse(request.RequestId, BuildConfigPayload());
            }

            if (string.Equals(request.Type, "config.save", StringComparison.Ordinal))
            {
                return SaveConfig(request);
            }

            if (string.Equals(request.Type, "processes.refresh", StringComparison.Ordinal))
            {
                return StartProcessRefresh(request);
            }

            if (string.Equals(request.Type, "monitor.start", StringComparison.Ordinal))
            {
                return StartMonitor(request);
            }

            if (string.Equals(request.Type, "monitor.stop", StringComparison.Ordinal))
            {
                return StopMonitor(request);
            }

            if (string.Equals(request.Type, "reports.list", StringComparison.Ordinal))
            {
                return OkResponse(request.RequestId, BuildReportsListPayload());
            }

            if (string.Equals(request.Type, "reports.open", StringComparison.Ordinal))
            {
                return OpenReport(request);
            }

            if (string.Equals(request.Type, "reports.openDirectory", StringComparison.Ordinal))
            {
                return OpenReportDirectory(request);
            }

            if (string.Equals(request.Type, "reports.regenerate", StringComparison.Ordinal))
            {
                return RegenerateReport(request);
            }

            if (string.Equals(request.Type, "diagnostics.generate", StringComparison.Ordinal))
            {
                return GenerateDiagnostics(request);
            }

            if (string.Equals(request.Type, "targets.get", StringComparison.Ordinal))
            {
                return OkResponse(request.RequestId, BuildTargetsPayload("loaded"));
            }

            if (string.Equals(request.Type, "targets.save", StringComparison.Ordinal))
            {
                return SaveTargets(request);
            }

            return ErrorResponse(request.RequestId, "unsupported_request", "Bridge request type is not implemented: " + request.Type);
        }
        catch (Exception ex)
        {
            PublishEvent("event.error", new Dictionary<string, object>
            {
                { "requestId", request.RequestId },
                { "code", "handler_failed" },
                { "message", ex.Message },
                { "type", request.Type }
            });
            return ErrorResponse(request.RequestId, "handler_failed", ex.Message);
        }
    }

    private FrameScopeWebBridgeRequest ParseRequest(string messageJson)
    {
        var root = json.Deserialize<Dictionary<string, object>>(messageJson ?? "");
        if (root == null) throw new InvalidOperationException("Request root is empty.");

        var request = new FrameScopeWebBridgeRequest
        {
            RequestId = ReadString(root, "requestId"),
            Type = ReadString(root, "type"),
            Payload = ReadDictionary(root, "payload")
        };
        return request;
    }

    private string OkResponse(string requestId, Dictionary<string, object> payload)
    {
        return json.Serialize(new Dictionary<string, object>
        {
            { "requestId", requestId ?? "" },
            { "type", "response" },
            { "ok", true },
            { "payload", payload ?? new Dictionary<string, object>() },
            { "error", null }
        });
    }

    private string ErrorResponse(string requestId, string code, string message)
    {
        return json.Serialize(new Dictionary<string, object>
        {
            { "requestId", requestId ?? "" },
            { "type", "response" },
            { "ok", false },
            { "payload", new Dictionary<string, object>() },
            { "error", new Dictionary<string, object>
                {
                    { "code", string.IsNullOrWhiteSpace(code) ? "error" : code },
                    { "message", message ?? "" }
                }
            }
        });
    }

    private void PublishEvent(string type, Dictionary<string, object> payload)
    {
        if (eventSink == null) return;
        var envelope = new Dictionary<string, object>
        {
            { "type", type },
            { "payload", payload ?? new Dictionary<string, object>() },
            { "sentAt", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) }
        };
        eventSink(json.Serialize(envelope));
    }

    private static string ReadString(Dictionary<string, object> map, string key)
    {
        object value;
        return map != null && map.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
    }

    private static int ReadInt(Dictionary<string, object> map, string key, int fallback)
    {
        object value;
        if (map == null || !map.TryGetValue(key, out value) || value == null) return fallback;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    private static bool ReadBool(Dictionary<string, object> map, string key, bool fallback)
    {
        object value;
        if (map == null || !map.TryGetValue(key, out value) || value == null) return fallback;
        try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> map, string key)
    {
        object value;
        if (map == null || !map.TryGetValue(key, out value) || value == null)
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        var dictionary = value as Dictionary<string, object>;
        if (dictionary != null)
        {
            return new Dictionary<string, object>(dictionary, StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    private FrameScopeWebBridgeOptions NormalizeOptions(FrameScopeWebBridgeOptions raw)
    {
        raw = raw ?? new FrameScopeWebBridgeOptions();
        string root = string.IsNullOrWhiteSpace(raw.Root) ? AppDomain.CurrentDomain.BaseDirectory : raw.Root;
        root = Path.GetFullPath(root);
        return new FrameScopeWebBridgeOptions
        {
            Root = root,
            ConfigPath = Path.GetFullPath(string.IsNullOrWhiteSpace(raw.ConfigPath) ? Path.Combine(root, "framescope-config.json") : raw.ConfigPath),
            StatePath = Path.GetFullPath(string.IsNullOrWhiteSpace(raw.StatePath) ? Path.Combine(root, "framescope-watcher-state.json") : raw.StatePath),
            HistoryPath = Path.GetFullPath(string.IsNullOrWhiteSpace(raw.HistoryPath) ? Path.Combine(root, "framescope-history.jsonl") : raw.HistoryPath),
            HostAdapter = raw.HostAdapter
        };
    }

    private bool IsPathInsideRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string root = EnsureTrailingSlash(Path.GetFullPath(options.Root));
        string full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSlash(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Path.DirectorySeparatorChar.ToString();
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;
    }
}
