using System;
using System.Collections.Generic;

public static class FrameScopeRunContractTests
{
    public static int Main()
    {
        AllHealthyStreamsWithFramesAreFull();
        FramesWithFailedSystemSamplerArePartial();
        AuxiliaryRowsWithoutFramesAreDiagnostic();
        NoUsableRowsAreError();
        CsvWithoutRowsIsNotHealthy();
        ForcedStopIsNotHealthy();
        EarlyExitIsNotHealthy();
        NonZeroExitIsNotHealthy();
        MissingExecutableIsNotHealthy();
        Console.WriteLine("FrameScopeRunContractTests: PASS");
        return 0;
    }

    private static void AllHealthyStreamsWithFramesAreFull()
    {
        AssertEqual("full", FrameScopeRunContract.Classify(120, HealthySampler(), HealthySampler()), "healthy full run");
    }

    private static void FramesWithFailedSystemSamplerArePartial()
    {
        FrameScopeSamplerEvidence system = HealthySampler();
        system.ExitCode = 7;

        AssertEqual("partial", FrameScopeRunContract.Classify(120, HealthySampler(), system), "failed system sampler with frames");
    }

    private static void AuxiliaryRowsWithoutFramesAreDiagnostic()
    {
        AssertEqual("diagnostic", FrameScopeRunContract.Classify(0, HealthySampler(), EmptySampler()), "auxiliary-only run");
    }

    private static void NoUsableRowsAreError()
    {
        AssertEqual("error", FrameScopeRunContract.Classify(0, EmptySampler(), EmptySampler()), "run without usable evidence");
    }

    private static void CsvWithoutRowsIsNotHealthy()
    {
        FrameScopeSamplerEvidence evidence = HealthySampler();
        evidence.ValidRows = 0;

        AssertEqual("no-data", FrameScopeRunContract.NormalizeSamplerStatus(evidence), "header-only CSV status");
        AssertEqual(false, FrameScopeRunContract.IsSamplerHealthy(evidence), "header-only CSV health");
    }

    private static void ForcedStopIsNotHealthy()
    {
        FrameScopeSamplerEvidence evidence = HealthySampler();
        evidence.ForcedStop = true;

        AssertEqual("forced-stop", FrameScopeRunContract.NormalizeSamplerStatus(evidence), "forced sampler status");
        AssertEqual(false, FrameScopeRunContract.IsSamplerHealthy(evidence), "forced sampler health");
    }

    private static void EarlyExitIsNotHealthy()
    {
        FrameScopeSamplerEvidence evidence = HealthySampler();
        evidence.ExitedEarly = true;

        AssertEqual("exited-early", FrameScopeRunContract.NormalizeSamplerStatus(evidence), "early sampler status");
        AssertEqual(false, FrameScopeRunContract.IsSamplerHealthy(evidence), "early sampler health");
    }

    private static void NonZeroExitIsNotHealthy()
    {
        FrameScopeSamplerEvidence evidence = HealthySampler();
        evidence.ExitCode = 9;

        AssertEqual("failed", FrameScopeRunContract.NormalizeSamplerStatus(evidence), "nonzero sampler status");
        AssertEqual(false, FrameScopeRunContract.IsSamplerHealthy(evidence), "nonzero sampler health");
    }

    private static void MissingExecutableIsNotHealthy()
    {
        FrameScopeSamplerEvidence evidence = HealthySampler();
        evidence.ExecutableAvailable = false;
        evidence.Started = false;
        evidence.Pid = null;
        evidence.StartedAt = null;
        evidence.ExitedAt = null;
        evidence.ExitCode = null;

        AssertEqual("missing", FrameScopeRunContract.NormalizeSamplerStatus(evidence), "missing sampler status");
        AssertEqual(false, FrameScopeRunContract.IsSamplerHealthy(evidence), "missing sampler health");
    }

    private static FrameScopeSamplerEvidence HealthySampler()
    {
        DateTime started = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        return new FrameScopeSamplerEvidence
        {
            Required = true,
            ExecutablePath = @"C:\FrameScope\sampler.exe",
            ExecutableAvailable = true,
            Started = true,
            Pid = 4242,
            StartedAt = started,
            ExitedAt = started.AddSeconds(5),
            ExitCode = 0,
            ExitedEarly = false,
            StopRequested = true,
            ForcedStop = false,
            CsvPath = @"C:\FrameScope\samples.csv",
            CsvExists = true,
            CsvBytes = 128,
            ValidRows = 3,
            ErrorTail = ""
        };
    }

    private static FrameScopeSamplerEvidence EmptySampler()
    {
        return new FrameScopeSamplerEvidence
        {
            Required = true,
            ExecutablePath = @"C:\FrameScope\sampler.exe",
            ExecutableAvailable = true,
            Started = false,
            CsvPath = @"C:\FrameScope\samples.csv",
            CsvExists = false,
            CsvBytes = 0,
            ValidRows = 0
        };
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }
}
