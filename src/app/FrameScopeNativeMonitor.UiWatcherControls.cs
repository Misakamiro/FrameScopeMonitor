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
    private static int watcherActionInFlight;

    private static bool IsWatcherRunning(out int pid)
    {
        pid = 0;
        if (!File.Exists(StatePath)) return false;
        try
        {
            var state = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath));
            if (!state.ContainsKey("WatcherPid")) return false;
            pid = Convert.ToInt32(state["WatcherPid"]);
            if (pid <= 0) return false;
            using (Process.GetProcessById(pid))
            {
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void StartWatcher()
    {
        if (Interlocked.CompareExchange(ref watcherActionInFlight, 1, 0) != 0)
        {
            SetStatus("监测启动或停止正在执行，请等待完成。");
            return;
        }

        try
        {
            SetStatus("正在启动监测...");
            WriteFrameScopeLog("gui-start-watcher-request");
            SaveConfig(ReadGridConfig());
            int existingPid;
            if (IsWatcherRunning(out existingPid))
            {
                WriteFrameScopeLog("gui-start-watcher-existing pid=" + existingPid);
                SetStatus("监测已经在运行，Watcher PID=" + existingPid);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = "--watcher --config " + Quote(ConfigPath),
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var proc = Process.Start(psi);
            var watcherPid = proc != null ? proc.Id.ToString(CultureInfo.InvariantCulture) : "null";
            try
            {
                if (proc != null) proc.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch { }
            finally
            {
                DisposeProcess(proc);
            }
            WriteFrameScopeLog("gui-start-watcher-started pid=" + watcherPid);
            SetStatus("监测已启动，Watcher PID=" + (watcherPid == "null" ? "未知" : watcherPid) + "。现在启动配置里的游戏就会自动记录。");
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("gui-start-watcher-failed " + ex);
            SetStatus("启动监测失败：" + ex.Message);
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Interlocked.Exchange(ref watcherActionInFlight, 0);
        }
    }

    private static void StopWatcher()
    {
        if (Interlocked.CompareExchange(ref watcherActionInFlight, 1, 0) != 0)
        {
            SetStatus("监测启动或停止正在执行，请等待完成。");
            return;
        }

        if (!HasFrameScopeBackgroundProcesses())
        {
            SetStatus("没有正在运行的 FrameScope watcher");
            Interlocked.Exchange(ref watcherActionInFlight, 0);
            return;
        }
        try
        {
            SetStatus("正在停止监测...");
            StopFrameScopeBackgroundProcesses();
            SetStatus("监测已停止。");
        }
        catch (Exception ex)
        {
            SetStatus("停止监测失败：" + ex.Message);
            MessageBox.Show(ex.Message, "停止失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Interlocked.Exchange(ref watcherActionInFlight, 0);
        }
    }
}
