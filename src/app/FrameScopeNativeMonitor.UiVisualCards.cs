using System;
using System.Drawing;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private static Label StatusCard(string title, string value, Color accent)
    {
        var label = new FrameScopeStatusLabel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 4, 8, 4),
            BackColor = Color.FromArgb(8, 24, 42),
            ForeColor = accent,
            Accent = accent,
            IconText = StatusIconForTitle(title)
        };
        label.Text = title + Environment.NewLine + value;
        return label;
    }

    private static Control MetricCard(string title, string value, string caption, Color accent)
    {
        return MetricCard(title, value, caption, accent, 22f);
    }

    private static Control MetricCard(string title, string value, string caption, Color accent, float valueFontSize)
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, UiSpaceSection, UiSpaceSection);
        card.Padding = new Padding(UiSpaceCard, 16, UiSpaceCard, 14);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = card.BackColor, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = UiSubText, Font = new Font("Microsoft YaHei UI", 10f), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, ForeColor = accent, Font = new Font("Segoe UI", valueFontSize, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = UiMuted, TextAlign = ContentAlignment.TopLeft }, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static Control InfoCard(string title, string value, string caption)
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, UiSpaceSection, 0);
        card.Padding = new Padding(UiSpaceCard, 14, UiSpaceCard, 14);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiSubText, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 1);
        layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiMuted, Font = new Font("Microsoft YaHei UI", 9f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static Control CaptureChainCard(string title, string subtitle)
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 0, 12);
        card.Padding = new Padding(20, 16, 20, 16);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "\uE71B  " + title, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiSubText, Font = new Font("Microsoft YaHei UI", 10.5f), TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        layout.Controls.Add(new Label { Text = "状态： 就绪\r\n游戏 / 进程      →      采样器      →      分析器", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiCyan, Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        layout.Controls.Add(new Label { Text = "●  链路状态：正常", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiGreen, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        card.Controls.Add(layout);
        return card;
    }

    private static Label MetricBlock(string caption, string value)
    {
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 8, 8),
            Padding = new Padding(12, 4, 12, 4),
            BackColor = UiRaised,
            ForeColor = UiText,
            Font = new Font("Consolas", 10f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        label.Text = caption + Environment.NewLine + value;
        return label;
    }
}
