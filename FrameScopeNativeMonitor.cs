using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public sealed class FrameScopeConfig
{
    public FrameScopeConfig()
    {
        PollIntervalMs = 500;
        DataRoot = "";
        OpenReportOnComplete = true;
        MonitorScript = "";
        Targets = new List<FrameScopeTarget>();
    }

    public int PollIntervalMs { get; set; }
    public string DataRoot { get; set; }
    public bool OpenReportOnComplete { get; set; }
    public string MonitorScript { get; set; }
    public List<FrameScopeTarget> Targets { get; set; }
}

public sealed class FrameScopeTarget
{
    public FrameScopeTarget()
    {
        Name = "";
        ProcessName = "";
        SampleIntervalMs = 100;
        ProcessSampleIntervalMs = 250;
        SlowSampleIntervalMs = 1000;
        OpenReportOnComplete = true;
    }

    public bool Enabled { get; set; }
    public string Name { get; set; }
    public string ProcessName { get; set; }
    public int SampleIntervalMs { get; set; }
    public int ProcessSampleIntervalMs { get; set; }
    public int SlowSampleIntervalMs { get; set; }
    public bool OpenReportOnComplete { get; set; }
}

public sealed class FrameScopeHistoryEntry
{
    public FrameScopeHistoryEntry()
    {
        Time = "";
        Game = "";
        ProcessName = "";
        RunDir = "";
        ReportHtml = "";
        PresentMonCsv = "";
        ProcessCsv = "";
        SystemCsv = "";
        SummaryPath = "";
    }

    public string Time { get; set; }
    public string Game { get; set; }
    public string ProcessName { get; set; }
    public string RunDir { get; set; }
    public string ReportHtml { get; set; }
    public string PresentMonCsv { get; set; }
    public string ProcessCsv { get; set; }
    public string SystemCsv { get; set; }
    public string SummaryPath { get; set; }
}

