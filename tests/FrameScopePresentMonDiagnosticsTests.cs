using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class FrameScopePresentMonDiagnosticsTests
{
    public static int Main()
    {
        AccessDeniedStderrClassifiesEtwPermissionFailure();
        SilentNoCsvExitZeroClassifiesWithoutPubgGuidance();
        PreflightRecordsElevationGroupAndToolState();
        Console.WriteLine("FrameScopePresentMonDiagnosticsTests: PASS");
        return 0;
    }

    private static void AccessDeniedStderrClassifiesEtwPermissionFailure()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-presentmon-diag-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string csv = Path.Combine(dir, "presentmon.csv");
            string stdout = Path.Combine(dir, "presentmon.stdout.log");
            string stderr = Path.Combine(dir, "presentmon.stderr.log");
            File.WriteAllText(stdout, "", Encoding.UTF8);
            File.WriteAllText(stderr,
                "error: failed to start trace session: access denied.\r\n"
                + "       PresentMon requires either administrative privileges or to be run by a user in the\r\n"
                + "       \"Performance Log Users\" user group.  View the readme for more details.",
                Encoding.UTF8);

            Dictionary<string, object> diagnostics = FrameScopePresentMonDiagnostics.BuildCaptureDiagnostics(
                csv,
                stdout,
                stderr,
                6,
                true,
                false);

            AssertEqual("presentmon-etw-access-denied", Convert.ToString(diagnostics["FrameCaptureStatus"]), "capture status");
            AssertEqual(false, Convert.ToBoolean(diagnostics["PresentMonCsvExists"]), "csv exists");
            AssertEqual(0L, Convert.ToInt64(diagnostics["PresentMonCsvBytes"]), "csv bytes");
            AssertEqual(0, Convert.ToInt32(diagnostics["PresentMonCsvRows"]), "csv rows");
            AssertTrue(Convert.ToString(diagnostics["FrameCaptureMessage"]).Contains("ETW trace"), "message mentions ETW trace");
            AssertTrue(Convert.ToString(diagnostics["FrameCaptureMessage"]).Contains("Performance Log Users"), "message mentions PLU");
            AssertTrue(Convert.ToString(diagnostics["PresentMonFailureCategory"]).Contains("access-denied"), "failure category");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void SilentNoCsvExitZeroClassifiesWithoutPubgGuidance()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-presentmon-silent-no-csv-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string csv = Path.Combine(dir, "presentmon.csv");
            string stdout = Path.Combine(dir, "presentmon.stdout.log");
            string stderr = Path.Combine(dir, "presentmon.stderr.log");
            File.WriteAllText(stdout, "Started recording.\r\n", Encoding.UTF8);
            File.WriteAllText(stderr, "", Encoding.UTF8);

            Dictionary<string, object> diagnostics = FrameScopePresentMonDiagnostics.BuildCaptureDiagnostics(
                csv,
                stdout,
                stderr,
                0,
                false,
                false);

            AssertEqual("presentmon-no-csv-silent", Convert.ToString(diagnostics["FrameCaptureStatus"]), "silent no-csv capture status");
            AssertEqual("presentmon-no-csv-silent", Convert.ToString(diagnostics["PresentMonFailureCategory"]), "silent no-csv failure category");
            AssertEqual(false, Convert.ToBoolean(diagnostics["PresentMonEtwAccessDenied"]), "silent no-csv should not be ETW access denied");
            AssertTrue(Convert.ToString(diagnostics["PresentMonStdoutTail"]).Contains("Started recording"), "stdout tail should be retained");
            AssertEqual("", Convert.ToString(diagnostics["PresentMonStderrTail"]), "stderr tail should stay empty");
            string message = Convert.ToString(diagnostics["FrameCaptureMessage"]);
            AssertTrue(message.IndexOf("PUBG", StringComparison.OrdinalIgnoreCase) < 0, "silent no-csv message must not mention PUBG");
            AssertTrue(message.IndexOf("presentmon.csv", StringComparison.OrdinalIgnoreCase) >= 0, "silent no-csv message should mention missing CSV");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void PreflightRecordsElevationGroupAndToolState()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-presentmon-preflight-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string tool = Path.Combine(dir, "PresentMon.exe");
            File.WriteAllText(tool, "fake", Encoding.UTF8);
            Dictionary<string, object> preflight = FrameScopePresentMonDiagnostics.BuildPreflightDiagnostics(tool);

            AssertTrue(preflight.ContainsKey("PresentMonPreflightIsElevated"), "elevation field");
            AssertTrue(preflight.ContainsKey("PresentMonPreflightInPerformanceLogUsers"), "PLU field");
            AssertTrue(preflight.ContainsKey("PresentMonPreflightToolExists"), "tool field");
            AssertEqual(true, Convert.ToBoolean(preflight["PresentMonPreflightToolExists"]), "tool exists");
            AssertEqual(tool, Convert.ToString(preflight["PresentMonPreflightToolPath"]), "tool path");
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
