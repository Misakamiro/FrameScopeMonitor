using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

public static class FrameScopeWebBridgeTests
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    public static int Main()
    {
        try
        {
            MissingRequestIdReturnsContractError();
            StateSnapshotReturnsMatchingResponse();
            StateSnapshotIncludesHostWindowState();
            ConfigGetReadsRealConfig();
            ConfigSaveWritesOnlyConfiguredPath();
            ConfigSaveRoundTripsThemeWindowAndCpuTelemetryFields();
            ProcessesRefreshReturnsAcceptedThenEvent();
            MonitorStartReturnsAcceptedThenEvent();
            MonitorStopReturnsAcceptedThenEvent();
            ReportsListReadsValidatedHistory();
            ReportsListSkipsNoisyFallbackButKeepsHistoryAndExpectedReports();
            ReportOpenRejectsFrontendPathPayload();
            ReportOpenUsesValidatedReportId();
            HtmlOnlyReportCannotOpen();
            ReportOpenDirectoryUsesValidatedReportId();
            LogsOpenDirectoryRejectsFrontendPathPayload();
            LogsOpenDirectoryUsesHostResolvedLogDirectory();
            LogsOpenDirectoryCreatesMissingLogDirectory();
            ReportRegenerateReturnsAcceptedThenProgressEvent();
            DiagnosticsGenerateReturnsAcceptedThenProgressEvent();
            TargetsGetAndSaveRoundTripThroughConfigStore();
            TargetsSavePreservesThemeWindowAndCpuTelemetryFields();
            Console.WriteLine("FrameScopeWebBridgeTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
    }

    private static void MissingRequestIdReturnsContractError()
    {
        var bridge = CreateBridge(CreateTempRoot("missing-request-id"));
        var response = Decode(bridge.HandleJsonMessage("{\"type\":\"state.snapshot\",\"payload\":{}}"));

        AssertEqual("response", AsString(response, "type"), "missing request id response type");
        AssertEqual(false, AsBool(response, "ok"), "missing request id ok");
        var error = (Dictionary<string, object>)response["error"];
        AssertEqual("missing_request_id", AsString(error, "code"), "missing request id error code");
    }

    private static void StateSnapshotReturnsMatchingResponse()
    {
        string root = CreateTempRoot("state-snapshot");
        var bridge = CreateBridge(root);
        var response = Decode(bridge.HandleJsonMessage(Request("state-1", "state.snapshot", "{}")));

        AssertEqual("state-1", AsString(response, "requestId"), "snapshot request id");
        AssertEqual("response", AsString(response, "type"), "snapshot response type");
        AssertEqual(true, AsBool(response, "ok"), "snapshot ok");
        var payload = (Dictionary<string, object>)response["payload"];
        AssertEqual("ready", AsString(payload, "bridgeStatus"), "snapshot bridge status");
        AssertEqual(false, AsBool((Dictionary<string, object>)payload["watcher"], "running"), "snapshot watcher running");
        AssertEqual(true, AsBool((Dictionary<string, object>)payload["config"], "exists"), "snapshot config exists");
    }

    private static void StateSnapshotIncludesHostWindowState()
    {
        string root = CreateTempRoot("state-snapshot-host");
        string configPath = Path.Combine(root, "framescope-config.json");
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
        config.CloseWindowBehavior = "exit";
        config.TrayEnabled = true;
        FrameScopeConfigStore.Save(configPath, config);

        var bridge = new FrameScopeWebBridge(new FrameScopeWebBridgeOptions
        {
            Root = root,
            ConfigPath = configPath,
            StatePath = Path.Combine(root, "framescope-watcher-state.json"),
            HistoryPath = Path.Combine(root, "framescope-history.jsonl"),
            HostStateProvider = delegate
            {
                return new FrameScopeWebBridgeHostState
                {
                    WindowVisible = false,
                    TrayAvailable = true
                };
            }
        }, null);

        var response = Decode(bridge.HandleJsonMessage(Request("state-host-1", "state.snapshot", "{}")));
        var payload = (Dictionary<string, object>)response["payload"];
        var host = (Dictionary<string, object>)payload["host"];

        AssertEqual(false, AsBool(host, "windowVisible"), "snapshot host window visible");
        AssertEqual(true, AsBool(host, "trayAvailable"), "snapshot host tray available");
        AssertEqual("exit", AsString(host, "closeWindowBehavior"), "snapshot host close behavior");
    }

    private static void ConfigGetReadsRealConfig()
    {
        string root = CreateTempRoot("config-get");
        string configPath = Path.Combine(root, "framescope-config.json");
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
        config.Targets = new List<FrameScopeTarget>
        {
            new FrameScopeTarget
            {
                Enabled = true,
                Name = "Bridge Test Game",
                ProcessName = "BridgeTest.exe",
                SampleIntervalMs = 250,
                ProcessSampleIntervalMs = 250,
                SlowSampleIntervalMs = 1000,
                OpenReportOnComplete = false
            }
        };
        FrameScopeConfigStore.Save(configPath, config);

        var bridge = CreateBridge(root);
        var response = Decode(bridge.HandleJsonMessage(Request("config-1", "config.get", "{}")));
        var payload = (Dictionary<string, object>)response["payload"];
        var returnedConfig = (Dictionary<string, object>)payload["config"];
        var targets = (IList)returnedConfig["Targets"];
        var target = (Dictionary<string, object>)targets[0];

        AssertEqual("config-1", AsString(response, "requestId"), "config request id");
        AssertEqual(true, AsBool(response, "ok"), "config ok");
        AssertEqual(1, AsInt(payload, "enabledTargetCount"), "enabled target count");
        AssertEqual("BridgeTest.exe", AsString(target, "ProcessName"), "config process");
        AssertEqual(false, AsBool(target, "OpenReportOnComplete"), "target auto open");
    }

    private static void ConfigSaveWritesOnlyConfiguredPath()
    {
        string root = CreateTempRoot("config-save");
        string configPath = Path.Combine(root, "framescope-config.json");
        FrameScopeConfigStore.Save(configPath, FrameScopeConfigStore.CreateDefaultConfig());

        var bridge = CreateBridge(root);
        string payload = "{\"config\":{\"DataRoot\":\"" + Escape(Path.Combine(root, "runs")) + "\",\"OpenReportOnComplete\":false,\"Targets\":[{\"Enabled\":true,\"Name\":\"Saved Game\",\"ProcessName\":\"SavedGame.exe\",\"SampleIntervalMs\":300,\"ProcessSampleIntervalMs\":300,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}]}}";
        var response = Decode(bridge.HandleJsonMessage(Request("save-1", "config.save", payload)));
        FrameScopeConfig saved = FrameScopeConfigStore.Load(configPath);

        AssertEqual("save-1", AsString(response, "requestId"), "save request id");
        AssertEqual(true, AsBool(response, "ok"), "save ok");
        AssertEqual(false, saved.OpenReportOnComplete, "saved auto open");
        AssertEqual("SavedGame.exe", saved.Targets[0].ProcessName, "saved target");
    }

    private static void ConfigSaveRoundTripsThemeWindowAndCpuTelemetryFields()
    {
        string root = CreateTempRoot("config-save-theme");
        string configPath = Path.Combine(root, "framescope-config.json");
        FrameScopeConfigStore.Save(configPath, FrameScopeConfigStore.CreateDefaultConfig());

        var bridge = CreateBridge(root);
        string payload = "{\"config\":{\"DataRoot\":\"" + Escape(Path.Combine(root, "runs")) + "\",\"TelemetrySampleIntervalMs\":1500,\"ThemeMode\":\"dark\",\"CloseWindowBehavior\":\"exit\",\"TrayEnabled\":false,\"CpuTelemetry\":{\"CollectPerCoreFrequency\":true,\"CollectCpuVoltage\":true,\"PerCoreSampleIntervalMs\":1500,\"PerCoreVoltageSampleIntervalMs\":1750,\"VoltageProvider\":\"disabled\"},\"Targets\":[{\"Enabled\":true,\"Name\":\"Saved Game\",\"ProcessName\":\"SavedGame.exe\",\"SampleIntervalMs\":300,\"ProcessSampleIntervalMs\":1000,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":true}]}}";
        var response = Decode(bridge.HandleJsonMessage(Request("save-theme-1", "config.save", payload)));
        var payloadMap = (Dictionary<string, object>)response["payload"];
        var returnedConfig = (Dictionary<string, object>)payloadMap["config"];
        var returnedTelemetry = (Dictionary<string, object>)returnedConfig["CpuTelemetry"];
        FrameScopeConfig saved = FrameScopeConfigStore.Load(configPath);

        AssertEqual(true, AsBool(response, "ok"), "theme config save ok");
        AssertEqual("dark", saved.ThemeMode, "saved theme mode");
        AssertEqual("exit", saved.CloseWindowBehavior, "saved close behavior");
        AssertEqual(false, saved.TrayEnabled, "saved tray enabled");
        AssertEqual(true, saved.CpuTelemetry.CollectPerCoreFrequency, "saved per-core frequency toggle");
        AssertEqual(true, saved.CpuTelemetry.CollectCpuVoltage, "saved voltage toggle");
        AssertEqual(1500, saved.TelemetrySampleIntervalMs, "saved global telemetry interval");
        AssertEqual(1500, saved.CpuTelemetry.PerCoreSampleIntervalMs, "saved per-core interval");
        AssertEqual(1500, saved.CpuTelemetry.PerCoreVoltageSampleIntervalMs, "saved per-core voltage interval");
        AssertEqual("disabled", saved.CpuTelemetry.VoltageProvider, "saved voltage provider");
        AssertEqual("dark", AsString(returnedConfig, "ThemeMode"), "returned theme mode");
        AssertEqual("exit", AsString(returnedConfig, "CloseWindowBehavior"), "returned close behavior");
        AssertEqual(false, AsBool(returnedConfig, "TrayEnabled"), "returned tray enabled");
        AssertEqual(true, AsBool(returnedTelemetry, "CollectPerCoreFrequency"), "returned per-core toggle");
        AssertEqual(true, AsBool(returnedTelemetry, "CollectCpuVoltage"), "returned voltage toggle");
        AssertEqual(1500, AsInt(returnedConfig, "TelemetrySampleIntervalMs"), "returned global telemetry interval");
        AssertEqual(1500, AsInt(returnedTelemetry, "PerCoreSampleIntervalMs"), "returned per-core interval");
        AssertEqual(1500, AsInt(returnedTelemetry, "PerCoreVoltageSampleIntervalMs"), "returned per-core voltage interval");
        AssertEqual("disabled", AsString(returnedTelemetry, "VoltageProvider"), "returned voltage provider");
    }

    private static void ProcessesRefreshReturnsAcceptedThenEvent()
    {
        string root = CreateTempRoot("process-refresh");
        ManualResetEvent done = new ManualResetEvent(false);
        Dictionary<string, object> eventMessage = null;
        var bridge = CreateBridge(root, delegate(string json)
        {
            var decoded = Decode(json);
            if (AsString(decoded, "type") == "event.processesRefreshed")
            {
                eventMessage = decoded;
                done.Set();
            }
        });

        var response = Decode(bridge.HandleJsonMessage(Request("process-1", "processes.refresh", "{\"query\":\"DefinitelyNoSuchFrameScopeProcessForBridgeTest\"}")));
        AssertEqual("process-1", AsString(response, "requestId"), "process request id");
        AssertEqual(true, AsBool(response, "ok"), "process accepted ok");
        var payload = (Dictionary<string, object>)response["payload"];
        AssertEqual("accepted", AsString(payload, "status"), "process accepted status");

        if (!done.WaitOne(10000))
        {
            throw new Exception("process refresh event timed out");
        }

        var eventPayload = (Dictionary<string, object>)eventMessage["payload"];
        AssertEqual("process-1", AsString(eventPayload, "requestId"), "process event request id");
        AssertEqual(0, AsInt(eventPayload, "count"), "process event count");
    }

    private static void MonitorStartReturnsAcceptedThenEvent()
    {
        string root = CreateTempRoot("monitor-start");
        ManualResetEvent done = new ManualResetEvent(false);
        Dictionary<string, object> statusEvent = null;
        var adapter = new RecordingHostAdapter();
        var bridge = CreateBridge(root, delegate(string json)
        {
            var decoded = Decode(json);
            if (AsString(decoded, "type") == "event.status")
            {
                var payload = (Dictionary<string, object>)decoded["payload"];
                if (AsString(payload, "requestId") == "monitor-start-1" && AsString(payload, "status") == "monitor.started")
                {
                    statusEvent = decoded;
                    done.Set();
                }
            }
        }, adapter);

        var response = Decode(bridge.HandleJsonMessage(Request("monitor-start-1", "monitor.start", "{}")));
        AssertEqual(true, AsBool(response, "ok"), "monitor start accepted ok");
        AssertEqual("accepted", AsString((Dictionary<string, object>)response["payload"], "status"), "monitor start accepted status");
        if (!done.WaitOne(10000)) throw new Exception("monitor start event timed out");
        AssertEqual(1, adapter.StartCount, "monitor start adapter count");
        AssertEqual("monitor-start-1", AsString((Dictionary<string, object>)statusEvent["payload"], "requestId"), "monitor start event request id");
    }

    private static void MonitorStopReturnsAcceptedThenEvent()
    {
        string root = CreateTempRoot("monitor-stop");
        ManualResetEvent done = new ManualResetEvent(false);
        var adapter = new RecordingHostAdapter();
        var bridge = CreateBridge(root, delegate(string json)
        {
            var decoded = Decode(json);
            if (AsString(decoded, "type") == "event.status")
            {
                var payload = (Dictionary<string, object>)decoded["payload"];
                if (AsString(payload, "requestId") == "monitor-stop-1" && AsString(payload, "status") == "monitor.stopped")
                {
                    done.Set();
                }
            }
        }, adapter);

        var response = Decode(bridge.HandleJsonMessage(Request("monitor-stop-1", "monitor.stop", "{}")));
        AssertEqual(true, AsBool(response, "ok"), "monitor stop accepted ok");
        AssertEqual("accepted", AsString((Dictionary<string, object>)response["payload"], "status"), "monitor stop accepted status");
        if (!done.WaitOne(10000)) throw new Exception("monitor stop event timed out");
        AssertEqual(1, adapter.StopCount, "monitor stop adapter count");
    }

    private static void ReportsListReadsValidatedHistory()
    {
        string root = CreateTempRoot("reports-list");
        string runDir = CreateReportRun(root, "Bridge Game", true);
        File.WriteAllText(Path.Combine(runDir, "status.json"), Json.Serialize(new Dictionary<string, object>
        {
            { "Phase", "done" },
            { "ReportKind", "partial" },
            { "ReportFrameCount", 120 },
            { "ReportHasFrameData", true },
            { "ProcessSamplerStatus", "healthy" },
            { "ProcessSamplerValidRows", 10 },
            { "SystemSamplerStatus", "failed" },
            { "SystemSamplerValidRows", 0 },
            { "ReportGenerationStartedAt", "2026-07-11T01:02:03.0000000Z" },
            { "ReportGenerationEndedAt", "2026-07-11T01:02:05.0000000Z" },
            { "ReportGenerationTimedOut", true },
            { "ReportCanRetry", true },
            { "ReportGenerationExitCode", -1 }
        }));
        string unsafeRun = Path.Combine(Path.GetTempPath(), "FrameScopeWebBridgeTests-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unsafeRun);
        File.AppendAllText(Path.Combine(root, "framescope-history.jsonl"), HistoryJson("Bridge Game", "BridgeGame.exe", runDir, Path.Combine(runDir, "charts", "framescope-interactive-report.html")) + Environment.NewLine);
        File.AppendAllText(Path.Combine(root, "framescope-history.jsonl"), HistoryJson("Unsafe", "Unsafe.exe", unsafeRun, Path.Combine(unsafeRun, "charts", "framescope-interactive-report.html")) + Environment.NewLine);

        var bridge = CreateBridge(root);
        var response = Decode(bridge.HandleJsonMessage(Request("reports-list-1", "reports.list", "{}")));
        var payload = (Dictionary<string, object>)response["payload"];
        var reports = (IList)payload["reports"];

        AssertEqual(true, AsBool(response, "ok"), "reports list ok");
        AssertEqual(1, reports.Count, "safe report count");
        var report = (Dictionary<string, object>)reports[0];
        AssertEqual("Bridge Game", AsString(report, "game"), "report game");
        AssertEqual(true, AsBool(report, "canOpenReport"), "report can open");
        AssertEqual("partial", AsString(report, "reportKind"), "report source status preserves partial");
        AssertEqual("healthy", AsString(report, "processSamplerStatus"), "report process sampler status");
        AssertEqual("failed", AsString(report, "systemSamplerStatus"), "report system sampler status");
        AssertEqual(10, Convert.ToInt32(report["processSamplerValidRows"]), "report process sampler rows");
        AssertEqual(0, Convert.ToInt32(report["systemSamplerValidRows"]), "report system sampler rows");
        AssertEqual(true, AsBool(report, "reportGenerationTimedOut"), "report timeout");
        AssertEqual(true, AsBool(report, "reportCanRetry"), "report retry");
        AssertEqual(-1, Convert.ToInt32(report["reportGenerationExitCode"]), "report generation exit code");
    }

    private static void ReportsListSkipsNoisyFallbackButKeepsHistoryAndExpectedReports()
    {
        string root = CreateTempRoot("reports-list-noise");
        CreateReportRun(root, "History Game", true);
        CreateReportRun(root, "Discovered Game", false);

        string dataRoot = Path.Combine(root, "runs");
        string noisyRun = Path.Combine(dataRoot, "node_modules", "package", "fake-run");
        string noisyCharts = Path.Combine(noisyRun, "charts");
        Directory.CreateDirectory(noisyCharts);
        File.WriteAllText(Path.Combine(noisyRun, "status.json"), "{\"Phase\":\"done\",\"ReportKind\":\"full\",\"ReportFrameCount\":999}");
        File.WriteAllText(Path.Combine(noisyRun, "presentmon.csv"), "Application,MsBetweenPresents" + Environment.NewLine);
        File.WriteAllText(Path.Combine(noisyCharts, "framescope-interactive-report.html"), "<html><body>noise</body></html>");
        File.SetLastWriteTime(Path.Combine(noisyCharts, "framescope-interactive-report.html"), DateTime.Now.AddMinutes(10));

        var bridge = CreateBridge(root);
        var response = Decode(bridge.HandleJsonMessage(Request("reports-list-noise-1", "reports.list", "{}")));
        var payload = (Dictionary<string, object>)response["payload"];
        var reports = (IList)payload["reports"];

        bool foundHistory = false;
        bool foundDiscovered = false;
        bool foundNoise = false;
        foreach (object item in reports)
        {
            var report = (Dictionary<string, object>)item;
            string game = AsString(report, "game");
            if (game == "History Game") foundHistory = true;
            if (game == "Discovered Game") foundDiscovered = true;
            if (game == "package" || AsString(report, "runDir").IndexOf("node_modules", StringComparison.OrdinalIgnoreCase) >= 0) foundNoise = true;
        }

        AssertEqual(true, AsBool(response, "ok"), "reports list with noise ok");
        AssertEqual(true, foundHistory, "history report remains visible");
        AssertEqual(true, foundDiscovered, "expected-layout report remains discoverable");
        AssertEqual(false, foundNoise, "node_modules report fallback should be skipped");
    }

    private static void ReportOpenRejectsFrontendPathPayload()
    {
        string root = CreateTempRoot("report-open-path");
        string runDir = CreateReportRun(root, "Bridge Game", true);
        string reportId = FirstReportId(root);
        var bridge = CreateBridge(root);
        string payload = "{\"reportId\":\"" + Escape(reportId) + "\",\"path\":\"" + Escape(Path.Combine(runDir, "charts", "framescope-interactive-report.html")) + "\"}";
        var response = Decode(bridge.HandleJsonMessage(Request("report-open-path-1", "reports.open", payload)));
        AssertEqual(false, AsBool(response, "ok"), "report open path rejected ok");
        AssertEqual("path_not_allowed", AsString((Dictionary<string, object>)response["error"], "code"), "report open path rejected code");
    }

    private static void ReportOpenUsesValidatedReportId()
    {
        string root = CreateTempRoot("report-open-id");
        CreateReportRun(root, "Bridge Game", true);
        string reportId = FirstReportId(root);
        var adapter = new RecordingHostAdapter();
        var bridge = CreateBridge(root, null, adapter);

        var response = Decode(bridge.HandleJsonMessage(Request("report-open-1", "reports.open", "{\"reportId\":\"" + Escape(reportId) + "\"}")));
        AssertEqual(true, AsBool(response, "ok"), "report open ok");
        AssertEqual(1, adapter.OpenReportCount, "report open adapter count");
        AssertEqual(true, adapter.LastReportPath.EndsWith("framescope-interactive-report.html", StringComparison.OrdinalIgnoreCase), "opened report path");
    }

    private static void HtmlOnlyReportCannotOpen()
    {
        string root = CreateTempRoot("report-open-html-only");
        string runDir = CreateReportRun(root, "Bridge Game", true);
        File.Delete(Path.Combine(runDir, "charts", "framescope-interactive-data.js"));
        File.Delete(Path.Combine(runDir, "charts", "framescope-interactive-manifest.json"));
        string reportId = FirstReportId(root);
        var adapter = new RecordingHostAdapter();
        var bridge = CreateBridge(root, null, adapter);

        var response = Decode(bridge.HandleJsonMessage(Request("report-open-html-only-1", "reports.open", "{\"reportId\":\"" + Escape(reportId) + "\"}")));
        AssertEqual(false, AsBool(response, "ok"), "HTML-only report open rejected");
        AssertEqual(0, adapter.OpenReportCount, "HTML-only report never reaches host adapter");
    }

    private static void ReportOpenDirectoryUsesValidatedReportId()
    {
        string root = CreateTempRoot("report-open-dir");
        string runDir = CreateReportRun(root, "Bridge Game", true);
        string reportId = FirstReportId(root);
        var adapter = new RecordingHostAdapter();
        var bridge = CreateBridge(root, null, adapter);

        var response = Decode(bridge.HandleJsonMessage(Request("report-open-dir-1", "reports.openDirectory", "{\"reportId\":\"" + Escape(reportId) + "\"}")));
        AssertEqual(true, AsBool(response, "ok"), "report open directory ok");
        AssertEqual(1, adapter.OpenDirectoryCount, "report open directory adapter count");
        AssertEqual(runDir, adapter.LastDirectoryPath, "opened directory path");
    }

    private static void LogsOpenDirectoryRejectsFrontendPathPayload()
    {
        string root = CreateTempRoot("logs-open-path");
        var bridge = CreateBridge(root);
        string payload = "{\"path\":\"" + Escape(Path.Combine(root, "other")) + "\"}";
        var response = Decode(bridge.HandleJsonMessage(Request("logs-open-path-1", "logs.openDirectory", payload)));
        AssertEqual(false, AsBool(response, "ok"), "logs open path rejected ok");
        AssertEqual("path_not_allowed", AsString((Dictionary<string, object>)response["error"], "code"), "logs open path rejected code");
    }

    private static void LogsOpenDirectoryUsesHostResolvedLogDirectory()
    {
        string root = CreateTempRoot("logs-open-dir");
        var adapter = new RecordingHostAdapter();
        var bridge = CreateBridge(root, null, adapter);

        var response = Decode(bridge.HandleJsonMessage(Request("logs-open-1", "logs.openDirectory", "{}")));
        var payload = (Dictionary<string, object>)response["payload"];

        AssertEqual(true, AsBool(response, "ok"), "logs open directory ok");
        AssertEqual(1, adapter.OpenLogsDirectoryCount, "logs open adapter count");
        AssertEqual(root, adapter.LastLogDirectoryPath, "opened host-resolved log directory");
        AssertEqual(root, AsString(payload, "directory"), "logs response directory");
    }

    private static void LogsOpenDirectoryCreatesMissingLogDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "FrameScopeWebBridgeTests", "logs-missing", Guid.NewGuid().ToString("N"));
        var adapter = new RecordingHostAdapter();
        var bridge = CreateBridge(root, null, adapter);

        var response = Decode(bridge.HandleJsonMessage(Request("logs-open-missing-1", "logs.openDirectory", "{}")));

        AssertEqual(true, AsBool(response, "ok"), "logs open missing directory ok");
        AssertEqual(true, Directory.Exists(root), "host adapter created missing log directory");
        AssertEqual(root, adapter.LastLogDirectoryPath, "created log directory path");
    }

    private static void ReportRegenerateReturnsAcceptedThenProgressEvent()
    {
        string root = CreateTempRoot("report-regenerate");
        CreateReportRun(root, "Bridge Game", true);
        string reportId = FirstReportId(root);
        ManualResetEvent done = new ManualResetEvent(false);
        var adapter = new RecordingHostAdapter();
        var bridge = CreateBridge(root, delegate(string json)
        {
            var decoded = Decode(json);
            if (AsString(decoded, "type") == "event.reportProgress")
            {
                var payload = (Dictionary<string, object>)decoded["payload"];
                if (AsString(payload, "requestId") == "report-regenerate-1" && AsString(payload, "status") == "completed")
                {
                    done.Set();
                }
            }
        }, adapter);

        var response = Decode(bridge.HandleJsonMessage(Request("report-regenerate-1", "reports.regenerate", "{\"reportId\":\"" + Escape(reportId) + "\"}")));
        AssertEqual(true, AsBool(response, "ok"), "report regenerate accepted ok");
        AssertEqual("accepted", AsString((Dictionary<string, object>)response["payload"], "status"), "report regenerate accepted status");
        if (!done.WaitOne(10000)) throw new Exception("report regenerate event timed out");
        AssertEqual(1, adapter.RegenerateCount, "report regenerate adapter count");
    }

    private static void DiagnosticsGenerateReturnsAcceptedThenProgressEvent()
    {
        string root = CreateTempRoot("diagnostics-generate");
        CreateReportRun(root, "Bridge Game", true);
        string reportId = FirstReportId(root);
        ManualResetEvent done = new ManualResetEvent(false);
        var adapter = new RecordingHostAdapter();
        var bridge = CreateBridge(root, delegate(string json)
        {
            var decoded = Decode(json);
            if (AsString(decoded, "type") == "event.reportProgress")
            {
                var payload = (Dictionary<string, object>)decoded["payload"];
                if (AsString(payload, "requestId") == "diagnostics-1" && AsString(payload, "status") == "completed")
                {
                    done.Set();
                }
            }
        }, adapter);

        var response = Decode(bridge.HandleJsonMessage(Request("diagnostics-1", "diagnostics.generate", "{\"reportId\":\"" + Escape(reportId) + "\"}")));
        AssertEqual(true, AsBool(response, "ok"), "diagnostics generate accepted ok");
        AssertEqual("accepted", AsString((Dictionary<string, object>)response["payload"], "status"), "diagnostics accepted status");
        if (!done.WaitOne(10000)) throw new Exception("diagnostics event timed out");
        AssertEqual(1, adapter.DiagnosticsCount, "diagnostics adapter count");
    }

    private static void TargetsGetAndSaveRoundTripThroughConfigStore()
    {
        string root = CreateTempRoot("targets-save");
        var bridge = CreateBridge(root);
        var getResponse = Decode(bridge.HandleJsonMessage(Request("targets-get-1", "targets.get", "{}")));
        AssertEqual(true, AsBool(getResponse, "ok"), "targets get ok");

        string payload = "{\"targets\":[{\"Enabled\":true,\"Name\":\"Target Saved\",\"ProcessName\":\"TargetSaved.exe\",\"SampleIntervalMs\":275,\"ProcessSampleIntervalMs\":275,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":false}],\"dataRoot\":\"" + Escape(Path.Combine(root, "runs")) + "\",\"openReportOnComplete\":false}";
        var saveResponse = Decode(bridge.HandleJsonMessage(Request("targets-save-1", "targets.save", payload)));
        FrameScopeConfig saved = FrameScopeConfigStore.Load(Path.Combine(root, "framescope-config.json"));

        AssertEqual(true, AsBool(saveResponse, "ok"), "targets save ok");
        AssertEqual(1, saved.Targets.Count, "saved targets count");
        AssertEqual("TargetSaved.exe", saved.Targets[0].ProcessName, "saved target process");
        AssertEqual(false, saved.OpenReportOnComplete, "saved global auto open");
    }

    private static void TargetsSavePreservesThemeWindowAndCpuTelemetryFields()
    {
        string root = CreateTempRoot("targets-save-theme");
        string configPath = Path.Combine(root, "framescope-config.json");
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
        config.TelemetrySampleIntervalMs = 1500;
        config.ThemeMode = "dark";
        config.CloseWindowBehavior = "exit";
        config.TrayEnabled = false;
        config.CpuTelemetry = new FrameScopeCpuTelemetryConfig
        {
            CollectPerCoreFrequency = true,
            CollectCpuVoltage = true,
            PerCoreSampleIntervalMs = 1500,
            PerCoreVoltageSampleIntervalMs = 1500,
            VoltageProvider = "disabled"
        };
        FrameScopeConfigStore.Save(configPath, config);

        var bridge = CreateBridge(root);
        string payload = "{\"targets\":[{\"Enabled\":true,\"Name\":\"Target Saved\",\"ProcessName\":\"TargetSaved.exe\",\"SampleIntervalMs\":275,\"ProcessSampleIntervalMs\":275,\"SlowSampleIntervalMs\":1000,\"OpenReportOnComplete\":false}],\"dataRoot\":\"" + Escape(Path.Combine(root, "runs")) + "\",\"openReportOnComplete\":false}";
        var saveResponse = Decode(bridge.HandleJsonMessage(Request("targets-save-theme-1", "targets.save", payload)));
        FrameScopeConfig saved = FrameScopeConfigStore.Load(configPath);

        AssertEqual(true, AsBool(saveResponse, "ok"), "targets theme save ok");
        AssertEqual("dark", saved.ThemeMode, "targets save should preserve theme mode");
        AssertEqual("exit", saved.CloseWindowBehavior, "targets save should preserve close behavior");
        AssertEqual(false, saved.TrayEnabled, "targets save should preserve tray enabled");
        AssertEqual(true, saved.CpuTelemetry.CollectPerCoreFrequency, "targets save should preserve per-core frequency toggle");
        AssertEqual(true, saved.CpuTelemetry.CollectCpuVoltage, "targets save should preserve voltage toggle");
        AssertEqual(1500, saved.TelemetrySampleIntervalMs, "targets save should preserve global telemetry interval");
        AssertEqual(1500, saved.CpuTelemetry.PerCoreSampleIntervalMs, "targets save should preserve per-core interval");
        AssertEqual(1500, saved.CpuTelemetry.PerCoreVoltageSampleIntervalMs, "targets save should preserve per-core voltage interval");
        AssertEqual("disabled", saved.CpuTelemetry.VoltageProvider, "targets save should preserve voltage provider");
    }

    private static FrameScopeWebBridge CreateBridge(string root)
    {
        return CreateBridge(root, null, null);
    }

    private static FrameScopeWebBridge CreateBridge(string root, Action<string> eventSink)
    {
        return CreateBridge(root, eventSink, null);
    }

    private static FrameScopeWebBridge CreateBridge(string root, Action<string> eventSink, IFrameScopeWebBridgeHostAdapter adapter)
    {
        return new FrameScopeWebBridge(new FrameScopeWebBridgeOptions
        {
            Root = root,
            ConfigPath = Path.Combine(root, "framescope-config.json"),
            StatePath = Path.Combine(root, "framescope-watcher-state.json"),
            HistoryPath = Path.Combine(root, "framescope-history.jsonl"),
            HostAdapter = adapter
        }, eventSink);
    }

    private static string CreateReportRun(string root, string game, bool appendHistory)
    {
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
        config.DataRoot = Path.Combine(root, "runs");
        FrameScopeConfigStore.Save(Path.Combine(root, "framescope-config.json"), config);

        string runDir = Path.Combine(config.DataRoot, game, game + "-20260521-120000");
        string charts = Path.Combine(runDir, "charts");
        Directory.CreateDirectory(charts);
        File.WriteAllText(Path.Combine(runDir, "presentmon.csv"),
            "Application,MsBetweenPresents" + Environment.NewLine +
            game.Replace(" ", "") + ".exe,6.9" + Environment.NewLine);
        File.WriteAllText(Path.Combine(runDir, "status.json"), "{\"Phase\":\"done\",\"ReportKind\":\"full\",\"ReportFrameCount\":1}");
        string report = Path.Combine(charts, "framescope-interactive-report.html");
        string data = Path.Combine(charts, "framescope-interactive-data.js");
        File.WriteAllText(report, "<html><body>report</body></html>");
        File.WriteAllText(data, "window.__FRAMESCOPE_REPORT__ = {};");
        File.WriteAllText(Path.Combine(charts, "framescope-interactive-manifest.json"), Json.Serialize(new Dictionary<string, object>
        {
            { "report", report },
            { "data", data },
            { "frames", 1 },
            { "processSamples", 0 },
            { "systemSamples", 0 },
            { "reportKind", "full" }
        }));
        if (appendHistory)
        {
            File.AppendAllText(Path.Combine(root, "framescope-history.jsonl"), HistoryJson(game, game.Replace(" ", "") + ".exe", runDir, Path.Combine(charts, "framescope-interactive-report.html")) + Environment.NewLine);
        }
        return runDir;
    }

    private static string HistoryJson(string game, string processName, string runDir, string reportHtml)
    {
        return Json.Serialize(new Dictionary<string, object>
        {
            { "Time", DateTime.Now.ToString("o") },
            { "Game", game },
            { "ProcessName", processName },
            { "RunDir", runDir },
            { "ReportHtml", reportHtml },
            { "PresentMonCsv", Path.Combine(runDir, "presentmon.csv") },
            { "ProcessCsv", Path.Combine(runDir, "process-samples.csv") },
            { "SystemCsv", Path.Combine(runDir, "system-samples.csv") },
            { "SummaryPath", Path.Combine(runDir, "summary.json") },
            { "MonitorExitCode", 0 }
        });
    }

    private static string FirstReportId(string root)
    {
        var bridge = CreateBridge(root);
        var response = Decode(bridge.HandleJsonMessage(Request("report-id", "reports.list", "{}")));
        var payload = (Dictionary<string, object>)response["payload"];
        var reports = (IList)payload["reports"];
        if (reports.Count == 0) throw new Exception("Expected a report id but reports.list returned none.");
        return AsString((Dictionary<string, object>)reports[0], "reportId");
    }

    private static string CreateTempRoot(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "FrameScopeWebBridgeTests", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string Request(string requestId, string type, string payload)
    {
        return "{\"requestId\":\"" + Escape(requestId) + "\",\"type\":\"" + Escape(type) + "\",\"payload\":" + payload + "}";
    }

    private static Dictionary<string, object> Decode(string json)
    {
        return Json.Deserialize<Dictionary<string, object>>(json);
    }

    private static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string AsString(Dictionary<string, object> map, string key)
    {
        object value;
        return map != null && map.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
    }

    private static bool AsBool(Dictionary<string, object> map, string key)
    {
        object value;
        if (map == null || !map.TryGetValue(key, out value) || value == null) return false;
        return Convert.ToBoolean(value);
    }

    private static int AsInt(Dictionary<string, object> map, string key)
    {
        object value;
        if (map == null || !map.TryGetValue(key, out value) || value == null) return 0;
        return Convert.ToInt32(value);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }

    private sealed class RecordingHostAdapter : IFrameScopeWebBridgeHostAdapter
    {
        public int StartCount;
        public int StopCount;
        public int OpenReportCount;
        public int OpenDirectoryCount;
        public int OpenLogsDirectoryCount;
        public int RegenerateCount;
        public int DiagnosticsCount;
        public string LastReportPath = "";
        public string LastDirectoryPath = "";
        public string LastLogDirectoryPath = "";

        public FrameScopeWebBridgeHostResult StartMonitor(FrameScopeWebBridgeHostContext context)
        {
            StartCount++;
            return FrameScopeWebBridgeHostResult.Success("monitor.started", "started", new Dictionary<string, object> { { "pid", 1234 } });
        }

        public FrameScopeWebBridgeHostResult StopMonitor(FrameScopeWebBridgeHostContext context)
        {
            StopCount++;
            return FrameScopeWebBridgeHostResult.Success("monitor.stopped", "stopped", new Dictionary<string, object> { { "stoppedProcessCount", 1 } });
        }

        public FrameScopeWebBridgeHostResult OpenReport(FrameScopeWebBridgeHostContext context, string reportHtml, string runDir)
        {
            OpenReportCount++;
            LastReportPath = reportHtml;
            return FrameScopeWebBridgeHostResult.Success("report.opened", "opened", new Dictionary<string, object> { { "reportHtml", reportHtml } });
        }

        public FrameScopeWebBridgeHostResult OpenDirectory(FrameScopeWebBridgeHostContext context, string directory)
        {
            OpenDirectoryCount++;
            LastDirectoryPath = directory;
            return FrameScopeWebBridgeHostResult.Success("report.directoryOpened", "opened", new Dictionary<string, object> { { "directory", directory } });
        }

        public FrameScopeWebBridgeHostResult OpenLogsDirectory(FrameScopeWebBridgeHostContext context)
        {
            OpenLogsDirectoryCount++;
            Directory.CreateDirectory(context.LogDirectory);
            LastLogDirectoryPath = context.LogDirectory;
            return FrameScopeWebBridgeHostResult.Success("logs.directoryOpened", "opened", new Dictionary<string, object> { { "directory", context.LogDirectory } });
        }

        public FrameScopeWebBridgeHostResult RegenerateReport(FrameScopeWebBridgeHostContext context, string runDir)
        {
            RegenerateCount++;
            return FrameScopeWebBridgeHostResult.Success("report.regenerated", "regenerated", new Dictionary<string, object> { { "runDir", runDir } });
        }

        public FrameScopeWebBridgeHostResult GenerateDiagnostics(FrameScopeWebBridgeHostContext context, string runDir)
        {
            DiagnosticsCount++;
            return FrameScopeWebBridgeHostResult.Success("diagnostics.generated", "generated", new Dictionary<string, object> { { "runDir", runDir }, { "markdownPath", Path.Combine(context.Root, "diagnostic.md") } });
        }
    }
}
