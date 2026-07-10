using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

internal static class FrameScopeReportRecoveryTests
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    private static int Main()
    {
        try
        {
            HtmlAloneIsIncomplete();
            MissingDataIsIncomplete();
            CorruptManifestIsIncomplete();
            ManifestPathEscapeIsIncomplete();
            CompleteArtifactSetPasses();
            MonitorCsvRequiresADataRow();
            RecoveryPolicyHonorsPhaseAndActivity();
            Console.WriteLine("FrameScopeReportRecoveryTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void HtmlAloneIsIncomplete()
    {
        string run = CreateRun("html-only");
        try
        {
            string charts = Path.Combine(run, "charts");
            Directory.CreateDirectory(charts);
            File.WriteAllText(Path.Combine(charts, "framescope-interactive-report.html"), "<html></html>");
            AssertFalse(FrameScopeReportArtifacts.Inspect(run).IsComplete, "HTML alone cannot complete a report");
        }
        finally { DeleteFixture(run); }
    }

    private static void MissingDataIsIncomplete()
    {
        string run = CreateRun("missing-data");
        try
        {
            WriteArtifactSet(run);
            File.Delete(Path.Combine(run, "charts", "framescope-interactive-data.js"));
            AssertFalse(FrameScopeReportArtifacts.Inspect(run).IsComplete, "missing data.js");
        }
        finally { DeleteFixture(run); }
    }

    private static void CorruptManifestIsIncomplete()
    {
        string run = CreateRun("corrupt-manifest");
        try
        {
            WriteArtifactSet(run);
            File.WriteAllText(Path.Combine(run, "charts", "framescope-interactive-manifest.json"), "{not-json");
            AssertFalse(FrameScopeReportArtifacts.Inspect(run).IsComplete, "corrupt manifest");
        }
        finally { DeleteFixture(run); }
    }

    private static void ManifestPathEscapeIsIncomplete()
    {
        string run = CreateRun("escaped-manifest");
        try
        {
            WriteArtifactSet(run, Path.Combine(Path.GetTempPath(), "foreign-report.html"), null);
            AssertFalse(FrameScopeReportArtifacts.Inspect(run).IsComplete, "manifest path escape");
        }
        finally { DeleteFixture(run); }
    }

    private static void CompleteArtifactSetPasses()
    {
        string run = CreateRun("complete");
        try
        {
            WriteArtifactSet(run);
            FrameScopeReportArtifactState state = FrameScopeReportArtifacts.Inspect(run);
            AssertTrue(state.IsComplete, state.Error);
        }
        finally { DeleteFixture(run); }
    }

    private static void MonitorCsvRequiresADataRow()
    {
        string run = CreateRun("csv-rows");
        try
        {
            File.WriteAllText(Path.Combine(run, "presentmon.csv"), "Application,MsBetweenPresents" + Environment.NewLine);
            AssertFalse(FrameScopeReportArtifacts.HasUsableMonitorData(run), "header-only CSV is not usable");
            File.WriteAllText(Path.Combine(run, "process-samples.csv"), "Time,Pid" + Environment.NewLine + "2026-07-11T00:00:00Z,42" + Environment.NewLine);
            AssertTrue(FrameScopeReportArtifacts.HasUsableMonitorData(run), "a data row is usable");
        }
        finally { DeleteFixture(run); }
    }

    private static void RecoveryPolicyHonorsPhaseAndActivity()
    {
        AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("done", true, false, false), "done crash window");
        AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("finalizing", true, false, false), "finalizing crash window");
        AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("error", true, false, false), "error retry");
        AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("", true, false, false), "legacy empty phase");
        AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("capturing", true, false, false), "stale inactive capture");
        AssertFalse(FrameScopeReportRecoveryPolicy.ShouldRecover("capturing", true, false, true), "active capture");
        AssertFalse(FrameScopeReportRecoveryPolicy.ShouldRecover("done", false, false, false), "no usable data");
        AssertFalse(FrameScopeReportRecoveryPolicy.ShouldRecover("done", true, true, false), "already complete");
        AssertFalse(FrameScopeReportRecoveryPolicy.ShouldRecover("starting", true, false, false), "non-recoverable phase");
    }

    private static string CreateRun(string name)
    {
        string run = Path.Combine(Path.GetTempPath(), "FrameScopeReportRecoveryTests", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        return run;
    }

    private static void WriteArtifactSet(string run, string reportOverride = null, string dataOverride = null)
    {
        string charts = Path.Combine(run, "charts");
        string report = Path.Combine(charts, "framescope-interactive-report.html");
        string data = Path.Combine(charts, "framescope-interactive-data.js");
        Directory.CreateDirectory(charts);
        File.WriteAllText(report, "<html></html>");
        File.WriteAllText(data, "window.__FRAMESCOPE_REPORT__ = {};");
        File.WriteAllText(Path.Combine(charts, "framescope-interactive-manifest.json"), Json.Serialize(new Dictionary<string, object>
        {
            { "report", reportOverride ?? report },
            { "data", dataOverride ?? data },
            { "frames", 1 }
        }));
    }

    private static void DeleteFixture(string run)
    {
        if (Directory.Exists(run)) Directory.Delete(run, true);
    }

    private static void AssertTrue(bool value, string message)
    {
        if (!value) throw new Exception("ASSERT TRUE FAILED: " + message);
    }

    private static void AssertFalse(bool value, string message)
    {
        if (value) throw new Exception("ASSERT FALSE FAILED: " + message);
    }
}
