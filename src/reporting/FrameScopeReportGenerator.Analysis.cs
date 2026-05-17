using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Web.Script.Serialization;

internal static partial class FrameScopeReportGenerator
{
    private static List<KeyValuePair<DateTime, double>> AlignPresentMonTime(List<KeyValuePair<DateTime, double>> frames, List<SystemRow> systemRows, out int timeShiftHours)
    {
        timeShiftHours = 0;
        if (frames.Count == 0 || systemRows.Count == 0) return frames;
        double hours = (systemRows[0].Time - frames[0].Key).TotalHours;
        int rounded = (int)Math.Round(hours);
        if (Math.Abs(hours) >= 1.0 && Math.Abs(hours - rounded) < 0.25 && Math.Abs(rounded) <= 14)
        {
            timeShiftHours = rounded;
            return frames.Select(f => new KeyValuePair<DateTime, double>(f.Key.AddHours(rounded), f.Value)).OrderBy(f => f.Key).ToList();
        }
        return frames;
    }

    private static Dictionary<string, object> BucketFps(List<KeyValuePair<DateTime, double>> frames, DateTime start, double bucketSeconds, double lowWindowSeconds)
    {
        Dictionary<string, object> empty = new Dictionary<string, object>
        {
            { "bucketMs", (int)Math.Round(bucketSeconds * 1000) },
            { "lowWindowMs", (int)Math.Round(lowWindowSeconds * 1000) },
            { "t", new List<double>() },
            { "avg", new List<double?>() },
            { "low1", new List<double?>() },
            { "low01", new List<double?>() },
            { "min", new List<double?>() }
        };
        if (frames.Count == 0) return empty;

        SortedDictionary<int, List<double>> buckets = new SortedDictionary<int, List<double>>();
        List<double> secs = new List<double>(frames.Count);
        List<double> msValues = new List<double>(frames.Count);
        foreach (KeyValuePair<DateTime, double> f in frames)
        {
            double sec = (f.Key - start).TotalSeconds;
            if (sec < 0) continue;
            int bucket = (int)Math.Floor(sec / bucketSeconds);
            List<double> list;
            if (!buckets.TryGetValue(bucket, out list))
            {
                list = new List<double>();
                buckets[bucket] = list;
            }
            list.Add(f.Value);
            secs.Add(sec);
            msValues.Add(f.Value);
        }

        List<double> times = new List<double>();
        List<double?> avg = new List<double?>();
        List<double?> low1 = new List<double?>();
        List<double?> low01 = new List<double?>();
        List<double?> min = new List<double?>();
        Fenwick fenwick = new Fenwick(100001);
        Queue<int> windowBins = new Queue<int>();
        Queue<double> windowSecs = new Queue<double>();
        int frameIndex = 0;

        foreach (KeyValuePair<int, List<double>> bucket in buckets)
        {
            double t = RoundDouble(bucket.Key * bucketSeconds, 3);
            double windowStart = t - lowWindowSeconds;
            while (frameIndex < secs.Count && secs[frameIndex] <= t + bucketSeconds)
            {
                int bin = MsToBin(msValues[frameIndex]);
                fenwick.Add(bin, 1);
                windowBins.Enqueue(bin);
                windowSecs.Enqueue(secs[frameIndex]);
                frameIndex++;
            }
            while (windowSecs.Count > 0 && windowSecs.Peek() < windowStart)
            {
                windowSecs.Dequeue();
                fenwick.Add(windowBins.Dequeue(), -1);
            }

            double meanMs = bucket.Value.Average();
            double maxMs = bucket.Value.Max();
            times.Add(t);
            avg.Add(RoundDouble(1000.0 / meanMs, 2));
            min.Add(RoundDouble(1000.0 / maxMs, 3));
            low1.Add(FpsFromFenwick(fenwick, 0.99));
            low01.Add(FpsFromFenwick(fenwick, 0.999));
        }

        empty["t"] = times;
        empty["avg"] = avg;
        empty["low1"] = low1;
        empty["low01"] = low01;
        empty["min"] = min;
        return empty;
    }

    private static int MsToBin(double ms)
    {
        int bin = (int)Math.Round(ms * 10.0);
        if (bin < 1) bin = 1;
        if (bin > 100000) bin = 100000;
        return bin;
    }

    private static double? FpsFromFenwick(Fenwick fenwick, double quantile)
    {
        if (fenwick.Count <= 0) return null;
        int rank = Math.Max(1, (int)Math.Ceiling(quantile * fenwick.Count));
        int bin = fenwick.FindByRank(rank);
        double ms = bin / 10.0;
        return ms > 0 ? RoundDouble(1000.0 / ms, 2) : (double?)null;
    }

    private static string FormatDuration(int seconds)
    {
        return (seconds / 60).ToString(CultureInfo.InvariantCulture) + "分" + (seconds % 60).ToString(CultureInfo.InvariantCulture) + "秒";
    }

    private static bool TryParseDate(string text, out DateTime value)
    {
        text = (text ?? "").Trim();
        value = DateTime.MinValue;
        if (string.IsNullOrEmpty(text) || text.Equals("NA", StringComparison.OrdinalIgnoreCase)) return false;
        DateTimeOffset dto;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dto))
        {
            value = dto.LocalDateTime;
            return true;
        }
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value)) return true;
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out value);
    }

    private static double? ParseNullableDouble(string text)
    {
        text = (text ?? "").Trim();
        if (string.IsNullOrEmpty(text) || text.Equals("NA", StringComparison.OrdinalIgnoreCase)) return null;
        double value;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return null;
        if (double.IsNaN(value) || double.IsInfinity(value)) return null;
        return value;
    }

    private static double? PercentileHigh(List<double> values, double quantile)
    {
        if (values == null || values.Count == 0) return null;
        List<double> sorted = new List<double>(values);
        sorted.Sort();
        int index = Math.Max(0, Math.Min(sorted.Count - 1, (int)Math.Ceiling(quantile * sorted.Count) - 1));
        return sorted[index];
    }

    private static double? LowFpsFromSlowFrames(List<double> frameMs, double fraction)
    {
        if (frameMs == null || frameMs.Count == 0) return null;
        int count = Math.Max(1, (int)Math.Ceiling(frameMs.Count * fraction));
        List<double> slowFrames = frameMs.OrderByDescending(v => v).Take(count).ToList();
        double averageMs = slowFrames.Average();
        return averageMs > 0 ? 1000.0 / averageMs : (double?)null;
    }

    private static double? AverageOrNull(List<double> values)
    {
        return values.Count > 0 ? values.Average() : (double?)null;
    }

    private static double? MaxOrNull(List<double> values)
    {
        return values.Count > 0 ? values.Max() : (double?)null;
    }

    private static object Round(double? value, int digits)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return null;
        return Math.Round(value.Value, digits, MidpointRounding.AwayFromZero);
    }

    private static double? RoundNullable(double? value, int digits)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return null;
        return Math.Round(value.Value, digits, MidpointRounding.AwayFromZero);
    }

    private static object Round(double value, int digits)
    {
        return Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }

    private static double RoundDouble(double value, int digits)
    {
        return Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }

    private static string Get(List<string> row, Dictionary<string, int> headers, string name)
    {
        int index;
        if (!headers.TryGetValue(name, out index) || index < 0 || index >= row.Count) return "";
        return row[index] ?? "";
    }

    private static bool ListHasValue(object value)
    {
        IEnumerable<double?> doubles = value as IEnumerable<double?>;
        if (doubles == null) return false;
        foreach (double? d in doubles) if (d.HasValue) return true;
        return false;
    }
}
