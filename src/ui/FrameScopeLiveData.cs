using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

internal static partial class FrameScopeNativeMonitor
{
    private static FrameScopeLiveSnapshot LoadLiveSnapshot(FrameScopeConfig config)
    {
        FrameScopeLiveRuntimeResult runtime = FrameScopeLiveRuntime.Resolve(activePageKey, BuildLiveRuntimeMonitors(config));
        if (runtime.ShouldClearCharts)
        {
            return CreateEmptyLiveSnapshot(runtime.Message, runtime);
        }

        var snapshot = new FrameScopeLiveSnapshot
        {
            HasRealData = true,
            RunDir = runtime.RunDir,
            TargetName = runtime.Game,
            ProcessName = runtime.ProcessName,
            SourceLabel = "真实数据：" + Path.GetFileName(runtime.RunDir) + "，读取当前活动目标。",
            MemoryLabel = "等待 system-samples.csv"
        };

        var status = ReadStatusDictionary(runtime.RunDir);
        if (status != null)
        {
            snapshot.ProcessName = StatusString(status, "TargetProcess", StatusString(status, "TargetResolvedProcess", snapshot.ProcessName));
            snapshot.LogLines.Add("[INFO] 会话状态：" + StatusString(status, "Phase", "未知"));
            snapshot.LogLines.Add("[INFO] FPS 采集：" + StatusString(status, "FrameCaptureStatus", "未知"));
            string message = StatusString(status, "FrameCaptureMessage", "");
            if (!string.IsNullOrWhiteSpace(message)) snapshot.LogLines.Add("[INFO] " + message);
        }

        TryPopulatePresentMonTail(Path.Combine(runtime.RunDir, "presentmon.csv"), snapshot, 420);
        TryPopulateSystemTail(Path.Combine(runtime.RunDir, "system-samples.csv"), snapshot);

        if (snapshot.FpsValues.Count == 0)
        {
            snapshot.SourceLabel = "真实会话：" + Path.GetFileName(runtime.RunDir) + "，presentmon.csv 暂无可绘制 FPS 数据。";
            snapshot.LogLines.Add("[WARN] 未读取到 FPS 帧数据，不切换成演示数据。");
        }
        else
        {
            snapshot.LogLines.Add("[INFO] 已读取真实 FPS 帧样本：" + snapshot.FrameCount.ToString(CultureInfo.InvariantCulture));
        }

        return snapshot;
    }

    private static FrameScopeLiveSnapshot CreateEmptyLiveSnapshot(string reason, FrameScopeLiveRuntimeResult runtime)
    {
        var snapshot = new FrameScopeLiveSnapshot
        {
            HasRealData = false,
            SourceLabel = string.IsNullOrWhiteSpace(reason) ? "未捕获：等待实时监测数据。" : reason,
            ProcessName = runtime == null ? "" : runtime.ProcessName,
            MemoryLabel = "未捕获"
        };
        snapshot.LogLines.Add("[INFO] " + snapshot.SourceLabel);
        snapshot.LogLines.Add("[INFO] 正常界面不会使用演示数据冒充实时监控。");
        return snapshot;
    }

