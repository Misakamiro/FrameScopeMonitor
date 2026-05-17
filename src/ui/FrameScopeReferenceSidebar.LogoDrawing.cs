using System.Drawing;
using System.Drawing.Drawing2D;

internal sealed partial class FrameScopeReferenceSidebar
{
    private void DrawLogo(Graphics g)
    {
        DrawFrameScopeLogo(g, new RectangleF(382, 105, 140, 140), Accent);

        using (var title = new Font("Segoe UI", 64f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var subtitle = new Font("Segoe UI", 47f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var titleBrush = new SolidBrush(Color.FromArgb(248, 252, 255)))
        using (var accentBrush = new SolidBrush(Accent))
        {
            DrawCenteredText(g, "FrameScope", title, titleBrush, new RectangleF(96, 292, 694, 76));
            DrawCenteredText(g, "Monitor", subtitle, accentBrush, new RectangleF(96, 373, 694, 62));
        }
    }

    private void DrawFrameScopeLogo(Graphics g, RectangleF rect, Color color)
    {
        using (var glowPen = new Pen(Color.FromArgb(70, color), 15f))
        using (var pen = new Pen(color, 12f))
        using (var thin = new Pen(Color.FromArgb(190, color), 6f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            glowPen.StartCap = LineCap.Round;
            glowPen.EndCap = LineCap.Round;
            glowPen.LineJoin = LineJoin.Round;
            thin.StartCap = LineCap.Round;
            thin.EndCap = LineCap.Round;
            thin.LineJoin = LineJoin.Round;

            var points = new[]
            {
                new PointF(rect.X + 24, rect.Y + 126),
                new PointF(rect.X + 24, rect.Y + 20),
                new PointF(rect.X + 24, rect.Y + 126),
                new PointF(rect.X + 126, rect.Y + 126)
            };
            g.DrawLines(glowPen, points);
            g.DrawLines(pen, points);

            var chart = new[]
            {
                new PointF(rect.X + 43, rect.Y + 91),
                new PointF(rect.X + 71, rect.Y + 91),
                new PointF(rect.X + 102, rect.Y + 76),
                new PointF(rect.X + 124, rect.Y + 93),
                new PointF(rect.X + 124, rect.Y + 55),
                new PointF(rect.X + 101, rect.Y + 34),
                new PointF(rect.X + 77, rect.Y + 58),
                new PointF(rect.X + 49, rect.Y + 44),
                new PointF(rect.X + 43, rect.Y + 48),
                new PointF(rect.X + 43, rect.Y + 91)
            };
            g.DrawLines(glowPen, chart);
            g.DrawLines(thin, chart);
            g.DrawLine(thin, rect.X + 43, rect.Y + 106, rect.X + 124, rect.Y + 106);
        }
    }

    private void DrawStatusDot(Graphics g, float x, float y)
    {
        using (var glow = new SolidBrush(Color.FromArgb(90, SuccessColor)))
        using (var dot = new SolidBrush(SuccessColor))
        {
            g.FillEllipse(glow, x - 17, y - 17, 34, 34);
            g.FillEllipse(dot, x - 11, y - 11, 22, 22);
        }
    }

    private static void DrawCenteredText(Graphics g, string text, Font font, Brush brush, RectangleF rect)
    {
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
        {
            g.DrawString(text, font, brush, rect, sf);
        }
    }

    private static void DrawLeftText(Graphics g, string text, Font font, Brush brush, RectangleF rect)
    {
        using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
        {
            g.DrawString(text, font, brush, rect, sf);
        }
    }
}
