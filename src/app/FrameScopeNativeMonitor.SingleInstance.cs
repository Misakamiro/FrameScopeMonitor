using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private const string InteractiveUiSingleInstanceMutexName = @"Local\FrameScopeMonitor.InteractiveUi.SingleInstance";
    private const string UiAlreadyRunningMessage = "FrameScope Monitor \u5df2\u5728\u8fd0\u884c\uff0c\u8bf7\u52ff\u91cd\u590d\u6253\u5f00\u3002";

    internal static bool IsInteractiveUiLaunch(string[] args)
    {
        if (HasLaunchArg(args, "--watcher")) return false;
        if (HasLaunchArg(args, "--monitor-session")) return false;
        if (HasLaunchArg(args, "--generate-diagnostic-report")) return false;
        if (HasLaunchArg(args, "--webview2-runtime-self-test")) return false;
        if (HasLaunchArg(args, "--web-ui-smoke")) return false;
        if (HasLaunchArg(args, "--web-ui-target-settings-evidence-smoke")) return false;
        if (HasLaunchArg(args, "--web-ui-settings-persistence-read-smoke")) return false;
        if (HasLaunchArg(args, "--web-ui-tray-smoke")) return false;
        if (HasLaunchArg(args, "--MonitorProcessRole")) return false;
        return true;
    }

    private static bool HasLaunchArg(string[] args, string name)
    {
        if (args == null) return false;
        foreach (string arg in args)
        {
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool TryAcquireInteractiveUiSingleInstanceLock(out Mutex mutex)
    {
        bool createdNew;
        mutex = new Mutex(true, InteractiveUiSingleInstanceMutexName, out createdNew);
        if (createdNew) return true;

        mutex.Dispose();
        mutex = null;
        return false;
    }

    private static void ReleaseInteractiveUiSingleInstanceLock(Mutex mutex)
    {
        if (mutex == null) return;
        try { mutex.ReleaseMutex(); }
        catch (ApplicationException) { }
        finally { mutex.Dispose(); }
    }

    private static void NotifyInteractiveUiAlreadyRunning()
    {
        TryActivateExistingInteractiveUiWindow();
        MessageBox.Show(
            UiAlreadyRunningMessage,
            "FrameScope Monitor",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static bool TryActivateExistingInteractiveUiWindow()
    {
        try
        {
            using (Process current = Process.GetCurrentProcess())
            {
                Process[] candidates = Process.GetProcessesByName(current.ProcessName);
                foreach (Process process in candidates)
                {
                    try
                    {
                        if (process.Id == current.Id) continue;
                        IntPtr handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero) continue;
                        if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
                        SetForegroundWindow(handle);
                        return true;
                    }
                    catch
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }
        catch
        {
        }
        return false;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    internal static bool IsInteractiveUiLaunchForTest(string[] args)
    {
        return IsInteractiveUiLaunch(args);
    }

    internal static bool TryAcquireInteractiveUiSingleInstanceLockForTest(out Mutex mutex)
    {
        return TryAcquireInteractiveUiSingleInstanceLock(out mutex);
    }

    internal static void ReleaseInteractiveUiSingleInstanceLockForTest(Mutex mutex)
    {
        ReleaseInteractiveUiSingleInstanceLock(mutex);
    }

    internal static string UiAlreadyRunningMessageForTest
    {
        get { return UiAlreadyRunningMessage; }
    }
}
