using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static partial class FrameScopeReportGenerator
{
    private static Dictionary<string, object> LoadRecordedHardware(string runDir)
    {
        string path = Path.Combine(runDir, "hardware-inventory.json");
        if (!File.Exists(path)) {
            // A legacy report can retain its recorded inventory, never today's WMI values.
            string report=Path.Combine(runDir,"charts","framescope-interactive-data.js");
            if(File.Exists(report))try {
                string raw=File.ReadAllText(report);int first=raw.IndexOf('{'),last=raw.LastIndexOf('}');
                var data=CreateArtifactJsonSerializer().Deserialize<Dictionary<string,object>>(raw.Substring(first,last-first+1));
                object value;var recorded=data.TryGetValue("hardware",out value)?value as Dictionary<string,object>:null;
                if(recorded!=null) {if(!recorded.ContainsKey("ConfigurationSource"))recorded["ConfigurationSource"]="report-generation";return recorded;}
            }catch { }
            return new Dictionary<string,object>{{"ConfigurationSource","not-recorded"}};
        }
        try { return CreateArtifactJsonSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path)); }
        catch { return new Dictionary<string, object> { { "ConfigurationSource", "not-recorded" } }; }
    }

    private static string GetArgValue(string[] args, string name, string fallback)
    {
        if (args == null) return fallback;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return fallback;
    }

    private static string FindLatestRun(string baseDir)
    {
        string runs = Path.Combine(baseDir, "cs2-monitor-runs");
        if (!Directory.Exists(runs)) throw new DirectoryNotFoundException(runs);
        DirectoryInfo latest = new DirectoryInfo(runs).GetDirectories()
            .Where(d => File.Exists(Path.Combine(d.FullName, "presentmon.csv")))
            .OrderByDescending(d => d.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latest == null) throw new FileNotFoundException("No monitor runs found in " + runs);
        return latest.FullName;
    }
}
