using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

internal static class FrameScopeHardwareSnapshotTests
{
    public static int Main()
    {
        try
        {
            Type provider = typeof(FrameScopeHardwareProvider);
            Type reading = provider.GetNestedType("Reading", BindingFlags.NonPublic);
            object readings = Activator.CreateInstance(typeof(List<>).MakeGenericType(reading));
            var watch = Stopwatch.StartNew();
            var stat = (Dictionary<string, object>)provider.GetMethod("BuildStatic", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { readings });
            var data = (Dictionary<string, object>)provider.GetMethod("BuildDynamic", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { readings, (object)DateTimeOffset.Now.ToString("O") });
            var total = (Dictionary<string, object>)data["memoryTotalGb"];
            var used = (Dictionary<string, object>)data["memoryUsedGb"];
            if (watch.ElapsedMilliseconds > 1000) throw new Exception("Sensor snapshot is waiting for optional inventory");
            if (Convert.ToDouble(total["value"]) <= 0 || Convert.ToDouble(used["value"]) > Convert.ToDouble(total["value"])) throw new Exception("Native physical memory values are invalid");
            if (Convert.ToDouble(stat["memoryTotalGb"]) != Convert.ToDouble(total["value"])) throw new Exception("Static and dynamic memory totals differ");
            if ((string)total["source"] != "GlobalMemoryStatusEx") throw new Exception("Memory source metadata is incorrect");
            Console.WriteLine("FrameScopeHardwareSnapshotTests: PASS");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
