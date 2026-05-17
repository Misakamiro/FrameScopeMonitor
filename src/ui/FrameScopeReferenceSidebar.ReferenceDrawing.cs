using System;
using System.Drawing;
using System.Drawing.Drawing2D;

internal sealed partial class FrameScopeReferenceSidebar
{
    private void DrawMainCard(Graphics g)
    {
        var card = new RectangleF(96, 24, 694, 1628);
        using (var shadowPath = FrameScopeRoundedDrawing.CreateRoundRect(Rectangle.Round(new RectangleF(card.X - 5, card.Y + 8, card.Width + 10, card.Height + 10)), 44))
        using (var shadow = new SolidBrush(Color.FromArgb(64, 0, 0, 0)))
        {
            g.FillPath(shadow, shadowPath);
        }

        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(Rectangle.Round(card), 40))
        using (var fill = new LinearGradientBrush(card, Color.FromArgb(31, 56, 91), Color.FromArgb(8, 24, 45), 90f))
        {
            g.FillPath(fill, path);
        }

        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(Rectangle.Round(card), 40))
        using (var glow = new PathGradientBrush(path))
        {
            glow.CenterPoint = new PointF(443, 570);
            glow.CenterColor = Color.FromArgb(44, 31, 83, 132);
            glow.SurroundColors = new[] { Color.FromArgb(0, 31, 83, 132) };
            g.FillPath(glow, path);
        }

        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(Rectangle.Round(card), 40))
        using (var border = new Pen(Color.FromArgb(58, 86, 126, 176), 2f))
        using (var inner = new Pen(Color.FromArgb(30, 154, 215, 255), 1f))
        {
            g.DrawPath(border, path);
            var innerRect = Rectangle.Round(card);
            innerRect.Inflate(-2, -2);
            using (var innerPath = FrameScopeRoundedDrawing.CreateRoundRect(innerRect, 38))
            {
                g.DrawPath(inner, innerPath);
            }
        }
    }

    private void DrawVisualScrollBar(Graphics g)
    {
        using (var track = new LinearGradientBrush(new RectangleF(802, 0, 82, DesignHeight), Color.FromArgb(7, 20, 36), Color.FromArgb(4, 13, 25), 0f))
        {
            g.FillRectangle(track, 802, 0, 82, DesignHeight);
        }

        using (var thumbPath = FrameScopeRoundedDrawing.CreateRoundRect(new Rectangle(846, 410, 16, 832), 8))
        using (var thumb = new SolidBrush(Color.FromArgb(190, 169, 185, 205)))
        {
            g.FillPath(thumb, thumbPath);
        }
    }

    private void DrawNavItems(Graphics g, float scale, float ox, float oy, bool updateHitRects)
    {
        const float startY = 474f;
        const float itemX = 147f;
        const float itemWidth = 590f;
        const float itemHeight = 106f;
        const float gap = 38f;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            item.Bounds = new RectangleF(itemX, startY + i * (itemHeight + gap), itemWidth, itemHeight);
            DrawNavItem(g, item, GetActiveAmount(item.Key), GetHoverAmount(item.Key));
            if (updateHitRects)
            {
                hitRects[item.Key] = Rectangle.Round(new RectangleF(
                    ox + item.Bounds.X * scale,
                    oy + item.Bounds.Y * scale,
                    item.Bounds.Width * scale,
                    item.Bounds.Height * scale));
            }
        }
    }

    private void DrawNavItem(Graphics g, NavItem item, float activeAmount, float hoverAmount)
    {
        var rect = item.Bounds;
        Color fillStart = FrameScopeMotion.LerpColor(Color.FromArgb(24, 8, 24, 43), Color.FromArgb(40, 16, 43, 70), hoverAmount);
        fillStart = FrameScopeMotion.LerpColor(fillStart, Color.FromArgb(58, 19, 73, 118), activeAmount);
        Color fillEnd = FrameScopeMotion.LerpColor(Color.FromArgb(18, 6, 19, 36), Color.FromArgb(30, 9, 31, 55), hoverAmount);
        fillEnd = FrameScopeMotion.LerpColor(fillEnd, Color.FromArgb(45, 10, 39, 73), activeAmount);
        Color borderColor = FrameScopeMotion.LerpColor(
            FrameScopeMotion.LerpColor(Color.FromArgb(38, 118, 154, 191), Color.FromArgb(72, 108, 160, 205), hoverAmount),
            Color.FromArgb(220, Accent),
            activeAmount);
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(Rectangle.Round(rect), 22))
        using (var fill = new LinearGradientBrush(rect, fillStart, fillEnd, 0f))
        {
            g.FillPath(fill, path);
        }

        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(Rectangle.Round(rect), 22))
        using (var pen = new Pen(borderColor, FrameScopeMotion.LerpFloat(1.5f, 2.4f, activeAmount)))
        {
            g.DrawPath(pen, path);
        }

        if (activeAmount > 0.01f)
        {
            using (var stripGlow = new SolidBrush(Color.FromArgb((int)Math.Round(72 * activeAmount), Accent)))
            using (var strip = new SolidBrush(Color.FromArgb((int)Math.Round(255 * activeAmount), Accent)))
            using (var glowPath = FrameScopeRoundedDrawing.CreateRoundRect(new Rectangle((int)rect.X - 4, (int)rect.Y + 4, 18, (int)rect.Height - 8), 9))
            using (var stripPath = FrameScopeRoundedDrawing.CreateRoundRect(new Rectangle((int)rect.X, (int)rect.Y + 9, 8, (int)rect.Height - 18), 5))
            {
                g.FillPath(stripGlow, glowPath);
                g.FillPath(strip, stripPath);
            }
        }

        Color itemColor = FrameScopeMotion.LerpColor(Color.FromArgb(178, 194, 214), Accent, activeAmount);
        Color textColor = FrameScopeMotion.LerpColor(Color.FromArgb(247, 250, 255), Accent, activeAmount);
        using (var iconFont = new Font("Segoe MDL2 Assets", 48f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var textFont = new Font("Microsoft YaHei UI", 41f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var iconBrush = new SolidBrush(itemColor))
        using (var textBrush = new SolidBrush(textColor))
        {
            var iconRect = new RectangleF(rect.X + 42, rect.Y + 24, 76, 58);
            var textRect = new RectangleF(rect.X + 142, rect.Y + 22, rect.Width - 170, 62);
            DrawCenteredText(g, item.Icon, iconFont, iconBrush, iconRect);
            DrawLeftText(g, item.Text, textFont, textBrush, textRect);
        }
    }

    private void DrawDivider(Graphics g)
    {
        using (var pen = new Pen(Color.FromArgb(44, 124, 160, 196), 1.4f))
        {
            g.DrawLine(pen, 147, 1284, 737, 1284);
        }
    }

    private void DrawServiceCard(Graphics g)
    {
        var rect = new RectangleF(147, 1318, 590, 310);
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(Rectangle.Round(rect), 24))
        using (var fill = new LinearGradientBrush(rect, Color.FromArgb(34, 14, 42, 70), Color.FromArgb(20, 6, 25, 45), 90f))
        using (var pen = new Pen(Color.FromArgb(50, 121, 158, 194), 1.5f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        using (var labelFont = new Font("Microsoft YaHei UI", 34f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var statusFont = new Font("Microsoft YaHei UI", 44f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var versionFont = new Font("Segoe UI", 40f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var muted = new SolidBrush(Color.FromArgb(170, 187, 207)))
        using (var success = new SolidBrush(SuccessColor))
        using (var text = new SolidBrush(Color.FromArgb(248, 252, 255)))
        {
            DrawLeftText(g, "服务状态", labelFont, muted, new RectangleF(193, 1364, 420, 50));
            DrawStatusDot(g, 204, 1452);
            DrawLeftText(g, "运行中", statusFont, success, new RectangleF(237, 1428, 260, 62));
            using (var pen = new Pen(Color.FromArgb(42, 130, 164, 196), 1.3f))
            {
                g.DrawLine(pen, 192, 1500, 694, 1500);
            }
            DrawLeftText(g, "版本", labelFont, muted, new RectangleF(193, 1534, 260, 44));
            DrawLeftText(g, VersionText, versionFont, text, new RectangleF(193, 1582, 260, 46));
        }
    }
}
