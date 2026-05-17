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
    private static void UpdateWatcherStatus()
    {
        int pid;
        if (IsWatcherRunning(out pid))
        {
            if (startButton != null)
            {
                startButton.Text = "监测中";
                startButton.BackColor = Color.FromArgb(19, 65, 92);
            }
            SetStatusPill("RUNNING", UiCyan, Color.FromArgb(8, 12, 18));
            string text = "运行中，Watcher PID=" + pid;
            string lastReport = "";
            try
            {
                var state = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath));
                if (state.ContainsKey("CompletedRuns")) text += "，已完成 " + state["CompletedRuns"] + " 次";
                if (state.ContainsKey("LastReport") && state["LastReport"] != null && state["LastReport"].ToString() != "")
                {
                    lastReport = state["LastReport"].ToString();
                    text += "，最近报告：" + lastReport;
                }
            }
            catch { }
            if (statusLabel != null) statusLabel.Text = "状态：" + text;
            if (watcherSummaryLabel != null) watcherSummaryLabel.Text = "监测器" + Environment.NewLine + "PID " + pid.ToString(CultureInfo.InvariantCulture);
            if (latestReportLabel != null) latestReportLabel.Text = string.IsNullOrWhiteSpace(lastReport) ? "最近报告：等待会话完成" : "最近报告：" + lastReport;
            UpdateReportProgressUi();
        }
        else
        {
            if (startButton != null)
            {
                startButton.Text = "启动监测";
                startButton.BackColor = ButtonPaletteColor(startButton, 0);
            }
            SetStatusPill("READY", UiGreen, Color.FromArgb(8, 12, 18));
            if (watcherSummaryLabel != null) watcherSummaryLabel.Text = "监测器" + Environment.NewLine + "就绪";
            UpdateReportProgressUi();
        }
    }

    private static void UpdateReportProgressUi()
    {
        if (reportProgressTrack == null || reportProgressLabel == null) return;
        Dictionary<string, object> progress = LatestReportProgress();
        if (progress == null || progress.Count == 0)
        {
            SetReportProgress(0, UiBlue);
            reportProgressLabel.Text = "报告生成：空闲";
            reportProgressLabel.ForeColor = UiMuted;
            if (reportStageLabel != null) reportStageLabel.Text = "报告状态：空闲";
            return;
        }

        int percent = StatusInt(progress, "ReportProgressPercent", 0);
        string phase = StatusString(progress, "ReportProgressPhase", "空闲");
        string message = LocalizeProgressMessage(StatusString(progress, "ReportProgressMessage", ""));
        string error = LocalizeProgressMessage(StatusString(progress, "ReportProgressError", ""));
        int eta = StatusInt(progress, "ReportProgressEtaSeconds", 0);
        if (!string.IsNullOrWhiteSpace(error))
        {
            SetReportProgress(percent, UiRed);
            reportProgressLabel.ForeColor = UiRed;
            reportProgressLabel.Text = "报告生成：" + phase + "，可重试，" + error;
            if (reportStageLabel != null) reportStageLabel.Text = "报告状态：失败";
        }
        else
        {
            SetReportProgress(percent, percent >= 100 ? UiGreen : UiBlue);
            reportProgressLabel.ForeColor = percent >= 100 ? UiGreen : UiCyan;
            reportProgressLabel.Text = "报告生成：" + phase + " " + percent + "%，预计 " + eta + " 秒" + (string.IsNullOrWhiteSpace(message) ? "" : "，" + message);
            if (reportStageLabel != null) reportStageLabel.Text = "报告状态：" + phase + " " + percent + "%";
        }
    }

    private static string LocalizeProgressMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        if (message.IndexOf("No frame data was captured by PresentMon", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "PresentMon 未采集到帧数据，已生成诊断报告。";
        }
        if (message.IndexOf("generated diagnostic report", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "已生成诊断报告。";
        }
        if (message.IndexOf("Report generation complete", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "报告生成完成。";
        }
        return message;
    }

    private static Dictionary<string, object> LatestReportProgress()
    {
        if ((DateTime.Now - lastProgressScan).TotalMilliseconds < 1200 && cachedReportProgress != null) return cachedReportProgress;
        lastProgressScan = DateTime.Now;
        cachedReportProgress = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string root = dataRootText != null ? dataRootText.Text : DefaultDataRoot;
            if (string.IsNullOrWhiteSpace(root)) root = DefaultDataRoot;
            if (!Path.IsPathRooted(root)) root = Path.Combine(Root, root);
            if (!Directory.Exists(root)) return cachedReportProgress;
            cachedReportProgress = FrameScopeReportProgress.FindLatestEffectiveStatus(root);
        }
        catch { }
        return cachedReportProgress;
    }
}
