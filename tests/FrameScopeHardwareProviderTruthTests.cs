using System;
using System.Collections.Generic;
using System.Reflection;

internal static class FrameScopeHardwareProviderTruthTests
{
    public sealed class FakeSensor
    {
        public object Name { get; set; }
        public object SensorType { get; set; }
        public object Value { get; set; }
    }

    public sealed class FakeHardware
    {
        public string Name { get; set; }
        public string HardwareType { get; set; }
        public string Identifier { get; set; }
        public object Sensors { get; set; }
        public object SubHardware { get; set; }
        public bool ThrowOnUpdate { get; set; }
        public int Updates { get; private set; }
        public void Update() { Updates++; if (ThrowOnUpdate) throw new InvalidOperationException("fixture update failure"); }
    }

    private static Type Provider { get { return typeof(FrameScopeHardwareProvider); } }
    private static Type Reading { get { return Provider.GetNestedType("Reading", BindingFlags.NonPublic); } }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static object Call(string name, params object[] args)
    {
        return Provider.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    }
    private static object NewRows() { return Activator.CreateInstance(typeof(List<>).MakeGenericType(Reading)); }
    private static void Add(object rows, string hw, string type, string identifier, string name, string sensor, double value)
    {
        object row = Activator.CreateInstance(Reading, true);
        Reading.GetField("hardware").SetValue(row, hw);
        Reading.GetField("hardwareType").SetValue(row, type);
        Reading.GetField("identifier").SetValue(row, identifier);
        Reading.GetField("name").SetValue(row, name);
        Reading.GetField("sensorType").SetValue(row, sensor);
        Reading.GetField("value").SetValue(row, value);
        ((System.Collections.IList)rows).Add(row);
    }
    private static double? Value(object row)
    {
        return row == null ? (double?)null : (double?)Reading.GetField("value").GetValue(row);
    }

    public static int Main()
    {
        try
        {
            RejectsMissingAndNonFiniteProviderValues();
            UpdateFailureDoesNotPublishOldSensorValues();
            CpuSelectionUsesClassAndNamePriority();
            GpuSelectionUsesStableHardwareIdentity();
            DynamicAggregationDoesNotBorrowUnmatchedSensors();
            Console.WriteLine("FrameScopeHardwareProviderTruthTests: PASS");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
    }

    private static void RejectsMissingAndNonFiniteProviderValues()
    {
        MethodInfo method = Provider.GetMethod("TryDouble", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (object input in new object[] { null, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            object[] args = { input, 123d };
            bool accepted = (bool)method.Invoke(null, args);
            Check(!accepted, "null and non-finite provider values are unavailable");
        }
        object[] zero = { 0d, 123d };
        Check((bool)method.Invoke(null, zero) && (double)zero[1] == 0d, "a real zero remains a valid reading");
    }

    private static void UpdateFailureDoesNotPublishOldSensorValues()
    {
        var hardware = new FakeHardware
        {
            Name = "Fixture CPU", HardwareType = "Cpu", Identifier = "/cpu/0",
            Sensors = new[] { new FakeSensor { Name = "CPU Total", SensorType = "Load", Value = 75d } },
            SubHardware = new object[0], ThrowOnUpdate = true
        };
        var rows = (System.Collections.IList)NewRows();
        var errors = new List<string>();
        Provider.GetMethod("ReadHardware", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { hardware, rows, null, errors });
        Check(hardware.Updates == 1, "fixture hardware Update invoked once");
        Check(rows.Count == 0, "failed Update publishes no previous sensor values");
        Check(errors.Count == 1 && errors[0].Contains("hardware update failed"), "failed Update is reported in this snapshot");
    }

    private static void CpuSelectionUsesClassAndNamePriority()
    {
        object rows = NewRows();
        Add(rows, "GPU", "Gpu", "/gpu/0", "CPU Total", "Load", 99);
        Add(rows, "Board", "SuperIO", "/board", "CPU Total", "Load", 88);
        Add(rows, "Processor", "Cpu", "/cpu/0", "Total", "Load", 44);
        Add(rows, "Processor", "Cpu", "/cpu/0", "CPU Total", "Load", 55);
        var selected = Call("Find", rows, "Cpu", new[] { "Load", "CPU Total", "Total" });
        Check(Value(selected) == 55, "CPU class filtering and sensor-name priority are applied before row order");
        Check(Call("Find", rows, "Cpu", new[] { "Temperature", "Tctl/Tdie", "CPU Package" }) == null,
            "missing CPU sensor is unavailable instead of borrowing another hardware class");
    }

    private static void GpuSelectionUsesStableHardwareIdentity()
    {
        object rows = NewRows();
        Add(rows, "Same GPU", "Gpu", "PCI\\GPU-A", "GPU Core", "Temperature", 61);
        Add(rows, "Same GPU", "Gpu", "PCI\\GPU-B", "GPU Core", "Temperature", 82);
        MethodInfo find = Provider.GetMethod("FindGpu", BindingFlags.NonPublic | BindingFlags.Static);
        Check(find != null, "GPU selection is keyed by stable hardware identity");
        Check(Value(find.Invoke(null, new object[] { rows, "PCI\\GPU-B", new[] { "Temperature", "GPU Core", "GPU Hot Spot" } })) == 82,
            "same-name GPUs do not share sensor values");
        Check(find.Invoke(null, new object[] { rows, "PCI\\GPU-C", new[] { "Temperature", "GPU Core" } }) == null,
            "an absent GPU identity does not borrow another device's value");
    }

    private static void DynamicAggregationDoesNotBorrowUnmatchedSensors()
    {
        object rows = NewRows();
        Add(rows, "Processor", "Cpu", "/cpu/0", "Unrelated temperature", "Temperature", 99);
        Add(rows, "GPU A", "Gpu", "", "GPU Core", "Temperature", 61);
        Add(rows, "GPU B", "Gpu", "", "GPU Core", "Temperature", 82);
        var data = (Dictionary<string, object>)Call("BuildDynamic", rows, "fixture-time");
        Check(((Dictionary<string, object>)data["cpuTemperature"])["value"] == null,
            "production aggregation returns null when no CPU candidate name matches");
        var gpus = (object[])data["gpu"];
        Check(gpus.Length == 2, "different named devices without identifiers remain separate");
        var gpuB = (Dictionary<string, object>)gpus[1];
        Check((double)((Dictionary<string, object>)gpuB["temperature"])["value"] == 82,
            "production GPU aggregation does not borrow the first unidentified device");
    }
}