    private static List<FrameScopeLiveRuntimeMonitor> BuildLiveRuntimeMonitors(FrameScopeConfig config)
    {
        var monitors = new List<FrameScopeLiveRuntimeMonitor>();
        if (!string.Equals(activePageKey, "live", StringComparison.OrdinalIgnoreCase)) return monitors;
        if (!File.Exists(StatePath)) return monitors;
        if (!IsWatcherRunningQuiet()) return monitors;

        Dictionary<string, object> state;
        try
        {
            state = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath, Encoding.UTF8));
        }
        catch
        {
            return monitors;
        }

        object activeObj;
        if (state == null || !state.TryGetValue("ActiveMonitors", out activeObj) || activeObj == null) return monitors;

        foreach (var map in EnumerateObjectMaps(activeObj))
        {
            string game = MapString(map, "Game", "未知目标");
            string processName = MapString(map, "ProcessName", "");
            string runRoot = MapString(map, "RunRoot", "");
            string runDir = LatestRunFromRoot(runRoot);
            var processBases = BuildTargetProcessBaseNames(processName, game);
            bool processRunning = processBases.Count > 0 && IsAnyTargetProcessRunning(processBases);

            monitors.Add(new FrameScopeLiveRuntimeMonitor
            {
                Game = game,
                ProcessName = processName,
                RunRoot = runRoot,
                RunDir = runDir,
                ProcessRunning = processRunning,
                HasReadableRun = IsReadableLiveRun(runDir)
            });
        }

        return monitors;
    }

    private static IEnumerable<Dictionary<string, object>> EnumerateObjectMaps(object value)
    {
        var direct = value as Dictionary<string, object>;
        if (direct != null)
        {
            yield return direct;
            yield break;
        }

        var array = value as IEnumerable;
        if (array == null) yield break;
        foreach (var item in array)
        {
            var map = item as Dictionary<string, object>;
            if (map != null) yield return map;
        }
    }

    private static string MapString(Dictionary<string, object> map, string key, string fallback)
    {
        if (map == null) return fallback;
        object value;
        if (!map.TryGetValue(key, out value) || value == null) return fallback;
        string text = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static string LatestRunFromRoot(string runRoot)
    {
        if (string.IsNullOrWhiteSpace(runRoot)) return "";
        try
        {
            DirectoryInfo latest = LatestRunDirectory(runRoot);
            return latest == null ? "" : latest.FullName;
        }
        catch
        {
            return "";
        }
    }

    private static bool IsReadableLiveRun(string runDir)
    {
        if (string.IsNullOrWhiteSpace(runDir) || !Directory.Exists(runDir)) return false;
        return File.Exists(Path.Combine(runDir, "presentmon.csv")) ||
               File.Exists(Path.Combine(runDir, "system-samples.csv")) ||
               File.Exists(Path.Combine(runDir, "status.json"));
    }

    private static void TryPopulatePresentMonTail(string csvPath, FrameScopeLiveSnapshot snapshot, int maxRows)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath)) return;

        try
        {
            var header = ParseCsvLine(ReadFirstLine(csvPath));
            int msIndex = HeaderIndex(header, "MsBetweenPresents");
            int appIndex = HeaderIndex(header, "Application");
            if (msIndex < 0) return;

            foreach (var line in ReadTailLines(csvPath, maxRows + 4))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Application,", StringComparison.OrdinalIgnoreCase)) continue;
                var row = ParseCsvLine(line);
                double frameMs;
                if (!TryCsvDouble(row, msIndex, out frameMs)) continue;
                if (frameMs <= 0 || frameMs > 1000) continue;

                snapshot.FrameMsValues.Add(frameMs);
                snapshot.FpsValues.Add(1000.0 / frameMs);
                if (string.IsNullOrWhiteSpace(snapshot.ProcessName) && appIndex >= 0 && appIndex < row.Count)
                {
                    snapshot.ProcessName = row[appIndex];
                }
                if (snapshot.FpsValues.Count >= maxRows) break;
            }

            ApplyFpsStats(snapshot);
        }
        catch (Exception ex)
        {
            snapshot.LogLines.Add("[WARN] FPS 数据读取失败：" + ex.Message);
        }
    }

    private static void TryPopulateSystemTail(string csvPath, FrameScopeLiveSnapshot snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath)) return;

        try
        {
            var header = ParseCsvLine(ReadFirstLine(csvPath));
            var line = ReadTailLines(csvPath, 3).LastOrDefault(v => !string.IsNullOrWhiteSpace(v) && !v.StartsWith("Time,", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(line)) return;

            var row = ParseCsvLine(line);
            double value;
            int cpuIndex = HeaderIndex(header, "TotalCpuPct");
            if (TryCsvDouble(row, cpuIndex, out value)) snapshot.CpuPct = value;

            int gpuIndex = HeaderIndex(header, "GpuUtilPct");
            if (TryCsvDouble(row, gpuIndex, out value)) snapshot.GpuPct = value;

            int availableIndex = HeaderIndex(header, "AvailableMB");
            if (TryCsvDouble(row, availableIndex, out value)) snapshot.MemoryLabel = "可用 " + (value / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            snapshot.LogLines.Add("[INFO] 已读取 system-samples.csv 最新系统状态。");
        }
        catch (Exception ex)
        {
            snapshot.LogLines.Add("[WARN] 系统采样读取失败：" + ex.Message);
        }
    }

    private static void ApplyFpsStats(FrameScopeLiveSnapshot snapshot)
    {
        if (snapshot == null) return;

        var fps = snapshot.FpsValues.Where(v => v > 0 && !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        snapshot.FrameCount = fps.Count;
        if (fps.Count == 0) return;

        snapshot.CurrentFps = fps[fps.Count - 1];
        snapshot.AverageFps = fps.Average();
        var sorted = fps.OrderBy(v => v).ToList();
        snapshot.Low1Fps = sorted.Take(Math.Max(1, sorted.Count / 100)).Average();
        snapshot.Low01Fps = sorted.Take(Math.Max(1, sorted.Count / 1000)).Average();

        var frameMs = snapshot.FrameMsValues.Where(v => v > 0 && !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        if (frameMs.Count > 0) snapshot.AverageFrameMs = frameMs.Average();
    }
}
