using System;

internal static partial class FrameScopeReportGenerator
{
    private static void WriteProgress(string progressPath, string phase, int percent, string message, DateTime startedAt, string error, bool canRetry)
    {
        if (string.IsNullOrWhiteSpace(progressPath)) return;
        try { FrameScopeReportProgress.Write(progressPath, phase, percent, message, startedAt, error, canRetry); }
        catch { }
    }
}
