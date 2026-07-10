# Report Recovery and Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make reports transactional and recoverable, bound report-generator execution, align diagnostics with the canonical run contract, and keep state/history files safe and bounded.

**Architecture:** A shared artifact inspector is the only authority for report completeness. Watcher recovery uses a pure phase/input policy and a bounded process runner; the report generator publishes a verified temporary artifact set. A shared atomic JSON writer and retention selector protect persistent state without deleting active or newest runs.

**Tech Stack:** C#/.NET Framework 4.x, `JavaScriptSerializer`, PowerShell test runner, file-system fixtures and fake child processes.

---

### Task 1: Report artifact completeness and recovery policy

**Files:**
- Create: `src/core/FrameScopeReportArtifacts.cs`
- Create: `src/core/FrameScopeReportRecoveryPolicy.cs`
- Create: `tests/FrameScopeReportRecoveryTests.cs`
- Modify: `tests/Build-FrameScopeTests.ps1`
- Modify: `src/app/FrameScopeNativeMonitor.ReportOrchestration.cs:19-109`
- Test: `tests/FrameScopeReportRecoveryTests.exe`

- [ ] **Step 1: Write failing artifact and phase tests**

```csharp
private static void HtmlAloneIsIncomplete()
{
    string run = CreateRun("html-only");
    Directory.CreateDirectory(Path.Combine(run, "charts"));
    File.WriteAllText(Path.Combine(run, "charts", "framescope-interactive-report.html"), "<html></html>");
    AssertFalse(FrameScopeReportArtifacts.Inspect(run).IsComplete, "html alone cannot complete a report");
}

private static void DoneRunWithMissingArtifactsIsRecoverable()
{
    AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("done", true, false), "done crash window");
    AssertTrue(FrameScopeReportRecoveryPolicy.ShouldRecover("finalizing", true, false), "finalizing crash window");
    AssertFalse(FrameScopeReportRecoveryPolicy.ShouldRecover("capturing", false, false), "no usable CSV");
    AssertFalse(FrameScopeReportRecoveryPolicy.ShouldRecover("done", true, true), "already complete");
}

private static void ManifestMustPointInsideTheRun()
{
    string run = CreateCompleteReport("foreign-manifest", "C:\\foreign\\report.html");
    AssertFalse(FrameScopeReportArtifacts.Inspect(run).IsComplete, "manifest path escape");
}
```

- [ ] **Step 2: Build and verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeReportRecoveryTests.exe`

Expected: compilation fails because the two policy classes do not exist.

- [ ] **Step 3: Implement one completeness authority**

```csharp
public sealed class FrameScopeReportArtifactState
{
    public string HtmlPath = "";
    public string DataPath = "";
    public string ManifestPath = "";
    public bool HtmlExists;
    public bool DataExists;
    public bool ManifestValid;
    public bool PathsMatchRun;
    public string Error = "";
    public bool IsComplete { get { return HtmlExists && DataExists && ManifestValid && PathsMatchRun; } }
}

public static class FrameScopeReportRecoveryPolicy
{
    public static bool ShouldRecover(string phase, bool hasMonitorCsv, bool reportComplete)
    {
        if (reportComplete || !hasMonitorCsv) return false;
        string value = (phase ?? "").Trim().ToLowerInvariant();
        return value.Length == 0 || value == "capturing" || value == "finalizing" || value == "done" || value == "error";
    }
}
```

`FrameScopeReportArtifacts.Inspect` must require the HTML, `framescope-interactive-data.js`, and parseable manifest; canonicalize `manifest.report` and `manifest.data`, then require both to remain inside the supplied run and equal the expected files. Replace every HTML-only short circuit in `RecoverStaleMissingReports` and `EnsureReportForCompletedRun` with this inspector.

- [ ] **Step 4: Verify GREEN with all partial artifact fixtures**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeReportRecoveryTests.exe`

Expected: fixtures for HTML-only, missing data, corrupt manifest, escaped manifest paths, and `Phase=done` all pass.

- [ ] **Step 5: Commit artifact validation and recovery**

```powershell
git add src/core/FrameScopeReportArtifacts.cs src/core/FrameScopeReportRecoveryPolicy.cs src/app/FrameScopeNativeMonitor.ReportOrchestration.cs tests/FrameScopeReportRecoveryTests.cs tests/Build-FrameScopeTests.ps1
git commit -m "fix: recover incomplete completed reports"
```

