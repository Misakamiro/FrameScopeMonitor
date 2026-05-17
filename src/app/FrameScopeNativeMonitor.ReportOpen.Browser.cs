using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

internal static partial class FrameScopeNativeMonitor
{
    private static bool TryOpenHtmlWithBrowsers(string htmlPath)
    {
        var uri = new Uri(htmlPath).AbsoluteUri;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
                Verb = "open"
            });
            WriteFrameScopeLog("open-report-default-browser report=" + htmlPath + " uri=" + uri);
            return true;
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("open-report-default-browser-failed report=" + htmlPath + " error=" + ex.Message);
        }

        foreach (var browserPath in GetBrowserCandidates())
        {
            if (!File.Exists(browserPath)) continue;
            if (TryOpenHtmlWithBrowser(browserPath, uri, htmlPath)) return true;
        }

        return false;
    }

    private static bool TryOpenHtmlWithBrowser(string browserPath, string uri, string htmlPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = browserPath,
                Arguments = GetBrowserOpenArguments(browserPath, uri),
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            });
            WriteFrameScopeLog("open-report-browser report=" + htmlPath + " browser=" + browserPath);
            return true;
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("open-report-browser-failed report=" + htmlPath + " browser=" + browserPath + " error=" + ex.Message);
            return false;
        }
    }

    private static string GetBrowserOpenArguments(string browserPath, string uri)
    {
        var name = Path.GetFileName(browserPath ?? "");
        if (name.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "-new-window " + QuoteCommandArgument(uri);
        }
        if (name.Equals("opera.exe", StringComparison.OrdinalIgnoreCase))
        {
            return QuoteCommandArgument(uri);
        }
        return "--new-window " + QuoteCommandArgument(uri);
    }

    private static IEnumerable<string> GetBrowserCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in GetRegisteredBrowserCandidates())
        {
            if (IsSupportedBrowserCandidate(path) && seen.Add(path)) yield return path;
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe"),
            Path.Combine(programFilesX86, "Mozilla Firefox", "firefox.exe"),
            Path.Combine(programFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(programFilesX86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(localAppData, "Vivaldi", "Application", "vivaldi.exe"),
            Path.Combine(programFiles, "Vivaldi", "Application", "vivaldi.exe"),
            Path.Combine(localAppData, "Programs", "Opera", "opera.exe"),
            Path.Combine(programFiles, "Opera", "opera.exe"),
            Path.Combine(programFilesX86, "Tencent", "QQBrowser", "QQBrowser.exe"),
            Path.Combine(localAppData, "Tencent", "QQBrowser", "Application", "QQBrowser.exe"),
            Path.Combine(programFiles, "360Chrome", "Chrome", "Application", "360chrome.exe"),
            Path.Combine(programFilesX86, "360Chrome", "Chrome", "Application", "360chrome.exe"),
            Path.Combine(programFiles, "360", "360se6", "Application", "360se.exe"),
            Path.Combine(programFilesX86, "360", "360se6", "Application", "360se.exe"),
            Path.Combine(programFiles, "SogouExplorer", "SogouExplorer.exe"),
            Path.Combine(programFilesX86, "SogouExplorer", "SogouExplorer.exe")
        };

        foreach (var path in candidates)
        {
            if (IsSupportedBrowserCandidate(path) && seen.Add(path)) yield return path;
        }
    }

    private static bool IsSupportedBrowserCandidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var name = Path.GetFileName(path);
        if (name.Equals("iexplore.exe", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static IEnumerable<string> GetRegisteredBrowserCandidates()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var subKey in new[] { @"SOFTWARE\Clients\StartMenuInternet", @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet" })
            {
                foreach (var path in GetRegisteredBrowserCandidates(root, subKey))
                {
                    yield return path;
                }
            }
        }
    }

    private static IEnumerable<string> GetRegisteredBrowserCandidates(RegistryKey root, string subKey)
    {
        var result = new List<string>();
        try
        {
            using (var key = root.OpenSubKey(subKey))
            {
                if (key == null) return result;
                foreach (var browserName in key.GetSubKeyNames())
                {
                    using (var commandKey = key.OpenSubKey(browserName + @"\shell\open\command"))
                    {
                        var command = commandKey == null ? "" : Convert.ToString(commandKey.GetValue(""));
                        var exe = ExtractExecutableFromCommand(command);
                        if (!string.IsNullOrWhiteSpace(exe)) result.Add(exe);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            WriteFrameScopeLog("browser-registry-read-failed key=" + subKey + " error=" + ex.Message);
        }
        return result;
    }

    private static string ExtractExecutableFromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "";
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = command.IndexOf('"', 1);
            if (end > 1) return command.Substring(1, end - 1);
        }

        var exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0) return command.Substring(0, exeIndex + 4).Trim();
        var space = command.IndexOf(' ');
        return space > 0 ? command.Substring(0, space).Trim() : command;
    }
}
