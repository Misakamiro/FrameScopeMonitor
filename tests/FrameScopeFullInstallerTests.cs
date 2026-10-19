using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

internal static class FrameScopeFullInstallerTests
{
    private static int passed;
    private static object Call(string name, params object[] args)
    {
        var type = typeof(FrameScopeFullInstallerTests).Assembly.GetType("FrameScopeFullInstallerCore");
        if (type == null) throw new Exception("Missing native bootstrap before WebView2 startup");
        try { return type.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    private static int Flow(Func<bool> available, Func<int> runtime, Func<int> app)
    { return (int)Call("Run", available, runtime, app); }

    public static int Main()
    {
        string fixture = Path.Combine(Path.GetTempPath(), "framescope-full-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            int installs = 0, appCalls = 0;
            Check(Flow(() => true, () => { installs++; return 0; }, () => { appCalls++; return 7; }) == 7,
                "existing runtime forwards native installer result");
            Check(installs == 0 && appCalls == 1, "existing runtime is not reinstalled");
            bool ready = false;
            Check(Flow(() => ready, () => { ready = true; return 0; }, () => { Check(ready, "runtime is ready before native installer starts"); return 0; }) == 0, "missing runtime installs before app");
            appCalls = 0;
            Check(Flow(() => false, () => 1602, () => { appCalls++; return 0; }) == 1602 && appCalls == 0,
                "cancelled prerequisite never starts app installer");
            Check(Flow(() => false, () => 3010, () => { appCalls++; return 0; }) == 3010 && appCalls == 0,
                "restart required does not start or reboot app");
            Check(Flow(() => false, () => 55, () => { appCalls++; return 0; }) == 55 && appCalls == 0,
                "failed prerequisite stops installation");
            bool rejected = false;
            try { Flow(() => false, () => 0, () => { appCalls++; return 0; }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && appCalls == 0, "exit zero without installed runtime is not success");
            ready = false;
            Check(Flow(() => ready, () => { ready = true; return 99; }, () => 0) == 0,
                "another runtime installation winning the race is accepted after recheck");
            foreach (var version in new[] { "", "0.0.0.0", "garbage", "9999999999999.0.0.0" })
                Check(!(bool)Call("IsValidRuntimeVersion", version), "invalid runtime version rejected");
            Check((bool)Call("IsValidRuntimeVersion", "148.0.3967.70"), "valid runtime version accepted");
            Check((string)Call("QuoteArgument", "/D=C:\\中文 目录") == "\"/D=C:\\中文 目录\"", "unicode and space preserved");
            Check((string)Call("QuoteArgument", "C:\\Path\\") == "\"C:\\Path\\\\\"", "trailing slash preserved");
            Check((string)Call("QuoteArgument", "a\"b") == "\"a\\\"b\"", "embedded quote preserved");
            byte[] bytes = Encoding.UTF8.GetBytes("verified resource");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            string file = Path.Combine(fixture, "component.exe");
            using (var stream = new MemoryStream(bytes)) Call("ExtractVerified", stream, hash, file);
            Check(File.ReadAllText(file) == "verified resource", "resource extraction verifies exact content");
            string bad = Path.Combine(fixture, "bad.exe");
            rejected = false;
            try { using (var stream = new MemoryStream(bytes)) Call("ExtractVerified", stream, new string('0', 64), bad); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && !File.Exists(bad), "bad resource is removed before execution");
            rejected = false;
            try { using (var stream = new MemoryStream(bytes)) Call("ExtractVerified", stream, hash, file); }
            catch (IOException) { rejected = true; }
            Check(rejected && File.ReadAllText(file) == "verified resource", "extraction cannot overwrite a preexisting file");
            Console.WriteLine("FrameScopeFullInstallerTests: PASS " + passed + " assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
        finally { Directory.Delete(fixture, true); }
    }
}
