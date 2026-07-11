using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

internal enum FrameScopeMonitorOwnerState
{
    Exited,
    Active,
    Uncertain
}

internal sealed class FrameScopeMonitorOwnerIdentity
{
    internal int ProcessId;
    internal string ExecutablePath = "";
    internal DateTime StartedAtUtc;
    internal string StartedAtUtcText
    {
        get { return StartedAtUtc == default(DateTime) ? "" : StartedAtUtc.ToString("o", CultureInfo.InvariantCulture); }
    }
}

internal sealed class FrameScopeReportRecoveryCandidate
{
    internal string RunDirectory = "";
    internal string TargetKey = "";
    internal string Phase = "";
    internal DateTime LastWriteTimeUtc;
    internal bool ReportComplete;
    internal bool HasUsableMonitorData;
    internal bool InputStable;
    internal string InputFingerprint = "";
    internal bool CaptureActive;
    internal int Attempts;
    internal bool Exhausted;
}

internal static class FrameScopeReportRecoveryPolicy
{
    internal const int MaximumRecoveryAttempts = 3;

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

    internal static FrameScopeMonitorOwnerIdentity CaptureCurrentProcessIdentity()
    {
        try
        {
            using (Process process = Process.GetCurrentProcess())
            {
                return CaptureProcessIdentity(process);
            }
        }
        catch { return new FrameScopeMonitorOwnerIdentity(); }
    }

    internal static bool HasRecordedMonitorOwnerIdentity(int processId, string executablePath, string startedAtUtc)
    {
        return processId > 0 || !string.IsNullOrWhiteSpace(executablePath) || !string.IsNullOrWhiteSpace(startedAtUtc);
    }

    internal static bool IsRecoveryCaptureActive(
        string phase,
        bool hasRecordedOwnerIdentity,
        FrameScopeMonitorOwnerState ownerState)
    {
        string value = (phase ?? "").Trim().ToLowerInvariant();
        bool potentiallyActiveLegacyPhase = value == "capturing" || value == "finalizing";
        return (hasRecordedOwnerIdentity || potentiallyActiveLegacyPhase) && ownerState != FrameScopeMonitorOwnerState.Exited;
    }

    internal static FrameScopeMonitorOwnerState ProbeMonitorOwner(int processId, string expectedExecutablePath, string expectedStartedAtUtc)
    {
        if (processId <= 0) return FrameScopeMonitorOwnerState.Uncertain;
        try
        {
            using (Process process = Process.GetProcessById(processId))
            {
                if (process.HasExited) return FrameScopeMonitorOwnerState.Exited;
                FrameScopeMonitorOwnerIdentity actual = CaptureProcessIdentity(process);
                DateTime expectedStart;
                if (string.IsNullOrWhiteSpace(expectedExecutablePath) ||
                    !DateTime.TryParse(expectedStartedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expectedStart))
                {
                    return FrameScopeMonitorOwnerState.Uncertain;
                }

                bool samePath = string.Equals(
                    Path.GetFullPath(actual.ExecutablePath),
                    Path.GetFullPath(expectedExecutablePath),
                    StringComparison.OrdinalIgnoreCase);
                bool sameStart = actual.StartedAtUtc == expectedStart.ToUniversalTime();
                return samePath && sameStart ? FrameScopeMonitorOwnerState.Active : FrameScopeMonitorOwnerState.Uncertain;
            }
        }
        catch (ArgumentException)
        {
            return FrameScopeMonitorOwnerState.Exited;
        }
        catch
        {
            return FrameScopeMonitorOwnerState.Uncertain;
        }
    }

    internal static List<FrameScopeReportRecoveryCandidate> SelectCandidates(
        IEnumerable<FrameScopeReportRecoveryCandidate> candidates,
        int maximumPerTarget,
        int maximumTotal)
    {
        int perTarget = Math.Max(1, maximumPerTarget);
        int total = Math.Max(1, maximumTotal);
        return (candidates ?? Enumerable.Empty<FrameScopeReportRecoveryCandidate>())
            .Where(candidate => candidate != null && candidate.InputStable && !candidate.CaptureActive)
            .Where(candidate => !candidate.Exhausted && candidate.Attempts < MaximumRecoveryAttempts)
            .Where(candidate => ShouldRecover(candidate.Phase, candidate.HasUsableMonitorData, candidate.ReportComplete, candidate.CaptureActive))
            .GroupBy(candidate => candidate.TargetKey ?? "", StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.OrderByDescending(candidate => candidate.LastWriteTimeUtc).Take(perTarget))
            .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
            .Take(total)
            .ToList();
    }

    internal static bool IsRecoveryExhausted(int attempts, bool generationSucceeded)
    {
        return !generationSucceeded && attempts >= MaximumRecoveryAttempts;
    }

    internal static bool IsGenerationSuccessful(
        bool timedOut,
        int exitCode,
        bool canRetry,
        bool artifactsComplete,
        bool inputFingerprintMatches)
    {
        return !timedOut && exitCode == 0 && !canRetry && artifactsComplete && inputFingerprintMatches;
    }

    private static FrameScopeMonitorOwnerIdentity CaptureProcessIdentity(Process process)
    {
        if (process == null) throw new ArgumentNullException("process");
        int processId = 0;
        string executablePath = "";
        DateTime startedAtUtc = default(DateTime);
        try { processId = process.Id; }
        catch { }
        try { executablePath = process.MainModule.FileName; }
        catch { }
        try { startedAtUtc = process.StartTime.ToUniversalTime(); }
        catch { }
        return new FrameScopeMonitorOwnerIdentity
        {
            ProcessId = processId,
            ExecutablePath = executablePath ?? "",
            StartedAtUtc = startedAtUtc
        };
    }
}
