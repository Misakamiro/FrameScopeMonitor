using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class FrameScopeSetupNative
{
    private const string ResourceName = "FrameScopePayload";
    private const string AppVersion = "1.1.0";
    private const string Publisher = "Misakamiro";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FrameScopeMonitor";
    private static InstallerForm form;

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        form = new InstallerForm();
        Application.Run(form);
    }

    internal static void BeginInstall(string dataRoot)
    {
        ThreadPool.QueueUserWorkItem(_ => Install(dataRoot));
    }

    private static void Install(string requestedDataRoot)
    {
        string appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameScopeMonitor");
        string logPath = Path.Combine(appDir, "install.log");
        string dataRoot = NormalizeDataRoot(requestedDataRoot);
        try
        {
            if (IsSameOrChildPath(dataRoot, appDir))
            {
                throw new InvalidOperationException("数据和报告目录不能放在程序安装目录内。请改选其他目录，例如 %LOCALAPPDATA%\\FrameScopeMonitorData\\framescope-runs。");
            }

            Directory.CreateDirectory(appDir);
            Log(logPath, "install-start dataRoot=" + dataRoot);
            Update("正在停止旧的 FrameScope Monitor 进程...", 2);
            foreach (var proc in Process.GetProcessesByName("FrameScopeMonitor"))
            {
                try { proc.Kill(); proc.WaitForExit(3000); }
                catch { }
            }

            Update("正在清理旧版组件...", 4);
            CleanStaleComponents(appDir);

            Update("正在迁移历史数据目录...", 5);
            MigrateDataRoot(appDir, dataRoot);

            Update("正在读取内置安装包...", 5);
            var assembly = Assembly.GetExecutingAssembly();
            using (var payload = assembly.GetManifestResourceStream(ResourceName))
            {
                if (payload == null)
                {
                    throw new InvalidOperationException("安装包损坏：找不到内置 payload。");
                }

                using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                {
                    int total = Math.Max(1, archive.Entries.Count);
                    int done = 0;
                    foreach (var entry in archive.Entries)
                    {
                        done++;
                        string targetPath = SafeCombine(appDir, entry.FullName);
                        int percent = 5 + (int)Math.Round(done * 88.0 / total);

                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(targetPath);
                            Update("正在创建目录：" + entry.FullName, percent);
                            continue;
                        }

                        string parent = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                        if (File.Exists(targetPath)) File.Delete(targetPath);
                        entry.ExtractToFile(targetPath);

                        if ((done % 40) == 0 || done == total)
                        {
                            Update("正在安装文件：" + entry.FullName, percent);
                        }
                    }
                }
            }

            Update("正在写入数据目录配置...", 94);
            EnsureConfigDataRoot(appDir, dataRoot);

            Update("正在创建快捷方式...", 95);
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "FrameScope Monitor.lnk"),
                Path.Combine(appDir, "FrameScopeMonitor.exe"),
                appDir);
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs\\FrameScope Monitor.lnk"),
                Path.Combine(appDir, "FrameScopeMonitor.exe"),
                appDir);

            Update("正在写入系统卸载信息...", 98);
            WriteUninstallRegistration(appDir, dataRoot);

            Update("安装完成，正在启动 FrameScope Monitor...", 100);
            Log(logPath, "install-complete");
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(appDir, "FrameScopeMonitor.exe"),
                WorkingDirectory = appDir,
                UseShellExecute = true
            });
            Thread.Sleep(900);
            form.Finish(true, "安装完成。软件已安装到：" + appDir + "\r\n数据和报告目录：" + dataRoot);
        }
        catch (Exception ex)
        {
            try { Log(logPath, "install-failed " + ex); } catch { }
            form.Finish(false, ex.Message);
        }
    }

    private static string SafeCombine(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("安装包内存在非法路径：" + relative);
        }
        return fullPath;
    }

    private static void CleanStaleComponents(string appDir)
    {
        string[] staleItems =
        {
            "Generate-CS2-FrameScope-Interactive-Report.py",
            "Monitor-CS2-HighFreq.ps1",
            "FrameScopeWatcher.ps1",
            "runtime",
            "__pycache__",
            Path.Combine("tools", "tools")
        };

        foreach (string relative in staleItems)
        {
            string path = SafeCombine(appDir, relative);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch { }
        }
    }

    private static string GetDefaultDataRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrameScopeMonitorData",
            "framescope-runs");
    }

    internal static string GetExistingOrDefaultDataRoot()
    {
        string configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrameScopeMonitor",
            "framescope-config.json");
        string configured = ReadConfigDataRoot(configPath);
        return NormalizeDataRoot(string.IsNullOrWhiteSpace(configured) ? GetDefaultDataRoot() : configured);
    }

    private static string NormalizeDataRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = GetDefaultDataRoot();
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Path.GetFullPath(path);
    }

    private static bool IsSameOrChildPath(string child, string parent)
    {
        string fullChild = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullChild.Equals(fullParent, StringComparison.OrdinalIgnoreCase) ||
               fullChild.StartsWith(fullParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullChild.StartsWith(fullParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadConfigDataRoot(string configPath)
    {
        try
        {
            if (!File.Exists(configPath)) return "";
            string text = File.ReadAllText(configPath);
            Match match = Regex.Match(
                text,
                "\"DataRoot\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"",
                RegexOptions.IgnoreCase);
            return match.Success ? JsonUnescape(match.Groups[1].Value) : "";
        }
        catch
        {
            return "";
        }
    }

    private static string JsonUnescape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\")
            .Replace("\\/", "/");
    }

    private static void MigrateDataRoot(string appDir, string dataRoot)
    {
        string oldRoot = Path.Combine(appDir, "framescope-runs");
        string newRoot = NormalizeDataRoot(dataRoot);
        try
        {
            if (Directory.Exists(oldRoot))
            {
                string parent = Path.GetDirectoryName(newRoot);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                if (!Directory.Exists(newRoot))
                {
                    Directory.Move(oldRoot, newRoot);
                }
                else
                {
                    MoveDirectoryContents(oldRoot, newRoot);
                    TryDeleteDirectory(oldRoot);
                }
            }
        }
        catch { }

        UpdateConfigDataRoot(Path.Combine(appDir, "framescope-config.json"), newRoot);
        ReplacePathInTextFile(Path.Combine(appDir, "framescope-history.jsonl"), oldRoot, newRoot);
        ReplacePathInTextFile(Path.Combine(appDir, "framescope-watcher-state.json"), oldRoot, newRoot);
    }

    private static void EnsureConfigDataRoot(string appDir, string dataRoot)
    {
        string configPath = Path.Combine(appDir, "framescope-config.json");
        string normalized = NormalizeDataRoot(dataRoot);
        try
        {
            Directory.CreateDirectory(normalized);
            if (File.Exists(configPath))
            {
                UpdateConfigDataRoot(configPath, normalized);
            }
            else
            {
                File.WriteAllText(configPath, CreateDefaultConfigJson(normalized));
            }
        }
        catch { }
    }

    private static string CreateDefaultConfigJson(string dataRoot)
    {
        string escaped = JsonEscape(dataRoot);
        return
            "{\"PollIntervalMs\":1000,\"DataRoot\":\"" + escaped + "\",\"OpenReportOnComplete\":true,\"MonitorScript\":\"native-csharp\",\"Targets\":[" +
            "{\"Enabled\":true,\"Name\":\"Counter-Strike 2\",\"ProcessName\":\"cs2.exe\",\"SampleIntervalMs\":100,\"ProcessSampleIntervalMs\":100,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}," +
            "{\"Enabled\":true,\"Name\":\"PUBG: BATTLEGROUNDS\",\"ProcessName\":\"TslGame.exe\",\"SampleIntervalMs\":100,\"ProcessSampleIntervalMs\":100,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}," +
            "{\"Enabled\":true,\"Name\":\"Delta Force\",\"ProcessName\":\"DeltaForceClient-Win64-Shipping.exe\",\"SampleIntervalMs\":100,\"ProcessSampleIntervalMs\":100,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}," +
            "{\"Enabled\":true,\"Name\":\"Neverness To Everness\",\"ProcessName\":\"HTGame.exe\",\"SampleIntervalMs\":100,\"ProcessSampleIntervalMs\":100,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}," +
            "{\"Enabled\":true,\"Name\":\"Valorant\",\"ProcessName\":\"VALORANT-Win64-Shipping.exe\",\"SampleIntervalMs\":100,\"ProcessSampleIntervalMs\":100,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}," +
            "{\"Enabled\":false,\"Name\":\"Cyberpunk 2077\",\"ProcessName\":\"Cyberpunk2077.exe\",\"SampleIntervalMs\":100,\"ProcessSampleIntervalMs\":100,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}," +
            "{\"Enabled\":true,\"Name\":\"Battlefield 6\",\"ProcessName\":\"bf6.exe\",\"SampleIntervalMs\":100,\"ProcessSampleIntervalMs\":100,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}," +
            "{\"Enabled\":false,\"Name\":\"Hogwarts Legacy\",\"ProcessName\":\"HogwartsLegacy.exe\",\"SampleIntervalMs\":100,\"ProcessSampleIntervalMs\":100,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}," +
            "{\"Enabled\":false,\"Name\":\"OPUS Prism Peak\",\"ProcessName\":\"OPUS_ Prism Peak.exe\",\"SampleIntervalMs\":100,\"ProcessSampleIntervalMs\":100,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}" +
            "]}";
    }

    private static void MoveDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(file));
            if (File.Exists(target))
            {
                string renamed = Path.Combine(
                    destination,
                    Path.GetFileNameWithoutExtension(file) + "-migrated-" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(file));
                File.Move(file, renamed);
            }
            else
            {
                File.Move(file, target);
            }
        }
        foreach (string dir in Directory.GetDirectories(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(dir));
            if (Directory.Exists(target))
            {
                MoveDirectoryContents(dir, target);
                TryDeleteDirectory(dir);
            }
            else
            {
                Directory.Move(dir, target);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }

    private static void UpdateConfigDataRoot(string configPath, string dataRoot)
    {
        try
        {
            if (!File.Exists(configPath)) return;
            string text = File.ReadAllText(configPath);
            string replacement = "\"DataRoot\":\"" + JsonEscape(dataRoot) + "\"";
            string updated = Regex.Replace(
                text,
                "\"DataRoot\"\\s*:\\s*\"(?:\\\\.|[^\"])*\"",
                replacement,
                RegexOptions.IgnoreCase);
            if (!updated.Equals(text, StringComparison.Ordinal))
            {
                File.WriteAllText(configPath, updated);
            }
        }
        catch { }
    }

    private static void ReplacePathInTextFile(string path, string oldPath, string newPath)
    {
        try
        {
            if (!File.Exists(path)) return;
            string text = File.ReadAllText(path);
            string updated = text
                .Replace(oldPath, newPath)
                .Replace(JsonEscape(oldPath), JsonEscape(newPath));
            if (!updated.Equals(text, StringComparison.Ordinal)) File.WriteAllText(path, updated);
        }
        catch { }
    }

    private static string JsonEscape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static void WriteUninstallRegistration(string appDir, string dataRoot)
    {
        string exePath = Path.Combine(appDir, "FrameScopeMonitor.exe");
        string uninstallPath = Path.Combine(appDir, "FrameScopeUninstaller.exe");
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
        {
            if (key == null) return;
            key.SetValue("DisplayName", "FrameScope Monitor", RegistryValueKind.String);
            key.SetValue("DisplayVersion", AppVersion, RegistryValueKind.String);
            key.SetValue("Publisher", Publisher, RegistryValueKind.String);
            key.SetValue("InstallLocation", appDir, RegistryValueKind.String);
            key.SetValue("DataRoot", NormalizeDataRoot(dataRoot), RegistryValueKind.String);
            key.SetValue("DisplayIcon", exePath, RegistryValueKind.String);
            key.SetValue("UninstallString", "\"" + uninstallPath + "\"", RegistryValueKind.String);
            key.SetValue("QuietUninstallString", "\"" + uninstallPath + "\" /quiet", RegistryValueKind.String);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", Math.Max(1, GetDirectorySizeKb(appDir)), RegistryValueKind.DWord);
        }
    }

    private static int GetDirectorySizeKb(string dir)
    {
        try
        {
            long bytes = 0;
            Stack<string> pending = new Stack<string>();
            pending.Push(dir);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                foreach (string file in Directory.GetFiles(current))
                {
                    try { bytes += new FileInfo(file).Length; }
                    catch { }
                }
                foreach (string child in Directory.GetDirectories(current))
                {
                    string name = Path.GetFileName(child);
                    if (name.Equals("framescope-runs", StringComparison.OrdinalIgnoreCase)) continue;
                    pending.Push(child);
                }
            }
            long kb = (bytes + 1023L) / 1024L;
            return kb > int.MaxValue ? int.MaxValue : (int)kb;
        }
        catch
        {
            return 1;
        }
    }

    private static void Update(string text, int percent)
    {
        form.UpdateProgress(text, Math.Max(0, Math.Min(100, percent)));
    }

    private static void Log(string path, string message)
    {
        File.AppendAllText(path, DateTime.Now.ToString("o") + " " + message + Environment.NewLine);
    }

    private static void CreateShortcut(string linkPath, string targetPath, string workingDirectory)
    {
        string parent = Path.GetDirectoryName(linkPath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        object shell = Activator.CreateInstance(shellType);
        object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
        Type shortcutType = shortcut.GetType();
        shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
        shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
        shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
        shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
    }

    internal sealed class InstallerForm : Form
    {
        private readonly Label status;
        private readonly TextBox dataRootText;
        private readonly ProgressBar progress;
        private readonly Button browseButton;
        private readonly Button installButton;
        private readonly Button closeButton;
        private bool installing;
        private bool finished;

        public InstallerForm()
        {
            Text = "FrameScope Monitor Setup";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(680, 310);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.FromArgb(17, 26, 36);
            ForeColor = Color.FromArgb(239, 247, 255);
            Font = new Font("Microsoft YaHei UI", 9f);

            var title = new Label
            {
                Text = "FrameScope Monitor 安装器",
                Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 211, 91),
                Location = new Point(22, 20),
                Size = new Size(610, 34)
            };
            Controls.Add(title);

            var dataLabel = new Label
            {
                Text = "数据和报告目录",
                Location = new Point(24, 64),
                Size = new Size(120, 24),
                ForeColor = Color.FromArgb(239, 247, 255)
            };
            Controls.Add(dataLabel);

            dataRootText = new TextBox
            {
                Text = FrameScopeSetupNative.GetExistingOrDefaultDataRoot(),
                Location = new Point(24, 90),
                Size = new Size(520, 26),
                BackColor = Color.FromArgb(20, 29, 40),
                ForeColor = Color.FromArgb(239, 247, 255),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(dataRootText);

            browseButton = new Button
            {
                Text = "选择",
                Location = new Point(555, 88),
                Size = new Size(86, 30)
            };
            browseButton.Click += (sender, args) => BrowseDataRoot();
            Controls.Add(browseButton);

            var hint = new Label
            {
                Text = "这里会保存 CSV 原始数据和 HTML 报告。建议不要放在程序安装目录里，避免卸载工具把历史数据算成软件体积。",
                Location = new Point(24, 124),
                Size = new Size(620, 38),
                ForeColor = Color.FromArgb(189, 208, 222)
            };
            Controls.Add(hint);

            status = new Label
            {
                Text = "选择目录后点击“开始安装”。",
                Location = new Point(24, 166),
                Size = new Size(620, 42),
                ForeColor = Color.FromArgb(189, 208, 222)
            };
            Controls.Add(status);

            progress = new ProgressBar
            {
                Location = new Point(24, 210),
                Size = new Size(617, 22),
                Minimum = 0,
                Maximum = 100
            };
            Controls.Add(progress);

            installButton = new Button
            {
                Text = "开始安装",
                Location = new Point(430, 242),
                Size = new Size(100, 30)
            };
            installButton.Click += (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(dataRootText.Text))
                {
                    MessageBox.Show(this, "请选择数据和报告目录。", "FrameScope Monitor Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                installing = true;
                finished = false;
                dataRootText.Enabled = false;
                browseButton.Enabled = false;
                installButton.Enabled = false;
                closeButton.Enabled = false;
                BeginInstall(dataRootText.Text);
            };
            Controls.Add(installButton);

            closeButton = new Button
            {
                Text = "关闭",
                Enabled = true,
                Location = new Point(547, 242),
                Size = new Size(94, 30)
            };
            closeButton.Click += (sender, args) => Close();
            Controls.Add(closeButton);

            FormClosing += (sender, args) =>
            {
                if (installing && !finished)
                {
                    args.Cancel = true;
                    MessageBox.Show(this, "安装正在进行，请等待完成。", "FrameScope Monitor Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
        }

        private void BrowseDataRoot()
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择 FrameScope 数据和报告目录" })
            {
                if (Directory.Exists(dataRootText.Text)) dialog.SelectedPath = dataRootText.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    dataRootText.Text = dialog.SelectedPath;
                }
            }
        }

        public void UpdateProgress(string text, int percent)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, int>(UpdateProgress), text, percent);
                return;
            }
            status.Text = text;
            progress.Value = percent;
        }

        public void Finish(bool success, string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool, string>(Finish), success, message);
                return;
            }
            finished = true;
            installing = false;
            status.Text = message;
            closeButton.Enabled = true;
            closeButton.Text = "关闭";
            if (!success)
            {
                progress.Value = 0;
                dataRootText.Enabled = true;
                browseButton.Enabled = true;
                installButton.Enabled = true;
                MessageBox.Show(this, message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
