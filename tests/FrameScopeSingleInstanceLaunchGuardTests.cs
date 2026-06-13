using System;
using System.Collections.Generic;
using System.Threading;

public static class FrameScopeSingleInstanceLaunchGuardTests
{
    private static int failures;

    public static int Main()
    {
        Run("ordinary UI launches are guarded", OrdinaryUiLaunchesAreGuarded);
        Run("worker and diagnostic launches bypass the UI guard", WorkerAndDiagnosticLaunchesBypassUiGuard);
        Run("duplicate UI lock is rejected and releases cleanly", DuplicateUiLockIsRejectedAndReleasesCleanly);
        Run("duplicate UI prompt stays Chinese", DuplicateUiPromptStaysChinese);

        if (failures != 0)
        {
            Console.Error.WriteLine("FrameScopeSingleInstanceLaunchGuardTests: FAIL " + failures);
            return 1;
        }

        Console.WriteLine("FrameScopeSingleInstanceLaunchGuardTests: PASS");
        return 0;
    }

    private static void OrdinaryUiLaunchesAreGuarded()
    {
        AssertTrue(FrameScopeNativeMonitor.IsInteractiveUiLaunchForTest(new string[0]), "empty argument launch should be ordinary UI");
        AssertTrue(FrameScopeNativeMonitor.IsInteractiveUiLaunchForTest(new[] { "--config", "custom.json" }), "config-only launch should still be ordinary UI");
    }

    private static void WorkerAndDiagnosticLaunchesBypassUiGuard()
    {
        AssertFalse(FrameScopeNativeMonitor.IsInteractiveUiLaunchForTest(new[] { "--watcher" }), "--watcher must bypass UI single-instance lock");
        AssertFalse(FrameScopeNativeMonitor.IsInteractiveUiLaunchForTest(new[] { "--monitor-session" }), "--monitor-session must bypass UI single-instance lock");
        AssertFalse(FrameScopeNativeMonitor.IsInteractiveUiLaunchForTest(new[] { "--MonitorProcessRole", "monitor-session-worker" }), "--MonitorProcessRole diagnostic role must bypass UI single-instance lock");
        AssertFalse(FrameScopeNativeMonitor.IsInteractiveUiLaunchForTest(new[] { "--generate-diagnostic-report" }), "diagnostic CLI must bypass UI single-instance lock");
        AssertFalse(FrameScopeNativeMonitor.IsInteractiveUiLaunchForTest(new[] { "--webview2-runtime-self-test" }), "runtime self-test must bypass UI single-instance lock");
        AssertFalse(FrameScopeNativeMonitor.IsInteractiveUiLaunchForTest(new[] { "--web-ui-smoke" }), "Web UI smoke automation must bypass ordinary UI lock");
    }

    private static void DuplicateUiLockIsRejectedAndReleasesCleanly()
    {
        Mutex first;
        AssertTrue(FrameScopeNativeMonitor.TryAcquireInteractiveUiSingleInstanceLockForTest(out first), "first ordinary UI lock should be acquired");
        try
        {
            Mutex second;
            AssertFalse(FrameScopeNativeMonitor.TryAcquireInteractiveUiSingleInstanceLockForTest(out second), "second ordinary UI lock should be rejected");
            AssertTrue(second == null, "rejected lock should not leak a Mutex handle");
        }
        finally
        {
            FrameScopeNativeMonitor.ReleaseInteractiveUiSingleInstanceLockForTest(first);
        }

        Mutex afterRelease;
        AssertTrue(FrameScopeNativeMonitor.TryAcquireInteractiveUiSingleInstanceLockForTest(out afterRelease), "lock should be acquirable after UI exits");
        FrameScopeNativeMonitor.ReleaseInteractiveUiSingleInstanceLockForTest(afterRelease);
    }

    private static void DuplicateUiPromptStaysChinese()
    {
        AssertEqual("FrameScope Monitor \u5df2\u5728\u8fd0\u884c\uff0c\u8bf7\u52ff\u91cd\u590d\u6253\u5f00\u3002", FrameScopeNativeMonitor.UiAlreadyRunningMessageForTest, "duplicate launch prompt");
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("[PASS] " + name);
        }
        catch (Exception ex)
        {
            failures++;
            Console.Error.WriteLine("[FAIL] " + name + ": " + ex.Message);
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition) throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }
}
