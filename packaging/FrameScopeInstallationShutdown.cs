using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;

internal static class FrameScopeInstallationShutdown
{
    internal const string Capability = "framescope-installation-shutdown-v1";
    internal interface Environment
    {
        List<FrameScopeWatcherShutdown.Identity> Capture(string root);
        FrameScopeWatcherShutdown.Identity Inspect(int pid);
        bool Wait(FrameScopeWatcherShutdown.Identity identity, int budget);
        void RequestHostExit(string root, FrameScopeWatcherShutdown.Identity identity, int budget);
        void StopHardware(string root, FrameScopeWatcherShutdown.Identity identity);
    }

    internal static string Stop(string root) { return Stop(root, new WindowsEnvironment()); }

    internal static int MaximumBudget(string root)
    {
        root = NormalizeRoot(root);
        var environment = new WindowsEnvironment();
        var processes = environment.Capture(root);
        int hosts = 0, services = 0, reports = 0;
        foreach (var process in processes)
        {
            if (FrameScopeWatcherShutdown.SamePath(process.ExecutablePath, Path.Combine(root, "FrameScope.exe"))) hosts++;
            if (FrameScopeWatcherShutdown.SamePath(process.ExecutablePath, Path.Combine(root, "FrameScopeHardwareService.exe"))) services++;
        }
        string statePath = Path.Combine(root, "framescope-watcher-state.json");
        if (File.Exists(statePath))
        {
            var state = FrameScopeWatcherShutdown.ReadState(statePath);
            var ownership = FrameScopeWatcherShutdown.Read(root, statePath, Convert.ToString(state["ConfigPath"]), 0, environment.Inspect);
            reports = ownership.ReportCount;
        }
        return checked(30000 + (2 * hosts + 1) * FrameScopeWatcherShutdown.Budget(reports) + 90000 * services);
    }

