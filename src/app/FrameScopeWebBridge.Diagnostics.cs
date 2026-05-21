using System;
using System.Collections.Generic;
using System.Threading;

internal sealed partial class FrameScopeWebBridge
{
    private string GenerateDiagnostics(FrameScopeWebBridgeRequest request)
    {
        if (PayloadContainsPathAuthority(request.Payload))
        {
            return ErrorResponse(request.RequestId, "path_not_allowed", "diagnostics.generate accepts only an optional host-generated reportId.");
        }

        string runDir = "";
        string reportId = ReadString(request.Payload, "reportId");
        FrameScopeWebReportEntry entry = null;
        if (!string.IsNullOrWhiteSpace(reportId))
        {
            string error = ResolveReportFromRequest(request, false, out entry);
            if (!string.IsNullOrWhiteSpace(error)) return ErrorResponse(request.RequestId, "report_not_found", error);
            runDir = entry.RunDir;
        }

        return StartHostAction(
            request,
            "diagnostics.generate",
            "diagnostics.generating",
            "diagnostics.in_flight",
            "event.reportProgress",
            delegate { return Interlocked.CompareExchange(ref diagnosticsGenerateInFlight, 1, 0) == 0; },
            delegate { Interlocked.Exchange(ref diagnosticsGenerateInFlight, 0); },
            delegate(FrameScopeWebBridgeHostContext context)
            {
                FrameScopeWebBridgeHostResult result = RequireHostAdapter().GenerateDiagnostics(context, runDir);
                if (result.Payload == null) result.Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (entry != null)
                {
                    result.Payload["reportId"] = entry.ReportId;
                    result.Payload["runDir"] = entry.RunDir;
                }
                return result;
            });
    }
}
