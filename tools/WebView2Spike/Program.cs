using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var options = SpikeOptions.Parse(args);
        using var form = new WebView2SpikeForm(options);
        Application.Run(form);
        Environment.ExitCode = form.ExitCode;
    }
}

internal sealed class WebView2SpikeForm : Form
{
    private readonly SpikeOptions options;
    private readonly WebView2 webView;
    private readonly List<string> messages = new();
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly string requestId = Guid.NewGuid().ToString("N");
    private System.Windows.Forms.Timer? timeoutTimer;
    private bool pageReady;
    private bool pageLoaded;
    private bool roundTripComplete;
    private bool finishStarted;

    public WebView2SpikeForm(SpikeOptions options)
    {
        this.options = options;
        ExitCode = 2;
        Text = "FrameScope WebView2 Spike";
        Width = 960;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = !options.Smoke;

        webView = new WebView2
        {
            Dock = DockStyle.Fill
        };
        Controls.Add(webView);
    }

    public int ExitCode { get; private set; }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            StartTimeout();
            var userDataFolder = Path.Combine(Path.GetTempPath(), "FrameScopeWebView2Spike", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(environment);

            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
            if (!File.Exists(indexPath))
            {
                throw new FileNotFoundException("Local frontend page was not found.", indexPath);
            }

            Log("host:navigate " + indexPath);
            webView.CoreWebView2.Navigate(new Uri(indexPath).AbsoluteUri);
        }
        catch (Exception ex)
        {
            Log("host:error " + ex.GetType().Name + " " + ex.Message);
            await FinishAsync(false, ex.Message);
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        pageLoaded = e.IsSuccess;
        Log("host:navigation-completed success=" + e.IsSuccess + " status=" + e.HttpStatusCode);
        if (!e.IsSuccess)
        {
            await FinishAsync(false, "Navigation failed: " + e.WebErrorStatus);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "host-ping",
            id = requestId,
            payload = new
            {
                app = "FrameScopeMonitor",
                bridgeVersion = 1,
                transport = "CoreWebView2.PostWebMessageAsJson"
            }
        });
        webView.CoreWebView2.PostWebMessageAsJson(payload);
        Log("host->js " + payload);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var json = e.WebMessageAsJson;
        Log("js->host " + json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : "";
            if (string.Equals(type, "webview-ready", StringComparison.Ordinal))
            {
                pageReady = true;
                return;
            }

            var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() : "";
            if (string.Equals(type, "js-reply", StringComparison.Ordinal) &&
                string.Equals(id, requestId, StringComparison.Ordinal))
            {
                roundTripComplete = true;
                await FinishAsync(true, "");
            }
        }
        catch (Exception ex)
        {
            await FinishAsync(false, "Invalid JSON from page: " + ex.Message);
        }
    }

    private void StartTimeout()
    {
        if (!options.Smoke) return;
        timeoutTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1000, options.TimeoutMs) };
        timeoutTimer.Tick += async (_, _) =>
        {
            await FinishAsync(false, "Timed out waiting for WebView2 message round trip.");
        };
        timeoutTimer.Start();
    }

    private async Task FinishAsync(bool success, string error)
    {
        if (finishStarted) return;
        finishStarted = true;
        timeoutTimer?.Stop();

        string screenshotError = "";
        if (!string.IsNullOrWhiteSpace(options.ScreenshotPath) && webView.CoreWebView2 != null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ScreenshotPath))!);
                await using var stream = File.Create(options.ScreenshotPath);
                await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                Log("host:screenshot " + Path.GetFullPath(options.ScreenshotPath));
            }
            catch (Exception ex)
            {
                screenshotError = ex.Message;
                Log("host:screenshot-error " + ex.Message);
            }
        }

        var evidence = new
        {
            success = success && pageLoaded && roundTripComplete,
            pageLoaded,
            pageReady,
            roundTripComplete,
            elapsedMs = stopwatch.ElapsedMilliseconds,
            screenshotPath = string.IsNullOrWhiteSpace(options.ScreenshotPath) ? "" : Path.GetFullPath(options.ScreenshotPath),
            screenshotError,
            error,
            messages
        };
        var evidenceJson = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });

        if (!string.IsNullOrWhiteSpace(options.EvidencePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.EvidencePath))!);
            File.WriteAllText(options.EvidencePath, evidenceJson);
            Log("host:evidence " + Path.GetFullPath(options.EvidencePath));
        }

        Console.WriteLine(evidenceJson);
        ExitCode = evidence.success ? 0 : 2;
        if (options.Smoke)
        {
            BeginInvoke((MethodInvoker)Close);
        }
    }

    private void Log(string message)
    {
        var line = DateTimeOffset.Now.ToString("O") + " " + message;
        messages.Add(line);
        Console.WriteLine(line);
    }
}

internal sealed class SpikeOptions
{
    public bool Smoke { get; private init; }
    public string EvidencePath { get; private init; } = "";
    public string ScreenshotPath { get; private init; } = "";
    public int TimeoutMs { get; private init; } = 15000;

    public static SpikeOptions Parse(string[] args)
    {
        var options = new SpikeOptions
        {
            Smoke = HasArg(args, "--smoke"),
            EvidencePath = GetValue(args, "--evidence", ""),
            ScreenshotPath = GetValue(args, "--screenshot", ""),
            TimeoutMs = int.TryParse(GetValue(args, "--timeout-ms", "15000"), out var timeoutMs) ? timeoutMs : 15000
        };
        return options;
    }

    private static bool HasArg(string[] args, string name)
    {
        return args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetValue(string[] args, string name, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return fallback;
    }
}
