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

        List<PresentRecord> records = new List<PresentRecord>();
        using (CsvTable table = CsvTable.Open(path))
        {
            Dictionary<string, int> h = table.Headers;
            List<string> row;
            while ((row = table.ReadRow()) != null)
            {
                totalRows++;
                DateTime t;
                double? ms = ParseNullableDouble(Get(row, h, "MsBetweenPresents"));
                if (!TryParseDate(Get(row, h, "TimeInDateTime"), out t) || !ms.HasValue)
                {
                    invalidRows++;
                    continue;
                }
                if (!(ms.Value > 0 && ms.Value < 10000))
                {
                    outOfRangeRows++;
                    continue;
                }
                records.Add(new PresentRecord
                {
                    RowIndex = totalRows - 1,
                    Time = t,
                    FrameMs = ms.Value,
                    Application = Get(row, h, "Application").Trim(),
                    ProcessId = Get(row, h, "ProcessID").Trim(),
                    SwapChain = Get(row, h, "SwapChainAddress").Trim(),
                    PresentMode = Get(row, h, "PresentMode").Trim(),
                    AllowsTearing = Get(row, h, "AllowsTearing").Trim()
                });
            }
        }

        SelectPresentMonFrames(records, result);
        result.Diagnostics["rawRows"] = totalRows;
        result.Diagnostics["validRows"] = records.Count;
        result.Diagnostics["invalidRows"] = invalidRows;
        result.Diagnostics["outOfRangeRows"] = outOfRangeRows;
        return result;
    }

    private static void SelectPresentMonFrames(List<PresentRecord> records, PresentReadResult result)
    {
        if (records.Count == 0)
        {
            result.Diagnostics["selectedRows"] = 0;
            result.Diagnostics["selectionMode"] = "empty";
            return;
        }

        Dictionary<string, PresentTrack> tracks = new Dictionary<string, PresentTrack>();
        foreach (PresentRecord r in records)
        {
            string key = r.ProcessId + "|" + r.SwapChain + "|" + r.Application;
            PresentTrack track;
            if (!tracks.TryGetValue(key, out track))
            {
                track = new PresentTrack { ProcessId = r.ProcessId, SwapChain = r.SwapChain, Application = r.Application };
                tracks[key] = track;
            }
            track.Records.Add(r);
        }

        List<PresentTrack> summaries = new List<PresentTrack>();
        foreach (PresentTrack track in tracks.Values)
        {
            track.Rows = track.Records.Count;
            List<double> values = new List<double>();
            List<double> hardware = new List<double>();
            foreach (PresentRecord r in track.Records)
            {
                values.Add(r.FrameMs);
                if (IsHardwarePresent(r))
                {
                    hardware.Add(r.FrameMs);
                    track.HardwareRows++;
                }
                if (r.AllowsTearing == "1" || r.AllowsTearing.Equals("true", StringComparison.OrdinalIgnoreCase)) track.AllowsTearingRows++;
                if (r.FrameMs > 1000) track.ArtifactRowsOver1000ms++;
            }
            List<double> scoring = hardware.Count > 0 ? hardware : values;
            double? medianMs = PercentileHigh(scoring, 0.5);
            double? p99 = PercentileHigh(scoring, 0.99);
            track.MedianFps = medianMs.HasValue && medianMs.Value > 0 ? 1000.0 / medianMs.Value : (double?)null;
            track.P99FrameMs = p99;
            track.Score = track.HardwareRows * 3.0 + track.Rows;
            if (track.MedianFps.HasValue) track.Score += Math.Min(240.0, track.MedianFps.Value) * 20.0;
            if (track.P99FrameMs.HasValue) track.Score -= Math.Min(1000.0, track.P99FrameMs.Value) * 2.0;
            summaries.Add(track);
        }
        summaries.Sort(delegate(PresentTrack a, PresentTrack b) { return b.Score.CompareTo(a.Score); });
        PresentTrack selected = summaries[0];
        bool multiTrack = summaries.Count > 1;
        bool useHardwareOnly = multiTrack && selected.HardwareRows > 0;

        List<PresentRecord> selectedRecords = new List<PresentRecord>();
        int droppedModeRows = 0;
        foreach (PresentRecord r in selected.Records)
        {
            if (useHardwareOnly && !IsHardwarePresent(r))
            {
                droppedModeRows++;
                continue;
            }
            if (r.FrameMs > 1000)
            {
                droppedModeRows++;
                continue;
            }
            selectedRecords.Add(r);
        }
        selectedRecords.Sort(delegate(PresentRecord a, PresentRecord b) { return a.Time.CompareTo(b.Time); });
        foreach (PresentRecord r in selectedRecords) result.Frames.Add(new KeyValuePair<DateTime, double>(r.Time, r.FrameMs));

        int droppedTrackRows = records.Count - selected.Records.Count;
        result.Diagnostics["selectedRows"] = selectedRecords.Count;
        result.Diagnostics["selectionMode"] = multiTrack ? "primary-hardware-track" : "all";
        result.Diagnostics["selectedTrack"] = selected.ToJson();
        result.Diagnostics["tracks"] = summaries.Select(t => t.ToJson()).ToList();
        result.Diagnostics["trackCount"] = summaries.Count;
        result.Diagnostics["droppedTrackRows"] = Math.Max(0, droppedTrackRows);
        result.Diagnostics["droppedModeRows"] = Math.Max(0, droppedModeRows);
        result.Diagnostics["droppedResumeArtifactRows"] = selected.Records.Count(r => r.FrameMs > 1000);
    }

    private static bool IsHardwarePresent(PresentRecord record)
    {
        return record.PresentMode != null && record.PresentMode.StartsWith("Hardware:", StringComparison.OrdinalIgnoreCase);
    }
}
