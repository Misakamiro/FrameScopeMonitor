using System;
using System.Reflection;
using Microsoft.Win32;

internal interface IFrameScopeRegistryValueReader
{
    string ReadString(RegistryHive hive, RegistryView view, string subKey, string valueName);
}

internal sealed class FrameScopeRegistryValueReader : IFrameScopeRegistryValueReader
{
    public string ReadString(RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        try
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
            using (RegistryKey key = baseKey.OpenSubKey(subKey))
            {
                object value = key == null ? null : key.GetValue(valueName);
                return value == null ? "" : Convert.ToString(value);
            }
        }
        catch
        {
            return "";
        }
    }
}

internal sealed class FrameScopeWebView2RuntimeStatus
{
    public bool IsAvailable { get; private set; }
    public string Version { get; private set; }
    public string Source { get; private set; }
    public string Message { get; private set; }

    private FrameScopeWebView2RuntimeStatus(bool isAvailable, string version, string source, string message)
    {
        IsAvailable = isAvailable;
        Version = version ?? "";
        Source = source ?? "";
        Message = message ?? "";
    }

    public static FrameScopeWebView2RuntimeStatus Available(string version, string source)
    {
        return new FrameScopeWebView2RuntimeStatus(true, version, source, "");
    }

    public static FrameScopeWebView2RuntimeStatus Missing(string message)
    {
        return new FrameScopeWebView2RuntimeStatus(false, "", "", message);
    }
}

internal static class FrameScopeWebView2Runtime
{
    internal const string RuntimeClientKeyPath = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    internal const string RuntimeTestModeEnvironmentVariable = "FRAMESCOPE_WEBVIEW2_RUNTIME_TEST_MODE";
    internal const string MissingRuntimeMessage = "\u5f53\u524d\u7cfb\u7edf\u7f3a\u5c11 Microsoft Edge WebView2 Runtime\uff0cFrameScope Monitor \u65b0\u754c\u9762\u9700\u8981\u8be5\u8fd0\u884c\u73af\u5883\u3002\u8bf7\u5b89\u88c5\u5b8c\u6574\u5b89\u88c5\u5305\uff0c\u6216\u524d\u5f80 Microsoft \u5b98\u7f51\u5b89\u88c5 WebView2 Runtime\u3002";

    public static FrameScopeWebView2RuntimeStatus GetRuntimeStatus()
    {
        return GetRuntimeStatus(new FrameScopeRegistryValueReader(), true, Environment.GetEnvironmentVariable(RuntimeTestModeEnvironmentVariable));
    }

    public static FrameScopeWebView2RuntimeStatus GetRuntimeStatus(IFrameScopeRegistryValueReader registry, bool allowWebView2ApiFallback, string testMode)
    {
        FrameScopeWebView2RuntimeStatus overrideStatus;
        if (TryGetOverrideStatus(testMode, out overrideStatus))
        {
            return overrideStatus;
        }

        registry = registry ?? new FrameScopeRegistryValueReader();
        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                string version = registry.ReadString(hive, view, RuntimeClientKeyPath, "pv");
                if (IsUsableRuntimeVersion(version))
                {
                    return FrameScopeWebView2RuntimeStatus.Available(
                        version.Trim(),
                        hive.ToString() + "\\" + view.ToString() + "\\" + RuntimeClientKeyPath + "\\pv");
                }
            }
        }

        if (allowWebView2ApiFallback)
        {
            string apiVersion = GetAvailableBrowserVersionStringFromWebView2Api();
            if (IsUsableRuntimeVersion(apiVersion))
            {
                return FrameScopeWebView2RuntimeStatus.Available(apiVersion.Trim(), "CoreWebView2Environment.GetAvailableBrowserVersionString");
            }
        }

        return FrameScopeWebView2RuntimeStatus.Missing("WebView2 Runtime pv was not found in EdgeUpdate Clients.");
    }

    public static bool ShouldInstallBundledRuntime(bool hasBundledRuntimeInstaller, FrameScopeWebView2RuntimeStatus status)
    {
        return hasBundledRuntimeInstaller && (status == null || !status.IsAvailable);
    }

    public static bool IsUsableRuntimeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        string trimmed = version.Trim();
        return !string.Equals(trimmed, "0.0.0.0", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetOverrideStatus(string testMode, out FrameScopeWebView2RuntimeStatus status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(testMode)) return false;
        string mode = testMode.Trim();
        if (mode.Equals("missing", StringComparison.OrdinalIgnoreCase))
        {
            status = FrameScopeWebView2RuntimeStatus.Missing("Simulated missing WebView2 Runtime.");
            return true;
        }

        if (mode.Equals("installed", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("available", StringComparison.OrdinalIgnoreCase))
        {
            status = FrameScopeWebView2RuntimeStatus.Available("test-installed", "test-override");
            return true;
        }

        return false;
    }

    private static string GetAvailableBrowserVersionStringFromWebView2Api()
    {
        try
        {
            Type environmentType = Type.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment, Microsoft.Web.WebView2.Core", false);
            if (environmentType == null) return "";
            MethodInfo method = environmentType.GetMethod(
                "GetAvailableBrowserVersionString",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            if (method == null) return "";
            object value = method.Invoke(null, new object[] { null });
            return value == null ? "" : Convert.ToString(value);
        }
        catch
        {
            return "";
        }
    }
}
