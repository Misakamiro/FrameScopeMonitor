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
    public const int DesignWidth = 943;
    public const int DesignHeight = 1677;

    private sealed class NavItem
    {
        public string Key;
        public string Icon;
        public string Text;
        public RectangleF Bounds;
    }

    private readonly List<NavItem> items = new List<NavItem>();
    private readonly Dictionary<string, Rectangle> hitRects = new Dictionary<string, Rectangle>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> activeAmounts = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> hoverAmounts = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IDisposable> itemMotions = new Dictionary<string, IDisposable>(StringComparer.OrdinalIgnoreCase);
    private string hoverKey = "";
    private string activeKey = "overview";

    public event EventHandler<FrameScopeNavigationEventArgs> NavigationRequested;

    public string ActiveKey
    {
        get { return activeKey; }
        set
        {
            string next = string.IsNullOrWhiteSpace(value) ? "overview" : value;
            if (string.Equals(activeKey, next, StringComparison.OrdinalIgnoreCase)) return;
            string previous = activeKey;
            AnimateItemAmount(activeAmounts, previous, 0f, 160);
            activeKey = next;
            AnimateItemAmount(activeAmounts, activeKey, 1f, 160);
        }
    }
    public string VersionText { get; set; }
    public Color Accent { get; set; }
    public Color SuccessColor { get; set; }

    public FrameScopeReferenceSidebar()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Default;
        activeKey = "overview";
        VersionText = "v1.1.1";
        Accent = Color.FromArgb(41, 230, 255);
        SuccessColor = Color.FromArgb(104, 252, 100);
        BackColor = Color.FromArgb(4, 13, 25);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);

        items.Add(new NavItem { Key = "overview", Icon = "\uE80F", Text = "概览" });
        items.Add(new NavItem { Key = "targets", Icon = "\uF272", Text = "监控目标" });
        items.Add(new NavItem { Key = "reports", Icon = "\uE9F9", Text = "报告" });
        items.Add(new NavItem { Key = "settings", Icon = "\uE713", Text = "设置" });
        items.Add(new NavItem { Key = "about", Icon = "\uE716", Text = "关于我们" });
    }

    public void SetActiveKeyImmediate(string key)
    {
        string next = string.IsNullOrWhiteSpace(key) ? "overview" : key;
        activeKey = next;
        foreach (var motionKey in itemMotions.Keys.Where(k => k.StartsWith("active:", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            IDisposable motion = itemMotions[motionKey];
            if (motion != null) motion.Dispose();
            itemMotions.Remove(motionKey);
        }
        foreach (var item in items)
        {
            activeAmounts[item.Key] = string.Equals(item.Key, activeKey, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        string next = "";
        foreach (var pair in hitRects)
        {
            if (pair.Value.Contains(e.Location))
            {
                next = pair.Key;
                break;
            }
        }
        Cursor = string.IsNullOrEmpty(next) ? Cursors.Default : Cursors.Hand;
        if (!string.Equals(next, hoverKey, StringComparison.OrdinalIgnoreCase))
        {
            SetHoverKey(next);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        SetHoverKey("");
        Cursor = Cursors.Default;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            foreach (var pair in hitRects)
            {
                if (pair.Value.Contains(e.Location))
                {
                    var handler = NavigationRequested;
                    if (handler != null) handler(this, new FrameScopeNavigationEventArgs(pair.Key));
                    break;
                }
            }
        }
        base.OnMouseClick(e);
    }

    private void SetHoverKey(string next)
    {
        next = next ?? "";
        if (string.Equals(next, hoverKey, StringComparison.OrdinalIgnoreCase)) return;
        string previous = hoverKey;
        hoverKey = next;
        AnimateItemAmount(hoverAmounts, previous, 0f, 120);
        AnimateItemAmount(hoverAmounts, hoverKey, 1f, 120);
    }

    private float GetActiveAmount(string key)
    {
        float value;
        if (!string.IsNullOrWhiteSpace(key) && activeAmounts.TryGetValue(key, out value)) return value;
        return string.Equals(key, activeKey, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
    }

    private float GetHoverAmount(string key)
    {
        float value;
        if (!string.IsNullOrWhiteSpace(key) && hoverAmounts.TryGetValue(key, out value)) return value;
        return string.Equals(key, hoverKey, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
    }

    private void AnimateItemAmount(Dictionary<string, float> values, string key, float target, int durationMs)
    {
        if (values == null || string.IsNullOrWhiteSpace(key)) return;
        float start;
        if (!values.TryGetValue(key, out start))
        {
            start = target <= 0f ? 1f : 0f;
        }

        string motionKey = (ReferenceEquals(values, activeAmounts) ? "active:" : "hover:") + key;
        IDisposable existing;
        if (itemMotions.TryGetValue(motionKey, out existing))
        {
            existing.Dispose();
        }

        if (!IsHandleCreated)
        {
            values[key] = target;
            Invalidate();
            return;
        }

        itemMotions[motionKey] = FrameScopeMotion.Animate(this, durationMs, delegate(float amount)
        {
            values[key] = FrameScopeMotion.LerpFloat(start, target, amount);
            Invalidate();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var motion in itemMotions.Values)
            {
                if (motion != null) motion.Dispose();
            }
            itemMotions.Clear();
        }
        base.Dispose(disposing);
    }
}
