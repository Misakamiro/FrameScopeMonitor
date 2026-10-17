using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
// Explicitly compiled disposable child for Rust regeneration failure injection.
internal static class FrameScopeReportControlledProcess
{
    public const string ReportInspectionProtocol = "framescope-report-inspection/1";
    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var json = new JavaScriptSerializer();
        if (args.Contains("--inspect-reports"))
        {
            string run = File.ReadAllLines(args[Array.IndexOf(args, "--run-list") + 1])[0];
            string input = string.Join("|", new[] { "presentmon.csv", "process-samples.csv", "system-samples.csv", "hardware-samples.csv", "cpu-core-samples.csv", "cpu-voltage-samples.csv", "cpu-vid-samples.csv" }.Select(n => File.Exists(Path.Combine(run,n)) ? n + File.ReadAllText(Path.Combine(run,n)) : n + "missing"));
            string fingerprint;
            using (var hash = SHA256.Create()) fingerprint = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(input)));
            bool complete = File.Exists(Path.Combine(run, "fixture-complete"));
            Console.WriteLine(json.Serialize(new { protocol = ReportInspectionProtocol, status = "inspected", reports = new[] { new { runDir = run, artifactStatus = complete ? "complete" : "incomplete", isComplete = complete, fingerprintVersion = 2, inputFingerprintMatches = complete, manifestInputFingerprint = complete ? fingerprint : "", currentInputFingerprintVersion = 2, currentInputFingerprint = fingerprint, currentInputFingerprintStable = true, reportKind = "full", canOpen = complete, canRegenerate = true } } }));
            return 0;
        }
        string directory = args[0];
        string mode = File.ReadAllText(Path.Combine(directory,"fixture-mode"));
        if (mode == "timeout") Thread.Sleep(20000);
        if (mode == "nonzero") { Console.Error.WriteLine("controlled report failure"); return 7; }
        if (mode == "changed") File.AppendAllText(Path.Combine(directory,"presentmon.csv"), "changed\n");
        if (mode != "invalid-success") File.WriteAllText(Path.Combine(directory,"fixture-complete"),"complete");
        return 0;
    }
}
