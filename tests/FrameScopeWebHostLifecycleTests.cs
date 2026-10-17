using System;
using System.Collections.Generic;

public static class FrameScopeWebHostLifecycleTests
{
    public static int Main()
    {
        try
        {
            UserCloseHidesOnlyWhenTrayMinimizeIsEnabled();
            ExplicitCloseAndDisposingAreNeverTrapped();
            ActiveMonitoringAlwaysRequiresExitConfirmation();
            EveryNonHiddenCloseStopsBeforeClosing();
            Console.WriteLine("FrameScopeWebHostLifecycleTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
    }

    private static void UserCloseHidesOnlyWhenTrayMinimizeIsEnabled()
    {
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
        config.CloseWindowBehavior = "minimize-to-tray";
        config.TrayEnabled = true;

        AssertEqual(true, FrameScopeWebHostLifecycle.ShouldHideOnUserClose(config, false, false, true), "minimize-to-tray user close");

        config.CloseWindowBehavior = "exit";
        AssertEqual(false, FrameScopeWebHostLifecycle.ShouldHideOnUserClose(config, false, false, true), "exit user close");

        config.CloseWindowBehavior = "minimize-to-tray";
        config.TrayEnabled = false;
        AssertEqual(false, FrameScopeWebHostLifecycle.ShouldHideOnUserClose(config, false, false, true), "tray disabled user close");

        config.TrayEnabled = true;
        AssertEqual(false, FrameScopeWebHostLifecycle.ShouldHideOnUserClose(config, false, false, false), "non-user close");
    }

    private static void ExplicitCloseAndDisposingAreNeverTrapped()
    {
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
        config.CloseWindowBehavior = "minimize-to-tray";
        config.TrayEnabled = true;

        AssertEqual(false, FrameScopeWebHostLifecycle.ShouldHideOnUserClose(config, true, false, true), "explicit close");
        AssertEqual(false, FrameScopeWebHostLifecycle.ShouldHideOnUserClose(config, false, true, true), "disposing close");
    }

    private static void ActiveMonitoringAlwaysRequiresExitConfirmation()
    {
        AssertEqual(true, FrameScopeWebHostLifecycle.RequiresActiveMonitoringConfirmation(true), "active monitoring confirmation");
        AssertEqual(false, FrameScopeWebHostLifecycle.RequiresActiveMonitoringConfirmation(false), "idle monitoring confirmation");
    }

    private static void EveryNonHiddenCloseStopsBeforeClosing()
    {
        var method = typeof(FrameScopeWebHostLifecycle).GetMethod("ShouldStopOnClose");
        AssertEqual(true, method != null, "close routing policy exists");
        var config = FrameScopeConfigStore.CreateDefaultConfig();
        config.CloseWindowBehavior = "minimize-to-tray";
        config.TrayEnabled = true;
        AssertEqual(false, (bool)method.Invoke(null, new object[] { config, false, false, true }), "user close hides to tray");
        AssertEqual(true, (bool)method.Invoke(null, new object[] { config, false, false, false }), "system/application/task-manager close requires stop");
        config.TrayEnabled = false;
        AssertEqual(true, (bool)method.Invoke(null, new object[] { config, false, false, true }), "disabled tray close requires stop");
        AssertEqual(false, (bool)method.Invoke(null, new object[] { config, true, false, false }), "completed explicit close passes through");
        AssertEqual(false, (bool)method.Invoke(null, new object[] { config, false, true, false }), "disposal passes through");
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }
}
