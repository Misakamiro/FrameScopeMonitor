using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

internal static class FrameScopeStatisticsV2Tests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static double? Number(object value) { return value == null ? (double?)null : Convert.ToDouble(value); }
    private static object[] Array(object value) { var result=new List<object>(); foreach(object item in (IEnumerable)value)result.Add(item);return result.ToArray(); }
    public static int Main(string[] args) {
        try { return Run(args); }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); return 1; }
    }
    private static int Run(string[] args) {
        string fixture=args.Length>0?args[0]:Path.Combine(FrameScopeTestPaths.SourceRoot,"tests","fixtures","frame-statistics-v2.json");
        var data = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(fixture));
        foreach (Dictionary<string,object> item in Array(data["snapshots"])) {
            var ms = new List<double>(); foreach (object value in Array(item["ms"])) ms.Add(Convert.ToDouble(value));
            var expected = Array(item["expected"]); var sorted=new List<double>(ms);sorted.Sort();
            Check(FrameScopeLiveFrameStats.LowFromSorted(sorted, .01) == Number(expected[1]), "floor/round 1%: " + item["name"]);
#if !INITIAL_RED
            var actual = FrameScopeFrameStatistics.Snapshot(ms);
            Check(actual.Average == Number(expected[0]) && actual.Low1 == Number(expected[1]) && actual.Low01 == Number(expected[2]), "snapshot: " + item["name"]);
#endif
        }
#if !INITIAL_RED
        foreach (Dictionary<string,object> item in Array(data["filters"])) {
            var samples = new List<FrameScopeFrameStatistics.Sample>(); int index = 0;
            foreach (object value in Array(item["fps"])) samples.Add(new FrameScopeFrameStatistics.Sample { TimeMs=index++*1000, Count=1, Average=Convert.ToDouble(value), Low1=1, Low01=1 });
            var actual = FrameScopeFrameStatistics.Summarize(samples); var bounds=Array(item["expected_bounds"]);
            Check(actual.MinimumFps==Number(bounds[0]) && actual.MaximumFps==Number(bounds[1]), "bounds: "+item["name"]);
            Check(actual.RetainedSamples==Convert.ToInt32(item["retained"]), "retained: "+item["name"]);
            Check(actual.Raw.Average==Number(item["raw_average"]) && actual.Filtered.Average==Number(item["filtered_average"]), "means: "+item["name"]);
        }
        foreach (Dictionary<string,object> item in Array(data["replays"])) {
            var frames=new List<FrameScopeFrameStatistics.Frame>();
            foreach (object raw in Array(item["frames"])) {var row=Array(raw);frames.Add(new FrameScopeFrameStatistics.Frame(Convert.ToDouble(row[0]),Convert.ToDouble(row[1])));}
            var actual=FrameScopeFrameStatistics.Replay(frames); var times=Array(item["times"]);var averages=Array(item["averages"]);
            Check(actual.Samples.Count==times.Length,"replay count: "+item["name"]);
            for(int i=0;i<times.Length;i++) Check(actual.Samples[i].TimeMs==Number(times[i]) && actual.Samples[i].Average==Number(averages[i]),"replay sample: "+item["name"]);
        }
#endif
        Console.WriteLine("PASS statistics-v2 shared fixture: " + checks + " checks"); return 0;
    }
}
