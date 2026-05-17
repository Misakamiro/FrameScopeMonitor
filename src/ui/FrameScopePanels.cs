using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

internal class FrameScopeCardPanel : Panel
{
    public Color BorderColor { get; set; }
    public Color GlowColor { get; set; }
    public int CornerRadius { get; set; }
    public bool GradientFill { get; set; }

    public FrameScopeCardPanel()
    {
        DoubleBuffered = true;
        BorderColor = Color.FromArgb(72, 60, 132, 190);
        GlowColor = Color.FromArgb(20, 0, 217, 255);
        BackColor = Color.FromArgb(9, 23, 40);
        CornerRadius = 16;
        GradientFill = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        FrameScopeRoundedDrawing.ApplyRegion(this, CornerRadius);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        if (rect.Width <= 2 || rect.Height <= 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fillRect = new Rectangle(0, 0, rect.Width - 1, rect.Height - 1);
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(fillRect, CornerRadius))
        {
            if (GradientFill)
            {
                using (var brush = new LinearGradientBrush(fillRect, Color.FromArgb(26, BackColor), BackColor, 90f))
                {
                    e.Graphics.FillPath(brush, path);
                }
                using (var glow = new PathGradientBrush(path))
                {
                    glow.CenterPoint = new PointF(fillRect.Width * 0.42f, fillRect.Height * 0.18f);
                    glow.CenterColor = Color.FromArgb(18, 52, 124, 185);
                    glow.SurroundColors = new[] { Color.FromArgb(0, 52, 124, 185) };
                    e.Graphics.FillPath(glow, path);
                }
            }
            else
            {
                using (var brush = new SolidBrush(BackColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }
        }
        FrameScopeRoundedDrawing.DrawRoundedBorder(e.Graphics, rect, CornerRadius, GlowColor, BorderColor);
    }
}

internal sealed class FrameScopeWorkspacePanel : TableLayoutPanel
{
    public FrameScopeWorkspacePanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(3, 11, 24);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        if (rect.Width <= 1 || rect.Height <= 1) return;
        using (var brush = new LinearGradientBrush(rect, Color.FromArgb(4, 17, 35), Color.FromArgb(2, 8, 18), 90f))
        {
            e.Graphics.FillRectangle(brush, rect);
        }
        using (var glow = new PathGradientBrush(new[] {
            new PointF(rect.Width * 0.10f, rect.Height * 0.05f),
            new PointF(rect.Width * 0.95f, rect.Height * 0.08f),
            new PointF(rect.Width * 0.95f, rect.Height * 0.94f),
            new PointF(rect.Width * 0.08f, rect.Height * 0.94f)
        }))
        {
            glow.CenterPoint = new PointF(rect.Width * 0.52f, rect.Height * 0.30f);
            glow.CenterColor = Color.FromArgb(40, 16, 58, 104);
            glow.SurroundColors = new[] { Color.FromArgb(0, 16, 58, 104) };
            e.Graphics.FillRectangle(glow, rect);
        }
    }
}

internal sealed class FrameScopeSettingRowPanel : FrameScopeCardPanel
{
    public string RowTitle { get; set; }
    public string RowSubtitle { get; set; }
    public Color TitleColor { get; set; }
    public Color SubtitleColor { get; set; }
    public int ContentLeft { get; set; }

    public FrameScopeSettingRowPanel()
    {
        RowTitle = "";
        RowSubtitle = "";
        TitleColor = Color.FromArgb(238, 246, 255);
        SubtitleColor = Color.FromArgb(127, 147, 168);
        ContentLeft = 330;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int textWidth = Math.Max(120, Math.Min(ContentLeft - 28, Width - 40));
        using (var titleFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold))
        using (var subtitleFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular))
        {
            TextRenderer.DrawText(e.Graphics, RowTitle ?? "", titleFont, new Rectangle(18, 9, textWidth, 22), TitleColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, RowSubtitle ?? "", subtitleFont, new Rectangle(18, 30, textWidth, 20), SubtitleColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}

internal sealed class FrameScopeSidebarPanel : Panel
{
    public int CornerRadius { get; set; }

    public FrameScopeSidebarPanel()
    {
        DoubleBuffered = true;
        CornerRadius = 34;
        BackColor = Color.FromArgb(9, 24, 42);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        FrameScopeRoundedDrawing.ApplyRegion(this, CornerRadius);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        if (rect.Width <= 2 || rect.Height <= 2) return;
        rect.Width -= 1;
        rect.Height -= 1;

        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(rect, CornerRadius))
        using (var brush = new LinearGradientBrush(rect, Color.FromArgb(19, 43, 71), Color.FromArgb(6, 17, 32), 90f))
        {
            e.Graphics.FillPath(brush, path);
        }

        using (var highlightPath = FrameScopeRoundedDrawing.CreateRoundRect(new Rectangle(2, 2, rect.Width - 4, Math.Max(60, rect.Height / 3)), Math.Max(0, CornerRadius - 2)))
        using (var brush = new LinearGradientBrush(new Rectangle(2, 2, rect.Width - 4, Math.Max(60, rect.Height / 3)), Color.FromArgb(54, 66, 112, 154), Color.FromArgb(0, 66, 112, 154), 90f))
        {
            e.Graphics.FillPath(brush, highlightPath);
        }

        using (var sideGlow = new LinearGradientBrush(new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width / 2), rect.Height), Color.FromArgb(34, 41, 230, 255), Color.FromArgb(0, 41, 230, 255), 0f))
        using (var sidePath = FrameScopeRoundedDrawing.CreateRoundRect(new Rectangle(5, 8, Math.Max(1, rect.Width / 3), rect.Height - 16), Math.Max(0, CornerRadius - 8)))
        {
            e.Graphics.FillPath(sideGlow, sidePath);
        }

        using (var glowPen = new Pen(Color.FromArgb(72, 45, 166, 255), 2.4f))
        using (var borderPen = new Pen(Color.FromArgb(110, 51, 127, 176), 1.1f))
        using (var outer = FrameScopeRoundedDrawing.CreateRoundRect(rect, CornerRadius))
        {
            e.Graphics.DrawPath(glowPen, outer);
            rect.Inflate(-2, -2);
            using (var inner = FrameScopeRoundedDrawing.CreateRoundRect(rect, Math.Max(0, CornerRadius - 2)))
            {
                e.Graphics.DrawPath(borderPen, inner);
            }
        }
    }
}

internal sealed class FrameScopeRoundedTableLayoutPanel : TableLayoutPanel
{
    public Color BorderColor { get; set; }
    public Color GlowColor { get; set; }
    public int CornerRadius { get; set; }

    public FrameScopeRoundedTableLayoutPanel()
    {
        BorderColor = Color.FromArgb(62, 60, 132, 190);
        GlowColor = Color.FromArgb(16, 0, 217, 255);
        CornerRadius = 16;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        FrameScopeRoundedDrawing.ApplyRegion(this, CornerRadius);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        FrameScopeRoundedDrawing.DrawRoundedBorder(e.Graphics, ClientRectangle, CornerRadius, GlowColor, BorderColor);
    }
}
