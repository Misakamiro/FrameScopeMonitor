using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

internal static partial class FrameScopeNativeMonitor
{
    private static void BuildUi(FrameScopeConfig config)
    {
        form = new Form
        {
            Text = "FrameScope Monitor",
            StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(1586, 992),
            MinimumSize = new Size(1280, 800),
            BackColor = Color.FromArgb(3, 8, 18),
            ForeColor = UiText,
            Font = new Font("Microsoft YaHei UI", 9f),
            AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.None,
            Opacity = 0
        };
        form.HandleCreated += (_, __) => EnableDarkTitleBar(form);
        form.Shown += (_, __) =>
        {
            FadeIn(form);
        };
        form.FormClosing += (_, __) =>
        {
            StopLiveRefresh();
            StopFrameScopeBackgroundProcesses();
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(3, 8, 18),
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        form.Controls.Add(root);
        root.Controls.Add(BuildTitleBar(), 0, 0);

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(3, 8, 18),
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 0, 0, 0)
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(shell, 0, 1);

        shell.Controls.Add(BuildSidebar(), 0, 0);

        var workspace = new FrameScopeWorkspacePanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(4, 12, 24),
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(22, 18, 18, 16)
        };
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        shell.Controls.Add(workspace, 1, 0);

        workspace.Controls.Add(BuildHeader(config), 0, 0);

        contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 14),
            AutoScroll = false
        };
        workspace.Controls.Add(contentHost, 0, 1);
        workspace.Controls.Add(BuildReportProgressCard(), 0, 2);

        statusTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        statusTimer.Tick += (_, __) => UpdateWatcherStatus();
        statusTimer.Start();
        ShowPage("overview");
    }

    private static Control BuildTitleBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(3, 10, 22),
            Padding = new Padding(18, 0, 14, 0)
        };
        bar.MouseDown += (_, e) => BeginWindowDrag(e);
        bar.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var borderPen = new Pen(Color.FromArgb(42, 24, 86, 124), 1f))
            {
                e.Graphics.DrawLine(borderPen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            }

            var iconRect = new RectangleF(19, 16, 18, 18);
            using (var iconBack = new LinearGradientBrush(iconRect, Color.FromArgb(18, 172, 255), Color.FromArgb(24, 255, 216), LinearGradientMode.ForwardDiagonal))
            using (var iconPen = new Pen(Color.FromArgb(150, 231, 255), 1.3f))
            using (var darkBrush = new SolidBrush(Color.FromArgb(4, 12, 24)))
            using (var titleFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point))
            using (var titleBrush = new SolidBrush(Color.FromArgb(238, 244, 252)))
            {
                var radius = 4;
                using (var path = FrameScopeRoundedDrawing.CreateRoundRect(Rectangle.Round(iconRect), radius))
                {
                    e.Graphics.FillPath(iconBack, path);
                    e.Graphics.DrawPath(iconPen, path);
                }

                e.Graphics.FillRectangle(darkBrush, iconRect.Left + 4, iconRect.Top + 4, 5, 5);
                e.Graphics.FillRectangle(darkBrush, iconRect.Left + 10, iconRect.Top + 4, 5, 5);
                e.Graphics.FillRectangle(darkBrush, iconRect.Left + 4, iconRect.Top + 10, 5, 5);
                e.Graphics.DrawString("FrameScope Monitor", titleFont, titleBrush, 50, 13);
            }
        };

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 150,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        controls.Controls.Add(WindowButton("\uE8BB", () => form.Close()));
        controls.Controls.Add(WindowButton("\uE922", () =>
        {
            form.WindowState = form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }));
        controls.Controls.Add(WindowButton("\uE921", () => form.WindowState = FormWindowState.Minimized));
        bar.Controls.Add(controls);
        return bar;
    }

    private static Control WindowButton(string icon, Action action)
    {
        var button = new Label
        {
            Text = icon,
            Width = 44,
            Height = 36,
            Margin = new Padding(0),
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(220, 232, 246),
            Font = new Font("Segoe MDL2 Assets", 10.5f),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        button.MouseEnter += (_, __) => button.BackColor = Color.FromArgb(30, 40, 72, 102);
        button.MouseLeave += (_, __) => button.BackColor = Color.Transparent;
        button.Click += (_, __) => action();
        return button;
    }

    private static Control BuildSidebar()
    {
        navButtons.Clear();
        referenceSidebar = new FrameScopeReferenceSidebar
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 20, 0),
            ActiveKey = activePageKey,
            VersionText = AppVersionText(),
            Accent = UiCyan,
            SuccessColor = UiGreen
        };
        referenceSidebar.NavigationRequested += delegate(object sender, FrameScopeNavigationEventArgs e)
        {
            ShowPage(e.Key);
        };
        return referenceSidebar;
    }

    [DllImport("dwmapi.dll")]

    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private static void BeginWindowDrag(MouseEventArgs e)
    {
        if (e == null || e.Button != MouseButtons.Left || form == null || form.IsDisposed) return;
        ReleaseCapture();
        SendMessage(form.Handle, 0xA1, 0x2, 0);
    }

    private static void EnableDarkTitleBar(Form target)
    {
        if (target == null || target.IsDisposed) return;
        try
        {
            int enabled = 1;
            int size = Marshal.SizeOf(typeof(int));
            if (DwmSetWindowAttribute(target.Handle, 20, ref enabled, size) != 0)
            {
                DwmSetWindowAttribute(target.Handle, 19, ref enabled, size);
            }
        }
        catch { }
    }

    private static Control BuildHeader(FrameScopeConfig config)
    {
        var header = new FrameScopeWorkspacePanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 12)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));

        var title = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 2, Padding = new Padding(10, 0, 0, 0) };
        title.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        title.Controls.Add(new Label { Text = "FrameScope Monitor", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiText, Font = new Font("Segoe UI", 29f, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft }, 0, 0);
        title.Controls.Add(new Label { Text = "游戏会话捕获、FPS 时间线、进程干扰分析与诊断报告。", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = UiSubText, Font = new Font("Microsoft YaHei UI", 12f), TextAlign = ContentAlignment.TopLeft }, 0, 1);
        header.Controls.Add(title, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 10, 4, 10) };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        watcherSummaryLabel = StatusCard("监测器", "就绪", UiCyan);
        targetCountLabel = StatusCard("已启用目标", EnabledTargetCount(config).ToString(CultureInfo.InvariantCulture) + " 已启用", UiBlue);
        statusPill = StatusCard("软件状态", "就绪", UiGreen);
        cards.Controls.Add(watcherSummaryLabel, 0, 0);
        cards.Controls.Add(targetCountLabel, 1, 0);
        cards.Controls.Add(statusPill, 2, 0);
        header.Controls.Add(cards, 1, 0);
        return header;
    }
}
