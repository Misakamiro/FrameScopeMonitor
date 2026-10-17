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
    private static PresentReadResult ReadPresentMon(string path)
    {
        PresentReadResult result = new PresentReadResult();
        int totalRows = 0;
        int validRows = 0;
        int invalidRows = 0;
        int outOfRangeRows = 0;
        if (!File.Exists(path))
        {
            result.Diagnostics["rawRows"] = 0;
            result.Diagnostics["validRows"] = 0;
            result.Diagnostics["selectedRows"] = 0;
            result.Diagnostics["selectionMode"] = "missing";
            return result;
        }

        Dictionary<string, PresentTrack> tracks = new Dictionary<string, PresentTrack>();
        using (CsvTable table = CsvTable.Open(path))
        {
            Dictionary<string, int> h = table.Headers;
            int[] columns = new[]
            {
                HeaderIndex(h, h.ContainsKey("TimeInDateTime") ? "TimeInDateTime" : "TimeInSeconds"),
                HeaderIndex(h, "MsBetweenPresents"),
                HeaderIndex(h, "Application"),
                HeaderIndex(h, "ProcessID"),
                HeaderIndex(h, "SwapChainAddress"),
                HeaderIndex(h, "PresentMode"),
                HeaderIndex(h, "AllowsTearing")
            };
            string[] row;
            while ((row = table.ReadFields(columns)) != null)
            {
                totalRows++;
                DateTime t;
                double? ms = ParseNullableDouble(row[1]);
                if (!TryParseDate(row[0], out t) || !ms.HasValue)
                {
                    invalidRows++;
                    continue;
                }
                if (!(ms.Value > 0 && ms.Value < 10000))
                {
                    outOfRangeRows++;
                    continue;
                }
                validRows++;

                string application = row[2].Trim();
                string processId = row[3].Trim();
                string swapChain = row[4].Trim();
                string presentMode = row[5].Trim();
                string allowsTearing = row[6].Trim();
                string key = processId + "|" + swapChain + "|" + application;
                PresentTrack track;
                if (!tracks.TryGetValue(key, out track))
                {
                    track = new PresentTrack { ProcessId = processId, SwapChain = swapChain, Application = application };
                    tracks[key] = track;
                }

                bool isHardware = IsHardwarePresentMode(presentMode);
                track.Rows++;
                if (isHardware) track.HardwareRows++;
                if (allowsTearing == "1" || allowsTearing.Equals("true", StringComparison.OrdinalIgnoreCase)) track.AllowsTearingRows++;
                if (ms.Value > 1000) track.ArtifactRowsOver1000ms++;
                track.Frames.Add(new PresentFrame { Time = t, FrameMs = ms.Value, IsHardware = isHardware });
            }
        }

        SelectPresentMonFrames(tracks.Values, validRows, result);
        result.Diagnostics["rawRows"] = totalRows;
        result.Diagnostics["validRows"] = validRows;
        result.Diagnostics["invalidRows"] = invalidRows;
        result.Diagnostics["outOfRangeRows"] = outOfRangeRows;
        return result;
    }

    private static void SelectPresentMonFrames(IEnumerable<PresentTrack> tracks, int validRows, PresentReadResult result)
    {
        List<PresentTrack> summaries = new List<PresentTrack>();
        foreach (PresentTrack track in tracks)
        {
            summaries.Add(track);
        }

        if (summaries.Count == 0)
        {
            result.Diagnostics["selectedRows"] = 0;
            result.Diagnostics["selectionMode"] = "empty";
            return;
        }

        foreach (PresentTrack track in summaries)
        {
            List<double> scoring = PresentTrackFrameValues(track, track.HardwareRows > 0);
            double? medianMs = PercentileHigh(scoring, 0.5);
            double? p99 = PercentileHigh(scoring, 0.99);
            track.MedianFps = medianMs.HasValue && medianMs.Value > 0 ? 1000.0 / medianMs.Value : (double?)null;
            track.P99FrameMs = p99;
            track.Score = FrameScopeLiveFrameStats.TrackScore(track.HardwareRows, track.Rows, medianMs, p99);
        }
        summaries.Sort(delegate(PresentTrack a, PresentTrack b) {
            return FrameScopeLiveFrameStats.CompareTracks(a.Score, a.ProcessId + "|" + a.SwapChain + "|" + a.Application,
                b.Score, b.ProcessId + "|" + b.SwapChain + "|" + b.Application);
        });
        PresentTrack selected = summaries[0];
        bool multiTrack = summaries.Count > 1;
        bool useHardwareOnly = multiTrack && selected.HardwareRows > 0;

        List<PresentFrame> selectedFrames = new List<PresentFrame>();
        int droppedModeRows = 0;
        foreach (PresentFrame frame in selected.Frames)
        {
            if (useHardwareOnly && !frame.IsHardware)
            {
                droppedModeRows++;
                continue;
            }
            if (frame.FrameMs > 1000)
            {
                droppedModeRows++;
                continue;
            }
            selectedFrames.Add(frame);
        }
        selectedFrames.Sort(delegate(PresentFrame a, PresentFrame b) { return a.Time.CompareTo(b.Time); });
        foreach (PresentFrame frame in selectedFrames) result.Frames.Add(new KeyValuePair<DateTime, double>(frame.Time, frame.FrameMs));

        int droppedTrackRows = validRows - selected.Rows;
        result.Diagnostics["selectedRows"] = selectedFrames.Count;
        result.Diagnostics["selectionMode"] = multiTrack ? "primary-hardware-track" : "all";
        result.Diagnostics["selectedTrack"] = selected.ToJson();
        result.Diagnostics["tracks"] = summaries.Select(t => t.ToJson()).ToList();
        result.Diagnostics["trackCount"] = summaries.Count;
        result.Diagnostics["droppedTrackRows"] = Math.Max(0, droppedTrackRows);
        result.Diagnostics["droppedModeRows"] = Math.Max(0, droppedModeRows);
        result.Diagnostics["droppedResumeArtifactRows"] = selected.ArtifactRowsOver1000ms;
    }

    private static List<double> PresentTrackFrameValues(PresentTrack track, bool hardwareOnly)
    {
        List<double> values = new List<double>(hardwareOnly ? track.HardwareRows : track.Rows);
        foreach (PresentFrame frame in track.Frames)
        {
            if (!hardwareOnly || frame.IsHardware) values.Add(frame.FrameMs);
        }
        return values;
    }

    private static bool IsHardwarePresentMode(string presentMode)
    {
        return presentMode != null && presentMode.StartsWith("Hardware:", StringComparison.OrdinalIgnoreCase);
    }

    internal static Dictionary<string, object> ReadPresentMonForTests(string path)
    {
        PresentReadResult result = ReadPresentMon(path);
        return new Dictionary<string, object>
        {
            { "frames", result.Frames.Count },
            { "diagnostics", result.Diagnostics }
        };
    }

    internal static Dictionary<string, object> ReadLiveFrameStatsForTests(string path)
    {
        var stats = CalculateFrameStats(ReadPresentMon(path).Frames);
        return new Dictionary<string, object> { { "count", (int)stats.Count }, { "low1", stats.Low1Fps }, { "low01", stats.Low01Fps } };
    }

    internal static Dictionary<string, object> ReadRollingFrameStatsForTests(string path)
    {
        var frames = ReadPresentMon(path).Frames;
        var fenwick = new Fenwick(100001);
        if (frames.Count > 0) {
            DateTime start = frames[frames.Count - 1].Key.AddSeconds(-3);
            foreach (var frame in frames) if (frame.Key >= start) fenwick.Add(MsToBin(frame.Value), 1);
        }
        return new Dictionary<string, object> { { "count", fenwick.Count },
            { "low1", FpsFromFenwick(fenwick, .99) }, { "low01", FpsFromFenwick(fenwick, .999) } };
    }
}
