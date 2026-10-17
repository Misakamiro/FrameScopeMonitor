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
    public const string ReportInspectionProtocol = "framescope-report-inspection/1";
    private const string Brand = "FrameScope";
    private static readonly string[] Colors = new[]
    {
        "#29e6ff", "#a9ff47", "#ffd35b", "#ff5d7d", "#65a7ff", "#d77cff",
        "#45ff9a", "#ff9f43", "#70e1f5", "#f8f871", "#38ef7d", "#ff6bcb",
        "#a4b0be", "#ffa502", "#2ed573", "#ff7675", "#7bed9f", "#5352ed"
    };

    private static int Main(string[] args)
    {
        if (args != null && args.Any(a => a.Equals("--inspect-reports", StringComparison.OrdinalIgnoreCase)))
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                string list = GetArgValue(args, "--run-list", "");
                if (string.IsNullOrWhiteSpace(list)) throw new ArgumentException("--run-list is required.");
                string[] runs = File.ReadAllLines(Path.GetFullPath(list), Encoding.UTF8);
                if (runs.Length > 50) throw new ArgumentException("--run-list exceeds the 50-run limit.");
                var results = new List<Dictionary<string, object>>();
                foreach (string raw in runs)
                {
                    string run = (raw ?? "").Trim();
                    if (run.Length == 0) continue;
                    try
                    {
                        FrameScopeReportArtifactState state = FrameScopeReportArtifacts.Inspect(Path.GetFullPath(run));
                        FrameScopeReportInputFingerprint currentInput = FrameScopeReportArtifacts.CaptureInputFingerprint(state.RunDirectory, 2);
                        results.Add(new Dictionary<string, object>
                        {
                            { "runDir", state.RunDirectory }, { "artifactStatus", state.ArtifactStatus },
                            { "isComplete", state.IsComplete }, { "legacyReadable", state.LegacyReadable },
                            { "fingerprintVersion", state.FingerprintVersion },
                            { "manifestInputFingerprint", state.ManifestInputFingerprint },
                            { "currentInputFingerprintVersion", 2 }, { "currentInputFingerprint", currentInput.Value }, { "currentInputFingerprintStable", currentInput.Stable },
                            { "inputFingerprintMatches", state.InputFingerprintMatches }, { "error", state.Error ?? "" },
                            { "reportKind", state.ReportKind }, { "durationSeconds", state.DurationSeconds }, { "canOpen", state.IsReadable }, { "canRegenerate", FrameScopeReportArtifacts.HasUsableMonitorData(state.RunDirectory) }
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new Dictionary<string, object> { { "runDir", run }, { "artifactStatus", "incomplete" }, { "reportKind", "error" }, { "durationSeconds", null }, { "error", ex.Message }, { "canOpen", false }, { "canRegenerate", false } });
                    }
                }
                Console.WriteLine(CreateArtifactJsonSerializer().Serialize(new Dictionary<string, object> { { "protocol", ReportInspectionProtocol }, { "status", "inspected" }, { "count", results.Count }, { "reports", results } }));
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
        }
        if (args != null && args.Any(a => a.Equals("--inspect-report", StringComparison.OrdinalIgnoreCase)))
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                string runDir = GetArgValue(args, "--run-dir", "");
                if (string.IsNullOrWhiteSpace(runDir)) throw new ArgumentException("--run-dir is required.");
                FrameScopeReportArtifactState state = FrameScopeReportArtifacts.Inspect(Path.GetFullPath(runDir));
                Console.WriteLine(CreateArtifactJsonSerializer().Serialize(new Dictionary<string, object>
                {
                    { "protocol", ReportInspectionProtocol }, { "runDir", state.RunDirectory }, { "artifactStatus", state.ArtifactStatus },
                    { "isComplete", state.IsComplete }, { "legacyReadable", state.LegacyReadable },
                    { "inputFingerprintMatches", state.InputFingerprintMatches }, { "error", state.Error ?? "" },
                    { "reportKind", state.ReportKind }, { "durationSeconds", state.DurationSeconds }, { "canOpen", state.IsReadable }, { "canRegenerate", FrameScopeReportArtifacts.HasUsableMonitorData(state.RunDirectory) }
                }));
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
        }
        string progressPath = args == null ? "" : GetArgValue(args, "--progress", "");
        string requestedInputFingerprint = args == null ? "" : GetArgValue(args, "--input-fingerprint", "");
        try
        {
            string runDir = args != null && args.Length > 0 ? Path.GetFullPath(args[0]) : FindLatestRun(Directory.GetCurrentDirectory());
            Generate(runDir, progressPath, requestedInputFingerprint);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private sealed class ReportOperationBusyException : InvalidOperationException {
        public ReportOperationBusyException() : base("记录正在处理，不能同时生成或删除报告") { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle handle, StringBuilder path, uint size, uint flags);

    private static string CanonicalReportRun(string runDir) {
        using (var handle = CreateFileW(Path.GetFullPath(runDir), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)) {
            if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception();
            var path = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0);
            if (length == 0 || length >= path.Capacity) throw new System.ComponentModel.Win32Exception();
            string result = path.ToString();
            return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result.Substring(4) : result;
        }
    }

    private static void Generate(string runDir, string progressPath, string requestedInputFingerprint)
    {
        string normalized = CanonicalReportRun(runDir).TrimEnd('\\', '/');
        char[] chars = normalized.ToCharArray();
        for (int i = 0; i < chars.Length; i++) if (chars[i] >= 'a' && chars[i] <= 'z') chars[i] = (char)(chars[i] - 32);
        string lockName;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            lockName = "Local\\FrameScopeReportOperation-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(new string(chars)))).Replace("-", "").ToLowerInvariant();
        using (var operationLock = new System.Threading.Mutex(false, lockName)) {
        bool acquired;
        try { acquired = operationLock.WaitOne(0); } catch (System.Threading.AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new ReportOperationBusyException();
        try {
        string inputFingerprint = string.IsNullOrWhiteSpace(requestedInputFingerprint)
            ? FrameScopeReportArtifacts.CaptureInputFingerprint(runDir, 2).Value
            : requestedInputFingerprint;
        FrameScopeReportPublisher.Publish(runDir, delegate(string outputDirectory)
        {
            GenerateArtifacts(runDir, progressPath, outputDirectory, inputFingerprint);
        });
        } catch (Exception ex) {
            if (!string.IsNullOrWhiteSpace(progressPath) && Directory.Exists(runDir)) {
                try { FrameScopeReportProgress.Write(progressPath, "生成失败", 100, ex.Message, DateTime.Now, ex.Message, true); }
                catch { }
            }
            throw;
        } finally { operationLock.ReleaseMutex(); }
        }
    }

    private static double? ReadRecordedCaptureDuration(string runDir)
    {
        foreach (string name in new[] { "summary.json", "status.json" })
        {
            try
            {
                var map = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runDir, name), Encoding.UTF8));
                object first, last; DateTime start, end;
                if (map != null && map.TryGetValue("StartTime", out first) && map.TryGetValue("EndTime", out last) &&
                    DateTime.TryParse(Convert.ToString(first), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start) &&
                    DateTime.TryParse(Convert.ToString(last), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out end) && end.ToUniversalTime() > start.ToUniversalTime())
                    return (end.ToUniversalTime() - start.ToUniversalTime()).TotalSeconds;
            }
            catch (IOException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        return null;
    }

    private static void GenerateArtifacts(string runDir, string progressPath, string chartsDir, string inputFingerprint)
    {
        DateTime progressStart = DateTime.Now;
        Directory.CreateDirectory(chartsDir);
        WriteProgress(progressPath, "读取数据", 5, "读取 PresentMon、系统和进程 CSV", progressStart, null, false);

        PresentReadResult present = ReadPresentMon(Path.Combine(runDir, "presentmon.csv"));
        List<SystemRow> systemRows = ReadSystem(Path.Combine(runDir, "system-samples.csv"));
        // The GPU columns come from the hardware-service snapshot stream (the
        // overlay's own data source) when hardware-samples.csv exists.
        int overlayMergedRows = MergeHardwareOverlaySamples(systemRows, Path.Combine(runDir, "hardware-samples.csv"));
        string processPath = Path.Combine(runDir, "process-samples.csv");
        int timeShiftHours;
        List<KeyValuePair<DateTime, double>> frames = AlignPresentMonTime(present.Frames, systemRows, out timeShiftHours);
        List<SystemRow> fpsSampleRows = frames.Count == 0
            ? ReadSystem(Path.Combine(runDir, "hardware-samples.csv"))
            : new List<SystemRow>();
        List<double> fpsSamples = fpsSampleRows.Where(r => r.Fps.HasValue && r.Fps.Value >= 0).Select(r => r.Fps.Value).ToList();
        bool hasFpsSamples = fpsSamples.Count > 0;
        WriteProgress(progressPath, "处理数据", 20, "对齐帧时间和系统采样时间线", progressStart, null, false);

        DateTime start;
        DateTime end;
        bool hasTrustedTime = true;
        DateTime processStart;
        DateTime processEnd;
        if (frames.Count > 0)
        {
            start = frames[0].Key;
            end = frames[frames.Count - 1].Key;
        }
        else if (hasFpsSamples)
        {
            start = fpsSampleRows[0].Time;
            end = fpsSampleRows[fpsSampleRows.Count - 1].Time;
        }
        else if (systemRows.Count > 0)
        {
            start = systemRows[0].Time;
            end = systemRows[systemRows.Count - 1].Time;
        }
        else if (TryReadProcessTimeRange(processPath, out processStart, out processEnd))
        {
            start = processStart;
            end = processEnd;
        }
        else
        {
            hasTrustedTime = false;
            start = end = DateTime.MinValue;
        }

        if (hasTrustedTime && end <= start) hasTrustedTime = false;
        double? durationSeconds = ReadRecordedCaptureDuration(runDir) ?? (hasTrustedTime ? (double?)((end - start).TotalSeconds) : FrameScopeReportArtifacts.ReadDuration(runDir, null));
        Dictionary<string, object> hardware = LoadHardware();
        Dictionary<string, string> metadata = LoadRunMetadata(runDir);
        Dictionary<string, object> captureDiagnostics = LoadCaptureDiagnostics(runDir);
        // H3 tail: make the overlay merge visible in the report so a silent
        // zero (column drift, missing file) is diagnosable from the artifact.
        captureDiagnostics["OverlayMergedRows"] = overlayMergedRows;
        captureDiagnostics["FramesFromHardwareSamples"] = false;
        captureDiagnostics["FpsSampleCount"] = fpsSamples.Count;
        captureDiagnostics["FpsSampleTargetVerified"] = false;
        FrameScopeSamplerEvidence processSampler = LoadSamplerEvidence(runDir, "ProcessSampler", "process-samples.csv", new[] { "Time", "ProcessName" });
        FrameScopeSamplerEvidence systemSampler = LoadSamplerEvidence(runDir, "SystemSampler", "system-samples.csv", new[] { "Time" });
        FrameScopeRunContract.AddStatusFields(captureDiagnostics, "ProcessSampler", processSampler);
        FrameScopeRunContract.AddStatusFields(captureDiagnostics, "SystemSampler", systemSampler);
        Dictionary<string, object> cpuCoreTelemetry = LoadCpuCoreTelemetryMetadataStrict(runDir, captureDiagnostics);
        string targetProcess = metadata.ContainsKey("targetProcess") ? metadata["targetProcess"] : "cs2.exe";
        string targetDisplayName = metadata.ContainsKey("targetDisplayName") ? metadata["targetDisplayName"] : targetProcess;
        if (string.IsNullOrWhiteSpace(targetDisplayName)) targetDisplayName = targetProcess;

        double? totalMemoryMb = GetDoubleFromHardware(hardware, "TotalMemoryMB");
        List<double> availableValues = systemRows.Where(r => r.AvailableMb.HasValue).Select(r => r.AvailableMb.Value).ToList();
        if (!totalMemoryMb.HasValue && availableValues.Count > 0) totalMemoryMb = availableValues.Max();
        double? totalMemoryGb = totalMemoryMb.HasValue ? totalMemoryMb.Value / 1024.0 : (double?)null;

        WriteProgress(progressPath, "处理进程", 35, "读取后台进程 CPU、内存和峰值", progressStart, null, false);
        ProcessMatrixResult process = ReadProcessMatrix(processPath, start, targetProcess);
        int processSampleCount = process.SamplingInstantCount;
        int systemSampleCount = systemRows.Count;
        string reportKind = hasFpsSamples ? "diagnostic" : FrameScopeRunContract.Classify(frames.Count, processSampler, systemSampler);
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
            { "framesOver20", frameSummary.Count > 0 ? (object)frameSummary.FramesOver20 : null },
            { "framesOver33", frameSummary.Count > 0 ? (object)frameSummary.FramesOver33 : null },
            { "framesOver100", frameSummary.Count > 0 ? (object)frameSummary.FramesOver100 : null }
        };
        Dictionary<string, object> fpsSampleStats = new Dictionary<string, object>
        {
            { "count", fpsSamples.Count },
            { "aggregation", "arithmetic-mean-of-samples" },
            { "average", Round(AverageOrNull(fpsSamples), 2) },
            { "minimum", hasFpsSamples ? Round(fpsSamples.Min(), 2) : null },
            { "maximum", Round(MaxOrNull(fpsSamples), 2) }
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
        Dictionary<string, object> fps = hasFpsSamples
            ? BuildSampledFps(fpsSampleRows, start)
            : BuildBucketedFps(frames, start, 1.0, 2.0);
        fps["source"] = hasFpsSamples ? "etw-fps-sampled" : (frames.Count > 0 ? "presentmon" : "none");
        Dictionary<string, object> notes = new Dictionary<string, object>
        {
            { "frameDataCaptured", frames.Count > 0 },
            { "cpuFrequencyCaptured", ListHasValue(((Dictionary<string, object>)((Dictionary<string, object>)systemSeries["perf"]))["cpuFreq"]) },
            { "presentMonSelectionMode", present.Diagnostics.ContainsKey("selectionMode") ? present.Diagnostics["selectionMode"] : null },
            { "frameCaptureStatus", hasFpsSamples ? "etw-fps-sampled" : (captureDiagnostics.ContainsKey("FrameCaptureStatus") ? captureDiagnostics["FrameCaptureStatus"] : (frames.Count > 0 ? "captured" : "no-frame-data")) },
            { "frameCaptureMessage", hasFpsSamples ? "No valid PresentMon frames were captured. Resident ETW FPS samples provide a diagnostic trend and an arithmetic sample mean, not per-frame statistics. Sample target identity was not recorded." : (captureDiagnostics.ContainsKey("FrameCaptureMessage") ? captureDiagnostics["FrameCaptureMessage"] : "") },
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
                    { "durationSeconds", durationSeconds },
                    { "durationLabel", durationSeconds.HasValue ? FormatDuration((int)Math.Round(durationSeconds.Value)) : "不可用" },
                    { "timeShiftHours", timeShiftHours }
                }
            },
            { "target", new Dictionary<string, object> { { "processName", targetProcess }, { "displayName", targetDisplayName } } },
            { "hardware", hardware },
            { "hardwareDerived", new Dictionary<string, object> { { "totalMemoryGb", Round(totalMemoryGb, 2) }, { "vramTotalGb", Round(vramTotalGb, 2) } } },
            { "counts", new Dictionary<string, object> { { "frames", frames.Count }, { "hasFrameData", frames.Count > 0 }, { "fpsSamples", fpsSamples.Count }, { "processSamples", processSampleCount }, { "processes", process.Names.Count }, { "systemSamples", systemSampleCount } } },
            { "presentMon", present.Diagnostics },
            { "frameStats", frameStats },
            { "fpsSampleStats", fpsSampleStats },
            { "systemStats", systemStats },
            { "fps", fps },
            { "system", systemSeries },
            { "cpuCore", cpuCoreCharts["frequency"] },
            { "cpuVoltage", cpuCoreCharts["voltage"] },
            { "cpuVid", cpuCoreCharts["vid"] },
            { "cpuOsdVoltage", ReadCpuOsdVoltageChart(runDir, start) },
            { "process", new Dictionary<string, object> { { "t", process.Times }, { "names", process.Names }, { "codec", process.Codec }, { "cpu", process.Cpu }, { "mem", process.Mem }, { "stats", process.Stats } } },
            { "capture", captureDiagnostics },
            { "notes", notes }
        };

        string dataPath = Path.Combine(chartsDir, "framescope-interactive-data.js");
        string htmlPath = Path.Combine(chartsDir, "framescope-interactive-report.html");
        string manifestPath = Path.Combine(chartsDir, "framescope-interactive-manifest.json");
        string finalChartsDir = Path.Combine(runDir, "charts");
        string finalDataPath = Path.Combine(finalChartsDir, "framescope-interactive-data.js");
        string finalHtmlPath = Path.Combine(finalChartsDir, "framescope-interactive-report.html");

        JavaScriptSerializer serializer = CreateArtifactJsonSerializer();

        WriteProgress(progressPath, "生成图表", 80, "写入交互数据和 HTML 报告", progressStart, null, false);
        File.WriteAllText(dataPath, "window.FRAMESCOPE_DATA = " + serializer.Serialize(data) + ";" + Environment.NewLine, new UTF8Encoding(false));
        File.WriteAllText(htmlPath, MakeHtml(), new UTF8Encoding(false));

        Dictionary<string, object> manifest = new Dictionary<string, object>
        {
            { "report", finalHtmlPath },
            { "data", finalDataPath },
            { "targetDisplayName", targetDisplayName },
            { "targetProcessName", targetProcess },
            { "frames", frames.Count },
            { "fpsSamples", fpsSamples.Count },
            { "fpsSource", fps["source"] },
            { "fpsSampleStats", fpsSampleStats },
            { "rawPresentMonRows", GetDiagnostic(present.Diagnostics, "rawRows") },
            { "validPresentMonRows", GetDiagnostic(present.Diagnostics, "validRows") },
            { "presentMonSelectionMode", GetDiagnostic(present.Diagnostics, "selectionMode") },
            { "presentMonSelectedTrack", GetDiagnostic(present.Diagnostics, "selectedTrack") },
            { "hasFrameData", frames.Count > 0 },
            { "reportKind", reportKind },
            { "durationSeconds", durationSeconds },
            { "inputFingerprintVersion", 2 },
            { FrameScopeReportArtifacts.InputFingerprintFieldName, inputFingerprint },
            { "processes", process.Names.Count },
            { "processSamples", processSampleCount },
            { "systemSamples", systemSampleCount },
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
        AddSamplerManifestFields(manifest, "processSampler", processSampler);
        AddSamplerManifestFields(manifest, "systemSampler", systemSampler);
        string manifestJson = SerializeArtifactJson(manifest);
        FrameScopeJsonFile.Write(manifestPath, manifestJson);
        WriteProgress(progressPath, "完成", 100, "报告生成完成", progressStart, null, false);
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Console.WriteLine(manifestJson);
    }

    internal static void GenerateForTests(string runDir)
    {
        Generate(runDir, "", FrameScopeReportArtifacts.CaptureInputFingerprint(runDir, 2).Value);
    }

    private static void AddSamplerManifestFields(Dictionary<string, object> manifest, string prefix, FrameScopeSamplerEvidence evidence)
    {
        manifest[prefix + "Required"] = evidence.Required;
        manifest[prefix + "Exe"] = evidence.ExecutablePath ?? "";
        manifest[prefix + "ExecutableAvailable"] = evidence.ExecutableAvailable;
        manifest[prefix + "Started"] = evidence.Started;
        manifest[prefix + "Pid"] = evidence.Pid;
        manifest[prefix + "StartedAt"] = evidence.StartedAt.HasValue ? evidence.StartedAt.Value.ToString("o", CultureInfo.InvariantCulture) : "";
        manifest[prefix + "ExitedAt"] = evidence.ExitedAt.HasValue ? evidence.ExitedAt.Value.ToString("o", CultureInfo.InvariantCulture) : "";
        manifest[prefix + "ExitCode"] = evidence.ExitCode;
        manifest[prefix + "ExitedEarly"] = evidence.ExitedEarly;
        manifest[prefix + "StopRequested"] = evidence.StopRequested;
        manifest[prefix + "ForcedStop"] = evidence.ForcedStop;
        manifest[prefix + "CsvPath"] = evidence.CsvPath ?? "";
        manifest[prefix + "CsvExists"] = evidence.CsvExists;
        manifest[prefix + "CsvBytes"] = evidence.CsvBytes;
        manifest[prefix + "ValidRows"] = evidence.ValidRows;
        manifest[prefix + "Status"] = evidence.Status ?? "";
        manifest[prefix + "ErrorTail"] = evidence.ErrorTail ?? "";
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
