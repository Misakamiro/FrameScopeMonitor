using System;
using System.Diagnostics;
using System.IO;
using System.Text;

internal static partial class FrameScopeNativeMonitor
{
    private static bool TryOpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var fullPath = Path.GetFullPath(path);
        if (Path.GetExtension(fullPath).Equals(".html", StringComparison.OrdinalIgnoreCase) && TryOpenHtmlWithBrowsers(fullPath))
        {
            return true;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true, Verb = "open" });
            return true;
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("open-report-shell-failed path=" + fullPath + " error=" + ex.Message);
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = QuoteCommandArgument(fullPath), UseShellExecute = false, CreateNoWindow = true });
            return true;
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("open-report-explorer-failed path=" + fullPath + " error=" + ex.Message);
            return false;
        }
    }

    private static bool TryOpenReport(string reportHtml, string runDir)
    {
        var markerPath = Path.Combine(runDir, "report-opened.flag");
        if (File.Exists(markerPath))
        {
            WriteFrameScopeLog("report-open-skip already-opened report=" + reportHtml);
            return true;
        }

        // Completion is acknowledged by the report component after data and chart initialization.
        var requestPath = Path.Combine(runDir, "report-open-request.json");
        var ackPath = Path.Combine(runDir, "report-open-ack.json");
        try
        {
            string normalized = Path.GetFullPath(runDir).TrimEnd('\\', '/').ToUpperInvariant();
            string id;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                id = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var request = File.Exists(requestPath) ? serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(requestPath)) : null;
            if (request != null && File.Exists(ackPath))
            {
                var ack = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(ackPath));
                if (Convert.ToString(ack["id"]) == id && Convert.ToString(ack["token"]) == Convert.ToString(request["token"]) && Convert.ToBoolean(ack["ready"]))
                {
                    File.WriteAllText(markerPath, DateTime.Now.ToString("o"), Encoding.UTF8);
                    WriteFrameScopeLog("report-window-ready report=" + reportHtml);
                    return true;
                }
            }
            if (request != null && DateTime.UtcNow - File.GetLastWriteTimeUtc(requestPath) < TimeSpan.FromSeconds(30)) return false;
            var host = Path.Combine(Root, "FrameScope.exe");
            if (!File.Exists(host)) { WriteFrameScopeLog("report-window-host-missing"); return false; }
            var token = Guid.NewGuid().ToString("N");
            File.WriteAllText(requestPath, serializer.Serialize(new { id = id, token = token }), new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo { FileName = host, Arguments = "--open-report " + id + " --report-request " + token, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Root });
            WriteFrameScopeLog("report-window-requested report=" + reportHtml);
            // Bounded wait for the component's acknowledgement, not merely process creation.
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(ackPath))
                {
                    var ack = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(ackPath));
                    if (Convert.ToString(ack["id"]) == id && Convert.ToString(ack["token"]) == token && Convert.ToBoolean(ack["ready"]))
                    {
                        File.WriteAllText(markerPath, DateTime.Now.ToString("o"), Encoding.UTF8);
                        return true;
                    }
                }
                System.Threading.Thread.Sleep(100);
            }
            WriteFrameScopeLog("report-window-ready-timeout report=" + reportHtml);
        }
        catch (Exception ex) { WriteFrameScopeLog("report-window-open-failed error=" + ex.Message); }
        return false;
    }
}
