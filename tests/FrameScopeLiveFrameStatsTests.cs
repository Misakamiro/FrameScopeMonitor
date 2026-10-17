using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;

internal static class FrameScopeLiveFrameStatsTests
{
    private const string Header = "TimeInDateTime,MsBetweenPresents,Application,ProcessID,SwapChainAddress,PresentMode,AllowsTearing\n";
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static string Row(double ms, int pid, string chain, string mode) {
        return "2026-10-11T00:00:00.0000000+08:00," + ms.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",game.exe," + pid + "," + chain + "," + mode + ",1\n";
    }
    public static int Main(string[] args)
    {
        string dir = Path.Combine(Path.GetTempPath(), "FrameScopeLiveLowTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string csv = Path.Combine(dir, "presentmon.csv");
        try {
            var burst = new StringBuilder(Header);
            for (int index = 0; index < 20000; index++) burst.Append(Row(10, 42, "A", "Hardware: Independent Flip"));
            File.WriteAllText(csv, burst.ToString(), Encoding.UTF8);
            var burstReader = new FrameScopeLiveFrameStats(csv, true);
            var burstResult = burstReader.Poll(true);
            Check(burstResult.CaughtUp && burstResult.Count == 20000 && burstResult.Low1 == 100,
                "Final publication must drain its whole frontier, including a backlog larger than 1 MiB");
            string promoted = Path.Combine(dir, "promoted.csv");
            File.Move(csv, promoted);
            burstReader.Relocate(promoted);
            File.AppendAllText(promoted, Row(20, 42, "A", "Hardware: Independent Flip"));
            burstResult = burstReader.Poll(false);
            Check(burstResult.CaughtUp && burstResult.Count == 20001 && burstResult.Low1 == 100,
                "Promotion must retain the live window and consume only newly appended frames");
            File.WriteAllText(csv, "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents\n"
                + "game.exe,42,A,DXGI,0,0,0,2026-10-16 00:00:00.0000000,0.1,16\n", Encoding.UTF8);
            var legacy = new FrameScopeLiveFrameStats(csv).Poll(true);
            Check(legacy.Error == null && legacy.Count == 1 && legacy.Low1 == 62.5,
                "Legacy date_time header must retain real per-present intervals for live Low");
            var legacyReport = FrameScopeReportGenerator.ReadLiveFrameStatsForTests(csv);
            Check(Convert.ToInt32(legacyReport["count"]) == 1 && (double)legacyReport["low1"] == 62.5,
                "Legacy report and OSD must select the same true frame intervals");
            Check(legacy.Fps == 62.5, "FPS fallback must use actual per-present intervals");
            File.WriteAllText(csv, "Application,ProcessID,SwapChainAddress,TimeInSeconds,msBetweenPresents\n"
                + "game.exe,42,A,1,16\n", Encoding.UTF8);
            Check(new FrameScopeLiveFrameStats(csv).Poll(true).Count == 0
                && Convert.ToInt32(FrameScopeReportGenerator.ReadLiveFrameStatsForTests(csv)["count"]) == 0,
                "Numeric elapsed seconds must not be fabricated into a calendar date");
            File.WriteAllText(csv, Header + Row(10, 42, "A", "Composed: Flip"), Encoding.UTF8);
            bool appended = false;
            var growing = new FrameScopeLiveFrameStats(csv, true, () => {
                if (!appended) {
                    appended = true;
                    var tail = new StringBuilder();
                    for (int index = 0; index < 20000; index++) tail.Append(Row(10, 42, "A", "Composed: Flip"));
                    File.AppendAllText(csv, tail.ToString());
                }
                return Stopwatch.GetTimestamp();
            });
            var growingResult = growing.Poll(false);
            Check(growingResult.CaughtUp && growingResult.Count == 1 && growingResult.Low1 == 100,
                "Appending during a poll must not turn the already-read publication frontier into unavailable Low");
            var streaming = FrameScopeCapturePlanner.CreatePresentMonPlan(new List<string> { "game" }, "game.exe", "Game", 42, csv, "test", true, 10, true);
            Check(streaming.Arguments.Contains("--output_stdout") && !streaming.Arguments.Contains("--output_file"), "Realtime capture must use the same PresentMon stdout stream without its exclusive CSV writer");
            File.WriteAllText(csv, Header + Row(10, 42, "A", "Hardware: Independent Flip"), Encoding.UTF8);
            var reader = new FrameScopeLiveFrameStats(csv);
            var first = reader.Poll(false);
            Check(first.Count == 1 && first.Low1 == 100 && first.Low01 == 100, "First valid frame must provide cumulative Low without warmup");
            using (var sharedWriter = new FileStream(csv, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) {
                byte[] row = Encoding.UTF8.GetBytes(Row(20, 42, "A", "Hardware: Independent Flip"));
                sharedWriter.Write(row, 0, row.Length); sharedWriter.Flush();
                Check(reader.Poll(false).Count == 2, "Frames must be readable while the worker's CSV writer stays open");
            }
            File.WriteAllText(csv, Header + Row(10, 42, "A", "Hardware: Independent Flip"), Encoding.UTF8);
            reader = new FrameScopeLiveFrameStats(csv); reader.Poll(false);
            reader.Poll(false);
            Check(reader.Poll(false).Count == 1, "Polling must not duplicate frames");
            string slow = Row(50, 42, "A", "Hardware: Independent Flip");
            File.AppendAllText(csv, slow.Substring(0, slow.Length - 4));
            Check(reader.Poll(false).Count == 1, "Partial row must not count");
            File.AppendAllText(csv, slow.Substring(slow.Length - 4));
            var second = reader.Poll(false);
            Check(second.Count == 2 && second.Low1 == 20 && second.Low01 == 20, "Completed row must immediately affect Low");
            var expected = FrameScopeReportGenerator.ReadLiveFrameStatsForTests(csv);
            Check(Math.Abs(second.Low1.Value - (double)expected["low1"]) < 1e-9, "Live 1% Low must match report");
            File.AppendAllText(csv, Row(1, 42, "B", "Composed: Flip") + Row(2000, 42, "A", "Hardware: Independent Flip"));
            var multi = reader.Poll(false);
            expected = FrameScopeReportGenerator.ReadLiveFrameStatsForTests(csv);
            Check(multi.Count == (int)expected["count"], "Multi-track filtering must match report");
            Check(Math.Abs(multi.Low01.Value - (double)expected["low01"]) < 1e-9, "Live 0.1% Low must match report");
            File.WriteAllText(csv, Header + Row(20, 99, "C", "Composed: Flip"), Encoding.UTF8);
            var reset = reader.Poll(false);
            Check(reset.Count == 1 && reset.ProcessId == 99 && reset.Low1 == 50, "Truncated CSV must reset previous run data");
            var content = new StringBuilder(Header);
            for (int i = 0; i < 12000; i++) content.Append(Row(i % 37 == 0 ? 1000.0 / 59.995 : 8.333333333333333, 101, "D", "Hardware: Independent Flip"));
            File.WriteAllText(csv, content.ToString(), Encoding.UTF8);
            reader = new FrameScopeLiveFrameStats(csv);
            FrameScopeLiveFrameStats.Snapshot longRun;
            do { longRun = reader.Poll(false); } while (!longRun.CaughtUp && longRun.Error == null);
            expected = FrameScopeReportGenerator.ReadLiveFrameStatsForTests(csv);
            Check(longRun.Low1.Value == (double)expected["low1"] && longRun.Low01.Value == (double)expected["low01"], "Floating point accumulation must match report exactly");
            if (args.Length > 0) {
                reader = new FrameScopeLiveFrameStats(args[0]);
                do { longRun = reader.Poll(false); } while (!longRun.CaughtUp && longRun.Error == null);
                Check(longRun.Error == null, "Historical recording must stay available: " + longRun.Error);
                expected = FrameScopeReportGenerator.ReadLiveFrameStatsForTests(args[0]);
                Check(longRun.Count == (int)expected["count"], "Historical recording frame selection mismatch");
                Check(longRun.Low1 == (double?)expected["low1"] && longRun.Low01 == (double?)expected["low01"], "Historical recording Low mismatch");
                Console.WriteLine("PARITY: " + longRun.Count + " frames; Low " + longRun.Low1 + " / " + longRun.Low01);
                reader = new FrameScopeLiveFrameStats(args[0], true);
                do { longRun = reader.Poll(false); } while (!longRun.CaughtUp && longRun.Error == null);
                expected = FrameScopeReportGenerator.ReadRollingFrameStatsForTests(args[0]);
                Check(longRun.Error == null && longRun.Count == Convert.ToInt64(expected["count"]), "Historical rolling window frame selection mismatch");
                Check(Math.Round(longRun.Low1.Value, 2, MidpointRounding.AwayFromZero) == (double)expected["low1"]
                    && Math.Round(longRun.Low01.Value, 2, MidpointRounding.AwayFromZero) == (double)expected["low01"], "Historical rolling Low must match the report curve's Fenwick percentile calculation");
                Console.WriteLine("ROLLING PARITY: " + longRun.Count + " recent frames; Low " + longRun.Low1 + " / " + longRun.Low01);
            }
            File.WriteAllText(csv, Header + Row(200, 42, "A", "Composed: Flip"), Encoding.UTF8);
            var rolling = new FrameScopeLiveFrameStats(csv, true);
            Check(rolling.Poll(false).Low01 == 5, "Rolling Low must be available from the first frame");
            var recent = new StringBuilder();
            for (int i = 0; i < 1000; i++) recent.Append(Row(i == 0 ? 100 : i < 11 ? 10 : 5, 42, "A", "Composed: Flip").Replace("00:00:00.0000000", "00:00:04.0000000"));
            File.AppendAllText(csv, recent.ToString());
            var rollingResult = rolling.Poll(false);
            Check(rollingResult.Count == 1000 && rollingResult.Low1 == 100 && rollingResult.Low01 == 100,
                "Rolling Low must expire old stalls and use percentile thresholds, not tail averages");
            var longRolling = new StringBuilder(Header);
            for (int i = 0; i < 70000; i++) {
                string timestamp = new DateTimeOffset(2026,10,11,0,0,0,TimeSpan.FromHours(8)).AddMilliseconds(i).ToString("o");
                longRolling.Append(Row(5.000001 + i * .000001,42,"A","Composed: Flip").Replace("2026-10-11T00:00:00.0000000+08:00",timestamp));
            }
            File.WriteAllText(csv,longRolling.ToString(),Encoding.UTF8);
            rolling = new FrameScopeLiveFrameStats(csv,true);
            do { rollingResult=rolling.Poll(false); } while(!rollingResult.CaughtUp && rollingResult.Error==null);
            Check(rollingResult.Error==null && rollingResult.Count<=3001 && rollingResult.Low1.HasValue,
                "Long rolling captures must not retain cumulative histogram buckets or exceed their capacity");
            File.WriteAllText(csv, Header + Row(200, 42, "A", "Composed: Flip"), Encoding.UTF8);
            long ticks = 0;
            var clocked = new FrameScopeLiveFrameStats(csv, true, () => ticks);
            clocked.Poll(false);
            ticks = (long)(2.999 * Stopwatch.Frequency);
            Check(clocked.Poll(false).Low1.HasValue, "Rolling value remains valid before 3 seconds of monotonic silence");
            clocked.Publish(dir, 123, "capturing", false);
            var livePayload = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                File.ReadAllText(Path.Combine(dir, "live-frame-stats.json")));
            Check(livePayload.ContainsKey("source_age_ms") && Convert.ToInt64(livePayload["source_age_ms"]) >= 2998,
                "Publication heartbeat must expose age of last actual frame without renewing it");
            ticks = 3 * Stopwatch.Frequency;
            Check(!clocked.Poll(false).Low1.HasValue && !clocked.Poll(false).Low01.HasValue, "Rolling values expire after 3 seconds without a newly accepted valid frame");
            Check(!clocked.Poll(false).Fps.HasValue, "Stale per-frame FPS must expire with Low");
            File.AppendAllText(csv, Row(2000, 42, "A", "Composed: Flip"));
            Check(!clocked.Poll(false).Low1.HasValue, "A frame excluded from Low statistics cannot renew or revive old Low values");
            File.AppendAllText(csv,Row(10,42,"B","Composed: Flip").Replace("00:00:00.0000000","00:01:20.0000000"));
            rollingResult=rolling.Poll(false);
            Check(rollingResult.Track.Contains("|B|") && rollingResult.Count==1 && rollingResult.Low1==100,
                "Rolling Low must follow the active swap chain after a fullscreen or resolution switch");
            Console.WriteLine("PASS: first frame, incremental rows, partial rows, report parity, track filtering, truncation reset, rolling expiry and percentile thresholds");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message); return 1;
        } finally { Directory.Delete(dir, true); }
    }
}
