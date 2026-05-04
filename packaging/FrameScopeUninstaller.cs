using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class FrameScopeUninstaller
{
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FrameScopeMonitor";

    [STAThread]
    private static void Main(string[] args)
    {
        bool quiet = HasArg(args, "/quiet") || HasArg(args, "--quiet");
        bool deleteData = HasArg(args, "/delete-data") || HasArg(args, "--delete-data");
        string appDir = ResolveAppDir();
        string dataRoot = ResolveDataRoot(appDir);

        if (!quiet)
        {
            DialogResult uninstall = MessageBox.Show(
                "确定要卸载 FrameScope Monitor 吗？",
                "FrameScope Monitor 卸载",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (uninstall != DialogResult.OK) return;

            DialogResult removeData = MessageBox.Show(
                "是否同时删除数据和报告目录？\r\n\r\n" + dataRoot + "\r\n\r\n选择“否”会只卸载程序，保留历史报告和 CSV 数据。",
                "是否删除数据",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (removeData == DialogResult.Cancel) return;
            deleteData = removeData == DialogResult.Yes;
        }

        try
        {
            StopFrameScopeProcesses();
            DeleteShortcuts();
            if (deleteData) DeleteDirectoryIfSafe(dataRoot);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); } catch { }
            ScheduleAppDirectoryRemoval(appDir);
            if (!quiet)
            {
                MessageBox.Show(
                    deleteData ? "卸载完成，程序和数据目录已删除。" : "卸载完成，数据和报告目录已保留。",
                    "FrameScope Monitor 卸载",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            if (!quiet)
            {
                MessageBox.Show(ex.Message, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Environment.ExitCode = 1;
        }
    }

    private static bool HasArg(string[] args, string value)
    {
        if (args == null) return false;
        foreach (string arg in args)
        {
            if (arg.Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string ResolveAppDir()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath))
            {
                object value = key == null ? null : key.GetValue("InstallLocation");
                if (value != null && Directory.Exists(Convert.ToString(value))) return Convert.ToString(value);
            }
        }
        catch { }
        return AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ResolveDataRoot(string appDir)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath))
            {
                object value = key == null ? null : key.GetValue("DataRoot");
                if (value != null && !string.IsNullOrWhiteSpace(Convert.ToString(value))) return Path.GetFullPath(Convert.ToString(value));
            }
        }
        catch { }

        string fromConfig = ReadConfigDataRoot(Path.Combine(appDir, "framescope-config.json"));
        if (!string.IsNullOrWhiteSpace(fromConfig)) return Path.GetFullPath(fromConfig);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameScopeMonitorData", "framescope-runs");
    }

    private static string ReadConfigDataRoot(string configPath)
    {
        try
        {
            if (!File.Exists(configPath)) return "";
            string text = File.ReadAllText(configPath);
            Match match = Regex.Match(text, "\"DataRoot\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase);
            if (!match.Success) return "";
            return JsonUnescape(match.Groups[1].Value);
        }
        catch
        {
            return "";
        }
    }

    private static string JsonUnescape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\/", "/");
    }

    private static void StopFrameScopeProcesses()
    {
        int currentPid = Process.GetCurrentProcess().Id;
        string[] names =
        {
            "FrameScopeMonitor",
            "FrameScopeReportGenerator",
            "FrameScopeProcessSampler",
            "FrameScopeSystemSampler",
            "PresentMon-2.4.1-x64"
        };

        foreach (string name in names)
        {
            foreach (Process proc in Process.GetProcessesByName(name))
            {
                try
                {
                    if (proc.Id == currentPid) continue;
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
                catch { }
            }
        }
    }

    private static void DeleteShortcuts()
    {
        TryDeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "FrameScope Monitor.lnk"));
        TryDeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "FrameScope Monitor.lnk"));
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void DeleteDirectoryIfSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root) || full.TrimEnd('\\', '/') == root.TrimEnd('\\', '/')) return;
        if (full.Length < 10) return;
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }

    private static void ScheduleAppDirectoryRemoval(string appDir)
    {
        if (string.IsNullOrWhiteSpace(appDir)) return;
        string full = Path.GetFullPath(appDir);
        if (!Directory.Exists(full)) return;

        string tempCmd = Path.Combine(Path.GetTempPath(), "FrameScopeMonitor-Uninstall-" + Guid.NewGuid().ToString("N") + ".cmd");
        string script =
            "@echo off\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            "rmdir /s /q \"" + full + "\"\r\n" +
            "del /f /q \"" + tempCmd + "\" >nul 2>nul\r\n";
        File.WriteAllText(tempCmd, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/c \"" + tempCmd + "\"",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath()
        });
    }
}
