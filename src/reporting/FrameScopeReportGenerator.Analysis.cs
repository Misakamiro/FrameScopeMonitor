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
    private sealed class ReportFpsWindow {
        public FrameScopeFrameStatistics.Sample Sample;
        public string Source, Reason;
        public double RealStart, RealEnd;
        public long Generation;
    }

    private static Dictionary<string,object> BuildStatisticsV3(List<KeyValuePair<DateTime,double>> frames,List<SystemRow> rows,DateTime start,double duration,long targetPid)
    {
        // PM owns each observed interval. Aggregated ETW windows cannot be split without raw frames.
        var slots=new SortedDictionary<long,ReportFpsWindow>();
        var rejected=new Dictionary<string,int>();
        Action<string> reject=reason=>{if(!rejected.ContainsKey(reason))rejected[reason]=0;rejected[reason]++;};
        int left=0,right=0;double previousBoundary=0;
        double end=Math.Max(0,Math.Min(FrameScopeFrameStatistics.MaxTimeMs/1000,duration));
        for(double boundary=Math.Min(1,end);boundary<=end;boundary=Math.Min(boundary+1,end)) {
            while(right<frames.Count&&(frames[right].Key-start).TotalSeconds<=boundary)right++;
            while(left<right&&(frames[left].Key-start).TotalSeconds<0)left++;
            while(left<right&&(frames[left].Key-start).TotalSeconds<=previousBoundary)left++;
            var ms=new List<double>();for(int i=left;i<right;i++)ms.Add(frames[i].Value);
            var sample=FrameScopeFrameStatistics.Snapshot(ms);sample.TimeMs=boundary*1000;
            slots[(long)Math.Ceiling(boundary)]=new ReportFpsWindow { Sample=sample,Source="presentmon",RealStart=previousBoundary,RealEnd=boundary,Reason="no-frames" };
            previousBoundary=boundary;
            if(boundary>=end)break;
        }
        var seen=new HashSet<string>();
        var occupied=slots.Values.Where(w=>FrameScopeFrameStatistics.Valid(w.Sample)).OrderBy(w=>w.RealStart).ToList();
        foreach(var row in rows.OrderBy(r=>r.Time)) {
            if(!ValidFpsRow(row)){reject(row.FpsIdentityRecorded?"invalid-stale-or-target-mismatch":"nonpositive-or-missing");continue;}
            if(row.FpsIdentityRecorded && targetPid>0 && row.RecordingTargetPid!=targetPid){reject("target-mismatch");continue;}
            // Old unverified FPS-only CSV is usable only when no PM frames exist.
            if(!row.FpsIdentityRecorded && frames.Count>0){reject("legacy-unverified");continue;}
            double realEnd=row.FpsIdentityRecorded
                ? (new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(row.FpsWindowEndMs)-start.ToUniversalTime()).TotalSeconds
                : (row.Time-start).TotalSeconds;
            if(realEnd<0||realEnd>end+0.5){reject("outside-recording");continue;}
            double realStart=row.FpsIdentityRecorded
                ? (new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(row.FpsWindowStartMs)-start.ToUniversalTime()).TotalSeconds
                : realEnd-1;
            realStart=Math.Max(0,realStart);realEnd=Math.Min(end,realEnd);
            if(realEnd<=realStart){reject("outside-recording");continue;}
            string identity=row.FpsTargetPid+":"+row.FpsTargetStartMs+":"+row.FpsGeneration+":"+row.FpsWindowEndMs;
            if(row.FpsIdentityRecorded&&!seen.Add(identity)){reject("duplicate-window");continue;}
            long slot=Math.Max(1,(long)Math.Ceiling(realEnd));ReportFpsWindow previous;
            if(slots.TryGetValue(slot,out previous)&&FrameScopeFrameStatistics.Valid(previous.Sample)){reject("covered-window");continue;}
            int low=0,high=occupied.Count;
            while(low<high){int middle=low+(high-low)/2;if(occupied[middle].RealEnd<=realStart+0.000001)low=middle+1;else high=middle;}
            if(low<occupied.Count&&occupied[low].RealStart<realEnd-0.000001){reject("overlapping-window");continue;}
            var sample=new FrameScopeFrameStatistics.Sample { TimeMs=Math.Min(end,realEnd)*1000,
                Count=row.FpsIdentityRecorded?(int)Math.Min(int.MaxValue,row.FpsFrameCount):1,Average=row.Fps,
                Low1=row.FpsIdentityRecorded?row.Fps1Low:null,Low01=row.FpsIdentityRecorded?row.Fps01Low:null };
            var window=new ReportFpsWindow { Sample=sample, Source=row.FpsIdentityRecorded?row.FpsSource:"legacy-etw-fps-unverified",
                RealStart=realStart,RealEnd=realEnd,Generation=row.FpsGeneration };
            slots[slot]=window;occupied.Insert(low,window);
        }
        var windows=slots.Values.OrderBy(w=>w.RealEnd).ToList();
        var statistics=FrameScopeFrameStatistics.Summarize(windows.Select(w=>w.Sample).ToList());
        var times=new List<double>();var realTimes=new List<double>();var averages=new List<double?>();
        var low1=new List<double?>();var low01=new List<double?>();var counts=new List<int>();var included=new List<bool>();
        var sources=new List<string>();var segments=new List<int>();var map=new List<Dictionary<string,object>>();
        double effective=0,lastRealEnd=-1;string lastSource=null;long lastGeneration=-1;int segment=0;
        for(int i=0;i<windows.Count;i++) {
            var w=windows[i];if(!FrameScopeFrameStatistics.Valid(w.Sample)){reject(w.Reason??"nonpositive-or-missing");continue;}
            if(lastSource!=w.Source||lastGeneration!=w.Generation||w.RealStart>lastRealEnd+0.001)segment++;
            // Union of observed window intervals, retaining the original timestamp for hover and cross-tab selection.
            double begin=Math.Max(w.RealStart,lastRealEnd),length=Math.Max(0,w.RealEnd-begin);
            map.Add(new Dictionary<string,object>{{"effectiveStart",effective},{"effectiveEnd",effective+length},{"realStart",begin},{"realEnd",w.RealEnd},{"segment",segment}});
            effective+=length;times.Add(effective);realTimes.Add(w.RealEnd);
            averages.Add(w.Sample.Average);low1.Add(w.Sample.Low1);low01.Add(w.Sample.Low01);counts.Add(w.Sample.Count);included.Add(statistics.Included[i]);
            sources.Add(w.Source);segments.Add(segment);lastSource=w.Source;lastGeneration=w.Generation;lastRealEnd=w.RealEnd;
        }
        var uniqueSources=sources.Distinct().ToArray();
        var exportWindows=windows.Where(w=>FrameScopeFrameStatistics.Valid(w.Sample)&&(w.Source=="etw-dxgi-present"||w.Source=="etw-kernel-estimate"))
            .Select(w=>new Dictionary<string,object>{{"start_ms",w.RealStart*1000},{"end_ms",w.RealEnd*1000},
                {"fps",w.Sample.Average},{"low1",w.Sample.Low1},{"low01",w.Sample.Low01},{"frame_count",w.Sample.Count},
                {"source",w.Source},{"valid",true}}).ToList();
        return new Dictionary<string,object> {
            {"recordingStartUnixMs",(start.ToUniversalTime()-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds},
            {"etwWindows",exportWindows},
            {"statisticsVersion",3},{"profile","framescope-valid-windows-v3"},{"bucketMs",1000},{"lowWindowMs",1000},{"updateIntervalMs",100},
            {"t",times},{"realT",realTimes},{"timeMap",map},{"segments",segments},{"sources",sources},{"source",uniqueSources.Length==1?uniqueSources[0]:uniqueSources.Length==0?"none":"mixed"},
            {"avg",averages},{"low1",low1},{"low01",low01},{"samples",counts},{"included",included},
            {"effectiveDurationSeconds",effective},{"recordedDurationSeconds",duration},{"excludedReasons",rejected},
            {"rawSummary",StatisticsSummary(statistics,false)},{"filteredSummary",StatisticsSummary(statistics,true)},
            {"filter",new Dictionary<string,object>{{"minimum",statistics.MinimumFps},{"maximum",statistics.MaximumFps},{"valid",statistics.ValidSamples},{"retained",statistics.RetainedSamples},{"excluded",statistics.ExcludedSamples},{"missing",statistics.MissingSamples}}}
        };
    }

    private static Dictionary<string,object> StatisticsSummary(FrameScopeFrameStatistics.Result result,bool filtered)
    {
        FrameScopeFrameStatistics.Values values=filtered?result.Filtered:result.Raw;
        double? minimum=null,maximum=null;
        for(int i=0;i<result.Samples.Count;i++) {
            if(filtered&&!result.Included[i])continue;
            if(!FrameScopeFrameStatistics.Valid(result.Samples[i]))continue;
            var value=result.Samples[i].Average;
            if(value.HasValue){minimum=!minimum.HasValue||value<minimum?value:minimum;maximum=!maximum.HasValue||value>maximum?value:maximum;}
        }
        return new Dictionary<string,object>{{"average",values.Average},{"low1",values.Low1},{"low01",values.Low01},{"min",minimum},{"max",maximum}};
    }

    private static Dictionary<string,object> BuildStatisticsV2(List<KeyValuePair<DateTime,double>> frames,DateTime start)
    {
        var input=new List<FrameScopeFrameStatistics.Frame>(frames.Count);
        foreach(var frame in frames)input.Add(new FrameScopeFrameStatistics.Frame((frame.Key-start).TotalMilliseconds,frame.Value));
        var statistics=FrameScopeFrameStatistics.Replay(input);
        var times=new List<double>();var average=new List<double?>();var low1=new List<double?>();var low01=new List<double?>();var counts=new List<int>();
        foreach(var sample in statistics.Samples){times.Add(sample.TimeMs/1000.0);average.Add(sample.Average);low1.Add(sample.Low1);low01.Add(sample.Low01);counts.Add(sample.Count);}
        return new Dictionary<string,object>{
            {"statisticsVersion",FrameScopeFrameStatistics.Version},{"profile","gamepp-lite-v2"},
            {"bucketMs",1000},{"lowWindowMs",1000},{"updateIntervalMs",100},
            {"t",times},{"avg",average},{"low1",low1},{"low01",low01},{"samples",counts},{"included",statistics.Included},
            {"rawSummary",StatisticsSummary(statistics,false)},{"filteredSummary",StatisticsSummary(statistics,true)},
            {"filter",new Dictionary<string,object>{{"minimum",statistics.MinimumFps},{"maximum",statistics.MaximumFps},
                {"valid",statistics.ValidSamples},{"retained",statistics.RetainedSamples},{"excluded",statistics.ExcludedSamples},{"missing",statistics.MissingSamples}}}
        };
    }

    private static List<KeyValuePair<DateTime, double>> AlignPresentMonTime(List<KeyValuePair<DateTime, double>> frames, List<SystemRow> systemRows, out int timeShiftHours)
    {
        timeShiftHours = 0;
        if (frames.Count == 0 || systemRows.Count == 0) return frames;
        double hours = (systemRows[0].Time - frames[0].Key).TotalHours;
        int rounded = (int)Math.Round(hours);
        if (Math.Abs(hours) >= 1.0 && Math.Abs(hours - rounded) < 0.25 && Math.Abs(rounded) <= 14)
        {
            timeShiftHours = rounded;
            return frames.Select(f => new KeyValuePair<DateTime, double>(f.Key.AddHours(rounded), f.Value)).ToList();
        }
        return frames;
    }

    private static Dictionary<string, object> BuildBucketedFps(List<KeyValuePair<DateTime, double>> frames, DateTime start, double bucketSeconds, double lowWindowSeconds)
    {
        Dictionary<string, object> result = new Dictionary<string, object>
        {
            { "bucketMs", (int)Math.Round(bucketSeconds * 1000) },
            { "lowWindowMs", (int)Math.Round(lowWindowSeconds * 1000) },
            { "t", new List<double>() },
            { "avg", new List<double?>() },
            { "low1", new List<double?>() },
            { "low01", new List<double?>() },
            { "samples", new List<int>() }
        };
        if (frames.Count == 0) return result;

        SortedDictionary<int, FpsBucketAccumulator> buckets = new SortedDictionary<int, FpsBucketAccumulator>();
        foreach (KeyValuePair<DateTime, double> frame in frames)
        {
            double sec = (frame.Key - start).TotalSeconds;
            if (sec < 0) continue;
            int bucket = (int)Math.Floor(sec / bucketSeconds);
            FpsBucketAccumulator accumulator;
            if (!buckets.TryGetValue(bucket, out accumulator))
            {
                accumulator = new FpsBucketAccumulator();
                buckets[bucket] = accumulator;
            }
            accumulator.SumMs += frame.Value;
            accumulator.Count++;
        }

        List<double> times = new List<double>(buckets.Count);
        List<double?> avg = new List<double?>(buckets.Count);
        List<double?> low1 = new List<double?>(buckets.Count);
        List<double?> low01 = new List<double?>(buckets.Count);
        List<int> samples = new List<int>(buckets.Count);
        Fenwick fenwick = new Fenwick(100001);
        Queue<int> windowBins = new Queue<int>();
        Queue<double> windowSecs = new Queue<double>();
        int frameIndex = 0;

        foreach (KeyValuePair<int, FpsBucketAccumulator> bucket in buckets)
        {
            double t = RoundDouble(bucket.Key * bucketSeconds, 3);
            double windowStart = t - lowWindowSeconds;
            double windowEnd = t + bucketSeconds;
            while (frameIndex < frames.Count)
            {
                double sec = (frames[frameIndex].Key - start).TotalSeconds;
                if (sec < 0)
                {
                    frameIndex++;
                    continue;
                }
                if (sec > windowEnd) break;

                int bin = MsToBin(frames[frameIndex].Value);
                fenwick.Add(bin, 1);
                windowBins.Enqueue(bin);
                windowSecs.Enqueue(sec);
                frameIndex++;
            }

            while (windowSecs.Count > 0 && windowSecs.Peek() < windowStart)
            {
                windowSecs.Dequeue();
                fenwick.Add(windowBins.Dequeue(), -1);
            }

            double meanMs = bucket.Value.Count > 0 ? bucket.Value.SumMs / bucket.Value.Count : 0;
            times.Add(t);
            avg.Add(meanMs > 0 ? RoundDouble(1000.0 / meanMs, 2) : (double?)null);
            low1.Add(FpsFromFenwick(fenwick, 0.99));
            low01.Add(FpsFromFenwick(fenwick, 0.999));
            samples.Add(bucket.Value.Count);
        }

        result["t"] = times;
        result["avg"] = avg;
        result["low1"] = low1;
        result["low01"] = low01;
        result["samples"] = samples;
        return result;
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
        return PercentileHighSorted(sorted, quantile);
    }

    private static double? PercentileHighSorted(List<double> sorted, double quantile)
    {
        if (sorted == null || sorted.Count == 0) return null;
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

    private static FrameStatsSummary CalculateFrameStats(List<KeyValuePair<DateTime, double>> frames)
    {
        FrameStatsSummary stats = new FrameStatsSummary();
        if (frames == null || frames.Count == 0) return stats;

        List<double> sortedFrameMs = new List<double>(frames.Count);
        foreach (KeyValuePair<DateTime, double> frame in frames)
        {
            double ms = frame.Value;
            stats.Count++;
            stats.SumMs += ms;
            if (ms < stats.MinMs) stats.MinMs = ms;
            if (ms > stats.MaxMs) stats.MaxMs = ms;
            if (ms > 20.0) stats.FramesOver20++;
            if (ms > 33.3) stats.FramesOver33++;
            if (ms > 100.0) stats.FramesOver100++;
            sortedFrameMs.Add(ms);
        }

        sortedFrameMs.Sort();
        stats.Low1Fps = LowFpsFromSortedFrameMs(sortedFrameMs, 0.01);
        stats.Low01Fps = LowFpsFromSortedFrameMs(sortedFrameMs, 0.001);
        return stats;
    }

    private static double? LowFpsFromSortedFrameMs(List<double> sortedFrameMs, double fraction)
    {
        return FrameScopeLiveFrameStats.LowFromSorted(sortedFrameMs, fraction);
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

    private static int HeaderIndex(Dictionary<string, int> headers, string name)
    {
        int index;
        return headers != null && headers.TryGetValue(name, out index) ? index : -1;
    }

    private static bool ListHasValue(object value)
    {
        IEnumerable<double?> doubles = value as IEnumerable<double?>;
        if (doubles == null) return false;
        foreach (double? d in doubles) if (d.HasValue) return true;
        return false;
    }
}