internal static class FrameScopeNativeMonitor
{
    private static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string ConfigPath = Path.Combine(Root, "framescope-config.json");
    private static readonly string WatcherScript = Path.Combine(Root, "FrameScopeWatcher.ps1");
    private static readonly string MonitorScript = Path.Combine(Root, "Monitor-CS2-HighFreq.ps1");
    private static readonly string StatePath = Path.Combine(Root, "framescope-watcher-state.json");
    private static readonly string HistoryPath = Path.Combine(Root, "framescope-history.jsonl");
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    private static Form form;
    private static DataGridView grid;
    private static ComboBox processCombo;
    private static TextBox dataRootText;
    private static CheckBox autoOpenCheck;
    private static Label statusLabel;
    private static Button startButton;
    private static Timer statusTimer;
    private static bool pulse;

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var config = LoadConfig();
        BuildUi(config);
        RefreshProcessList();
        Application.Run(form);
    }

    private static FrameScopeConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            var created = CreateDefaultConfig();
            SaveConfig(created);
            return created;
        }

        try
        {
            var config = Json.Deserialize<FrameScopeConfig>(File.ReadAllText(ConfigPath));
            NormalizeConfig(config);
            return config;
        }
        catch
        {
            return CreateDefaultConfig();
        }
    }

    private static FrameScopeConfig CreateDefaultConfig()
    {
        var config = new FrameScopeConfig
        {
            PollIntervalMs = 500,
            DataRoot = Path.Combine(Root, "framescope-runs"),
            OpenReportOnComplete = true,
            MonitorScript = MonitorScript,
            Targets = new List<FrameScopeTarget>
            {
                Target(true, "Counter-Strike 2", "cs2.exe"),
                Target(true, "Delta Force", "DeltaForceClient-Win64-Shipping.exe"),
                Target(true, "Neverness To Everness", "HTGame.exe"),
                Target(false, "Valorant", "VALORANT-Win64-Shipping.exe"),
                Target(false, "Cyberpunk 2077", "Cyberpunk2077.exe"),
                Target(false, "Battlefield 6", "bf6.exe"),
                Target(false, "Hogwarts Legacy", "HogwartsLegacy.exe"),
                Target(false, "OPUS Prism Peak", "OPUS_ Prism Peak.exe")
            }
        };
        return config;
    }

    private static FrameScopeTarget Target(bool enabled, string name, string processName)
    {
        return new FrameScopeTarget
        {
            Enabled = enabled,
            Name = name,
            ProcessName = processName,
            SampleIntervalMs = 100,
            ProcessSampleIntervalMs = 250,
            SlowSampleIntervalMs = 1000,
            OpenReportOnComplete = true
        };
    }

    private static void NormalizeConfig(FrameScopeConfig config)
    {
        if (config == null) throw new InvalidOperationException("FrameScope config is empty.");
        if (config.Targets == null) config.Targets = new List<FrameScopeTarget>();
        if (string.IsNullOrWhiteSpace(config.DataRoot)) config.DataRoot = Path.Combine(Root, "framescope-runs");
        if (string.IsNullOrWhiteSpace(config.MonitorScript) || !File.Exists(config.MonitorScript)) config.MonitorScript = MonitorScript;
        if (config.PollIntervalMs <= 0) config.PollIntervalMs = 500;
        foreach (var target in config.Targets)
        {
            if (target == null) continue;
            if (target.SampleIntervalMs < 50) target.SampleIntervalMs = 100;
            if (target.ProcessSampleIntervalMs < 250) target.ProcessSampleIntervalMs = 250;
            if (target.SlowSampleIntervalMs < target.SampleIntervalMs) target.SlowSampleIntervalMs = 1000;
        }
    }

    private static void SaveConfig(FrameScopeConfig config)
    {
        NormalizeConfig(config);
        File.WriteAllText(ConfigPath, Json.Serialize(config));
    }

    private static void BuildUi(FrameScopeConfig config)
    {
        form = new Form
        {
            Text = "FrameScope Monitor",
            StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(1080, 680),
            MinimumSize = new Size(980, 610),
            BackColor = Color.FromArgb(17, 26, 36),
            ForeColor = Color.FromArgb(239, 247, 255),
            Font = new Font("Microsoft YaHei UI", 9f),
            Opacity = 0
        };
        form.Shown += (_, __) => FadeIn(form);

        var title = new Label
        {
            Text = "FrameScope Monitor",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 211, 91),
            Location = new Point(18, 14),
            Size = new Size(420, 42)
        };
        form.Controls.Add(title);

        var subtitle = new Label
        {
            Text = "Select game processes to monitor; reports open after capture and CSV paths are saved.",
            ForeColor = Color.FromArgb(159, 180, 196),
            Location = new Point(22, 58),
            Size = new Size(920, 24)
        };
        form.Controls.Add(subtitle);

        grid = new DataGridView
        {
            Location = new Point(20, 96),
            Size = new Size(1020, 340),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = true,
            AllowUserToResizeColumns = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BackgroundColor = Color.FromArgb(20, 29, 40),
            GridColor = Color.FromArgb(56, 84, 103),
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeight = 30,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles = false
        };
        grid.RowTemplate.Height = 28;
        grid.RowTemplate.Resizable = DataGridViewTriState.False;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(33, 49, 69);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(33, 49, 69);
        grid.DefaultCellStyle.BackColor = Color.FromArgb(20, 29, 40);
        grid.DefaultCellStyle.ForeColor = Color.FromArgb(239, 247, 255);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(20, 116, 190);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(24, 36, 50);
        grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(239, 247, 255);

        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "On", FillWeight = 42 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "GameName", HeaderText = "Name", FillWeight = 150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProcessName", HeaderText = "Process", FillWeight = 230 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SampleMs", HeaderText = "Sample(ms)", FillWeight = 62 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "AutoOpen", HeaderText = "Open report", FillWeight = 86 });
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.Resizable = DataGridViewTriState.False;
        }

        foreach (var target in config.Targets)
        {
            grid.Rows.Add(target.Enabled, target.Name, target.ProcessName, target.SampleIntervalMs, target.OpenReportOnComplete);
        }
        form.Controls.Add(grid);

        processCombo = new ComboBox
        {
            Location = new Point(20, 450),
            Size = new Size(300, 28),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            DropDownStyle = ComboBoxStyle.DropDown,
            BackColor = Color.FromArgb(20, 29, 40),
            ForeColor = Color.FromArgb(239, 247, 255)
        };
        form.Controls.Add(processCombo);

        var refreshButton = Button("Refresh", 330, 449, 90, 30);
        refreshButton.Click += (_, __) => RefreshProcessList();
        form.Controls.Add(refreshButton);

        var addButton = Button("Add", 430, 449, 90, 30);
        addButton.Click += (_, __) => AddSelectedProcess();
        form.Controls.Add(addButton);

        form.Controls.Add(new Label { Text = "Data root", Location = new Point(20, 496), Size = new Size(70, 24), Anchor = AnchorStyles.Left | AnchorStyles.Bottom, ForeColor = Color.FromArgb(239, 247, 255) });
        dataRootText = new TextBox
        {
            Text = config.DataRoot,
            Location = new Point(90, 494),
            Size = new Size(640, 26),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Color.FromArgb(20, 29, 40),
            ForeColor = Color.FromArgb(239, 247, 255),
            BorderStyle = BorderStyle.FixedSingle
        };
        form.Controls.Add(dataRootText);

        var browseButton = Button("Browse", 740, 492, 70, 30);
        browseButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        browseButton.Click += (_, __) => BrowseDataRoot();
        form.Controls.Add(browseButton);

        autoOpenCheck = new CheckBox
        {
            Text = "Open report after capture completes",
            Checked = config.OpenReportOnComplete,
            Location = new Point(830, 496),
            Size = new Size(210, 24),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            ForeColor = Color.FromArgb(239, 247, 255)
        };
        form.Controls.Add(autoOpenCheck);

        var saveButton = Button("Save", 20, 542, 100, 34);
        saveButton.Click += (_, __) => SaveConfigFromGrid();
        form.Controls.Add(saveButton);

        startButton = Button("Start", 130, 542, 100, 34);
        startButton.Click += (_, __) => StartWatcher();
        form.Controls.Add(startButton);

        var stopButton = Button("Stop", 240, 542, 100, 34);
        stopButton.Click += (_, __) => StopWatcher();
        form.Controls.Add(stopButton);

        var openDataButton = Button("Data folder", 350, 542, 115, 34);
        openDataButton.Click += (_, __) => OpenDataRoot();
        form.Controls.Add(openDataButton);

        var openLatestButton = Button("Latest report", 475, 542, 115, 34);
        openLatestButton.Click += (_, __) => OpenLatestReport();
        form.Controls.Add(openLatestButton);

        var openHistoryButton = Button("History", 600, 542, 115, 34);
        openHistoryButton.Click += (_, __) => OpenHistory();
        form.Controls.Add(openHistoryButton);

        statusLabel = new Label
        {
            Text = "Status: idle",
            ForeColor = Color.FromArgb(169, 255, 71),
            Location = new Point(20, 592),
            Size = new Size(1000, 28),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        form.Controls.Add(statusLabel);

        statusTimer = new Timer { Interval = 2500 };
        statusTimer.Tick += (_, __) => UpdateWatcherStatus();
        statusTimer.Start();
    }

    private static Button Button(string text, int x, int y, int width, int height)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(24, 36, 50),
            ForeColor = Color.FromArgb(239, 247, 255)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(113, 166, 197);
        button.MouseEnter += (_, __) => button.BackColor = Color.FromArgb(34, 68, 92);
        button.MouseLeave += (_, __) => button.BackColor = Color.FromArgb(24, 36, 50);
        button.MouseDown += (_, __) => button.BackColor = Color.FromArgb(38, 112, 145);
        button.MouseUp += (_, __) => button.BackColor = Color.FromArgb(34, 68, 92);
        return button;
    }

    private static void FadeIn(Form target)
    {
        var timer = new Timer { Interval = 15 };
        timer.Tick += (_, __) =>
        {
            if (target.IsDisposed)
            {
                timer.Stop();
                timer.Dispose();
                return;
            }
            target.Opacity = Math.Min(1, target.Opacity + 0.08);
            if (target.Opacity >= 1)
            {
                timer.Stop();
                timer.Dispose();
            }
        };
        timer.Start();
    }

    private static void SetStatus(string text)
    {
        statusLabel.Text = "Status: " + text;
        statusLabel.ForeColor = Color.FromArgb(41, 230, 255);
        var timer = new Timer { Interval = 180 };
        timer.Tick += (_, __) =>
        {
            statusLabel.ForeColor = Color.FromArgb(169, 255, 71);
            timer.Stop();
            timer.Dispose();
        };
        timer.Start();
    }

    private static FrameScopeConfig ReadGridConfig()
    {
        var targets = new List<FrameScopeTarget>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            var processName = Convert.ToString(row.Cells["ProcessName"].Value) ?? "";
            if (string.IsNullOrWhiteSpace(processName)) continue;
            var name = Convert.ToString(row.Cells["GameName"].Value);
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(processName);
            int sampleMs;
            if (!int.TryParse(Convert.ToString(row.Cells["SampleMs"].Value), out sampleMs)) sampleMs = 100;
            if (sampleMs < 50) sampleMs = 50;
            targets.Add(new FrameScopeTarget
            {
                Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? false),
                Name = name ?? processName,
                ProcessName = processName,
                SampleIntervalMs = sampleMs,
                ProcessSampleIntervalMs = 250,
                SlowSampleIntervalMs = 1000,
                OpenReportOnComplete = Convert.ToBoolean(row.Cells["AutoOpen"].Value ?? true)
            });
        }

        return new FrameScopeConfig
        {
            PollIntervalMs = 500,
            DataRoot = dataRootText.Text,
            OpenReportOnComplete = autoOpenCheck.Checked,
            MonitorScript = MonitorScript,
            Targets = targets
        };
    }

    private static void SaveConfigFromGrid()
    {
        try
        {
            SaveConfig(ReadGridConfig());
            SetStatus("Saved config: " + ConfigPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void RefreshProcessList()
    {
        processCombo.Items.Clear();
        foreach (var name in Process.GetProcesses().Select(p => p.ProcessName + ".exe").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v))
        {
            processCombo.Items.Add(name);
        }
        SetStatus("Process list refreshed.");
    }

    private static void AddSelectedProcess()
    {
        var processName = processCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(processName)) return;
        if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) processName += ".exe";
        grid.Rows.Add(true, Path.GetFileNameWithoutExtension(processName), processName, 100, true);
        SetStatus("Added " + processName);
    }

    private static void BrowseDataRoot()
    {
        using (var dialog = new FolderBrowserDialog { Description = "Select FrameScope data folder" })
        {
            if (dialog.ShowDialog(form) == DialogResult.OK)
            {
                dataRootText.Text = dialog.SelectedPath;
            }
        }
    }

    private static bool IsWatcherRunning(out int pid)
    {
        pid = 0;
        if (!File.Exists(StatePath)) return false;
        try
        {
            var state = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath));
            if (!state.ContainsKey("WatcherPid")) return false;
            pid = Convert.ToInt32(state["WatcherPid"]);
            Process.GetProcessById(pid);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void StartWatcher()
    {
        try
        {
            SaveConfig(ReadGridConfig());
            int existingPid;
            if (IsWatcherRunning(out existingPid))
            {
                SetStatus("Monitor already running, Watcher PID=" + existingPid);
                return;
            }

            if (!File.Exists(WatcherScript))
            {
                MessageBox.Show("FrameScopeWatcher.ps1 not found.", "Start failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            var psi = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Quote(WatcherScript) + " -ConfigPath " + Quote(ConfigPath),
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var proc = Process.Start(psi);
            SetStatus("Monitor started, Watcher PID=" + (proc != null ? proc.Id.ToString() : "unknown") + ". Launch a configured game to start capture.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Start failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void StopWatcher()
    {
        int pid;
        if (!IsWatcherRunning(out pid))
        {
            SetStatus("No running FrameScope watcher");
            return;
        }
        try
        {
            Process.GetProcessById(pid).Kill();
            SetStatus("Monitor stopped.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Stop failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void UpdateWatcherStatus()
    {
        int pid;
        if (IsWatcherRunning(out pid))
        {
            pulse = !pulse;
            startButton.BackColor = pulse ? Color.FromArgb(30, 96, 78) : Color.FromArgb(24, 36, 50);
            string text = "Running, Watcher PID=" + pid;
            try
            {
                var state = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath));
                if (state.ContainsKey("CompletedRuns")) text += ", completed " + state["CompletedRuns"] + " run(s)";
                if (state.ContainsKey("LastReport") && state["LastReport"] != null && state["LastReport"].ToString() != "") text += ", latest report: " + state["LastReport"];
            }
            catch { }
            statusLabel.Text = "Status: " + text;
        }
        else if (startButton.BackColor != Color.FromArgb(24, 36, 50))
        {
            startButton.BackColor = Color.FromArgb(24, 36, 50);
        }
    }

    private static void OpenDataRoot()
    {
        var path = dataRootText.Text;
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private static FrameScopeHistoryEntry LatestHistory()
    {
        if (!File.Exists(HistoryPath)) return null;
        var line = File.ReadLines(HistoryPath).LastOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (line == null) return null;
        try { return Json.Deserialize<FrameScopeHistoryEntry>(line); }
        catch { return null; }
    }

    private static string LatestReportPath()
    {
        var entry = LatestHistory();
        if (entry != null && File.Exists(entry.ReportHtml)) return entry.ReportHtml;

        var root = dataRootText != null ? dataRootText.Text : Path.Combine(Root, "framescope-runs");
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Root, "framescope-runs");
        if (!Path.IsPathRooted(root)) root = Path.Combine(Root, root);
        if (!Directory.Exists(root)) return "";

        try
        {
            return Directory.GetFiles(root, "framescope-interactive-report.html", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void OpenLatestReport()
    {
        var reportPath = LatestReportPath();
        if (!string.IsNullOrWhiteSpace(reportPath) && File.Exists(reportPath))
        {
            Process.Start(new ProcessStartInfo { FileName = reportPath, UseShellExecute = true });
            SetStatus("Opened latest report: " + reportPath);
            return;
        }
        SetStatus("No report found.");
    }

    private static void OpenHistory()
    {
        if (!File.Exists(HistoryPath)) File.WriteAllText(HistoryPath, "");
        Process.Start(new ProcessStartInfo { FileName = HistoryPath, UseShellExecute = true });
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}

