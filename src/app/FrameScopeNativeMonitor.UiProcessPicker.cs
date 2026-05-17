using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private const string ProcessPickerPlaceholder = "\u8f93\u5165\u8fdb\u7a0b\u540d\u6216\u70b9\u51fb\u5237\u65b0";
    private const string ProcessPickerRefreshing = "\u6b63\u5728\u5237\u65b0\u8fdb\u7a0b...";

    private static void RefreshProcessList()
    {
        if (processText == null && grid == null) return;
        OpenProcessPickerDialog();
    }

    private static void RefreshProcessList(bool updateStatus)
    {
        if (updateStatus) OpenProcessPickerDialog();
        else PreloadProcessListAutocomplete();
    }

    private static void OpenProcessPickerDialog()
    {
        if (processText == null || grid == null)
        {
            if (processText == null) SetStatus("\u8bf7\u5148\u6253\u5f00\u201c\u76d1\u63a7\u76ee\u6807\u201d\u9875\u9762\u518d\u9009\u62e9\u8fdb\u7a0b\u3002");
            return;
        }

        string before = (processText.Text ?? "").Trim();
        if (string.Equals(before, ProcessPickerPlaceholder, StringComparison.Ordinal) ||
            string.Equals(before, ProcessPickerRefreshing, StringComparison.Ordinal))
        {
            before = "";
        }

        SetStatus("\u6b63\u5728\u6253\u5f00\u8fdb\u7a0b\u9009\u62e9\u5668...");
        using (var dialog = new FrameScopeProcessPickerDialog(before, cachedProcessPickerItems))
        {
            DialogResult result = dialog.ShowDialog(form);
            cachedProcessPickerItems = dialog.CurrentItems;
            ApplyProcessPickerAutocomplete(cachedProcessPickerItems, false);
            if (result != DialogResult.OK)
            {
                SetStatus("\u5df2\u5173\u95ed\u8fdb\u7a0b\u9009\u62e9\u5668\uff0c\u672a\u6dfb\u52a0\u65b0\u76ee\u6807\u3002");
                return;
            }

            string selected = (dialog.SelectedProcessName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(selected)) return;
            SetStatus("\u5df2\u9009\u62e9\u8fdb\u7a0b\uff1a" + selected);
            processText.Text = selected;
            AddSelectedProcess();
        }
    }

    private static void PreloadProcessListAutocomplete()
    {
        if (processText == null && processCombo == null) return;
        if (Interlocked.CompareExchange(ref processRefreshInFlight, 1, 0) != 0) return;

        ThreadPool.QueueUserWorkItem(delegate
        {
            List<FrameScopeProcessPickerItem> items = null;
            try
            {
                items = FrameScopeProcessPicker.EnumerateRunningProcesses();
            }
            catch
            {
                items = new List<FrameScopeProcessPickerItem>();
            }

            MethodInvoker apply = delegate
            {
                try
                {
                    ApplyProcessPickerAutocomplete(items ?? new List<FrameScopeProcessPickerItem>(), false);
                }
                finally
                {
                    Interlocked.Exchange(ref processRefreshInFlight, 0);
                }
            };

            Control target = processText as Control ?? processCombo as Control ?? form;
            try
            {
                if (target != null && !target.IsDisposed && target.IsHandleCreated) target.BeginInvoke(apply);
                else Interlocked.Exchange(ref processRefreshInFlight, 0);
            }
            catch
            {
                Interlocked.Exchange(ref processRefreshInFlight, 0);
            }
        });
    }

    private static void ApplyProcessPickerAutocomplete(List<FrameScopeProcessPickerItem> items, bool updateStatus)
    {
        cachedProcessPickerItems = items ?? new List<FrameScopeProcessPickerItem>();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchText = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in cachedProcessPickerItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ProcessName)) continue;
            names.Add(item.ProcessName);
            searchText.Add(item.ProcessName);
            if (!string.IsNullOrWhiteSpace(item.DisplayText)) searchText.Add(item.DisplayText);
        }

        if (processText != null)
        {
            var source = new AutoCompleteStringCollection();
            source.AddRange(names.ToArray());
            processText.AutoCompleteCustomSource = source;
            if (string.IsNullOrWhiteSpace(processText.Text)) processText.Text = ProcessPickerPlaceholder;
        }

        if (processCombo != null)
        {
            var comboSource = new AutoCompleteStringCollection();
            comboSource.AddRange(searchText.ToArray());
            processCombo.AutoCompleteCustomSource = comboSource;
        }

        if (updateStatus) SetStatus("\u5df2\u5237\u65b0\u5f53\u524d\u8fdb\u7a0b\u5217\u8868\uff0c\u5171 " + names.Count.ToString(CultureInfo.InvariantCulture) + " \u4e2a\u8fdb\u7a0b\u3002");
    }

    private static void AddSelectedProcess()
    {
        try
        {
            if ((processText == null && processCombo == null) || grid == null)
            {
                SetStatus("\u8bf7\u5148\u6253\u5f00\u201c\u76d1\u63a7\u76ee\u6807\u201d\u9875\u9762\u518d\u6dfb\u52a0\u8fdb\u7a0b\u3002");
                return;
            }

            string processName = SelectedProcessNameFromPicker();
            if (string.Equals(processName, ProcessPickerPlaceholder, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(processName, ProcessPickerRefreshing, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("\u8bf7\u5148\u9009\u62e9\u4e00\u4e2a\u6b63\u5728\u8fd0\u884c\u7684\u8fdb\u7a0b\uff0c\u6216\u76f4\u63a5\u8f93\u5165 TslGame.exe\u3002");
                return;
            }
            if (string.IsNullOrWhiteSpace(processName)) return;
            processName = FrameScopeTargetEditRules.NormalizeSingleProcessForAdd(processName);
            if (string.IsNullOrWhiteSpace(processName)) return;

            var plan = FrameScopeTargetEditRules.PlanAddProcess(IsWatcherRunningQuiet());
            if (plan.ShouldStopWatcherFirst)
            {
                StopFrameScopeBackgroundProcesses();
                SetStatus("\u76d1\u6d4b\u5df2\u6682\u505c\uff0c\u6dfb\u52a0\u5b8c\u6210\u540e\u8bf7\u624b\u52a8\u70b9\u51fb\u201c\u542f\u52a8\u76d1\u6d4b\u201d\u3002");
            }

            int rowIndex = grid.Rows.Add(true, Path.GetFileNameWithoutExtension(processName), processName, 100, true);
            if (rowIndex >= 0 && rowIndex < grid.Rows.Count)
            {
                grid.ClearSelection();
                grid.Rows[rowIndex].Selected = true;
                try { grid.FirstDisplayedScrollingRowIndex = rowIndex; }
                catch { }
            }
            SaveConfig(ReadGridConfig());
            MarkPageCacheDirty("overview", "settings", "reports");
            SetStatus("\u5df2\u6dfb\u52a0\u5e76\u4fdd\u5b58 " + processName + "\u3002\u9700\u8981\u624b\u52a8\u542f\u52a8\u76d1\u6d4b\u3002");
        }
        catch (Exception ex)
        {
            SetStatus("\u6dfb\u52a0\u8fdb\u7a0b\u5931\u8d25\uff1a" + ex.Message);
            MessageBox.Show(ex.Message, "\u6dfb\u52a0\u8fdb\u7a0b\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string SelectedProcessNameFromPicker()
    {
        if (processText != null) return (processText.Text ?? "").Trim();

        if (processCombo != null)
        {
            var selected = processCombo.SelectedItem as FrameScopeProcessPickerItem;
            if (selected != null) return selected.ProcessName;
            return (processCombo.Text ?? "").Trim();
        }

        return "";
    }
}
