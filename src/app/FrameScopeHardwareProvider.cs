using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

// Small out-of-process adapter for the official LibreHardwareMonitor library.
// The Rust service owns lifecycle/protocol; this process owns the .NET-only DLL.
internal static class FrameScopeHardwareProvider
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    private sealed class Reading
    {
        public string hardware = "";
        public string hardwareType = "";
        public string subHardware = null;
        public string name = "";
        public string sensorType = "";
        public double value;
        public string unit = null;
    }

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);
        string method = ResolveMethod(args);
        try
        {
            Dictionary<string, object> payload = Capture(method);
            Console.Out.WriteLine(Json.Serialize(payload));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Out.WriteLine(Json.Serialize(new Dictionary<string, object>
            {
                { "availability", "unavailable" }, { "static", null }, { "dynamic", null },
                { "sensors", new object[0] }, { "errors", new[] { "LibreHardwareMonitor provider failed: " + RootMessage(ex) } },
                { "capturedAt", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) }, { "sequence", 0L }
            }));
            return 0;
        }
    }

    private static Dictionary<string, object> Capture(string method)
    {
        string capturedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
        List<Reading> readings = new List<Reading>();
        List<string> errors = new List<string>();
        Dictionary<string, object> pawnIoStatus = new Dictionary<string, object> { { "installed", false }, { "version", "" } };
        object computer = null;
        try
        {
            string dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LibreHardwareMonitorLib.dll");
            if (!File.Exists(dll)) throw new FileNotFoundException("LibreHardwareMonitorLib.dll was not found", dll);
            Assembly assembly = Assembly.LoadFrom(dll);
            pawnIoStatus = ReadPawnIoStatus(assembly);
            Type computerType = assembly.GetType("LibreHardwareMonitor.Hardware.Computer", true);
            computer = Activator.CreateInstance(computerType);
            Set(computer, "IsCpuEnabled", true); Set(computer, "IsGpuEnabled", true);
            Set(computer, "IsMemoryEnabled", true); Set(computer, "IsMotherboardEnabled", true);
            Set(computer, "IsStorageEnabled", true); Set(computer, "IsControllerEnabled", true);
            Invoke(computer, "Open");
            foreach (object hardware in Enumerate(Get(computer, "Hardware"))) ReadHardware(hardware, readings, null);
        }
        catch (Exception ex) { errors.Add(RootMessage(ex)); }
        finally { if (computer != null) { try { Invoke(computer, "Close"); } catch { } } }

        Dictionary<string, object> stat = BuildStatic(readings);
        Dictionary<string, object> dynamic = BuildDynamic(readings, capturedAt);
        object[] sensorObjects = readings.Select(ToJson).Cast<object>().ToArray();
        string availability = readings.Count > 0 ? "available" : "unavailable";
        if (errors.Count > 0 && readings.Count > 0) availability = "partial";
        bool pawnIoInstalled = pawnIoStatus.ContainsKey("installed") && Convert.ToBoolean(pawnIoStatus["installed"], CultureInfo.InvariantCulture);
        string pawnIoVersion = pawnIoStatus.ContainsKey("version") ? Convert.ToString(pawnIoStatus["version"], CultureInfo.InvariantCulture) : "";
        if (!pawnIoInstalled || String.IsNullOrWhiteSpace(pawnIoVersion))
        {
            pawnIoStatus["availability"] = "unavailable";
            pawnIoStatus["error"] = "PawnIO is not fully installed; Vcore and driver-backed sensors may be unavailable.";
        }
        else pawnIoStatus["availability"] = "available";
        if (string.Equals(method, "get_sensors", StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, object> { { "availability", availability }, { "sensors", sensorObjects }, { "errors", errors.ToArray() }, { "updatedAt", capturedAt }, { "capturedAt", capturedAt }, { "pawnIo", pawnIoStatus } };
        if (string.Equals(method, "get_static_info", StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, object> { { "availability", availability }, { "static", stat }, { "errors", errors.ToArray() }, { "capturedAt", capturedAt }, { "pawnIo", pawnIoStatus } };
        return new Dictionary<string, object>
        {
            { "capturedAt", capturedAt }, { "sequence", DateTime.UtcNow.Ticks }, { "availability", availability },
            { "static", stat }, { "dynamic", dynamic }, { "sensors", sensorObjects }, { "errors", errors.ToArray() }, { "pawnIo", pawnIoStatus }
        };
    }

    private static void ReadHardware(object hardware, List<Reading> output, string parent)
    {
        if (hardware == null) return;
        try { Invoke(hardware, "Update"); } catch { }
        string hw = StringValue(Get(hardware, "Name"));
        string type = StringValue(Get(hardware, "HardwareType"));
        foreach (object sensor in Enumerate(Get(hardware, "Sensors")))
        {
            double value;
            if (!TryDouble(Get(sensor, "Value"), out value) || Double.IsNaN(value) || Double.IsInfinity(value)) continue;
            output.Add(new Reading
            {
                hardware = hw, hardwareType = type, subHardware = parent,
                name = StringValue(Get(sensor, "Name")), sensorType = StringValue(Get(sensor, "SensorType")),
                value = value, unit = UnitFor(StringValue(Get(sensor, "SensorType")))
            });
        }
        foreach (object child in Enumerate(Get(hardware, "SubHardware"))) ReadHardware(child, output, hw);
    }

    private static Dictionary<string, object> BuildStatic(List<Reading> readings)
    {
        string cpu = readings.Where(r => r.hardwareType.IndexOf("Cpu", StringComparison.OrdinalIgnoreCase) >= 0).Select(r => r.hardware).FirstOrDefault() ?? "";
        string[] gpus = readings.Where(r => r.hardwareType.IndexOf("Gpu", StringComparison.OrdinalIgnoreCase) >= 0).Select(r => r.hardware).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        double? mem = WmiBytes("Win32_ComputerSystem", "TotalPhysicalMemory");
        return new Dictionary<string, object>
        {
            { "cpuName", cpu }, { "gpuNames", gpus.Cast<object>().ToArray() }, { "memoryTotalGb", mem },
            { "storageNames", DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name.TrimEnd('\\')).Cast<object>().ToArray() },
            { "motherboard", WmiString("Win32_BaseBoard", "Product") }
        };
    }

    private static Dictionary<string, object> BuildDynamic(List<Reading> rows, string at)
    {
        Reading cpuLoad = Find(rows, "Load", "CPU Total", "Total");
        Reading cpuTemp = Find(rows, "Temperature", "Tctl/Tdie", "CPU Package", "Core Average", "CPU Total");
        Reading cpuClock = Find(rows, "Clock", "Cores (Average)", "CPU Core", "Core Effective Clock", "CPU Clock");
        Reading cpuPower = Find(rows, "Power", "Package", "CPU Package", "CPU PPT");
        double? total = WmiBytes("Win32_ComputerSystem", "TotalPhysicalMemory");
        double? free = WmiBytes("Win32_OperatingSystem", "FreePhysicalMemory");
        double? used = total.HasValue && free.HasValue ? Math.Max(0, total.Value - free.Value) : (double?)null;
        List<Dictionary<string, object>> gpu = new List<Dictionary<string, object>>();
        foreach (string name in rows.Where(r => r.hardwareType.IndexOf("Gpu", StringComparison.OrdinalIgnoreCase) >= 0).Select(r => r.hardware).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            gpu.Add(new Dictionary<string, object> { { "name", name }, { "usage", Metric(Find(rows, "Load", name, "GPU Core"), "%", at) }, { "temperature", Metric(Find(rows, "Temperature", name, "GPU Core", "GPU Hot Spot"), "°C", at) }, { "power", Metric(Find(rows, "Power", name, "GPU Power"), "W", at) } });
        }
        return new Dictionary<string, object>
        {
            { "cpuUsage", Metric(cpuLoad, "%", at) }, { "cpuTemperature", Metric(cpuTemp, "°C", at) }, { "cpuFrequency", Metric(cpuClock, "MHz", at) }, { "cpuPower", Metric(cpuPower, "W", at) },
            { "gpu", gpu.Cast<object>().ToArray() }, { "memoryUsedGb", Metric(used, "GB", "framescope-lhm-wmi", at) }, { "memoryTotalGb", Metric(total, "GB", "framescope-lhm-wmi", at) },
            { "storageUsedGb", Metric(null, "GB", "framescope-lhm", at) }, { "storageTotalGb", Metric(null, "GB", "framescope-lhm", at) }
        };
    }

    private static Dictionary<string, object> Metric(Reading r, string unit, string at) { return Metric(r == null ? (double?)null : r.value, unit, r == null ? "framescope-lhm" : "librehardwaremonitor", at); }
    private static Dictionary<string, object> Metric(double? value, string unit, string source, string at) { return new Dictionary<string, object> { { "value", value }, { "unit", unit }, { "source", source }, { "capturedAt", at } }; }
    private static Dictionary<string, object> ToJson(Reading r) { return new Dictionary<string, object> { { "hardware", r.hardware }, { "hardwareType", r.hardwareType }, { "subHardware", r.subHardware }, { "name", r.name }, { "sensorType", r.sensorType }, { "value", r.value }, { "unit", r.unit } }; }
    private static Reading Find(List<Reading> rows, params string[] terms)
    {
        if (terms == null || terms.Length == 0) return null;
        string sensorType = terms[0];
        string[] names = terms.Skip(1).ToArray();
        return rows.FirstOrDefault(r => r.sensorType.IndexOf(sensorType, StringComparison.OrdinalIgnoreCase) >= 0 && names.Any(name => (r.hardware + " " + r.name).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
            ?? rows.FirstOrDefault(r => r.sensorType.IndexOf(sensorType, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    private static string UnitFor(string type) { if (type.IndexOf("Temperature", StringComparison.OrdinalIgnoreCase) >= 0) return "°C"; if (type.IndexOf("Load", StringComparison.OrdinalIgnoreCase) >= 0) return "%"; if (type.IndexOf("Clock", StringComparison.OrdinalIgnoreCase) >= 0) return "MHz"; if (type.IndexOf("Power", StringComparison.OrdinalIgnoreCase) >= 0) return "W"; if (type.IndexOf("Voltage", StringComparison.OrdinalIgnoreCase) >= 0) return "V"; if (type.IndexOf("Fan", StringComparison.OrdinalIgnoreCase) >= 0) return "RPM"; return null; }
    private static double? WmiBytes(string cls, string property) { double n; return Double.TryParse(WmiString(cls, property), NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n / (property.IndexOf("FreePhysicalMemory", StringComparison.OrdinalIgnoreCase) >= 0 ? 1048576d : 1073741824d) : (double?)null; }
    private static string WmiString(string cls, string property) { try { using (var s = new System.Management.ManagementObjectSearcher("SELECT " + property + " FROM " + cls)) using (var r = s.Get()) foreach (System.Management.ManagementObject o in r) return Convert.ToString(o[property], CultureInfo.InvariantCulture) ?? ""; } catch { } return ""; }
    private static object Get(object o, string p) { if (o == null) return null; PropertyInfo i = o.GetType().GetProperty(p, BindingFlags.Public | BindingFlags.Instance); return i == null ? null : i.GetValue(o, null); }
    private static void Set(object o, string p, bool v) { PropertyInfo i = o.GetType().GetProperty(p, BindingFlags.Public | BindingFlags.Instance); if (i != null && i.CanWrite) i.SetValue(o, v, null); }
    private static object Invoke(object o, string m) { MethodInfo i = o.GetType().GetMethod(m, BindingFlags.Public | BindingFlags.Instance); return i == null ? null : i.Invoke(o, null); }
    private static IEnumerable<object> Enumerate(object value) { var e = value as System.Collections.IEnumerable; if (e == null) yield break; foreach (object item in e) yield return item; }
    private static bool TryDouble(object value, out double n) { try { n = Convert.ToDouble(value, CultureInfo.InvariantCulture); return true; } catch { n = 0; return false; } }
    private static string StringValue(object value) { return Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""; }
    private static string Arg(string[] args, string name, string fallback) { for (int i = 0; i + 1 < args.Length; i++) if (String.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1]; return fallback; }
    private static string RootMessage(Exception ex) { while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException; return ex.Message; }

    private static string ResolveMethod(string[] args)
    {
        string explicitMethod = Arg(args, "--method", "");
        if (!String.IsNullOrWhiteSpace(explicitMethod)) return explicitMethod;
        try
        {
            string line = Console.ReadLine();
            if (!String.IsNullOrWhiteSpace(line))
            {
                line = line.Trim('\0').TrimStart('\uFEFF').Trim();
                Dictionary<string, object> request = Json.Deserialize<Dictionary<string, object>>(line);
                object value;
                if (request != null && request.TryGetValue("method", out value) && value != null && !String.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)))
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
        catch { }
        return "get_snapshot";
    }

    private static Dictionary<string, object> ReadPawnIoStatus(Assembly assembly)
    {
        Dictionary<string, object> status = new Dictionary<string, object> { { "installed", false }, { "version", "" } };
        try
        {
            Type pawnIoType = assembly.GetType("LibreHardwareMonitor.PawnIo.PawnIo", false);
            if (pawnIoType == null) return status;
            PropertyInfo installed = pawnIoType.GetProperty("IsInstalled", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo version = pawnIoType.GetProperty("Version", BindingFlags.Public | BindingFlags.Static);
            status["installed"] = installed != null && Convert.ToBoolean(installed.GetValue(null, null), CultureInfo.InvariantCulture);
            status["version"] = version == null || version.GetValue(null, null) == null ? "" : Convert.ToString(version.GetValue(null, null), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { status["error"] = RootMessage(ex); }
        return status;
    }

}
