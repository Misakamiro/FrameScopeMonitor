using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static partial class FrameScopeNativeMonitor
{
    private static int RunWebUi(string[] args)
    {
        using (var host = new FrameScopeWebHostForm(new FrameScopeWebHostOptions
        {
            Root = Root,
            ConfigPath = ConfigPath,
            StatePath = StatePath,
            HistoryPath = HistoryPath,
            HostAdapter = CreateWebBridgeHostAdapter(),
            FrontendPath = ResolveWebFrontendPath(Root),
            Smoke = HasArg(args, "--web-ui-smoke"),
            ReducedMotion = HasArg(args, "--web-ui-reduced-motion"),
            EvidencePath = GetArgValue(args, "--web-ui-evidence", Path.Combine(Root, "artifacts", "webview2-bridge", "smoke.json")),
            ScreenshotPath = GetArgValue(args, "--web-ui-screenshot", Path.Combine(Root, "artifacts", "webview2-bridge", "smoke.png")),
            TimeoutMs = ParseIntArg(args, "--web-ui-timeout-ms", 15000)
        }))
        {
            Application.Run(host);
            return host.ExitCode;
        }
    }

    private static int ParseIntArg(string[] args, string name, int fallback)
    {
        int parsed;
        return int.TryParse(GetArgValue(args, name, fallback.ToString(CultureInfo.InvariantCulture)), out parsed) ? parsed : fallback;
    }

    private static string ResolveWebFrontendPath(string root)
    {
        string installedFrontend = Path.Combine(root, "frontend");
        if (File.Exists(Path.Combine(installedFrontend, "index.html"))) return installedFrontend;

        string sourceFrontend = Path.Combine(root, "src", "frontend", "dist");
        if (File.Exists(Path.Combine(sourceFrontend, "index.html"))) return sourceFrontend;

        return "";
    }

    private static IFrameScopeWebBridgeHostAdapter CreateWebBridgeHostAdapter()
    {
        return new FrameScopeNativeWebBridgeHostAdapter();
    }

    private sealed class FrameScopeNativeWebBridgeHostAdapter : IFrameScopeWebBridgeHostAdapter
    {
        public FrameScopeWebBridgeHostResult StartMonitor(FrameScopeWebBridgeHostContext context)
        {
            try
            {
                FrameScopeConfig config = FrameScopeConfigStore.Load(ConfigPath);
                FrameScopeConfigStore.Save(ConfigPath, config);

                int existingPid;
                if (IsWatcherRunning(out existingPid))
                {
                    return FrameScopeWebBridgeHostResult.Success(
                        "monitor.started",
                        "FrameScope watcher is already running.",
                        new Dictionary<string, object>
                        {
                            { "pid", existingPid },
                            { "alreadyRunning", true }
                        });
                }

                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--watcher --config " + Quote(ConfigPath),
                    WorkingDirectory = Root,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var process = Process.Start(psi);
                int pid = process == null ? 0 : process.Id;
                try
                {
                    if (process != null) process.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch { }
                finally
                {
                    DisposeProcess(process);
                }

                WriteFrameScopeLog("web-bridge-monitor-started pid=" + pid.ToString(CultureInfo.InvariantCulture));
                return FrameScopeWebBridgeHostResult.Success(
                    "monitor.started",
                    pid > 0 ? "FrameScope watcher started." : "FrameScope watcher start was requested.",
                    new Dictionary<string, object>
                    {
                        { "pid", pid },
                        { "alreadyRunning", false }
                    });
            }
            catch (Exception ex)
            {
                WriteFrameScopeLog("web-bridge-monitor-start-failed " + ex.Message);
                return FrameScopeWebBridgeHostResult.Failure("monitor_start_failed", ex.Message, null);
            }
        }

        public FrameScopeWebBridgeHostResult StopMonitor(FrameScopeWebBridgeHostContext context)
        {
            try
            {
                int before = EnumerateFrameScopeBackgroundPids().Count;
                if (before > 0)
                {
                    StopFrameScopeBackgroundProcesses();
                }

                int after = EnumerateFrameScopeBackgroundPids().Count;
                WriteFrameScopeLog("web-bridge-monitor-stopped before=" + before.ToString(CultureInfo.InvariantCulture) + " after=" + after.ToString(CultureInfo.InvariantCulture));
                return FrameScopeWebBridgeHostResult.Success(
                    "monitor.stopped",
                    before == 0 ? "No FrameScope watcher was running." : "FrameScope watcher stopped.",
                    new Dictionary<string, object>
                    {
                        { "stoppedProcessCount", before },
                        { "remainingProcessCount", after }
                    });
            }
            catch (Exception ex)
            {
                WriteFrameScopeLog("web-bridge-monitor-stop-failed " + ex.Message);
                return FrameScopeWebBridgeHostResult.Failure("monitor_stop_failed", ex.Message, null);
            }
        }

        public FrameScopeWebBridgeHostResult OpenReport(FrameScopeWebBridgeHostContext context, string reportHtml, string runDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reportHtml) || !File.Exists(reportHtml))
                {
                    return FrameScopeWebBridgeHostResult.Failure("report_missing", "Report HTML does not exist.", null);
                }

                if (!TryOpenPath(reportHtml))
                {
                    return FrameScopeWebBridgeHostResult.Failure("report_open_failed", "The report could not be opened by the host.", null);
                }

                MarkReportOpened(runDir, ReadStatusDictionary(runDir));
                return FrameScopeWebBridgeHostResult.Success("report.opened", "Report opened.", new Dictionary<string, object>
                {
                    { "reportHtml", reportHtml },
                    { "runDir", runDir }
                });
            }
            catch (Exception ex)
            {
                return FrameScopeWebBridgeHostResult.Failure("report_open_failed", ex.Message, null);
            }
        }

        public FrameScopeWebBridgeHostResult OpenDirectory(FrameScopeWebBridgeHostContext context, string directory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    return FrameScopeWebBridgeHostResult.Failure("directory_missing", "Directory does not exist.", null);
                }

                if (!TryOpenPath(directory))
                {
                    return FrameScopeWebBridgeHostResult.Failure("directory_open_failed", "The directory could not be opened by the host.", null);
                }

                return FrameScopeWebBridgeHostResult.Success("report.directoryOpened", "Directory opened.", new Dictionary<string, object>
                {
                    { "directory", directory }
                });
            }
            catch (Exception ex)
            {
                return FrameScopeWebBridgeHostResult.Failure("directory_open_failed", ex.Message, null);
            }
        }

        public FrameScopeWebBridgeHostResult RegenerateReport(FrameScopeWebBridgeHostContext context, string runDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(runDir) || !Directory.Exists(runDir))
                {
                    return FrameScopeWebBridgeHostResult.Failure("run_directory_missing", "Run directory does not exist.", null);
                }

                if (!HasAnyMonitorCsv(runDir))
                {
                    return FrameScopeWebBridgeHostResult.Failure("missing_monitor_data", "Run directory has no monitor CSV data.", null);
                }

                var status = ReadStatusDictionary(runDir);
                var result = RunReportGeneration(runDir);
                UpdateStatusAfterReportGeneration(runDir, status, result, StatusInt(status, "ExitCode", 0));
                if (result.ExitCode != 0)
                {
                    return FrameScopeWebBridgeHostResult.Failure("report_regenerate_failed", result.Error ?? "Report generation failed.", BuildReportGenerationPayload(result));
                }

                return FrameScopeWebBridgeHostResult.Success("report.regenerated", "Report regenerated.", BuildReportGenerationPayload(result));
            }
            catch (Exception ex)
            {
                return FrameScopeWebBridgeHostResult.Failure("report_regenerate_failed", ex.Message, null);
            }
        }

        public FrameScopeWebBridgeHostResult GenerateDiagnostics(FrameScopeWebBridgeHostContext context, string runDir)
        {
            try
            {
                FrameScopeConfig config = FrameScopeConfigStore.Load(ConfigPath);
                var result = FrameScopeDiagnostics.GenerateReport(config, Root, ResolveDataRoot(config.DataRoot), "web-bridge", runDir ?? "");
                return FrameScopeWebBridgeHostResult.Success("diagnostics.generated", "Diagnostic report generated.", new Dictionary<string, object>
                {
                    { "directoryPath", result.DirectoryPath },
                    { "markdownPath", result.MarkdownPath },
                    { "jsonPath", result.JsonPath },
                    { "runDir", runDir ?? "" }
                });
            }
            catch (Exception ex)
            {
                return FrameScopeWebBridgeHostResult.Failure("diagnostics_generate_failed", ex.Message, null);
            }
        }

        private static Dictionary<string, object> BuildReportGenerationPayload(ReportGenerationResult result)
        {
            if (result == null) return new Dictionary<string, object>();
            return new Dictionary<string, object>
            {
                { "exitCode", result.ExitCode },
                { "reportHtml", result.ReportHtml ?? "" },
                { "logPath", result.LogPath ?? "" },
                { "progressPath", result.ProgressPath ?? "" },
                { "error", result.Error ?? "" },
                { "frameCount", result.FrameCount },
                { "processSampleCount", result.ProcessSampleCount },
                { "systemSampleCount", result.SystemSampleCount },
                { "hasFrameData", result.HasFrameData },
                { "reportKind", result.ReportKind ?? "" }
            };
        }
    }
}

