using System;
using System.Collections.Generic;
using System.Linq;

public static class FrameScopeCapturePlannerTests
{
    public static int Main()
    {
        PubgAliasesIncludeShippingProcess();
        PubgUsesProcessNameCaptureAcrossAliases();
        NonPubgSingleProcessUsesPidCapture();
        TargetNotFoundDiagnosticIsActionable();
        EmptyCaptureRecoveryIsBoundedAndPreservesTarget();
        Console.WriteLine("FrameScopeCapturePlannerTests: PASS");
        return 0;
    }

    private static void PubgAliasesIncludeShippingProcess()
    {
        List<string> aliases = FrameScopeCapturePlanner.BuildTargetProcessBaseNames("TslGame.exe", "PUBG: BATTLEGROUNDS");

        AssertSequence(new[] { "TslGame", "TslGame-Win64-Shipping" }, aliases, "PUBG aliases");
    }

    private static void PubgUsesProcessNameCaptureAcrossAliases()
    {
        List<string> aliases = FrameScopeCapturePlanner.BuildTargetProcessBaseNames("TslGame.exe", "PUBG: BATTLEGROUNDS");
        FrameScopePresentMonPlan plan = FrameScopeCapturePlanner.CreatePresentMonPlan(
            aliases,
            "TslGame.exe",
            "PUBG: BATTLEGROUNDS",
            4242,
            @"C:\run\presentmon.csv",
            "FrameScopeNativePresentMon_PUBG",
            false,
            0);

        AssertEqual("process_name", plan.CaptureMode, "PUBG capture mode");
        AssertEqual("TslGame.exe;TslGame-Win64-Shipping.exe", plan.CaptureTarget, "PUBG capture target");
        AssertTrue(ContainsPair(plan.Arguments, "--process_name", "TslGame.exe"), "TslGame process_name arg");
        AssertTrue(ContainsPair(plan.Arguments, "--process_name", "TslGame-Win64-Shipping.exe"), "Shipping process_name arg");
        AssertTrue(!plan.Arguments.Contains("--process_id"), "PUBG should not be locked to transient pid");
    }

    private static void NonPubgSingleProcessUsesPidCapture()
    {
        List<string> aliases = FrameScopeCapturePlanner.BuildTargetProcessBaseNames("cs2.exe", "Counter-Strike 2");
        FrameScopePresentMonPlan plan = FrameScopeCapturePlanner.CreatePresentMonPlan(
            aliases,
            "cs2.exe",
            "Counter-Strike 2",
            1234,
            @"C:\run\presentmon.csv",
            "FrameScopeNativePresentMon_CS2",
            true,
            8);

        AssertEqual("process_id", plan.CaptureMode, "single process capture mode");
        AssertEqual("1234", plan.CaptureTarget, "single process capture target");
        AssertTrue(ContainsPair(plan.Arguments, "--process_id", "1234"), "pid arg");
        AssertTrue(ContainsPair(plan.Arguments, "--timed", "8"), "timed arg");
    }

    private static void TargetNotFoundDiagnosticIsActionable()
    {
        Dictionary<string, object> diagnostic = FrameScopeCapturePlanner.CreateTargetNotFoundDiagnostic(
            new[] { "TslGame", "TslGame-Win64-Shipping" },
            4242,
            "PUBG: BATTLEGROUNDS",
            15);

        AssertEqual("target-not-found", Convert.ToString(diagnostic["FrameCaptureStatus"]), "not found status");
        AssertEqual("waiting-timeout", Convert.ToString(diagnostic["WindowWaitStatus"]), "window wait status");
        AssertTrue(Convert.ToString(diagnostic["FrameCaptureMessage"]).Contains("PUBG"), "message names PUBG");
        AssertTrue(Convert.ToString(diagnostic["FrameCaptureMessage"]).Contains("管理员"), "message mentions elevation");
        AssertTrue(Convert.ToString(diagnostic["FrameCaptureMessage"]).Contains("无边框"), "message mentions borderless");
    }

    private static void EmptyCaptureRecoveryIsBoundedAndPreservesTarget()
    {
        AssertTrue(FrameScopeCapturePlanner.ShouldRecoverStalledCapture(8000, null, false), "recover a collector which never produces a frame");
        AssertTrue(FrameScopeCapturePlanner.ShouldRecoverStalledCapture(90000, 2100, false), "recover after an initially healthy collector stops producing");
        AssertTrue(!FrameScopeCapturePlanner.ShouldRecoverStalledCapture(90000, 100, false), "retain genuinely fresh full frames");
        AssertTrue(!FrameScopeCapturePlanner.ShouldRecoverStalledCapture(7999, null, false), "allow initial startup grace");
        AssertTrue(!FrameScopeCapturePlanner.ShouldRecoverStalledCapture(90000, 9000, true), "recovery remains one-way");
        var plan = FrameScopeCapturePlanner.CreatePresentMonPlan(new List<string> { "cs2" }, "cs2.exe", "CS2", 1234,
            "presentmon.csv", "owned-session", true, 7, true, true);
        AssertTrue(ContainsPair(plan.Arguments, "--process_id", "1234"), "preserve selected pid");
        AssertTrue(ContainsPair(plan.Arguments, "--timed", "7"), "use remaining capture duration");
        foreach (string flag in new[] { "--v1_metrics", "--no_track_gpu", "--no_track_input", "--no_track_display", "--output_stdout" })
            AssertTrue(plan.Arguments.Contains(flag), "runtime recovery flag " + flag);
        var normal = FrameScopeCapturePlanner.CreatePresentMonPlan(new List<string> { "cs2" }, "cs2.exe", "CS2", 1234,
            "presentmon.csv", "owned-session", false, 0, true);
        AssertTrue(!normal.Arguments.Contains("--no_track_display"), "keep full tracing as default");
    }

    private static bool ContainsPair(List<string> values, string key, string value)
    {
        for (int i = 0; i < values.Count - 1; i++)
        {
            if (values[i] == key && values[i + 1] == value) return true;
        }
        return false;
    }

    private static void AssertSequence(IEnumerable<string> expected, IEnumerable<string> actual, string label)
    {
        string left = string.Join("|", expected.ToArray());
        string right = string.Join("|", actual.ToArray());
        if (!left.Equals(right, StringComparison.Ordinal))
        {
            throw new Exception(label + ": expected <" + left + "> but got <" + right + ">");
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
