using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

internal static class FrameScopeStartupElevation
{
    internal const string Marker = "--framescope-uac-relay";
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, out int value, int length, out int returned);

    internal static bool ContinueStartup(bool elevated, bool background, bool attempted, Func<bool> relaunch)
    {
        if (elevated) return true;
        if (background) throw new InvalidOperationException("后台任务缺少管理员权限，请从 FrameScope 主程序启动。");
        if (attempted) throw new InvalidOperationException("管理员权限未取得，已停止启动；不会重复请求 UAC。");
        if (!relaunch()) throw new InvalidOperationException("无法启动管理员进程，FrameScope 已停止启动。");
        return false;
    }

    internal static bool Ensure(string[] args, bool background)
    {
        args = args ?? new string[0];
        int elevated, returned;
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            if (!GetTokenInformation(identity.Token, 20, out elevated, sizeof(int), out returned))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法确认管理员权限。");
        return ContinueStartup(elevated != 0, background, args.Contains(Marker), delegate
        {
            var start = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = String.Join(" ", args.Concat(new[] { Marker }).Select(QuoteArgument)),
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };
            try { using (Process child = Process.Start(start)) return child != null; }
            catch (Win32Exception error)
            {
                if (error.NativeErrorCode == 1223) throw new InvalidOperationException("已取消管理员授权，FrameScope 已停止启动。", error);
                throw;
            }
        });
    }

    internal static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char ch in value)
        {
            if (ch == '\\') { slashes++; continue; }
            result.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            result.Append(ch);
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
