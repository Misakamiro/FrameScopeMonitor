using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public sealed class FrameScopeLiveFrameStats
{
    public sealed class Snapshot {
        public long Count;
        public int ProcessId;
        public string Track;
        public string Error;
        public bool CaughtUp;
        public double? Low1;
        public double? Low01;
        public double? Fps;
    }
    private sealed class TrackData {
        public string Key;
        public long Rows;
        public long HardwareRows;
        public readonly SortedDictionary<double, long> All = new SortedDictionary<double, long>();
        public readonly SortedDictionary<double, long> Hardware = new SortedDictionary<double, long>();
        public readonly Queue<Tuple<DateTimeOffset, double, bool>> Recent = new Queue<Tuple<DateTimeOffset, double, bool>>();
        public readonly Queue<double> Instant = new Queue<double>();
        public DateTimeOffset Latest;
    }
    private string path;
    private readonly bool rolling;
    private readonly Dictionary<string, TrackData> tracks = new Dictionary<string, TrackData>();
    private readonly StringBuilder pending = new StringBuilder();
    private Decoder decoder = Encoding.UTF8.GetDecoder();
    private Dictionary<string, int> headers;
    private long offset;
    private int buckets;
    private string error;
    private bool caughtUp;
    private DateTimeOffset latestFrame;
    private readonly Func<long> monotonicClock;
    private long lastNewValidFrameAt;
    private bool hasNewValidFrameAt;
    private const int MaxBuckets = 65536;
    public bool HasFrames { get { return tracks.Count > 0; } }
    public long? SourceAgeMilliseconds { get { return hasNewValidFrameAt && caughtUp ? (long?)ElapsedMilliseconds(lastNewValidFrameAt, monotonicClock()) : null; } }
    public FrameScopeLiveFrameStats(string csvPath, bool rollingWindow = false, Func<long> clock = null) {
        path = csvPath; rolling = rollingWindow;
        monotonicClock = clock ?? (() => System.Diagnostics.Stopwatch.GetTimestamp());
        lastNewValidFrameAt = 0;
        hasNewValidFrameAt = false;
    }

    // Only used after the owner renames the same append-only capture file.
    // Preserve its cursor, partial row and live window across that rename.
    public void Relocate(string csvPath) { path = csvPath; }

    public static double TrackScore(long hardwareRows, long rows, double? medianMs, double? p99Ms) {
        double score = hardwareRows * 3.0 + rows;
        if (medianMs.HasValue && medianMs.Value > 0) score += Math.Min(240.0, 1000.0 / medianMs.Value) * 20.0;
        if (p99Ms.HasValue) score -= Math.Min(1000.0, p99Ms.Value) * 2.0;
        return score;
    }
    public static int CompareTracks(double aScore, string aKey, double bScore, string bKey) {
        int order = bScore.CompareTo(aScore);
        return order != 0 ? order : string.CompareOrdinal(aKey, bKey);
    }
    public static double? LowFromSorted(List<double> sorted, double fraction) {
        if (sorted == null || sorted.Count == 0) return null;
        int count = Math.Max(1, (int)Math.Ceiling(sorted.Count * fraction));
        double sum = 0;
        for (int i = sorted.Count - 1, taken = 0; i >= 0 && taken < count; i--, taken++) sum += sorted[i];
        return sum > 0 ? (double?)(1000.0 / (sum / count)) : null;
    }
    private static double? Rank(SortedDictionary<double, long> values, long count, double fraction) {
        long rank = Math.Max(1, (long)Math.Ceiling(count * fraction));
        long seen = 0;
        foreach (var entry in values) { seen += entry.Value; if (seen >= rank) return entry.Key; }
        return null;
    }
    private static long ValidCount(SortedDictionary<double, long> values) {
        long count = 0;
        foreach (var entry in values) { if (entry.Key > 1000) break; count += entry.Value; }
        return count;
    }
    private static double? Low(SortedDictionary<double, long> values, long count, double fraction) {
        if (count == 0) return null;
        long tail = Math.Max(1, (long)Math.Ceiling(count * fraction));
        long remaining = tail;
        var sorted = new List<KeyValuePair<double, long>>();
        foreach (var entry in values) { if (entry.Key > 1000) break; sorted.Add(entry); }
        double sum = 0;
        // Match the report's descending, per-frame floating point sum exactly.
        for (int index = sorted.Count - 1; index >= 0 && remaining > 0; index--) {
            long take = Math.Min(remaining, sorted[index].Value);
            for (long frame = 0; frame < take; frame++) sum += sorted[index].Key;
            remaining -= take;
        }
        return sum > 0 ? (double?)(1000.0 / (sum / tail)) : null;
    }
    private void Add(SortedDictionary<double, long> values, double ms) {
        long count;
        if (values.TryGetValue(ms, out count)) values[ms] = count + 1;
        else if (buckets < MaxBuckets) { values.Add(ms, 1); buckets++; }
        else error = "frame-statistics-capacity-exceeded";
    }
    private static List<string> ParseLine(string line) {
        var fields = new List<string>();
        var text = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++) {
            char ch = line[i];
            if (ch == '"') {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { text.Append('"'); i++; }
                else quoted = !quoted;
            } else if (ch == ',' && !quoted) { fields.Add(text.ToString()); text.Length = 0; }
            else text.Append(ch);
        }
        fields.Add(text.ToString());
        return fields;
    }
    private string Field(List<string> row, string name) {
        int index;
        return headers.TryGetValue(name, out index) && index < row.Count ? row[index].Trim() : "";
    }
    private void Consume(string line) {
        var row = ParseLine(line.TrimEnd('\r'));
        if (headers == null) {
            headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < row.Count; i++) headers[row[i].TrimStart('\uFEFF')] = i;
            // PresentMon v1 keeps this name even with --date_time enabled.
            int legacyTime;
            if (!headers.ContainsKey("TimeInDateTime") && headers.TryGetValue("TimeInSeconds", out legacyTime))
                headers["TimeInDateTime"] = legacyTime;
            foreach (string name in new[] { "TimeInDateTime", "MsBetweenPresents", "ProcessID", "SwapChainAddress", "Application" })
                if (!headers.ContainsKey(name)) error = "missing-presentmon-column:" + name;
            return;
        }
        double ms;
        DateTimeOffset dto;
        DateTime time;
        string date = Field(row, "TimeInDateTime");
        bool validDate = DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dto)
            || DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time)
            || DateTime.TryParse(date, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out time);
        if (!validDate || !double.TryParse(Field(row, "MsBetweenPresents"), NumberStyles.Float, CultureInfo.InvariantCulture, out ms)
            || double.IsNaN(ms) || double.IsInfinity(ms) || ms <= 0 || ms >= 10000) return;
        string key = Field(row, "ProcessID") + "|" + Field(row, "SwapChainAddress") + "|" + Field(row, "Application");
        DateTimeOffset timestamp = dto;
        if (rolling) {
            if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out timestamp))
                timestamp = new DateTimeOffset(DateTime.Parse(date, CultureInfo.CurrentCulture));
            if (timestamp > latestFrame) latestFrame = timestamp;
            TrimRollingTracks();
        }
        TrackData track;
        if (!tracks.TryGetValue(key, out track)) {
            if (tracks.Count >= 32) { error = "frame-track-capacity-exceeded"; return; }
            track = new TrackData { Key = key }; tracks.Add(key, track);
        }
        bool hardware = Field(row, "PresentMode").StartsWith("Hardware:", StringComparison.OrdinalIgnoreCase);
        track.Instant.Enqueue(ms);
        while (track.Instant.Count > 60) track.Instant.Dequeue();
        if (ms <= 1000) {
            lastNewValidFrameAt = monotonicClock();
            hasNewValidFrameAt = true;
        }
        if (rolling) {
            if (timestamp > track.Latest) track.Latest = timestamp;
            track.Recent.Enqueue(Tuple.Create(timestamp, ms, hardware));
        } else {
            track.Rows++; Add(track.All, ms);
            if (hardware) { track.HardwareRows++; Add(track.Hardware, ms); }
        }
    }
    private void TrimRollingTracks() {
        if (latestFrame == DateTimeOffset.MinValue) return;
        var expired = new List<string>();
        foreach (var track in tracks.Values) {
            while (track.Recent.Count > 0 && track.Recent.Peek().Item1 < latestFrame.AddSeconds(-3)) track.Recent.Dequeue();
            if (track.Recent.Count == 0) expired.Add(track.Key);
        }
        foreach (string key in expired) tracks.Remove(key);
    }
    private void Reset() {
        tracks.Clear(); pending.Length = 0; headers = null; offset = 0; buckets = 0; error = null;
        latestFrame = DateTimeOffset.MinValue;
        lastNewValidFrameAt = 0;
        hasNewValidFrameAt = false;
        decoder = Encoding.UTF8.GetDecoder();
    }
    public Snapshot Poll(bool final) {
        ReadAvailable(final);
        return Current();
    }
    public void ReadAvailable(bool final = false) {
        caughtUp = false;
        if (!File.Exists(path)) return;
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
            // Drain the frontier observed at entry. The writer may append again
            // after EOF; that is input for the next poll, not a failed current poll.
            long frontier = file.Length;
            if (frontier < offset) Reset();
            file.Position = offset;
            byte[] bytes = new byte[8192];
            char[] chars = new char[8193];
            var work = System.Diagnostics.Stopwatch.StartNew();
            // Bound foreground work by time rather than a byte rate below a
            // valid high-FPS stream. Completion drains its frozen frontier.
            while (offset < frontier && (final || work.ElapsedMilliseconds < 50) && error == null) {
                int read = file.Read(bytes, 0, (int)Math.Min(bytes.Length, frontier - offset));
                if (read == 0) break;
                offset += read;
                int length = decoder.GetChars(bytes, 0, read, chars, 0, false);
                for (int i = 0; i < length && error == null; i++) {
                    if (chars[i] == '\n') { Consume(pending.ToString()); pending.Length = 0; }
                    else pending.Append(chars[i]);
                    if (pending.Length > 65536) error = "presentmon-row-too-large";
                }
            }
            caughtUp = offset == frontier;
            if (final && caughtUp && pending.Length > 0 && error == null) { Consume(pending.ToString()); pending.Length = 0; }
        }
    }
    private Snapshot Current() {
        var result = new Snapshot { Error = error, CaughtUp = caughtUp };
        if (error != null || !caughtUp) return result;
        if (rolling) return RollingCurrent(result);
        TrackData selected = null;
        double selectedScore = 0;
        foreach (var track in tracks.Values) {
            var scoring = track.HardwareRows > 0 ? track.Hardware : track.All;
            long count = track.HardwareRows > 0 ? track.HardwareRows : track.Rows;
            double score = TrackScore(track.HardwareRows, track.Rows, Rank(scoring, count, .5), Rank(scoring, count, .99));
            if (selected == null || CompareTracks(score, track.Key, selectedScore, selected.Key) < 0) { selected = track; selectedScore = score; }
        }
        if (selected == null) return result;
        var values = tracks.Count > 1 && selected.HardwareRows > 0 ? selected.Hardware : selected.All;
        result.Count = ValidCount(values);
        int.TryParse(selected.Key.Split('|')[0], out result.ProcessId);
        result.Track = selected.Key;
        result.Low1 = Low(values, result.Count, .01);
        result.Low01 = Low(values, result.Count, .001);
        result.Fps = InstantFps(selected);
        return result;
    }
    private Snapshot RollingCurrent(Snapshot result) {
        if (!hasNewValidFrameAt || ElapsedMilliseconds(lastNewValidFrameAt, monotonicClock()) >= 3000) return result;
        TrimRollingTracks();
        TrackData selected = null;
        List<double> selectedValues = null;
        double selectedScore = 0;
        foreach (var track in tracks.Values) {
            var all = new List<double>();
            var hardware = new List<double>();
            foreach (var frame in track.Recent) { all.Add(frame.Item2); if (frame.Item3) hardware.Add(frame.Item2); }
            var scoring = hardware.Count > 0 ? hardware : all;
            scoring.Sort();
            double score = TrackScore(hardware.Count, all.Count, scoring[(int)Math.Ceiling(scoring.Count*.5)-1], scoring[(int)Math.Ceiling(scoring.Count*.99)-1]);
            if (selected == null || CompareTracks(score, track.Key, selectedScore, selected.Key) < 0) {
                selected = track; selectedScore = score;
                selectedValues = tracks.Count > 1 && hardware.Count > 0 ? hardware : all;
            }
        }
        if (selected == null) return result;
        selectedValues.RemoveAll(ms => ms > 1000);
        selectedValues.Sort();
        result.Count = selectedValues.Count;
        result.Track = selected.Key;
        int.TryParse(selected.Key.Split('|')[0], out result.ProcessId);
        result.Low1 = PercentileLow(selectedValues,.99);
        result.Low01 = PercentileLow(selectedValues,.999);
        result.Fps = InstantFps(selected);
        return result;
    }
    private static double? InstantFps(TrackData track) {
        double sum = 0;
        foreach (double ms in track.Instant) sum += ms;
        return sum > 0 ? (double?)(1000.0 * track.Instant.Count / sum) : null;
    }
    private static long ElapsedMilliseconds(long started, long now) {
        if (now <= started) return 0;
        double elapsed = (now - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        return elapsed >= long.MaxValue ? long.MaxValue : (long)elapsed;
    }
    private static double? PercentileLow(List<double> sorted, double quantile) {
        if (sorted.Count == 0) return null;
        int index = Math.Max(0, (int)Math.Ceiling(sorted.Count * quantile) - 1);
        // The report curve quantizes frame times to 0.1 ms before selecting the percentile.
        double ms = Math.Max(1, Math.Min(100000, Math.Round(sorted[index] * 10.0))) / 10.0;
        return 1000.0 / ms;
    }
    public void Publish(string runDir, int workerPid, string phase, bool final) {
        Snapshot snapshot;
        try { snapshot = Poll(final); }
        catch (IOException exception) { snapshot = new Snapshot { Error = exception.Message }; }
        catch (UnauthorizedAccessException exception) { snapshot = new Snapshot { Error = exception.Message }; }
        bool available = snapshot.CaughtUp && snapshot.Error == null && snapshot.Count > 0 && snapshot.ProcessId > 0
            && (phase == "capturing" || phase == "completed");
        var value = new Dictionary<string, object> {
            { "source", "presentmon" }, { "phase", phase }, { "run_dir", Path.GetFullPath(runDir) },
            { "statistics_scope", rolling ? "rolling-percentile" : "cumulative-tail-mean" },
            { "window_ms", rolling ? 3000 : 0 },
            { "worker_pid", workerPid }, { "target_pid", snapshot.ProcessId },
            { "worker_start_time_unix", (long)(System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds },
            { "timestamp_ms", (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds },
            { "source_age_ms", SourceAgeMilliseconds },
            { "frame_count", snapshot.Count }, { "selected_track", snapshot.Track },
            { "error", snapshot.Error }, { "caught_up", snapshot.CaughtUp },
            { "fps_1low", available ? Rounded(snapshot.Low1) : null },
            { "fps", available ? Rounded(snapshot.Fps) : null },
            { "fps_01low", available ? Rounded(snapshot.Low01) : null }
        };
        try { FrameScopeJsonFile.WriteTransient(Path.Combine(runDir, "live-frame-stats.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value)); }
        catch (IOException exception) { System.Diagnostics.Trace.WriteLine("Live frame statistics write failed: " + exception.Message); }
    }
    private static object Rounded(double? value) {
        return value.HasValue && !double.IsInfinity(value.Value) && !double.IsNaN(value.Value)
            ? (object)Math.Round(value.Value, 2, MidpointRounding.AwayFromZero) : null;
    }
}
