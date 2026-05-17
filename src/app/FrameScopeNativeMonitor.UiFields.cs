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
private static Form form;

    private static DataGridView grid;

    private static ComboBox processCombo;

    private static TextBox processText;

    private static int processRefreshInFlight;

    private static List<FrameScopeProcessPickerItem> cachedProcessPickerItems = new List<FrameScopeProcessPickerItem>();

    private static TextBox dataRootText;

    private static CheckBox autoOpenCheck;

    private static CheckBox verboseLogCheck;

    private static CheckBox performanceDiagnosticsCheck;

    private static CheckBox autoDiagnosticReportCheck;

    private static TextBox settingsSampleIntervalText;

    private static TextBox logRetentionDaysText;

    private static TextBox maxLogDiskMbText;

    private static Label statusLabel;

    private static Label statusPill;

    private static Panel reportProgressTrack;

    private static Panel reportProgressFill;

    private static Label reportProgressLabel;

    private static Button startButton;

    private static Label watcherSummaryLabel;

    private static Label targetCountLabel;

    private static Label latestReportLabel;

    private static Label reportStageLabel;

    private static Panel contentHost;

    private static ListView reportListView;

    private static Label reportDetailLabel;

    private static FrameScopeHistoryEntry selectedReportEntry;

    private static Dictionary<string, FrameScopeNavButton> navButtons = new Dictionary<string, FrameScopeNavButton>(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, FrameScopeCachedPage> pageCache = new Dictionary<string, FrameScopeCachedPage>(StringComparer.OrdinalIgnoreCase);

    private static System.Windows.Forms.Timer pageCacheWarmupTimer;

    private static int pageCacheWarmupIndex;

    private static FrameScopeReferenceSidebar referenceSidebar;

    private static string activePageKey = "overview";

    private static System.Windows.Forms.Timer statusTimer;

    private static System.Windows.Forms.Timer liveRefreshTimer;

    private static Label liveLogLabel;

    private static Button liveLogPauseButton;

    private static Button liveLogClearButton;

    private static bool liveLogPaused;

    private static bool liveLogCleared;

    private static string liveLogClearSignature = "";

    private static List<string> liveLogDisplayLines = new List<string>();

    private static DateTime lastProgressScan = DateTime.MinValue;

    private static Dictionary<string, object> cachedReportProgress;

    private static int reportProgressPercent;

    private static DateTime lastLatestReportScan = DateTime.MinValue;

    private static string cachedLatestReportPath = "";
}
