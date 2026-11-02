using System;
using System.IO;
using System.Threading;

internal static class FrameScopeInventorySnapshotTests
{
    private static int Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"framescope-inventory-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            string immediate=Path.Combine(root,"immediate");Directory.CreateDirectory(immediate);
            string value="{\"result\":{\"hardware\":{\"cpu\":{\"name\":\"CPU\"},\"memory\":[{\"manufacturer\":\"Kingston\",\"capacity_gb\":16}],\"memory_summary\":\"Kingston 16GB CL30\",\"memory_details\":{\"status\":\"available\"}}}}";
            FrameScopeHardwareServiceClient.SaveInventorySnapshot(immediate,delegate{return value;});
            FrameScopeHardwareServiceClient.WaitInventorySnapshot(immediate);
            if(!File.ReadAllText(Path.Combine(immediate,"hardware-inventory.json")).Contains("Kingston 16GB CL30")) throw new Exception("captured memory summary was discarded");
            string late=Path.Combine(root,"late");Directory.CreateDirectory(late);
            FrameScopeHardwareServiceClient.SaveInventorySnapshot(late,delegate{Thread.Sleep(5300);return value;});
            FrameScopeHardwareServiceClient.WaitInventorySnapshot(late);
            string frozen=File.ReadAllText(Path.Combine(late,"hardware-inventory.json"));Thread.Sleep(500);
            if(frozen!=File.ReadAllText(Path.Combine(late,"hardware-inventory.json"))) throw new Exception("late metadata rewrote a frozen run");
            if(frozen.Contains("Kingston")) throw new Exception("late/current machine information was used as capture metadata");
            if(!frozen.Contains("timeout")) throw new Exception("timed-out memory must have an explicit missing status");
            Console.WriteLine("FrameScopeInventorySnapshotTests: PASS");return 0;
        } finally {if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
    }
}
