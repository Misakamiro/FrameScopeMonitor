using System;
using System.Drawing;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private static IDisposable statusPulseMotion;

    private static void FadeIn(Form target)
    {
        FrameScopeMotion.Animate(target, 220, delegate(float amount)
        {
            if (target == null || target.IsDisposed) return;
            target.Opacity = FrameScopeMotion.LerpFloat(0f, 1f, amount);
        });
    }

    private static void SetStatus(string text)
    {
        if (statusLabel != null)
        {
            statusLabel.Text = "\u72b6\u6001\uff1a" + text;
            statusLabel.ForeColor = UiCyan;
        }

        if (watcherSummaryLabel != null)
        {
            watcherSummaryLabel.Text = "\u76d1\u6d4b\u5668" + Environment.NewLine + "\u66f4\u65b0\u4e2d";
        }

        SetStatusPill("ACTIVE", UiCyan, Color.FromArgb(8, 12, 18));
        bool running = IsWatcherRunningQuiet();
        Color finalColor = running ? UiCyan : UiGreen;
        Control owner = statusLabel as Control ?? statusPill as Control ?? form;
        if (statusPulseMotion != null) statusPulseMotion.Dispose();
        statusPulseMotion = FrameScopeMotion.Animate(owner, 220, delegate(float amount)
        {
            Color color = FrameScopeMotion.LerpColor(UiCyan, finalColor, FrameScopeMotion.EaseInOutCubic(amount));
            if (statusLabel != null) statusLabel.ForeColor = color;
            if (statusPill != null) statusPill.ForeColor = color;
        }, delegate
        {
            SetStatusPill(running ? "RUNNING" : "READY", finalColor, Color.FromArgb(8, 12, 18));
            if (watcherSummaryLabel != null)
            {
                watcherSummaryLabel.Text = "\u76d1\u6d4b\u5668" + Environment.NewLine + (running ? "\u8fd0\u884c\u4e2d" : "\u5c31\u7eea");
            }
        });
    }

    private static void SetStatusPill(string text, Color backColor, Color foreColor)
    {
        if (statusPill == null) return;
        string value = text;
        if (string.Equals(text, "READY", StringComparison.OrdinalIgnoreCase)) value = "\u5c31\u7eea";
        else if (string.Equals(text, "RUNNING", StringComparison.OrdinalIgnoreCase)) value = "\u8fd0\u884c\u4e2d";
        else if (string.Equals(text, "ACTIVE", StringComparison.OrdinalIgnoreCase)) value = "\u66f4\u65b0\u4e2d";
        statusPill.Text = "\u8f6f\u4ef6\u72b6\u6001" + Environment.NewLine + value;
        statusPill.BackColor = Color.FromArgb(10, 30, 50);
        statusPill.ForeColor = backColor;
    }
}
