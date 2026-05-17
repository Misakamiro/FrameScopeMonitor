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
    private static Control BuildTargetsPage(FrameScopeConfig config)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(4, 12, 24), ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        var left = SectionPanel("监控目标", "启用游戏、添加采样，并添加检测到的进程。", 2);
        left.Margin = new Padding(0, 0, 12, 0);
        if (left.RowStyles.Count > 2)
        {
            left.RowStyles[2] = new RowStyle(SizeType.Absolute, 180);
        }
        grid = CreateTargetGrid(config);
        left.Controls.Add(grid, 0, 1);
        left.Controls.Add(BuildTargetActionRow(), 0, 2);
        root.Controls.Add(left, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = root.BackColor, ColumnCount = 1, RowCount = 3 };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 27));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 39));
        right.Controls.Add(CaptureChainCard("捕获链状态", "最近报告：" + (string.IsNullOrWhiteSpace(LatestReportPath()) ? "暂无" : "可用")), 0, 0);
        right.Controls.Add(ReportActionsCard(), 0, 1);
        right.Controls.Add(TargetSettingsCard(config), 0, 2);
        root.Controls.Add(right, 1, 0);
        return root;
    }

}
