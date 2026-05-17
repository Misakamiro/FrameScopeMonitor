using System;
using System.IO;
using System.Threading;

internal static class FakePresentMon
{
    private static int Main(string[] args)
    {
        string sessionName = FrameScopePubgSimulationCommon.GetArgValue(args, "--session_name", "FrameScopeSynthetic");
        string sentinel = FrameScopePubgSimulationCommon.StopSentinelPath(sessionName);

        if (FrameScopePubgSimulationCommon.HasFlag(args, "--terminate_existing_session"))
        {
            File.WriteAllText(sentinel, DateTime.Now.ToString("o"));
            return 0;
        }

        string output = FrameScopePubgSimulationCommon.GetArgValue(args, "--output_file", "");
        if (string.IsNullOrWhiteSpace(output)) return 0;

        string scenario = Environment.GetEnvironmentVariable("FRAMESCOPE_FAKE_PRESENTMON_SCENARIO");
        if (string.IsNullOrWhiteSpace(scenario)) scenario = "stable";
        int rows = FrameScopePubgSimulationCommon.ParseInt(Environment.GetEnvironmentVariable("FRAMESCOPE_FAKE_PRESENTMON_ROWS"), 240);

        try { if (File.Exists(sentinel)) File.Delete(sentinel); }
        catch { }

        if (!scenario.Equals("missing-csv", StringComparison.OrdinalIgnoreCase))
        {
            FrameScopePubgSimulationCommon.WritePresentMonCsv(output, scenario, rows);
        }

        DateTime deadline = DateTime.Now.AddSeconds(30);
        while (DateTime.Now < deadline && !File.Exists(sentinel))
        {
            Thread.Sleep(100);
        }

        return 0;
    }
}
