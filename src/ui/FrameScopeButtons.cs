using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

internal sealed class FrameScopeRoundedButton : Button
{
    public int CornerRadius { get; set; }
    public bool UseGdiPlusText { get; set; }
    private Color paintBackColor;
    private float disabledAmount;
    private bool paintBackColorReady;
    private IDisposable backColorMotion;
    private IDisposable enabledMotion;

    public FrameScopeRoundedButton()
    {
        CornerRadius = 10;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FrameScopeRoundedDrawing.ApplyRegion(this, CornerRadius);
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        if (!paintBackColorReady || !IsHandleCreated)
        {
            paintBackColor = BackColor;
            paintBackColorReady = true;
            Invalidate();
            return;
        }

        Color start = paintBackColor;
        Color end = BackColor;
        if (backColorMotion != null) backColorMotion.Dispose();
        backColorMotion = FrameScopeMotion.Animate(this, 120, delegate(float amount)
        {
            paintBackColor = FrameScopeMotion.LerpColor(start, end, amount);
            Invalidate();
        });
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        float start = disabledAmount;
        float end = Enabled ? 0f : 1f;
        if (!IsHandleCreated)
        {
            disabledAmount = end;
            Invalidate();
            return;
        }

        if (enabledMotion != null) enabledMotion.Dispose();
        enabledMotion = FrameScopeMotion.Animate(this, 120, delegate(float amount)
        {
            disabledAmount = FrameScopeMotion.LerpFloat(start, end, amount);
            Invalidate();
        });
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (!paintBackColorReady)
        {
            paintBackColor = BackColor;
            paintBackColorReady = true;
        }

        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        pevent.Graphics.Clear(Parent == null ? Color.Transparent : Parent.BackColor);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Color visualBack = FrameScopeMotion.LerpColor(paintBackColor, ControlPaint.Dark(paintBackColor, 0.25f), disabledAmount);
        Color visualBorder = FrameScopeMotion.LerpColor(FlatAppearance.BorderColor, Color.FromArgb(85, FlatAppearance.BorderColor), disabledAmount);
        Color visualText = FrameScopeMotion.LerpColor(ForeColor, Color.FromArgb(130, ForeColor), disabledAmount);
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(rect, CornerRadius))
        using (var brush = new LinearGradientBrush(rect, ControlPaint.Light(visualBack, 0.10f), visualBack, 90f))
        using (var pen = new Pen(visualBorder, Math.Max(1, FlatAppearance.BorderSize)))
        {
            pevent.Graphics.FillPath(brush, path);
            using (var glow = new Pen(Color.FromArgb(22, visualBorder), 2f))
            {
                pevent.Graphics.DrawPath(glow, path);
            }
            pevent.Graphics.DrawPath(pen, path);
        }

