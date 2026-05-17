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
    private static Control BuildOverviewPage(FrameScopeConfig config)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 43));

        var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 5, RowCount = 1 };
        for (int i = 0; i < 5; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        metrics.Controls.Add(MetricCard("已启用目标", EnabledTargetCount(config).ToString(CultureInfo.InvariantCulture), "正在监控的目标", UiBlue), 0, 0);
        metrics.Controls.Add(MetricCard("捕获链状态", IsWatcherRunningQuiet() ? "运行中" : "空闲", "监测器当前状态", UiPurple()), 1, 0);
        metrics.Controls.Add(MetricCard("最近报告状态", string.IsNullOrWhiteSpace(LatestReportPath()) ? "无" : "可用", "报告输出", UiGreen), 2, 0);
        metrics.Controls.Add(MetricCard("输出目录状态", Directory.Exists(ResolveCurrentDataRoot()) ? "就绪" : "未创建", "可写入输出目录", UiCyan), 3, 0);
        metrics.Controls.Add(MetricCard("诊断模式", config.EnableVerboseLogs || config.EnablePerformanceDiagnosticsLogs ? "已启用" : "标准模式", "监控 + 诊断 + 保留", UiPurple()), 4, 0);
        root.Controls.Add(metrics, 0, 0);

        var mid = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1 };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        mid.Controls.Add(CaptureChainCard("捕获链流程", "从游戏 / 进程到报告输出的完整流程"), 0, 0);
        mid.Controls.Add(TargetListCard(config), 1, 0);
        root.Controls.Add(mid, 0, 1);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 4, RowCount = 1 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        bottom.Controls.Add(InfoCard("最近捕获状态", IsWatcherRunningQuiet() ? "监测运行中" : "暂无进行中的捕获", "最后活动：自动刷新"), 0, 0);
        bottom.Controls.Add(InfoCard("最近报告", string.IsNullOrWhiteSpace(LatestReportPath()) ? "暂无报告" : "报告可打开", "报告状态：" + (string.IsNullOrWhiteSpace(LatestReportPath()) ? "空闲" : "可用")), 1, 0);
        bottom.Controls.Add(OutputDirectoryCard(), 2, 0);
        bottom.Controls.Add(QuickActionsCard(), 3, 0);
        root.Controls.Add(bottom, 0, 2);
        return root;
    }

    private static Control OutputDirectoryCard()
    {
        string path = ResolveCurrentDataRoot();
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 12, 0);
        card.Padding = new Padding(UiSpaceCard, 14, UiSpaceCard, 14);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "输出目录", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var pathLabel = new Label
        {
            Text = CompactPathForDisplay(path),
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = UiText,
            Font = new Font("Microsoft YaHei UI", 10f),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        new ToolTip { AutomaticDelay = 120, AutoPopDelay = 12000 }.SetToolTip(pathLabel, path);
        layout.Controls.Add(pathLabel, 0, 1);
        layout.Controls.Add(new Label
        {
            Text = Directory.Exists(path) ? "目录状态：可写入" : "目录状态：未创建",
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = UiMuted,
            Font = new Font("Microsoft YaHei UI", 9f),
            TextAlign = ContentAlignment.TopLeft
        }, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static string CompactPathForDisplay(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "--";
        try
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parts = trimmed.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 3) return trimmed;
            return "..." + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(parts.Length - 3).ToArray());
        }
        catch
        {
            return path;
        }
    }

    private static Control QuickActionsCard()
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(UiSpaceCard, 14, UiSpaceCard, 14);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "▸  快速操作", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var start = CompactDashboardButton("启动监测", "primary");
        start.Click += (_, __) => StartWatcher();
        layout.Controls.Add(start, 0, 1);
        var output = CompactDashboardButton("打开输出目录", "secondary");
        output.Click += (_, __) => OpenDataRoot();
        layout.Controls.Add(output, 0, 2);
        card.Controls.Add(layout);
        return card;
    }
}
