using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

internal static partial class FrameScopeNativeMonitor
{
    private const int ReportGenerationTimeoutMs = 120000;

    private static ReportGenerationResult RunReportGeneration(string runDir, FrameScopeConfig config = null)
    {
        return RunReportGeneration(runDir, config, ReportGenerationTimeoutMs);
    }

    private static ReportGenerationResult RunReportGeneration(string runDir, FrameScopeConfig config, int timeoutMs)
    {
        Stopwatch totalTimer = Stopwatch.StartNew();
        var result = new ReportGenerationResult
        {
            Attempted = false,
            ExitCode = -1,
            ReportHtml = Path.Combine(runDir, "charts", FrameScopeReportArtifacts.ReportFileName),
            LogPath = Path.Combine(runDir, "report-generation.log"),
            ProgressPath = Path.Combine(runDir, "report-progress.json"),
            Error = null,
            FrameCount = 0,
            HasFrameData = false,
            ReportKind = "error",
            CanRetry = false
        };

        if (!File.Exists(ReportGeneratorExe))
        {
            result.Error = "Native report generator not found: " + ReportGeneratorExe;
            result.CanRetry = true;
            result.GenerationEndedAt = DateTime.UtcNow;
            FrameScopeReportProgress.Write(result.ProgressPath, "Generation failed", 100, result.Error, DateTime.Now, result.Error, true);
            WriteReportLog(result.LogPath, result.Error);
            WriteFrameScopeLog("report-generate-failed run=" + runDir + " error=" + result.Error);
            return FinishReportGeneration(result, config, totalTimer);
        }

        result.Attempted = true;
        DateTime progressStartedAt = DateTime.Now;
        FrameScopeReportProgress.Write(result.ProgressPath, "Starting", 1, "Starting report generator", progressStartedAt, null, false);
        UpdateStatusFromReportProgress(runDir, result.ProgressPath, result.ReportHtml, result.LogPath);
        WriteFrameScopeLog("report-generate-start run=" + runDir);
        try
        {
            FrameScopeProcessResult process = FrameScopeBoundedProcessRunner.Run(
                ReportGeneratorExe,
                Quote(runDir) + " --progress " + Quote(result.ProgressPath),
                Root,
                timeoutMs,
                delegate { UpdateStatusFromReportProgress(runDir, result.ProgressPath, result.ReportHtml, result.LogPath); });

            result.GenerationStartedAt = process.StartedAtUtc;
            result.GenerationEndedAt = process.EndedAtUtc;
            result.TimedOut = process.TimedOut;
            result.CanRetry = process.CanRetry;
            result.ExitCode = process.ExitCode;
            WriteReportLog(result.LogPath, process.StandardOutput +
                (string.IsNullOrWhiteSpace(process.StandardError) ? "" : Environment.NewLine + process.StandardError));

            if (!process.Started)
            {
                result.Error = string.IsNullOrWhiteSpace(process.Error) ? "Failed to start report generator." : process.Error;
                result.CanRetry = true;
                WriteFailureProgress(result, progressStartedAt, "Generation failed");
            }
            else if (result.TimedOut)
            {
                result.Error = string.IsNullOrWhiteSpace(process.Error) ? "Report generation timed out." : process.Error;
                result.CanRetry = true;
                WriteFailureProgress(result, progressStartedAt, "Generation timed out");
                WriteFrameScopeLog("report-generate-timeout run=" + runDir);
            }
            else if (result.ExitCode != 0)
            {
                result.Error = "Report generation failed with exit code " + result.ExitCode + ".";
                result.CanRetry = true;
                WriteFailureProgress(result, progressStartedAt, "Generation failed");
                WriteFrameScopeLog("report-generate-failed run=" + runDir + " exit=" + result.ExitCode);
            }
            else
            {
                FrameScopeReportArtifactState artifacts = FrameScopeReportArtifacts.Inspect(runDir);
                if (!artifacts.IsComplete)
                {
                    result.Error = "Report generator finished with incomplete artifacts: " + artifacts.Error;
                    result.CanRetry = true;
                    WriteFailureProgress(result, progressStartedAt, "Generation failed");
                    WriteFrameScopeLog("report-generate-incomplete run=" + runDir);
                }
                else
                {
                    result.CanRetry = false;
                    ReadReportManifest(runDir, result);
                    CompleteReportProgress(runDir, result, progressStartedAt);
                }
            }
            UpdateStatusFromReportProgress(runDir, result.ProgressPath, result.ReportHtml, result.LogPath);
        }
        catch (Exception ex)
        {
            result.ExitCode = -1;
            result.Error = ex.Message;
            result.CanRetry = true;
            result.GenerationEndedAt = DateTime.UtcNow;
            WriteFailureProgress(result, progressStartedAt, "Generation failed");
            UpdateStatusFromReportProgress(runDir, result.ProgressPath, result.ReportHtml, result.LogPath);
            WriteReportLog(result.LogPath, ex.ToString());
            WriteFrameScopeLog("report-generate-error run=" + runDir + " error=" + ex.Message);
        }

        return FinishReportGeneration(result, config, totalTimer);
    }

    private static void WriteFailureProgress(ReportGenerationResult result, DateTime startedAt, string phase)
    {
        FrameScopeReportProgress.Write(result.ProgressPath, phase, 100, result.Error, startedAt, result.Error, true);
    }

    private static void CompleteReportProgress(string runDir, ReportGenerationResult result, DateTime startedAt)
    {
        if (string.Equals(result.ReportKind, "diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            result.Error = "No frame data was captured; generated a diagnostic report from auxiliary data.";
            FrameScopeReportProgress.Write(result.ProgressPath, "Completed", 100, result.Error, startedAt, null, false);
            WriteFrameScopeLog("report-generate-diagnostic run=" + runDir + " report=" + result.ReportHtml);
        }
        else if (string.Equals(result.ReportKind, "partial", StringComparison.OrdinalIgnoreCase))
        {
            result.Error = "Frame data was captured, but one or more required auxiliary samplers were unhealthy.";
            FrameScopeReportProgress.Write(result.ProgressPath, "Completed", 100, result.Error, startedAt, null, false);
            WriteFrameScopeLog("report-generate-partial run=" + runDir + " report=" + result.ReportHtml + " frames=" + result.FrameCount);
        }
        else if (string.Equals(result.ReportKind, "full", StringComparison.OrdinalIgnoreCase))
        {
            FrameScopeReportProgress.Write(result.ProgressPath, "Completed", 100, "Report generation completed", startedAt, null, false);
            WriteFrameScopeLog("report-generate-complete run=" + runDir + " report=" + result.ReportHtml + " frames=" + result.FrameCount);
        }
        else
        {
            result.ReportKind = "error";
            result.Error = "No valid frame, process, or system rows were available for this report.";
            FrameScopeReportProgress.Write(result.ProgressPath, "Completed", 100, result.Error, startedAt, null, false);
            WriteFrameScopeLog("report-generate-error-kind run=" + runDir + " report=" + result.ReportHtml);
        }
    }
}
