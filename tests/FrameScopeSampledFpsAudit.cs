using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

internal static class FrameScopeSampledFpsAudit
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private static readonly List<object> Assertions = new List<object>();
    private static readonly Dictionary<string, string> Fixtures = new Dictionary<string, string>();
    private static int failures;
    private static MethodInfo generate;
    private static string output;

    private static int Main(string[] args)
    {
        string assemblyPath = args.Length > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FrameScopeReportManifestTests.exe");
        output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "framescope-fps-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        generate = Assembly.LoadFrom(Path.GetFullPath(assemblyPath)).GetType("FrameScopeReportGenerator")
            .GetMethod("GenerateForTests", BindingFlags.NonPublic | BindingFlags.Static);
        try
        {
            var sampled = Generate("sampled", "60", "120", true, false);
            CheckSampled(sampled, 2, 90);
            var raw = Generate("raw", "60", "120", true, true);
            var rawStats = Map(raw, "frameStats");
            Check(Convert.ToInt32(Map(raw, "counts")["frames"]) == 2, "PresentMon frame count is preserved");
            Check(Convert.ToString(Map(raw, "manifest")["reportKind"]) == "full", "healthy genuine frames retain full classification");
            Check(Convert.ToDouble(rawStats["average"]) == 66.67 && Convert.ToDouble(rawStats["low1"]) == 50, "raw average and low use real frame durations");
            Check(Value(Map(raw, "fps"), "source") as string == "presentmon", "PresentMon takes precedence over hardware FPS");
            Check(Convert.ToInt32(Value(Map(raw, "counts"), "fpsSamples")) == 0, "unused fallback is not counted");

            var gaps = Generate("sampled-gaps", "60", "", false, false,
                "2026-09-18T00:00:02Z,-1\r\n2026-09-18T00:00:03Z,NaN\r\n2026-09-18T00:00:04Z,0\r\n2026-09-18T00:00:05Z,1200\r\n");
            CheckSampled(gaps, 3, 420);
            IList values = List(Map(gaps, "fps"), "avg");
            Check(values.Count == 6 && values[1] == null && values[2] == null && values[3] == null, "missing and invalid samples remain gaps");
            Check(values.Count == 6 && Convert.ToDouble(values[4]) == 0 && Convert.ToDouble(values[5]) == 1200, "measured zero and finite high FPS remain samples");

            var empty = Generate("empty", "", "-1", false, false);
            Check(Convert.ToString(Map(empty, "manifest")["reportKind"]) == "error", "invalid-only data does not become diagnostic FPS");
            Check(List(Map(empty, "fps"), "avg").Count == 0, "invalid-only data does not produce a trend");
            Check(Value(Map(empty, "frameStats"), "framesOver33") == null, "absent frames do not imply zero long frames");
        }
        catch (Exception ex)
        {
            failures++;
            Console.Error.WriteLine(ex);
        }
        File.WriteAllText(Path.Combine(output, "audit-result.json"), Json.Serialize(new
        {
            failures = failures, assertions = Assertions, fixtures = Fixtures, assembly = Path.GetFullPath(assemblyPath)
        }));
        Console.WriteLine("FrameScopeSampledFpsAudit: assertions=" + Assertions.Count + " failures=" + failures + " output=" + output);
        return failures == 0 ? 0 : 1;
    }

    private static Dictionary<string, object> Generate(string name, string first, string second, bool auxiliary, bool raw, string tail = "")
    {
        string run = Path.Combine(output, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        Fixtures[name] = run;
        var status = new Dictionary<string, object>
        {
            { "Phase", "done" }, { "TargetProcess", "game.exe" },
            { "FrameCaptureStatus", raw ? "captured" : "no-presentmon-csv" },
            { "FrameCaptureMessage", "No anti-cheat denial was observed." }
        };
        foreach (string prefix in new[] { "ProcessSampler", "SystemSampler" })
        {
            status[prefix + "Required"] = true;
            status[prefix + "ExecutableAvailable"] = auxiliary;
            status[prefix + "Started"] = auxiliary;
            status[prefix + "Pid"] = 4242;
            status[prefix + "StartedAt"] = "2026-09-18T00:00:00Z";
            status[prefix + "ExitedAt"] = "2026-09-18T00:00:06Z";
            status[prefix + "ExitCode"] = 0;
            status[prefix + "StopRequested"] = true;
        }
        File.WriteAllText(Path.Combine(run, "status.json"), Json.Serialize(status));
        if (auxiliary)
        {
            File.WriteAllText(Path.Combine(run, "process-samples.csv"),
                "Time,ProcessName,SampleIndex,CpuPct,WorkingSetMB\r\n2026-09-18T00:00:00Z,game,0,10,256\r\n");
            File.WriteAllText(Path.Combine(run, "system-samples.csv"),
                "Time,TotalCpuPct,AvailableMB\r\n2026-09-18T00:00:00Z,10,16000\r\n");
        }
        if (raw)
            File.WriteAllText(Path.Combine(run, "presentmon.csv"),
                "TimeInDateTime,MsBetweenPresents,Application,ProcessID,SwapChainAddress,PresentMode\r\n2026-09-18T00:00:00Z,10,game.exe,4242,1,Hardware: Independent Flip\r\n2026-09-18T00:00:01Z,20,game.exe,4242,1,Hardware: Independent Flip\r\n");
        File.WriteAllText(Path.Combine(run, "hardware-samples.csv"),
            "Time,Fps\r\n2026-09-18T00:00:00Z," + first + "\r\n2026-09-18T00:00:01Z," + second + "\r\n" + tail);
        generate.Invoke(null, new object[] { run });
        string text = File.ReadAllText(Path.Combine(run, "charts", "framescope-interactive-data.js"));
        var data = Json.Deserialize<Dictionary<string, object>>(text.Substring(text.IndexOf('=') + 1).Trim().TrimEnd(';'));
        data["manifest"] = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(run, "charts", "framescope-interactive-manifest.json")));
        return data;
    }

    private static void CheckSampled(Dictionary<string, object> data, int count, double average)
    {
        var manifest = Map(data, "manifest");
        var stats = Map(data, "frameStats");
        var fps = Map(data, "fps");
        var samples = Map(data, "fpsSampleStats");
        Check(Convert.ToInt32(manifest["frames"]) == 0 && !Convert.ToBoolean(manifest["hasFrameData"]), "sample rows are not captured frames");
        Check(Convert.ToString(manifest["reportKind"]) == "diagnostic", "sampled-only report uses diagnostic classification");
        foreach (string key in new[] { "average", "low1", "low01", "minInstant", "maxInstant", "maxFrameMs", "framesOver20", "framesOver33", "framesOver100" })
            Check(Value(stats, key) == null, "no fabricated frame statistic: " + key);
        Check(Convert.ToInt32(Value(manifest, "fpsSamples")) == count && Convert.ToInt32(Value(Map(data, "counts"), "fpsSamples")) == count, "sample count is explicit and consistent");
        Check(Convert.ToDouble(Value(samples, "average")) == average, "sample average is arithmetic rather than harmonic");
        Check(Value(samples, "aggregation") as string == "arithmetic-mean-of-samples", "sample average has an explicit statistical definition");
        Check(Value(fps, "source") as string == "etw-fps-sampled", "trend identifies the sampled source");
        Check(List(fps, "avg").Count >= count && AllNull(List(fps, "low1")) && AllNull(List(fps, "low01")), "trend survives without fake low curves");
        Check(!Convert.ToBoolean(Map(data, "notes")["frameDataCaptured"]), "notes do not claim frame capture");
        Check(Convert.ToString(manifest["frameCaptureMessage"]).IndexOf("denied by anti-cheat", StringComparison.OrdinalIgnoreCase) < 0, "missing CSV does not prove anti-cheat denial");
    }

    private static bool AllNull(IList values) { foreach (object value in values) if (value != null) return false; return true; }
    private static object Value(Dictionary<string, object> map, string key) { object value; return map.TryGetValue(key, out value) ? value : null; }
    private static Dictionary<string, object> Map(Dictionary<string, object> map, string key) { return Value(map, key) as Dictionary<string, object> ?? new Dictionary<string, object>(); }
    private static IList List(Dictionary<string, object> map, string key) { return Value(map, key) as IList ?? new object[0]; }
    private static void Check(bool condition, string assertion)
    {
        Assertions.Add(new { assertion = assertion, passed = condition });
        Console.WriteLine((condition ? "PASS: " : "FAIL: ") + assertion);
        if (!condition) failures++;
    }
}
