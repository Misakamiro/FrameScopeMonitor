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
    private static Control BuildReportProgressCard()
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0);
        card.Padding = new Padding(24, 14, 24, 14);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        layout.Controls.Add(new Label { Text = "▤  报告生成", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        reportProgressLabel = new Label { Text = "报告生成：空闲", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiMuted, Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
        layout.Controls.Add(reportProgressLabel, 1, 0);
        var openReportDirButton = DashboardButton("打开报告目录", "secondary");
        openReportDirButton.Click += (_, __) => OpenDataRoot();
        layout.Controls.Add(openReportDirButton, 2, 0);

        reportProgressTrack = new FrameScopeCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 22, 5),
            BackColor = Color.FromArgb(12, 38, 64),
            BorderColor = Color.FromArgb(52, 80, 139, 185),
            GlowColor = Color.FromArgb(12, 41, 230, 255),
            CornerRadius = UiRadiusControl
        };
        reportProgressFill = new Panel { Location = new Point(0, 0), Size = new Size(0, 24), BackColor = UiGreen };
        MakeRounded(reportProgressFill, UiRadiusControl);
        reportProgressTrack.Controls.Add(reportProgressFill);
        reportProgressTrack.Resize += (_, __) => ApplyReportProgressWidth();
        layout.Controls.Add(reportProgressTrack, 0, 1);
        layout.SetColumnSpan(reportProgressTrack, 2);

        reportStageLabel = new Label { Text = "报告状态：空闲", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, TextAlign = ContentAlignment.MiddleCenter };
        layout.Controls.Add(reportStageLabel, 2, 1);
        layout.Controls.Add(new Label { Text = "● 就绪", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiGreen, Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        return card;
    }

    private static void SetReportProgress(int percent, Color color)
    {
        reportProgressPercent = Math.Max(0, Math.Min(100, percent));
        if (reportProgressFill != null) reportProgressFill.BackColor = color;
        ApplyReportProgressWidth();
    }

    private static void ApplyReportProgressWidth()
    {
        if (reportProgressTrack == null || reportProgressFill == null) return;
        int width = Math.Max(0, (int)Math.Round(reportProgressTrack.ClientSize.Width * (reportProgressPercent / 100.0)));
        reportProgressFill.Bounds = new Rectangle(0, 0, width, reportProgressTrack.ClientSize.Height);
        FrameScopeRoundedDrawing.ApplyRegion(reportProgressFill, Math.Min(10, Math.Max(0, reportProgressFill.Height / 2)));
    }
}
