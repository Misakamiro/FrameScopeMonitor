using System;
using System.IO;
using System.Reflection;

internal static class FrameScopeUninstallerSafetyTests
{
    public static int Main()
    {
        try
        {
            string temp = Path.GetFullPath(Path.GetTempPath());
            string fixture = Path.Combine(temp, "framescope-uninstall-safety-" + Guid.NewGuid().ToString("N"));
            string shared = Path.Combine(fixture, "shared-folder");
            string sentinel = Path.Combine(shared, "unrelated-user-document.txt");
            if (!Path.GetFullPath(shared).StartsWith(temp, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe test fixture");
            Directory.CreateDirectory(shared);
            File.WriteAllText(sentinel, "user data must survive");
            Console.WriteLine("Retained isolated fixture: " + fixture);
            var destructive = typeof(FrameScopeUninstaller).GetMethod("DeleteDirectoryIfSafe", BindingFlags.NonPublic | BindingFlags.Static);
            if (destructive != null)
            {
                try { destructive.Invoke(null, new object[] { shared }); }
                catch (TargetInvocationException ex) { if (!(ex.InnerException is InvalidOperationException)) throw; }
            }
            if (!File.Exists(sentinel)) throw new Exception("FAIL: the shipped helper recursively removed an unowned shared directory");

            // Only reach legacy entry points once the destructive legacy path is absent.
            if (typeof(FrameScopeUninstaller).GetMethod("ScheduleAppDirectoryRemoval", BindingFlags.NonPublic | BindingFlags.Static) != null)
                throw new Exception("FAIL: legacy helper still contains whole-install-directory deletion");
            foreach (string[] args in new[] { new[] { "/quiet" }, new[] { "--uninstall", "/quiet" }, new[] { "/delete-data", "/quiet" } })
            {
                Environment.ExitCode = 0;
                typeof(FrameScopeUninstaller).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { args });
                if (Environment.ExitCode == 0) throw new Exception("Legacy uninstall entry must fail closed");
                if (File.ReadAllText(sentinel) != "user data must survive") throw new Exception("Legacy entry modified unrelated data");
            }
            Environment.ExitCode = 0;
            Console.WriteLine("PASS: stop helper cannot delete application directories or configured data roots");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
