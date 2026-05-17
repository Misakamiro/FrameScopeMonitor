using System;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private static void StartLiveRefresh()
    {
        if (liveRefreshTimer != null) return;
        liveRefreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        liveRefreshTimer.Tick += (_, __) => RefreshLivePage();
        liveRefreshTimer.Start();
    }

    private static void StopLiveRefresh()
    {
        if (liveRefreshTimer == null) return;
        try
        {
            liveRefreshTimer.Stop();
            liveRefreshTimer.Dispose();
        }
        catch { }
        liveRefreshTimer = null;
    }

    private static void RefreshLivePage()
    {
        if (!string.Equals(activePageKey, "live", StringComparison.OrdinalIgnoreCase) || contentHost == null || form == null || form.IsDisposed)
        {
            StopLiveRefresh();
            return;
        }

        if (contentHost.InvokeRequired)
        {
            contentHost.BeginInvoke((MethodInvoker)RefreshLivePage);
            return;
        }

        FrameScopeConfig config = LoadConfig();
        contentHost.SuspendLayout();
        contentHost.Controls.Clear();
        contentHost.Controls.Add(BuildLivePage(config));
        contentHost.ResumeLayout(true);
        UpdateWatcherStatus();
    }
}
