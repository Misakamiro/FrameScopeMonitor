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
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int flags);

    private static void CaptureUiScreenshot(string path, string pageKey)
    {
        if (form == null) return;
        try
        {
            pageKey = NormalizeVisiblePageKey(pageKey);
            if (!string.IsNullOrWhiteSpace(pageKey)) ShowPage(pageKey);
            form.Opacity = 1;
            form.ShowInTaskbar = false;
            form.TopMost = true;
            form.StartPosition = FormStartPosition.Manual;
            bool screenCapture =
                string.Equals(pageKey, "targets", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pageKey, "settings", StringComparison.OrdinalIgnoreCase);
            form.Location = screenCapture ? new Point(20, 20) : new Point(-32000, -32000);
            form.CreateControl();
            form.Show();
            form.Activate();
            form.BringToFront();
            form.Refresh();
            Application.DoEvents();
            Thread.Sleep(280);
            Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                if (screenCapture)
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(form.Location, Point.Empty, bitmap.Size);
                    }
                }
                else if (!TryPrintWindow(form, bitmap))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                }
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                bitmap.Save(path);
            }
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(path + ".error.txt", ex.ToString(), Encoding.UTF8); }
            catch { }
        }
        finally
        {
            form.Close();
        }
    }

    private static bool TryPrintWindow(Form target, Bitmap bitmap)
    {
        if (target == null || bitmap == null || target.IsDisposed) return false;
        try
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    return PrintWindow(target.Handle, hdc, 0);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }
        }
        catch
        {
            return false;
        }
    }

    private static void CaptureSidebarScreenshot(string path, string activeKey)
    {
        try
        {
            activeKey = NormalizeVisiblePageKey(activeKey);
            using (var sidebar = new FrameScopeReferenceSidebar
            {
                Size = new Size(FrameScopeReferenceSidebar.DesignWidth, FrameScopeReferenceSidebar.DesignHeight),
                ActiveKey = string.IsNullOrWhiteSpace(activeKey) ? "overview" : activeKey,
                VersionText = AppVersionText(),
                Accent = UiCyan,
                SuccessColor = UiGreen
            })
            using (var bitmap = new Bitmap(FrameScopeReferenceSidebar.DesignWidth, FrameScopeReferenceSidebar.DesignHeight))
            {
                sidebar.CreateControl();
                sidebar.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                bitmap.Save(path);
            }
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(path + ".error.txt", ex.ToString(), Encoding.UTF8); }
            catch { }
        }
    }
}
