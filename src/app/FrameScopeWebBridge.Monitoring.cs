using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

internal sealed partial class FrameScopeWebBridge
{
    private string StartMonitor(FrameScopeWebBridgeRequest request)
    {
        return StartHostAction(
            request,
            "monitor.start",
            "monitor.starting",
            "monitor.in_flight",
            "event.status",
            delegate { return Interlocked.CompareExchange(ref monitorActionInFlight, 1, 0) == 0; },
            delegate { Interlocked.Exchange(ref monitorActionInFlight, 0); },
            delegate(FrameScopeWebBridgeHostContext context)
            {
                return RequireHostAdapter().StartMonitor(context);
            });
    }

    private string StopMonitor(FrameScopeWebBridgeRequest request)
    {
        return StartHostAction(
            request,
            "monitor.stop",
            "monitor.stopping",
            "monitor.in_flight",
            "event.status",
            delegate { return Interlocked.CompareExchange(ref monitorActionInFlight, 1, 0) == 0; },
            delegate { Interlocked.Exchange(ref monitorActionInFlight, 0); },
            delegate(FrameScopeWebBridgeHostContext context)
            {
                return RequireHostAdapter().StopMonitor(context);
            });
    }

    private delegate FrameScopeWebBridgeHostResult HostAction(FrameScopeWebBridgeHostContext context);
    private delegate bool TryEnterGate();
    private delegate void ExitGate();

    private string StartHostAction(
        FrameScopeWebBridgeRequest request,
        string action,
        string runningStatus,
        string inFlightStatus,
        string completionEventType,
        TryEnterGate tryEnter,
        ExitGate exit,
        HostAction actionBody)
    {
        if (options.HostAdapter == null)
        {
            return ErrorResponse(request.RequestId, "host_adapter_missing", action + " is not safely connected to the C# host.");
        }

        if (!tryEnter())
        {
            PublishEvent("event.status", new Dictionary<string, object>
            {
                { "requestId", request.RequestId },
                { "status", inFlightStatus },
                { "action", action },
                { "message", HostActionMessage(action, "in_flight") }
            });
            return OkResponse(request.RequestId, new Dictionary<string, object>
            {
                { "status", "in_flight" },
                { "requestId", request.RequestId },
                { "action", action },
                { "message", HostActionMessage(action, "in_flight") }
            });
        }

        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                PublishEvent(completionEventType, new Dictionary<string, object>
                {
                    { "requestId", request.RequestId },
                    { "status", runningStatus },
                    { "action", action },
                    { "message", HostActionMessage(action, "running") },
                    { "percent", completionEventType == "event.reportProgress" ? 5 : 0 }
                });

                FrameScopeWebBridgeHostResult result = actionBody(BuildHostContext());
                Dictionary<string, object> payload = BuildHostEventPayload(request.RequestId, action, result);
                if (completionEventType == "event.reportProgress" && result.Ok)
                {
                    payload["resultStatus"] = payload["status"];
                    payload["status"] = "completed";
                }
                if (result.Ok)
                {
                    PublishEvent(completionEventType, payload);
                }
                else
                {
                    PublishEvent("event.error", payload);
                    if (completionEventType == "event.reportProgress")
                    {
                        PublishEvent(completionEventType, payload);
                    }
                }
            }
            catch (Exception ex)
            {
                PublishEvent("event.error", new Dictionary<string, object>
                {
                    { "requestId", request.RequestId },
                    { "status", "error" },
                    { "action", action },
                    { "code", "host_action_failed" },
                    { "message", ex.Message },
                    { "percent", completionEventType == "event.reportProgress" ? 100 : 0 }
                });
            }
            finally
            {
                exit();
            }
        });

        return OkResponse(request.RequestId, new Dictionary<string, object>
        {
            { "status", "accepted" },
            { "requestId", request.RequestId },
            { "action", action },
            { "message", HostActionMessage(action, "accepted") }
        });
    }

    private static string HostActionMessage(string action, string phase)
    {
        if (string.Equals(action, "monitor.start", StringComparison.Ordinal))
        {
            if (string.Equals(phase, "accepted", StringComparison.Ordinal)) return "启动请求已接受，正在由本机程序启动监控 worker。";
            if (string.Equals(phase, "running", StringComparison.Ordinal)) return "正在启动监控 worker。";
            return "监控操作正在执行。";
        }

        if (string.Equals(action, "monitor.stop", StringComparison.Ordinal))
        {
            if (string.Equals(phase, "accepted", StringComparison.Ordinal)) return "停止请求已接受，正在清理监控 worker。";
            if (string.Equals(phase, "running", StringComparison.Ordinal)) return "正在停止监控 worker。";
            return "监控操作正在执行。";
        }

        if (string.Equals(phase, "accepted", StringComparison.Ordinal)) return action + " accepted. Completion will be pushed as an event.";
        if (string.Equals(phase, "running", StringComparison.Ordinal)) return action + " is running.";
        return action + " is already running.";
    }

    private IFrameScopeWebBridgeHostAdapter RequireHostAdapter()
    {
        if (options.HostAdapter == null)
        {
            throw new InvalidOperationException("Host adapter is not configured.");
        }
        return options.HostAdapter;
    }

    private FrameScopeWebBridgeHostContext BuildHostContext()
    {
        FrameScopeConfig config = FrameScopeConfigStore.Load(options.ConfigPath);
        return new FrameScopeWebBridgeHostContext
        {
            Root = options.Root,
            ConfigPath = options.ConfigPath,
            StatePath = options.StatePath,
            HistoryPath = options.HistoryPath,
            DataRoot = ResolveDataRoot(config.DataRoot),
            LogDirectory = ResolveCurrentLogDirectory()
        };
    }

    private string ResolveCurrentLogDirectory()
    {
        return Path.GetFullPath(options.Root);
    }

    private static Dictionary<string, object> BuildHostEventPayload(string requestId, string action, FrameScopeWebBridgeHostResult result)
    {
        if (result == null)
        {
            result = FrameScopeWebBridgeHostResult.Failure("host_action_failed", "Host action returned no result.", null);
        }

        Dictionary<string, object> payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        payload["requestId"] = requestId ?? "";
        payload["status"] = result.Ok
            ? (string.IsNullOrWhiteSpace(result.Status) ? "completed" : result.Status)
            : "error";
        payload["action"] = action ?? "";
        payload["ok"] = result.Ok;
        payload["code"] = result.Ok ? "" : (string.IsNullOrWhiteSpace(result.Code) ? "host_action_failed" : result.Code);
        payload["message"] = result.Message ?? "";
        payload["percent"] = 100;

        if (result.Payload != null)
        {
            foreach (KeyValuePair<string, object> pair in result.Payload)
            {
                payload[pair.Key] = pair.Value;
            }
        }

        return payload;
    }
}