### Task 2: Transactional report publication

**Files:**
- Create: `src/core/FrameScopeReportPublisher.cs`
- Modify: `src/reporting/FrameScopeReportGenerator.cs:41-45,142-260`
- Modify: `tests/FrameScopeReportManifestTests.cs`
- Modify: `tests/Build-FrameScopeTests.ps1`
- Test: `tests/FrameScopeReportManifestTests.exe`

- [ ] **Step 1: Add failing publication tests**

```csharp
private static void FailedGenerationPreservesPreviousReport()
{
    string run = CreateBasicRun("publish-rollback");
    string charts = CreateExistingArtifactSet(run, "old-marker");
    AssertThrows<InvalidOperationException>(() => FrameScopeReportPublisher.PublishForTests(run, temp =>
    {
        File.WriteAllText(Path.Combine(temp, "framescope-interactive-report.html"), "new-marker");
        throw new InvalidOperationException("synthetic failure");
    }));
    AssertContains(File.ReadAllText(Path.Combine(charts, "framescope-interactive-report.html")), "old-marker", "rollback");
}

private static void SuccessfulGenerationPublishesAllThreeTogether()
{
    string run = CreateBasicRun("publish-success");
    FrameScopeReportGenerator.GenerateForTests(run);
    FrameScopeReportArtifactState state = FrameScopeReportArtifacts.Inspect(run);
    AssertTrue(state.IsComplete, state.Error);
    AssertEqual(0, Directory.GetDirectories(run, ".framescope-report-*", SearchOption.TopDirectoryOnly).Length, "temp cleanup");
}
```

- [ ] **Step 2: Run the manifest suite to verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeReportManifestTests.exe`

Expected: compilation fails because transactional publication is not implemented.

- [ ] **Step 3: Generate into a sibling temporary directory and publish with rollback**

```csharp
public static void Publish(string runDir, Action<string> generate)
{
    string charts = Path.Combine(runDir, "charts");
    string temp = Path.Combine(runDir, ".framescope-report-" + Guid.NewGuid().ToString("N"));
    string backup = Path.Combine(runDir, ".framescope-report-backup-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        generate(temp);
        FrameScopeReportArtifacts.ValidateDirectory(runDir, temp);
        if (Directory.Exists(charts)) Directory.Move(charts, backup);
        Directory.Move(temp, charts);
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
    }
    catch
    {
        if (!Directory.Exists(charts) && Directory.Exists(backup)) Directory.Move(backup, charts);
        throw;
    }
    finally
    {
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        if (Directory.Exists(backup) && Directory.Exists(charts)) Directory.Delete(backup, true);
    }
}
```

Pass the selected output directory through the generator instead of writing to `runDir\charts`. Write the manifest last and ensure its final `report`/`data` paths describe the canonical post-publication files, not the temporary directory.

- [ ] **Step 4: Verify generation, rollback, and existing manifest tests**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeReportManifestTests.exe; .\tests\FrameScopeReportRecoveryTests.exe`

Expected: all artifact and reporting cases pass; no temporary or backup directory remains.

- [ ] **Step 5: Commit transactional publication**

```powershell
git add src/core/FrameScopeReportPublisher.cs src/reporting/FrameScopeReportGenerator.cs tests/FrameScopeReportManifestTests.cs tests/Build-FrameScopeTests.ps1
git commit -m "fix: publish reports transactionally"
```

### Task 3: Bounded report-generator execution and asynchronous pipe draining

**Files:**
- Create: `src/core/FrameScopeBoundedProcessRunner.cs`
- Create: `tests/FrameScopeReportProcessTests.cs`
- Modify: `tests/Build-FrameScopeTests.ps1`
- Modify: `src/app/FrameScopeNativeMonitor.ReportOrchestration.cs:111-239`
- Modify: `src/app/FrameScopeNativeMonitor.ReportOrchestration.Models.cs`
- Modify: `src/app/FrameScopeNativeMonitor.ReportStatus.cs`
- Test: `tests/FrameScopeReportProcessTests.exe`

- [ ] **Step 1: Add fake-child deadlock and timeout tests**

