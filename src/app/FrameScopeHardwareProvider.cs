using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
        public string identifier = null;
        public string subHardware = null;
        public string name = "";
        public string sensorType = "";
        public double value;
        public string unit = null;
    }

    private static int Main(string[] args)
    {
        // Setting the console encodings throws when the process was started
        // without a console (CREATE_NO_WINDOW); encoding is handled explicitly
        // in serve mode via raw streams, so failures here are non-fatal.
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
        try { Console.InputEncoding = new UTF8Encoding(false); } catch { }
        if (args.Any(a => String.Equals(a, "--serve", StringComparison.OrdinalIgnoreCase)))
        {
            return ServeMain();
        }
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

    // Resident mode: open LibreHardwareMonitor once, then serve snapshot reads from
    // stdin. Mirrors the NexBoxMonitor pull protocol: each `{"cmd":"read"}` line
    // yields one JSON payload line; `{"cmd":"exit"}` shuts the process down. A
    // hung Computer.Open() must not take the resident process with it, so Open
    // runs on a worker with a 15s timeout and the process stays alive either way.
    private static int ServeMain()
    {
        // Raw redirected streams with explicit UTF-8 (no BOM): the resident
        // process may run without any console at all (CREATE_NO_WINDOW).
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));

        object computer = null;
        List<string> openErrors = new List<string>();
        Dictionary<string, object> pawnIoStatus = new Dictionary<string, object> { { "installed", false }, { "version", "" } };
        long sequence = 0;
        try
        {
            string dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LibreHardwareMonitorLib.dll");
            if (!File.Exists(dll)) throw new FileNotFoundException("LibreHardwareMonitorLib.dll was not found", dll);
            Assembly assembly = Assembly.LoadFrom(dll);
            pawnIoStatus = ReadPawnIoStatus(assembly);
            var openTask = System.Threading.Tasks.Task.Run(delegate
            {
                try
                {
                    object c = Activator.CreateInstance(assembly.GetType("LibreHardwareMonitor.Hardware.Computer", true));
                    Set(c, "IsCpuEnabled", true); Set(c, "IsGpuEnabled", true);
                    Set(c, "IsMemoryEnabled", true); Set(c, "IsMotherboardEnabled", true);
                    Set(c, "IsStorageEnabled", true); Set(c, "IsControllerEnabled", true);
                    Invoke(c, "Open");
                    computer = c;
                    Console.Error.WriteLine("[FrameScopeHardwareProvider] serve: Computer.Open() completed");
                }
                catch (Exception ex)
                {
                    lock (openErrors) { openErrors.Add(RootMessage(ex)); }
                    Console.Error.WriteLine("[FrameScopeHardwareProvider] serve: hardware init error: " + RootMessage(ex));
                }
            });
            if (!openTask.Wait(TimeSpan.FromSeconds(15)))
            {
                Console.Error.WriteLine("[FrameScopeHardwareProvider] serve: Computer.Open() timed out (15s); serving partial data");
            }
        }
        catch (Exception ex)
        {
            lock (openErrors) { openErrors.Add(RootMessage(ex)); }
        }

        string line;
        while ((line = stdin.ReadLine()) != null)
        {
            line = line.Trim('\0').TrimStart('\uFEFF').Trim();
            if (line.Length == 0) continue;
            string cmd = "";
            try
            {
                Dictionary<string, object> request = Json.Deserialize<Dictionary<string, object>>(line);
                object value;
                if (request != null && request.TryGetValue("cmd", out value) && value != null)
                    cmd = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
            catch { continue; }
            if (cmd == "exit") break;
            if (cmd == "static")
            {
                // Inventory enrichment must not block the sensor protocol.
                if (_staticCacheTask == null && (_staticCache == null || (DateTime.UtcNow - _staticCacheAt).TotalSeconds > 30))
                {
                    _staticCacheTask = System.Threading.Tasks.Task.Factory.StartNew(delegate {
                        var staticErrors = new List<string>();
                        try { return Tuple.Create(BuildStaticFull(staticErrors), staticErrors.ToArray()); }
                        catch (Exception ex) { return Tuple.Create(new Dictionary<string, object>(), new[] { RootMessage(ex) }); }
                    });
                }
                if (_staticCacheTask != null && _staticCacheTask.Wait(1000))
                {
                    var result = _staticCacheTask.Result;
                    _staticCache = result.Item1;
                    _staticCacheErrors = result.Item2;
                    _staticCacheAt = DateTime.UtcNow;
                    _staticCacheTask = null;
                }
                var staticPayload = new Dictionary<string, object>
                {
                    { "capturedAt", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) },
                    { "staticFull", _staticCache },
                    { "errors", _staticCache == null ? new[] { "Hardware inventory is still loading; sensor sampling remains available." } : _staticCacheErrors }
                };
                stdout.WriteLine(Json.Serialize(staticPayload));
                continue;
            }
            if (cmd != "read") continue;

            sequence++;
            string capturedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            List<Reading> readings = new List<Reading>();
            List<string> errors;
            lock (openErrors) { errors = new List<string>(openErrors); }
            if (computer != null)
            {
                foreach (object hardware in Enumerate(Get(computer, "Hardware")))
                {
                    try { ReadHardware(hardware, readings, null, errors); }
                    catch (Exception ex) { errors.Add("hardware read failed: " + RootMessage(ex)); }
                }
            }
            else if (errors.Count == 0)
            {
                errors.Add("LibreHardwareMonitor provider is still initializing");
            }

            string availability = readings.Count > 0 ? (errors.Count > 0 ? "partial" : "available") : "unavailable";
            Dictionary<string, object> payload = new Dictionary<string, object>
            {
                { "capturedAt", capturedAt }, { "sequence", sequence }, { "availability", availability },
                { "static", BuildStatic(readings) }, { "dynamic", BuildDynamic(readings, capturedAt) },
                { "sensors", readings.Select(ToJson).Cast<object>().ToArray() },
                { "errors", errors.ToArray() }, { "pawnIo", pawnIoStatus }
            };
            stdout.WriteLine(Json.Serialize(payload));
        }

        if (computer != null) { try { Invoke(computer, "Close"); } catch { } }
        return 0;
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
            foreach (object hardware in Enumerate(Get(computer, "Hardware"))) ReadHardware(hardware, readings, null, errors);
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

    private static void ReadHardware(object hardware, List<Reading> output, string parent, List<string> errors)
    {
        if (hardware == null) return;
        bool updated = true;
        try { Invoke(hardware, "Update"); } catch (Exception ex) { updated = false; if (errors != null) errors.Add("hardware update failed: " + RootMessage(ex)); }
        string hw = StringValue(Get(hardware, "Name"));
        string type = StringValue(Get(hardware, "HardwareType"));
        foreach (object sensor in updated ? Enumerate(Get(hardware, "Sensors")) : Enumerable.Empty<object>())
        {
            double value;
            if (!TryDouble(Get(sensor, "Value"), out value) || Double.IsNaN(value) || Double.IsInfinity(value)) continue;
            output.Add(new Reading
            {
                hardware = hw, hardwareType = type, identifier = StringValue(Get(hardware, "Identifier")), subHardware = parent,
                name = StringValue(Get(sensor, "Name")), sensorType = StringValue(Get(sensor, "SensorType")),
                value = value, unit = UnitFor(StringValue(Get(sensor, "SensorType")))
            });
        }
        foreach (object child in Enumerate(Get(hardware, "SubHardware"))) ReadHardware(child, output, hw, errors);
    }

    private static Dictionary<string, object> _staticCache;
    private static string[] _staticCacheErrors = new string[0];
    private static DateTime _staticCacheAt = DateTime.MinValue;
    private static System.Threading.Tasks.Task<Tuple<Dictionary<string, object>, string[]>> _staticCacheTask;

    private static string WmiGet(System.Management.ManagementObject o, string p)
    {
        try { return StringValue(o[p]); } catch { return ""; }
    }

    private static double? WmiGetDouble(System.Management.ManagementObject o, string p)
    {
        try { if (o[p] == null) return null; return Convert.ToDouble(o[p], CultureInfo.InvariantCulture); } catch { return null; }
    }

    private static string WmiFriendlyDate(object value)
    {
        try { return System.Management.ManagementDateTimeConverter.ToDateTime(StringValue(value)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } catch { return ""; }
    }

    private static double RegistryGpuMemoryBytes(string pnpDeviceId, string gpuName)
    {
        // AdapterRAM (uint32) overflows for cards with >4GB; the display-class
        // registry key carries the true 64-bit HardwareInformation.qwMemorySize.
        // MatchingDeviceId values vary (some lack the instance path), so match
        // by DriverDesc first, then by the device-id prefix.
        try
        {
            string baseKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using (var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(baseKey))
            {
                if (root == null) return 0;
                foreach (string sub in root.GetSubKeyNames())
                {
                    using (var key = root.OpenSubKey(sub))
                    {
                        if (key == null) continue;
                        object rawSize = key.GetValue("HardwareInformation.qwMemorySize");
                        if (rawSize == null) continue;
                        string driverDesc = Convert.ToString(key.GetValue("DriverDesc"), CultureInfo.InvariantCulture) ?? "";
                        string matchingId = Convert.ToString(key.GetValue("MatchingDeviceId"), CultureInfo.InvariantCulture) ?? "";
                        bool descMatch = !String.IsNullOrWhiteSpace(gpuName) && driverDesc.Equals(gpuName, StringComparison.OrdinalIgnoreCase);
                        bool idMatch = !String.IsNullOrWhiteSpace(pnpDeviceId) && pnpDeviceId.StartsWith(matchingId, StringComparison.OrdinalIgnoreCase);
                        if (!descMatch && !idMatch) continue;
                        double parsedSize;
                        if (rawSize is long) return (long)rawSize;
                        if (rawSize is int) return (int)rawSize;
                        if (Double.TryParse(StringValue(rawSize), NumberStyles.Float, CultureInfo.InvariantCulture, out parsedSize)) return parsedSize;
                    }
                }
            }
        }
        catch { }
        return 0;
    }

    private static string GpuVendorFor(string adapterCompatibility)
    {
        string value = adapterCompatibility ?? "";
        if (value.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return "NVIDIA";
        if (value.IndexOf("Advanced Micro", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("ATI", StringComparison.OrdinalIgnoreCase) >= 0) return "AMD";
        if (value.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) return "Intel";
        return "Unknown";
    }

    // Heuristic for indirect/virtual display adapters (GameViewer, Duet,
    // spacedesk, mirroring drivers, ...): they enumerate through the same
    // Win32_VideoController table but must not headline the GPU list.
    private static bool LooksLikeVirtualGpu(Dictionary<string, object> gpu)
    {
        if (gpu == null) return false;
        string name = (gpu.ContainsKey("name") ? gpu["name"] as string : null) ?? "";
        string lower = name.ToLowerInvariant();
        return lower.Contains("virtual") || lower.Contains("indirect") || lower.Contains("remote")
            || lower.Contains("mirroring") || lower.Contains("vmware") || lower.Contains("parallels")
            || lower.Contains("displaylink") || lower.Contains("spacedesk");
    }

    private static string[] WmiStringArray(System.Management.ManagementObject o, string p)
    {
        try
        {
            object raw = o[p];
            if (raw is System.Collections.IEnumerable && !(raw is string))
            {
                List<string> result = new List<string>();
                foreach (object item in (System.Collections.IEnumerable)raw) result.Add(StringValue(item));
                return result.ToArray();
            }
        }
        catch { }
        return new string[0];
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle device,
        uint control, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    private static bool? ReadDiskSolidState(string devicePath)
    {
        if (String.IsNullOrWhiteSpace(devicePath)) return null;
        // Query only: no read/write access and no disk contents are read. Use the
        // exact Win32_DiskDrive device, avoiding model-based joins across disks.
        using (var device = CreateFile(devicePath, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            if (device.IsInvalid) return null;
            var query = new byte[12]; // STORAGE_PROPERTY_QUERY / PropertyStandardQuery
            query[0] = 7; // StorageDeviceSeekPenaltyProperty
            var descriptor = new byte[12]; // DEVICE_SEEK_PENALTY_DESCRIPTOR
            int returned;
            if (!DeviceIoControl(device, 0x002D1400, query, query.Length,
                descriptor, descriptor.Length, out returned, IntPtr.Zero)) return null;
            return ParseDiskSolidState(descriptor, returned);
        }
    }

    private static bool? ParseDiskSolidState(byte[] descriptor, int returned)
    {
        if (descriptor == null || returned < 12 || descriptor.Length < 12
            || BitConverter.ToUInt32(descriptor, 0) < 12 || BitConverter.ToUInt32(descriptor, 4) < 12) return null;
        return descriptor[8] == 0; // IncursSeekPenalty is a one-byte BOOLEAN.
    }

    private static bool? ResolveDiskSolidState(bool? deviceResult, string model, string mediaType)
    {
        if (deviceResult.HasValue) return deviceResult;
        string text = (model ?? "") + " " + (mediaType ?? "");
        if (text.IndexOf("SSD", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (text.IndexOf("HDD", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        // "Fixed hard disk media" describes removability, not rotational media.
        return null;
    }

    /// Full frontend-facing static hardware info (snake_case contract shared
    /// with the NexBox-derived UI). Every field degrades to null/"" on its own.
    private static Dictionary<string, object> BuildStaticFull(List<string> errors)
    {
        var cpu = new Dictionary<string, object>();
        var gpus = new List<object>();
        var memory = new List<object>();
        var disks = new List<object>();
        var soundCards = new List<object>();
        var networkCards = new List<object>();
        var monitors = new List<object>();
        var motherboard = new Dictionary<string, object>();

        try
        {
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    double? architecture = WmiGetDouble(o, "Architecture");
                    string archText = architecture.HasValue
                        ? (architecture.Value == 9 ? "x64" : architecture.Value == 0 ? "x86" : architecture.Value == 12 ? "ARM64" : architecture.Value == 6 ? "Itanium" : architecture.Value.ToString(CultureInfo.InvariantCulture))
                        : "";
                    cpu["name"] = WmiGet(o, "Name");
                    cpu["manufacturer"] = WmiGet(o, "Manufacturer");
                    cpu["cores"] = WmiGetDouble(o, "NumberOfCores") ?? 0;
                    cpu["threads"] = WmiGetDouble(o, "NumberOfLogicalProcessors") ?? 0;
                    cpu["max_clock_speed"] = WmiGetDouble(o, "MaxClockSpeed") ?? 0;
                    cpu["l2_cache_size"] = WmiGetDouble(o, "L2CacheSize") ?? 0;
                    cpu["l3_cache_size"] = WmiGetDouble(o, "L3CacheSize") ?? 0;
                    cpu["load_percentage"] = WmiGetDouble(o, "LoadPercentage");
                    cpu["architecture"] = archText;
                    cpu["socket"] = WmiGet(o, "SocketDesignation");
                    cpu["l2_cache_speed"] = WmiGetDouble(o, "L2CacheSpeed");
                    cpu["l3_cache_speed"] = WmiGetDouble(o, "L3CacheSpeed");
                    cpu["current_clock_speed"] = WmiGetDouble(o, "CurrentClockSpeed");
                    cpu["ext_clock"] = WmiGetDouble(o, "ExtClock");
                    cpu["processor_id"] = WmiGet(o, "ProcessorId");
                    cpu["family"] = WmiGetDouble(o, "Family");
                    cpu["stepping"] = WmiGet(o, "Stepping");
                    cpu["revision"] = WmiGet(o, "Revision");
                    cpu["enabled_cores"] = WmiGetDouble(o, "NumberOfEnabledCore");
                    double? voltageCaps = WmiGetDouble(o, "VoltageCaps");
                    cpu["voltage_caps"] = voltageCaps.HasValue && voltageCaps.Value != 0 ? voltageCaps.Value.ToString(CultureInfo.InvariantCulture) : (object)"";
                    break; // one physical CPU entry is enough for the UI
                }
            }
        }
        catch (Exception ex) { errors.Add("Win32_Processor failed: " + RootMessage(ex)); }
        if (cpu.Count == 0) { foreach (string key in new[] { "name", "manufacturer", "architecture", "socket", "processor_id", "stepping", "revision" }) cpu[key] = ""; }

        try
        {
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    double? ram = WmiGetDouble(o, "AdapterRAM");
                    string gpuName = WmiGet(o, "Name");
                    double registryBytes = RegistryGpuMemoryBytes(WmiGet(o, "PNPDeviceID"), gpuName);
                    if (registryBytes > 0) ram = registryBytes;
                    double? memoryGb = ram.HasValue && ram.Value > 0 ? Math.Round(ram.Value / 1073741824.0, 1) : (double?)null;
                    double? width = WmiGetDouble(o, "CurrentHorizontalResolution");
                    double? height = WmiGetDouble(o, "CurrentVerticalResolution");
                    double? refresh = WmiGetDouble(o, "CurrentRefreshRate");
                    double? memoryType = WmiGetDouble(o, "VideoMemoryType");
                    double? architectureCode = WmiGetDouble(o, "VideoArchitecture");
                    gpus.Add(new Dictionary<string, object>
                    {
                        { "name", WmiGet(o, "Name") },
                        { "vendor", GpuVendorFor(WmiGet(o, "AdapterCompatibility")) },
                        { "memory_gb", memoryGb },
                        { "driver_version", WmiGet(o, "DriverVersion") },
                        { "temperature", (object)null },
                        { "usage", (object)null },
                        { "video_processor", WmiGet(o, "VideoProcessor") },
                        { "adapter_compatibility", WmiGet(o, "AdapterCompatibility") },
                        { "driver_date", WmiFriendlyDate(o["DriverDate"]) },
                        { "installed_drivers", WmiGet(o, "InstalledDisplayDrivers") },
                        { "video_mode", WmiGet(o, "VideoModeDescription") },
                        { "resolution_width", width },
                        { "resolution_height", height },
                        { "refresh_rate", refresh },
                        { "device_id", WmiGet(o, "DeviceID") },
                        { "pnp_device_id", WmiGet(o, "PNPDeviceID") },
                        { "status", WmiGet(o, "Status") },
                        { "inf_filename", WmiGet(o, "InfFilename") },
                        { "video_architecture", architectureCode.HasValue ? architectureCode.Value.ToString(CultureInfo.InvariantCulture) : (object)null },
                        { "video_memory_type", memoryType.HasValue ? memoryType.Value.ToString(CultureInfo.InvariantCulture) : (object)null }
                    });
                }
            }
        }
        catch (Exception ex) { errors.Add("Win32_VideoController failed: " + RootMessage(ex)); }

        // WMI returns virtual display adapters (GameViewer/indirect screens)
        // before the physical GPU on some machines, which made the hero card
        // present a virtual device as the main graphics card (audit F-3).
        // Stable sort: physical adapters first, virtual ones last.
        gpus = gpus.OrderBy(g => LooksLikeVirtualGpu(g as Dictionary<string, object>)).ToList();

        try
        {
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    double? capacity = WmiGetDouble(o, "Capacity");
                    double? smbiosType = WmiGetDouble(o, "SMBIOSMemoryType");
                    double? formFactor = WmiGetDouble(o, "FormFactor");
                    string formFactorText = formFactor.HasValue ? (formFactor.Value == 8 ? "DIMM" : formFactor.Value == 12 ? "SODIMM" : formFactor.Value.ToString(CultureInfo.InvariantCulture)) : "";
                    string memoryTypeText = smbiosType.HasValue
                        ? (smbiosType.Value == 20 ? "DDR" : smbiosType.Value == 21 ? "DDR2" : smbiosType.Value == 24 ? "DDR3" : smbiosType.Value == 26 ? "DDR4" : smbiosType.Value == 34 ? "DDR5" : smbiosType.Value.ToString(CultureInfo.InvariantCulture))
                        : "";
                    memory.Add(new Dictionary<string, object>
                    {
                        { "manufacturer", WmiGet(o, "Manufacturer") },
                        { "part_number", WmiGet(o, "PartNumber").Trim('\0').Trim() },
                        { "capacity_gb", capacity.HasValue ? Math.Round(capacity.Value / 1073741824.0, 1) : 0 },
                        { "speed_mhz", WmiGetDouble(o, "Speed") ?? 0 },
                        { "bank_label", WmiGet(o, "BankLabel") },
                        { "form_factor", formFactorText },
                        { "memory_type", memoryTypeText },
                        { "configured_clock_speed", WmiGetDouble(o, "ConfiguredClockSpeed") },
                        { "configured_voltage", WmiGetDouble(o, "ConfiguredVoltage") },
                        { "data_width", WmiGetDouble(o, "DataWidth") },
                        { "total_width", WmiGetDouble(o, "TotalWidth") },
                        { "serial_number", WmiGet(o, "SerialNumber") },
                        { "type_detail", WmiGet(o, "TypeDetail") }
                    });
                }
            }
        }
        catch (Exception ex) { errors.Add("Win32_PhysicalMemory failed: " + RootMessage(ex)); }

        try
        {
            string baseboardProduct = "", baseboardManufacturer = "", baseboardSerial = "", baseboardVersion = "";
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    baseboardProduct = WmiGet(o, "Product");
                    baseboardManufacturer = WmiGet(o, "Manufacturer");
                    baseboardSerial = WmiGet(o, "SerialNumber");
                    baseboardVersion = WmiGet(o, "Version");
                    break;
                }
            }
            string biosVendor = "", biosVersion = "", biosDate = "";
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_BIOS"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    biosVendor = WmiGet(o, "Manufacturer");
                    biosVersion = WmiGet(o, "SMBIOSBIOSVersion");
                    biosDate = WmiFriendlyDate(o["ReleaseDate"]);
                    break;
                }
            }
            string systemManufacturer = "", systemModel = "", systemType = "";
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    systemManufacturer = WmiGet(o, "Manufacturer");
                    systemModel = WmiGet(o, "Model");
                    systemType = WmiGet(o, "SystemType");
                    break;
                }
            }
            string chassisType = "";
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_SystemEnclosure"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    string[] chassisTypes = WmiStringArray(o, "ChassisTypes");
                    if (chassisTypes.Length > 0)
                    {
                        double code = 0;
                        if (Double.TryParse(chassisTypes[0], NumberStyles.Float, CultureInfo.InvariantCulture, out code))
                            chassisType = code == 3 || code == 4 || code == 6 || code == 7 ? "Desktop" : code == 8 || code == 9 || code == 10 || code == 14 ? "Laptop" : code == 31 || code == 32 ? "Convertible" : chassisTypes[0];
                    }
                    break;
                }
            }
            motherboard["product"] = baseboardProduct;
            motherboard["manufacturer"] = baseboardManufacturer;
            motherboard["serial_number"] = baseboardSerial;
            motherboard["version"] = baseboardVersion;
            motherboard["bios_vendor"] = biosVendor;
            motherboard["bios_version"] = biosVersion;
            motherboard["bios_release_date"] = biosDate;
            motherboard["system_manufacturer"] = systemManufacturer;
            motherboard["system_model"] = systemModel;
            motherboard["system_type"] = systemType;
            motherboard["chassis_type"] = chassisType;
        }
        catch (Exception ex) { errors.Add("motherboard info failed: " + RootMessage(ex)); }

        try
        {
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    double? size = WmiGetDouble(o, "Size");
                    double? bytesPerSector = WmiGetDouble(o, "BytesPerSector");
                    string model = WmiGet(o, "Model");
                    string mediaType = WmiGet(o, "MediaType");
                    bool? isSsd = ResolveDiskSolidState(ReadDiskSolidState(WmiGet(o, "DeviceID")), model, mediaType);
                    disks.Add(new Dictionary<string, object>
                    {
                        { "model", model },
                        { "size_gb", size.HasValue ? Math.Round(size.Value / 1073741824.0, 1) : 0 },
                        { "interface_type", WmiGet(o, "InterfaceType") },
                        { "serial_number", WmiGet(o, "SerialNumber") },
                        { "firmware_revision", WmiGet(o, "FirmwareRevision") },
                        { "media_type", mediaType },
                        { "bytes_per_sector", bytesPerSector },
                        { "partitions", WmiGetDouble(o, "Partitions") ?? 0 },
                        { "status", WmiGet(o, "Status") },
                        { "is_ssd", isSsd }
                    });
                }
            }
        }
        catch (Exception ex) { errors.Add("Win32_DiskDrive failed: " + RootMessage(ex)); }

        try
        {
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_SoundDevice"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    soundCards.Add(new Dictionary<string, object>
                    {
                        { "name", WmiGet(o, "Name") },
                        { "manufacturer", WmiGet(o, "Manufacturer") },
                        { "status", WmiGet(o, "Status") },
                        { "device_id", WmiGet(o, "DeviceID") },
                        { "pnp_device_id", WmiGet(o, "PNPDeviceID") }
                    });
                }
            }
        }
        catch (Exception ex) { errors.Add("Win32_SoundDevice failed: " + RootMessage(ex)); }

        try
        {
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapter"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    object physical = null;
                    try { physical = o["Physical"]; } catch { }
                    string connectionName = WmiGet(o, "NetConnectionID");
                    string adapterName = WmiGet(o, "Name");
                    // Some systems report Physical=false for every adapter
                    // (virtualized NICs); fall back to connected, non-miniport
                    // adapters so real hardware is not hidden.
                    bool isPhysical = physical is bool && (bool)physical;
                    bool looksReal = connectionName.Length > 0 && adapterName.IndexOf("Miniport", StringComparison.OrdinalIgnoreCase) < 0;
                    if (!isPhysical && !looksReal) continue;
                    double? speed = WmiGetDouble(o, "Speed");
                    // WMI reports unknown link speed as the int64 sentinel.
                    double? speedMbps = speed.HasValue && speed.Value > 0 && speed.Value < 1e15 ? Math.Round(speed.Value / 1000000.0) : (double?)null;
                    networkCards.Add(new Dictionary<string, object>
                    {
                        { "name", WmiGet(o, "Name") },
                        { "manufacturer", WmiGet(o, "Manufacturer") },
                        { "adapter_type", WmiGet(o, "AdapterType") },
                        { "mac_address", WmiGet(o, "MACAddress") },
                        { "speed_mbps", speedMbps ?? 0 },
                        { "connection_name", WmiGet(o, "NetConnectionID") },
                        { "service_name", WmiGet(o, "ServiceName") },
                        { "index", WmiGetDouble(o, "Index") ?? 0 },
                        { "max_speed", (object)null },
                        { "guid", WmiGet(o, "GUID") }
                    });
                }
            }
        }
        catch (Exception ex) { errors.Add("Win32_NetworkAdapter failed: " + RootMessage(ex)); }

        try
        {
            // Monitor identity from WmiMonitorID (root\wmi), mode from the
            // active video controller. Friendly names arrive as ushort arrays.
            var monitorNames = new List<Tuple<string, string, string>>();
            using (var searcher = new System.Management.ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorID"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    monitorNames.Add(Tuple.Create(DecodeMonitorId(o["UserFriendlyName"]), DecodeMonitorId(o["ManufacturerName"]), WmiGet(o, "InstanceName")));
                }
            }
            var videoModes = new List<Tuple<double?, double?, double?>>();
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController"))
            using (var results = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in results)
                {
                    videoModes.Add(Tuple.Create(WmiGetDouble(o, "CurrentHorizontalResolution"), WmiGetDouble(o, "CurrentVerticalResolution"), WmiGetDouble(o, "CurrentRefreshRate")));
                }
            }
            for (int i = 0; i < monitorNames.Count; i++)
            {
                Tuple<double?, double?, double?> mode = videoModes.Count > 0 ? videoModes[Math.Min(i, videoModes.Count - 1)] : Tuple.Create<double?, double?, double?>(null, null, null);
                monitors.Add(new Dictionary<string, object>
                {
                    { "name", monitorNames[i].Item1.Length > 0 ? monitorNames[i].Item1 : monitorNames[i].Item2 },
                    { "manufacturer", monitorNames[i].Item2 },
                    { "screen_width", mode.Item1 },
                    { "screen_height", mode.Item2 },
                    { "refresh_rate", mode.Item3 },
                    { "pnp_device_id", monitorNames[i].Item3 },
                    { "status", "" },
                    { "availability", (object)null }
                });
            }
        }
        catch (Exception ex) { errors.Add("monitor info failed: " + RootMessage(ex)); }

        return new Dictionary<string, object>
        {
            { "cpu", cpu }, { "gpu", gpus.ToArray() }, { "memory", memory.ToArray() },
            { "motherboard", motherboard }, { "disk", disks.ToArray() }, { "sound_card", soundCards.ToArray() },
            { "network_card", networkCards.ToArray() }, { "monitor", monitors.ToArray() }
        };
    }

    private static string DecodeMonitorId(object value)
    {
        try
        {
            if (!(value is System.Collections.IEnumerable) || value is string) return "";
            var bytes = new List<byte>();
            foreach (object item in (System.Collections.IEnumerable)value)
            {
                ushort code = Convert.ToUInt16(item, CultureInfo.InvariantCulture);
                if (code == 0) break;
                bytes.Add((byte)code);
            }
            return Encoding.ASCII.GetString(bytes.ToArray()).Trim();
        }
        catch { return ""; }
    }

    private static Dictionary<string, object> BuildStatic(List<Reading> readings)
    {
        string cpu = readings.Where(r => r.hardwareType.IndexOf("Cpu", StringComparison.OrdinalIgnoreCase) >= 0).Select(r => r.hardware).FirstOrDefault() ?? "";
        string[] gpus = readings.Where(r => r.hardwareType.IndexOf("Gpu", StringComparison.OrdinalIgnoreCase) >= 0).Select(r => r.hardware).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var memory = ReadMemoryStatus();
        double? mem = memory == null ? (double?)null : memory.TotalPhysical / 1073741824d;
        return new Dictionary<string, object>
        {
            { "cpuName", cpu }, { "gpuNames", gpus.Cast<object>().ToArray() }, { "memoryTotalGb", mem },
            { "storageNames", DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name.TrimEnd('\\')).Cast<object>().ToArray() },
            { "motherboard", readings.Where(r => r.hardwareType.IndexOf("SuperIO", StringComparison.OrdinalIgnoreCase) >= 0).Select(r => r.subHardware).FirstOrDefault() ?? "" }
        };
    }

    private static Dictionary<string, object> BuildDynamic(List<Reading> rows, string at)
    {
        Reading cpuLoad = Find(rows, "Cpu", "Load", "CPU Total", "Total");
        Reading cpuTemp = Find(rows, "Cpu", "Temperature", "Tctl/Tdie", "CPU Package", "Core Average", "CPU Total");
        Reading cpuClock = Find(rows, "Cpu", "Clock", "Cores (Average)", "CPU Core", "Core Effective Clock", "CPU Clock");
        Reading cpuPower = Find(rows, "Cpu", "Power", "Package", "CPU Package", "CPU PPT");
        var memory = ReadMemoryStatus();
        double? total = memory == null ? (double?)null : memory.TotalPhysical / 1073741824d;
        double? free = memory == null ? (double?)null : memory.AvailablePhysical / 1073741824d;
        double? used = total.HasValue && free.HasValue ? Math.Max(0, total.Value - free.Value) : (double?)null;
        List<Dictionary<string, object>> gpu = new List<Dictionary<string, object>>();
        foreach (var gpuIdentity in rows.Where(r => r.hardwareType.IndexOf("Gpu", StringComparison.OrdinalIgnoreCase) >= 0)
            .GroupBy(r => String.IsNullOrWhiteSpace(r.identifier) ? "name:" + r.hardware : "id:" + r.identifier, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { name = g.First().hardware, identifier = g.First().identifier, readings = g.ToList() }))
        {
            gpu.Add(new Dictionary<string, object> { { "name", gpuIdentity.name }, { "identifier", gpuIdentity.identifier }, { "usage", Metric(FindGpu(gpuIdentity.readings, gpuIdentity.identifier, "Load", "GPU Core"), "%", at) }, { "temperature", Metric(FindGpu(gpuIdentity.readings, gpuIdentity.identifier, "Temperature", "GPU Core", "GPU Hot Spot"), "°C", at) }, { "power", Metric(FindGpu(gpuIdentity.readings, gpuIdentity.identifier, "Power", "GPU Power"), "W", at) } });
        }
        return new Dictionary<string, object>
        {
            { "cpuUsage", Metric(cpuLoad, "%", at) }, { "cpuTemperature", Metric(cpuTemp, "°C", at) }, { "cpuFrequency", Metric(cpuClock, "MHz", at) }, { "cpuPower", Metric(cpuPower, "W", at) },
            { "gpu", gpu.Cast<object>().ToArray() }, { "memoryUsedGb", Metric(used, "GB", "GlobalMemoryStatusEx", at) }, { "memoryTotalGb", Metric(total, "GB", "GlobalMemoryStatusEx", at) },
            { "storageUsedGb", Metric(null, "GB", "framescope-lhm", at) }, { "storageTotalGb", Metric(null, "GB", "framescope-lhm", at) }
        };
    }

    private static Dictionary<string, object> Metric(Reading r, string unit, string at) { return Metric(r == null ? (double?)null : r.value, unit, r == null ? "framescope-lhm" : "librehardwaremonitor", at); }
    private static Dictionary<string, object> Metric(double? value, string unit, string source, string at) { return new Dictionary<string, object> { { "value", value }, { "unit", unit }, { "source", source }, { "capturedAt", at } }; }
    private static Dictionary<string, object> ToJson(Reading r) { return new Dictionary<string, object> { { "hardware", r.hardware }, { "hardwareType", r.hardwareType }, { "identifier", r.identifier }, { "subHardware", r.subHardware }, { "name", r.name }, { "sensorType", r.sensorType }, { "value", r.value }, { "unit", r.unit } }; }
    private static Reading Find(List<Reading> rows, string hardwareType, params string[] terms)
    {
        if (terms == null || terms.Length == 0) return null;
        string sensorType = terms[0];
        string[] names = terms.Skip(1).ToArray();
        return rows.Where(r => r.hardwareType.IndexOf(hardwareType, StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(r => r.sensorType.IndexOf(sensorType, StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(r => names.Any(name => r.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderBy(r => { int i = Array.FindIndex(names, name => r.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0); return i < 0 ? int.MaxValue : i; })
            .FirstOrDefault();
    }

    private static Reading FindGpu(List<Reading> rows, string identifier, params string[] terms)
    {
        return Find(rows.Where(r => string.Equals(r.identifier ?? "", identifier ?? "", StringComparison.OrdinalIgnoreCase)).ToList(), "Gpu", terms);
    }
    private static string UnitFor(string type) { if (type.IndexOf("Temperature", StringComparison.OrdinalIgnoreCase) >= 0) return "°C"; if (type.IndexOf("Load", StringComparison.OrdinalIgnoreCase) >= 0) return "%"; if (type.IndexOf("Clock", StringComparison.OrdinalIgnoreCase) >= 0) return "MHz"; if (type.IndexOf("Power", StringComparison.OrdinalIgnoreCase) >= 0) return "W"; if (type.IndexOf("Voltage", StringComparison.OrdinalIgnoreCase) >= 0) return "V"; if (type.IndexOf("Fan", StringComparison.OrdinalIgnoreCase) >= 0) return "RPM"; return null; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private sealed class MemoryStatus
    {
        public uint Length = 64;
        public uint Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] MemoryStatus status);
    private static MemoryStatus ReadMemoryStatus()
    {
        var status = new MemoryStatus();
        return GlobalMemoryStatusEx(status) ? status : null;
    }
    private static object Get(object o, string p) { if (o == null) return null; PropertyInfo i = o.GetType().GetProperty(p, BindingFlags.Public | BindingFlags.Instance); return i == null ? null : i.GetValue(o, null); }
    private static void Set(object o, string p, bool v) { PropertyInfo i = o.GetType().GetProperty(p, BindingFlags.Public | BindingFlags.Instance); if (i != null && i.CanWrite) i.SetValue(o, v, null); }
    private static object Invoke(object o, string m) { MethodInfo i = o.GetType().GetMethod(m, BindingFlags.Public | BindingFlags.Instance); return i == null ? null : i.Invoke(o, null); }
    private static IEnumerable<object> Enumerate(object value) { var e = value as System.Collections.IEnumerable; if (e == null) yield break; foreach (object item in e) yield return item; }
    private static bool TryDouble(object value, out double n) {
        if (value == null) { n = 0; return false; }
        try { n = Convert.ToDouble(value, CultureInfo.InvariantCulture); return !Double.IsNaN(n) && !Double.IsInfinity(n); }
        catch { n = 0; return false; }
    }
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
