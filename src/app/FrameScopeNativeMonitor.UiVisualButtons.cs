using System;
using System.Drawing;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private static Button DashboardButton(string text, string variant)
    {
        var button = Button(text, 0, 0, 100, 34);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(3, 2, 3, 2);
        if (string.Equals(variant, "primary", StringComparison.OrdinalIgnoreCase))
        {
            button.ForeColor = UiText;
            SetButtonPalette(button, Color.FromArgb(16, 123, 235), Color.FromArgb(70, 190, 255), Color.FromArgb(28, 148, 255), Color.FromArgb(10, 96, 190));
        }
        else if (string.Equals(variant, "danger", StringComparison.OrdinalIgnoreCase))
        {
            button.ForeColor = UiText;
            SetButtonPalette(button, UiRed, Color.FromArgb(255, 134, 157), Color.FromArgb(255, 118, 145), Color.FromArgb(210, 62, 91));
        }
        else
        {
            button.ForeColor = UiText;
            SetButtonPalette(button, Color.FromArgb(18, 38, 62), Color.FromArgb(74, 126, 174), Color.FromArgb(24, 56, 86), Color.FromArgb(18, 80, 118));
        }
        return button;
    }

    private static Button CompactDashboardButton(string text, string variant)
    {
        var button = DashboardButton(text, variant);
        button.Margin = new Padding(1, 1, 1, 1);
        button.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        button.Padding = new Padding(0);
        MakeRounded(button, UiRadiusControl);
        return button;
    }

    private static Button SettingsSmallButton(string text, string variant)
    {
        var button = DashboardButton(text, variant);
        button.Dock = DockStyle.None;
        button.Size = new Size(116, UiControlHeight);
        button.Anchor = AnchorStyles.Right;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold);
        button.Padding = new Padding(0);
        MakeRounded(button, UiRadiusControl);
        return button;
    }

    private static Button Button(string text, int x, int y, int width, int height)
    {
        var button = new FrameScopeRoundedButton
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(239, 247, 255),
            UseVisualStyleBackColor = false,
            CornerRadius = UiRadiusControl
        };
        SetButtonPalette(button, Color.FromArgb(24, 36, 50), Color.FromArgb(88, 133, 160), Color.FromArgb(34, 68, 92), Color.FromArgb(38, 112, 145));
        button.FlatAppearance.BorderSize = 1;
        button.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        MakeRounded(button, UiRadiusControl);
        button.MouseEnter += (_, __) => button.BackColor = ButtonPaletteColor(button, 2);
        button.MouseLeave += (_, __) => button.BackColor = ButtonPaletteColor(button, 0);
        button.MouseDown += (_, __) => button.BackColor = ButtonPaletteColor(button, 3);
        button.MouseUp += (_, __) => button.BackColor = ButtonPaletteColor(button, 2);
        return button;
    }

    private static void SetButtonPalette(Button button, Color normal, Color border, Color hover, Color down)
    {
        if (button == null) return;
        button.Tag = new[] { normal, border, hover, down };
        button.BackColor = normal;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = down;
    }

    private static Color ButtonPaletteColor(Button button, int index)
    {
        var colors = button == null ? null : button.Tag as Color[];
        if (colors == null || index < 0 || index >= colors.Length) return Color.FromArgb(24, 36, 50);
        return colors[index];
    }
}
