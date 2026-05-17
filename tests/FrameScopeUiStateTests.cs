using System;
using System.Collections.Generic;

public static class FrameScopeUiStateTests
{
    public static int Main()
    {
        NonLivePageDoesNotStartRefresh();
        LivePageKeyNormalizesToOverview();
        RemovedLivePageDoesNotStartRefresh();
        TargetEditRulesRejectInvalidSampleInterval();
        TargetEditRulesPreserveProcessAliases();
        AddProcessWhileWatcherRunningStopsWithoutRestart();
        ProcessPickerDisplayTextMatchesSelectableAppList();
        ProcessPickerSearchMatchesTitleAndProcessName();
        ProcessPickerSortsByDisplayNameAndProcessName();
        ProcessPickerRecentSortPrioritizesWindowedProcesses();
        ManualProcessNameStillNormalizesForAdd();
        MotionEasingStartsEndsAndStaysMonotonic();
        MotionLerpColorInterpolatesChannels();
        ReportActionsRequireSelectedReport();
        ReportActionsReflectMissingReportFiles();
        Console.WriteLine("FrameScopeUiStateTests: PASS");
        return 0;
    }

    private static void NonLivePageDoesNotStartRefresh()
    {
        FrameScopeLiveRuntimeResult result = FrameScopeLiveRuntime.Resolve(
            "overview",
            new List<FrameScopeLiveRuntimeMonitor>());

        AssertEqual(false, result.RefreshEnabled, "non-live page refresh");
        AssertEqual(true, result.ShouldClearCharts, "non-live page clear");
    }

    private static void LivePageKeyNormalizesToOverview()
    {
        AssertEqual("overview", FrameScopeVisiblePageRules.NormalizeKey("live"), "live key normalization");
        AssertEqual("overview", FrameScopeVisiblePageRules.NormalizeKey(""), "empty key normalization");
        AssertEqual("reports", FrameScopeVisiblePageRules.NormalizeKey("reports"), "reports key preservation");
    }

    private static void RemovedLivePageDoesNotStartRefresh()
    {
        FrameScopeLiveRuntimeResult result = FrameScopeLiveRuntime.Resolve(
            "live",
            new[]
            {
                new FrameScopeLiveRuntimeMonitor
                {
                    Game = "Valorant",
                    ProcessName = "VALORANT-Win64-Shipping.exe",
                    ProcessRunning = true,
                    HasReadableRun = true,
                    RunDir = @"C:\runs\Valorant\latest"
                }
            });

        AssertEqual(false, result.RefreshEnabled, "removed live page refresh");
        AssertEqual(true, result.ShouldClearCharts, "exited game clear");
        AssertEqual(false, result.HasActiveTarget, "removed live page inactive");
    }

    private static void TargetEditRulesRejectInvalidSampleInterval()
    {
        int sampleMs;
        string error;
        AssertEqual(false, FrameScopeTargetEditRules.TryParseSampleInterval("abc", out sampleMs, out error), "reject text sample");
        AssertEqual(false, FrameScopeTargetEditRules.TryParseSampleInterval("49", out sampleMs, out error), "reject too fast sample");
        AssertEqual(true, FrameScopeTargetEditRules.TryParseSampleInterval("50", out sampleMs, out error), "accept lower bound sample");
        AssertEqual(50, sampleMs, "sample lower bound value");
    }

    private static void TargetEditRulesPreserveProcessAliases()
    {
        string value = FrameScopeTargetEditRules.NormalizeProcessName(" TslGame.exe ; TslGame-Win64-Shipping.exe ");
        AssertEqual("TslGame.exe;TslGame-Win64-Shipping.exe", value, "alias preservation");
    }

    private static void AddProcessWhileWatcherRunningStopsWithoutRestart()
    {
        FrameScopeAddProcessPlan plan = FrameScopeTargetEditRules.PlanAddProcess(true);
        AssertEqual(true, plan.ShouldStopWatcherFirst, "add process stop watcher");
        AssertEqual(false, plan.ShouldAutoRestartWatcher, "add process no auto restart");
    }

    private static void ProcessPickerDisplayTextMatchesSelectableAppList()
    {
        FrameScopeProcessPickerItem item = new FrameScopeProcessPickerItem
        {
            ProcessName = "TslGame.exe",
            ProcessId = 1234,
            WindowTitle = "PUBG: BATTLEGROUNDS"
        };

        AssertEqual("PUBG: BATTLEGROUNDS (TslGame.exe)", item.DisplayText, "process picker window title display");

        FrameScopeProcessPickerItem fallback = new FrameScopeProcessPickerItem
        {
            ProcessName = "FrameScopeMonitor.exe",
            ProcessId = 2345,
            WindowTitle = ""
        };

        AssertEqual("FrameScopeMonitor.exe", fallback.DisplayText, "process picker process-name fallback display");
    }

    private static void ProcessPickerSearchMatchesTitleAndProcessName()
    {
        var items = new List<FrameScopeProcessPickerItem>
        {
            new FrameScopeProcessPickerItem { ProcessName = "chrome.exe", ProcessId = 1, WindowTitle = "Docs - Google Chrome" },
            new FrameScopeProcessPickerItem { ProcessName = "TslGame.exe", ProcessId = 2, WindowTitle = "PUBG: BATTLEGROUNDS" },
            new FrameScopeProcessPickerItem { ProcessName = "explorer.exe", ProcessId = 3, WindowTitle = "" }
        };

        AssertEqual(1, FrameScopeProcessPicker.FilterAndSortItems(items, "pubg", FrameScopeProcessPicker.SortRecent).Count, "search title count");
        AssertEqual("TslGame.exe", FrameScopeProcessPicker.FilterAndSortItems(items, "tsl", FrameScopeProcessPicker.SortRecent)[0].ProcessName, "search process name");
        AssertEqual(3, FrameScopeProcessPicker.FilterAndSortItems(items, "", FrameScopeProcessPicker.SortRecent).Count, "empty search count");
    }

