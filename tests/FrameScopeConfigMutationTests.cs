using System;
using System.IO;
using System.Threading;

public static class FrameScopeConfigMutationTests
{
    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "framescope-config-mutation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "framescope-config.json");
            FrameScopeConfig observed = null;
            Exception failure = null;
            var entered = new ManualResetEvent(false);
            Thread reader;
            using (FrameScopeConfigStore.AcquireMutationLock(path))
            {
                reader = new Thread(delegate()
                {
                    entered.Set();
                    try { observed = FrameScopeConfigStore.Load(path); } catch (Exception error) { failure = error; }
                });
                reader.Start();
                entered.WaitOne();
                if (reader.Join(150)) throw new Exception("Missing-file loader bypassed active mutation lock.");
                var saved = FrameScopeConfigStore.CreateDefaultConfig();
                saved.ThemeMode = "dark";
                FrameScopeConfigStore.Save(path, saved);
            }
            if (!reader.Join(5000) || failure != null || observed.ThemeMode != "dark") throw new Exception("Missing-file loader overwrote a concurrent configuration creation.");
            Thread writer;
            entered.Reset();
            try
            {
                using (FrameScopeConfigStore.AcquireMutationLock(path))
                {
                    writer = new Thread(delegate()
                    {
                        entered.Set();
                        try { var update = FrameScopeConfigStore.CreateDefaultConfig(); update.ThemeMode = "light"; FrameScopeConfigStore.Save(path, update); }
                        catch (Exception error) { failure = error; }
                    });
                    writer.Start();
                    entered.WaitOne();
                    if (writer.Join(150)) throw new Exception("Direct Save bypassed active mutation lock.");
                    throw new InvalidOperationException("fixture exception");
                }
            }
            catch (InvalidOperationException) { }
            // The writer must finish after the exceptional owner releases the lock.
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (FrameScopeConfigStore.Load(path).ThemeMode != "light" && DateTime.UtcNow < deadline) Thread.Sleep(20);
            if (failure != null || FrameScopeConfigStore.Load(path).ThemeMode != "light") throw new Exception("Exceptional owner did not release the lock.");
            Console.WriteLine("FrameScopeConfigMutationTests: PASS");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
