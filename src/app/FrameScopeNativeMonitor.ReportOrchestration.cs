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
using System.Threading;
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
            var candidates = new List<DirectoryInfo>();
            foreach (var gameDir in root.GetDirectories())
            {
                candidates.AddRange(gameDir.GetDirectories()
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .Take(3)
                    .Where(run =>
                        !FrameScopeReportArtifacts.Inspect(run.FullName).IsComplete &&
                        FrameScopeReportArtifacts.HasUsableMonitorData(run.FullName) &&
                        DateTime.Now - LatestMonitorCsvWriteTime(run.FullName) > TimeSpan.FromMinutes(2)));
            }

            foreach (var run in candidates.OrderByDescending(d => d.LastWriteTimeUtc).Take(5))
            {
                var status = ReadStatusDictionary(run.FullName);
                var phase = StatusString(status, "Phase", "");
                FrameScopeReportArtifactState artifacts = FrameScopeReportArtifacts.Inspect(run.FullName);
                if (FrameScopeReportRecoveryPolicy.ShouldRecover(
                    phase,
                    FrameScopeReportArtifacts.HasUsableMonitorData(run.FullName),
                    artifacts.IsComplete,
                    false))
                {
                    WriteFrameScopeLog("recover-stale-report run=" + run.FullName + " phase=" + phase);
                    EnsureReportForCompletedRun(run.FullName, status, StatusInt(status, "ExitCode", -1), config);
                }
            }
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("recover-stale-report-error " + ex.Message);
        }
    }

    private static Dictionary<string, object> EnsureReportForCompletedRun(string runDir, Dictionary<string, object> status, int monitorExitCode, FrameScopeConfig config)
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
            WriteFrameScopeLog("report-generate-skip missing-monitor-data run=" + runDir);
            return UpdateStatusAfterReportGeneration(runDir, status, new ReportGenerationResult
            {
                Attempted = false,
                ExitCode = -1,
                ReportHtml = reportHtml,
                LogPath = Path.Combine(runDir, "report-generation.log"),
                ProgressPath = Path.Combine(runDir, "report-progress.json"),
                Error = "No monitor CSV data was found.",
                ReportKind = "error"
            }, monitorExitCode);
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
        return UpdateStatusAfterReportGeneration(runDir, status, result, monitorExitCode);
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

    private static ReportGenerationResult RunReportGenerationLegacy(string runDir, FrameScopeConfig config = null)
    {
        var totalTimer = Stopwatch.StartNew();
        var result = new ReportGenerationResult
        {
            Attempted = false,
            ExitCode = -1,
            ReportHtml = Path.Combine(runDir, "charts", "framescope-interactive-report.html"),
            LogPath = Path.Combine(runDir, "report-generation.log"),
            ProgressPath = Path.Combine(runDir, "report-progress.json"),
            Error = null,
            FrameCount = 0,
            HasFrameData = false,
            ReportKind = "error"
        };

        if (!File.Exists(ReportGeneratorExe))
        {
            result.Error = "Native report generator not found: " + ReportGeneratorExe;
            FrameScopeReportProgress.Write(result.ProgressPath, "生成失败", 100, result.Error, DateTime.Now, result.Error, true);
            WriteReportLog(result.LogPath, result.Error);
            WriteFrameScopeLog("report-generate-failed run=" + runDir + " error=" + result.Error);
            return FinishReportGeneration(result, config, totalTimer);
        }

        result.Attempted = true;
        var progressStartedAt = DateTime.Now;
        FrameScopeReportProgress.Write(result.ProgressPath, "启动生成", 1, "启动报告生成器", progressStartedAt, null, false);
        UpdateStatusFromReportProgress(runDir, result.ProgressPath, result.ReportHtml, result.LogPath);
        WriteFrameScopeLog("report-generate-start run=" + runDir);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ReportGeneratorExe,
                Arguments = Quote(runDir) + " --progress " + Quote(result.ProgressPath),
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(psi))
            {
                if (process == null)
                {
                    result.Error = "Failed to start report generator.";
                    WriteReportLog(result.LogPath, result.Error);
                    return FinishReportGeneration(result, config, totalTimer);
                }
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch { }

                string output = "";
                string error = "";
                var outputThread = new Thread(new ThreadStart(delegate
                {
                    try { output = process.StandardOutput.ReadToEnd(); }
                    catch { }
                }));
                var errorThread = new Thread(new ThreadStart(delegate
                {
                    try { error = process.StandardError.ReadToEnd(); }
                    catch { }
                }));
                outputThread.IsBackground = true;
                errorThread.IsBackground = true;
                outputThread.Start();
                errorThread.Start();

                while (!process.WaitForExit(250))
                {
                    UpdateStatusFromReportProgress(runDir, result.ProgressPath, result.ReportHtml, result.LogPath);
                }
                try { outputThread.Join(5000); }
                catch { }
                try { errorThread.Join(5000); }
                catch { }
                result.ExitCode = process.ExitCode;
                WriteReportLog(result.LogPath, output + (string.IsNullOrWhiteSpace(error) ? "" : Environment.NewLine + error));
                if (result.ExitCode != 0)
                {
                    result.Error = "Report generation failed with exit code " + result.ExitCode + ".";
                    FrameScopeReportProgress.Write(result.ProgressPath, "生成失败", 100, result.Error, progressStartedAt, result.Error, true);
                    WriteFrameScopeLog("report-generate-failed run=" + runDir + " exit=" + result.ExitCode);
                }
                else if (!File.Exists(result.ReportHtml))
                {
                    result.Error = "Report generator finished but report html was not created.";
                    FrameScopeReportProgress.Write(result.ProgressPath, "生成失败", 100, result.Error, progressStartedAt, result.Error, true);
                    WriteFrameScopeLog("report-generate-missing-html run=" + runDir);
                }
                else
                {
                    ReadReportManifest(runDir, result);
                    if (string.Equals(result.ReportKind, "diagnostic", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Error = "No frame data was captured by PresentMon; generated diagnostic report from process/system data.";
                        FrameScopeReportProgress.Write(result.ProgressPath, "完成", 100, result.Error, progressStartedAt, null, false);
                        WriteFrameScopeLog("report-generate-diagnostic run=" + runDir + " report=" + result.ReportHtml);
                    }
                    else if (string.Equals(result.ReportKind, "partial", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Error = "Frame data was captured, but one or more required auxiliary samplers were unhealthy.";
                        FrameScopeReportProgress.Write(result.ProgressPath, "Completed", 100, result.Error, progressStartedAt, null, false);
                        WriteFrameScopeLog("report-generate-partial run=" + runDir + " report=" + result.ReportHtml + " frames=" + result.FrameCount);
                    }
                    else if (string.Equals(result.ReportKind, "full", StringComparison.OrdinalIgnoreCase))
                    {
                        FrameScopeReportProgress.Write(result.ProgressPath, "完成", 100, "报告生成完成", progressStartedAt, null, false);
                        WriteFrameScopeLog("report-generate-complete run=" + runDir + " report=" + result.ReportHtml + " frames=" + result.FrameCount);
                    }
                    else
                    {
                        result.ReportKind = "error";
                        result.Error = "No valid frame, process, or system rows were available for this report.";
                        FrameScopeReportProgress.Write(result.ProgressPath, "Completed", 100, result.Error, progressStartedAt, null, false);
                        WriteFrameScopeLog("report-generate-error-kind run=" + runDir + " report=" + result.ReportHtml);
                    }
                }
                UpdateStatusFromReportProgress(runDir, result.ProgressPath, result.ReportHtml, result.LogPath);
            }
        }
        catch (Exception ex)
        {
            result.ExitCode = -1;
            result.Error = ex.Message;
            FrameScopeReportProgress.Write(result.ProgressPath, "生成失败", 100, result.Error, progressStartedAt, result.Error, true);
            UpdateStatusFromReportProgress(runDir, result.ProgressPath, result.ReportHtml, result.LogPath);
            WriteReportLog(result.LogPath, ex.ToString());
            WriteFrameScopeLog("report-generate-error run=" + runDir + " error=" + ex.Message);
        }

        return FinishReportGeneration(result, config, totalTimer);
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
