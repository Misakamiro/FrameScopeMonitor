using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

// This bootstrap uses only Windows/.NET UI. No WebView is created until the
// verified, original FrameScope installer is launched after the prerequisite.
internal static class FrameScopeFullInstaller
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool silent = args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase) || a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase));
        string id = Guid.NewGuid().ToString("N");
        string scratch = Path.Combine(Path.GetTempPath(), "FrameScope-Full-" + id);
        string log = scratch + ".log";
        try
        {
            Directory.CreateDirectory(scratch);
            Action<string> record = text => File.AppendAllText(log, DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine, new UTF8Encoding(false));
            record("bootstrap=" + FrameScopeProductInfo.GitSha + " app=" + FrameScopeFullBuildInfo.AppGitSha);
            int code = FrameScopeFullInstallerCore.Run(
                FrameScopeFullInstallerCore.HasRuntime,
                () =>
                {
                    if (!silent && MessageBox.Show("此电脑需要 Microsoft Edge WebView2 运行时。接下来将打开微软离线安装程序，完成后自动打开 FrameScope 安装器。", "FrameScope 完整安装程序", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return 1602;
                    string runtime = Extract("FrameScope.WebView2Installer", FrameScopeFullBuildInfo.RuntimeSha256, scratch, "MicrosoftEdgeWebView2RuntimeInstallerX64.exe");
                    return Run(runtime, silent ? "/silent /install" : "/install", scratch, record, 10 * 60 * 1000);
                },
                () =>
                {
                    string app = Extract("FrameScope.MainInstaller", FrameScopeFullBuildInfo.AppSha256, scratch, "FrameScope_" + FrameScopeProductInfo.Version + "_x64-setup.exe");
                    return Run(app, String.Join(" ", args.Select(FrameScopeFullInstallerCore.QuoteArgument)), scratch, record, -1);
                });
            record("exit=" + code);
            if (!silent && code == 3010) MessageBox.Show("WebView2 安装要求重启 Windows。请重启后再次运行此完整安装包；本次未继续安装 FrameScope。", "FrameScope 完整安装程序", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (!silent && code != 0 && code != 1602) MessageBox.Show("安装未完成，退出码：" + code + "\n日志：" + log, "FrameScope 完整安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return code;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(log, ex.ToString()); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (!silent) MessageBox.Show(ex.Message + "\n日志：" + log, "FrameScope 完整安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            // Only this run's randomly named extracted files are disposable.
            // A prerequisite process still running after a timeout is not killed.
            try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Extract(string resource, string sha256, string directory, string name)
    {
        string path = Path.Combine(directory, name);
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
        {
            if (stream == null) throw new InvalidDataException("完整安装包缺少内置组件：" + resource);
            FrameScopeFullInstallerCore.ExtractVerified(stream, sha256, path);
        }
        return path;
    }

    private static int Run(string path, string arguments, string directory, Action<string> record, int timeout)
    {
        record("start=" + Path.GetFileName(path));
        using (var process = Process.Start(new ProcessStartInfo(path, arguments)
        {
            UseShellExecute = false, WorkingDirectory = directory, CreateNoWindow = true
        }))
        {
            if (process == null) throw new IOException("无法启动安装组件。");
            if (!process.WaitForExit(timeout)) throw new TimeoutException("WebView2 安装仍未结束，未继续安装 FrameScope。请等待微软安装程序完成后重试。");
            record("component-exit=" + process.ExitCode);
            return process.ExitCode;
        }
    }
}
