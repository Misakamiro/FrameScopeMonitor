using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class FrameScopeSetupNative
{
    private const string ResourceName = "FrameScopePayload";
    private static InstallerForm form;

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        form = new InstallerForm();
        Application.Run(form);
    }

    internal static void BeginInstall()
    {
        ThreadPool.QueueUserWorkItem(_ => Install());
    }

    private static void Install()
    {
        string appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameScopeMonitor");
        string logPath = Path.Combine(appDir, "install.log");
        try
        {
            Directory.CreateDirectory(appDir);
            Log(logPath, "install-start");
            Update("正在停止旧的 FrameScope Monitor 进程...", 2);
            foreach (var proc in Process.GetProcessesByName("FrameScopeMonitor"))
            {
                try { proc.Kill(); proc.WaitForExit(3000); }
                catch { }
            }

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

            Update("正在创建快捷方式...", 95);
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "FrameScope Monitor.lnk"),
                Path.Combine(appDir, "FrameScopeMonitor.exe"),
                appDir);
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs\\FrameScope Monitor.lnk"),
                Path.Combine(appDir, "FrameScopeMonitor.exe"),
                appDir);

            Update("安装完成，正在启动 FrameScope Monitor...", 100);
            Log(logPath, "install-complete");
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(appDir, "FrameScopeMonitor.exe"),
                WorkingDirectory = appDir,
                UseShellExecute = true
            });
            Thread.Sleep(900);
            form.Finish(true, "安装完成。软件已安装到：" + appDir);
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
        private readonly ProgressBar progress;
        private readonly Button closeButton;
        private bool finished;

        public InstallerForm()
        {
            Text = "FrameScope Monitor Setup";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(560, 220);
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
                Size = new Size(500, 34)
            };
            Controls.Add(title);

            status = new Label
            {
                Text = "准备安装...",
                Location = new Point(24, 68),
                Size = new Size(500, 42),
                ForeColor = Color.FromArgb(189, 208, 222)
            };
            Controls.Add(status);

            progress = new ProgressBar
            {
                Location = new Point(24, 118),
                Size = new Size(500, 22),
                Minimum = 0,
                Maximum = 100
            };
            Controls.Add(progress);

            closeButton = new Button
            {
                Text = "关闭",
                Enabled = false,
                Location = new Point(430, 152),
                Size = new Size(94, 30)
            };
            closeButton.Click += (sender, args) => Close();
            Controls.Add(closeButton);

            Shown += (sender, args) => BeginInstall();
            FormClosing += (sender, args) =>
            {
                if (!finished)
                {
                    args.Cancel = true;
                    MessageBox.Show(this, "安装正在进行，请等待完成。", "FrameScope Monitor Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
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
            status.Text = message;
            closeButton.Enabled = true;
            if (!success)
            {
                progress.Value = 0;
                MessageBox.Show(this, message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
