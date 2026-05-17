using System;
using System.Collections.Generic;
using System.IO;

public static class FrameScopeReportProgressTests
{
    public static int Main()
    {
        CreatesStatusFieldsWithEta();
        WritesAndReadsProgressJson();
        FindsFreshProgressInsteadOfTouchedStaleStatus();
        Console.WriteLine("FrameScopeReportProgressTests: PASS");
        return 0;
    }

    private static void CreatesStatusFieldsWithEta()
    {
        DateTime start = DateTime.Now.AddSeconds(-10);
        Dictionary<string, object> fields = FrameScopeReportProgress.CreateFields("降采样", 50, "正在压缩图表点", start, null, false);

        AssertEqual("降采样", Convert.ToString(fields["ReportProgressPhase"]), "phase");
        AssertEqual(50, Convert.ToInt32(fields["ReportProgressPercent"]), "percent");
        AssertEqual("正在压缩图表点", Convert.ToString(fields["ReportProgressMessage"]), "message");
        AssertTrue(Convert.ToInt32(fields["ReportProgressEtaSeconds"]) >= 0, "eta should be non-negative");
        AssertEqual(false, Convert.ToBoolean(fields["ReportCanRetry"]), "retry");
    }

    private static void WritesAndReadsProgressJson()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-progress-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "report-progress.json");
        try
        {
            FrameScopeReportProgress.Write(path, "生成图表", 75, "写入 HTML", DateTime.Now.AddSeconds(-3), null, false);
            Dictionary<string, object> fields = FrameScopeReportProgress.Read(path);

            AssertEqual("生成图表", Convert.ToString(fields["ReportProgressPhase"]), "written phase");
            AssertEqual(75, Convert.ToInt32(fields["ReportProgressPercent"]), "written percent");
            AssertEqual("写入 HTML", Convert.ToString(fields["ReportProgressMessage"]), "written message");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void FindsFreshProgressInsteadOfTouchedStaleStatus()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-progress-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Local);
            string staleRun = Path.Combine(dir, "Game", "OldRun");
            string freshRun = Path.Combine(dir, "Game", "FreshRun");
            Directory.CreateDirectory(staleRun);
            Directory.CreateDirectory(freshRun);

            string staleStatus = Path.Combine(staleRun, "status.json");
            File.WriteAllText(staleStatus,
                "{\"ReportProgressPhase\":\"完成\",\"ReportProgressPercent\":100,\"ReportProgressMessage\":\"old\",\"ReportProgressUpdatedAt\":\"" +
                now.AddHours(-2).ToString("o") + "\"}");
            File.SetLastWriteTime(staleStatus, now);

            string freshStatus = Path.Combine(freshRun, "status.json");
            File.WriteAllText(freshStatus,
                "{\"ReportProgressPhase\":\"生成图表\",\"ReportProgressPercent\":45,\"ReportProgressMessage\":\"fresh\",\"ReportProgressUpdatedAt\":\"" +
                now.AddMinutes(-1).ToString("o") + "\"}");
            File.SetLastWriteTime(freshStatus, now.AddMinutes(-5));

            Dictionary<string, object> fields = FrameScopeReportProgress.FindLatestEffectiveStatus(dir, now);

            AssertEqual("生成图表", Convert.ToString(fields["ReportProgressPhase"]), "fresh phase selected");
            AssertEqual(45, Convert.ToInt32(fields["ReportProgressPercent"]), "fresh percent selected");
            AssertEqual(freshRun, Convert.ToString(fields["ReportProgressRunDir"]), "fresh run selected");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
    }
}