    private static void ProcessPickerSortsByDisplayNameAndProcessName()
    {
        var items = new List<FrameScopeProcessPickerItem>
        {
            new FrameScopeProcessPickerItem { ProcessName = "zeta.exe", ProcessId = 1, WindowTitle = "Alpha Window" },
            new FrameScopeProcessPickerItem { ProcessName = "alpha.exe", ProcessId = 2, WindowTitle = "Zulu Window" },
            new FrameScopeProcessPickerItem { ProcessName = "middle.exe", ProcessId = 3, WindowTitle = "" }
        };

        AssertEqual("zeta.exe", FrameScopeProcessPicker.FilterAndSortItems(items, "", FrameScopeProcessPicker.SortName)[0].ProcessName, "sort display name first");
        AssertEqual("alpha.exe", FrameScopeProcessPicker.FilterAndSortItems(items, "", FrameScopeProcessPicker.SortProcessName)[0].ProcessName, "sort process name first");
    }

    private static void ProcessPickerRecentSortPrioritizesWindowedProcesses()
    {
        var items = new List<FrameScopeProcessPickerItem>
        {
            new FrameScopeProcessPickerItem { ProcessName = "background.exe", ProcessId = 1, WindowTitle = "" },
            new FrameScopeProcessPickerItem { ProcessName = "visible.exe", ProcessId = 2, WindowTitle = "Visible App" }
        };

        AssertEqual("visible.exe", FrameScopeProcessPicker.FilterAndSortItems(items, "", FrameScopeProcessPicker.SortRecent)[0].ProcessName, "recent sort window priority");
    }

    private static void ManualProcessNameStillNormalizesForAdd()
    {
        AssertEqual("TslGame.exe", FrameScopeTargetEditRules.NormalizeSingleProcessForAdd("TslGame"), "manual exe suffix");
        AssertEqual("TslGame.exe;TslGame-Win64-Shipping.exe", FrameScopeTargetEditRules.NormalizeSingleProcessForAdd(" TslGame.exe ; TslGame-Win64-Shipping.exe "), "manual aliases unchanged");
    }

    private static void MotionEasingStartsEndsAndStaysMonotonic()
    {
        AssertClose(0f, FrameScopeMotion.EaseOutCubic(0f), "ease-out start");
        AssertClose(1f, FrameScopeMotion.EaseOutCubic(1f), "ease-out end");
        AssertClose(0f, FrameScopeMotion.EaseInOutCubic(0f), "ease-in-out start");
        AssertClose(1f, FrameScopeMotion.EaseInOutCubic(1f), "ease-in-out end");

        float previousOut = FrameScopeMotion.EaseOutCubic(0f);
        float previousInOut = FrameScopeMotion.EaseInOutCubic(0f);
        for (int i = 1; i <= 10; i++)
        {
            float value = i / 10f;
            float nextOut = FrameScopeMotion.EaseOutCubic(value);
            float nextInOut = FrameScopeMotion.EaseInOutCubic(value);
            AssertEqual(true, nextOut >= previousOut, "ease-out monotonic " + i.ToString());
            AssertEqual(true, nextInOut >= previousInOut, "ease-in-out monotonic " + i.ToString());
            previousOut = nextOut;
            previousInOut = nextInOut;
        }
    }

    private static void MotionLerpColorInterpolatesChannels()
    {
        var start = System.Drawing.Color.FromArgb(10, 20, 30, 40);
        var end = System.Drawing.Color.FromArgb(110, 120, 130, 140);
        var middle = FrameScopeMotion.LerpColor(start, end, 0.5f);
        AssertEqual(60, middle.A, "lerp color alpha");
        AssertEqual(70, middle.R, "lerp color red");
        AssertEqual(80, middle.G, "lerp color green");
        AssertEqual(90, middle.B, "lerp color blue");
    }

    private static void ReportActionsRequireSelectedReport()
    {
        FrameScopeReportActionAvailability availability = FrameScopeReportActionRules.ResolveAvailability(false, false, false);
        AssertEqual(false, availability.CanOpenFolder, "no report open folder");
        AssertEqual(false, availability.CanOpenReport, "no report open html");
        AssertEqual(false, availability.CanOpenDetailedReport, "no report detail");
        AssertEqual(false, availability.CanRegenerateReport, "no report regenerate");
    }

    private static void ReportActionsReflectMissingReportFiles()
    {
        FrameScopeReportActionAvailability missingHtml = FrameScopeReportActionRules.ResolveAvailability(true, false, true);
        AssertEqual(true, missingHtml.CanOpenFolder, "missing html open folder");
        AssertEqual(false, missingHtml.CanOpenReport, "missing html open report");
        AssertEqual(true, missingHtml.CanOpenDetailedReport, "missing html detail report");
        AssertEqual(true, missingHtml.CanRegenerateReport, "missing html regenerate");

        FrameScopeReportActionAvailability missingRunDir = FrameScopeReportActionRules.ResolveAvailability(true, true, false);
        AssertEqual(true, missingRunDir.CanOpenFolder, "missing run dir open folder");
        AssertEqual(true, missingRunDir.CanOpenReport, "missing run dir open report");
        AssertEqual(false, missingRunDir.CanOpenDetailedReport, "missing run dir detail report");
        AssertEqual(false, missingRunDir.CanRegenerateReport, "missing run dir regenerate");
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }

    private static void AssertClose(float expected, float actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.0001f)
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }
}
