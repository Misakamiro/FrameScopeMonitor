using System.Diagnostics;

internal static partial class FrameScopeSystemSampler
{
    private static int CountProcesses()
    {
        Process[] processes = null;
        try
        {
            processes = Process.GetProcesses();
            return processes.Length;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (processes != null)
            {
                foreach (Process process in processes) process.Dispose();
            }
        }
    }

    private static bool ProcessNameRunning(string processName)
    {
        Process[] processes = null;
        try
        {
            processes = Process.GetProcessesByName(processName);
            return processes.Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (processes != null)
            {
                foreach (Process process in processes) process.Dispose();
            }
        }
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using (Process process = Process.GetProcessById(pid))
            {
                return !process.HasExited;
            }
        }
        catch
        {
            return false;
        }
    }
}
