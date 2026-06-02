internal static class FrameScopeWebHostLifecycle
{
    public const string CloseBehaviorExit = "exit";
    public const string CloseBehaviorMinimizeToTray = "minimize-to-tray";

    public static bool ShouldHideOnUserClose(FrameScopeConfig config, bool explicitCloseRequested, bool disposing, bool userClosing)
    {
        if (config == null || explicitCloseRequested || disposing || !userClosing) return false;
        return config.TrayEnabled &&
            string.Equals(config.CloseWindowBehavior, CloseBehaviorMinimizeToTray, System.StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequiresActiveMonitoringConfirmation(bool activeMonitoring)
    {
        return activeMonitoring;
    }
}
