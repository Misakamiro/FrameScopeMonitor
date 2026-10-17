using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

internal static class FrameScopeReportRecoveryTests
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    private static int Main()
    {
        try
        {
            DefaultFingerprintIsVersionTwo();
            FingerprintDetectsSameLengthSameTimestampRewrite();
            LegacyVersionZeroAndDurationInspection();
            HtmlAloneIsIncomplete();
            MissingDataIsIncomplete();
            CorruptManifestIsIncomplete();
            ManifestPathEscapeIsIncomplete();
            CompleteArtifactSetPasses();
            MissingHardwareCsvPreservesLegacyFingerprint();
            CsvChangeInvalidatesCompleteArtifactSet();
            HardwareCsvChangeInvalidatesCompleteArtifactSet();
            HardwareOnlyRunCanBeRecovered();
            MonitorCsvRequiresADataRow();
            RecoveryPolicyHonorsPhaseAndActivity();
            LiveOwnerIdentityBlocksRecovery();
            RecoveryOwnerGateCoversTerminalAndLegacyRuns();
            RecoverySelectionFiltersBeforeApplyingLimits();
            FailedRecoveryAttemptsBecomeExhausted();
            GenerationSuccessRequiresVerifiedStableArtifacts();
            VersionedFingerprintCoversCpuCsvAndLegacyIsReadable();
            Console.WriteLine("FrameScopeReportRecoveryTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void DefaultFingerprintIsVersionTwo()
    {
        string run = CreateRun("default-v2");
        try { AssertEqual(FrameScopeReportArtifacts.CaptureInputFingerprint(run, 2).Value, FrameScopeReportArtifacts.CaptureInputFingerprint(run).Value, "new input snapshots always use v2"); }
        finally { DeleteFixture(run); }
    }
    private static void FingerprintDetectsSameLengthSameTimestampRewrite()
    {
        string run = CreateRun("same-size-rewrite");
        try
        {
            string path = Path.Combine(run, "presentmon.csv");
            File.WriteAllText(path, "Time,Value\r\n2026-01-01T00:00:00Z,1\r\n");
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            string before = FrameScopeReportArtifacts.CaptureInputFingerprint(run, 2).Value;
            File.WriteAllText(path, "Time,Value\r\n2026-01-01T00:00:00Z,2\r\n");
            File.SetLastWriteTimeUtc(path, stamp);
            AssertFalse(before == FrameScopeReportArtifacts.CaptureInputFingerprint(run, 2).Value, "v2 fingerprints cover content, not only metadata");
        }
        finally { DeleteFixture(run); }
    }
    private static void LegacyVersionZeroAndDurationInspection()
    {
        string run = CreateRun("legacy-v0-duration");
        try
        {
            AssertEqual<double?>(null, FrameScopeReportArtifacts.Inspect(run).DurationSeconds, "unknown duration remains null");
            File.WriteAllText(Path.Combine(run, "system-samples.csv"), "Time,TotalCpuPct\r\n2026-01-01T00:00:00Z,10\r\n2026-01-01T00:00:05Z,11\r\n");
            AssertEqual<double?>(5, FrameScopeReportArtifacts.Inspect(run).DurationSeconds, "old duration comes from samples");
            File.WriteAllText(Path.Combine(run, "status.json"), "{\"StartTime\":\"2026-01-01T00:00:00Z\",\"EndTime\":\"2026-01-01T00:00:07Z\"}");
            AssertEqual<double?>(7, FrameScopeReportArtifacts.Inspect(run).DurationSeconds, "recorded range takes precedence");
            WriteArtifactSet(run);
            string path = Path.Combine(run, "charts", FrameScopeReportArtifacts.ManifestFileName);
            var manifest = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            manifest.Remove("inputFingerprintVersion");
            manifest["inputFingerprint"] = FrameScopeReportArtifacts.CaptureInputFingerprint(run, 1).Value;
            File.WriteAllText(Path.Combine(run, "cpu-core-samples.csv"), "Time,ActualFrequencyMHz\r\n2026-01-01T00:00:02Z,4200\r\n");
            File.WriteAllText(path, Json.Serialize(manifest));
            var state = FrameScopeReportArtifacts.Inspect(run);
            AssertTrue(state.IsReadable, "unversioned historical report remains readable");
            AssertFalse(state.IsComplete, "historical report cannot verify current CPU inputs");
            AssertEqual("legacy-readable", state.ArtifactStatus, "historical status explicit");
            foreach (string kind in new[] { "full", "partial", "diagnostic", "error" })
            {
                manifest["reportKind"] = kind;
                File.WriteAllText(path, Json.Serialize(manifest));
                AssertEqual(kind, FrameScopeReportArtifacts.Inspect(run).ReportKind, "all report types supported");
            }
        }
        finally { DeleteFixture(run); }
    }

    private static void VersionedFingerprintCoversCpuCsvAndLegacyIsReadable()
    {
        string run = CreateRun("versioned-fingerprint");
        try
        {
            File.WriteAllText(Path.Combine(run, "cpu-core-samples.csv"), "Time,Core\r\n2026-09-18T00:00:00Z,1\r\n");
            string v2 = FrameScopeReportArtifacts.CaptureInputFingerprint(run).Value;
            File.AppendAllText(Path.Combine(run, "cpu-core-samples.csv"), "2026-09-18T00:00:01Z,2\r\n");
            AssertFalse(string.Equals(v2, FrameScopeReportArtifacts.CaptureInputFingerprint(run).Value, StringComparison.OrdinalIgnoreCase), "CPU CSV changes invalidate v2 fingerprint");

            WriteArtifactSet(run);
            string manifest = Path.Combine(run, "charts", "framescope-interactive-manifest.json");
            var map = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifest));
            map["inputFingerprintVersion"] = 1;
            map["inputFingerprint"] = FrameScopeReportArtifacts.CaptureInputFingerprint(run, 1).Value;
            File.WriteAllText(manifest, Json.Serialize(map));
            FrameScopeReportArtifactState state = FrameScopeReportArtifacts.Inspect(run);
            AssertTrue(state.LegacyReadable, "v1 report is explicitly legacy-readable");
            AssertFalse(state.IsComplete, "v1 report is not strict complete");
        }
        finally { DeleteFixture(run); }
    }

    private static void HtmlAloneIsIncomplete()
    {
        string run = CreateRun("html-only");
        try
        {
            string charts = Path.Combine(run, "charts");
            Directory.CreateDirectory(charts);
            File.WriteAllText(Path.Combine(charts, "framescope-interactive-report.html"), "<html></html>");
            AssertFalse(FrameScopeReportArtifacts.Inspect(run).IsComplete, "HTML alone cannot complete a report");
        }
        finally { DeleteFixture(run); }
    }

    private static void MissingDataIsIncomplete()
    {
        string run = CreateRun("missing-data");
        try
        {
            WriteArtifactSet(run);
            File.Delete(Path.Combine(run, "charts", "framescope-interactive-data.js"));
            AssertFalse(FrameScopeReportArtifacts.Inspect(run).IsComplete, "missing data.js");
        }
        finally { DeleteFixture(run); }
    }

    private static void CorruptManifestIsIncomplete()
    {
        string run = CreateRun("corrupt-manifest");
        try
        {
            WriteArtifactSet(run);
            File.WriteAllText(Path.Combine(run, "charts", "framescope-interactive-manifest.json"), "{not-json");
            AssertFalse(FrameScopeReportArtifacts.Inspect(run).IsComplete, "corrupt manifest");
        }
        finally { DeleteFixture(run); }
    }

    private static void ManifestPathEscapeIsIncomplete()
    {
        string run = CreateRun("escaped-manifest");
        try
        {
            WriteArtifactSet(run, Path.Combine(Path.GetTempPath(), "foreign-report.html"), null);
            AssertFalse(FrameScopeReportArtifacts.Inspect(run).IsComplete, "manifest path escape");
        }
        finally { DeleteFixture(run); }
    }

    private static void CompleteArtifactSetPasses()
    {
        string run = CreateRun("complete");
        try
        {
            WriteArtifactSet(run);
            FrameScopeReportArtifactState state = FrameScopeReportArtifacts.Inspect(run);
            AssertTrue(state.IsComplete, state.Error);
        }
        finally { DeleteFixture(run); }
    }

    private static void CsvChangeInvalidatesCompleteArtifactSet()
    {
        string run = CreateRun("fingerprint-change");
        try
        {
            string csv = Path.Combine(run, "presentmon.csv");
            File.WriteAllText(csv, "Application,MsBetweenPresents" + Environment.NewLine + "game.exe,6.9" + Environment.NewLine);
            WriteArtifactSet(run);
            AssertTrue(FrameScopeReportArtifacts.Inspect(run).IsComplete, "matching input fingerprint");

            File.AppendAllText(csv, "game.exe,7.1" + Environment.NewLine);
            FrameScopeReportArtifactState changed = FrameScopeReportArtifacts.Inspect(run);
            AssertFalse(changed.IsComplete, "CSV changes must invalidate published artifacts");
            AssertFalse(changed.InputFingerprintMatches, "changed CSV fingerprint mismatch");
        }
        finally { DeleteFixture(run); }
    }

    private static void MonitorCsvRequiresADataRow()
    {
        string run = CreateRun("csv-rows");
        try
        {
            File.WriteAllText(Path.Combine(run, "presentmon.csv"), "Application,MsBetweenPresents" + Environment.NewLine);
            AssertFalse(FrameScopeReportArtifacts.HasUsableMonitorData(run), "header-only CSV is not usable");
            File.WriteAllText(Path.Combine(run, "process-samples.csv"), "Time,Pid" + Environment.NewLine + "2026-07-11T00:00:00Z,42" + Environment.NewLine);
            AssertTrue(FrameScopeReportArtifacts.HasUsableMonitorData(run), "a data row is usable");
        }
        finally { DeleteFixture(run); }
    }

    private static void HardwareCsvChangeInvalidatesCompleteArtifactSet()
    {
        string run = CreateRun("hardware-fingerprint-change");
        try
        {
            string csv = Path.Combine(run, "hardware-samples.csv");
            File.WriteAllText(csv, "Time,Fps\r\n2026-09-18T00:00:00Z,60\r\n");
            WriteArtifactSet(run);
            AssertTrue(FrameScopeReportArtifacts.Inspect(run).IsComplete, "hardware baseline complete");
            File.AppendAllText(csv, "2026-09-18T00:00:01Z,30\r\n");
            AssertFalse(FrameScopeReportArtifacts.Inspect(run).IsComplete, "hardware CSV change must invalidate sampled FPS report");
        }
        finally { DeleteFixture(run); }
    }

    private static void MissingHardwareCsvPreservesLegacyFingerprint()
    {
        string run = CreateRun("legacy-fingerprint");
        try
        {
            string snapshot = "presentmon.csv|missing\nprocess-samples.csv|missing\nsystem-samples.csv|missing\n";
            string expected;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                expected = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(snapshot))).Replace("-", "").ToLowerInvariant();
            }
            AssertEqual(expected, FrameScopeReportArtifacts.CaptureInputFingerprint(run, 1).Value,
                "runs without hardware CSV retain their legacy fingerprint");
        }
        finally { DeleteFixture(run); }
    }

    private static void HardwareOnlyRunCanBeRecovered()
    {
        string run = CreateRun("hardware-only");
        try
        {
            string csv = Path.Combine(run, "hardware-samples.csv");
            File.WriteAllText(csv, "Time,Fps\r\n");
            AssertFalse(FrameScopeReportArtifacts.HasUsableMonitorData(run), "hardware header alone is not usable");
            File.AppendAllText(csv, "2026-09-18T00:00:00Z,60\r\n");
            AssertTrue(FrameScopeReportArtifacts.HasUsableMonitorData(run), "hardware FPS data must qualify for report recovery");
        }
        finally { DeleteFixture(run); }
    }

    private static void RecoveryPolicyHonorsPhaseAndActivity()
    {
        AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("done", true, false, false), "done crash window");
        AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("finalizing", true, false, false), "finalizing crash window");
        AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("error", true, false, false), "error retry");
        AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("", true, false, false), "legacy empty phase");
        AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("capturing", true, false, false), "stale inactive capture");
        AssertFalse(FrameScopeReportRecoveryPolicy.ShouldRecover("capturing", true, false, true), "active capture");
        AssertFalse(FrameScopeReportRecoveryPolicy.ShouldRecover("done", false, false, false), "no usable data");
        AssertFalse(FrameScopeReportRecoveryPolicy.ShouldRecover("done", true, true, false), "already complete");
        AssertFalse(FrameScopeReportRecoveryPolicy.ShouldRecover("starting", true, false, false), "non-recoverable phase");
    }

    private static void LiveOwnerIdentityBlocksRecovery()
    {
        FrameScopeMonitorOwnerIdentity identity = FrameScopeReportRecoveryPolicy.CaptureCurrentProcessIdentity();
        AssertEqual(
            FrameScopeMonitorOwnerState.Active,
            FrameScopeReportRecoveryPolicy.ProbeMonitorOwner(identity.ProcessId, identity.ExecutablePath, identity.StartedAtUtcText),
            "matching live owner");
        AssertEqual(
            FrameScopeMonitorOwnerState.Uncertain,
            FrameScopeReportRecoveryPolicy.ProbeMonitorOwner(identity.ProcessId, identity.ExecutablePath, identity.StartedAtUtc.AddSeconds(-1).ToString("o")),
            "PID reuse/start-time mismatch is not recoverable");
        AssertEqual(
            FrameScopeMonitorOwnerState.Exited,
            FrameScopeReportRecoveryPolicy.ProbeMonitorOwner(Int32.MaxValue, identity.ExecutablePath, identity.StartedAtUtcText),
            "missing owner process");
    }

    private static void RecoveryOwnerGateCoversTerminalAndLegacyRuns()
    {
        AssertTrue(
            FrameScopeReportRecoveryPolicy.IsRecoveryCaptureActive("done", true, FrameScopeMonitorOwnerState.Active),
            "terminal run with a live recorded owner stays blocked");
        AssertTrue(
            FrameScopeReportRecoveryPolicy.IsRecoveryCaptureActive("error", true, FrameScopeMonitorOwnerState.Uncertain),
            "terminal run with an uncertain recorded owner stays blocked");
        AssertFalse(
            FrameScopeReportRecoveryPolicy.IsRecoveryCaptureActive("done", true, FrameScopeMonitorOwnerState.Exited),
            "terminal run with a confirmed exited owner can recover");
        AssertFalse(
            FrameScopeReportRecoveryPolicy.IsRecoveryCaptureActive("done", false, FrameScopeMonitorOwnerState.Uncertain),
            "legacy terminal run without owner identity remains recoverable");
        AssertTrue(
            FrameScopeReportRecoveryPolicy.IsRecoveryCaptureActive("capturing", false, FrameScopeMonitorOwnerState.Uncertain),
            "legacy capturing run without owner identity remains conservatively blocked");

        AssertFalse(FrameScopeReportRecoveryPolicy.HasRecordedMonitorOwnerIdentity(0, "", ""), "legacy status has no owner identity");
        AssertTrue(FrameScopeReportRecoveryPolicy.HasRecordedMonitorOwnerIdentity(4100, "", ""), "partial PID identity is recorded");
    }

    private static void RecoverySelectionFiltersBeforeApplyingLimits()
    {
        DateTime now = DateTime.UtcNow;
        var newestComplete = RecoveryCandidate("game", "newest-complete", now, true, true, true, false);
        var olderIncomplete = RecoveryCandidate("game", "older-incomplete", now.AddDays(-1), false, true, true, false);
        var unstable = RecoveryCandidate("game", "unstable", now.AddDays(-2), false, true, false, false);
        var active = RecoveryCandidate("other", "active", now.AddDays(-3), false, true, true, true);

        List<FrameScopeReportRecoveryCandidate> selected = FrameScopeReportRecoveryPolicy.SelectCandidates(
            new[] { newestComplete, olderIncomplete, unstable, active }, 1, 1);

        AssertEqual(1, selected.Count, "recovery selection count");
        AssertEqual(olderIncomplete.RunDirectory, selected[0].RunDirectory, "complete newest run must not hide older incomplete run");
    }

    private static void FailedRecoveryAttemptsBecomeExhausted()
    {
        AssertFalse(FrameScopeReportRecoveryPolicy.IsRecoveryExhausted(2, false), "attempts below limit");
        AssertTrue(FrameScopeReportRecoveryPolicy.IsRecoveryExhausted(3, false), "third failed attempt exhausts recovery");
        AssertFalse(FrameScopeReportRecoveryPolicy.IsRecoveryExhausted(3, true), "success clears exhaustion");
    }

    private static void GenerationSuccessRequiresVerifiedStableArtifacts()
    {
        AssertTrue(FrameScopeReportRecoveryPolicy.IsGenerationSuccessful(false, 0, false, true, true), "fully verified generation");
        AssertFalse(FrameScopeReportRecoveryPolicy.IsGenerationSuccessful(true, 0, false, true, true), "timeout");
        AssertFalse(FrameScopeReportRecoveryPolicy.IsGenerationSuccessful(false, 0, true, true, true), "retryable result");
        AssertFalse(FrameScopeReportRecoveryPolicy.IsGenerationSuccessful(false, 0, false, false, true), "incomplete artifacts");
        AssertFalse(FrameScopeReportRecoveryPolicy.IsGenerationSuccessful(false, 0, false, true, false), "input fingerprint mismatch");
    }

    private static string CreateRun(string name)
    {
        string run = Path.Combine(Path.GetTempPath(), "FrameScopeReportRecoveryTests", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        return run;
    }

    private static void WriteArtifactSet(string run, string reportOverride = null, string dataOverride = null)
    {
        string charts = Path.Combine(run, "charts");
        string report = Path.Combine(charts, "framescope-interactive-report.html");
        string data = Path.Combine(charts, "framescope-interactive-data.js");
        Directory.CreateDirectory(charts);
        File.WriteAllText(report, "<html></html>");
        File.WriteAllText(data, "window.__FRAMESCOPE_REPORT__ = {};");
        string inputFingerprint = FrameScopeReportArtifacts.CaptureInputFingerprint(run).Value;
        File.WriteAllText(Path.Combine(charts, "framescope-interactive-manifest.json"), Json.Serialize(new Dictionary<string, object>
        {
            { "report", reportOverride ?? report },
            { "data", dataOverride ?? data },
            { "frames", 1 },
            { "inputFingerprintVersion", 2 },
            { "inputFingerprint", inputFingerprint }
        }));
    }

    private static FrameScopeReportRecoveryCandidate RecoveryCandidate(
        string target,
        string name,
        DateTime writeTime,
        bool complete,
        bool hasData,
        bool stable,
        bool active)
    {
        return new FrameScopeReportRecoveryCandidate
        {
            RunDirectory = Path.Combine(target, name),
            TargetKey = target,
            LastWriteTimeUtc = writeTime,
            Phase = "done",
            ReportComplete = complete,
            HasUsableMonitorData = hasData,
            InputStable = stable,
            CaptureActive = active
        };
    }

    private static void DeleteFixture(string run)
    {
        if (Directory.Exists(run)) Directory.Delete(run, true);
    }

    private static void AssertTrue(bool value, string message)
    {
        if (!value) throw new Exception("ASSERT TRUE FAILED: " + message);
    }

    private static void AssertFalse(bool value, string message)
    {
        if (value) throw new Exception("ASSERT FALSE FAILED: " + message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!object.Equals(expected, actual)) throw new Exception(message + ": expected " + expected + ", actual " + actual);
    }
}
