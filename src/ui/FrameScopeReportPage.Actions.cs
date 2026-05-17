using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private static int selectedReportDetailInFlight;
    private static int selectedReportRegenerateInFlight;

    private static void BindReportActionsCardButtons(Button latest, Button data, Button diag, Button history)
    {
        if (latest != null) latest.Click += (_, __) => OpenLatestReport();
        if (data != null) data.Click += (_, __) => OpenDataRoot();
        if (diag != null) diag.Click += (_, __) => GenerateDiagnosticReportFromUi(diag);
        if (history != null) history.Click += (_, __) => OpenHistory();
    }

    private static void BindReportDetailActionButtons(Button openDir, Button openReport, Button support, Button regenerate, Button refresh)
    {
        if (openDir != null) openDir.Click += (_, __) => OpenSelectedReportFolder();
        if (openReport != null) openReport.Click += (_, __) => OpenSelectedReport();
        if (support != null) support.Click += (_, __) => OpenSelectedDetailedReport(support);
        if (regenerate != null) regenerate.Click += (_, __) => RegenerateSelectedReport(regenerate);
        if (refresh != null) refresh.Click += (_, __) => RefreshCachedPage("reports");
        var actionToolTip = new ToolTip();
        Action refreshState = () => RefreshReportDetailActionAvailability(openDir, openReport, support, regenerate, actionToolTip);
        if (reportListView != null) reportListView.SelectedIndexChanged += (_, __) => refreshState();
        refreshState();
    }

    private static void RefreshReportDetailActionAvailability(Button openDir, Button openReport, Button support, Button regenerate, ToolTip toolTip)
    {
        FrameScopeReportActionAvailability availability = SelectedReportActionAvailability();
        ApplyReportActionState(openDir, availability.CanOpenFolder, availability.Reason, toolTip);
        ApplyReportActionState(openReport, availability.CanOpenReport, availability.CanOpenReport ? "" : availability.Reason, toolTip);
        string detailReason = availability.CanOpenDetailedReport ? "" : (string.IsNullOrWhiteSpace(availability.Reason) ? "运行目录不存在，无法生成详细报告。" : availability.Reason);
        ApplyReportActionState(support, availability.CanOpenDetailedReport && selectedReportDetailInFlight == 0, detailReason, toolTip);
        string regenerateReason = string.IsNullOrWhiteSpace(availability.Reason) ? "运行目录不存在，无法重新生成报告。" : availability.Reason;
        ApplyReportActionState(regenerate, availability.CanRegenerateReport && selectedReportRegenerateInFlight == 0, availability.CanRegenerateReport ? "" : regenerateReason, toolTip);
    }

    private static void ApplyReportActionState(Button button, bool enabled, string reason, ToolTip toolTip)
    {
        if (button == null) return;
        button.Enabled = enabled;
        if (toolTip != null) toolTip.SetToolTip(button, enabled ? "" : reason);
    }

    private static FrameScopeReportActionAvailability SelectedReportActionAvailability()
    {
        var entry = selectedReportEntry;
        bool selected = entry != null;
        bool reportExists = selected && !string.IsNullOrWhiteSpace(entry.ReportHtml) && File.Exists(entry.ReportHtml);
        bool runDirExists = selected && !string.IsNullOrWhiteSpace(entry.RunDir) && Directory.Exists(entry.RunDir);
        return FrameScopeReportActionRules.ResolveAvailability(selected, reportExists, runDirExists);
    }

    private static void OpenLatestReport()
    {
        var reportPath = LatestReportPath();
        if (!string.IsNullOrWhiteSpace(reportPath) && File.Exists(reportPath))
        {
            if (TryOpenPath(reportPath))
            {
                SetStatus("已打开最近报告：" + reportPath);
            }
            else
            {
                SetStatus("找到报告但打开失败：" + reportPath);
            }
            return;
        }

        SetStatus("没有找到报告。");
    }

    private static void OpenHistory()
    {
        try
        {
            if (!File.Exists(HistoryPath)) File.WriteAllText(HistoryPath, "");
            Process.Start(new ProcessStartInfo { FileName = HistoryPath, UseShellExecute = true });
            SetStatus("已打开报告历史记录：" + HistoryPath);
        }
        catch (Exception ex)
        {
            SetStatus("打开报告历史记录失败：" + ex.Message);
        }
    }

    private static void OpenSelectedReport()
    {
        var entry = selectedReportEntry;
        if (entry == null || string.IsNullOrWhiteSpace(entry.ReportHtml))
        {
            SetStatus("请先选择一个报告。");
            return;
        }

        if (!File.Exists(entry.ReportHtml))
        {
            SetStatus("选中的报告文件不存在：" + entry.ReportHtml);
            return;
        }

        SetStatus(TryOpenPath(entry.ReportHtml) ? "已打开选中报告：" + entry.ReportHtml : "打开选中报告失败：" + entry.ReportHtml);
    }

    private static void OpenSelectedReportFolder()
    {
        var entry = selectedReportEntry;
        string path = "";
        if (entry != null && !string.IsNullOrWhiteSpace(entry.ReportHtml) && File.Exists(entry.ReportHtml))
        {
            path = Path.GetDirectoryName(entry.ReportHtml);
        }
        else if (entry != null && !string.IsNullOrWhiteSpace(entry.RunDir) && Directory.Exists(entry.RunDir))
        {
            path = entry.RunDir;
        }
        else
        {
            path = ResolveCurrentDataRoot();
        }

        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            SetStatus("已打开目录：" + path);
        }
        catch (Exception ex)
        {
            SetStatus("打开目录失败：" + ex.Message);
        }
    }

    private static void GenerateSelectedDiagnosticReport()
    {
        OpenSelectedDetailedReport(null);
    }

    private static void GenerateSelectedDiagnosticReport(Button trigger)
    {
        OpenSelectedDetailedReport(trigger);
    }

    private static void OpenSelectedDetailedReport(Button trigger)
    {
        var entry = selectedReportEntry;
        if (entry == null)
        {
            SetStatus("请先选择一个报告，再打开详细报告。");
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.RunDir) || !Directory.Exists(entry.RunDir))
        {
            SetStatus("运行目录不存在，无法生成详细报告：" + (entry.RunDir ?? ""));
            return;
        }

        if (Interlocked.CompareExchange(ref selectedReportDetailInFlight, 1, 0) != 0)
        {
            SetStatus("详细报告正在后台生成，请等待完成。");
            return;
        }

        string originalText = SetButtonBusy(trigger, "生成中...");
        FrameScopeConfig config;
        try
        {
            config = ReadGridConfig();
            SaveConfig(config);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref selectedReportDetailInFlight, 0);
            RestoreButtonFromAnyThread(trigger, originalText);
            SetStatus("详细报告生成失败：" + ex.Message);
            MessageBox.Show(ex.Message, "详细报告生成失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string runDir = entry.RunDir;
        SetStatus("正在后台生成选中报告的详细诊断报告...");
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                var result = FrameScopeDiagnostics.GenerateReport(config, Root, ResolveDataRoot(config.DataRoot), "selected-report-detail-ui", runDir);
                if (!string.IsNullOrWhiteSpace(result.MarkdownPath) && File.Exists(result.MarkdownPath) && TryOpenPath(result.MarkdownPath))
                {
                    SetStatusFromAnyThread("详细报告已生成并打开：" + result.MarkdownPath);
                }
                else
                {
                    SetStatusFromAnyThread("详细报告已生成，但打开失败：" + (result.MarkdownPath ?? ""));
                }
            }
            catch (Exception ex)
            {
                SetStatusFromAnyThread("详细报告生成失败：" + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref selectedReportDetailInFlight, 0);
                RestoreButtonFromAnyThread(trigger, originalText);
            }
        });
    }

    private static void RegenerateSelectedReport()
    {
        RegenerateSelectedReport(null);
    }

    private static void RegenerateSelectedReport(Button trigger)
    {
        var entry = selectedReportEntry;
        if (entry == null || string.IsNullOrWhiteSpace(entry.RunDir))
        {
            SetStatus("请先选择一个可重新生成的报告。");
            return;
        }

        if (!Directory.Exists(entry.RunDir))
        {
            SetStatus("运行目录不存在，无法重新生成：" + entry.RunDir);
            return;
        }

        if (Interlocked.CompareExchange(ref selectedReportRegenerateInFlight, 1, 0) != 0)
        {
            SetStatus("报告正在后台重新生成，请等待完成。");
            return;
        }

        string originalText = SetButtonBusy(trigger, "生成中...");
        if (!HasAnyMonitorCsv(entry.RunDir))
        {
            Interlocked.Exchange(ref selectedReportRegenerateInFlight, 0);
            RestoreButtonFromAnyThread(trigger, originalText);
            SetStatus("运行目录缺少 CSV 采样数据，无法重新生成报告。");
            return;
        }

        SetStatus("正在后台重新生成选中报告...");
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                var status = ReadStatusDictionary(entry.RunDir);
                var result = RunReportGeneration(entry.RunDir);
                UpdateStatusAfterReportGeneration(entry.RunDir, status, result, entry.MonitorExitCode);
                Interlocked.Exchange(ref selectedReportRegenerateInFlight, 0);
                RestoreButtonFromAnyThread(trigger, originalText);
                SetStatusFromAnyThread(result.ExitCode == 0 ? "选中报告已重新生成：" + result.ReportHtml : "选中报告重新生成失败：" + result.Error);
                if (form != null && !form.IsDisposed)
                {
                    form.BeginInvoke((MethodInvoker)(() => RefreshCachedPage("reports")));
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref selectedReportRegenerateInFlight, 0);
                RestoreButtonFromAnyThread(trigger, originalText);
                SetStatusFromAnyThread("选中报告重新生成失败：" + ex.Message);
            }
        });
    }
}