internal sealed class FrameScopeWebHostOptions
{
    public FrameScopeWebHostOptions()
    {
        Root = "";
        ConfigPath = "";
        StatePath = "";
        HistoryPath = "";
        FrontendPath = "";
        EvidencePath = "";
        ScreenshotPath = "";
        TimeoutMs = 15000;
    }

    public string Root { get; set; }
    public string ConfigPath { get; set; }
    public string StatePath { get; set; }
    public string HistoryPath { get; set; }
    public IFrameScopeWebBridgeHostAdapter HostAdapter { get; set; }
    public string FrontendPath { get; set; }
    public bool Smoke { get; set; }
    public bool ReducedMotion { get; set; }
    public string EvidencePath { get; set; }
    public string ScreenshotPath { get; set; }
    public int TimeoutMs { get; set; }
}

internal sealed class FrameScopeWebHostForm : Form
{
    private static readonly string[] SmokeExternalProcessNames = new[]
    {
        "msedge",
        "chrome",
        "firefox",
        "brave",
        "opera",
        "iexplore",
        "360se",
        "SogouExplorer",
        "explorer"
    };

    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private readonly FrameScopeWebHostOptions options;
    private readonly WebView2 webView;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly List<string> messages = new List<string>();
    private readonly FrameScopeWebBridge bridge;
    private System.Windows.Forms.Timer timeoutTimer;
    private bool pageLoaded;
    private bool pageReady;
    private bool finishStarted;
    private bool usingReactFrontend;
    private bool reactSmokeStarted;
    private Dictionary<string, object> smokePayload = new Dictionary<string, object>();
    private bool smokeConfigSnapshotCaptured;
    private bool smokeConfigExisted;
    private bool smokeConfigRestoreNeeded;
    private string smokeOriginalConfigText = "";

    public FrameScopeWebHostForm(FrameScopeWebHostOptions options)
    {
        this.options = options ?? new FrameScopeWebHostOptions();
        ExitCode = this.options.Smoke ? 2 : 0;
        Text = "FrameScope Monitor Web UI";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = !this.options.Smoke;

        webView = new WebView2
        {
            Dock = DockStyle.Fill
        };
        Controls.Add(webView);

        bridge = new FrameScopeWebBridge(new FrameScopeWebBridgeOptions
        {
            Root = this.options.Root,
            ConfigPath = this.options.ConfigPath,
            StatePath = this.options.StatePath,
            HistoryPath = this.options.HistoryPath,
            HostAdapter = this.options.HostAdapter
        }, PostBridgeEvent);
    }

