using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private static TableLayoutPanel SectionPanel(string title, string subtitle, int contentRows)
    {
        if (contentRows < 1) contentRows = 1;
        var panel = new FrameScopeRoundedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPanel,
            BorderColor = Color.FromArgb(75, 60, 132, 190),
            GlowColor = Color.FromArgb(24, 41, 230, 255),
            CornerRadius = UiRadiusCard,
            ColumnCount = 1,
            RowCount = contentRows + 1,
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        for (int i = 0; i < contentRows; i++)
        {
            if (contentRows == 2 && i == 1)
            {
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            }
            else
            {
                panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            }
        }

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPanel,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16, 8, 16, 2)
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = UiText,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = UiMuted,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        panel.Controls.Add(header, 0, 0);
        return panel;
    }

    private static Label FormLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = UiSubText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 2, 8, 2)
        };
    }

    private static void StyleDarkListView(ListView list)
    {
        if (list == null) return;
        list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var brush = new SolidBrush(Color.FromArgb(10, 30, 50)))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
                if (e.ColumnIndex == list.Columns.Count - 1 && e.Bounds.Right < list.ClientSize.Width)
                {
                    e.Graphics.FillRectangle(brush, new Rectangle(e.Bounds.Right, e.Bounds.Top, list.ClientSize.Width - e.Bounds.Right, e.Bounds.Height));
                }
            }
            TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, e.Bounds, UiText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            using (var pen = new Pen(Color.FromArgb(45, 85, 120)))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 5, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
            }
        };
        list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item != null && e.Item.Selected;
            var fillColor = selected ? Color.FromArgb(10, 45, 70) : Color.FromArgb(7, 19, 34);
            using (var brush = new SolidBrush(fillColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
                if (e.ColumnIndex == list.Columns.Count - 1 && e.Bounds.Right < list.ClientSize.Width)
                {
                    e.Graphics.FillRectangle(brush, new Rectangle(e.Bounds.Right, e.Bounds.Top, list.ClientSize.Width - e.Bounds.Right, e.Bounds.Height));
                }
            }
            if (e.ColumnIndex == 2)
            {
                bool ok = (e.SubItem.Text ?? "").Contains("完成");
                var dot = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top + (e.Bounds.Height - 14) / 2, 14, 14);
                using (var dotBrush = new SolidBrush(ok ? UiGreen : UiAmber))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillEllipse(dotBrush, dot);
                }
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, new Rectangle(e.Bounds.Left + 28, e.Bounds.Top, e.Bounds.Width - 30, e.Bounds.Height), ok ? UiGreen : UiAmber, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
            else if (e.ColumnIndex == list.Columns.Count - 1 && string.Equals((e.SubItem.Text ?? "").Trim(), "打开", System.StringComparison.Ordinal))
            {
                var buttonRect = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top + 7, Math.Min(86, e.Bounds.Width - 18), Math.Max(26, e.Bounds.Height - 14));
                using (var path = FrameScopeRoundedDrawing.CreateRoundRect(buttonRect, 8))
                using (var fill = new LinearGradientBrush(buttonRect, Color.FromArgb(38, 30, 105, 170), Color.FromArgb(32, 10, 52, 96), 90f))
                using (var border = new Pen(Color.FromArgb(140, UiBlue), 1f))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, buttonRect, UiText, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }
            else
            {
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, e.Bounds, UiText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
            using (var pen = new Pen(Color.FromArgb(42, 73, 105)))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
        };
    }
}
