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
    private const string Brand = "FrameScope";
    private static readonly string[] Colors = new[]
    {
        "#29e6ff", "#a9ff47", "#ffd35b", "#ff5d7d", "#65a7ff", "#d77cff",
        "#45ff9a", "#ff9f43", "#70e1f5", "#f8f871", "#38ef7d", "#ff6bcb",
        "#a4b0be", "#ffa502", "#2ed573", "#ff7675", "#7bed9f", "#5352ed"
    };

    private static int Main(string[] args)
    {
        string progressPath = args == null ? "" : GetArgValue(args, "--progress", "");
        try
        {
            string runDir = args != null && args.Length > 0 ? Path.GetFullPath(args[0]) : FindLatestRun(Directory.GetCurrentDirectory());
            Generate(runDir, progressPath);
            return 0;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(progressPath))
            {
                try { FrameScopeReportProgress.Write(progressPath, "生成失败", 100, ex.Message, DateTime.Now, ex.Message, true); }
                catch { }
            }
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void Generate(string runDir, string progressPath)
    {
        DateTime progressStart = DateTime.Now;
        Directory.CreateDirectory(Path.Combine(runDir, "charts"));
        WriteProgress(progressPath, "读取数据", 5, "读取 PresentMon、系统和进程 CSV", progressStart, null, false);

        PresentReadResult present = ReadPresentMon(Path.Combine(runDir, "presentmon.csv"));
        List<SystemRow> systemRows = ReadSystem(Path.Combine(runDir, "system-samples.csv"));
        int timeShiftHours;
        List<KeyValuePair<DateTime, double>> frames = AlignPresentMonTime(present.Frames, systemRows, out timeShiftHours);
        WriteProgress(progressPath, "处理数据", 20, "对齐帧时间和系统采样时间线", progressStart, null, false);

        DateTime start;
        DateTime end;
        if (frames.Count > 0)
        {
            start = frames[0].Key;
            end = frames[frames.Count - 1].Key;
        }
        else if (systemRows.Count > 0)
        {
            start = systemRows[0].Time;
            end = systemRows[systemRows.Count - 1].Time;
        }
        else
        {
            start = end = DateTime.Now;
        }

        int durationSeconds = Math.Max(1, (int)Math.Round((end - start).TotalSeconds));
        Dictionary<string, object> hardware = LoadHardware();
        Dictionary<string, string> metadata = LoadRunMetadata(runDir);
        Dictionary<string, object> captureDiagnostics = LoadCaptureDiagnostics(runDir);
        Dictionary<string, object> cpuCoreTelemetry = LoadCpuCoreTelemetryMetadataStrict(runDir, captureDiagnostics);
        string targetProcess = metadata.ContainsKey("targetProcess") ? metadata["targetProcess"] : "cs2.exe";
        string targetDisplayName = metadata.ContainsKey("targetDisplayName") ? metadata["targetDisplayName"] : targetProcess;
        if (string.IsNullOrWhiteSpace(targetDisplayName)) targetDisplayName = targetProcess;

        double? totalMemoryMb = GetDoubleFromHardware(hardware, "TotalMemoryMB");
        List<double> availableValues = systemRows.Where(r => r.AvailableMb.HasValue).Select(r => r.AvailableMb.Value).ToList();
        if (!totalMemoryMb.HasValue && availableValues.Count > 0) totalMemoryMb = availableValues.Max();
        double? totalMemoryGb = totalMemoryMb.HasValue ? totalMemoryMb.Value / 1024.0 : (double?)null;

        WriteProgress(progressPath, "处理进程", 35, "读取后台进程 CPU、内存和峰值", progressStart, null, false);
        ProcessMatrixResult process = ReadProcessMatrix(Path.Combine(runDir, "process-samples.csv"), start, targetProcess);
        Dictionary<string, object> systemSeries = SeriesFromSystem(systemRows, start, totalMemoryMb);
        Dictionary<string, object> cpuCoreCharts = ReadCpuCoreCharts(runDir, start, cpuCoreTelemetry);

        FrameStatsSummary frameSummary = CalculateFrameStats(frames);
        Dictionary<string, object> frameStats = new Dictionary<string, object>
        {
            { "average", frameSummary.Count > 0 ? Round(1000.0 / (frameSummary.SumMs / frameSummary.Count), 2) : null },
            { "low1", Round(frameSummary.Low1Fps, 2) },
            { "low01", Round(frameSummary.Low01Fps, 2) },
            { "minInstant", frameSummary.Count > 0 ? Round(1000.0 / frameSummary.MaxMs, 3) : null },
            { "maxInstant", frameSummary.Count > 0 ? Round(1000.0 / frameSummary.MinMs, 3) : null },
            { "maxFrameMs", frameSummary.Count > 0 ? Round(frameSummary.MaxMs, 3) : null },
            { "framesOver20", frameSummary.FramesOver20 },
            { "framesOver33", frameSummary.FramesOver33 },
            { "framesOver100", frameSummary.FramesOver100 }
        };

        List<double> vramTotalValues = systemRows.Where(r => r.VramTotalMiB.HasValue).Select(r => r.VramTotalMiB.Value / 1024.0).ToList();
        double? vramTotalGb = vramTotalValues.Count > 0 ? vramTotalValues.Max() : (double?)null;
        List<double> vramUsedValues = systemRows.Where(r => r.VramUsedMiB.HasValue).Select(r => r.VramUsedMiB.Value / 1024.0).ToList();
        List<double> cpuValues = systemRows.Where(r => r.Cpu.HasValue).Select(r => r.Cpu.Value).ToList();
        List<double> gpuValues = systemRows.Where(r => r.Gpu.HasValue).Select(r => r.Gpu.Value).ToList();
        List<double> gpuTempValues = systemRows.Where(r => r.GpuTemp.HasValue).Select(r => r.GpuTemp.Value).ToList();
        List<double> gpuClockValues = systemRows.Where(r => r.GpuClock.HasValue).Select(r => r.GpuClock.Value).ToList();
        List<double> powerValues = systemRows.Where(r => r.Power.HasValue).Select(r => r.Power.Value).ToList();
        double? availableAvgGb = availableValues.Count > 0 ? availableValues.Average() / 1024.0 : (double?)null;
        double? memUsedAvgGb = totalMemoryGb.HasValue && availableAvgGb.HasValue ? totalMemoryGb.Value - availableAvgGb.Value : (double?)null;
        double? memUsedPctAvg = totalMemoryGb.HasValue && memUsedAvgGb.HasValue ? memUsedAvgGb.Value / totalMemoryGb.Value * 100.0 : (double?)null;
        double? vramUsedAvg = vramUsedValues.Count > 0 ? vramUsedValues.Average() : (double?)null;
        double? vramUsedPctAvg = vramTotalGb.HasValue && vramUsedAvg.HasValue ? vramUsedAvg.Value / vramTotalGb.Value * 100.0 : (double?)null;
        Dictionary<string, object> systemStats = new Dictionary<string, object>
        {
            { "cpuAvg", Round(AverageOrNull(cpuValues), 2) },
            { "cpuMax", Round(MaxOrNull(cpuValues), 2) },
            { "gpuAvg", Round(AverageOrNull(gpuValues), 2) },
            { "gpuTempAvg", Round(AverageOrNull(gpuTempValues), 2) },
            { "gpuClockAvg", Round(AverageOrNull(gpuClockValues), 0) },
            { "powerAvg", Round(AverageOrNull(powerValues), 2) },
            { "vramUsedAvg", Round(vramUsedAvg, 2) },
            { "vramUsedPctAvg", Round(vramUsedPctAvg, 2) },
            { "memUsedAvgGb", Round(memUsedAvgGb, 2) },
            { "memUsedPctAvg", Round(memUsedPctAvg, 2) }
        };

        WriteProgress(progressPath, "降采样", 55, "计算 FPS、1% Low、0.1% Low 和图表序列", progressStart, null, false);
        Dictionary<string, object> fps = BuildBucketedFps(frames, start, 1.0, 2.0);
        Dictionary<string, object> notes = new Dictionary<string, object>
        {
            { "frameDataCaptured", frames.Count > 0 },
            { "cpuFrequencyCaptured", ListHasValue(((Dictionary<string, object>)((Dictionary<string, object>)systemSeries["perf"]))["cpuFreq"]) },
            { "presentMonSelectionMode", present.Diagnostics.ContainsKey("selectionMode") ? present.Diagnostics["selectionMode"] : null },
            { "frameCaptureStatus", captureDiagnostics.ContainsKey("FrameCaptureStatus") ? captureDiagnostics["FrameCaptureStatus"] : (frames.Count > 0 ? "captured" : "no-frame-data") },
            { "frameCaptureMessage", captureDiagnostics.ContainsKey("FrameCaptureMessage") ? captureDiagnostics["FrameCaptureMessage"] : "" },
            { "generator", "native-csharp" }
        };

        Dictionary<string, object> data = new Dictionary<string, object>
        {
            { "brand", Brand },
            { "colors", Colors },
            { "run", new Dictionary<string, object>
                {
                    { "dir", runDir },
                    { "startLabel", start.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "endLabel", end.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "durationLabel", FormatDuration(durationSeconds) },
                    { "timeShiftHours", timeShiftHours }
                }
            },
            { "target", new Dictionary<string, object> { { "processName", targetProcess }, { "displayName", targetDisplayName } } },
            { "hardware", hardware },
            { "hardwareDerived", new Dictionary<string, object> { { "totalMemoryGb", Round(totalMemoryGb, 2) }, { "vramTotalGb", Round(vramTotalGb, 2) } } },
            { "counts", new Dictionary<string, object> { { "frames", frames.Count }, { "hasFrameData", frames.Count > 0 }, { "processSamples", process.Times.Count }, { "processes", process.Names.Count }, { "systemSamples", systemRows.Count } } },
            { "presentMon", present.Diagnostics },
            { "frameStats", frameStats },
            { "systemStats", systemStats },
            { "fps", fps },
            { "system", systemSeries },
            { "cpuCore", cpuCoreCharts["frequency"] },
            { "cpuVoltage", cpuCoreCharts["voltage"] },
            { "cpuVid", cpuCoreCharts["vid"] },
            { "process", new Dictionary<string, object> { { "t", process.Times }, { "names", process.Names }, { "codec", process.Codec }, { "cpu", process.Cpu }, { "mem", process.Mem }, { "stats", process.Stats } } },
            { "capture", captureDiagnostics },
            { "notes", notes }
        };

        string chartsDir = Path.Combine(runDir, "charts");
        string dataPath = Path.Combine(chartsDir, "framescope-interactive-data.js");
        string htmlPath = Path.Combine(chartsDir, "framescope-interactive-report.html");
        string manifestPath = Path.Combine(chartsDir, "framescope-interactive-manifest.json");

        JavaScriptSerializer serializer = CreateArtifactJsonSerializer();

        WriteProgress(progressPath, "生成图表", 80, "写入交互数据和 HTML 报告", progressStart, null, false);
        File.WriteAllText(dataPath, "window.FRAMESCOPE_DATA = " + serializer.Serialize(data) + ";" + Environment.NewLine, new UTF8Encoding(false));
        File.WriteAllText(htmlPath, MakeHtml(), new UTF8Encoding(false));

        Dictionary<string, object> manifest = new Dictionary<string, object>
        {
            { "report", htmlPath },
            { "data", dataPath },
            { "targetDisplayName", targetDisplayName },
            { "targetProcessName", targetProcess },
            { "frames", frames.Count },
            { "rawPresentMonRows", GetDiagnostic(present.Diagnostics, "rawRows") },
            { "validPresentMonRows", GetDiagnostic(present.Diagnostics, "validRows") },
            { "presentMonSelectionMode", GetDiagnostic(present.Diagnostics, "selectionMode") },
            { "presentMonSelectedTrack", GetDiagnostic(present.Diagnostics, "selectedTrack") },
            { "hasFrameData", frames.Count > 0 },
            { "reportKind", frames.Count > 0 ? "full" : "diagnostic" },
            { "processes", process.Names.Count },
            { "processSamples", process.Times.Count },
            { "systemSamples", systemRows.Count },
            { "cpuFrequencyCaptured", notes["cpuFrequencyCaptured"] },
            { "frameCaptureStatus", notes["frameCaptureStatus"] },
            { "frameCaptureMessage", notes["frameCaptureMessage"] },
            { "presentMonFailureCategory", captureDiagnostics.ContainsKey("PresentMonFailureCategory") ? captureDiagnostics["PresentMonFailureCategory"] : null },
            { "presentMonEtwAccessDenied", captureDiagnostics.ContainsKey("PresentMonEtwAccessDenied") ? captureDiagnostics["PresentMonEtwAccessDenied"] : null },
            { "presentMonCsvPath", captureDiagnostics.ContainsKey("PresentMonCsvPath") ? captureDiagnostics["PresentMonCsvPath"] : null },
            { "presentMonCsvLastCheckTime", captureDiagnostics.ContainsKey("PresentMonCsvLastCheckTime") ? captureDiagnostics["PresentMonCsvLastCheckTime"] : null },
            { "presentMonCsvBytes", captureDiagnostics.ContainsKey("PresentMonCsvBytes") ? captureDiagnostics["PresentMonCsvBytes"] : null },
            { "presentMonCsvRows", captureDiagnostics.ContainsKey("PresentMonCsvRows") ? captureDiagnostics["PresentMonCsvRows"] : null },
            { "presentMonRuntimeMs", captureDiagnostics.ContainsKey("PresentMonRuntimeMs") ? captureDiagnostics["PresentMonRuntimeMs"] : null },
            { "presentMonStartedAt", captureDiagnostics.ContainsKey("PresentMonStartedAt") ? captureDiagnostics["PresentMonStartedAt"] : null },
            { "presentMonExitedAt", captureDiagnostics.ContainsKey("PresentMonExitedAt") ? captureDiagnostics["PresentMonExitedAt"] : null },
            { "presentMonStdoutTail", captureDiagnostics.ContainsKey("PresentMonStdoutTail") ? captureDiagnostics["PresentMonStdoutTail"] : null },
            { "presentMonStderrTail", captureDiagnostics.ContainsKey("PresentMonStderrTail") ? captureDiagnostics["PresentMonStderrTail"] : null },
            { "presentMonArgs", captureDiagnostics.ContainsKey("PresentMonArgs") ? captureDiagnostics["PresentMonArgs"] : null },
            { "presentMonCaptureMode", captureDiagnostics.ContainsKey("PresentMonCaptureMode") ? captureDiagnostics["PresentMonCaptureMode"] : null },
            { "presentMonCaptureTarget", captureDiagnostics.ContainsKey("PresentMonCaptureTarget") ? captureDiagnostics["PresentMonCaptureTarget"] : null },
            { "targetPid", captureDiagnostics.ContainsKey("TargetPid") ? captureDiagnostics["TargetPid"] : null },
            { "targetResolvedProcess", captureDiagnostics.ContainsKey("TargetResolvedProcess") ? captureDiagnostics["TargetResolvedProcess"] : null },
            { "targetRunningAtPresentMonExitCheck", captureDiagnostics.ContainsKey("TargetRunningAtPresentMonExitCheck") ? captureDiagnostics["TargetRunningAtPresentMonExitCheck"] : null },
            { "targetPidRunningAtPresentMonExitCheck", captureDiagnostics.ContainsKey("TargetPidRunningAtPresentMonExitCheck") ? captureDiagnostics["TargetPidRunningAtPresentMonExitCheck"] : null },
            { "presentMonPreflightIsElevated", captureDiagnostics.ContainsKey("PresentMonPreflightIsElevated") ? captureDiagnostics["PresentMonPreflightIsElevated"] : null },
            { "presentMonPreflightInPerformanceLogUsers", captureDiagnostics.ContainsKey("PresentMonPreflightInPerformanceLogUsers") ? captureDiagnostics["PresentMonPreflightInPerformanceLogUsers"] : null },
            { "presentMonPreflightToolExists", captureDiagnostics.ContainsKey("PresentMonPreflightToolExists") ? captureDiagnostics["PresentMonPreflightToolExists"] : null },
            { "presentMonPreflightEtwProbeAttempted", captureDiagnostics.ContainsKey("PresentMonPreflightEtwProbeAttempted") ? captureDiagnostics["PresentMonPreflightEtwProbeAttempted"] : null },
            { "cpuCoreSampleCount", cpuCoreTelemetry["cpuCoreSampleCount"] },
            { "cpuCoreTelemetryAvailable", cpuCoreTelemetry["cpuCoreTelemetryAvailable"] },
            { "cpuVoltageAvailable", cpuCoreTelemetry["cpuVoltageAvailable"] },
            { "cpuVoltageVcoreAvailable", cpuCoreTelemetry["cpuVoltageVcoreAvailable"] },
            { "cpuVoltagePerCoreAvailable", cpuCoreTelemetry["cpuVoltagePerCoreAvailable"] },
            { "cpuVoltageNonPerCoreAvailable", cpuCoreTelemetry["cpuVoltageNonPerCoreAvailable"] },
            { "cpuVoltageStatus", cpuCoreTelemetry["cpuVoltageStatus"] },
            { "cpuVoltageReason", cpuCoreTelemetry["cpuVoltageReason"] },
            { "cpuVoltageSource", cpuCoreTelemetry["cpuVoltageSource"] },
            { "cpuVoltageProviderKind", cpuCoreTelemetry["cpuVoltageProviderKind"] },
            { "cpuVoltageProviderRequested", cpuCoreTelemetry["cpuVoltageProviderRequested"] },
            { "cpuVoltageSampleCount", cpuCoreTelemetry["cpuVoltageSampleCount"] },
            { "cpuVoltageVcoreSampleCount", cpuCoreTelemetry["cpuVoltageVcoreSampleCount"] },
            { "cpuVoltagePerCoreSampleCount", cpuCoreTelemetry["cpuVoltagePerCoreSampleCount"] },
            { "cpuVoltageNonPerCoreSampleCount", cpuCoreTelemetry["cpuVoltageNonPerCoreSampleCount"] },
            { "cpuVoltageRejectedSampleCount", cpuCoreTelemetry["cpuVoltageRejectedSampleCount"] },
            { "cpuVoltageSampleIntervalMs", cpuCoreTelemetry["cpuVoltageSampleIntervalMs"] },
            { "cpuVoltageSamplesCsv", cpuCoreTelemetry["cpuVoltageSamplesCsv"] },
            { "cpuVidAvailable", cpuCoreTelemetry["cpuVidAvailable"] },
            { "cpuVidStatus", cpuCoreTelemetry["cpuVidStatus"] },
            { "cpuVidReason", cpuCoreTelemetry["cpuVidReason"] },
            { "cpuVidNote", cpuCoreTelemetry["cpuVidNote"] },
            { "cpuVidSource", cpuCoreTelemetry["cpuVidSource"] },
            { "cpuVidProviderKind", cpuCoreTelemetry["cpuVidProviderKind"] },
            { "cpuVidProviderRequested", cpuCoreTelemetry["cpuVidProviderRequested"] },
            { "cpuVidSampleCount", cpuCoreTelemetry["cpuVidSampleCount"] },
            { "cpuVidCoreCount", cpuCoreTelemetry["cpuVidCoreCount"] },
            { "cpuVidSampleIntervalMs", cpuCoreTelemetry["cpuVidSampleIntervalMs"] },
            { "cpuVidSamplesCsv", cpuCoreTelemetry["cpuVidSamplesCsv"] },
            { "generator", "native-csharp" }
        };
        string manifestJson = SerializeArtifactJson(manifest);
        File.WriteAllText(manifestPath, manifestJson, new UTF8Encoding(false));
        WriteProgress(progressPath, "完成", 100, "报告生成完成", progressStart, null, false);
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Console.WriteLine(manifestJson);
    }

    internal static void GenerateForTests(string runDir)
    {
        Generate(runDir, "");
    }

    internal static string SerializeArtifactJson(object value)
    {
        return EscapeNonAsciiJsonText(CreateArtifactJsonSerializer().Serialize(value));
    }

    private static JavaScriptSerializer CreateArtifactJsonSerializer()
    {
        return new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 256
        };
    }

    private static string EscapeNonAsciiJsonText(string json)
    {
        if (string.IsNullOrEmpty(json)) return json ?? "";
        StringBuilder builder = null;
        for (int i = 0; i < json.Length; i++)
        {
            char ch = json[i];
            if (ch <= 0x7f)
            {
                if (builder != null) builder.Append(ch);
                continue;
            }

            if (builder == null)
            {
                builder = new StringBuilder(json.Length + 16);
                builder.Append(json, 0, i);
            }
            builder.Append("\\u");
            builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
        }

        return builder == null ? json : builder.ToString();
    }

}
