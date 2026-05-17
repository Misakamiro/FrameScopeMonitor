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
    private static Control BuildAboutPage(FrameScopeConfig config)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(4, 12, 24), ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));

        var hero = GlassCard();
        hero.Dock = DockStyle.Fill;
        hero.Margin = new Padding(0, 0, 0, 12);
        hero.Padding = new Padding(28, 24, 28, 24);
        var heroLayout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1 };
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        heroLayout.Controls.Add(AboutTextBlock(), 0, 0);
        heroLayout.Controls.Add(AboutLogoBlock(), 1, 0);
        hero.Controls.Add(heroLayout);
        root.Controls.Add(hero, 0, 0);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        bottom.Controls.Add(AboutInfoCard("开发者", "FrameScope Team", "本地性能监测、会话报告与诊断工具维护。", "\uE716"), 0, 0);
        bottom.Controls.Add(AboutInfoCard("联系方式", "support@framescope.dev\r\nhttps://framescope.dev", "用于反馈报告生成、采集链路或界面问题。", "\uE715"), 1, 0);
        root.Controls.Add(bottom, 0, 1);
        return root;
    }

    private static Control AboutTextBlock()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 3, Padding = new Padding(8, 0, 18, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = "关于 FrameScope Monitor", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = UiSubText,
            Font = new Font("Microsoft YaHei UI", 10.6f),
            TextAlign = ContentAlignment.TopLeft,
            Text = "FrameScope Monitor 是一款专业的游戏会话监控与报告生成工具，旨在帮助玩家与开发者深入分析游戏运行表现，识别进程干扰，优化系统性能，并生成详尽的诊断报告。"
        }, 0, 1);
        var features = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 5, Padding = new Padding(0, 6, 0, 0) };
        for (int i = 0; i < 5; i++) features.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        features.Controls.Add(AboutFeatureRow("游戏会话捕获与监控"), 0, 0);
        features.Controls.Add(AboutFeatureRow("FPS 时间线与性能分析"), 0, 1);
        features.Controls.Add(AboutFeatureRow("进程干扰诊断与报告"), 0, 2);
        features.Controls.Add(AboutFeatureRow("监控目标管理与配置"), 0, 3);
        features.Controls.Add(AboutFeatureRow("多游戏支持与结果导出"), 0, 4);
        panel.Controls.Add(features, 0, 2);
        return panel;
    }

    private static Control AboutFeatureRow(string text)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new AboutCheckGlyph { Dock = DockStyle.Fill, BackColor = Color.Transparent, CheckColor = UiGreen }, 0, 0);
        row.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 11f), TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        return row;
    }

    private static Control AboutLogoBlock()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 4, 22, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.Controls.Add(new Label { Text = "\uE9D2", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiCyan, Font = new Font("Segoe MDL2 Assets", 88f), TextAlign = ContentAlignment.BottomCenter }, 0, 0);
        panel.Controls.Add(new Label { Text = "FrameScope", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Segoe UI", 31f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter }, 0, 1);
        panel.Controls.Add(new Label { Text = "Monitor", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Segoe UI", 31f, FontStyle.Bold), TextAlign = ContentAlignment.TopCenter }, 0, 2);
        panel.Controls.Add(new Label { Text = AppVersionText(), Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiMuted, Font = new Font("Segoe UI", 16f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter }, 0, 3);
        return panel;
    }

    private static Control AboutInfoCard(string title, string value, string caption, string icon)
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 12, 0);
        card.Padding = new Padding(24, 18, 24, 18);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = icon, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiCyan, Font = new Font("Segoe MDL2 Assets", 18f), TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        layout.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 10.5f), TextAlign = ContentAlignment.MiddleLeft }, 1, 1);
        layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiMuted, Font = new Font("Microsoft YaHei UI", 9.2f), TextAlign = ContentAlignment.TopLeft }, 1, 2);
        card.Controls.Add(layout);
        return card;
    }
}

internal sealed class AboutCheckGlyph : Control
{
    public Color CheckColor { get; set; }

    public AboutCheckGlyph()
    {
        CheckColor = Color.FromArgb(125, 250, 114);
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int size = Math.Min(22, Math.Min(Width, Height) - 4);
        if (size < 8) return;
        var rect = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
        using (var brush = new SolidBrush(CheckColor))
        using (var glow = new SolidBrush(Color.FromArgb(58, CheckColor)))
        {
            var glowRect = rect;
            glowRect.Inflate(4, 4);
            e.Graphics.FillEllipse(glow, glowRect);
            e.Graphics.FillEllipse(brush, rect);
        }
        using (var pen = new Pen(Color.FromArgb(4, 12, 24), 2.2f))
        {
            e.Graphics.DrawLines(pen, new[]
            {
                new Point(rect.Left + size / 4, rect.Top + size / 2),
                new Point(rect.Left + size / 2 - 1, rect.Bottom - size / 4),
                new Point(rect.Right - size / 4, rect.Top + size / 3)
            });
        }
    }
}
