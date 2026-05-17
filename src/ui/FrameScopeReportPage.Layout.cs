using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private static Control BuildReportsPage(FrameScopeConfig config)
    {
        var entries = RecentHistoryEntries().ToList();
        selectedReportEntry = entries.FirstOrDefault();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(4, 12, 24), ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 69));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        root.Controls.Add(ReportListCard(entries), 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = root.BackColor, ColumnCount = 1, RowCount = 3 };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        right.Controls.Add(ReportSummaryCard(), 0, 0);
        right.Controls.Add(ReportsPageQuickActionsCard(), 0, 1);
        right.Controls.Add(ReportsPageExportOptionsCard(config), 0, 2);
        root.Controls.Add(right, 1, 0);
        return root;
    }

    private static Control ReportActionsCard()
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 0, 12);
        card.Padding = new Padding(16);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = card.BackColor, ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(new Label { Text = "报告", Dock = DockStyle.Fill, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold) }, 0, 0);

        var latest = DashboardButton("打开报告", "secondary");
        layout.Controls.Add(latest, 0, 1);

        var data = DashboardButton("打开输出目录", "secondary");
        layout.Controls.Add(data, 1, 1);

        var diag = DashboardButton("导出支持包", "secondary");
        layout.Controls.Add(diag, 0, 2);

        var history = DashboardButton("打开历史记录", "secondary");
        layout.Controls.Add(history, 1, 2);
        BindReportActionsCardButtons(latest, data, diag, history);

        card.Controls.Add(layout);
        return card;
    }

    private static Control ReportListCard(List<FrameScopeHistoryEntry> entries)
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 12, 0);
        card.Padding = new Padding(UiSpaceCard);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = card.BackColor, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "▤  报告中心\r\n查看、导出和管理捕获后的报告结果。", Dock = DockStyle.Fill, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(ReportCenterMetrics(entries), 0, 1);

        var list = new ListView { Dock = DockStyle.Fill, BackColor = Color.FromArgb(7, 19, 34), ForeColor = UiText, BorderStyle = BorderStyle.None, View = View.Details, FullRowSelect = true, OwnerDraw = true, Scrollable = false, Font = new Font("Microsoft YaHei UI", 10f) };
        list.SmallImageList = new ImageList { ImageSize = new Size(1, 48), ColorDepth = ColorDepth.Depth32Bit };
        reportListView = list;
        StyleDarkListView(list);
        list.Columns.Add("报告名称", 250);
        list.Columns.Add("类型", 115);
        list.Columns.Add("状态", 95);
        list.Columns.Add("生成时间", 160);
        list.Columns.Add("操作", 260);

        foreach (var entry in (entries ?? new List<FrameScopeHistoryEntry>()).Take(12))
        {
            var item = new ListViewItem(string.IsNullOrWhiteSpace(entry.Game) ? Path.GetFileNameWithoutExtension(entry.ReportHtml) : entry.Game);
            item.SubItems.Add("HTML报告");
            item.SubItems.Add(File.Exists(entry.ReportHtml) ? "已完成" : "缺失");
            item.SubItems.Add(FormatReportListTime(entry.Time));
            item.SubItems.Add("打开");
            item.Tag = entry;
            list.Items.Add(item);
        }

        if (list.Items.Count == 0)
        {
            var item = new ListViewItem("暂无报告");
            item.SubItems.Add("--");
            item.SubItems.Add("空闲");
            item.SubItems.Add("--");
            item.SubItems.Add("--");
            list.Items.Add(item);
        }
        else
        {
            list.Items[0].Selected = true;
        }

        list.SelectedIndexChanged += delegate
        {
            var selected = list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as FrameScopeHistoryEntry : null;
            if (selected != null)
            {
                selectedReportEntry = selected;
                if (reportDetailLabel != null) reportDetailLabel.Text = BuildReportSummaryText(selected);
            }
        };
        list.MouseClick += delegate(object sender, MouseEventArgs e)
        {
            var hit = list.HitTest(e.Location);
            if (hit.Item == null || hit.SubItem == null) return;
            int actionColumn = Math.Max(0, list.Columns.Count - 1);
            if (hit.Item.SubItems.IndexOf(hit.SubItem) != actionColumn) return;
            var selected = hit.Item.Tag as FrameScopeHistoryEntry;
            if (selected == null) return;
            selectedReportEntry = selected;
            hit.Item.Selected = true;
            OpenSelectedReport();
        };

        MakeRounded(list, UiRadiusControl);
        layout.Controls.Add(list, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static Control ReportCenterMetrics(List<FrameScopeHistoryEntry> entries)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 4, 0, 10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        int count = entries == null ? 0 : entries.Count;
        string latest = LatestReportPath();
        layout.Controls.Add(ReportMiniMetric("最近报告状态", string.IsNullOrWhiteSpace(latest) ? "暂无" : "可用", UiGreen), 0, 0);
        layout.Controls.Add(ReportMiniMetric("已生成报告", count.ToString(), UiBlue), 1, 0);
        layout.Controls.Add(ReportMiniMetric("导出格式", "HTML + 详细", UiCyan), 2, 0);
        return layout;
    }

    private static Control ReportMiniMetric(string title, string value, Color accent)
    {
        var panel = GlassCard();
        panel.Dock = DockStyle.Fill;
        panel.Margin = new Padding(0, 0, 12, 0);
        panel.Padding = new Padding(12, 8, 12, 8);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = UiSubText, Font = new Font("Microsoft YaHei UI", 9f), TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
        layout.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, ForeColor = accent, Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter }, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private static Control ReportSummaryCard()
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 0, 12);
        card.Padding = new Padding(UiSpaceCard, 14, UiSpaceCard, 14);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "▤  报告摘要", Dock = DockStyle.Fill, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        reportDetailLabel = new Label { Text = BuildReportSummaryText(selectedReportEntry), Dock = DockStyle.Fill, ForeColor = UiSubText, Font = new Font("Microsoft YaHei UI", 10f), TextAlign = ContentAlignment.TopLeft, AutoEllipsis = true };
        layout.Controls.Add(reportDetailLabel, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private static string FormatReportListTime(string value)
    {
        DateTime parsed;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed) ||
            DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed))
        {
            return parsed.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        if (string.IsNullOrWhiteSpace(value)) return "--";
        return value.Length > 16 ? value.Substring(0, 16) : value;
    }

    private static string BuildReportSummaryText(FrameScopeHistoryEntry entry)
    {
        if (entry == null)
        {
            return "当前选中：暂无报告\r\n类型：--\r\n状态：等待生成\r\n输出目录：--";
        }

        string report = entry.ReportHtml ?? "";
        return "当前选中：" + (string.IsNullOrWhiteSpace(entry.Game) ? "--" : entry.Game) +
            "\r\n类型：HTML 报告" +
            "\r\n状态：" + (File.Exists(report) ? "已完成" : "缺失") +
            "\r\n生成时间：" + FormatReportListTime(entry.Time) +
            "\r\n输出目录：" + (string.IsNullOrWhiteSpace(entry.RunDir) ? "--" : "可用");
    }

    private static Control ReportsPageQuickActionsCard()
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 0, 12);
        card.Padding = new Padding(UiSpaceCard);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        layout.Controls.Add(new Label { Text = "▸  快速操作", Dock = DockStyle.Fill, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var openDir = DashboardButton("打开报告目录", "primary");
        var openReport = DashboardButton("打开 HTML 报告", "secondary");
        var support = DashboardButton("打开详细报告", "secondary");
        layout.Controls.Add(openDir, 0, 1);
        layout.Controls.Add(openReport, 0, 2);
        layout.Controls.Add(support, 0, 3);
        BindReportDetailActionButtons(openDir, openReport, support, null, null);
        card.Controls.Add(layout);
        return card;
    }

    private static Control ReportsPageExportOptionsCard(FrameScopeConfig config)
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(UiSpaceCard);
        var label = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = UiSubText,
            Font = new Font("Microsoft YaHei UI", 10f),
            TextAlign = ContentAlignment.TopLeft,
            Text = "▤  导出选项\r\n\r\n自动打开报告：" + (config.OpenReportOnComplete ? "已启用" : "关闭") +
                "\r\n保留天数：" + config.LogRetentionDays.ToString() +
                "\r\n最大 MB：" + config.MaxLogDiskMb.ToString() +
                "\r\n输出目录：" + (Directory.Exists(ResolveCurrentDataRoot()) ? "可用" : "未创建")
        };
        card.Controls.Add(label);
        return card;
    }

    private static Control ReportDetailCard(FrameScopeConfig config)
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(18);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = card.BackColor, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(new Label { Text = "报告详情", Dock = DockStyle.Fill, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold) }, 0, 0);

        reportDetailLabel = new Label { Text = BuildReportDetailText(selectedReportEntry), Dock = DockStyle.Fill, ForeColor = UiSubText, TextAlign = ContentAlignment.TopLeft };
        layout.Controls.Add(reportDetailLabel, 0, 1);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = card.BackColor, ColumnCount = 5, RowCount = 1 };
        for (int i = 0; i < 5; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        var openDir = DashboardButton("打开输出目录", "secondary");
        actions.Controls.Add(openDir, 0, 0);

        var openReport = DashboardButton("打开报告", "secondary");
        actions.Controls.Add(openReport, 1, 0);

        var support = DashboardButton("导出支持包", "secondary");
        actions.Controls.Add(support, 2, 0);

        var regenerate = DashboardButton("重新生成", "secondary");
        actions.Controls.Add(regenerate, 3, 0);

        var refresh = DashboardButton("刷新列表", "secondary");
        actions.Controls.Add(refresh, 4, 0);
        BindReportDetailActionButtons(openDir, openReport, support, regenerate, refresh);

        layout.Controls.Add(actions, 0, 2);
        card.Controls.Add(layout);
        return card;
    }
}
