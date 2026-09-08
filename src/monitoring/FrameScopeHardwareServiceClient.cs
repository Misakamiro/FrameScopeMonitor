using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// Named-pipe client for FrameScopeHardwareService (protocol
// framescope-hardware-service/1). Used by the capture worker to record the
// same aggregate snapshot the overlay displays. Short-lived connections
// (connect → one request → read → dispose): a stalled service can never wedge
// the recorder, and the service's multi-instance accept loop serves each
// connection independently.
internal static class FrameScopeHardwareServiceClient
{
    private const string PipeName = "FrameScopeHardwareService";
    private const int ConnectTimeoutMs = 800;
    private const int ReadTimeoutMs = 4000;
    private static int _requestSequence;

    /// Returns the raw `result` JSON for get_overlay_data, or null when the
    /// service is unavailable. Never throws.
    public static string QueryOverlayData()
    {
        try
        {
            var line = Exchange("{\"id\":\"" + Interlocked.Increment(ref _requestSequence).ToString(CultureInfo.InvariantCulture) + "\",\"method\":\"get_overlay_data\",\"params\":{}}");
            if (string.IsNullOrWhiteSpace(line)) return null;
            var serializer = new JavaScriptSerializer();
            var response = serializer.Deserialize<Dictionary<string, object>>(line);
            if (response == null || !response.ContainsKey("ok") || !Convert.ToBoolean(response["ok"], CultureInfo.InvariantCulture)) return null;
            var result = response.ContainsKey("result") ? response["result"] as Dictionary<string, object> : null;
            return result == null ? null : serializer.Serialize(result);
        }
        catch
        {
            return null;
        }
    }

    /// One request over a fresh pipe connection; spawns the service when the
    /// pipe has no listener and retries once.
    private static string Exchange(string request)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(ConnectTimeoutMs);
                    var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true);
                    writer.AutoFlush = true;
                    var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                    using (writer)
                    {
                        using (reader)
                        {
                            writer.WriteLine(request);
                            var readTask = System.Threading.Tasks.Task.Factory.StartNew(delegate() { return reader.ReadLine(); });
                            if (!readTask.Wait(ReadTimeoutMs)) return null;
                            return readTask.Result;
                        }
                    }
                }
            }
            catch
            {
                // Second consecutive failure gives up; the first falls through
                // so EnsureServiceRunning below can bring the service up.
                if (attempt > 0) return null;
            }
            if (attempt == 0) EnsureServiceRunning();
        }
        return null;
    }

    /// Probes the pipe; when nobody is listening, starts the service from the
    /// application directory (a second instance of the service simply exits on
    /// its own when the pipe name is already taken).
    public static void EnsureServiceRunning()
    {
        try
        {
            using (var probe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                probe.Connect(400);
                return; // somebody is listening
            }
        }
        catch { }
        try
        {
            var root = AppDomain.CurrentDomain.BaseDirectory;
            var service = Path.Combine(root, "FrameScopeHardwareService.exe");
            var helper = Path.Combine(root, "FrameScopeHardwareProvider.exe");
            if (!File.Exists(service) || !File.Exists(helper)) return;
            var start = new ProcessStartInfo
            {
                FileName = service,
                Arguments = "--pipe-name " + QuoteArgument(PipeName) + " --lhm-helper " + QuoteArgument(helper),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (Process.Start(start)) { }
        }
        catch { }
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}

/// Appends one hardware-samples.csv row per second while a capture session is
/// running. The values come from the same hardware-service snapshot the
/// overlay displays, so the report matches what the user saw.
internal sealed class FrameScopeHardwareSampleRecorder
{
    private static readonly string[] Header =
    {
        // Column names after "Time" for the GPU block intentionally match the
        // system-samples.csv GPU columns so the report merge can overlay them.
        "Time", "CpuUsage", "CpuTempC", "CpuClockMHz", "CpuPowerW", "CpuVoltage",
        "GpuUtilPct", "GpuTempC", "PowerW", "GpuClockMHz", "MemClockMHz",
        "VramUsedMiB", "VramTotalMiB", "MemoryUsedPct", "SsdTempC", "GamePing",
        // Fps is the resident ETW FPS session's value from the same overlay
        // snapshot. It lets the report still build a full FPS series when an
        // anti-cheat denies PresentMon's own ETW trace (Battlefield 6).
        "Fps"
    };

    private readonly string _csvPath;
    private Thread _thread;
    private volatile bool _stop;

    public FrameScopeHardwareSampleRecorder(string csvPath)
    {
        _csvPath = csvPath;
    }

    public void Start()
    {
        try
        {
            File.WriteAllText(_csvPath, string.Join(",", Header) + "\r\n", new UTF8Encoding(false));
        }
        catch { return; }
        _thread = new Thread(RecordLoop) { IsBackground = true, Name = "framescope-hardware-recorder" };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        // One exchange can take ~6s worst case (spawn probe + connect + read timeouts).
        if (_thread != null) _thread.Join(8000);
    }

    private void RecordLoop()
    {
        while (!_stop)
        {
            var json = FrameScopeHardwareServiceClient.QueryOverlayData();
            if (!string.IsNullOrEmpty(json))
            {
                try { AppendRow(json); }
                catch { }
            }
            // Roughly one second per row; the snapshot itself is throttled
            // server-side, so a small drift here is harmless.
            Thread.Sleep(1000);
        }
    }

    private void AppendRow(string json)
    {
        var serializer = new JavaScriptSerializer();
        var data = serializer.Deserialize<Dictionary<string, object>>(json);
        if (data == null) return;
        var values = new[]
        {
            DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            Text(data, "cpu_usage"),
            Text(data, "cpu_temp"),
            Text(data, "cpu_clock"),
            Text(data, "cpu_power"),
            Text(data, "cpu_voltage"),
            Text(data, "gpu_usage"),
            Text(data, "gpu_temp"),
            Text(data, "gpu_power"),
            Text(data, "gpu_clock"),
            Text(data, "gpu_memory_clock"),
            GibToMib(Text(data, "gpu_vram_used")),
            GibToMib(Text(data, "gpu_vram_total")),
            Text(data, "memory_usage"),
            Text(data, "ssd_temp"),
            Text(data, "game_ping"),
            Text(data, "fps")
        };
        File.AppendAllText(_csvPath, string.Join(",", values) + "\r\n", new UTF8Encoding(false));
    }

    private static string Text(Dictionary<string, object> data, string key)
    {
        object value;
        if (data == null || !data.TryGetValue(key, out value) || value == null) return "";
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return text == null ? "" : text.Replace(",", "");
    }

    private static string GibToMib(string gibText)
    {
        double gib;
        if (!double.TryParse(gibText, NumberStyles.Float, CultureInfo.InvariantCulture, out gib)) return "";
        return Math.Round(gib * 1024.0, 1).ToString(CultureInfo.InvariantCulture);
    }
}
