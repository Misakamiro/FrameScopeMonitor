using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

public static class FrameScopePubgSimulatorTests
{
    public static int Main()
    {
        try
        {
            PubgTargetUsesExpectedAliases();
            StableScenarioWritesValidPresentMonRows();
            SpikeScenarioContainsDropAndHighFpsRows();
            NoDataScenarioWritesHeaderOnly();
            MonitorArgsUseProcessNameCaptureAndInitialPid();
            Console.WriteLine("FrameScopePubgSimulatorTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static void PubgTargetUsesExpectedAliases()
    {
        FrameScopeTarget target = FrameScopePubgSimulationCommon.CreateTarget();
        AssertEqual("PUBG: BATTLEGROUNDS", target.Name, "target name");
        AssertEqual("TslGame.exe", target.ProcessName, "target process");

        List<string> aliases = FrameScopeCapturePlanner.BuildTargetProcessBaseNames(target.ProcessName, target.Name);
        AssertSequence(new[] { "TslGame", "TslGame-Win64-Shipping" }, aliases, "target aliases");
    }

    private static void StableScenarioWritesValidPresentMonRows()
    {
        string dir = NewTempDir();
        try
        {
            string csv = Path.Combine(dir, "presentmon.csv");
            FrameScopePubgSimulationCommon.WritePresentMonCsv(csv, "stable", 100);
            List<string> lines = File.ReadAllLines(csv).ToList();

            AssertTrue(lines.Count == 101, "stable line count includes header and rows");
            AssertTrue(lines[0].Contains("MsBetweenPresents"), "header has frame time");
            AssertTrue(lines[1].StartsWith("TslGame.exe,"), "application column names PUBG process");
            double firstMs = ParseFrameMs(lines[1]);
            AssertTrue(firstMs > 15.0 && firstMs < 18.0, "stable frame time around 60 fps");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void SpikeScenarioContainsDropAndHighFpsRows()
    {
        string dir = NewTempDir();
        try
        {
            string csv = Path.Combine(dir, "presentmon.csv");
            FrameScopePubgSimulationCommon.WritePresentMonCsv(csv, "spikes", 180);
            List<double> frameMs = File.ReadAllLines(csv).Skip(1).Select(ParseFrameMs).ToList();

            AssertTrue(frameMs.Any(ms => ms >= 90.0), "spike scenario has drop-frame stall");
            AssertTrue(frameMs.Any(ms => ms <= 5.0), "spike scenario has high-fps burst");
            AssertTrue(frameMs.Count == 180, "spike row count");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void NoDataScenarioWritesHeaderOnly()
    {
        string dir = NewTempDir();
        try
        {
            string csv = Path.Combine(dir, "presentmon.csv");
            FrameScopePubgSimulationCommon.WritePresentMonCsv(csv, "no-data", 100);
            AssertEqual(1, File.ReadAllLines(csv).Length, "no-data writes only header");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void MonitorArgsUseProcessNameCaptureAndInitialPid()
    {
        string runRoot = Path.Combine(Path.GetTempPath(), "framescope-pubg-sim-runs");
        string fakePresentMon = Path.Combine(Path.GetTempPath(), "FakePresentMon.exe");
        string args = FrameScopePubgSimulationCommon.BuildMonitorSessionArguments(runRoot, fakePresentMon, 4321, 3);

        AssertTrue(args.Contains("--TargetProcessName TslGame.exe"), "target process arg");
        AssertTrue(args.Contains("--TargetProcessAliases \"TslGame;TslGame-Win64-Shipping\""), "aliases arg");
        AssertTrue(args.Contains("--TargetDisplayName \"PUBG: BATTLEGROUNDS\""), "display arg");
        AssertTrue(args.Contains("--InitialTargetPid 4321"), "initial pid arg");
        AssertTrue(args.Contains("--PresentMonExe " + fakePresentMon), "presentmon override arg");
    }

    private static double ParseFrameMs(string line)
    {
        string[] parts = line.Split(',');
        return double.Parse(parts[6], CultureInfo.InvariantCulture);
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-pubg-simulator-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); }
        catch { }
    }

    private static void AssertSequence(IEnumerable<string> expected, IEnumerable<string> actual, string label)
    {
        string left = string.Join("|", expected.ToArray());
        string right = string.Join("|", actual.ToArray());
        if (!left.Equals(right, StringComparison.Ordinal))
        {
            throw new Exception(label + ": expected <" + left + "> but got <" + right + ">");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
    }
}
