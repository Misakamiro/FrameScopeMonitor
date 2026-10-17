using System;
using System.IO;
using System.Threading;

public static class FrameScopeConfigLockFixture
{
    public static int Main(string[] args)
    {
        if (args.Length != 3 && args.Length != 4) return 2;
        using (FrameScopeConfigStore.AcquireMutationLock(args[0]))
        {
            File.WriteAllText(args[1], "ready");
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (!File.Exists(args[2]) && DateTime.UtcNow < deadline) Thread.Sleep(20);
            if (File.Exists(args[2]) && args.Length == 4 && args[3] == "save-target")
            {
                FrameScopeConfig config = FrameScopeConfigStore.Load(args[0]);
                config.Targets = new System.Collections.Generic.List<FrameScopeTarget>
                {
                    new FrameScopeTarget { Enabled = true, Name = "Concurrent", ProcessName = "Concurrent.exe" }
                };
                FrameScopeConfigStore.Save(args[0], config);
            }
            return File.Exists(args[2]) ? 0 : 3;
        }
    }
}
