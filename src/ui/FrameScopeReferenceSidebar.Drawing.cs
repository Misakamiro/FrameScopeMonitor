using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class FrameScopeReferenceSidebar : Control
{
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        e.Graphics.Clear(Color.FromArgb(3, 12, 23));
        DrawCompactSidebar(e.Graphics);
    }

    public void DrawReferenceSidebar(Graphics g, float scale, float ox, float oy, bool updateHitRects)
    {
        if (updateHitRects) hitRects.Clear();
        var state = g.Save();
        g.TranslateTransform(ox, oy);
        g.ScaleTransform(scale, scale);

        using (var bg = new LinearGradientBrush(new RectangleF(0, 0, DesignWidth, DesignHeight), Color.FromArgb(6, 20, 36), Color.FromArgb(2, 9, 18), 0f))
        {
            g.FillRectangle(bg, 0, 0, DesignWidth, DesignHeight);
        }

        using (var centerGlow = new PathGradientBrush(new[] {
            new PointF(110, 0), new PointF(835, 0), new PointF(835, DesignHeight), new PointF(110, DesignHeight)
        }))
        {
            centerGlow.CenterPoint = new PointF(430, 350);
            centerGlow.CenterColor = Color.FromArgb(56, 31, 66, 108);
            centerGlow.SurroundColors = new[] { Color.FromArgb(0, 31, 66, 108) };
            g.FillRectangle(centerGlow, 0, 0, DesignWidth, DesignHeight);
        }

        DrawMainCard(g);
        DrawVisualScrollBar(g);
        DrawLogo(g);
        DrawNavItems(g, scale, ox, oy, updateHitRects);
        DrawDivider(g);
        DrawServiceCard(g);

        g.Restore(state);
    }

}
