using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

internal sealed class FrameScopeStatusLabel : Label
{
    public string IconText { get; set; }
    public Color Accent { get; set; }
    public int CornerRadius { get; set; }

    public FrameScopeStatusLabel()
    {
        IconText = "";
        Accent = Color.FromArgb(41, 230, 255);
        CornerRadius = 16;
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        ForeColor = Color.FromArgb(241, 247, 255);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FrameScopeRoundedDrawing.ApplyRegion(this, CornerRadius);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 2 || rect.Height <= 2) return;
        var accent = ForeColor == Color.Empty ? Accent : ForeColor;
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(rect, CornerRadius))
        using (var brush = new LinearGradientBrush(rect, Color.FromArgb(24, 8, 32, 58), Color.FromArgb(34, 8, 22, 40), 90f))
        {
            e.Graphics.FillPath(brush, path);
        }
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(rect, CornerRadius))
        using (var pen = new Pen(Color.FromArgb(150, accent), 1.4f))
        using (var glow = new Pen(Color.FromArgb(50, accent), 3f))
        {
            e.Graphics.DrawPath(glow, path);
            e.Graphics.DrawPath(pen, path);
        }

        var parts = (Text ?? "").Replace("\r\n", "\n").Split('\n');
        string title = parts.Length > 0 ? parts[0] : "";
        string value = parts.Length > 1 ? parts[1] : "";
        using (var iconFont = new Font("Segoe MDL2 Assets", 24f, FontStyle.Regular))
        using (var titleFont = new Font("Microsoft YaHei UI", 9.8f, FontStyle.Bold))
        using (var valueFont = new Font("Microsoft YaHei UI", 12.4f, FontStyle.Bold))
        {
            TextRenderer.DrawText(e.Graphics, IconText, iconFont, new Rectangle(10, 0, 42, Height), accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, title, titleFont, new Rectangle(58, 14, Width - 66, 26), Color.FromArgb(221, 233, 247), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, value, valueFont, new Rectangle(58, 39, Width - 66, 34), accent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

internal sealed class FrameScopeCaptureChainVisual : Control
{
    public Color Accent { get; set; }
    public Color SuccessColor { get; set; }

    public FrameScopeCaptureChainVisual()
    {
        Accent = Color.FromArgb(41, 230, 255);
        SuccessColor = Color.FromArgb(104, 252, 100);
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var labels = new[] { "游戏 / 进程", "采样器", "分析引擎", "数据存储", "报告输出" };
        var icons = new[] { "\uE7FC", "\uE950", "\uE9D9", "\uE1DB", "\uE9F9" };
        int count = labels.Length;
        bool compact = Width < 460;
        int top = Math.Max(8, Height / 2 - (compact ? 34 : 42));
        int box = Math.Max(compact ? 38 : 48, Math.Min(compact ? 54 : 70, Height / 2));
        int sidePad = compact ? 24 : 28;
        int usable = Math.Max(1, Width - sidePad * 2);
        int labelWidth = compact ? 82 : 108;
        using (var iconFont = new Font("Segoe MDL2 Assets", Math.Max(compact ? 17f : 20f, box * 0.42f), FontStyle.Regular))
        using (var labelFont = new Font("Microsoft YaHei UI", compact ? 8f : 9.5f, FontStyle.Bold))
        using (var arrowPen = new Pen(Color.FromArgb(190, Accent), 2f))
        using (var textBrush = new SolidBrush(Color.FromArgb(225, 238, 250)))
        {
            for (int i = 0; i < count; i++)
            {
                int centerX = sidePad + (int)Math.Round(usable * (i / (double)(count - 1)));
                var rect = new Rectangle(centerX - box / 2, top, box, box);
                using (var path = FrameScopeRoundedDrawing.CreateRoundRect(rect, 14))
                using (var fill = new LinearGradientBrush(rect, Color.FromArgb(34, 11, 54, 82), Color.FromArgb(20, 8, 29, 50), 90f))
                using (var border = new Pen(i == count - 1 ? SuccessColor : Accent, 1.6f))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }

                Color iconColor = i == count - 1 ? SuccessColor : (i == 2 ? Color.FromArgb(154, 92, 255) : Accent);
                TextRenderer.DrawText(e.Graphics, icons[i], iconFont, rect, iconColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, labels[i], labelFont, new Rectangle(centerX - labelWidth / 2, top + box + 8, labelWidth, 22), Color.FromArgb(225, 238, 250), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (i < count - 1)
                {
                    int nextX = sidePad + (int)Math.Round(usable * ((i + 1) / (double)(count - 1)));
                    int y = top + box / 2;
                    e.Graphics.DrawLine(arrowPen, centerX + box / 2 + 10, y, nextX - box / 2 - 16, y);
                    e.Graphics.DrawLine(arrowPen, nextX - box / 2 - 26, y - 7, nextX - box / 2 - 16, y);
                    e.Graphics.DrawLine(arrowPen, nextX - box / 2 - 26, y + 7, nextX - box / 2 - 16, y);
                }
            }
        }
    }
}

internal sealed class FrameScopeToggleCheckBox : CheckBox
{
    public Color Accent { get; set; }
    public Color TextColor { get; set; }
    public Color MutedColor { get; set; }

    public FrameScopeToggleCheckBox()
    {
        Accent = Color.FromArgb(41, 230, 255);
        TextColor = Color.FromArgb(238, 246, 255);
        MutedColor = Color.FromArgb(127, 147, 168);
        DoubleBuffered = true;
        BackColor = Color.FromArgb(8, 24, 42);
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent == null ? Color.FromArgb(8, 24, 42) : Parent.BackColor);
        var box = new Rectangle(2, Math.Max(2, Height / 2 - 9), 18, 18);
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(box, 4))
        using (var fill = new LinearGradientBrush(box,
            Checked ? Color.FromArgb(70, 150, 238) : Color.FromArgb(18, 35, 56),
            Checked ? Color.FromArgb(9, 107, 201) : Color.FromArgb(10, 22, 38),
            90f))
        using (var border = new Pen(Checked ? Color.FromArgb(205, Accent) : Color.FromArgb(90, 82, 116, 148), 1.2f))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        if (Checked)
        {
            using (var pen = new Pen(Color.White, 1.8f))
            {
                e.Graphics.DrawLines(pen, new[] {
                    new Point(box.Left + 4, box.Top + 9),
                    new Point(box.Left + 7, box.Top + 12),
                    new Point(box.Right - 4, box.Top + 5)
                });
            }
        }

        var textRect = new Rectangle(32, 0, Math.Max(0, Width - 34), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, Checked ? TextColor : Color.FromArgb(205, TextColor), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class FrameScopeSidebarLogo : Control
{
    public Color Accent { get; set; }
    public Color PrimaryText { get; set; }

    public FrameScopeSidebarLogo()
    {
        Accent = Color.FromArgb(41, 230, 255);
        PrimaryText = Color.FromArgb(238, 246, 255);
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        BackColor = Color.FromArgb(11, 26, 45);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Width <= 1 || Height <= 1) return;
        FrameScopeRoundedDrawing.PaintSidebarBackground(e.Graphics, this);

        int iconBox = Math.Max(88, Math.Min(102, Height * 42 / 100));
        int titleHeight = Math.Max(52, Math.Min(62, Height * 24 / 100));
        int monitorHeight = Math.Max(42, Math.Min(50, Height * 20 / 100));
        int gapAfterIcon = 12;
        int totalHeight = iconBox + gapAfterIcon + titleHeight + monitorHeight;
        int top = Math.Max(8, (Height - totalHeight) / 2);
        int iconSize = Math.Max(78, Math.Min(96, iconBox - 10));
        int centerX = Width / 2;
        var iconCircle = new Rectangle(centerX - iconSize / 2, top + (iconBox - iconSize) / 2, iconSize, iconSize);

        using (var iconFont = new Font("Segoe MDL2 Assets", Math.Max(56f, iconSize * 0.70f), FontStyle.Regular))
        using (var titleFont = new Font("Segoe UI", Math.Max(27f, titleHeight * 0.64f), FontStyle.Bold))
        using (var monitorFont = new Font("Segoe UI", Math.Max(22f, monitorHeight * 0.60f), FontStyle.Bold))
        using (var glowBrush = new SolidBrush(Color.FromArgb(45, Accent)))
        using (var ringPen = new Pen(Color.FromArgb(190, Accent), 2.0f))
        using (var innerPen = new Pen(Color.FromArgb(86, Accent), 1.1f))
        {
            var glowRect = iconCircle;
            glowRect.Inflate(10, 10);
            e.Graphics.FillEllipse(glowBrush, glowRect);
            e.Graphics.DrawEllipse(ringPen, iconCircle);
            var innerRect = iconCircle;
            innerRect.Inflate(-7, -7);
            e.Graphics.DrawEllipse(innerPen, innerRect);

            var iconRect = new Rectangle(0, top, Width, iconBox);
            TextRenderer.DrawText(e.Graphics, "\uE9D2", iconFont, iconRect, Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            var titleRect = new Rectangle(0, top + iconBox + gapAfterIcon, Width, titleHeight);
            TextRenderer.DrawText(e.Graphics, "FrameScope", titleFont, titleRect, PrimaryText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            var monitorRect = new Rectangle(0, titleRect.Bottom - 2, Width, monitorHeight);
            TextRenderer.DrawText(e.Graphics, "Monitor", monitorFont, monitorRect, Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}

internal sealed class FrameScopeGlowDot : Control
{
    public Color DotColor { get; set; }

    public FrameScopeGlowDot()
    {
        DotColor = Color.FromArgb(109, 255, 106);
        DoubleBuffered = true;
        Size = new Size(22, 22);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var center = new PointF(Width / 2f, Height / 2f);
        using (var glow = new SolidBrush(Color.FromArgb(78, DotColor)))
        using (var dot = new SolidBrush(DotColor))
        {
            e.Graphics.FillEllipse(glow, center.X - 8, center.Y - 8, 16, 16);
            e.Graphics.FillEllipse(dot, center.X - 4.5f, center.Y - 4.5f, 9, 9);
        }
    }
}
