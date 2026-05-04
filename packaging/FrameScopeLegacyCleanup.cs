using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class FrameScopeLegacyCleanup
{
    private const string ProductName = "FrameScope Monitor";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FrameScopeMonitor";
    private static readonly object LogLock = new object();
    private static string logPath;
    private static CleanupForm form;
    private static bool dryRun;

    [STAThread]
    private static void Main(string[] args)
    {
        dryRun = HasArg(args, "/dry-run") || HasArg(args, "--dry-run") || HasArg(args, "/preview") || HasArg(args, "--preview");
        bool quiet = HasArg(args, "/quiet") || HasArg(args, "--quiet");
        bool deleteData = !HasArg(args, "/keep-data") && !HasArg(args, "--keep-data");
        if (HasArg(args, "/delete-data") || HasArg(args, "--delete-data")) deleteData = true;
        logPath = GetArgValue(args, "/log:") ?? GetArgValue(args, "--log:");
        if (string.IsNullOrWhiteSpace(logPath))
        {
            logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "FrameScope-LegacyCleanup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
        }

        if (quiet)
        {
            ExecuteCleanup(deleteData, null);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        form = new CleanupForm(deleteData);
        Application.Run(form);
    }

    internal static CleanupPlan BuildPlan(bool deleteData)
    {
        var plan = new CleanupPlan();
        plan.LogPath = logPath;

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string appDir = Path.Combine(localAppData, "FrameScopeMonitor");

        plan.Processes.AddRange(FindFrameScopeProcesses());
        plan.RegistryKeys.AddRange(FindUninstallRegistryKeys());
        plan.RegistryValues.AddRange(FindStartupRegistryValues());
        plan.Shortcuts.AddRange(FindShortcuts());
        plan.ScheduledTasks.AddRange(FindScheduledTasks());

        AddDirectoryIfFrameScope(plan.ProgramDirectories, appDir);
        AddDirectoryIfFrameScope(plan.ProgramDirectories, Path.Combine(appData, "FrameScopeMonitor"));
        AddDirectoryIfFrameScope(plan.ProgramDirectories, Path.Combine(localAppData, "Programs", "FrameScopeMonitor"));
        AddDirectoryIfFrameScope(plan.ProgramDirectories, Path.Combine(programFiles, "FrameScopeMonitor"));
        AddDirectoryIfFrameScope(plan.ProgramDirectories, Path.Combine(programFilesX86, "FrameScopeMonitor"));

        if (deleteData)
        {
            foreach (string dataRoot in FindDataRoots(appDir))
            {
                AddDirectoryIfSafe(plan.DataDirectories, dataRoot);
            }
        }

        return plan;
    }

    internal static void ExecuteCleanup(bool deleteData, Action<string> progress)
    {
        CleanupPlan plan = BuildPlan(deleteData);
        Log("FrameScope legacy cleanup started. dryRun=" + dryRun + " deleteData=" + deleteData);
        LogPlan(plan);

        RunStep("停止 FrameScope / PresentMon / 旧版 PowerShell 监测进程", progress, () =>
        {
            foreach (ProcessInfo proc in plan.Processes)
            {
                if (dryRun)
                {
                    Log("[DRY-RUN] would stop process: " + proc);
                    continue;
                }

                try
                {
                    Process target = Process.GetProcessById(proc.ProcessId);
                    target.Kill();
                    target.WaitForExit(3000);
                    Log("stopped process: " + proc);
                }
                catch (Exception ex)
                {
                    Log("process stop skipped: " + proc + " error=" + ex.Message);
                }
            }
        });

        RunStep("删除旧版计划任务", progress, () =>
        {
            foreach (string task in plan.ScheduledTasks)
            {
                if (dryRun)
                {
                    Log("[DRY-RUN] would delete scheduled task: " + task);
                    continue;
                }
                RunHidden("schtasks.exe", "/Delete /TN \"" + task + "\" /F");
                Log("deleted scheduled task: " + task);
            }
        });

        RunStep("删除旧版启动项", progress, () =>
        {
            foreach (RegistryValueRef value in plan.RegistryValues)
            {
                if (dryRun)
                {
                    Log("[DRY-RUN] would delete registry value: " + value);
                    continue;
                }
                try
                {
                    using (RegistryKey key = value.Root.OpenSubKey(value.SubKey, true))
                    {
                        if (key != null) key.DeleteValue(value.ValueName, false);
                    }
                    Log("deleted registry value: " + value);
                }
                catch (Exception ex)
                {
                    Log("registry value delete skipped: " + value + " error=" + ex.Message);
                }
            }
        });

        RunStep("删除卸载注册表入口", progress, () =>
        {
            foreach (RegistryKeyRef keyRef in plan.RegistryKeys)
            {
                if (dryRun)
                {
                    Log("[DRY-RUN] would delete registry key: " + keyRef);
                    continue;
                }
                try
                {
                    keyRef.Root.DeleteSubKeyTree(keyRef.SubKey, false);
                    Log("deleted registry key: " + keyRef);
                }
                catch (Exception ex)
                {
                    Log("registry key delete skipped: " + keyRef + " error=" + ex.Message);
                }
            }
        });

        RunStep("删除快捷方式", progress, () =>
        {
            foreach (string shortcut in plan.Shortcuts)
            {
                DeleteFile(shortcut);
            }
        });

        if (deleteData)
        {
            RunStep("删除旧数据和报告目录", progress, () =>
            {
                foreach (string dir in plan.DataDirectories)
                {
                    DeleteDirectory(dir);
                }
            });
        }

        RunStep("删除旧程序目录和旧版 Python / PowerShell 组件", progress, () =>
        {
            foreach (string dir in plan.ProgramDirectories)
            {
                DeleteDirectory(dir);
            }
        });

        Log("FrameScope legacy cleanup finished.");
        if (progress != null) progress("清理完成。日志已写入：" + logPath);
    }

    private static void RunStep(string title, Action<string> progress, Action action)
    {
        if (progress != null) progress(title + "...");
        Log("step: " + title);
        action();
    }

    private static void LogPlan(CleanupPlan plan)
    {
        Log("plan.processes=" + plan.Processes.Count);
        foreach (ProcessInfo item in plan.Processes) Log("  process " + item);
        Log("plan.scheduledTasks=" + plan.ScheduledTasks.Count);
        foreach (string item in plan.ScheduledTasks) Log("  task " + item);
        Log("plan.registryKeys=" + plan.RegistryKeys.Count);
        foreach (RegistryKeyRef item in plan.RegistryKeys) Log("  regkey " + item);
        Log("plan.registryValues=" + plan.RegistryValues.Count);
        foreach (RegistryValueRef item in plan.RegistryValues) Log("  regvalue " + item);
        Log("plan.shortcuts=" + plan.Shortcuts.Count);
        foreach (string item in plan.Shortcuts) Log("  shortcut " + item);
        Log("plan.programDirs=" + plan.ProgramDirectories.Count);
        foreach (string item in plan.ProgramDirectories) Log("  programDir " + item);
        Log("plan.dataDirs=" + plan.DataDirectories.Count);
        foreach (string item in plan.DataDirectories) Log("  dataDir " + item);
    }

    private static List<ProcessInfo> FindFrameScopeProcesses()
    {
        var result = new List<ProcessInfo>();
        int currentPid = Process.GetCurrentProcess().Id;
        string[] directNames =
        {
            "FrameScopeMonitor",
            "FrameScopeReportGenerator",
            "FrameScopeProcessSampler",
            "FrameScopeSystemSampler",
            "FrameScopeUninstaller",
            "FrameScopeLegacyCleanup",
            "FrameScopeMonitor-LegacyCleanup",
            "PresentMon-2.4.1-x64"
        };

        foreach (string name in directNames)
        {
            foreach (Process proc in Process.GetProcessesByName(name))
            {
                try
                {
                    if (proc.Id == currentPid) continue;
                    AddProcess(result, proc.Id, proc.ProcessName, SafeMainModule(proc), "");
                }
                catch { }
            }
        }

        try
        {
            using (var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process WHERE Name='powershell.exe' OR Name='pwsh.exe' OR Name='cmd.exe' OR Name='python.exe' OR Name='pythonw.exe'"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    int pid = Convert.ToInt32(obj["ProcessId"]);
                    if (pid == currentPid) continue;
                    string name = Convert.ToString(obj["Name"]) ?? "";
                    string exe = Convert.ToString(obj["ExecutablePath"]) ?? "";
                    string cmd = Convert.ToString(obj["CommandLine"]) ?? "";
                    string haystack = (exe + "\n" + cmd).ToLowerInvariant();
                    if (haystack.Contains("framescope-watcher") ||
                        haystack.Contains("monitor-cs2-highfreq") ||
                        haystack.Contains("generate-cs2-framescope") ||
                        haystack.Contains("framescope-runs") ||
                        haystack.Contains("\\appdata\\local\\framescopemonitor\\runtime\\python") ||
                        haystack.Contains("\\appdata\\local\\framescopemonitor\\frameScopewatcher.ps1".ToLowerInvariant()) ||
                        haystack.Contains("\\appdata\\local\\framescopemonitor\\monitor-cs2-highfreq.ps1"))
                    {
                        AddProcess(result, pid, name, exe, cmd);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log("process WMI scan failed: " + ex.Message);
        }

        return result;
    }

    private static void AddProcess(List<ProcessInfo> result, int pid, string name, string path, string commandLine)
    {
        foreach (ProcessInfo existing in result)
        {
            if (existing.ProcessId == pid) return;
        }
        result.Add(new ProcessInfo { ProcessId = pid, Name = name ?? "", Path = path ?? "", CommandLine = commandLine ?? "" });
    }

    private static string SafeMainModule(Process proc)
    {
        try { return proc.MainModule == null ? "" : proc.MainModule.FileName; }
        catch { return ""; }
    }

    private static List<RegistryKeyRef> FindUninstallRegistryKeys()
    {
        var result = new List<RegistryKeyRef>();
        AddRegistryKeyIfExists(result, Registry.CurrentUser, UninstallKeyPath);
        AddRegistryKeyIfExists(result, Registry.LocalMachine, UninstallKeyPath);
        AddRegistryKeyIfExists(result, Registry.CurrentUser, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\FrameScopeMonitor");
        AddRegistryKeyIfExists(result, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\FrameScopeMonitor");
        AddRegistryKeyIfExists(result, Registry.CurrentUser, @"Software\FrameScopeMonitor");
        return result;
    }

    private static void AddRegistryKeyIfExists(List<RegistryKeyRef> result, RegistryKey root, string subKey)
    {
        try
        {
            using (RegistryKey key = root.OpenSubKey(subKey))
            {
                if (key != null) result.Add(new RegistryKeyRef(root, subKey));
            }
        }
        catch { }
    }

    private static List<RegistryValueRef> FindStartupRegistryValues()
    {
        var result = new List<RegistryValueRef>();
        AddFrameScopeRunValues(result, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run");
        AddFrameScopeRunValues(result, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run");
        AddFrameScopeRunValues(result, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce");
        AddFrameScopeRunValues(result, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce");
        return result;
    }

    private static void AddFrameScopeRunValues(List<RegistryValueRef> result, RegistryKey root, string subKey)
    {
        try
        {
            using (RegistryKey key = root.OpenSubKey(subKey))
            {
                if (key == null) return;
                foreach (string name in key.GetValueNames())
                {
                    string data = Convert.ToString(key.GetValue(name)) ?? "";
                    if (ContainsFrameScope(name) || ContainsFrameScope(data))
                    {
                        result.Add(new RegistryValueRef(root, subKey, name));
                    }
                }
            }
        }
        catch { }
    }

    private static List<string> FindScheduledTasks()
    {
        var tasks = new List<string>();
        try
        {
            string output = RunHiddenCapture("schtasks.exe", "/Query /FO CSV /NH");
            using (var reader = new StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] cols = ParseCsvLine(line);
                    if (cols.Length == 0) continue;
                    string taskName = cols[0].Trim();
                    if (ContainsFrameScope(taskName) && !ContainsIgnoreCase(tasks, taskName))
                    {
                        tasks.Add(taskName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log("scheduled task scan failed: " + ex.Message);
        }
        return tasks;
    }

    private static string[] ParseCsvLine(string line)
    {
        var cols = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                cols.Add(current.ToString());
                current.Length = 0;
            }
            else
            {
                current.Append(c);
            }
        }
        cols.Add(current.ToString());
        return cols.ToArray();
    }

    private static List<string> FindShortcuts()
    {
        var result = new List<string>();
        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
        };

        foreach (string root in roots)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                foreach (string file in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (ContainsFrameScope(name) && !ContainsIgnoreCase(result, file)) result.Add(file);
                }
            }
            catch { }
        }
        return result;
    }

    private static IEnumerable<string> FindDataRoots(string appDir)
    {
        var roots = new List<string>();
        AddDataRoot(roots, ReadRegistryString(Registry.CurrentUser, UninstallKeyPath, "DataRoot"));
        AddDataRoot(roots, ReadRegistryString(Registry.LocalMachine, UninstallKeyPath, "DataRoot"));
        AddDataRoot(roots, ReadConfigDataRoot(Path.Combine(appDir, "framescope-config.json")));
        AddDataRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameScopeMonitorData"));
        AddDataRoot(roots, Path.Combine(appDir, "framescope-runs"));
        AddDataRoot(roots, Path.Combine(appDir, "cs2-monitor-runs"));
        return roots;
    }

    private static void AddDataRoot(List<string> roots, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            if (!Directory.Exists(full)) return;
            if (!ContainsIgnoreCase(roots, full)) roots.Add(full);
        }
        catch { }
    }

    private static string ReadRegistryString(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            using (RegistryKey key = root.OpenSubKey(subKey))
            {
                object value = key == null ? null : key.GetValue(valueName);
                return value == null ? "" : Convert.ToString(value);
            }
        }
        catch
        {
            return "";
        }
    }

    private static string ReadConfigDataRoot(string configPath)
    {
        try
        {
            if (!File.Exists(configPath)) return "";
            string text = File.ReadAllText(configPath);
            Match match = Regex.Match(text, "\"DataRoot\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase);
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
        return value.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\/", "/");
    }

    private static void AddDirectoryIfFrameScope(List<string> result, string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) return;
            if (!IsFrameScopeDirectory(full)) return;
            AddDirectoryIfSafe(result, full);
        }
        catch { }
    }

    private static bool IsFrameScopeDirectory(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return false;
            string name = Path.GetFileName(dir.TrimEnd('\\', '/'));
            if (ContainsFrameScope(name)) return true;
            return File.Exists(Path.Combine(dir, "FrameScopeMonitor.exe")) ||
                   File.Exists(Path.Combine(dir, "framescope-config.json")) ||
                   File.Exists(Path.Combine(dir, "FrameScopeWatcher.ps1")) ||
                   File.Exists(Path.Combine(dir, "Monitor-CS2-HighFreq.ps1"));
        }
        catch
        {
            return false;
        }
    }

    private static void AddDirectoryIfSafe(List<string> result, string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (!IsSafeDirectoryTarget(full)) return;
            for (int i = result.Count - 1; i >= 0; i--)
            {
                if (IsSameOrChildPath(result[i], full))
                {
                    result.RemoveAt(i);
                }
                else if (IsSameOrChildPath(full, result[i]))
                {
                    return;
                }
            }
            if (!ContainsIgnoreCase(result, full)) result.Add(full);
        }
        catch { }
    }

    private static bool IsSafeDirectoryTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full = Path.GetFullPath(path).TrimEnd('\\', '/');
        string root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root)) return false;
        if (full.Equals(root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return false;
        if (full.Length < 10) return false;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\', '/');
        if (full.Equals(windows, StringComparison.OrdinalIgnoreCase) || full.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/');
        if (full.Equals(userProfile, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static void DeleteFile(string path)
    {
        if (dryRun)
        {
            Log("[DRY-RUN] would delete file: " + path);
            return;
        }
        try
        {
            if (File.Exists(path)) File.Delete(path);
            Log("deleted file: " + path);
        }
        catch (Exception ex)
        {
            Log("file delete skipped: " + path + " error=" + ex.Message);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (!IsSafeDirectoryTarget(path))
        {
            Log("unsafe directory skipped: " + path);
            return;
        }
        if (dryRun)
        {
            Log("[DRY-RUN] would delete directory: " + path);
            return;
        }

        try
        {
            if (!Directory.Exists(path)) return;
            string currentExe = Assembly.GetExecutingAssembly().Location;
            if (IsSameOrChildPath(currentExe, path))
            {
                ScheduleDirectoryRemoval(path);
                Log("scheduled directory removal after exit: " + path);
                return;
            }
            Directory.Delete(path, true);
            Log("deleted directory: " + path);
        }
        catch (Exception ex)
        {
            Log("directory delete skipped: " + path + " error=" + ex.Message);
            ScheduleDirectoryRemoval(path);
        }
    }

    private static void ScheduleDirectoryRemoval(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (!Directory.Exists(full) || !IsSafeDirectoryTarget(full)) return;
            string tempCmd = Path.Combine(Path.GetTempPath(), "FrameScope-LegacyCleanup-" + Guid.NewGuid().ToString("N") + ".cmd");
            string script =
                "@echo off\r\n" +
                "timeout /t 3 /nobreak >nul\r\n" +
                "rmdir /s /q \"" + full + "\"\r\n" +
                "del /f /q \"" + tempCmd + "\" >nul 2>nul\r\n";
            File.WriteAllText(tempCmd, script, Encoding.ASCII);
            RunHidden(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", "/c \"" + tempCmd + "\"");
        }
        catch (Exception ex)
        {
            Log("schedule removal failed: " + path + " error=" + ex.Message);
        }
    }

    private static bool IsSameOrChildPath(string child, string parent)
    {
        string fullChild = Path.GetFullPath(child).TrimEnd('\\', '/');
        string fullParent = Path.GetFullPath(parent).TrimEnd('\\', '/');
        return fullChild.Equals(fullParent, StringComparison.OrdinalIgnoreCase) ||
               fullChild.StartsWith(fullParent + "\\", StringComparison.OrdinalIgnoreCase) ||
               fullChild.StartsWith(fullParent + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsFrameScope(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string lower = text.ToLowerInvariant();
        return lower.Contains("framescope") || lower.Contains("frame scope") || lower.Contains("frame-scope");
    }

    private static bool ContainsIgnoreCase(IEnumerable<string> values, string value)
    {
        foreach (string item in values)
        {
            if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void RunHidden(string fileName, string arguments)
    {
        try
        {
            using (Process proc = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }))
            {
                if (proc != null) proc.WaitForExit(10000);
            }
        }
        catch (Exception ex)
        {
            Log("run hidden failed: " + fileName + " " + arguments + " error=" + ex.Message);
        }
    }

    private static string RunHiddenCapture(string fileName, string arguments)
    {
        using (Process proc = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }))
        {
            if (proc == null) return "";
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(15000);
            if (!string.IsNullOrWhiteSpace(stderr)) Log("capture stderr: " + stderr.Trim());
            return stdout;
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

    private static string GetArgValue(string[] args, string prefix)
    {
        if (args == null) return null;
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg.Substring(prefix.Length).Trim('"');
            }
        }
        return null;
    }

    private static void Log(string message)
    {
        lock (LogLock)
        {
            try
            {
                string parent = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.AppendAllText(logPath, DateTime.Now.ToString("o") + " " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    internal sealed class CleanupPlan
    {
        public readonly List<ProcessInfo> Processes = new List<ProcessInfo>();
        public readonly List<string> ScheduledTasks = new List<string>();
        public readonly List<RegistryKeyRef> RegistryKeys = new List<RegistryKeyRef>();
        public readonly List<RegistryValueRef> RegistryValues = new List<RegistryValueRef>();
        public readonly List<string> Shortcuts = new List<string>();
        public readonly List<string> ProgramDirectories = new List<string>();
        public readonly List<string> DataDirectories = new List<string>();
        public string LogPath = "";

        public int Count
        {
            get
            {
                return Processes.Count + ScheduledTasks.Count + RegistryKeys.Count + RegistryValues.Count +
                       Shortcuts.Count + ProgramDirectories.Count + DataDirectories.Count;
            }
        }
    }

    internal sealed class ProcessInfo
    {
        public int ProcessId;
        public string Name;
        public string Path;
        public string CommandLine;

        public override string ToString()
        {
            string path = string.IsNullOrWhiteSpace(Path) ? CommandLine : Path;
            return Name + " pid=" + ProcessId + (string.IsNullOrWhiteSpace(path) ? "" : " path=" + path);
        }
    }

    internal sealed class RegistryKeyRef
    {
        public readonly RegistryKey Root;
        public readonly string SubKey;

        public RegistryKeyRef(RegistryKey root, string subKey)
        {
            Root = root;
            SubKey = subKey;
        }

        public override string ToString()
        {
            return Root.Name + "\\" + SubKey;
        }
    }

    internal sealed class RegistryValueRef
    {
        public readonly RegistryKey Root;
        public readonly string SubKey;
        public readonly string ValueName;

        public RegistryValueRef(RegistryKey root, string subKey, string valueName)
        {
            Root = root;
            SubKey = subKey;
            ValueName = valueName;
        }

        public override string ToString()
        {
            return Root.Name + "\\" + SubKey + " value=" + ValueName;
        }
    }

    internal sealed class CleanupForm : Form
    {
        private readonly CheckBox deleteDataCheck;
        private readonly TextBox details;
        private readonly Label status;
        private readonly Button cleanupButton;
        private readonly Button refreshButton;
        private readonly Button closeButton;
        private bool running;

        public CleanupForm(bool defaultDeleteData)
        {
            Text = "FrameScope 旧版本完全卸载工具";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(760, 540);
            MinimumSize = new Size(760, 540);
            BackColor = Color.FromArgb(17, 26, 36);
            ForeColor = Color.FromArgb(239, 247, 255);
            Font = new Font("Microsoft YaHei UI", 9f);

            var title = new Label
            {
                Text = "FrameScope 旧版本完全卸载工具",
                Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 211, 91),
                Location = new Point(22, 20),
                Size = new Size(700, 32)
            };
            Controls.Add(title);

            var desc = new Label
            {
                Text = "用于清理早期或异常安装的 FrameScope Monitor。会扫描旧版程序、PowerShell/Python 监测残留、快捷方式、启动项、计划任务和卸载注册表。",
                Location = new Point(24, 60),
                Size = new Size(700, 42),
                ForeColor = Color.FromArgb(189, 208, 222)
            };
            Controls.Add(desc);

            deleteDataCheck = new CheckBox
            {
                Text = "同时删除旧数据和 HTML 报告目录（旧版完全卸载建议勾选）",
                Checked = defaultDeleteData,
                Location = new Point(24, 104),
                Size = new Size(620, 28),
                ForeColor = Color.FromArgb(239, 247, 255)
            };
            deleteDataCheck.CheckedChanged += (sender, args) => RefreshPlan();
            Controls.Add(deleteDataCheck);

            details = new TextBox
            {
                Location = new Point(24, 140),
                Size = new Size(700, 300),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(20, 29, 40),
                ForeColor = Color.FromArgb(239, 247, 255),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(details);

            status = new Label
            {
                Text = "正在扫描...",
                Location = new Point(24, 452),
                Size = new Size(700, 34),
                ForeColor = Color.FromArgb(189, 208, 222)
            };
            Controls.Add(status);

            cleanupButton = new Button
            {
                Text = "开始完全卸载",
                Location = new Point(420, 490),
                Size = new Size(130, 32)
            };
            cleanupButton.Click += (sender, args) => BeginCleanup();
            Controls.Add(cleanupButton);

            refreshButton = new Button
            {
                Text = "重新扫描",
                Location = new Point(560, 490),
                Size = new Size(86, 32)
            };
            refreshButton.Click += (sender, args) => RefreshPlan();
            Controls.Add(refreshButton);

            closeButton = new Button
            {
                Text = "关闭",
                Location = new Point(656, 490),
                Size = new Size(68, 32)
            };
            closeButton.Click += (sender, args) => Close();
            Controls.Add(closeButton);

            FormClosing += (sender, args) =>
            {
                if (running)
                {
                    args.Cancel = true;
                    MessageBox.Show(this, "清理正在执行，请等待完成。", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            RefreshPlan();
        }

        private void RefreshPlan()
        {
            CleanupPlan plan = BuildPlan(deleteDataCheck.Checked);
            var text = new StringBuilder();
            text.AppendLine("将写入日志：" + plan.LogPath);
            text.AppendLine();
            AppendSection(text, "将停止的进程", plan.Processes);
            AppendSection(text, "将删除的计划任务", plan.ScheduledTasks);
            AppendSection(text, "将删除的启动项", plan.RegistryValues);
            AppendSection(text, "将删除的卸载注册表", plan.RegistryKeys);
            AppendSection(text, "将删除的快捷方式", plan.Shortcuts);
            AppendSection(text, "将删除的旧程序目录", plan.ProgramDirectories);
            AppendSection(text, "将删除的旧数据和报告目录", plan.DataDirectories);
            if (plan.Count == 0)
            {
                text.AppendLine("没有扫描到明显的旧版残留。");
            }
            details.Text = text.ToString();
            status.Text = "扫描完成，共发现 " + plan.Count + " 项可清理内容。";
        }

        private static void AppendSection<T>(StringBuilder text, string title, IEnumerable<T> items)
        {
            text.AppendLine(title + "：");
            int count = 0;
            foreach (T item in items)
            {
                count++;
                text.AppendLine("  - " + item);
            }
            if (count == 0) text.AppendLine("  - 无");
            text.AppendLine();
        }

        private void BeginCleanup()
        {
            string warning = deleteDataCheck.Checked
                ? "将完全删除旧程序、旧启动残留、旧数据和旧 HTML 报告。确定继续吗？"
                : "将删除旧程序和旧启动残留，但保留扫描到的数据和报告目录。确定继续吗？";
            if (MessageBox.Show(this, warning, ProductName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                return;
            }

            running = true;
            cleanupButton.Enabled = false;
            refreshButton.Enabled = false;
            closeButton.Enabled = false;
            deleteDataCheck.Enabled = false;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ExecuteCleanup(deleteDataCheck.Checked, UpdateStatus);
                    Invoke(new Action(() =>
                    {
                        running = false;
                        closeButton.Enabled = true;
                        status.Text = "清理完成。日志：" + logPath;
                        MessageBox.Show(this, "旧版 FrameScope Monitor 已清理完成。\r\n\r\n日志：" + logPath, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        RefreshPlan();
                    }));
                }
                catch (Exception ex)
                {
                    Invoke(new Action(() =>
                    {
                        running = false;
                        cleanupButton.Enabled = true;
                        refreshButton.Enabled = true;
                        closeButton.Enabled = true;
                        deleteDataCheck.Enabled = true;
                        status.Text = "清理失败：" + ex.Message;
                        MessageBox.Show(this, ex.Message, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void UpdateStatus(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatus), text);
                return;
            }
            status.Text = text;
        }
    }
}
