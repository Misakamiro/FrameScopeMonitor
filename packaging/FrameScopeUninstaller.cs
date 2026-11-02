using System;
using System.IO;
using System.Windows.Forms;

internal static class FrameScopeUninstaller
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--shutdown-capability" || args[0] == "--shutdown-plan" || args[0] == "--stop-installation"))
        {
            // A winexe has no console codepage; write UTF-8 directly to inherited pipes.
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
        }
        if (args.Length > 0 && args[0] == "--shutdown-capability")
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Expected --shutdown-capability without arguments.");
                Environment.ExitCode = 2;
                return;
            }
            Console.WriteLine(FrameScopeInstallationShutdown.Capability);
            return;
        }
        if (args.Length > 0 && (args[0] == "--stop-installation" || args[0] == "--shutdown-plan"))
        {
            try
            {
                if (args.Length != 2) throw new InvalidOperationException("Expected --stop-installation <absolute root>.");
                if (args[0] == "--shutdown-plan")
                {
                    Console.WriteLine(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { capability = FrameScopeInstallationShutdown.Capability,
                        maximumBudgetMs = FrameScopeInstallationShutdown.MaximumBudget(args[1]) }));
                    return;
                }
                string warning = FrameScopeInstallationShutdown.Stop(args[1]);
                Console.WriteLine(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { status = warning.Length == 0 ? "stopped" : "stopped-with-warning", warning = warning }));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = ex is TimeoutException ? 3 : 2;
            }
            return;
        }
        // The current product uninstalls through the transactional Rust installer.
        // This executable is only the verified normal-stop helper; never delete a
        // registry-selected install directory or an arbitrary configured DataRoot.
        bool quiet = Array.Exists(args ?? new string[0], arg =>
            string.Equals(arg, "/quiet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--quiet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/S", StringComparison.OrdinalIgnoreCase));
        const string message = "此程序仅用于正常停止 FrameScope，不执行卸载或删除数据。请通过 Windows“已安装的应用”中的 FrameScope 卸载入口操作。";
        Console.Error.WriteLine(message);
        Environment.ExitCode = 2;
        if (!quiet) MessageBox.Show(message, "FrameScope 辅助程序", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
