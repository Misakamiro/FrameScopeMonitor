using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private static DataGridView CreateTargetGrid(FrameScopeConfig config)
    {
        var view = new DataGridView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(16, 4, 16, 12),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = true,
            AllowUserToResizeColumns = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BackgroundColor = Color.FromArgb(7, 19, 34),
            GridColor = Color.FromArgb(22, 56, 88),
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.Single,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeight = 44,
            EnableHeadersVisualStyles = false,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
        };
        view.AdvancedColumnHeadersBorderStyle.All = DataGridViewAdvancedCellBorderStyle.Single;
        view.AdvancedCellBorderStyle.All = DataGridViewAdvancedCellBorderStyle.Single;
        view.AdvancedCellBorderStyle.Left = DataGridViewAdvancedCellBorderStyle.None;
        view.AdvancedCellBorderStyle.Right = DataGridViewAdvancedCellBorderStyle.None;
        view.RowTemplate.Height = 50;
        view.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(9, 27, 47);
        view.ColumnHeadersDefaultCellStyle.ForeColor = UiText;
        view.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(10, 30, 50);
        view.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiText;
        view.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        view.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        view.DefaultCellStyle.BackColor = Color.FromArgb(7, 19, 34);
        view.DefaultCellStyle.ForeColor = UiText;
        view.DefaultCellStyle.SelectionBackColor = Color.FromArgb(14, 64, 100);
        view.DefaultCellStyle.SelectionForeColor = UiText;
        view.DefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
        view.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(9, 25, 42);
        MakeRounded(view, UiRadiusControl);
        view.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "启用", FillWeight = 42 });
        view.Columns.Add(new DataGridViewTextBoxColumn { Name = "GameName", HeaderText = "游戏 / 软件", FillWeight = 150 });
        view.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProcessName", HeaderText = "进程名", FillWeight = 230 });
        view.Columns.Add(new DataGridViewTextBoxColumn { Name = "SampleMs", HeaderText = "采样(ms)", FillWeight = 72 });
        view.Columns.Add(new DataGridViewCheckBoxColumn { Name = "AutoOpen", HeaderText = "自动打开报告", FillWeight = 90 });
        if (config.Targets != null)
        {
            foreach (var target in config.Targets)
            {
                view.Rows.Add(target.Enabled, target.Name, target.ProcessName, target.SampleIntervalMs, target.OpenReportOnComplete);
            }
        }
        view.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string name = view.Columns[e.ColumnIndex].Name;
            if (name == "GameName" || name == "ProcessName" || name == "SampleMs") view.BeginEdit(true);
        };
        view.CellValidating += (_, e) =>
        {
            if (e.RowIndex < 0 || view.Columns[e.ColumnIndex].Name != "SampleMs") return;
            int sampleMs;
            string error;
            if (!FrameScopeTargetEditRules.TryParseSampleInterval(Convert.ToString(e.FormattedValue), out sampleMs, out error))
            {
                e.Cancel = true;
                view.Rows[e.RowIndex].ErrorText = error;
                MessageBox.Show(error, "采样率无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                view.Rows[e.RowIndex].ErrorText = "";
            }
        };
        view.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            view.Rows[e.RowIndex].ErrorText = "";
            if (view.Columns[e.ColumnIndex].Name == "ProcessName")
            {
                view.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = FrameScopeTargetEditRules.NormalizeProcessName(Convert.ToString(view.Rows[e.RowIndex].Cells[e.ColumnIndex].Value));
            }
        };
        view.CurrentCellDirtyStateChanged += (_, __) =>
        {
            if (view.IsCurrentCellDirty) view.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        view.CellPainting += DrawTargetGridCheckboxCell;
        return view;
    }

    private static void DrawTargetGridCheckboxCell(object sender, DataGridViewCellPaintingEventArgs e)
    {
        var view = sender as DataGridView;
        if (view == null || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        string name = view.Columns[e.ColumnIndex].Name;
        if (name != "Enabled" && name != "AutoOpen") return;

        e.Handled = true;
        bool selected = (e.State & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;
        using (var back = new SolidBrush(selected ? Color.FromArgb(14, 78, 118) : (e.RowIndex % 2 == 0 ? Color.FromArgb(7, 19, 34) : Color.FromArgb(9, 25, 42))))
        {
            e.Graphics.FillRectangle(back, e.CellBounds);
        }
        bool on = false;
        try { on = Convert.ToBoolean(e.Value ?? false); }
        catch { }
        int size = 16;
        var box = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - size) / 2, e.CellBounds.Y + (e.CellBounds.Height - size) / 2, size, size);
        using (var path = FrameScopeRoundedDrawing.CreateRoundRect(box, 5))
        using (var fill = new LinearGradientBrush(box, on ? Color.FromArgb(58, 150, 238) : Color.FromArgb(18, 35, 56), on ? Color.FromArgb(9, 107, 201) : Color.FromArgb(10, 22, 38), 90f))
        using (var border = new Pen(on ? UiBlue : Color.FromArgb(92, 128, 160), 1.2f))
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }
        if (on)
        {
            using (var pen = new Pen(Color.White, 1.8f))
            {
                e.Graphics.DrawLines(pen, new[] {
                    new Point(box.Left + 4, box.Top + 8),
                    new Point(box.Left + 7, box.Top + 11),
                    new Point(box.Right - 4, box.Top + 5)
                });
            }
        }
        using (var line = new Pen(Color.FromArgb(34, 68, 98), 1f))
        {
            e.Graphics.DrawLine(line, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
        }
    }
}
