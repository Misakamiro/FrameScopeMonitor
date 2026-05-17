using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

internal static partial class FrameScopeNativeMonitor
{
    private static FrameScopeCardPanel GlassCard()
    {
        return new FrameScopeCardPanel
        {
            BackColor = Color.FromArgb(8, 24, 42),
            BorderColor = Color.FromArgb(66, 45, 133, 196),
            GlowColor = Color.FromArgb(16, 41, 230, 255),
            CornerRadius = UiRadiusCard
        };
    }

    private static Color UiPurple()
    {
        return Color.FromArgb(154, 92, 255);
    }

    private static string AppVersionText()
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(Application.ExecutablePath).FileVersion;
            return string.IsNullOrWhiteSpace(version) || version == "0.0.0.0" ? "v1.1.1" : "v" + version;
        }
        catch
        {
            return "v1.1.1";
        }
    }

    private static Control IconBlock(string icon, int size, Color color)
    {
        return new Label
        {
            Text = icon,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe MDL2 Assets", size, FontStyle.Regular),
            ForeColor = color,
            TextAlign = ContentAlignment.MiddleCenter
        };
    }

    private static void MakeRounded(Control control, int radius)
    {
        if (control == null) return;
        FrameScopeRoundedDrawing.ApplyRegion(control, radius);
        control.Resize += (_, __) =>
        {
            FrameScopeRoundedDrawing.ApplyRegion(control, radius == UiRadiusPill ? Math.Min(control.Width, control.Height) / 2 : radius);
            control.Invalidate();
        };
    }

}