        var textRect = new Rectangle(Padding.Left + 6, Padding.Top, Width - Padding.Horizontal - 12, Height - Padding.Vertical);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        if (TextAlign == ContentAlignment.MiddleLeft) flags |= TextFormatFlags.Left;
        else if (TextAlign == ContentAlignment.MiddleRight) flags |= TextFormatFlags.Right;
        else flags |= TextFormatFlags.HorizontalCenter;
        if (UseGdiPlusText)
        {
            using (var brush = new SolidBrush(visualText))
            using (var format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                if (TextAlign == ContentAlignment.MiddleLeft) format.Alignment = StringAlignment.Near;
                else if (TextAlign == ContentAlignment.MiddleRight) format.Alignment = StringAlignment.Far;
                else format.Alignment = StringAlignment.Center;
                pevent.Graphics.DrawString(Text, Font, brush, textRect, format);
            }
        }
        else
        {
            TextRenderer.DrawText(pevent.Graphics, Text, Font, textRect, visualText, flags);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (backColorMotion != null) backColorMotion.Dispose();
            if (enabledMotion != null) enabledMotion.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class FrameScopeNavButton : Control
{
    public string IconText { get; set; }
    public string LabelText { get; set; }
    public bool Active
    {
        get { return active; }
        set
        {
            if (active == value) return;
            active = value;
            AnimateAmount(activeAmount, value ? 1f : 0f, 160, ref activeMotion, delegate(float value2) { activeAmount = value2; });
        }
    }
    public Color Accent { get; set; }
    private bool active;
    private float activeAmount;
    private float hoverAmount;
    private float pressAmount;
    private IDisposable activeMotion;
    private IDisposable hoverMotion;
    private IDisposable pressMotion;

    public FrameScopeNavButton()
    {
        IconText = "";
        LabelText = "";
        Active = false;
        Accent = Color.FromArgb(41, 230, 255);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        ForeColor = Color.FromArgb(238, 246, 255);
        BackColor = Color.FromArgb(9, 24, 42);
        Font = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
    }

    public void SetActiveImmediate(bool value)
    {
        active = value;
        if (activeMotion != null)
        {
            activeMotion.Dispose();
            activeMotion = null;
        }
        activeAmount = value ? 1f : 0f;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        AnimateAmount(hoverAmount, 1f, 120, ref hoverMotion, delegate(float value) { hoverAmount = value; });
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        AnimateAmount(hoverAmount, 0f, 120, ref hoverMotion, delegate(float value) { hoverAmount = value; });
        AnimateAmount(pressAmount, 0f, 80, ref pressMotion, delegate(float value) { pressAmount = value; });
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            AnimateAmount(pressAmount, 1f, 80, ref pressMotion, delegate(float value) { pressAmount = value; });
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        AnimateAmount(pressAmount, 0f, 80, ref pressMotion, delegate(float value) { pressAmount = value; });
        base.OnMouseUp(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FrameScopeRoundedDrawing.ApplyRegion(this, 18);
    }

    private void AnimateAmount(float current, float target, int durationMs, ref IDisposable motion, Action<float> setter)
    {
        if (!IsHandleCreated)
        {
            setter(target);
            Invalidate();
            return;
        }

        float start = current;
        if (motion != null) motion.Dispose();
        motion = FrameScopeMotion.Animate(this, durationMs, delegate(float amount)
        {
            setter(FrameScopeMotion.LerpFloat(start, target, amount));
            Invalidate();
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        FrameScopeRoundedDrawing.PaintSidebarBackground(e.Graphics, this);
        Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Color idleBack = Color.FromArgb(28, 10, 29, 49);
        Color hoverBack = Color.FromArgb(38, 15, 43, 67);
        Color activeBack = Color.FromArgb(56, 8, 54, 86);
        Color pressedBack = Color.FromArgb(62, 8, 66, 98);
        Color back = FrameScopeMotion.LerpColor(idleBack, hoverBack, hoverAmount);
        back = FrameScopeMotion.LerpColor(back, activeBack, activeAmount);
        back = FrameScopeMotion.LerpColor(back, pressedBack, pressAmount);
        Color border = FrameScopeMotion.LerpColor(Color.FromArgb(64, 55, 84, 116), Accent, activeAmount);
        Color hoverGlow = FrameScopeMotion.LerpColor(Color.FromArgb(22, 70, 105, 140), Color.FromArgb(56, 41, 230, 255), hoverAmount);
        Color glow = FrameScopeMotion.LerpColor(hoverGlow, Color.FromArgb(132, Accent), activeAmount);

        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(rect, 20))
        using (var brush = new LinearGradientBrush(rect, back, FrameScopeMotion.LerpColor(Color.FromArgb(24, 7, 22, 40), Color.FromArgb(58, 16, 72, 114), activeAmount), 0f))
        using (var glowPen = new Pen(glow, FrameScopeMotion.LerpFloat(1.4f, 3.0f, activeAmount)))
        using (var borderPen = new Pen(border, FrameScopeMotion.LerpFloat(1f, 1.8f, activeAmount)))
        {
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(glowPen, path);
            rect.Inflate(-1, -1);
            using (var inner = FrameScopeRoundedDrawing.CreateRoundRect(rect, 19))
            {
                e.Graphics.DrawPath(borderPen, inner);
            }
        }

        if (activeAmount > 0.01f)
        {
            using (var pillGlow = new SolidBrush(Color.FromArgb((int)Math.Round(70 * activeAmount), Accent)))
            using (var pill = FrameScopeRoundedDrawing.CreateRoundRect(new Rectangle(0, 11, 8, Height - 22), 4))
            using (var brush = new SolidBrush(Color.FromArgb((int)Math.Round(255 * activeAmount), Accent)))
            {
                e.Graphics.FillRectangle(pillGlow, 0, 8, 12, Height - 16);
                e.Graphics.FillPath(brush, pill);
            }
        }

        using (var iconFont = new Font("Segoe MDL2 Assets", 35f, FontStyle.Regular))
        using (var textFont = new Font("Microsoft YaHei UI", FrameScopeMotion.LerpFloat(19f, 20f, activeAmount), FontStyle.Bold))
        using (var iconBrush = new SolidBrush(FrameScopeMotion.LerpColor(Color.FromArgb(166, 184, 204), Accent, activeAmount)))
        using (var textBrush = new SolidBrush(FrameScopeMotion.LerpColor(Color.FromArgb(238, 246, 255), Accent, activeAmount)))
        {
            var iconRect = new Rectangle(24, 0, 54, Height);
            var textRect = new Rectangle(112, 0, Math.Max(1, Width - 122), Height);
            TextRenderer.DrawText(e.Graphics, IconText, iconFont, iconRect, iconBrush.Color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, LabelText, textFont, textRect, textBrush.Color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (activeMotion != null) activeMotion.Dispose();
            if (hoverMotion != null) hoverMotion.Dispose();
            if (pressMotion != null) pressMotion.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class FrameScopeVisualImmediateExtensions
{
    public static void SetActiveImmediate(this FrameScopeNavButton button, bool active)
    {
        if (button == null) return;
        button.Active = active;
    }

    public static void SetActiveKeyImmediate(this FrameScopeReferenceSidebar sidebar, string key)
    {
        if (sidebar == null) return;
        sidebar.ActiveKey = string.IsNullOrWhiteSpace(key) ? "overview" : key;
    }
}
