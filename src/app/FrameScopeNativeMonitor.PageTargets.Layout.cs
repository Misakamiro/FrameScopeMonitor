using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private static Control TargetListCard(FrameScopeConfig config)
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 0, 12);
        card.Padding = new Padding(20, 16, 20, 16);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "▣  受监控游戏（" + EnabledTargetCount(config).ToString(CultureInfo.InvariantCulture) + "）", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var open = CompactDashboardButton("打开输出目录", "secondary");
        open.Click += (_, __) => OpenDataRoot();
        layout.Controls.Add(open, 1, 0);
        var list = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(7, 19, 34), ColumnCount = 1, RowCount = 2, Padding = new Padding(10, 6, 10, 6) };
        for (int i = 0; i < 2; i++) list.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        var enabled = config.Targets == null ? new List<FrameScopeTarget>() : config.Targets.Where(t => t.Enabled).Take(2).ToList();
        int row = 0;
        foreach (var target in enabled)
        {
            var label = new Label
            {
                Text = "●  " + target.Name + "        " + target.ProcessName,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ForeColor = UiText,
                Font = new Font("Microsoft YaHei UI", 9.3f),
                TextAlign = ContentAlignment.MiddleLeft
            };
            list.Controls.Add(label, 0, row++);
        }
        if (row == 0)
        {
            list.Controls.Add(new Label { Text = "暂无启用目标", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiMuted, TextAlign = ContentAlignment.MiddleLeft }, 0, row++);
        }
        MakeRounded(list, UiRadiusControl);
        layout.Controls.Add(list, 0, 1);
        layout.SetColumnSpan(list, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static Control TargetSettingsCard(FrameScopeConfig config)
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(UiSpaceCard);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = card.BackColor, ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "设置", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);

        var rootRow = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 1 };
        rootRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        rootRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rootRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        rootRow.Controls.Add(FormLabel("数据目录"), 0, 0);
        var dataRootHost = RoundedInputHost(config.DataRoot, out dataRootText);
        rootRow.Controls.Add(dataRootHost, 1, 0);
        var browse = SettingsSmallButton("选择", "secondary"); browse.Click += (_, __) => BrowseDataRoot(); rootRow.Controls.Add(browse, 2, 0);
        layout.Controls.Add(rootRow, 0, 1);

        autoOpenCheck = new CheckBox { Checked = config.OpenReportOnComplete };
        layout.Controls.Add(SettingsToggleButton("监测结束后自动打开报告", autoOpenCheck), 0, 2);
        verboseLogCheck = new CheckBox { Checked = config.EnableVerboseLogs };
        layout.Controls.Add(SettingsToggleButton("详细日志", verboseLogCheck), 0, 3);
        performanceDiagnosticsCheck = new CheckBox { Checked = config.EnablePerformanceDiagnosticsLogs };
        layout.Controls.Add(SettingsToggleButton("性能诊断", performanceDiagnosticsCheck), 0, 4);
        autoDiagnosticReportCheck = new CheckBox { Checked = config.AutoGenerateDiagnosticReport };
        layout.Controls.Add(SettingsToggleButton("自动生成诊断报告", autoDiagnosticReportCheck), 0, 5);
        card.Controls.Add(layout);
        return card;
    }
}
