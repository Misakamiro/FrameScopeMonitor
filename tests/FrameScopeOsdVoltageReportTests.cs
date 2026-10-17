using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

internal static class FrameScopeOsdVoltageReportTests
{
    private static Dictionary<string, object> Read(string dir, DateTime start)
    {
        MethodInfo reader = typeof(FrameScopeReportGenerator).GetMethod("ReadCpuOsdVoltageChart", BindingFlags.NonPublic | BindingFlags.Static);
        if (reader == null) throw new Exception("Report has no reader for the recorded OSD core voltage array");
        return (Dictionary<string, object>)reader.Invoke(null, new object[] { dir, start });
    }

    public static int Main()
    {
        try { Run(); SlowInventoryDoesNotBlockReport(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
    }

    private static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-osd-voltage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DateTime start = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Local);
            string csv = Path.Combine(dir, "hardware-samples.csv");
            File.WriteAllText(Path.Combine(dir, "cpu-vid-samples.csv"), "VidVolts\r\n0.5\r\n");
            File.WriteAllText(Path.Combine(dir, "cpu-voltage-samples.csv"), "VoltageVolts\r\n1.8\r\n");
            Check(!Convert.ToBoolean(Read(dir, start)["available"]), "Missing OSD samples must not fall back to VID or another voltage source");
            File.WriteAllText(csv, "Time,CpuVoltage\r\n" + start.ToString("o") + ",1.8\r\n");
            Check(!Convert.ToBoolean(Read(dir, start)["available"]), "Aggregate CpuVoltage must not be copied to core series");
            string rows = "Time,CpuVoltage,CpuCoreVoltages\r\n" + start.AddSeconds(-1).ToString("o", CultureInfo.InvariantCulture) + ",1.8,1.8;1.8;1.8\r\n";
            string[] values = { "1.06;;1.12", "1.04;1.2;", "", "NaN;0;-1", "99;1.15;1.17" };
            for (int i = 0; i < values.Length; i++) rows += start.AddSeconds(i).ToString("o", CultureInfo.InvariantCulture) + ",1.8," + values[i] + "\r\n";
            File.WriteAllText(csv, rows);
            var chart = Read(dir, start);
            Check(Convert.ToBoolean(chart["available"]), "Valid recorded OSD core voltage should render");
            Check(Convert.ToString(chart["sourceField"]) == "CpuCoreVoltages", "Source must identify the recorded OSD array");
            var series = (List<Dictionary<string, object>>)chart["series"];
            Check(series.Count == 3, "OSD core indexes must stay independent");
            var first = (List<double?>)series[0]["data"];
            var second = (List<double?>)series[1]["data"];
            var third = (List<double?>)series[2]["data"];
            Check(first.Count == 5 && first[0] == 1.06 && first[1] == 1.04, "Report must preserve each core's recorded values and discard pre-start samples");
            Check(!second[0].HasValue && second[1] == 1.2 && second[4] == 1.15, "Missing middle core must not shift other indexes");
            Check(third[0] == 1.12 && !third[1].HasValue && third[4] == 1.17, "Missing trailing core must remain a gap");
            for (int i = 2; i < 4; i++) Check(!first[i].HasValue && !second[i].HasValue && !third[i].HasValue, "Invalid/missing core samples must remain gaps");
            File.WriteAllText(csv, "Time,CpuVoltage,CpuCoreVoltages\r\n" + start.ToString("o") + ",1.8,;;\r\n");
            Check(!Convert.ToBoolean(Read(dir, start)["available"]), "All-null core voltages must not use aggregate voltage");
            var recorder = new FrameScopeHardwareSampleRecorder(csv);
            DateTime recordingStart = DateTime.Now.AddMinutes(-1);
            var header = (string[])typeof(FrameScopeHardwareSampleRecorder).GetField("Header", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            File.WriteAllText(csv, string.Join(",", header) + "\r\n");
            typeof(FrameScopeHardwareSampleRecorder).GetMethod("AppendRow", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(recorder, new object[] { "{\"cpu_voltage\":1.8,\"cpu_core_voltages\":[1.06,null,1.12]}" });
            string[] cells = File.ReadAllLines(csv)[1].Split(',');
            int column = Array.IndexOf(header, "CpuCoreVoltages");
            Check(column >= 0 && cells[column] == "1.06;;1.12", "Recorder must persist the exact OSD array, including null indexes");
            Check(((List<Dictionary<string, object>>)Read(dir, recordingStart)["series"]).Count == 3, "Recorded array must flow into per-core report series");
            Console.WriteLine("FrameScopeOsdVoltageReportTests: PASS");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static void SlowInventoryDoesNotBlockReport()
    {
        var field = typeof(FrameScopeReportGenerator).GetField("hardwareRead", BindingFlags.NonPublic | BindingFlags.Static);
        var load = typeof(FrameScopeReportGenerator).GetMethod("LoadHardware", BindingFlags.NonPublic | BindingFlags.Static);
        object previous = field.GetValue(null);
        var pending = new System.Threading.Tasks.TaskCompletionSource<Dictionary<string, object>>();
        try
        {
            field.SetValue(null, pending.Task);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var unavailable = (Dictionary<string, object>)load.Invoke(null, null);
            Check(watch.ElapsedMilliseconds < 3000 && Convert.ToString(unavailable["QueryStatus"]) == "unavailable", "Unresponsive WMI must not prevent report generation");
            pending.SetResult(new Dictionary<string, object> { { "CpuName", "Test CPU" } });
            var available = (Dictionary<string, object>)load.Invoke(null, null);
            Check(Convert.ToString(available["CpuName"]) == "Test CPU", "Completed inventory should still enrich reports");
        }
        finally { field.SetValue(null, previous); }
    }
}
