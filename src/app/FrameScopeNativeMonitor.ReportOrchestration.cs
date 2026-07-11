using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

internal static partial class FrameScopeNativeMonitor
{
    private static void RecoverStaleMissingReports(string dataRoot, FrameScopeConfig config)
    {
        try
        {
            var root = new DirectoryInfo(dataRoot);
            if (!root.Exists) return;
            var candidates = new List<FrameScopeReportRecoveryCandidate>();
            foreach (var gameDir in root.GetDirectories())
            {
                foreach (DirectoryInfo run in gameDir.GetDirectories())
                {
                    Dictionary<string, object> status = ReadStatusDictionary(run.FullName);
                    string phase = StatusString(status, "Phase", "");
                    FrameScopeReportArtifactState artifacts = FrameScopeReportArtifacts.Inspect(run.FullName);
                    FrameScopeReportInputFingerprint input = FrameScopeReportArtifacts.CaptureInputFingerprint(run.FullName);
                    FrameScopeMonitorOwnerState owner = ProbeRecoveryOwner(status);
                    bool captureActive = IsRecoveryCaptureActive(status, phase, owner);
                    candidates.Add(new FrameScopeReportRecoveryCandidate
                    {
                        RunDirectory = run.FullName,
                        TargetKey = gameDir.FullName,
                        Phase = phase,
                        LastWriteTimeUtc = run.LastWriteTimeUtc,
                        ReportComplete = artifacts.IsComplete,
                        HasUsableMonitorData = FrameScopeReportArtifacts.HasUsableMonitorData(run.FullName),
                        InputStable = input.Stable && DateTime.Now - LatestMonitorCsvWriteTime(run.FullName) > TimeSpan.FromMinutes(2),
                        InputFingerprint = input.Value,
                        CaptureActive = captureActive,
                        Attempts = StatusInt(status, "ReportRecoveryAttempts", 0),
                        Exhausted = StatusBool(status, "ReportRecoveryExhausted", false)
                    });
                }
            }

            foreach (FrameScopeReportRecoveryCandidate candidate in FrameScopeReportRecoveryPolicy.SelectCandidates(candidates, 3, 5))
            {
                Dictionary<string, object> status = ReadStatusDictionary(candidate.RunDirectory);
                FrameScopeReportInputFingerprint before = FrameScopeReportArtifacts.CaptureInputFingerprint(candidate.RunDirectory);
                FrameScopeMonitorOwnerState ownerBefore = ProbeRecoveryOwner(status);
                if (!before.Stable || !string.Equals(before.Value, candidate.InputFingerprint, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsRecoveryCaptureActive(status, candidate.Phase, ownerBefore)) continue;

                status = MarkRecoveryAttemptStarted(candidate.RunDirectory, status);
                WriteFrameScopeLog("recover-stale-report run=" + candidate.RunDirectory + " phase=" + candidate.Phase);
                status = EnsureReportForCompletedRun(
                    candidate.RunDirectory,
                    status,
                    StatusInt(status, "ExitCode", -1),
                    config,
                    true);

                FrameScopeReportInputFingerprint after = FrameScopeReportArtifacts.CaptureInputFingerprint(candidate.RunDirectory);
                Dictionary<string, object> statusAfter = ReadStatusDictionary(candidate.RunDirectory) ?? status;
                FrameScopeMonitorOwnerState ownerAfter = ProbeRecoveryOwner(statusAfter);
                if (!after.Stable || !string.Equals(before.Value, after.Value, StringComparison.OrdinalIgnoreCase) ||
                    IsRecoveryCaptureActive(statusAfter, candidate.Phase, ownerAfter))
                {
                    InvalidateRecoveryPublication(candidate.RunDirectory, statusAfter, "Capture ownership or monitor CSV inputs changed during recovery.");
                }
            }
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("recover-stale-report-error " + ex.Message);
        }
    }

    private static Dictionary<string, object> EnsureReportForCompletedRun(string runDir, Dictionary<string, object> status, int monitorExitCode, FrameScopeConfig config, bool recoveryAttempt = false)
    {
        FrameScopeReportArtifactState artifacts = FrameScopeReportArtifacts.Inspect(runDir);
        var reportHtml = artifacts.HtmlPath;
        if (artifacts.IsComplete)
        {
            return status;
        }

        var presentMonCsv = Path.Combine(runDir, "presentmon.csv");
        bool hasUsableMonitorData = FrameScopeReportArtifacts.HasUsableMonitorData(runDir);
        if (!hasUsableMonitorData)
        {
            FrameScopeReportInputFingerprint input = FrameScopeReportArtifacts.CaptureInputFingerprint(runDir);
            WriteFrameScopeLog("report-generate-skip missing-monitor-data run=" + runDir);
            return UpdateStatusAfterReportGeneration(runDir, status, new ReportGenerationResult
            {
                Attempted = false,
                ExitCode = -1,
                ReportHtml = reportHtml,
                LogPath = Path.Combine(runDir, "report-generation.log"),
                ProgressPath = Path.Combine(runDir, "report-progress.json"),
                Error = "No monitor CSV data was found.",
                ReportKind = "error",
                InputFingerprint = input.Value,
                InputFingerprintStable = input.Stable,
                ArtifactsComplete = false,
                InputFingerprintMatches = false
            }, monitorExitCode, recoveryAttempt);
        }

        string phase = StatusString(status, "Phase", "");
        if (!FrameScopeReportRecoveryPolicy.ShouldRecover(phase, true, false, false))
        {
            WriteFrameScopeLog("report-generate-skip phase=" + phase + " run=" + runDir);
            return status;
        }

        if (!File.Exists(presentMonCsv))
        {
            WriteFrameScopeLog("report-generate-partial missing-presentmon run=" + runDir);
        }

        var result = RunReportGeneration(runDir, config);
        return UpdateStatusAfterReportGeneration(runDir, status, result, monitorExitCode, recoveryAttempt);
    }

    private static FrameScopeMonitorOwnerState ProbeRecoveryOwner(Dictionary<string, object> status)
    {
        return FrameScopeReportRecoveryPolicy.ProbeMonitorOwner(
            StatusInt(status, "MonitorPid", 0),
            StatusString(status, "MonitorProcessPath", ""),
            StatusString(status, "MonitorStartedAtUtc", ""));
    }

    private static bool IsRecoveryCaptureActive(
        Dictionary<string, object> status,
        string phase,
        FrameScopeMonitorOwnerState ownerState)
    {
        bool hasRecordedOwnerIdentity = FrameScopeReportRecoveryPolicy.HasRecordedMonitorOwnerIdentity(
            StatusInt(status, "MonitorPid", 0),
            StatusString(status, "MonitorProcessPath", ""),
            StatusString(status, "MonitorStartedAtUtc", ""));
        return FrameScopeReportRecoveryPolicy.IsRecoveryCaptureActive(phase, hasRecordedOwnerIdentity, ownerState);
    }

    private static Dictionary<string, object> MarkRecoveryAttemptStarted(string runDir, Dictionary<string, object> status)
    {
        var map = status != null
            ? new Dictionary<string, object>(status, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        map["ReportRecoveryAttempts"] = StatusInt(map, "ReportRecoveryAttempts", 0) + 1;
        map["ReportRecoveryExhausted"] = false;
        map["ReportRecoveryLastAttemptAt"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
        FrameScopeJsonFile.Write(Path.Combine(runDir, "status.json"), Json.Serialize(map));
        return map;
    }

    private static void InvalidateRecoveryPublication(string runDir, Dictionary<string, object> status, string error)
    {
        try
        {
            string manifest = Path.Combine(runDir, "charts", FrameScopeReportArtifacts.ManifestFileName);
            if (File.Exists(manifest)) File.Delete(manifest);
        }
        catch { }

        Dictionary<string, object> current = ReadStatusDictionary(runDir) ?? status ?? new Dictionary<string, object>();
        current["ReportArtifactsComplete"] = false;
        current["ReportInputFingerprintMatches"] = false;
        current["ReportCanRetry"] = true;
        current["ReportError"] = error;
        int attempts = StatusInt(current, "ReportRecoveryAttempts", 0);
        current["ReportRecoveryExhausted"] = FrameScopeReportRecoveryPolicy.IsRecoveryExhausted(attempts, false);
        FrameScopeJsonFile.Write(Path.Combine(runDir, "status.json"), Json.Serialize(current));

        try
        {
            string summaryPath = Path.Combine(runDir, "summary.json");
            Dictionary<string, object> summary = File.Exists(summaryPath)
                ? Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(summaryPath, Encoding.UTF8))
                : new Dictionary<string, object>();
            if (summary == null) summary = new Dictionary<string, object>();
            summary["ReportArtifactsComplete"] = false;
            summary["ReportInputFingerprintMatches"] = false;
            summary["ReportCanRetry"] = true;
            summary["ReportError"] = error;
            summary["ReportRecoveryAttempts"] = attempts;
            summary["ReportRecoveryExhausted"] = current["ReportRecoveryExhausted"];
            FrameScopeJsonFile.Write(summaryPath, Json.Serialize(summary));
        }
        catch { }
    }

    private static bool HasAnyMonitorCsv(string runDir)
    {
        return FrameScopeReportArtifacts.HasUsableMonitorData(runDir);
    }

    private static DateTime LatestMonitorCsvWriteTime(string runDir)
    {
        DateTime latest = DateTime.MinValue;
        foreach (var name in new[] { "presentmon.csv", "process-samples.csv", "system-samples.csv" })
        {
            var path = Path.Combine(runDir, name);
            if (!File.Exists(path)) continue;
            var writeTime = File.GetLastWriteTime(path);
            if (writeTime > latest) latest = writeTime;
        }
        return latest == DateTime.MinValue ? DateTime.Now : latest;
    }

    private static ReportGenerationResult FinishReportGeneration(ReportGenerationResult result, FrameScopeConfig config, Stopwatch totalTimer)
    {
        try { if (totalTimer != null) totalTimer.Stop(); }
        catch { }

        WritePerformanceFrameScopeLog(
            config,
            delegate
            {
                return "report-generation-ms=" + (totalTimer == null ? 0 : totalTimer.ElapsedMilliseconds).ToString(CultureInfo.InvariantCulture) +
                    " exit=" + (result == null ? -1 : result.ExitCode).ToString(CultureInfo.InvariantCulture) +
                    " frames=" + (result == null ? 0 : result.FrameCount).ToString(CultureInfo.InvariantCulture) +
                    " processSamples=" + (result == null ? 0 : result.ProcessSampleCount).ToString(CultureInfo.InvariantCulture) +
                    " systemSamples=" + (result == null ? 0 : result.SystemSampleCount).ToString(CultureInfo.InvariantCulture) +
                    " kind=" + (result == null ? "" : result.ReportKind);
            });
        return result;
    }

    private static void ReadReportManifest(string runDir, ReportGenerationResult result)
    {
        try
        {
            var manifestPath = Path.Combine(runDir, "charts", "framescope-interactive-manifest.json");
            if (!File.Exists(manifestPath)) return;
            var manifest = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifestPath, Encoding.UTF8));
            object framesValue;
            if (manifest != null && manifest.TryGetValue("frames", out framesValue) && framesValue != null)
            {
                result.FrameCount = Convert.ToInt32(framesValue);
                result.HasFrameData = result.FrameCount > 0;
            }
            object processSamplesValue;
            if (manifest != null && manifest.TryGetValue("processSamples", out processSamplesValue) && processSamplesValue != null)
            {
                result.ProcessSampleCount = Convert.ToInt32(processSamplesValue);
            }
            object systemSamplesValue;
            if (manifest != null && manifest.TryGetValue("systemSamples", out systemSamplesValue) && systemSamplesValue != null)
            {
                result.SystemSampleCount = Convert.ToInt32(systemSamplesValue);
            }
            object kindValue;
            if (manifest != null && manifest.TryGetValue("reportKind", out kindValue) && kindValue != null)
            {
                result.ReportKind = Convert.ToString(kindValue);
            }
            CopyManifestSamplerEvidence(manifest, result.SamplerEvidenceFields, "processSampler", "ProcessSampler");
            CopyManifestSamplerEvidence(manifest, result.SamplerEvidenceFields, "systemSampler", "SystemSampler");
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("report-manifest-read-failed run=" + runDir + " error=" + ex.Message);
        }
    }

    private static void CopyManifestSamplerEvidence(Dictionary<string, object> manifest, Dictionary<string, object> target, string manifestPrefix, string statusPrefix)
    {
        if (manifest == null || target == null) return;
        foreach (string suffix in new[]
        {
            "Required", "Exe", "ExecutableAvailable", "Started", "Pid", "StartedAt", "ExitedAt", "ExitCode",
            "ExitedEarly", "StopRequested", "ForcedStop", "CsvPath", "CsvExists", "CsvBytes", "ValidRows", "Status", "ErrorTail"
        })
        {
            object value;
            if (manifest.TryGetValue(manifestPrefix + suffix, out value)) target[statusPrefix + suffix] = value;
        }
    }

    private static void WriteReportLog(string logPath, string text)
    {
        try { File.WriteAllText(logPath, text ?? "", Encoding.UTF8); }
        catch { }
    }
}
