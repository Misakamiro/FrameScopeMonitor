using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

internal static class FrameScopeRoundedDrawing
{
    public static GraphicsPath CreateRoundRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
        if (r <= 0)
        {
            path.AddRectangle(rect);
            path.CloseFigure();
            return path;
        }

        int d = r * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void ApplyRegion(Control control, int radius)
    {
        if (control == null || control.Width <= 0 || control.Height <= 0) return;
        using (var path = CreateRoundRect(new Rectangle(0, 0, control.Width, control.Height), radius))
        {
            var old = control.Region;
            control.Region = new Region(path);
            if (old != null) old.Dispose();
        }
    }

    public static void DrawRoundedBorder(Graphics graphics, Rectangle rect, int radius, Color glow, Color border)
    {
        if (graphics == null || rect.Width <= 1 || rect.Height <= 1) return;
        rect.Width -= 1;
        rect.Height -= 1;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var glowPath = CreateRoundRect(rect, radius))
        using (var glowPen = new Pen(glow, 2f))
        {
            graphics.DrawPath(glowPen, glowPath);
        }
        rect.Inflate(-1, -1);
        using (var borderPath = CreateRoundRect(rect, Math.Max(0, radius - 1)))
        using (var borderPen = new Pen(border, 1f))
        {
            graphics.DrawPath(borderPen, borderPath);
        }
    }

    public static void PaintSidebarBackground(Graphics graphics, Control control)
    {
        if (graphics == null || control == null) return;
        var sidebar = control;
        while (sidebar != null && !(sidebar is FrameScopeSidebarPanel))
        {
            sidebar = sidebar.Parent;
        }

        if (sidebar == null || sidebar.Width <= 0 || sidebar.Height <= 0)
        {
            using (var fallback = new SolidBrush(control.BackColor))
            {
                graphics.FillRectangle(fallback, control.ClientRectangle);
            }
            return;
        }

        var childScreen = control.PointToScreen(Point.Empty);
        var sidebarScreen = sidebar.PointToScreen(Point.Empty);
        var offsetX = childScreen.X - sidebarScreen.X;
        var offsetY = childScreen.Y - sidebarScreen.Y;
        var sidebarRect = new Rectangle(-offsetX, -offsetY, sidebar.Width, sidebar.Height);
        using (var brush = new LinearGradientBrush(sidebarRect, Color.FromArgb(19, 43, 71), Color.FromArgb(6, 17, 32), 90f))
        {
            graphics.FillRectangle(brush, control.ClientRectangle);
        }

        var topHighlightRect = new Rectangle(2 - offsetX, 2 - offsetY, sidebar.Width - 4, Math.Max(60, sidebar.Height / 3));
        using (var highlightPath = CreateRoundRect(topHighlightRect, 32))
        using (var highlightBrush = new LinearGradientBrush(topHighlightRect, Color.FromArgb(54, 66, 112, 154), Color.FromArgb(0, 66, 112, 154), 90f))
        {
            graphics.FillPath(highlightBrush, highlightPath);
        }

        var glowRect = new Rectangle(5 - offsetX, 8 - offsetY, Math.Max(1, sidebar.Width / 3), sidebar.Height - 16);
        using (var sideGlow = new LinearGradientBrush(new Rectangle(-offsetX, -offsetY, Math.Max(1, sidebar.Width / 2), sidebar.Height), Color.FromArgb(34, 41, 230, 255), Color.FromArgb(0, 41, 230, 255), 0f))
        using (var sidePath = CreateRoundRect(glowRect, 26))
        {
            graphics.FillPath(sideGlow, sidePath);
        }
    }
}
