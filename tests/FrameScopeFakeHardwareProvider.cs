using System;
using System.IO;
using System.Threading;

internal static class FrameScopeFakeHardwareProvider
{
    private static int Main()
    {
        string mode = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, mode + ".pid"),
            System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
        bool first = true;
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            if (line.Contains("exit"))
            {
                if (mode == "hang-exit") Thread.Sleep(Timeout.Infinite);
                return 0;
            }
            if (first && mode == "slow-init") Thread.Sleep(9000);
            Console.WriteLine(mode == "invalid-init" || (!first && mode == "invalid-read")
                ? "invalid JSON" : "{\"availability\":\"available\"}");
            first = false;
        }
        return 0;
    }
}