```csharp
private static void LargeOutputDoesNotDeadlock()
{
    FrameScopeProcessResult result = FrameScopeBoundedProcessRunner.Run(
        SelfPath, "--emit-bytes 2097152", Environment.CurrentDirectory, 10000, null);
    AssertFalse(result.TimedOut, "large redirected output");
    AssertEqual(0, result.ExitCode, "large output exit code");
    AssertTrue(result.StandardOutput.Length >= 2097152, "stdout was drained while running");
}

private static void HangingChildIsKilledAtTotalTimeout()
{
    Stopwatch timer = Stopwatch.StartNew();
    FrameScopeProcessResult result = FrameScopeBoundedProcessRunner.Run(
        SelfPath, "--hang", Environment.CurrentDirectory, 750, null);
    timer.Stop();
    AssertTrue(result.TimedOut, "timeout flag");
    AssertTrue(timer.ElapsedMilliseconds < 5000, "bounded wall clock");
}
```

- [ ] **Step 2: Run the new process test to verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeReportProcessTests.exe`

Expected: compilation fails because the bounded runner is missing.

- [ ] **Step 3: Implement asynchronous drains and a total timeout**

```csharp
process.OutputDataReceived += (sender, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
process.ErrorDataReceived += (sender, e) => { if (e.Data != null) lock (error) error.AppendLine(e.Data); };
process.Start();
process.BeginOutputReadLine();
process.BeginErrorReadLine();
bool exited = process.WaitForExit(timeoutMs);
if (!exited)
{
    result.TimedOut = true;
    TryKillProcessTree(process);
}
process.WaitForExit();
```

Use a production timeout constant of 120 seconds and allow the test entry point to inject a shorter value. Persist `ReportGenerationStartedAt`, `ReportGenerationEndedAt`, `ReportGenerationTimedOut`, exit code, error, and `ReportCanRetry=true`; update progress while waiting without resetting the total deadline.

- [ ] **Step 4: Verify the pipe and timeout tests**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeReportProcessTests.exe; .\tests\FrameScopeReportProgressTests.exe`

Expected: large output exits normally, the hanging child is killed within five seconds in the fixture, and progress tests remain green.

- [ ] **Step 5: Commit bounded orchestration**

```powershell
git add src/core/FrameScopeBoundedProcessRunner.cs src/app/FrameScopeNativeMonitor.ReportOrchestration.cs src/app/FrameScopeNativeMonitor.ReportOrchestration.Models.cs src/app/FrameScopeNativeMonitor.ReportStatus.cs tests/FrameScopeReportProcessTests.cs tests/Build-FrameScopeTests.ps1
git commit -m "fix: bound report generator execution"
```

### Task 4: Atomic JSON writes

**Files:**
- Create: `src/core/FrameScopeJsonFile.cs`
- Create: `tests/FrameScopeJsonFileTests.cs`
- Modify: `tests/Build-FrameScopeTests.ps1`
- Modify: `src/core/FrameScopeConfigStore.cs`
- Modify: `src/core/FrameScopeReportProgress.cs`
- Modify: `src/app/FrameScopeNativeMonitor.MonitorSession.Status.cs`
- Modify: `src/app/FrameScopeNativeMonitor.ReportStatus.cs`
- Modify: `src/app/FrameScopeNativeMonitor.Watcher.cs`
- Modify: `src/diagnostics/FrameScopeDiagnostics.IO.cs`
- Modify: `src/monitoring/FrameScopeSystemSampler.IO.cs`
- Modify: `src/reporting/FrameScopeReportGenerator.cs`
- Test: `tests/FrameScopeJsonFileTests.exe`

- [ ] **Step 1: Add failing replace-safety tests**

```csharp
private static void FailedWritePreservesPreviousJson()
{
    string path = Path.Combine(CreateTempRoot(), "status.json");
    File.WriteAllText(path, "{\"generation\":1}");
    AssertThrows<IOException>(() => FrameScopeJsonFile.WriteTextAtomicForTests(
        path,
        "{\"generation\":2}",
        (temp, destination) => { throw new IOException("synthetic replace failure"); }));
    AssertEqual("{\"generation\":1}", File.ReadAllText(path), "previous JSON remains readable");
    AssertEqual(0, Directory.GetFiles(Path.GetDirectoryName(path), ".status.json.*.tmp").Length, "temporary cleanup");
}
```

- [ ] **Step 2: Verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeJsonFileTests.exe`

Expected: compilation fails because `FrameScopeJsonFile` is absent.

- [ ] **Step 3: Implement same-directory flush and replace**

```csharp
public static void WriteTextAtomic(string path, string text)
{
    string fullPath = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
    string temp = Path.Combine(Path.GetDirectoryName(fullPath), "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
    try
    {
        using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(text ?? "");
            writer.Flush();
            stream.Flush(true);
        }
        ReplaceTempFile(temp, fullPath);
    }
    finally { if (File.Exists(temp)) File.Delete(temp); }
}
```

Keep the production entry point free of test state. An `internal WriteTextAtomicForTests(path, text, Action<string,string> replace)` overload supplies the replace operation as a parameter; production uses a private `ReplaceTempFile` method.

Replace direct overwrites for config, status, summary, watcher state, report progress, sampler telemetry status, diagnostics JSON, and report manifest. Append-only logs/history remain append-only.

- [ ] **Step 4: Run focused and affected suites**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeJsonFileTests.exe; .\tests\FrameScopeConfigStoreTests.exe; .\tests\FrameScopeReportProgressTests.exe; .\tests\FrameScopeSystemSamplerCpuCoreTests.exe`

Expected: all executables pass and failed replacement never corrupts the previous JSON.

- [ ] **Step 5: Commit safe state writes**

```powershell
git add src/core/FrameScopeJsonFile.cs src/core/FrameScopeConfigStore.cs src/core/FrameScopeReportProgress.cs src/app/FrameScopeNativeMonitor.MonitorSession.Status.cs src/app/FrameScopeNativeMonitor.ReportStatus.cs src/app/FrameScopeNativeMonitor.Watcher.cs src/diagnostics/FrameScopeDiagnostics.IO.cs src/monitoring/FrameScopeSystemSampler.IO.cs src/reporting/FrameScopeReportGenerator.cs tests/FrameScopeJsonFileTests.cs tests/Build-FrameScopeTests.ps1
git commit -m "fix: write persistent JSON atomically"
```

### Task 5: Diagnostics schema compatibility

**Files:**
- Modify: `src/diagnostics/FrameScopeDiagnostics.Sections.cs:128-215`
- Modify: `src/diagnostics/FrameScopeDiagnostics.IO.cs`
- Modify: `src/diagnostics/FrameScopeDiagnostics.Markdown.cs`
- Modify: `tests/FrameScopeDiagnosticsTests.cs`
- Test: `tests/FrameScopeDiagnosticsTests.exe`

- [ ] **Step 1: Add current-run and legacy-run failing fixtures**

```csharp
File.WriteAllText(Path.Combine(run, "status.json"),
    "{\"Phase\":\"done\",\"ReportHtml\":\"charts\\\\framescope-interactive-report.html\",\"ReportFrameCount\":240,\"ReportHasFrameData\":true,\"ReportKind\":\"partial\"}");
File.WriteAllText(Path.Combine(run, "monitor-error.txt"), "current monitor failure");
WriteDataJs(run, "{\"frameStats\":{\"average\":144.5,\"low1\":90.0,\"low01\":61.0,\"minInstant\":42.0}}");
Dictionary<string, object> report = GenerateDiagnostics(run);
AssertEqual(240, ReadNestedInt(report, "recentSession", "frames"), "current frame field");
AssertEqual("partial", ReadNestedString(report, "recentSession", "reportKind"), "current kind");
AssertContains(ReadErrors(report), "current monitor failure", "current error file");
AssertEqual(144.5, ReadNestedDouble(report, "fpsSummary", "averageFps"), "data.js frameStats");
```

Retain a second fixture using legacy `FrameCount`, `LastReport`, and `error.txt` and assert it remains readable.

- [ ] **Step 2: Run diagnostics to verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeDiagnosticsTests.exe`

Expected: current-run frame/path/error/stat assertions fail while the legacy fixture passes.

- [ ] **Step 3: Prefer canonical fields with explicit legacy fallbacks**

```csharp
{ "reportHtml", RedactForPrivacy(FirstNonEmpty(GetString(status, "ReportHtml"), GetString(summary, "ReportHtml"), GetString(status, "LastReport"))) },
{ "hasFrameData", FirstNonNull(GetObject(status, "ReportHasFrameData"), GetObject(manifest, "hasFrameData")) },
{ "reportKind", FirstNonEmpty(GetString(status, "ReportKind"), GetString(manifest, "reportKind")) },
{ "frames", FirstNonNull(GetObject(status, "ReportFrameCount"), GetObject(manifest, "frames"), GetObject(status, "FrameCount")) }
```

Read `monitor-error.txt` first and `error.txt` as legacy fallback. Parse the JSON assignment in `framescope-interactive-data.js` for `frameStats`; do not expect it in the manifest. Include process/system sampler health and report timeout fields in JSON and Markdown diagnostics.

- [ ] **Step 4: Verify both schemas**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeDiagnosticsTests.exe; .\tests\FrameScopeWebBridgeTests.exe`

Expected: current and legacy fixtures pass and agree on frames, report kind, paths, and errors.

- [ ] **Step 5: Commit diagnostics alignment**

```powershell
git add src/diagnostics/FrameScopeDiagnostics.Sections.cs src/diagnostics/FrameScopeDiagnostics.IO.cs src/diagnostics/FrameScopeDiagnostics.Markdown.cs tests/FrameScopeDiagnosticsTests.cs
git commit -m "fix: align diagnostics with current run schema"
```

### Task 6: Run/history retention with safety boundaries

**Files:**
- Create: `src/core/FrameScopeRunRetention.cs`
- Create: `tests/FrameScopeRunRetentionTests.cs`
- Modify: `tests/Build-FrameScopeTests.ps1`
- Modify: `src/diagnostics/FrameScopeDiagnostics.Retention.cs`
- Modify: `src/app/FrameScopeNativeMonitor.Watcher.cs:29-63`
- Modify: `src/app/FrameScopeNativeMonitor.ReportStatus.cs:110-160`
- Test: `tests/FrameScopeRunRetentionTests.exe`

- [ ] **Step 1: Add failing retention selection tests**

```csharp
private static void RetentionNeverSelectsActiveNewestOrOutsideRoot()
{
    List<FrameScopeRunRetentionCandidate> selected = FrameScopeRunRetention.Select(
        DataRoot,
        new[] { OldDone, OldActive, NewestDone, OutsideRoot },
        DateTime.UtcNow,
        14,
        1024L * 1024L * 1024L);
    AssertContains(selected, OldDone, "expired completed run");
    AssertNotContains(selected, OldActive, "active run");
    AssertNotContains(selected, NewestDone, "newest run per target");
    AssertNotContains(selected, OutsideRoot, "path boundary");
}
```

- [ ] **Step 2: Verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeRunRetentionTests.exe`

Expected: compilation fails because the retention selector is missing.

- [ ] **Step 3: Add safe selection, deletion, and history compaction**

```csharp
public static bool IsTerminalPhase(string phase)
{
    string value = (phase ?? "").Trim().ToLowerInvariant();
    return value == "done" || value == "error" || value == "timeout-waiting-for-target";
}
```

Canonicalize every candidate under `DataRoot`, preserve non-terminal runs and the newest run under each target directory, select expired completed runs first, then oldest completed runs until under `MaxLogDiskMb`. Delete only the selected run directories. Rebuild `framescope-history.jsonl` to contain entries whose `RunDir` still exists plus the newest 500 entries, using an atomic replacement.

- [ ] **Step 4: Verify policy and existing diagnostics retention**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeRunRetentionTests.exe; .\tests\FrameScopeDiagnosticsTests.exe`

Expected: safe selection and history compaction pass; no path outside the configured data root is touched.

- [ ] **Step 5: Commit run retention**

```powershell
git add src/core/FrameScopeRunRetention.cs src/diagnostics/FrameScopeDiagnostics.Retention.cs src/app/FrameScopeNativeMonitor.Watcher.cs src/app/FrameScopeNativeMonitor.ReportStatus.cs tests/FrameScopeRunRetentionTests.cs tests/Build-FrameScopeTests.ps1
git commit -m "fix: bound run and history retention"
```

### Task 7: Stage B/C integration verification

**Files:**
- Verify only

- [ ] **Step 1: Rebuild and run every native test**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; Get-ChildItem .\tests\FrameScope*Tests.exe | ForEach-Object { & $_.FullName; if ($LASTEXITCODE -ne 0) { throw "$($_.Name) failed" } }`

Expected: every test executable exits 0.

- [ ] **Step 2: Run chart and lightweight contract suites**

Run: `node .\tests\chart-sampling-tests.js; powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\lightweight-separation-tests.ps1`

Expected: both scripts report PASS.

- [ ] **Step 3: Check repository whitespace and temporary artifacts**

Run: `git diff --check; Get-ChildItem -Recurse -Directory -Filter '.framescope-report-*' | ForEach-Object { throw "temporary report directory remains: $($_.FullName)" }`

Expected: no output from `git diff --check` and no temporary report directory exception.
