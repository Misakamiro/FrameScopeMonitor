using System;

internal static class FrameScopeReportRecoveryPolicy
{
    internal static bool ShouldRecover(string phase, bool hasUsableMonitorData, bool reportComplete, bool captureActive)
    {
        if (reportComplete || !hasUsableMonitorData || captureActive) return false;
        string value = (phase ?? "").Trim().ToLowerInvariant();
        return value.Length == 0 ||
            value == "capturing" ||
            value == "finalizing" ||
            value == "done" ||
            value == "error";
    }
}
