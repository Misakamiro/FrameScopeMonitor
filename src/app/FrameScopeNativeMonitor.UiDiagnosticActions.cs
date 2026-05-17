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
    private static int diagnosticReportInFlight;

    private static void OpenDataRoot()
    {
        try
        {
            var path = ResolveCurrentDataRoot();
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            SetStatus("已打开输出目录：" + path);
        }
        catch (Exception ex)
        {
            SetStatus("打开输出目录失败：" + ex.Message);
        }
    }

    private static void GenerateDiagnosticReportFromUi()
    {
        GenerateDiagnosticReportFromUi(null);
    }

    private static void GenerateDiagnosticReportFromUi(Button trigger)
    {
        if (Interlocked.CompareExchange(ref diagnosticReportInFlight, 1, 0) != 0)
        {
            SetStatus("诊断报告正在后台生成，请等待完成。");
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
            Interlocked.Exchange(ref diagnosticReportInFlight, 0);
            RestoreButtonFromAnyThread(trigger, originalText);
            SetStatus("诊断报告生成失败：" + ex.Message);
            MessageBox.Show(ex.Message, "诊断报告失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        SetStatus("正在后台生成诊断报告...");
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                var result = FrameScopeDiagnostics.GenerateReport(config, Root, ResolveDataRoot(config.DataRoot), "manual-ui", "");
                WriteFrameScopeLog("diagnostic-report-generated path=" + result.MarkdownPath);
                SetStatusFromAnyThread("诊断报告已生成：" + result.MarkdownPath);
            }
            catch (Exception ex)
            {
                WriteFrameScopeLog("diagnostic-report-failed " + ex.Message);
                SetStatusFromAnyThread("诊断报告生成失败：" + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref diagnosticReportInFlight, 0);
                RestoreButtonFromAnyThread(trigger, originalText);
            }
        });
    }

    private static void OpenDiagnosticFolder()
    {
        var path = FrameScopeDiagnostics.DefaultDiagnosticRoot;
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private static void SetStatusFromAnyThread(string text)
    {
        try
        {
            if (form != null && form.InvokeRequired)
            {
                form.BeginInvoke((MethodInvoker)(() => SetStatus(text)));
            }
            else
            {
                SetStatus(text);
            }
        }
        catch { }
    }

    private static string SetButtonBusy(Button button, string busyText)
    {
        if (button == null) return "";
        string originalText = button.Text;
        button.Enabled = false;
        button.Text = busyText;
        return originalText;
    }

    private static void RestoreButtonFromAnyThread(Button button, string originalText)
    {
        if (button == null) return;
        try
        {
            if (button.InvokeRequired)
            {
                button.BeginInvoke((MethodInvoker)(() => RestoreButton(button, originalText)));
            }
            else
            {
                RestoreButton(button, originalText);
            }
        }
        catch { }
    }

    private static void RestoreButton(Button button, string originalText)
    {
        if (button == null || button.IsDisposed) return;
        if (!string.IsNullOrEmpty(originalText)) button.Text = originalText;
        button.Enabled = true;
    }
}