    internal static string Stop(string root, Environment environment)
    {
        root = NormalizeRoot(root);
        var processes = environment.Capture(root);
        string statePath = Path.Combine(root, "framescope-watcher-state.json");
        FrameScopeWatcherShutdown.Ownership watcher = null;
        if (File.Exists(statePath))
        {
            var state = FrameScopeWatcherShutdown.ReadState(statePath);
            string config = Convert.ToString(state["ConfigPath"]);
            if (string.IsNullOrWhiteSpace(config) || !Path.IsPathRooted(config) || !File.Exists(config))
                throw new InvalidOperationException("Watcher config must be an existing absolute path.");
            watcher = FrameScopeWatcherShutdown.Read(root, statePath, config, 0, environment.Inspect);
            if (environment.Inspect(watcher.Pid) != null)
            {
                var captured = processes.Find(p => FrameScopeWatcherShutdown.SameIdentity(p, FrameScopeWatcherShutdown.ToIdentity(watcher)));
                if (captured == null || !Regex.IsMatch(captured.CommandLine ?? "", "(?:^|\\s)--watcher(?:\\s|$)", RegexOptions.IgnoreCase))
                    throw new InvalidOperationException("Watcher command-line ownership is unknown.");
                Match argument = Regex.Match(captured.CommandLine, "(?:^|\\s)--config\\s+(?:\"([^\"]+)\"|([^\\s]+))", RegexOptions.IgnoreCase);
                string actualConfig = argument.Success ? (argument.Groups[1].Success ? argument.Groups[1].Value : argument.Groups[2].Value) : Path.Combine(root, "framescope-config.json");
                if (!Path.IsPathRooted(actualConfig) || !FrameScopeWatcherShutdown.SamePath(actualConfig, watcher.ConfigPath))
                    throw new InvalidOperationException("Watcher state config does not match captured process arguments.");
            }
        }
        var hosts = new List<FrameScopeWatcherShutdown.Identity>();
        var services = new List<FrameScopeWatcherShutdown.Identity>();
        bool hasService = processes.Exists(p => FrameScopeWatcherShutdown.SamePath(p.ExecutablePath, Path.Combine(root, "FrameScopeHardwareService.exe")));
        foreach (var process in processes)
        {
            FrameScopeWatcherShutdown.RequirePlainPath(process.ExecutablePath);
            string name = Path.GetFileName(process.ExecutablePath);
            if (FrameScopeWatcherShutdown.SamePath(process.ExecutablePath, Path.Combine(root, "FrameScope.exe")))
            {
                VerifyHostCapability(root);
                hosts.Add(process);
            }
            else if (FrameScopeWatcherShutdown.SamePath(process.ExecutablePath, Path.Combine(root, "FrameScopeHardwareService.exe"))) services.Add(process);
            else if (watcher != null && FrameScopeWatcherShutdown.SameIdentity(process, FrameScopeWatcherShutdown.ToIdentity(watcher))) { }
            else if (hasService && FrameScopeWatcherShutdown.SamePath(process.ExecutablePath, Path.Combine(root, "FrameScopeHardwareProvider.exe"))) { }
            else if (name.Equals("FrameScopeMonitor.exe", StringComparison.OrdinalIgnoreCase) &&
                (process.CommandLine ?? "").IndexOf("--monitor-session", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Unknown Monitor/UI ownership; close this instance normally before retrying.");
            else if (watcher == null || !(name.Equals("FrameScopeProcessSampler.exe", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("FrameScopeSystemSampler.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("FrameScopeReportGenerator.exe", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("FrameScopeHardwareProvider.exe", StringComparison.OrdinalIgnoreCase) || name.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase) ||
                (name.Equals("FrameScopeMonitor.exe", StringComparison.OrdinalIgnoreCase) && (process.CommandLine ?? "").IndexOf("--monitor-session", StringComparison.OrdinalIgnoreCase) >= 0)))
                throw new InvalidOperationException("Uncontrolled installation process: " + process.Pid + " " + process.ExecutablePath);
        }
        int budget = FrameScopeWatcherShutdown.Budget(watcher == null ? 0 : watcher.ReportCount);
        foreach (var host in hosts)
        {
            RequireSame(environment, host);
            environment.RequestHostExit(root, host, budget);
            if (!environment.Wait(host, budget) || environment.Inspect(host.Pid) != null) throw new TimeoutException("Original host did not exit; no files were changed.");
        }
        string warning = watcher == null ? "" : FrameScopeWatcherShutdown.Stop(root, statePath, watcher, 0, environment.Inspect, environment.Wait);
        foreach (var service in services)
        {
            if (environment.Inspect(service.Pid) == null) continue;
            RequireSame(environment, service);
            environment.StopHardware(root, service);
            if (!environment.Wait(service, 15000) || environment.Inspect(service.Pid) != null) throw new TimeoutException("Hardware service acknowledged stop but did not exit.");
        }
        if (environment.Capture(root).Count != 0) throw new InvalidOperationException("Installation still has owned or uncontrolled processes; no files may be changed.");
        return warning;
    }

    internal static void RequireSame(Environment environment, FrameScopeWatcherShutdown.Identity identity)
    {
        if (!FrameScopeWatcherShutdown.SameIdentity(environment.Inspect(identity.Pid), identity)) throw new InvalidOperationException("Process executable/start identity changed; stop refused.");
    }

    internal static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) throw new InvalidOperationException("An explicit absolute installation root is required.");
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (FrameScopeWatcherShutdown.SamePath(root, Path.GetPathRoot(root))) throw new InvalidOperationException("Drive root is not an installation root.");
        for (var current = new DirectoryInfo(root); current != null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Reparse installation root is not supported.");
        return root;
    }

    internal static bool Within(string path, string root)
    {
        return !string.IsNullOrWhiteSpace(path) && Path.GetFullPath(path).StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    internal static void VerifyHostCapability(string root)
    {
        FrameScopeWatcherShutdown.RequirePlainPath(Path.Combine(root, "FrameScope.exe"));
        FrameScopeWatcherShutdown.RequirePlainPath(Path.Combine(root, "FrameScopeHostCapabilities.json"));
        var json = new JavaScriptSerializer();
        string marker = Path.Combine(root, "FrameScopeHostCapabilities.json");
        if (!File.Exists(marker)) throw new InvalidOperationException("Host exit protocol is unknown; close the host normally before retrying.");
        var capability = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(marker));
        if (Convert.ToInt32(capability["schema"]) != 1 || Convert.ToInt32(capability["requestExitProtocol"]) != 1)
            throw new InvalidOperationException("Host exit capability is unsupported.");
        string hash;
        using (var stream = File.OpenRead(Path.Combine(root, "FrameScope.exe")))
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        if (!string.Equals(hash, Convert.ToString(capability["hostSha256"]), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Host capability does not match the installed executable.");
    }

    internal sealed class WindowsEnvironment : Environment
    {
        private readonly string pipeName;
        internal WindowsEnvironment() : this("FrameScopeHardwareService") { }
        internal WindowsEnvironment(string fixturePipeName) { pipeName = fixturePipeName; }
        internal static bool ProductName(string name)
        {
            return name.StartsWith("FrameScope", StringComparison.OrdinalIgnoreCase) || name.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool Selected(string root, string name, string path, string command)
        {
            bool script = name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("python.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("pythonw.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
            return (ProductName(name) && Within(path, root)) || (script &&
                (command.IndexOf("\"" + root + "\\", StringComparison.OrdinalIgnoreCase) >= 0 || command.IndexOf(" " + root + "\\", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString { internal ushort Length, MaximumLength; internal IntPtr Buffer; }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder image, ref int length);
        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int kind, IntPtr buffer, int length, out int needed);

        private static string ImagePath(int pid)
        {
            IntPtr process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var image = new StringBuilder(32768); int length = image.Capacity;
                if (!QueryFullProcessImageName(process, 0, image, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return image.ToString();
            }
            finally { CloseHandle(process); }
        }

        private static string CommandLine(int pid)
        {
            IntPtr process = OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int needed;
                NtQueryInformationProcess(process, 60, IntPtr.Zero, 0, out needed); // ProcessCommandLineInformation
                int header = Marshal.SizeOf(typeof(UnicodeString));
                if (needed < header || needed > 131072) throw new InvalidOperationException("Process command line size unavailable.");
                int capacity = needed;
                buffer = Marshal.AllocHGlobal(capacity);
                int status = NtQueryInformationProcess(process, 60, buffer, capacity, out needed);
                if (status < 0) throw new InvalidOperationException("Process command line query failed: 0x" + status.ToString("X8"));
                if (needed < header || needed > capacity) throw new InvalidOperationException("Process command line response size is invalid.");
                var value = (UnicodeString)Marshal.PtrToStructure(buffer, typeof(UnicodeString));
                if (value.Length == 0) return "";
                long begin = buffer.ToInt64() + header, end = buffer.ToInt64() + needed;
                if ((value.Length & 1) != 0 || value.Length > value.MaximumLength || value.Buffer.ToInt64() < begin || value.Buffer.ToInt64() > end - value.Length)
                    throw new InvalidOperationException("Process command line buffer is invalid.");
                return Marshal.PtrToStringUni(value.Buffer, value.Length / 2);
            }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); CloseHandle(process); }
        }

        public List<FrameScopeWatcherShutdown.Identity> Capture(string root)
        {
            var result = new List<FrameScopeWatcherShutdown.Identity>();
            int ownPid = Process.GetCurrentProcess().Id;
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    int pid = process.Id;
                    if (pid == ownPid) continue;
                    string name;
                    try { name = process.ProcessName + ".exe"; } catch (InvalidOperationException) { continue; }
                    bool script = name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("python.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("pythonw.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
                    if (!ProductName(name) && !script) continue;
                    string path;
                    try { path = ImagePath(pid); }
                    catch { if (!ProductName(name)) continue; try { if (process.HasExited) continue; } catch { } throw; }
                    if (!script && !Within(path, root)) continue;
                    string command = "";
                    if (script || name.Equals("FrameScopeMonitor.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        try { command = CommandLine(pid); }
                        catch { if (!Within(path, root)) continue; try { if (process.HasExited) continue; } catch { } throw; }
                    }
                    if (!Selected(root, name, path, command)) continue;
                    var identity = Inspect(pid);
                    if (identity == null) continue;
                    if (!FrameScopeWatcherShutdown.SamePath(identity.ExecutablePath, path)) throw new InvalidOperationException("Process image changed during capture.");
                    identity.CommandLine = command;
                    result.Add(identity);
                }
            }
            return result;
        }

        public FrameScopeWatcherShutdown.Identity Inspect(int pid) { return FrameScopeWatcherShutdown.Inspect(pid); }
        public bool Wait(FrameScopeWatcherShutdown.Identity identity, int budget) { return FrameScopeWatcherShutdown.Wait(identity, budget); }

        public void RequestHostExit(string root, FrameScopeWatcherShutdown.Identity identity, int budget)
        {
            VerifyHostCapability(root);
            RequireSame(this, identity);
            using (var request = Process.Start(new ProcessStartInfo(identity.ExecutablePath, "--request-exit --request-exit-root \"" + root + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root }))
            {
                if (!request.WaitForExit(budget)) throw new TimeoutException("Host request did not complete; processes were preserved.");
                if (request.ExitCode != 0) throw new InvalidOperationException("Host request failed: " + request.ExitCode);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);

        public void StopHardware(string root, FrameScopeWatcherShutdown.Identity identity)
        {
            if (!FrameScopeWatcherShutdown.SamePath(identity.ExecutablePath, Path.Combine(root, "FrameScopeHardwareService.exe")))
                throw new InvalidOperationException("Hardware service image is outside the requested root.");
            FrameScopeWatcherShutdown.RequirePlainPath(identity.ExecutablePath);
            using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(15000);
                uint pid;
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out pid) || pid != identity.Pid)
                    throw new InvalidOperationException("Pipe server belongs to another installation; stop was not sent.");
                RequireSame(this, identity);
                using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true))
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                {
                    var handshake = Exchange(reader, writer, "handshake");
                    if (Convert.ToString(handshake["protocol"]) != "framescope-hardware-service/1" ||
                        !(handshake["capabilities"] is IList) || !Contains((IList)handshake["capabilities"], "stop"))
                        throw new InvalidOperationException("Hardware stop protocol is unsupported.");
                    if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out pid) || pid != identity.Pid)
                        throw new InvalidOperationException("Pipe server changed before stop.");
                    RequireSame(this, identity);
                    var ack = Exchange(reader, writer, "stop");
                    if (Convert.ToString(ack["status"]) != "stopped" || !ack.ContainsKey("shutdownComplete") || !Convert.ToBoolean(ack["shutdownComplete"]))
                        throw new InvalidOperationException("Hardware stop did not confirm cleanup.");
                }
            }
        }

        private static bool Contains(IList items, string wanted)
        {
            foreach (var item in items) if (Convert.ToString(item) == wanted) return true;
            return false;
        }

        private static Dictionary<string, object> Exchange(StreamReader reader, StreamWriter writer, string method)
        {
            string id = Guid.NewGuid().ToString("N");
            string request = new JavaScriptSerializer().Serialize(new { id = id, method = method, @params = new { } });
            var write = Task.Factory.StartNew(delegate { writer.WriteLine(request); });
            if (!write.Wait(15000)) throw new TimeoutException("Hardware request write timed out.");
            var read = reader.ReadLineAsync();
            if (!read.Wait(15000)) throw new TimeoutException("Hardware reply timed out.");
            if (read.Result == null) throw new InvalidOperationException("Hardware pipe closed without acknowledgement.");
            var reply = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(read.Result);
            if (Convert.ToString(reply["id"]) != id || !Convert.ToBoolean(reply["ok"])) throw new InvalidOperationException("Hardware reply identity/status mismatch.");
            return (Dictionary<string, object>)reply["result"];
        }
    }
}
