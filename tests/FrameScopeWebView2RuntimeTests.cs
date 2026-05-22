using System;
using System.Collections.Generic;
using Microsoft.Win32;

internal static class FrameScopeWebView2RuntimeTests
{
    private static int failures;

    private static int Main()
    {
        Run("Detects runtime from HKLM pv", DetectsRuntimeFromHklmPv);
        Run("Detects runtime from HKCU pv", DetectsRuntimeFromHkcuPv);
        Run("Ignores empty and zero pv values", IgnoresEmptyAndZeroPvValues);
        Run("Does not treat Edge browser client as WebView2 Runtime", DoesNotTreatEdgeBrowserClientAsRuntime);
        Run("Honors runtime test overrides", HonorsRuntimeTestOverrides);
        Run("Installer decision only installs bundled runtime when full package is missing runtime", InstallerDecisionRequiresFullPackageAndMissingRuntime);
        Run("Keeps exact Chinese missing runtime message", KeepsExactChineseMissingRuntimeMessage);

        if (failures != 0)
        {
            Console.Error.WriteLine("FrameScopeWebView2RuntimeTests: FAIL " + failures);
            return 1;
        }

        Console.WriteLine("FrameScopeWebView2RuntimeTests: PASS");
        return 0;
    }

    private static void DetectsRuntimeFromHklmPv()
    {
        var registry = new FakeRegistryValueReader();
        registry.Set(RegistryHive.LocalMachine, RegistryView.Registry64, FrameScopeWebView2Runtime.RuntimeClientKeyPath, "pv", "148.0.3967.70");

        FrameScopeWebView2RuntimeStatus status = FrameScopeWebView2Runtime.GetRuntimeStatus(registry, false, "");

        AssertTrue(status.IsAvailable, "Runtime should be available from HKLM pv.");
        AssertEqual("148.0.3967.70", status.Version, "Runtime version should come from pv.");
    }

    private static void DetectsRuntimeFromHkcuPv()
    {
        var registry = new FakeRegistryValueReader();
        registry.Set(RegistryHive.CurrentUser, RegistryView.Registry64, FrameScopeWebView2Runtime.RuntimeClientKeyPath, "pv", "147.0.3912.98");

        FrameScopeWebView2RuntimeStatus status = FrameScopeWebView2Runtime.GetRuntimeStatus(registry, false, "");

        AssertTrue(status.IsAvailable, "Runtime should be available from HKCU pv.");
        AssertEqual("147.0.3912.98", status.Version, "Runtime version should come from HKCU pv.");
    }

    private static void IgnoresEmptyAndZeroPvValues()
    {
        var registry = new FakeRegistryValueReader();
        registry.Set(RegistryHive.LocalMachine, RegistryView.Registry64, FrameScopeWebView2Runtime.RuntimeClientKeyPath, "pv", "0.0.0.0");
        registry.Set(RegistryHive.CurrentUser, RegistryView.Registry32, FrameScopeWebView2Runtime.RuntimeClientKeyPath, "pv", " ");

        FrameScopeWebView2RuntimeStatus status = FrameScopeWebView2Runtime.GetRuntimeStatus(registry, false, "");

        AssertFalse(status.IsAvailable, "Empty and 0.0.0.0 pv values should not count as installed runtime.");
    }

    private static void DoesNotTreatEdgeBrowserClientAsRuntime()
    {
        var registry = new FakeRegistryValueReader();
        registry.Set(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}", "pv", "148.0.3967.70");

        FrameScopeWebView2RuntimeStatus status = FrameScopeWebView2Runtime.GetRuntimeStatus(registry, false, "");

        AssertFalse(status.IsAvailable, "Edge browser pv must not be treated as WebView2 Runtime.");
    }

    private static void HonorsRuntimeTestOverrides()
    {
        var registry = new FakeRegistryValueReader();

        FrameScopeWebView2RuntimeStatus missing = FrameScopeWebView2Runtime.GetRuntimeStatus(registry, false, "missing");
        FrameScopeWebView2RuntimeStatus installed = FrameScopeWebView2Runtime.GetRuntimeStatus(registry, false, "installed");

        AssertFalse(missing.IsAvailable, "missing override should simulate a missing runtime.");
        AssertTrue(installed.IsAvailable, "installed override should simulate an installed runtime.");
    }

    private static void InstallerDecisionRequiresFullPackageAndMissingRuntime()
    {
        FrameScopeWebView2RuntimeStatus missing = FrameScopeWebView2RuntimeStatus.Missing("test");
        FrameScopeWebView2RuntimeStatus installed = FrameScopeWebView2RuntimeStatus.Available("148.0.3967.70", "test");

        AssertTrue(FrameScopeWebView2Runtime.ShouldInstallBundledRuntime(true, missing), "Full installer should install bundled runtime when missing.");
        AssertFalse(FrameScopeWebView2Runtime.ShouldInstallBundledRuntime(true, installed), "Full installer should skip bundled runtime when already installed.");
        AssertFalse(FrameScopeWebView2Runtime.ShouldInstallBundledRuntime(false, missing), "Normal installer should not try to install a non-bundled runtime.");
    }

    private static void KeepsExactChineseMissingRuntimeMessage()
    {
        const string expected = "\u5f53\u524d\u7cfb\u7edf\u7f3a\u5c11 Microsoft Edge WebView2 Runtime\uff0cFrameScope Monitor \u65b0\u754c\u9762\u9700\u8981\u8be5\u8fd0\u884c\u73af\u5883\u3002\u8bf7\u5b89\u88c5\u5b8c\u6574\u5b89\u88c5\u5305\uff0c\u6216\u524d\u5f80 Microsoft \u5b98\u7f51\u5b89\u88c5 WebView2 Runtime\u3002";
        AssertEqual(expected, FrameScopeWebView2Runtime.MissingRuntimeMessage, "Missing runtime message must match the requested Chinese copy.");
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("[PASS] " + name);
        }
        catch (Exception ex)
        {
            failures++;
            Console.Error.WriteLine("[FAIL] " + name + ": " + ex.Message);
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition) throw new InvalidOperationException(message);
    }

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Expected=[" + expected + "] Actual=[" + actual + "]");
        }
    }

    private sealed class FakeRegistryValueReader : IFrameScopeRegistryValueReader
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void Set(RegistryHive hive, RegistryView view, string subKey, string valueName, string value)
        {
            values[BuildKey(hive, view, subKey, valueName)] = value;
        }

        public string ReadString(RegistryHive hive, RegistryView view, string subKey, string valueName)
        {
            string value;
            return values.TryGetValue(BuildKey(hive, view, subKey, valueName), out value) ? value : "";
        }

        private static string BuildKey(RegistryHive hive, RegistryView view, string subKey, string valueName)
        {
            return hive + "|" + view + "|" + subKey + "|" + valueName;
        }
    }
}
