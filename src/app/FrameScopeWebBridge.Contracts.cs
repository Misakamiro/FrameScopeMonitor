using System;
using System.Collections.Generic;

internal sealed class FrameScopeWebBridgeOptions
{
    public FrameScopeWebBridgeOptions()
    {
        Root = "";
        ConfigPath = "";
        StatePath = "";
        HistoryPath = "";
    }

    public string Root { get; set; }
    public string ConfigPath { get; set; }
    public string StatePath { get; set; }
    public string HistoryPath { get; set; }
    public IFrameScopeWebBridgeHostAdapter HostAdapter { get; set; }
    public Func<FrameScopeWebBridgeHostState> HostStateProvider { get; set; }
}

internal sealed class FrameScopeWebBridgeHostState
{
    public FrameScopeWebBridgeHostState()
    {
        WindowVisible = true;
        TrayAvailable = false;
    }

    public bool WindowVisible { get; set; }
    public bool TrayAvailable { get; set; }
}

internal sealed class FrameScopeWebBridgeRequest
{
    public FrameScopeWebBridgeRequest()
    {
        RequestId = "";
        Type = "";
        Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    public string RequestId { get; set; }
    public string Type { get; set; }
    public Dictionary<string, object> Payload { get; set; }
}

internal sealed class FrameScopeWebBridgeHostContext
{
    public FrameScopeWebBridgeHostContext()
    {
        Root = "";
        ConfigPath = "";
        StatePath = "";
        HistoryPath = "";
        DataRoot = "";
        LogDirectory = "";
    }

    public string Root { get; set; }
    public string ConfigPath { get; set; }
    public string StatePath { get; set; }
    public string HistoryPath { get; set; }
    public string DataRoot { get; set; }
    public string LogDirectory { get; set; }
}

internal sealed class FrameScopeWebBridgeHostResult
{
    public FrameScopeWebBridgeHostResult()
    {
        Ok = false;
        Status = "";
        Code = "";
        Message = "";
        Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    public bool Ok { get; set; }
    public string Status { get; set; }
    public string Code { get; set; }
    public string Message { get; set; }
    public Dictionary<string, object> Payload { get; set; }

    public static FrameScopeWebBridgeHostResult Success(string status, string message, Dictionary<string, object> payload)
    {
        return new FrameScopeWebBridgeHostResult
        {
            Ok = true,
            Status = status ?? "",
            Code = "",
            Message = message ?? "",
            Payload = payload ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        };
    }

    public static FrameScopeWebBridgeHostResult Failure(string code, string message, Dictionary<string, object> payload)
    {
        return new FrameScopeWebBridgeHostResult
        {
            Ok = false,
            Status = "error",
            Code = string.IsNullOrWhiteSpace(code) ? "host_action_failed" : code,
            Message = message ?? "",
            Payload = payload ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        };
    }
}

internal interface IFrameScopeWebBridgeHostAdapter
{
    FrameScopeWebBridgeHostResult StartMonitor(FrameScopeWebBridgeHostContext context);
    FrameScopeWebBridgeHostResult StopMonitor(FrameScopeWebBridgeHostContext context);
    FrameScopeWebBridgeHostResult OpenReport(FrameScopeWebBridgeHostContext context, string reportHtml, string runDir);
    FrameScopeWebBridgeHostResult OpenDirectory(FrameScopeWebBridgeHostContext context, string directory);
    FrameScopeWebBridgeHostResult OpenLogsDirectory(FrameScopeWebBridgeHostContext context);
    FrameScopeWebBridgeHostResult RegenerateReport(FrameScopeWebBridgeHostContext context, string runDir);
    FrameScopeWebBridgeHostResult GenerateDiagnostics(FrameScopeWebBridgeHostContext context, string runDir);
}