    public int ExitCode { get; private set; }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            StartTimeout();
            string userDataFolder = Path.Combine(Path.GetTempPath(), "FrameScopeMonitorWebView2", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(userDataFolder);
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(environment);

            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            await ApplyReducedMotionIfRequestedAsync();

            if (!string.IsNullOrWhiteSpace(options.FrontendPath) &&
                File.Exists(Path.Combine(options.FrontendPath, "index.html")))
            {
                usingReactFrontend = true;
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.framescope.local",
                    options.FrontendPath,
                    CoreWebView2HostResourceAccessKind.DenyCors);
                Log("host:navigate react-web-ui " + options.FrontendPath + " smoke=" + options.Smoke.ToString(CultureInfo.InvariantCulture));
                webView.CoreWebView2.Navigate("https://app.framescope.local/index.html");
            }
            else
            {
                Log("host:navigate embedded-web-ui smoke=" + options.Smoke.ToString(CultureInfo.InvariantCulture));
                webView.NavigateToString(BuildEmbeddedHtml(options.Smoke));
            }
        }
        catch (Exception ex)
        {
            Log("host:error " + ex.GetType().Name + " " + ex.Message);
            QueueFinish(false, ex.Message);
        }
    }

    private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        string uri = e.Uri ?? "";
        if (uri.Length == 0 ||
            uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("https://app.framescope.local/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        e.Cancel = true;
        Log("host:blocked-navigation " + uri);
    }

    private async System.Threading.Tasks.Task ApplyReducedMotionIfRequestedAsync()
    {
        if (!options.ReducedMotion || webView.CoreWebView2 == null) return;
        string payload = "{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}";
        await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", payload);
        Log("host:reduced-motion emulated");
    }

    private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        pageLoaded = e.IsSuccess;
        Log("host:navigation-completed success=" + e.IsSuccess.ToString(CultureInfo.InvariantCulture) + " status=" + e.HttpStatusCode.ToString(CultureInfo.InvariantCulture));
        if (!e.IsSuccess)
        {
            await FinishAsync(false, "WebView2 navigation failed: " + e.WebErrorStatus);
        }
    }

    private async void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string messageJson = e.WebMessageAsJson;
        Log("js->host " + messageJson);
        try
        {
            Dictionary<string, object> message = json.Deserialize<Dictionary<string, object>>(messageJson);
            string type = ReadString(message, "type");
            string requestId = ReadString(message, "requestId");

            if (string.Equals(type, "webview-ready", StringComparison.Ordinal))
            {
                pageReady = true;
                PostJson(new Dictionary<string, object>
                {
                    { "type", "event.status" },
                    { "payload", new Dictionary<string, object>
                        {
                            { "status", "webview.ready" },
                            { "smoke", options.Smoke }
                        }
                    }
                });
                if (options.Smoke && usingReactFrontend)
                {
                    StartReactSmoke();
                }
                return;
            }

            if (string.Equals(type, "smoke-complete", StringComparison.Ordinal))
            {
                smokePayload = ReadDictionary(message, "payload");
                await FinishAsync(ReadBool(smokePayload, "success", false), ReadString(smokePayload, "error"));
                return;
            }

            if (!string.IsNullOrWhiteSpace(requestId))
            {
                string responseJson = bridge.HandleJsonMessage(messageJson);
                PostJson(responseJson);
                return;
            }

            PostJson(bridge.HandleJsonMessage(messageJson));
        }
        catch (Exception ex)
        {
            Log("host:web-message-error " + ex.Message);
            if (options.Smoke)
            {
                QueueFinish(false, "Web message failed: " + ex.Message);
            }
        }
    }

    private async void StartReactSmoke()
    {
        if (reactSmokeStarted) return;
        reactSmokeStarted = true;

        try
        {
            await System.Threading.Tasks.Task.Delay(1200);
            bool overviewLoaded = await WaitForScriptBoolAsync("document.querySelector('[data-smoke-page=\"overview\"]') !== null", 8000);
            await CaptureSmokePreviewAsync("overview");

            await ExecuteScriptSafeAsync("var el=document.querySelector('[data-smoke-nav=\"targets\"]'); if(el){el.click();}");
            await CaptureSmokePreviewAsync("transition-overview-targets-01");
            await System.Threading.Tasks.Task.Delay(80);
            await CaptureSmokePreviewAsync("transition-overview-targets-02");
            bool targetsLoaded = await WaitForScriptBoolAsync("document.querySelector('[data-smoke-page=\"targets\"]') !== null", 5000);
            await CaptureSmokePreviewAsync("transition-overview-targets-03");

            await ExecuteScriptSafeAsync("var refresh=document.querySelector('[data-smoke-action=\"refresh-processes\"]'); if(refresh){refresh.click();}");
            await System.Threading.Tasks.Task.Delay(100);
            await CaptureSmokePreviewAsync("targets-loading");
            bool processRefreshObserved = await WaitForScriptBoolAsync(
                "document.body && document.body.innerText && document.body.innerText.indexOf('Process refresh completed') >= 0",
                12000);
            await CaptureSmokePreviewAsync("targets-result");

            await ExecuteScriptSafeAsync("var reports=document.querySelector('[data-smoke-nav=\"reports\"]'); if(reports){reports.click();}");
            await CaptureSmokePreviewAsync("transition-targets-reports-01");
            await System.Threading.Tasks.Task.Delay(80);
            await CaptureSmokePreviewAsync("transition-targets-reports-02");
            bool reportsLoaded = await WaitForScriptBoolAsync("document.querySelector('[data-smoke-page=\"reports\"]') !== null", 5000);
            await CaptureSmokePreviewAsync("transition-targets-reports-03");
            await CaptureSmokePreviewAsync("reports");
            Dictionary<string, object> reportLiveActionSmoke = reportsLoaded
                ? await RunReportsLiveActionSmokeAsync()
                : new Dictionary<string, object> { { "success", false }, { "error", "Reports page was not loaded before live action smoke." } };

            await ExecuteScriptSafeAsync("var settings=document.querySelector('[data-smoke-nav=\"settings\"]'); if(settings){settings.click();}");
            await CaptureSmokePreviewAsync("transition-reports-settings-01");
            await System.Threading.Tasks.Task.Delay(80);
            await CaptureSmokePreviewAsync("transition-reports-settings-02");
            bool settingsLoaded = await WaitForScriptBoolAsync("document.querySelector('[data-smoke-page=\"settings\"]') !== null", 5000);
            await CaptureSmokePreviewAsync("transition-reports-settings-03");
            await CaptureSmokePreviewAsync("settings-clean");

            CaptureSmokeConfigSnapshot();
            await ExecuteScriptSafeAsync("var input=document.querySelector('[data-smoke-field=\"poll-interval\"]'); if(input){window.__framescopeSmokeOriginalPoll=input.value; var next=String((parseInt(input.value,10)||1000)+1); var setter=Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype,'value').set; setter.call(input,next); input.dispatchEvent(new Event('input',{bubbles:true})); input.dispatchEvent(new Event('change',{bubbles:true}));}");
            bool configDirtyObserved = await WaitForScriptBoolAsync("document.body && document.body.innerText && document.body.innerText.indexOf('dirty') >= 0 && document.querySelector('[data-smoke-action=\"save-config\"]') && !document.querySelector('[data-smoke-action=\"save-config\"]').disabled", 5000);
            await CaptureSmokePreviewAsync("settings-dirty");

            smokeConfigRestoreNeeded = true;
            await ExecuteScriptSafeAsync("var save=document.querySelector('[data-smoke-action=\"save-config\"]'); if(save){save.click();}");
            bool configSavingObserved = await WaitForScriptBoolAsync("document.body && document.body.innerText && document.body.innerText.indexOf('Saving FrameScope config.') >= 0", 3000);
            await CaptureSmokePreviewAsync("settings-saving");
            bool configSaveSuccessObserved = await WaitForScriptBoolAsync("document.body && document.body.innerText && document.body.innerText.indexOf('Config saved.') >= 0", 8000);
            await CaptureSmokePreviewAsync("settings-saved");
            Dictionary<string, object> bridgeExtensionSmoke = await RunBridgeExtensionSmokeAsync();

            await ExecuteScriptSafeAsync("var targetNav=document.querySelector('[data-smoke-nav=\"targets\"]'); if(targetNav){targetNav.click();}");
            bool targetsReloaded = await WaitForScriptBoolAsync("document.querySelector('[data-smoke-page=\"targets\"]') !== null", 5000);
            await ExecuteScriptSafeAsync("var about=document.querySelector('[data-smoke-nav=\"about\"]'); if(about){about.click();}");
            await CaptureSmokePreviewAsync("transition-targets-about-01");
            await System.Threading.Tasks.Task.Delay(80);
            await CaptureSmokePreviewAsync("transition-targets-about-02");
            bool aboutLoaded = await WaitForScriptBoolAsync("document.querySelector('[data-smoke-page=\"about\"]') !== null", 5000);
            await CaptureSmokePreviewAsync("transition-targets-about-03");
            await CaptureSmokePreviewAsync("about");

            bool reportLiveActionSuccess = ReadBool(reportLiveActionSmoke, "success", false);
            bool smokeSuccess = overviewLoaded && targetsLoaded && processRefreshObserved && reportsLoaded && reportLiveActionSuccess && aboutLoaded &&
                settingsLoaded && targetsReloaded && configDirtyObserved && configSavingObserved && configSaveSuccessObserved;
            string smokeError = "";
            if (!overviewLoaded) smokeError = "React overview page was not observed before timeout.";
            else if (!targetsLoaded) smokeError = "React targets page was not observed before timeout.";
            else if (!processRefreshObserved) smokeError = "React UI loaded, but process refresh result was not observed before timeout.";
            else if (!reportsLoaded) smokeError = "React reports page was not observed before timeout.";
            else if (!reportLiveActionSuccess) smokeError = "Reports live UI action smoke did not complete open/openDirectory/regenerate successfully.";
            else if (!settingsLoaded) smokeError = "React settings page was not observed before timeout.";
            else if (!configDirtyObserved) smokeError = "Settings edit did not produce a dirty state.";
            else if (!configSavingObserved) smokeError = "Settings save did not expose a saving state.";
            else if (!configSaveSuccessObserved) smokeError = "Settings save success was not observed before timeout.";
            else if (!targetsReloaded) smokeError = "React targets page was not observed before about transition.";
            else if (!aboutLoaded) smokeError = "React about page was not observed before timeout.";

            smokePayload = new Dictionary<string, object>
            {
                { "success", smokeSuccess },
                { "frontendPath", options.FrontendPath },
                { "reducedMotion", options.ReducedMotion },
                { "reactOverviewLoaded", overviewLoaded },
                { "reactTargetsLoaded", targetsLoaded },
                { "processRefreshObserved", processRefreshObserved },
                { "reactReportsLoaded", reportsLoaded },
                { "reportLiveActionSmoke", reportLiveActionSmoke },
                { "reactAboutLoaded", aboutLoaded },
                { "reactSettingsLoaded", settingsLoaded },
                { "reactTargetsReloaded", targetsReloaded },
                { "configDirtyObserved", configDirtyObserved },
                { "configSavingObserved", configSavingObserved },
                { "configSaveSuccessObserved", configSaveSuccessObserved },
                { "bridgeExtensionSmoke", bridgeExtensionSmoke }
            };

            bool bridgeExtensionSuccess = ReadBool(bridgeExtensionSmoke, "success", false);
            smokeSuccess = smokeSuccess && bridgeExtensionSuccess;
            if (string.IsNullOrWhiteSpace(smokeError) && !bridgeExtensionSuccess)
            {
                smokeError = "Bridge extension smoke requests did not all complete successfully.";
            }
            await FinishAsync(smokeSuccess, smokeError);
        }
        catch (Exception ex)
        {
            QueueFinish(false, "React smoke failed: " + ex.Message);
        }
    }

    private async System.Threading.Tasks.Task<Dictionary<string, object>> RunReportsLiveActionSmokeAsync()
    {
        var result = new Dictionary<string, object>();
        Dictionary<int, DateTime> externalProcessBaseline = CaptureSmokeExternalProcessSnapshot();

        try
        {
            await InitializeBridgeSmokeProbeAsync();
            await ExecuteScriptSafeAsync("window.__framescopeReportClickEvidence = { selected: false, startedAt: new Date().toISOString() }; var refresh=document.querySelector('[data-smoke-action=\"refresh-reports\"]'); if(refresh){refresh.click();}");

            bool reportsListClickOk = await WaitForScriptBoolAsync(
                "window.__framescopeBridgeSmoke && window.__framescopeBridgeSmoke.responses && Object.keys(window.__framescopeBridgeSmoke.responses).some(function(key){ var response = window.__framescopeBridgeSmoke.responses[key]; return response && response.ok === true && response.payload && response.payload.status === 'loaded' && response.payload.reports && response.payload.reports.length > 0; })",
                10000);

            await ExecuteScriptSafeAsync(@"
var smoke = window.__framescopeBridgeSmoke || {};
var responses = smoke.responses || {};
var best = null;
var fallback = null;
Object.keys(responses).forEach(function(key){
  var response = responses[key];
  if(!response || response.ok !== true || !response.payload || !response.payload.reports) return;
  var reports = response.payload.reports;
  for(var i = 0; i < reports.length; i++){
    var report = reports[i] || {};
    if(!report.reportId || report.canOpenReport !== true || report.canOpenDirectory !== true || report.canRegenerate !== true) continue;
    var candidate = {
      selected: true,
      source: 'reports.list',
      responseRequestId: key,
      reportIndex: i,
      reportId: String(report.reportId || ''),
      game: String(report.game || ''),
      processName: String(report.processName || ''),
      runDir: String(report.runDir || ''),
      reportHtml: String(report.reportHtml || ''),
      reportKind: String(report.reportKind || ''),
      frameCount: Number(report.frameCount || 0),
      hasFrameData: report.hasFrameData === true,
      reportSizeBytes: Number(report.reportSizeBytes || 0)
    };
    if(!fallback) fallback = candidate;
    if(candidate.hasFrameData && candidate.frameCount > 0 && (!best || candidate.frameCount < best.frameCount)){
      best = candidate;
    }
  }
});
window.__framescopeReportClickEvidence = best || fallback || {
  selected: false,
  error: 'No backend-returned reportId was eligible for reports.open/openDirectory/regenerate.'
};");

            bool reportIdCaptured = await WaitForScriptBoolAsync(
                "window.__framescopeReportClickEvidence && window.__framescopeReportClickEvidence.selected === true && !!window.__framescopeReportClickEvidence.reportId",
                1000);
            await CaptureSmokePreviewAsync("reports-list-after-refresh");

            await ExecuteScriptSafeAsync(@"
var evidence = window.__framescopeReportClickEvidence || {};
var openButton = document.querySelector('[data-smoke-action=""open-report-' + evidence.reportIndex + '""]');
if(openButton && !openButton.disabled){
  openButton.scrollIntoView({ block: 'center', inline: 'center' });
  evidence.openClickedAt = new Date().toISOString();
  openButton.click();
} else {
  evidence.openClickError = openButton ? 'open button disabled' : 'open button not found';
}");
            bool reportOpenClicked = await WaitForScriptBoolAsync("window.__framescopeReportClickEvidence && !!window.__framescopeReportClickEvidence.openClickedAt", 1000);
            await System.Threading.Tasks.Task.Delay(150);
            await CaptureSmokePreviewAsync("reports-open-clicked");
            bool reportOpenClickOk = await WaitForScriptBoolAsync(
                "window.__framescopeReportClickEvidence && window.__framescopeBridgeSmoke && Object.keys(window.__framescopeBridgeSmoke.responses).some(function(key){ var response = window.__framescopeBridgeSmoke.responses[key]; return response && response.ok === true && response.payload && response.payload.status === 'opened' && response.payload.reportId === window.__framescopeReportClickEvidence.reportId; })",
                10000);
            await CaptureSmokePreviewAsync("reports-open-success");

            await ExecuteScriptSafeAsync(@"
var evidence = window.__framescopeReportClickEvidence || {};
var directoryButton = document.querySelector('[data-smoke-action=""open-directory-' + evidence.reportIndex + '""]');
if(directoryButton && !directoryButton.disabled){
  directoryButton.scrollIntoView({ block: 'center', inline: 'center' });
  evidence.openDirectoryClickedAt = new Date().toISOString();
  directoryButton.click();
} else {
  evidence.openDirectoryClickError = directoryButton ? 'open directory button disabled' : 'open directory button not found';
}");
            bool reportOpenDirectoryClicked = await WaitForScriptBoolAsync("window.__framescopeReportClickEvidence && !!window.__framescopeReportClickEvidence.openDirectoryClickedAt", 1000);
            await System.Threading.Tasks.Task.Delay(150);
            await CaptureSmokePreviewAsync("reports-open-directory-clicked");
            bool reportOpenDirectoryClickOk = await WaitForScriptBoolAsync(
                "window.__framescopeReportClickEvidence && window.__framescopeBridgeSmoke && Object.keys(window.__framescopeBridgeSmoke.responses).some(function(key){ var response = window.__framescopeBridgeSmoke.responses[key]; return response && response.ok === true && response.payload && response.payload.status === 'directory_opened' && response.payload.reportId === window.__framescopeReportClickEvidence.reportId; })",
                10000);
            await CaptureSmokePreviewAsync("reports-open-directory-success");

            await ExecuteScriptSafeAsync(@"
var evidence = window.__framescopeReportClickEvidence || {};
var regenerateButton = document.querySelector('[data-smoke-action=""regenerate-report-' + evidence.reportIndex + '""]');
if(regenerateButton && !regenerateButton.disabled){
  regenerateButton.scrollIntoView({ block: 'center', inline: 'center' });
  evidence.regenerateClickedAt = new Date().toISOString();
  regenerateButton.click();
} else {
  evidence.regenerateClickError = regenerateButton ? 'regenerate button disabled' : 'regenerate button not found';
}");
            bool reportRegenerateClicked = await WaitForScriptBoolAsync("window.__framescopeReportClickEvidence && !!window.__framescopeReportClickEvidence.regenerateClickedAt", 1000);
            await System.Threading.Tasks.Task.Delay(150);
            await CaptureSmokePreviewAsync("reports-regenerate-clicked");
            bool reportRegenerateClickAccepted = await WaitForScriptBoolAsync(
                "window.__framescopeBridgeSmoke && Object.keys(window.__framescopeBridgeSmoke.responses).some(function(key){ var response = window.__framescopeBridgeSmoke.responses[key]; return response && response.ok === true && response.payload && response.payload.status === 'accepted' && response.payload.action === 'reports.regenerate'; })",
                10000);
            bool reportRegenerateClickInFlight = await WaitForScriptBoolAsync(
                "window.__framescopeBridgeSmoke && window.__framescopeBridgeSmoke.events && window.__framescopeBridgeSmoke.events.some(function(event){ return event.type === 'event.reportProgress' && event.payload && event.payload.action === 'reports.regenerate' && event.payload.status === 'report.regenerating'; })",
                5000);
            await CaptureSmokePreviewAsync("reports-regenerate-accepted");
            bool reportRegenerateClickCompleted = await WaitForScriptBoolAsync(
                "window.__framescopeReportClickEvidence && window.__framescopeBridgeSmoke && window.__framescopeBridgeSmoke.events && window.__framescopeBridgeSmoke.events.some(function(event){ return event.type === 'event.reportProgress' && event.payload && event.payload.action === 'reports.regenerate' && event.payload.status === 'completed' && event.payload.reportId === window.__framescopeReportClickEvidence.reportId; })",
                90000);
            await CaptureSmokePreviewAsync("reports-regenerate-success");

            await ExecuteScriptSafeAsync(@"
var evidence = window.__framescopeReportClickEvidence || {};
var smoke = window.__framescopeBridgeSmoke || {};
var responses = smoke.responses || {};
var events = smoke.events || [];
function findResponse(predicate){
  var keys = Object.keys(responses);
  for(var i = keys.length - 1; i >= 0; i--){
    var key = keys[i];
    var response = responses[key];
    if(predicate(response)){
      return { requestId: key, response: response };
    }
  }
  return null;
}
function findEvent(predicate){
  for(var i = events.length - 1; i >= 0; i--){
    var event = events[i];
    if(predicate(event)) return event;
  }
  return null;
}
evidence.openResponse = findResponse(function(response){
  return response && response.ok === true && response.payload && response.payload.status === 'opened' && response.payload.reportId === evidence.reportId;
});
evidence.openDirectoryResponse = findResponse(function(response){
  return response && response.ok === true && response.payload && response.payload.status === 'directory_opened' && response.payload.reportId === evidence.reportId;
});
evidence.regenerateAcceptedResponse = findResponse(function(response){
  return response && response.ok === true && response.payload && response.payload.status === 'accepted' && response.payload.action === 'reports.regenerate';
});
evidence.regenerateInFlightEvent = findEvent(function(event){
  return event.type === 'event.reportProgress' && event.payload && event.payload.action === 'reports.regenerate' && event.payload.status === 'report.regenerating';
});
evidence.regenerateCompletedEvent = findEvent(function(event){
  return event.type === 'event.reportProgress' && event.payload && event.payload.action === 'reports.regenerate' && event.payload.status === 'completed' && event.payload.reportId === evidence.reportId;
});
window.__framescopeReportClickEvidence = evidence;");

            Dictionary<string, object> reportEvidence = await EvaluateScriptJsonDictionaryAsync("window.__framescopeReportClickEvidence || {}");
            bool success = reportsListClickOk &&
                reportIdCaptured &&
                reportOpenClicked &&
                reportOpenClickOk &&
                reportOpenDirectoryClicked &&
                reportOpenDirectoryClickOk &&
                reportRegenerateClicked &&
                reportRegenerateClickAccepted &&
                reportRegenerateClickInFlight &&
                reportRegenerateClickCompleted;

            result["success"] = success;
            result["reportsListClickOk"] = reportsListClickOk;
            result["reportIdCaptured"] = reportIdCaptured;
            result["reportOpenClicked"] = reportOpenClicked;
            result["reportOpenClickOk"] = reportOpenClickOk;
            result["reportOpenDirectoryClicked"] = reportOpenDirectoryClicked;
            result["reportOpenDirectoryClickOk"] = reportOpenDirectoryClickOk;
            result["reportRegenerateClicked"] = reportRegenerateClicked;
            result["reportRegenerateClickAccepted"] = reportRegenerateClickAccepted;
            result["reportRegenerateClickInFlight"] = reportRegenerateClickInFlight;
            result["reportRegenerateClickCompleted"] = reportRegenerateClickCompleted;
            result["selectedReportId"] = ReadString(reportEvidence, "reportId");
            result["selectedReportEvidence"] = reportEvidence;
            result["error"] = success ? "" : "One or more Reports UI live action checks failed.";
        }
        catch (Exception ex)
        {
            result["success"] = false;
            result["error"] = ex.Message;
        }
        finally
        {
            List<Dictionary<string, object>> newExternalProcesses = FindSmokeExternalProcessesStartedAfter(externalProcessBaseline);
            result["externalProcessBaselineCount"] = externalProcessBaseline.Count;
            result["newExternalProcesses"] = newExternalProcesses;
            result["externalProcessCleanup"] = CleanupSmokeExternalProcesses(newExternalProcesses);
        }

        return result;
    }

    private async System.Threading.Tasks.Task<Dictionary<string, object>> RunBridgeExtensionSmokeAsync()
    {
        var result = new Dictionary<string, object>();
        bool monitorStartAccepted = false;
        bool monitorNeedsCleanup = false;
        try
        {
            await InitializeBridgeSmokeProbeAsync();

            bool reportsListOk = await SendBridgeSmokeRequestAndWaitAsync("smoke-reports-list", "reports.list", "{}", "ok", 8000);
            bool targetsGetOk = await SendBridgeSmokeRequestAndWaitAsync("smoke-targets-get", "targets.get", "{}", "ok", 8000);
            bool reportOpenPathRejected = await SendBridgeSmokeRequestAndWaitAsync("smoke-report-open-path", "reports.open", "{\"path\":\"C:\\\\Windows\\\\not-allowed.html\"}", "path_not_allowed", 8000);
            bool targetsSavePathRejected = await SendBridgeSmokeRequestAndWaitAsync("smoke-targets-save-path", "targets.save", "{\"path\":\"C:\\\\Windows\\\\framescope-config.json\",\"targets\":[]}", "path_not_allowed", 8000);
            bool reportRegenerateMissingRejected = await SendBridgeSmokeRequestAndWaitAsync("smoke-report-regenerate-missing", "reports.regenerate", "{\"reportId\":\"missing-report-id\"}", "report_not_found", 8000);

            bool diagnosticsAccepted = await SendBridgeSmokeRequestAndWaitAsync("smoke-diagnostics-generate", "diagnostics.generate", "{}", "accepted", 8000);
            bool diagnosticsCompleted = await WaitForBridgeSmokeEventAsync("event.reportProgress", "smoke-diagnostics-generate", "completed", 20000);

            monitorStartAccepted = await SendBridgeSmokeRequestAndWaitAsync("smoke-monitor-start", "monitor.start", "{}", "accepted", 8000);
            monitorNeedsCleanup = monitorStartAccepted;
            bool monitorStarted = await WaitForBridgeSmokeEventAsync("event.status", "smoke-monitor-start", "monitor.started", 12000);
            bool monitorStopAccepted = await SendBridgeSmokeRequestAndWaitAsync("smoke-monitor-stop", "monitor.stop", "{}", "accepted", 8000);
            bool monitorStopped = await WaitForBridgeSmokeEventAsync("event.status", "smoke-monitor-stop", "monitor.stopped", 12000);
            if (monitorStopped) monitorNeedsCleanup = false;

            bool success = reportsListOk &&
                targetsGetOk &&
                reportOpenPathRejected &&
                targetsSavePathRejected &&
                reportRegenerateMissingRejected &&
                diagnosticsAccepted &&
                diagnosticsCompleted &&
                monitorStartAccepted &&
                monitorStarted &&
                monitorStopAccepted &&
                monitorStopped;

            result["success"] = success;
            result["reportsListOk"] = reportsListOk;
            result["targetsGetOk"] = targetsGetOk;
            result["reportOpenPathRejected"] = reportOpenPathRejected;
            result["targetsSavePathRejected"] = targetsSavePathRejected;
            result["reportRegenerateMissingRejected"] = reportRegenerateMissingRejected;
            result["diagnosticsAccepted"] = diagnosticsAccepted;
            result["diagnosticsCompleted"] = diagnosticsCompleted;
            result["monitorStartAccepted"] = monitorStartAccepted;
            result["monitorStarted"] = monitorStarted;
            result["monitorStopAccepted"] = monitorStopAccepted;
            result["monitorStopped"] = monitorStopped;
        }
        catch (Exception ex)
        {
            result["success"] = false;
            result["error"] = ex.Message;
        }

        if (monitorNeedsCleanup)
        {
            try
            {
                await SendBridgeSmokeRequestAndWaitAsync("smoke-monitor-stop-finally", "monitor.stop", "{}", "accepted", 5000);
                await WaitForBridgeSmokeEventAsync("event.status", "smoke-monitor-stop-finally", "monitor.stopped", 8000);
            }
            catch
            {
            }
        }

        return result;
    }

    private async System.Threading.Tasks.Task InitializeBridgeSmokeProbeAsync()
    {
        await ExecuteScriptSafeAsync(@"
if(!window.__framescopeBridgeSmoke){
  window.__framescopeBridgeSmoke = { requests: [], responses: {}, events: [] };
  try {
    if(window.chrome && window.chrome.webview && !window.chrome.webview.__framescopeSmokePostWrapped){
      var originalPostMessage = window.chrome.webview.postMessage.bind(window.chrome.webview);
      window.chrome.webview.postMessage = function(message){
        try {
          if(message && message.requestId){
            window.__framescopeBridgeSmoke.requests.push({
              requestId: String(message.requestId || ''),
              type: String(message.type || ''),
              payload: message.payload || {},
              sentAt: new Date().toISOString()
            });
          }
        } catch(e) {}
        return originalPostMessage(message);
      };
      window.chrome.webview.__framescopeSmokePostWrapped = true;
      window.__framescopeBridgeSmoke.postMessageWrapped = true;
    }
  } catch(e) {
    window.__framescopeBridgeSmoke.postMessageWrapError = String(e);
  }
  window.chrome.webview.addEventListener('message', function(event){
    var message = event.data || {};
    if(message.type === 'response'){
      window.__framescopeBridgeSmoke.responses[message.requestId] = message;
    } else if(message.type && String(message.type).indexOf('event.') === 0){
      window.__framescopeBridgeSmoke.events.push(message);
    }
  });
}");
    }

    private async System.Threading.Tasks.Task<bool> SendBridgeSmokeRequestAndWaitAsync(string requestId, string type, string payloadJson, string expectation, int timeoutMs)
    {
        var envelope = new Dictionary<string, object>
        {
            { "requestId", requestId },
            { "type", type },
            { "payload", json.Deserialize<Dictionary<string, object>>(payloadJson) ?? new Dictionary<string, object>() }
        };
        await ExecuteScriptSafeAsync("window.chrome.webview.postMessage(" + json.Serialize(envelope) + ");");
        string condition;
        if (string.Equals(expectation, "ok", StringComparison.OrdinalIgnoreCase))
        {
            condition = "window.__framescopeBridgeSmoke && window.__framescopeBridgeSmoke.responses['" + requestId + "'] && window.__framescopeBridgeSmoke.responses['" + requestId + "'].ok === true";
        }
        else if (string.Equals(expectation, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            condition = "window.__framescopeBridgeSmoke && window.__framescopeBridgeSmoke.responses['" + requestId + "'] && window.__framescopeBridgeSmoke.responses['" + requestId + "'].ok === true && window.__framescopeBridgeSmoke.responses['" + requestId + "'].payload && window.__framescopeBridgeSmoke.responses['" + requestId + "'].payload.status === 'accepted'";
        }
        else
        {
            condition = "window.__framescopeBridgeSmoke && window.__framescopeBridgeSmoke.responses['" + requestId + "'] && window.__framescopeBridgeSmoke.responses['" + requestId + "'].ok === false && window.__framescopeBridgeSmoke.responses['" + requestId + "'].error && window.__framescopeBridgeSmoke.responses['" + requestId + "'].error.code === '" + expectation + "'";
        }
        return await WaitForScriptBoolAsync(condition, timeoutMs);
    }

    private async System.Threading.Tasks.Task<bool> WaitForBridgeSmokeEventAsync(string eventType, string requestId, string status, int timeoutMs)
    {
        string condition = "window.__framescopeBridgeSmoke && window.__framescopeBridgeSmoke.events && window.__framescopeBridgeSmoke.events.some(function(event){ return event.type === '" + eventType + "' && event.payload && event.payload.requestId === '" + requestId + "' && event.payload.status === '" + status + "'; })";
        return await WaitForScriptBoolAsync(condition, timeoutMs);
    }

    private async System.Threading.Tasks.Task<bool> WaitForScriptBoolAsync(string conditionScript, int timeoutMs)
    {
        Stopwatch wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < timeoutMs)
        {
            if (await EvaluateScriptBoolAsync(conditionScript)) return true;
            await System.Threading.Tasks.Task.Delay(150);
        }
        return false;
    }

    private async System.Threading.Tasks.Task<bool> EvaluateScriptBoolAsync(string conditionScript)
    {
        if (webView.CoreWebView2 == null) return false;
        string script = "(function(){try{return Boolean(" + conditionScript + ");}catch(e){return false;}})();";
        string result = await webView.CoreWebView2.ExecuteScriptAsync(script);
        return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
    }

    private async System.Threading.Tasks.Task<Dictionary<string, object>> EvaluateScriptJsonDictionaryAsync(string expressionScript)
    {
        var fallback = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (webView.CoreWebView2 == null) return fallback;

        string script = "(function(){try{return JSON.stringify(" + expressionScript + ");}catch(e){return JSON.stringify({error:String(e)});}})();";
        string serialized = await webView.CoreWebView2.ExecuteScriptAsync(script);
        string jsonText = "";
        try
        {
            jsonText = json.Deserialize<string>(serialized);
        }
        catch
        {
            jsonText = serialized;
        }

        if (string.IsNullOrWhiteSpace(jsonText)) return fallback;
        try
        {
            Dictionary<string, object> value = json.Deserialize<Dictionary<string, object>>(jsonText);
            return value ?? fallback;
        }
        catch (Exception ex)
        {
            fallback["error"] = "Failed to decode script JSON: " + ex.Message;
            fallback["raw"] = jsonText;
            return fallback;
        }
    }

    private async System.Threading.Tasks.Task ExecuteScriptSafeAsync(string bodyScript)
    {
        if (webView.CoreWebView2 == null) return;
        string script = "(function(){try{" + bodyScript + "}catch(e){}})();";
        await webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async System.Threading.Tasks.Task CaptureSmokePreviewAsync(string suffix)
    {
        if (string.IsNullOrWhiteSpace(options.ScreenshotPath) || webView.CoreWebView2 == null) return;
        try
        {
            string requestedPath = Path.GetFullPath(options.ScreenshotPath);
            string directory = Path.GetDirectoryName(requestedPath);
            string fileName = Path.GetFileNameWithoutExtension(requestedPath);
            string extension = Path.GetExtension(requestedPath);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
            string screenshotPath = Path.Combine(directory, fileName + "-" + suffix + extension);
            Directory.CreateDirectory(directory);
            using (FileStream stream = File.Create(screenshotPath))
            {
                await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            }
            Log("host:screenshot " + screenshotPath);
        }
        catch (Exception ex)
        {
            Log("host:screenshot-error " + suffix + " " + ex.Message);
        }
    }

    private static Dictionary<int, DateTime> CaptureSmokeExternalProcessSnapshot()
    {
        var snapshot = new Dictionary<int, DateTime>();
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (!IsSmokeExternalProcess(process.ProcessName)) continue;
                snapshot[process.Id] = process.StartTime;
            }
            catch
            {
            }
            finally
            {
                try { process.Dispose(); }
                catch { }
            }
        }
        return snapshot;
    }

    private static List<Dictionary<string, object>> FindSmokeExternalProcessesStartedAfter(Dictionary<int, DateTime> baseline)
    {
        var processes = new List<Dictionary<string, object>>();
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (!IsSmokeExternalProcess(process.ProcessName)) continue;

                DateTime startTime = process.StartTime;
                DateTime baselineStart;
                if (baseline != null && baseline.TryGetValue(process.Id, out baselineStart) && baselineStart == startTime)
                {
                    continue;
                }

                processes.Add(new Dictionary<string, object>
                {
                    { "processName", process.ProcessName },
                    { "pid", process.Id },
                    { "startTime", startTime.ToString("O", CultureInfo.InvariantCulture) },
                    { "path", SafeProcessPath(process) },
                    { "mainWindowTitle", SafeMainWindowTitle(process) }
                });
            }
            catch
            {
            }
            finally
            {
                try { process.Dispose(); }
                catch { }
            }
        }

        return processes;
    }

    private static List<Dictionary<string, object>> CleanupSmokeExternalProcesses(List<Dictionary<string, object>> processes)
    {
        var cleanup = new List<Dictionary<string, object>>();
        if (processes == null) return cleanup;

        foreach (Dictionary<string, object> item in processes)
        {
            int pid = ReadIntObject(item, "pid", 0);
            string processName = ReadString(item, "processName");
            string startTimeText = ReadString(item, "startTime");
            var outcome = new Dictionary<string, object>
            {
                { "processName", processName },
                { "pid", pid },
                { "attempted", false },
                { "closed", false },
                { "killed", false },
                { "message", "" }
            };

            if (pid <= 0 || !IsSmokeExternalProcess(processName))
            {
                outcome["message"] = "Process was not eligible for smoke cleanup.";
                cleanup.Add(outcome);
                continue;
            }

            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    DateTime expectedStartTime;
                    if (DateTime.TryParse(startTimeText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expectedStartTime))
                    {
                        DateTime actualStartTime = process.StartTime;
                        if (actualStartTime != expectedStartTime)
                        {
                            outcome["message"] = "PID start time changed; cleanup skipped.";
                            cleanup.Add(outcome);
                            continue;
                        }
                    }

                    outcome["attempted"] = true;
                    if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase))
                    {
                        if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow() && process.WaitForExit(2500))
                        {
                            outcome["closed"] = true;
                            outcome["message"] = "Closed new Explorer window.";
                        }
                        else
                        {
                            outcome["message"] = "Explorer cleanup skipped to avoid closing the desktop shell.";
                        }
                        cleanup.Add(outcome);
                        continue;
                    }

                    if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow() && process.WaitForExit(2500))
                    {
                        outcome["closed"] = true;
                        outcome["message"] = "Closed external process window.";
                    }
                    else
                    {
                        process.Kill();
                        if (process.WaitForExit(2500))
                        {
                            outcome["killed"] = true;
                            outcome["message"] = "Killed smoke-started external process.";
                        }
                        else
                        {
                            outcome["message"] = "Cleanup requested but process did not exit in time.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                outcome["message"] = ex.Message;
            }

            cleanup.Add(outcome);
        }

        return cleanup;
    }

    private static bool IsSmokeExternalProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        foreach (string name in SmokeExternalProcessNames)
        {
            if (string.Equals(processName, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string SafeProcessPath(Process process)
    {
        try
        {
            return process.MainModule == null ? "" : process.MainModule.FileName;
        }
        catch
        {
            return "";
        }
    }

    private static string SafeMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle ?? "";
        }
        catch
        {
            return "";
        }
    }

    private void CaptureSmokeConfigSnapshot()
    {
        if (!options.Smoke || smokeConfigSnapshotCaptured) return;
        smokeConfigSnapshotCaptured = true;
        try
        {
            smokeConfigExisted = File.Exists(options.ConfigPath);
            smokeOriginalConfigText = smokeConfigExisted ? File.ReadAllText(options.ConfigPath, Encoding.UTF8) : "";
            Log("host:config-snapshot captured existed=" + smokeConfigExisted.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Log("host:config-snapshot-error " + ex.Message);
        }
    }

    private void RestoreSmokeConfigIfNeeded()
    {
        if (!options.Smoke || !smokeConfigSnapshotCaptured || !smokeConfigRestoreNeeded) return;
        try
        {
            if (smokeConfigExisted)
            {
                File.WriteAllText(options.ConfigPath, smokeOriginalConfigText ?? "", Encoding.UTF8);
            }
            else if (File.Exists(options.ConfigPath))
            {
                File.Delete(options.ConfigPath);
            }
            Log("host:config-snapshot restored");
        }
        catch (Exception ex)
        {
            Log("host:config-restore-error " + ex.Message);
        }
    }

    private void PostBridgeEvent(string eventJson)
    {
        if (webView == null || webView.IsDisposed) return;
        try
        {
            BeginInvoke((MethodInvoker)delegate
            {
                if (webView.CoreWebView2 == null) return;
                Log("host->js " + eventJson);
                webView.CoreWebView2.PostWebMessageAsJson(eventJson);
            });
        }
        catch
        {
        }
    }

    private void PostJson(Dictionary<string, object> message)
    {
        PostJson(json.Serialize(message));
    }

    private void PostJson(string messageJson)
    {
        if (webView.CoreWebView2 == null) return;
        Log("host->js " + messageJson);
        webView.CoreWebView2.PostWebMessageAsJson(messageJson);
    }

    private void StartTimeout()
    {
        if (!options.Smoke) return;
        timeoutTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1000, options.TimeoutMs) };
        timeoutTimer.Tick += async delegate
        {
            await FinishAsync(false, "Timed out waiting for WebView2 bridge smoke.");
        };
        timeoutTimer.Start();
    }

    private async System.Threading.Tasks.Task FinishAsync(bool success, string error)
    {
        if (finishStarted) return;
        finishStarted = true;
        if (timeoutTimer != null) timeoutTimer.Stop();

        string screenshotError = "";
        string screenshotPath = "";
        if (!string.IsNullOrWhiteSpace(options.ScreenshotPath) && webView.CoreWebView2 != null)
        {
            try
            {
                screenshotPath = Path.GetFullPath(options.ScreenshotPath);
                Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath));
                using (FileStream stream = File.Create(screenshotPath))
                {
                    await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                }
                Log("host:screenshot " + screenshotPath);
            }
            catch (Exception ex)
            {
                screenshotError = ex.Message;
                Log("host:screenshot-error " + ex.Message);
            }
        }

        RestoreSmokeConfigIfNeeded();

        bool finalSuccess = success && pageLoaded && pageReady && string.IsNullOrWhiteSpace(screenshotError);
        var evidence = new Dictionary<string, object>
        {
            { "success", finalSuccess },
            { "pageLoaded", pageLoaded },
            { "pageReady", pageReady },
            { "elapsedMs", stopwatch.ElapsedMilliseconds },
            { "error", error ?? "" },
            { "screenshotPath", screenshotPath },
            { "screenshotError", screenshotError },
            { "smokePayload", smokePayload },
            { "messages", messages.ToArray() }
        };

        if (!string.IsNullOrWhiteSpace(options.EvidencePath))
        {
            string evidencePath = Path.GetFullPath(options.EvidencePath);
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath));
            File.WriteAllText(evidencePath, json.Serialize(evidence), Encoding.UTF8);
            Log("host:evidence " + evidencePath);
        }

        ExitCode = finalSuccess ? 0 : 2;
        if (options.Smoke)
        {
            BeginInvoke((MethodInvoker)Close);
        }
    }

    private void QueueFinish(bool success, string error)
    {
        System.Threading.Tasks.Task ignored = FinishAsync(success, error);
    }

    private static string BuildEmbeddedHtml(bool smoke)
    {
        string smokeLiteral = smoke ? "true" : "false";
        return @"<!doctype html>
<html lang=""zh-CN"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>FrameScope Monitor Web UI</title>
  <style>
    html, body { height: 100%; margin: 0; background: #0b111c; color: #eef5ff; font-family: ""Segoe UI"", ""Microsoft YaHei UI"", sans-serif; }
    main { box-sizing: border-box; min-height: 100%; padding: 24px; display: grid; grid-template-columns: 260px minmax(0, 1fr); gap: 18px; }
    aside, section { border: 1px solid rgba(130, 170, 210, .24); background: rgba(18, 29, 45, .86); border-radius: 8px; padding: 18px; }
    h1 { margin: 0 0 8px; font-size: 24px; letter-spacing: 0; }
    p { color: #aebbd0; line-height: 1.55; }
    button { border: 1px solid rgba(100, 180, 255, .48); background: #163452; color: #eef5ff; border-radius: 6px; padding: 10px 12px; cursor: pointer; }
    .stack { display: grid; gap: 10px; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; }
    .metric { border: 1px solid rgba(130, 170, 210, .2); border-radius: 8px; padding: 12px; min-width: 150px; background: rgba(255,255,255,.04); }
    .label { color: #8fa1bb; font-size: 12px; }
    .value { margin-top: 6px; font-size: 20px; font-weight: 700; color: #66d99a; }
    pre { min-height: 280px; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; border: 1px solid rgba(130, 170, 210, .18); border-radius: 8px; padding: 14px; background: rgba(0,0,0,.24); color: #d9e7f7; }
  </style>
</head>
<body>
  <main>
    <aside>
      <h1>FrameScope Web UI</h1>
      <p>WebView2 backend bridge host. This screen is a minimal bridge surface, not the final visual UI.</p>
      <div class=""stack"">
        <button id=""snapshot"">state.snapshot</button>
        <button id=""config"">config.get</button>
        <button id=""processes"">processes.refresh</button>
      </div>
    </aside>
    <section>
      <div class=""row"">
        <div class=""metric""><div class=""label"">Page</div><div class=""value"" id=""page"">loaded</div></div>
        <div class=""metric""><div class=""label"">Bridge</div><div class=""value"" id=""bridge"">waiting</div></div>
        <div class=""metric""><div class=""label"">Processes</div><div class=""value"" id=""processCount"">pending</div></div>
      </div>
      <pre id=""log""></pre>
    </section>
  </main>
  <script>
    const smoke = " + smokeLiteral + @";
    const logEl = document.getElementById('log');
    const bridgeEl = document.getElementById('bridge');
    const processCountEl = document.getElementById('processCount');
    const responses = {};
    const events = [];
    const ids = {};
    let completed = false;

    function append(text) {
      logEl.textContent += new Date().toISOString() + ' ' + text + '\n';
    }

    function request(type, payload, label) {
      const requestId = label + '-' + Math.random().toString(16).slice(2);
      ids[label] = requestId;
      const message = { requestId, type, payload: payload || {} };
      append('send ' + JSON.stringify(message));
      window.chrome.webview.postMessage(message);
      return requestId;
    }

    function maybeFinishSmoke() {
      if (!smoke || completed) return;
      const processEvent = events.find(e => e.type === 'event.processesRefreshed' && e.payload && e.payload.requestId === ids.processes);
      if (!responses[ids.snapshot] || !responses[ids.config] || !responses[ids.processes] || !processEvent) return;
      completed = true;
      const success = !!responses[ids.snapshot].ok && !!responses[ids.config].ok && !!responses[ids.processes].ok && processEvent.payload.count >= 0;
      window.chrome.webview.postMessage({
        type: 'smoke-complete',
        payload: {
          success,
          error: success ? '' : 'One or more bridge smoke requests failed.',
          responses,
          events,
          requestIds: ids,
          processCount: processEvent.payload.count
        }
      });
    }

    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.addEventListener('message', event => {
        const message = event.data || {};
        append('receive ' + JSON.stringify(message));
        if (message.type === 'response') {
          responses[message.requestId] = message;
          bridgeEl.textContent = message.ok ? 'ok' : 'error';
        } else if (message.type && message.type.indexOf('event.') === 0) {
          events.push(message);
          if (message.type === 'event.processesRefreshed') {
            processCountEl.textContent = String(message.payload.count);
          }
        }
        maybeFinishSmoke();
      });
      window.chrome.webview.postMessage({ type: 'webview-ready', payload: { smoke } });
      document.getElementById('snapshot').onclick = () => request('state.snapshot', {}, 'snapshot');
      document.getElementById('config').onclick = () => request('config.get', {}, 'config');
      document.getElementById('processes').onclick = () => request('processes.refresh', { query: '' }, 'processes');
      if (smoke) {
        setTimeout(() => {
          request('state.snapshot', {}, 'snapshot');
          request('config.get', {}, 'config');
          request('processes.refresh', { query: '' }, 'processes');
        }, 200);
      }
    } else {
      append('chrome.webview is unavailable');
      bridgeEl.textContent = 'unavailable';
    }
  </script>
</body>
</html>";
    }

    private void Log(string message)
    {
        string line = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) + " " + message;
        messages.Add(line);
        Console.WriteLine(line);
    }

    private static string ReadString(Dictionary<string, object> map, string key)
    {
        object value;
        return map != null && map.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
    }

    private static bool ReadBool(Dictionary<string, object> map, string key, bool fallback)
    {
        object value;
        if (map == null || !map.TryGetValue(key, out value) || value == null) return fallback;
        try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    private static int ReadIntObject(Dictionary<string, object> map, string key, int fallback)
    {
        object value;
        if (map == null || !map.TryGetValue(key, out value) || value == null) return fallback;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> map, string key)
    {
        object value;
        if (map == null || !map.TryGetValue(key, out value) || value == null)
        {
            return new Dictionary<string, object>();
        }

        var dictionary = value as Dictionary<string, object>;
        return dictionary ?? new Dictionary<string, object>();
    }
}
