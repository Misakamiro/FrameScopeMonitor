using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

internal sealed class FrameScopeProcessPickerDialog : Form
{
    private const string SortRecent = "\u6700\u8fd1\u4f7f\u7528";
    private const string SortName = "\u6309\u540d\u79f0";
    private const string SortProcess = "\u6309\u8fdb\u7a0b\u540d";

    private readonly TextBox searchText;
    private readonly ComboBox sortCombo;
    private readonly ListView processList;
    private readonly Label statusLabel;
    private readonly Button addButton;
    private readonly Button refreshButton;
    private readonly ImageList imageList;
    private readonly List<FrameScopeProcessPickerItem> items = new List<FrameScopeProcessPickerItem>();
    private int refreshInFlight;
    private int refreshVersion;

    public FrameScopeProcessPickerDialog(string initialSearch, IEnumerable<FrameScopeProcessPickerItem> initialItems)
    {
        Text = "\u6dfb\u52a0 / \u9009\u62e9\u4e00\u4e2a\u7a0b\u5e8f";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 560);
        MinimumSize = new Size(680, 480);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(246, 248, 251);
        ForeColor = Color.FromArgb(20, 30, 42);
        ShowIcon = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        imageList = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(24, 24) };
        imageList.Images.Add("default", SystemIcons.Application.ToBitmap());

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "\u9009\u62e9\u4e00\u4e2a\u7a0b\u5e8f",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        root.Controls.Add(filters, 0, 1);

        sortCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 6, 10, 6)
        };
        sortCombo.Items.AddRange(new object[] { SortRecent, SortName, SortProcess });
        sortCombo.SelectedIndex = 0;
        sortCombo.SelectedIndexChanged += delegate { ApplyFilterAndSort(); };
        filters.Controls.Add(sortCombo, 0, 0);

        searchText = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 7, 10, 6)
        };
        searchText.TextChanged += delegate { ApplyFilterAndSort(); };
        filters.Controls.Add(searchText, 1, 0);

        refreshButton = new Button
        {
            Text = "\u5237\u65b0",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 0, 5)
        };
        refreshButton.Click += delegate { BeginRefresh(); };
        filters.Controls.Add(refreshButton, 2, 0);

        processList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            SmallImageList = imageList,
            BorderStyle = BorderStyle.FixedSingle
        };
        processList.Columns.Add("\u7a0b\u5e8f", 350);
        processList.Columns.Add("\u8fdb\u7a0b\u540d", 170);
        processList.Columns.Add("\u8be6\u7ec6\u4fe1\u606f", 150);
        processList.SelectedIndexChanged += delegate { UpdateAddButton(); };
        processList.DoubleClick += delegate { ConfirmSelection(); };
        root.Controls.Add(processList, 0, 2);

        statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(86, 96, 110),
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(statusLabel, 0, 3);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        root.Controls.Add(buttons, 0, 4);

        var browseButton = new Button { Text = "\u6d4f\u89c8...", Dock = DockStyle.Fill, Margin = new Padding(0, 6, 10, 0) };
        browseButton.Click += delegate { BrowseForExecutable(); };
        buttons.Controls.Add(browseButton, 0, 0);

        addButton = new Button { Text = "\u6dfb\u52a0\u9009\u5b9a\u7684\u7a0b\u5e8f", Dock = DockStyle.Fill, Margin = new Padding(0, 6, 10, 0), Enabled = false };
        addButton.Click += delegate { ConfirmSelection(); };
        buttons.Controls.Add(addButton, 2, 0);

        var cancelButton = new Button { Text = "\u53d6\u6d88", Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0), DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancelButton, 3, 0);
        CancelButton = cancelButton;
        AcceptButton = addButton;

        if (initialItems != null) items.AddRange(initialItems.Where(item => item != null));
        if (!string.IsNullOrWhiteSpace(initialSearch)) searchText.Text = initialSearch;
        ApplyFilterAndSort();
        Shown += delegate
        {
            if (items.Count == 0) BeginRefresh();
            else searchText.Focus();
        };
        KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
    }

    public string SelectedProcessName { get; private set; }

    public List<FrameScopeProcessPickerItem> CurrentItems
    {
        get { return new List<FrameScopeProcessPickerItem>(items); }
    }

    private void BeginRefresh()
    {
        if (Interlocked.CompareExchange(ref refreshInFlight, 1, 0) != 0)
        {
            statusLabel.Text = "\u6b63\u5728\u5237\u65b0\u8fdb\u7a0b...";
            return;
        }

        int version = Interlocked.Increment(ref refreshVersion);
        statusLabel.Text = "\u6b63\u5728\u5237\u65b0\u8fdb\u7a0b...";
        addButton.Enabled = false;
        refreshButton.Enabled = false;
        processList.Items.Clear();

        var timeoutTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        timeoutTimer.Tick += delegate
        {
            timeoutTimer.Stop();
            timeoutTimer.Dispose();
            if (Interlocked.CompareExchange(ref refreshInFlight, 0, 1) == 1)
            {
                Interlocked.Increment(ref refreshVersion);
                refreshButton.Enabled = true;
                statusLabel.Text = "\u5237\u65b0\u8fdb\u7a0b\u8d85\u65f6\uff0c\u53ef\u4ee5\u91cd\u8bd5\u6216\u76f4\u63a5\u8f93\u5165\u8fdb\u7a0b\u540d\u3002";
            }
        };
        timeoutTimer.Start();

        ThreadPool.QueueUserWorkItem(delegate
        {
            List<FrameScopeProcessPickerItem> refreshed;
            try
            {
                refreshed = FrameScopeProcessPicker.EnumerateRunningProcesses();
            }
            catch
            {
                refreshed = new List<FrameScopeProcessPickerItem>();
            }

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (version != refreshVersion) return;
                    timeoutTimer.Stop();
                    timeoutTimer.Dispose();
                    items.Clear();
                    items.AddRange(refreshed.Where(item => item != null && !string.IsNullOrWhiteSpace(item.ProcessName)));
                    Interlocked.Exchange(ref refreshInFlight, 0);
                    refreshButton.Enabled = true;
                    ApplyFilterAndSort();
                });
            }
            catch
            {
                Interlocked.Exchange(ref refreshInFlight, 0);
            }
        });
    }

    private void ApplyFilterAndSort()
    {
        processList.BeginUpdate();
        try
        {
            processList.Items.Clear();
            string sortMode = Convert.ToString(sortCombo.SelectedItem) ?? SortRecent;
            string query = searchText.Text ?? "";
            List<FrameScopeProcessPickerItem> displayItems = FrameScopeProcessPicker.FilterAndSortItems(items, query, sortMode);
            foreach (FrameScopeProcessPickerItem item in displayItems)
            {
                string title = FrameScopeProcessPicker.FormatDisplayText(item.ProcessName, item.WindowTitle);
                string imageKey = !string.IsNullOrWhiteSpace(item.Path) && imageList.Images.ContainsKey(item.ProcessName) ? item.ProcessName : "default";
                var row = new ListViewItem(title) { ImageKey = imageKey, Tag = item };
                row.SubItems.Add(item.ProcessName ?? "");
                row.SubItems.Add(item.ProcessId > 0 ? "PID " + item.ProcessId.ToString() : "");
                processList.Items.Add(row);
            }
            if (processList.Items.Count > 0) processList.Items[0].Selected = true;
            statusLabel.Text = refreshInFlight != 0
                ? "\u6b63\u5728\u5237\u65b0\u8fdb\u7a0b..."
                : "\u53ef\u9009\u7a0b\u5e8f " + processList.Items.Count.ToString() + " \u4e2a";
        }
        finally
        {
            processList.EndUpdate();
        }
        UpdateAddButton();
    }

    private void UpdateAddButton()
    {
        addButton.Enabled = refreshInFlight == 0 && processList.SelectedItems.Count > 0;
    }

    private void ConfirmSelection()
    {
        if (refreshInFlight != 0 || processList.SelectedItems.Count == 0) return;
        var item = processList.SelectedItems[0].Tag as FrameScopeProcessPickerItem;
        if (item == null || string.IsNullOrWhiteSpace(item.ProcessName)) return;
        SelectedProcessName = item.ProcessName.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void BrowseForExecutable()
    {
        using (var dialog = new OpenFileDialog())
        {
            dialog.Title = "\u9009\u62e9\u4e00\u4e2a\u7a0b\u5e8f";
            dialog.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
            dialog.CheckFileExists = true;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            string path = dialog.FileName;
            string processName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(processName)) return;
            var item = new FrameScopeProcessPickerItem
            {
                ProcessName = processName,
                ProcessId = 0,
                WindowTitle = SafeProductName(path),
                Path = path
            };
            items.Insert(0, item);
            AddIconForPath(path, processName);
            ApplyFilterAndSort();
            SelectProcess(processName);
        }
    }

    private void SelectProcess(string processName)
    {
        foreach (ListViewItem row in processList.Items)
        {
            var item = row.Tag as FrameScopeProcessPickerItem;
            if (item != null && string.Equals(item.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
                row.Focused = true;
                row.EnsureVisible();
                break;
            }
        }
    }

    private void AddIconForPath(string path, string key)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(key) || imageList.Images.ContainsKey(key)) return;
        try
        {
            using (Icon icon = Icon.ExtractAssociatedIcon(path))
            {
                if (icon != null) imageList.Images.Add(key, icon.ToBitmap());
            }
        }
        catch
        {
        }
    }

    private static string SafeProductName(string path)
    {
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription;
            if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName;
        }
        catch
        {
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}
