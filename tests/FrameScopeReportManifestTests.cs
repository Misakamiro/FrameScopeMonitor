using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

public static class FrameScopeReportManifestTests
{
    public static int Main()
    {
        ManifestJsonSurvivesPowerShellDefaultRead();
        Console.WriteLine("FrameScopeReportManifestTests: PASS");
        return 0;
    }

    private static void ManifestJsonSurvivesPowerShellDefaultRead()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-manifest-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "framescope-interactive-manifest.json");
        try
        {
            Dictionary<string, object> manifest = new Dictionary<string, object>
            {
                { "hasFrameData", true },
                { "reportKind", "full" },
                { "frames", 240 },
                { "processSamples", 59 },
                { "systemSamples", 2 },
                { "frameCaptureStatus", "captured" },
                { "frameCaptureMessage", "PresentMon 已成功写入帧数据。" }
            };

            string json = FrameScopeReportGenerator.SerializeArtifactJson(manifest);
            AssertAsciiOnly(json, "manifest json should be ASCII-safe for default PowerShell reads");
            File.WriteAllText(path, json, new UTF8Encoding(false));

            string defaultDecoded = File.ReadAllText(path, Encoding.Default);
            Dictionary<string, object> parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(defaultDecoded);

            AssertEqual(true, Convert.ToBoolean(parsed["hasFrameData"]), "has frame data");
            AssertEqual("full", Convert.ToString(parsed["reportKind"]), "report kind");
            AssertEqual(240, Convert.ToInt32(parsed["frames"]), "frame count");
            AssertEqual(59, Convert.ToInt32(parsed["processSamples"]), "process sample count");
            AssertEqual(2, Convert.ToInt32(parsed["systemSamples"]), "system sample count");
            AssertEqual("PresentMon 已成功写入帧数据。", Convert.ToString(parsed["frameCaptureMessage"]), "frame capture message");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void AssertAsciiOnly(string value, string label)
    {
        foreach (char ch in value)
        {
            if (ch > 0x7f) throw new Exception(label + ": found non-ASCII char U+" + ((int)ch).ToString("X4"));
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }
}
