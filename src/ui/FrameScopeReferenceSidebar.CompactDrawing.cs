using System;
using System.Drawing;
using System.Drawing.Drawing2D;

internal sealed partial class FrameScopeReferenceSidebar
{
    private void DrawCompactSidebar(Graphics g)
    {
        hitRects.Clear();
        int w = Math.Max(1, Width);
        int h = Math.Max(1, Height);

        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(6, 20, 36), Color.FromArgb(2, 9, 18), 0f))
        {
            g.FillRectangle(bg, 0, 0, w, h);
        }

        var card = new Rectangle(18, 16, Math.Max(1, w - 38), Math.Max(1, h - 32));
        using (var shadowPath = FrameScopeRoundedDrawing.CreateRoundRect(new Rectangle(card.X - 4, card.Y + 8, card.Width + 8, card.Height + 8), 24))
        using (var shadow = new SolidBrush(Color.FromArgb(68, 0, 0, 0)))
        {
            g.FillPath(shadow, shadowPath);
        }
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(card, 24))
        using (var fill = new LinearGradientBrush(card, Color.FromArgb(34, 50, 85, 126), Color.FromArgb(8, 24, 45), 90f))
        {
            g.FillPath(fill, path);
        }
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(card, 24))
        using (var glow = new PathGradientBrush(path))
        {
            glow.CenterPoint = new PointF(card.Left + card.Width * 0.50f, card.Top + 220);
            glow.CenterColor = Color.FromArgb(38, 31, 83, 132);
            glow.SurroundColors = new[] { Color.FromArgb(0, 31, 83, 132) };
            g.FillPath(glow, path);
        }
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(card, 24))
        using (var border = new Pen(Color.FromArgb(74, 71, 132, 191), 1.6f))
        {
            g.DrawPath(border, path);
        }

        DrawCompactLogo(g, card);
        DrawCompactNavItems(g, card);
        DrawCompactServiceCard(g, card);
    }

    private void DrawCompactLogo(Graphics g, Rectangle card)
    {
        int iconSize = Math.Max(62, Math.Min(88, card.Width / 3));
        int iconX = card.Left + (card.Width - iconSize) / 2;
        int iconY = card.Top + 42;
        var state = g.Save();
        g.TranslateTransform(iconX, iconY);
        float iconScale = iconSize / 140f;
        g.ScaleTransform(iconScale, iconScale);
        DrawFrameScopeLogo(g, new RectangleF(0, 0, 140, 140), Accent);
        g.Restore(state);

        using (var title = new Font("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var subtitle = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var titleBrush = new SolidBrush(Color.FromArgb(248, 252, 255)))
        using (var accentBrush = new SolidBrush(Accent))
        {
            DrawCenteredText(g, "FrameScope", title, titleBrush, new RectangleF(card.Left, iconY + iconSize + 18, card.Width, 32));
            DrawCenteredText(g, "Monitor", subtitle, accentBrush, new RectangleF(card.Left, iconY + iconSize + 49, card.Width, 28));
        }
    }

    private void DrawCompactNavItems(Graphics g, Rectangle card)
    {
        int itemX = card.Left + 16;
        int itemW = Math.Max(1, card.Width - 32);
        int itemH = 56;
        int gap = 14;
        int startY = card.Top + 258;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var rect = new Rectangle(itemX, startY + i * (itemH + gap), itemW, itemH);
            item.Bounds = rect;
            DrawCompactNavItem(g, item, rect, GetActiveAmount(item.Key), GetHoverAmount(item.Key));
            hitRects[item.Key] = rect;
        }
    }

    private void DrawCompactNavItem(Graphics g, NavItem item, Rectangle rect, float activeAmount, float hoverAmount)
    {
        Color fillStart = FrameScopeMotion.LerpColor(Color.FromArgb(24, 8, 24, 43), Color.FromArgb(42, 16, 43, 70), hoverAmount);
        fillStart = FrameScopeMotion.LerpColor(fillStart, Color.FromArgb(70, 19, 73, 118), activeAmount);
        Color fillEnd = FrameScopeMotion.LerpColor(Color.FromArgb(18, 6, 19, 36), Color.FromArgb(32, 9, 31, 55), hoverAmount);
        fillEnd = FrameScopeMotion.LerpColor(fillEnd, Color.FromArgb(48, 10, 39, 73), activeAmount);
        Color borderColor = FrameScopeMotion.LerpColor(
            FrameScopeMotion.LerpColor(Color.FromArgb(36, 118, 154, 191), Color.FromArgb(82, 108, 160, 205), hoverAmount),
            Color.FromArgb(220, Accent),
            activeAmount);
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(rect, 13))
        using (var fill = new LinearGradientBrush(rect, fillStart, fillEnd, 0f))
        {
            g.FillPath(fill, path);
        }
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(rect, 13))
        using (var pen = new Pen(borderColor, FrameScopeMotion.LerpFloat(1.0f, 1.8f, activeAmount)))
        {
            g.DrawPath(pen, path);
        }
        if (activeAmount > 0.01f)
        {
            using (var stripGlow = new SolidBrush(Color.FromArgb((int)Math.Round(82 * activeAmount), Accent)))
            using (var strip = new SolidBrush(Color.FromArgb((int)Math.Round(255 * activeAmount), Accent)))
            using (var glowPath = FrameScopeRoundedDrawing.CreateRoundRect(new Rectangle(rect.X - 3, rect.Y + 5, 10, rect.Height - 10), 5))
            using (var stripPath = FrameScopeRoundedDrawing.CreateRoundRect(new Rectangle(rect.X, rect.Y + 8, 5, rect.Height - 16), 3))
            {
                g.FillPath(stripGlow, glowPath);
                g.FillPath(strip, stripPath);
            }
        }

        Color itemColor = FrameScopeMotion.LerpColor(Color.FromArgb(178, 194, 214), Accent, activeAmount);
        Color textColor = FrameScopeMotion.LerpColor(Color.FromArgb(247, 250, 255), Accent, activeAmount);
        using (var iconFont = new Font("Segoe MDL2 Assets", 25f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var textFont = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var iconBrush = new SolidBrush(itemColor))
        using (var textBrush = new SolidBrush(textColor))
        {
            DrawCenteredText(g, item.Icon, iconFont, iconBrush, new RectangleF(rect.X + 28, rect.Y + 13, 36, 30));
            DrawLeftText(g, item.Text, textFont, textBrush, new RectangleF(rect.X + 76, rect.Y + 10, rect.Width - 90, 36));
        }
    }

    private void DrawCompactServiceCard(Graphics g, Rectangle card)
    {
        int rectX = card.Left + 16;
        int rectW = Math.Max(1, card.Width - 32);
        int rectH = 158;
        int rectY = card.Bottom - rectH - 16;
        using (var pen = new Pen(Color.FromArgb(35, 124, 160, 196), 1f))
        {
            g.DrawLine(pen, rectX, rectY - 22, rectX + rectW, rectY - 22);
        }
        var rect = new Rectangle(rectX, rectY, rectW, rectH);
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(rect, 13))
        using (var fill = new LinearGradientBrush(rect, Color.FromArgb(34, 14, 42, 70), Color.FromArgb(20, 6, 25, 45), 90f))
        using (var pen = new Pen(Color.FromArgb(50, 121, 158, 194), 1.2f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        using (var labelFont = new Font("Microsoft YaHei UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var statusFont = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var versionFont = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var muted = new SolidBrush(Color.FromArgb(170, 187, 207)))
        using (var success = new SolidBrush(SuccessColor))
        using (var text = new SolidBrush(Color.FromArgb(248, 252, 255)))
        {
            DrawLeftText(g, "\u670d\u52a1\u72b6\u6001", labelFont, muted, new RectangleF(rect.X + 20, rect.Y + 16, rect.Width - 40, 24));
            DrawStatusDot(g, rect.X + 30, rect.Y + 64);
            DrawLeftText(g, "\u8fd0\u884c\u4e2d", statusFont, success, new RectangleF(rect.X + 48, rect.Y + 48, rect.Width - 68, 32));
            using (var line = new Pen(Color.FromArgb(42, 130, 164, 196), 1f))
            {
                g.DrawLine(line, rect.X + 20, rect.Y + 92, rect.Right - 20, rect.Y + 92);
            }
            DrawLeftText(g, "\u7248\u672c", labelFont, muted, new RectangleF(rect.X + 20, rect.Y + 102, 120, 22));
            DrawLeftText(g, VersionText, versionFont, text, new RectangleF(rect.X + 20, rect.Y + 128, 140, 24));
        }
    }
}
