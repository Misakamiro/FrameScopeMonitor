using System;
using System.Drawing;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private static Control BuildTargetActionRow()
    {
        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(8, 24, 42),
            Padding = new Padding(16, 10, 16, 10),
            ColumnCount = 5,
            RowCount = 3
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var processInput = RoundedProcessInputHost(out processText);
        processInput.Margin = new Padding(0, 6, 14, 6);
        actions.Controls.Add(processInput, 0, 0);
        actions.SetColumnSpan(processInput, 3);

        var refresh = DashboardButton("刷新进程", "secondary");
        refresh.Click += (_, __) => RefreshProcessList();
        actions.Controls.Add(refresh, 3, 0);

        var add = DashboardButton("添加进程", "secondary");
        add.Click += (_, __) => AddSelectedProcess();
        actions.Controls.Add(add, 4, 0);

        var spacer = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        actions.Controls.Add(spacer, 0, 1);
        actions.SetColumnSpan(spacer, 2);

        var save = DashboardButton("保存配置", "secondary");
        save.Click += (_, __) => SaveConfigFromGrid();
        actions.Controls.Add(save, 2, 1);

        startButton = DashboardButton("启动监测", "primary");
        startButton.Click += (_, __) => StartWatcher();
        actions.Controls.Add(startButton, 3, 1);

        var stop = DashboardButton("停止监测", "danger");
        stop.Click += (_, __) => StopWatcher();
        actions.Controls.Add(stop, 4, 1);

        var hint = new Label
        {
            Text = "ⓘ  提示：目标进程可填写多个别名，用分号分隔；PUBG 模拟器会优先验证 TslGame 系列进程。",
            Dock = DockStyle.Fill,
            ForeColor = UiSubText,
            Font = new Font("Microsoft YaHei UI", 9f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        actions.Controls.Add(hint, 0, 2);
        actions.SetColumnSpan(hint, 5);
        return actions;
    }

    private static FrameScopeCardPanel RoundedProcessInputHost(out TextBox textBox)
    {
        var host = new FrameScopeCardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(9, 28, 48),
            BorderColor = Color.FromArgb(95, 60, 132, 190),
            GlowColor = Color.FromArgb(18, 41, 230, 255),
            CornerRadius = UiRadiusControl,
            Padding = new Padding(12, 7, 34, 5)
        };
        var created = new TextBox
        {
            Text = "输入进程名或点击刷新",
            Dock = DockStyle.Fill,
            BackColor = host.BackColor,
            ForeColor = UiText,
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 10f),
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.CustomSource,
            Margin = new Padding(0, 4, 0, 0)
        };
        created.Enter += (_, __) =>
        {
            if (string.Equals(created.Text, ProcessPickerPlaceholder, StringComparison.Ordinal)) created.SelectAll();
        };
        created.Click += (_, __) =>
        {
            OpenProcessPickerDialog();
        };
        textBox = created;
        host.Controls.Add(created);

        var arrowCover = new Label
        {
            Text = "\uE70D",
            Dock = DockStyle.Right,
            Width = 30,
            BackColor = host.BackColor,
            ForeColor = UiText,
            Font = new Font("Segoe MDL2 Assets", 8.5f),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        arrowCover.Click += (_, __) =>
        {
            OpenProcessPickerDialog();
        };
        host.Controls.Add(arrowCover);
        arrowCover.BringToFront();
        return host;
    }

    private static void DrawDarkComboItem(object sender, DrawItemEventArgs e)
    {
        var combo = sender as ComboBox;
        if (combo == null || e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using (var brush = new SolidBrush(selected ? Color.FromArgb(14, 64, 100) : Color.FromArgb(9, 28, 48)))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }
        var text = Convert.ToString(combo.Items[e.Index]) ?? "";
        TextRenderer.DrawText(e.Graphics, text, combo.Font, e.Bounds, UiText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
