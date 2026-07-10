using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

internal static class FrameScopeVersionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));

    private static int Main()
    {
        try
        {
            string expected = File.ReadAllText(Path.Combine(Root, "VERSION")).Trim();
            AssertEqual("1.2.1", expected, "local remediation version");
            AssertEqual(expected, FrameScopeProductInfo.Version, "generated product version");
            PackageVersionMatches(expected);
            BuiltAssembliesUseProductVersion(expected);
            Console.WriteLine("FrameScopeVersionTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
    }

    private static void PackageVersionMatches(string expected)
    {
        var json = new JavaScriptSerializer();
        Dictionary<string, object> package = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Root, "src", "frontend", "package.json")));
        Dictionary<string, object> packageLock = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Root, "src", "frontend", "package-lock.json")));
        AssertEqual(expected, Convert.ToString(package["version"]), "package version");
        AssertEqual(expected, Convert.ToString(packageLock["version"]), "lockfile version");
    }

    private static void BuiltAssembliesUseProductVersion(string expected)
    {
        foreach (string name in new[]
        {
            "FrameScopeMonitor.exe",
            "FrameScopeProcessSampler.exe",
            "FrameScopeSystemSampler.exe",
            "FrameScopeReportGenerator.exe",
            "FrameScopeUninstaller.exe",
            "FrameScopeLegacyCleanup.exe"
        })
        {
            string path = Path.Combine(Root, name);
            if (!File.Exists(path)) throw new Exception("Missing built assembly: " + name);
            string version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "";
            if (!version.StartsWith(expected + ".", StringComparison.Ordinal))
            {
                throw new Exception(name + " version mismatch: " + version);
            }
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!object.Equals(expected, actual)) throw new Exception(label + ": expected " + expected + ", actual " + actual);
    }
}
