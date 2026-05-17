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
    private static int pageTransitionInFlight;
    private static readonly string[] CachedVisiblePageKeys = new[] { "overview", "targets", "reports", "settings", "about" };
    private const int WmSetRedraw = 0x000B;
    private const int RdwInvalidate = 0x0001;
    private const int RdwErase = 0x0004;
    private const int RdwAllChildren = 0x0080;
    private const int RdwUpdateNow = 0x0100;
    private const int RdwEraseNow = 0x0200;

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, int flags);

    private static void ShowPage(string key)
    {
        if (contentHost == null) return;
        key = NormalizeVisiblePageKey(key);
        FrameScopeCachedPage visibleCachedPage;
        if (string.Equals(activePageKey, key, StringComparison.OrdinalIgnoreCase) &&
            pageCache.TryGetValue(key, out visibleCachedPage) &&
            visibleCachedPage != null &&
            !visibleCachedPage.Dirty &&
            visibleCachedPage.Page != null &&
            !visibleCachedPage.Page.IsDisposed &&
            visibleCachedPage.Page.Parent == contentHost &&
            visibleCachedPage.Page.Visible)
        {
            WriteFrameScopeLog("ui-page-switch-skip page=" + key + " reason=already-active");
            return;
        }
        if (pageTransitionInFlight != 0) return;
        pageTransitionInFlight = 1;

        var timing = Stopwatch.StartNew();
        long buildMs = 0;
        long commitMs = 0;
        Control[] previousControls = contentHost.Controls.Cast<Control>().ToArray();
        try
        {
            StopLiveRefresh();
            FrameScopeConfig config = LoadConfig();

            long buildStart = timing.ElapsedMilliseconds;
            FrameScopeCachedPage cachedPage = GetOrBuildCachedPage(key, config);
            buildMs = timing.ElapsedMilliseconds - buildStart;
            RestorePageControls(cachedPage.Controls);

            long commitStart = timing.ElapsedMilliseconds;
            CommitPageTransition(key, cachedPage.Page, previousControls);
            commitMs = timing.ElapsedMilliseconds - commitStart;
            QueuePostPageRefresh(key);

            WriteFrameScopeLog("ui-page-switch page=" + key +
                " cached=" + cachedPage.Reused.ToString(CultureInfo.InvariantCulture) +
                " buildMs=" + buildMs.ToString(CultureInfo.InvariantCulture) +
                " commitMs=" + commitMs.ToString(CultureInfo.InvariantCulture) +
                " totalMs=" + timing.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
            StartPageCacheWarmup();
        }
        catch
        {
            pageTransitionInFlight = 0;
            throw;
        }
    }

    private static FrameScopeCachedPage GetOrBuildCachedPage(string key, FrameScopeConfig config)
    {
        FrameScopeCachedPage cached;
        if (pageCache.TryGetValue(key, out cached) && cached != null && cached.Page != null && !cached.Page.IsDisposed && !cached.Dirty)
        {
            cached.Reused = true;
            return cached;
        }

        FrameScopePageControlSnapshot previousRefs = CapturePageControls();
        ResetPageControls();
        Control page;
        bool pageLoaded = true;
        try
        {
            page = BuildPageForKey(key, config);
        }
        catch (Exception ex)
        {
            pageLoaded = false;
            WriteFrameScopeLog("ui-page-build-failed page=" + key + " error=" + ex.Message);
            page = BuildPageLoadError(key, ex);
            SetStatus("页面加载失败：" + key + "，" + ex.Message);
        }

        PreparePageForTransition(page);
        if (pageLoaded) AttachConfigDirtyFeedback(key);
        WarmUpPageForTransition(page);

        var replacement = new FrameScopeCachedPage
        {
            Key = key,
            Page = page,
            Controls = CapturePageControls(),
            Dirty = false,
            Reused = false
        };

        FrameScopeCachedPage old;
        if (pageCache.TryGetValue(key, out old) && old != null && old.Page != null && old.Page.Parent == null && !old.Page.IsDisposed)
        {
            old.Page.Dispose();
        }

        pageCache[key] = replacement;
        RestorePageControls(previousRefs);
        return replacement;
    }

    private static void PreparePageForTransition(Control page)
    {
        if (page == null) return;
        page.Dock = DockStyle.Fill;
        if (contentHost != null)
        {
            page.Bounds = contentHost.ClientRectangle;
        }
        page.Visible = false;
    }

    private static void HidePreviousPageControls(Control[] previousControls, Control page)
    {
        if (contentHost == null || previousControls == null) return;
        foreach (Control previous in previousControls)
        {
            if (previous == null || previous == page || previous.IsDisposed) continue;
            if (IsControlInPageCache(previous))
            {
                previous.Visible = false;
                continue;
            }

            if (previous.Parent == contentHost) contentHost.Controls.Remove(previous);
            previous.Dispose();
        }
    }

    private static bool IsControlInPageCache(Control control)
    {
        if (control == null) return false;
        foreach (FrameScopeCachedPage cached in pageCache.Values)
        {
            if (cached != null && ReferenceEquals(cached.Page, control)) return true;
        }
        return false;
    }

    private static void WarmUpPageForTransition(Control page)
    {
        if (page == null || page.IsDisposed) return;
        try
        {
            page.CreateControl();
            CreateChildHandles(page);
            page.PerformLayout();
        }
        catch
        {
        }
    }

    private static void CreateChildHandles(Control control)
    {
        if (control == null || control.IsDisposed) return;
        control.CreateControl();
        foreach (Control child in control.Controls)
        {
            CreateChildHandles(child);
        }
    }

    private static void CompletePageTransition()
    {
        pageTransitionInFlight = 0;
        QueueWatcherStatusRefresh();
    }

    private static void QueueWatcherStatusRefresh()
    {
        if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
        try
        {
            form.BeginInvoke((MethodInvoker)delegate
            {
                if (form == null || form.IsDisposed) return;
                UpdateWatcherStatus();
            });
        }
        catch { }
    }

    private static void CommitPageTransition(string key, Control page, Control[] previousControls)
    {
        bool formRedrawLocked = false;
        bool contentRedrawLocked = false;
        try
        {
            if (form != null && !form.IsDisposed && form.IsHandleCreated)
            {
                SendMessage(form.Handle, WmSetRedraw, 0, 0);
                formRedrawLocked = true;
            }
            if (contentHost != null && !contentHost.IsDisposed && contentHost.IsHandleCreated)
            {
                SendMessage(contentHost.Handle, WmSetRedraw, 0, 0);
                contentRedrawLocked = true;
            }

            if (page != null && !page.IsDisposed)
            {
                if (page.Parent != contentHost)
                {
                    contentHost.Controls.Add(page);
                }
                HidePreviousPageControls(previousControls, page);
                page.Visible = true;
                page.BringToFront();
            }
            activePageKey = key;
            SetActiveNavButton(key);
        }
        finally
        {
            if (contentRedrawLocked && contentHost != null && !contentHost.IsDisposed && contentHost.IsHandleCreated)
            {
                SendMessage(contentHost.Handle, WmSetRedraw, 1, 0);
            }
            if (formRedrawLocked && form != null && !form.IsDisposed && form.IsHandleCreated)
            {
                SendMessage(form.Handle, WmSetRedraw, 1, 0);
            }
            if (referenceSidebar != null && !referenceSidebar.IsDisposed)
            {
                referenceSidebar.Invalidate();
                referenceSidebar.Update();
            }
            foreach (var pair in navButtons)
            {
                if (pair.Value == null || pair.Value.IsDisposed) continue;
                pair.Value.Invalidate();
                pair.Value.Update();
            }
            if (contentHost != null && !contentHost.IsDisposed)
            {
                contentHost.Invalidate(true);
                if (page != null && !page.IsDisposed) page.Invalidate(true);
                contentHost.Update();
            }
            if (form != null && !form.IsDisposed && form.IsHandleCreated)
            {
                RedrawWindow(form.Handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwAllChildren);
            }
            CompletePageTransition();
        }
    }

    private static FrameScopePageControlSnapshot CapturePageControls()
    {
        return new FrameScopePageControlSnapshot
        {
            Grid = grid,
            ProcessCombo = processCombo,
            ProcessText = processText,
            DataRootText = dataRootText,
            AutoOpenCheck = autoOpenCheck,
            VerboseLogCheck = verboseLogCheck,
            PerformanceDiagnosticsCheck = performanceDiagnosticsCheck,
            AutoDiagnosticReportCheck = autoDiagnosticReportCheck,
            SettingsSampleIntervalText = settingsSampleIntervalText,
            LogRetentionDaysText = logRetentionDaysText,
            MaxLogDiskMbText = maxLogDiskMbText,
            StatusLabel = statusLabel,
            LatestReportLabel = latestReportLabel,
            StartButton = startButton,
            ReportListView = reportListView,
            ReportDetailLabel = reportDetailLabel,
            LiveLogLabel = liveLogLabel,
            LiveLogPauseButton = liveLogPauseButton,
            LiveLogClearButton = liveLogClearButton
        };
    }

    private static void RestorePageControls(FrameScopePageControlSnapshot controls)
    {
        if (controls == null)
        {
            ResetPageControls();
            return;
        }

        grid = controls.Grid;
        processCombo = controls.ProcessCombo;
        processText = controls.ProcessText;
        dataRootText = controls.DataRootText;
        autoOpenCheck = controls.AutoOpenCheck;
        verboseLogCheck = controls.VerboseLogCheck;
        performanceDiagnosticsCheck = controls.PerformanceDiagnosticsCheck;
        autoDiagnosticReportCheck = controls.AutoDiagnosticReportCheck;
        settingsSampleIntervalText = controls.SettingsSampleIntervalText;
        logRetentionDaysText = controls.LogRetentionDaysText;
        maxLogDiskMbText = controls.MaxLogDiskMbText;
        statusLabel = controls.StatusLabel;
        latestReportLabel = controls.LatestReportLabel;
        startButton = controls.StartButton;
        reportListView = controls.ReportListView;
        reportDetailLabel = controls.ReportDetailLabel;
        liveLogLabel = controls.LiveLogLabel;
        liveLogPauseButton = controls.LiveLogPauseButton;
        liveLogClearButton = controls.LiveLogClearButton;
    }

    private static void MarkPageCacheDirty(params string[] keys)
    {
        lastLatestReportScan = DateTime.MinValue;
        cachedLatestReportPath = "";
        if (keys == null || keys.Length == 0)
        {
            foreach (FrameScopeCachedPage cached in pageCache.Values)
            {
                if (cached != null) cached.Dirty = true;
            }
            return;
        }

        foreach (string key in keys)
        {
            string normalized = NormalizeVisiblePageKey(key);
            FrameScopeCachedPage cached;
            if (pageCache.TryGetValue(normalized, out cached) && cached != null) cached.Dirty = true;
        }
    }

    private static void RefreshCachedPage(string key)
    {
        key = NormalizeVisiblePageKey(key);
        MarkPageCacheDirty(key);
        ShowPage(key);
    }

    private static void QueuePostPageRefresh(string key)
    {
        if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
        try
        {
            form.BeginInvoke((MethodInvoker)delegate
            {
                if (form == null || form.IsDisposed) return;
                if (!string.Equals(key, activePageKey, StringComparison.OrdinalIgnoreCase)) return;
                if (string.Equals(key, "targets", StringComparison.OrdinalIgnoreCase))
                {
                    RefreshProcessList(false);
                }
            });
        }
        catch { }
    }

    private static void StartPageCacheWarmup()
    {
        if (form == null || form.IsDisposed || pageCacheWarmupTimer != null) return;
        pageCacheWarmupIndex = 0;
        pageCacheWarmupTimer = new System.Windows.Forms.Timer { Interval = 180 };
        pageCacheWarmupTimer.Tick += delegate
        {
            if (form == null || form.IsDisposed)
            {
                StopPageCacheWarmup();
                return;
            }

            while (pageCacheWarmupIndex < CachedVisiblePageKeys.Length)
            {
                string key = CachedVisiblePageKeys[pageCacheWarmupIndex++];
                if (string.Equals(key, activePageKey, StringComparison.OrdinalIgnoreCase)) continue;
                FrameScopeCachedPage cached;
                if (pageCache.TryGetValue(key, out cached) && cached != null && !cached.Dirty) continue;
                try
                {
                    GetOrBuildCachedPage(key, LoadConfig());
                    WriteFrameScopeLog("ui-page-cache-warmed page=" + key);
                }
                catch (Exception ex)
                {
                    WriteFrameScopeLog("ui-page-cache-warm-failed page=" + key + " error=" + ex.Message);
                }
                return;
            }
            StopPageCacheWarmup();
        };
        pageCacheWarmupTimer.Start();
    }

    private static void StopPageCacheWarmup()
    {
        if (pageCacheWarmupTimer == null) return;
        pageCacheWarmupTimer.Stop();
        pageCacheWarmupTimer.Dispose();
        pageCacheWarmupTimer = null;
    }

    private static Control BuildPageForKey(string key, FrameScopeConfig config)
    {
        key = NormalizeVisiblePageKey(key);
        Control page;
        if (string.Equals(key, "targets", StringComparison.OrdinalIgnoreCase)) page = BuildTargetsPage(config);
        else if (string.Equals(key, "settings", StringComparison.OrdinalIgnoreCase)) page = BuildSettingsPage(config);
        else if (string.Equals(key, "reports", StringComparison.OrdinalIgnoreCase)) page = BuildReportsPage(config);
        else if (string.Equals(key, "about", StringComparison.OrdinalIgnoreCase)) page = BuildAboutPage(config);
        else page = BuildOverviewPage(config);
        return page;
    }

    private static string NormalizeVisiblePageKey(string key)
    {
        return FrameScopeVisiblePageRules.NormalizeKey(key);
    }

    private static Control BuildPageLoadError(string key, Exception ex)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(4, 12, 24),
            Padding = new Padding(24)
        };
        var message = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 24, 43),
            ForeColor = UiText,
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "页面加载失败\r\n\r\n页面：" + key + "\r\n原因：" + ex.Message
        };
        panel.Controls.Add(message);
        return panel;
    }

    private static void ResetPageControls()
    {
        grid = null;
        processCombo = null;
        processText = null;
        dataRootText = null;
        autoOpenCheck = null;
        verboseLogCheck = null;
        performanceDiagnosticsCheck = null;
        autoDiagnosticReportCheck = null;
        settingsSampleIntervalText = null;
        logRetentionDaysText = null;
        maxLogDiskMbText = null;
        statusLabel = null;
        latestReportLabel = null;
        startButton = null;
        reportListView = null;
        reportDetailLabel = null;
        liveLogLabel = null;
        liveLogPauseButton = null;
        liveLogClearButton = null;
    }

    private static Control NavButton(string key, string icon, string text)
    {
        var button = new FrameScopeNavButton
        {
            IconText = icon,
            LabelText = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 8),
            BackColor = Color.FromArgb(10, 24, 43),
            Accent = UiCyan
        };
        button.Click += (_, __) => ShowPage(key);
        navButtons[key] = button;
        return button;
    }

    private static void SetActiveNavButton(string key)
    {
        if (referenceSidebar != null)
        {
            referenceSidebar.SetActiveKeyImmediate(key);
            referenceSidebar.Invalidate();
        }
        foreach (var pair in navButtons)
        {
            bool active = string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase);
            pair.Value.SetActiveImmediate(active);
            pair.Value.Invalidate();
        }
    }

    private sealed class FrameScopeCachedPage
    {
        public string Key;
        public Control Page;
        public FrameScopePageControlSnapshot Controls;
        public bool Dirty;
        public bool Reused;
    }

    private sealed class FrameScopePageControlSnapshot
    {
        public DataGridView Grid;
        public ComboBox ProcessCombo;
        public TextBox ProcessText;
        public TextBox DataRootText;
        public CheckBox AutoOpenCheck;
        public CheckBox VerboseLogCheck;
        public CheckBox PerformanceDiagnosticsCheck;
        public CheckBox AutoDiagnosticReportCheck;
        public TextBox SettingsSampleIntervalText;
        public TextBox LogRetentionDaysText;
        public TextBox MaxLogDiskMbText;
        public Label StatusLabel;
        public Label LatestReportLabel;
        public Button StartButton;
        public ListView ReportListView;
        public Label ReportDetailLabel;
        public Label LiveLogLabel;
        public Button LiveLogPauseButton;
        public Button LiveLogClearButton;
    }
}
