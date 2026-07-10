using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

internal static partial class FrameScopeNativeMonitor
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private static readonly string HistoryPath = Path.Combine(Path.GetTempPath(), "framescope-report-status-history-unused.jsonl");

    public static int Main()
    {
        try
        {
            ReportCompletionKeepsTopLevelAndNestedSummaryInSync();
            Console.WriteLine("FrameScopeReportStatusTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
    }

    private static void ReportCompletionKeepsTopLevelAndNestedSummaryInSync()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-report-status-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string summaryPath = Path.Combine(dir, "summary.json");
            File.WriteAllText(summaryPath, Json.Serialize(new Dictionary<string, object>
            {
                { "RunDir", dir },
                { "Reports", new Dictionary<string, object>
                    {
                        { "Attempted", false },
                        { "ExitCode", null },
                        { "ReportHtml", "old.html" },
                        { "PreviewPng", "preview.png" },
                        { "LogPath", "old.log" },
                        { "Error", null },
                        { "Unrelated", "keep-me" }
                    }
                }
            }), Encoding.UTF8);

            ReportGenerationResult result = new ReportGenerationResult
            {
                Attempted = true,
                ExitCode = 0,
                ReportHtml = Path.Combine(dir, "charts", "framescope-interactive-report.html"),
                LogPath = Path.Combine(dir, "report-generation.log"),
                ProgressPath = Path.Combine(dir, "report-progress.json"),
                Error = "auxiliary sampler unhealthy",
                FrameCount = 120,
                ProcessSampleCount = 4,
                SystemSampleCount = 3,
                HasFrameData = true,
                ReportKind = "partial",
                GenerationStartedAt = new DateTime(2026, 7, 11, 1, 2, 3, DateTimeKind.Utc),
                GenerationEndedAt = new DateTime(2026, 7, 11, 1, 2, 5, DateTimeKind.Utc),
                TimedOut = true,
                CanRetry = true
            };
            result.SamplerEvidenceFields["ProcessSamplerStatus"] = "healthy";
            result.SamplerEvidenceFields["ProcessSamplerValidRows"] = 8;
            result.SamplerEvidenceFields["SystemSamplerStatus"] = "failed";
            result.SamplerEvidenceFields["SystemSamplerValidRows"] = 0;

            Dictionary<string, object> status = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "Phase", "done" },
                { "SummaryPath", summaryPath }
            };
            Dictionary<string, object> updated = UpdateStatusAfterReportGeneration(dir, status, result, 0);
            Dictionary<string, object> summary = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(summaryPath, Encoding.UTF8));
            Dictionary<string, object> reports = (Dictionary<string, object>)summary["Reports"];

            AssertEqual("partial", Convert.ToString(updated["ReportKind"]), "status report kind");
            AssertEqual("partial", Convert.ToString(summary["ReportKind"]), "summary report kind");
            AssertEqual(true, Convert.ToBoolean(reports["Attempted"]), "nested attempted");
            AssertEqual("partial", Convert.ToString(reports["ReportKind"]), "nested report kind");
            AssertEqual(0, Convert.ToInt32(reports["ExitCode"]), "nested exit code");
            AssertEqual(result.ReportHtml, Convert.ToString(reports["ReportHtml"]), "nested report html");
            AssertEqual(result.LogPath, Convert.ToString(reports["LogPath"]), "nested log path");
            AssertEqual(result.Error, Convert.ToString(reports["Error"]), "nested error");
            AssertEqual(true, Convert.ToBoolean(reports["HasFrameData"]), "nested frame availability");
            AssertEqual(120, Convert.ToInt32(reports["FrameCount"]), "nested frame count");
            AssertEqual(4, Convert.ToInt32(reports["ProcessSampleCount"]), "nested process sample count");
            AssertEqual(3, Convert.ToInt32(reports["SystemSampleCount"]), "nested system sample count");
            AssertEqual("healthy", Convert.ToString(reports["ProcessSamplerStatus"]), "nested process sampler status");
            AssertEqual(8, Convert.ToInt32(reports["ProcessSamplerValidRows"]), "nested process sampler rows");
            AssertEqual("failed", Convert.ToString(reports["SystemSamplerStatus"]), "nested system sampler status");
            AssertEqual(0, Convert.ToInt32(reports["SystemSamplerValidRows"]), "nested system sampler rows");
            AssertEqual(true, Convert.ToBoolean(updated["ReportGenerationTimedOut"]), "status timeout");
            AssertEqual(true, Convert.ToBoolean(updated["ReportCanRetry"]), "status retry");
            AssertEqual(result.GenerationStartedAt.ToString("o"), Convert.ToString(reports["StartedAt"]), "nested started at");
            AssertEqual(result.GenerationEndedAt.ToString("o"), Convert.ToString(reports["EndedAt"]), "nested ended at");
            AssertEqual(true, Convert.ToBoolean(reports["TimedOut"]), "nested timeout");
            AssertEqual(true, Convert.ToBoolean(reports["CanRetry"]), "nested retry");
            AssertEqual(Convert.ToBoolean(summary["ReportGenerationAttempted"]), Convert.ToBoolean(reports["Attempted"]), "top-level and nested attempted agree");
            AssertEqual(Convert.ToInt32(summary["ReportGenerationExitCode"]), Convert.ToInt32(reports["ExitCode"]), "top-level and nested exit code agree");
            AssertEqual(Convert.ToInt32(summary["ReportFrameCount"]), Convert.ToInt32(reports["FrameCount"]), "top-level and nested frame count agree");
            AssertEqual(Convert.ToInt32(summary["ReportProcessSampleCount"]), Convert.ToInt32(reports["ProcessSampleCount"]), "top-level and nested process samples agree");
            AssertEqual(Convert.ToInt32(summary["ReportSystemSampleCount"]), Convert.ToInt32(reports["SystemSampleCount"]), "top-level and nested system samples agree");
            AssertEqual(Convert.ToString(summary["ProcessSamplerStatus"]), Convert.ToString(reports["ProcessSamplerStatus"]), "top-level and nested process health agree");
            AssertEqual(Convert.ToString(summary["SystemSamplerStatus"]), Convert.ToString(reports["SystemSamplerStatus"]), "top-level and nested system health agree");
            AssertEqual("preview.png", Convert.ToString(reports["PreviewPng"]), "nested preview preserved");
            AssertEqual("keep-me", Convert.ToString(reports["Unrelated"]), "nested unrelated member preserved");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void WriteFrameScopeLog(string message)
    {
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }
}
