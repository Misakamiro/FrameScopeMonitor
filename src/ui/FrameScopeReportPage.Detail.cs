using System;
using System.Globalization;
using System.IO;
using System.Linq;

internal static partial class FrameScopeNativeMonitor
{
    private static string BuildReportDetailText(FrameScopeHistoryEntry entry)
    {
        if (entry == null)
        {
            return "暂无可用报告\r\n\r\n生成一次监测报告后，这里会显示目标游戏、进程、耗时、大小、导出路径和包含模块。";
        }

        string report = entry.ReportHtml ?? "";
        string runDir = entry.RunDir ?? "";
        string statusText = File.Exists(report) ? "已完成" : "报告文件缺失";
        string size = File.Exists(report) ? FormatBytes(new FileInfo(report).Length) : "--";
        string generated = File.Exists(report) ? File.GetLastWriteTime(report).ToString("yyyy-MM-dd HH:mm:ss") : entry.Time;
        string runStatus = "";
        var status = !string.IsNullOrWhiteSpace(runDir) ? ReadStatusDictionary(runDir) : null;
        if (status != null)
        {
            runStatus = "\r\n采集状态：" + StatusString(status, "FrameCaptureStatus", "未知") +
                "\r\n报告类型：" + StatusString(status, "ReportKind", "--") +
                "\r\n帧数：" + StatusInt(status, "ReportFrameCount", 0).ToString(CultureInfo.InvariantCulture);
        }

        return "目标游戏：" + (string.IsNullOrWhiteSpace(entry.Game) ? "--" : entry.Game) +
            "\r\n目标进程：" + (string.IsNullOrWhiteSpace(entry.ProcessName) ? "--" : entry.ProcessName) +
            "\r\n生成时间：" + generated +
            "\r\n状态：" + statusText +
            "\r\n报告大小：" + size +
            runStatus +
            "\r\n导出路径：" + (string.IsNullOrWhiteSpace(report) ? "--" : report) +
            "\r\n运行目录：" + (string.IsNullOrWhiteSpace(runDir) ? "--" : runDir) +
            "\r\n包含模块：FPS 时间线、进程干扰分析、CPU/GPU 使用率、内存分配";
    }

    private static void UpdateReportDetailUi()
    {
        if (reportDetailLabel != null) reportDetailLabel.Text = BuildReportDetailText(selectedReportEntry);
    }

    private static string LatestReportPath()
    {
        if ((DateTime.Now - lastLatestReportScan).TotalMilliseconds < 2000)
        {
            return cachedLatestReportPath ?? "";
        }

        lastLatestReportScan = DateTime.Now;
        var entry = LatestHistory();
        if (entry != null && File.Exists(entry.ReportHtml))
        {
            cachedLatestReportPath = entry.ReportHtml;
            return cachedLatestReportPath;
        }

        var root = dataRootText != null ? dataRootText.Text : DefaultDataRoot;
        if (string.IsNullOrWhiteSpace(root)) root = DefaultDataRoot;
        if (!Path.IsPathRooted(root)) root = Path.Combine(Root, root);
        if (!Directory.Exists(root))
        {
            cachedLatestReportPath = "";
            return cachedLatestReportPath;
        }

        try
        {
            cachedLatestReportPath = Directory.GetFiles(root, "framescope-interactive-report.html", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault() ?? "";
            return cachedLatestReportPath;
        }
        catch
        {
            cachedLatestReportPath = "";
            return "";
        }
    }
}
