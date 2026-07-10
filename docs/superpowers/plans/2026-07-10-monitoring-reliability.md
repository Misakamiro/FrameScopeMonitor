# Monitoring Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make concurrent monitor sessions isolated, keep multi-alias targets alive correctly, and classify each run from measured child-process health instead of optimistic defaults.

**Architecture:** Add small pure policy classes under `src/core` for PresentMon ownership, target lifetime, sampler health, and the canonical run classification. The native monitor records raw evidence for PresentMon, ProcessSampler, and SystemSampler; watcher, manifest, Bridge, and React consume the resulting `full|partial|diagnostic|error` contract.

**Tech Stack:** C#/.NET Framework 4.x, PowerShell build scripts, React 18, TypeScript, Vitest.

---

### Task 1: PresentMon session ownership and concurrent isolation

**Files:**
- Create: `src/core/FrameScopePresentMonSessionPolicy.cs`
- Create: `tests/FrameScopeMonitoringReliabilityTests.cs`
- Modify: `tests/Build-FrameScopeTests.ps1`
- Modify: `src/app/FrameScopeNativeMonitor.MonitorSession.cs:105-145,389-427`
- Modify: `src/app/FrameScopeNativeMonitor.MonitorSession.PresentMon.cs:18-125`
- Test: `tests/FrameScopeMonitoringReliabilityTests.exe`

- [ ] **Step 1: Write the failing ownership tests**

```csharp
private static void PresentMonCleanupStopsOnlyDeadOwners()
{
    string active = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4100, "a1");
    string stale = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4200, "b2");
    string other = "ThirdPartyPresentMon";
    List<string> selected = FrameScopePresentMonSessionPolicy.SelectStaleOwnedSessions(
        new[] { active, stale, other }, pid => pid == 4100).ToList();

    AssertSequence(new[] { stale }, selected, "only the dead FrameScope owner is stale");
}

private static void ConcurrentOwnersReceiveUniqueNames()
{
    string first = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4100, "a1");
    string second = FrameScopePresentMonSessionPolicy.CreateSessionName("PUBG", 4200, "a1");
    AssertNotEqual(first, second, "owner pid must isolate concurrent sessions");
}
```

- [ ] **Step 2: Build and run the new test to verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeMonitoringReliabilityTests.exe`

Expected: compilation fails because `FrameScopePresentMonSessionPolicy` does not exist.

- [ ] **Step 3: Add the minimal ownership policy**

```csharp
public static class FrameScopePresentMonSessionPolicy
{
    public const string Prefix = "FrameScopeNativePresentMon_";

    public static string CreateSessionName(string label, int ownerPid, string nonce)
    {
        string safe = new string((label ?? "run").Where(char.IsLetterOrDigit).Take(32).ToArray());
        if (safe.Length == 0) safe = "run";
        return Prefix + safe + "_p" + ownerPid.ToString(CultureInfo.InvariantCulture) + "_" + (nonce ?? "");
    }

    public static List<string> SelectStaleOwnedSessions(IEnumerable<string> sessions, Func<int, bool> isOwnerAlive)
    {
        var result = new List<string>();
        foreach (string session in sessions ?? Enumerable.Empty<string>())
        {
            int ownerPid;
            if (!TryGetOwnerPid(session, out ownerPid)) continue;
            if (!isOwnerAlive(ownerPid)) result.Add(session);
        }
        return result;
    }

    public static bool TryGetOwnerPid(string sessionName, out int ownerPid)
    {
        ownerPid = 0;
        if (String.IsNullOrWhiteSpace(sessionName) || !sessionName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        Match match = Regex.Match(sessionName, "_p(?<pid>[0-9]+)_", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && Int32.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out ownerPid);
    }
}
```

Generate each session name with the monitor worker PID plus a GUID nonce. Replace both calls to `CleanupFrameScopePresentMonSessions` with startup cleanup of `SelectStaleOwnedSessions(...)`; normal shutdown must call `RequestPresentMonStop` only for `presentMonSessionName` and retain `StopMonitorChild(presentMon, ...)` as the owned-process fallback.

- [ ] **Step 4: Verify GREEN and the collision regression**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeMonitoringReliabilityTests.exe`

Expected: `FrameScope monitoring reliability tests passed.` and the test proves an active peer session is never selected for cleanup.

- [ ] **Step 5: Commit the isolated session fix**

```powershell
git add src/core/FrameScopePresentMonSessionPolicy.cs src/app/FrameScopeNativeMonitor.MonitorSession.cs src/app/FrameScopeNativeMonitor.MonitorSession.PresentMon.cs tests/FrameScopeMonitoringReliabilityTests.cs tests/Build-FrameScopeTests.ps1
git commit -m "fix: isolate concurrent PresentMon sessions"
```

### Task 2: Multi-alias target lifetime

**Files:**
- Create: `src/core/FrameScopeTargetLifecycle.cs`
- Modify: `src/app/FrameScopeNativeMonitor.MonitorSession.cs:321-380`
- Modify: `tests/FrameScopeMonitoringReliabilityTests.cs`
- Modify: `tests/Build-FrameScopeTests.ps1`
- Test: `tests/FrameScopeMonitoringReliabilityTests.exe`

- [ ] **Step 1: Add failing policy tests**

```csharp
private static void AliasReplacementKeepsCaptureAlive()
{
    AssertFalse(FrameScopeTargetLifecycle.ShouldStopCapture(true, true, true, false),
        "selected pid exit must not stop while another alias is alive");
    AssertTrue(FrameScopeTargetLifecycle.ShouldStopCapture(true, true, false, false),
        "capture stops after every alias exits");
    AssertTrue(FrameScopeTargetLifecycle.ShouldStopCapture(false, false, true, true),
        "timed capture stops at its deadline");
}

private static void AliasOrderProducesOneWatcherKey()
{
    string first = FrameScopeTargetLifecycle.CanonicalTargetKey(new[] { "TslGame", "TslGame_BE" });
    string second = FrameScopeTargetLifecycle.CanonicalTargetKey(new[] { "tslgame_be.exe", "tslgame.exe" });
    AssertEqual(first, second, "alias order and exe suffix cannot create duplicate monitors");
}
```

- [ ] **Step 2: Run the focused test to verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeMonitoringReliabilityTests.exe`

Expected: compilation fails because `FrameScopeTargetLifecycle` is absent.

- [ ] **Step 3: Implement the policy and use it in the capture loop**

```csharp
public static class FrameScopeTargetLifecycle
{
    public static bool ShouldStopCapture(bool untilTargetExit, bool selectedPidExited, bool anyAliasRunning, bool deadlineReached)
    {
        if (!untilTargetExit) return deadlineReached;
        return selectedPidExited && !anyAliasRunning;
    }

    public static string CanonicalTargetKey(IEnumerable<string> aliases)
    {
        return String.Join("|", (aliases ?? Enumerable.Empty<string>())
            .Select(value => Path.GetFileNameWithoutExtension((value ?? "").Trim()).ToLowerInvariant())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }
}
```

Use `CanonicalTargetKey` for the watcher's `activeMonitors` dictionary so alias order/casing cannot launch a duplicate worker. When `targetProc.WaitForExit(remainingMs)` returns true, query `IsAnyTargetProcessRunning(targetProcessBases)`. Continue with a bounded `Thread.Sleep(remainingMs)` when another alias is alive; break only when the policy returns true. Preserve the timed-capture deadline behavior.

- [ ] **Step 4: Rebuild and verify GREEN**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeMonitoringReliabilityTests.exe`

Expected: all monitoring reliability cases pass without a busy loop.

- [ ] **Step 5: Commit the alias lifecycle fix**

```powershell
git add src/core/FrameScopeTargetLifecycle.cs src/app/FrameScopeNativeMonitor.MonitorSession.cs tests/FrameScopeMonitoringReliabilityTests.cs tests/Build-FrameScopeTests.ps1
git commit -m "fix: track the full target alias lifetime"
```

### Task 3: Child sampler health and canonical run classification

**Files:**
- Create: `src/core/FrameScopeRunContract.cs`
- Modify: `src/app/FrameScopeNativeMonitor.MonitorSession.cs:240-478`
- Modify: `src/app/FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs`
- Modify: `src/app/FrameScopeNativeMonitor.MonitorSession.Models.cs`
- Modify: `src/app/FrameScopeNativeMonitor.MonitorSession.Paths.cs`
- Modify: `src/app/FrameScopeNativeMonitor.MonitorSession.Status.cs`
- Modify: `src/app/FrameScopeNativeMonitor.ReportOrchestration.Models.cs`
- Modify: `src/app/FrameScopeNativeMonitor.ReportStatus.cs`
- Modify: `src/reporting/FrameScopeReportGenerator.Metadata.cs`
- Modify: `tests/FrameScopeMonitoringReliabilityTests.cs`
- Modify: `tests/FrameScopeReportManifestTests.cs`
- Modify: `tests/Build-FrameScopeTests.ps1`
- Test: `tests/FrameScopeMonitoringReliabilityTests.exe`
- Test: `tests/FrameScopeReportManifestTests.exe`

- [ ] **Step 1: Add failing classification tests**

```csharp
private static void MissingAuxiliarySamplesCannotBeFull()
{
    var process = new FrameScopeSamplerHealth { Started = true, ValidRows = 25, ExitCode = 0 };
    var system = new FrameScopeSamplerHealth { Started = true, ValidRows = 0, ExitCode = 9, ExitedEarly = true };
    AssertEqual("partial", FrameScopeRunContract.ClassifyReportKind(300, process, system), "failed system sampler");
    AssertEqual("full", FrameScopeRunContract.ClassifyReportKind(300, process,
        new FrameScopeSamplerHealth { Started = true, ValidRows = 20, ExitCode = 0 }), "all streams valid");
    AssertEqual("diagnostic", FrameScopeRunContract.ClassifyReportKind(0, process, system), "auxiliary-only run");
    AssertEqual("error", FrameScopeRunContract.ClassifyReportKind(0,
        new FrameScopeSamplerHealth(), new FrameScopeSamplerHealth()), "no usable evidence");
}
```

Add a manifest regression fixture with frame rows present, process rows present, and zero system rows; assert `reportKind == "partial"`.

- [ ] **Step 2: Verify both tests fail for the expected optimistic classification**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeMonitoringReliabilityTests.exe; .\tests\FrameScopeReportManifestTests.exe`

Expected: the core class is initially missing, then the manifest assertion reports `full` instead of `partial`.

- [ ] **Step 3: Add the canonical health model**

```csharp
public sealed class FrameScopeSamplerHealth
{
    public bool Started;
    public int? ProcessId;
    public string StartedAt = "";
    public string ExitedAt = "";
    public int? ExitCode;
    public bool ExitedEarly;
    public bool StoppedByOwner;
    public bool CsvExists;
    public long CsvBytes;
    public int ValidRows;
    public string Status = "not-started";
    public string ErrorTail = "";

    public bool IsHealthy { get { return Started && ValidRows > 0 && !ExitedEarly && (!ExitCode.HasValue || ExitCode.Value == 0 || StoppedByOwner); } }
}

public static class FrameScopeRunContract
{
    public static string ClassifyReportKind(int frameRows, FrameScopeSamplerHealth process, FrameScopeSamplerHealth system)
    {
        bool hasFrames = frameRows > 0;
        bool hasAux = (process != null && process.ValidRows > 0) || (system != null && system.ValidRows > 0);
        if (hasFrames && process != null && process.IsHealthy && system != null && system.IsHealthy) return "full";
        if (hasFrames) return "partial";
        return hasAux ? "diagnostic" : "error";
    }
}
```

Add dedicated stdout/stderr paths for ProcessSampler and SystemSampler and pass them to `StartNativeMonitorChild`. Record each sampler's start PID/time, early exit, final exit code, owner stop, CSV existence/bytes/data rows, status, and bounded stderr/log tail in `status.json` and `summary.json`. `ReportGenerationResult.ReportKind`, manifest metadata, history, and Bridge must use `ClassifyReportKind`; none may promote `partial` to `full` merely because PresentMon has frames.

- [ ] **Step 4: Verify health status and manifest classification**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeMonitoringReliabilityTests.exe; .\tests\FrameScopeReportManifestTests.exe; .\tests\FrameScopeLoggingPolicyTests.exe`

Expected: all four executables pass and sampler failure produces `partial`.

- [ ] **Step 5: Commit the canonical classification**

```powershell
git add src/core/FrameScopeRunContract.cs src/app/FrameScopeNativeMonitor.MonitorSession.cs src/app/FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs src/app/FrameScopeNativeMonitor.MonitorSession.Models.cs src/app/FrameScopeNativeMonitor.MonitorSession.Paths.cs src/app/FrameScopeNativeMonitor.MonitorSession.Status.cs src/app/FrameScopeNativeMonitor.ReportOrchestration.Models.cs src/app/FrameScopeNativeMonitor.ReportStatus.cs src/reporting/FrameScopeReportGenerator.Metadata.cs tests/FrameScopeMonitoringReliabilityTests.cs tests/FrameScopeReportManifestTests.cs tests/Build-FrameScopeTests.ps1
git commit -m "fix: persist sampler health and classify partial runs"
```

### Task 4: Propagate partial status through Bridge and React

**Files:**
- Modify: `src/app/FrameScopeWebBridge.Reports.cs:251-279,410-445`
- Modify: `src/frontend/src/bridge/contract.ts:180-210`
- Modify: `src/frontend/src/pages/ReportsPage.tsx:270-305,535-548`
- Modify: `src/frontend/src/data/mockPreview.ts:310-335`
- Modify: `tests/FrameScopeWebBridgeTests.cs`
- Modify: `src/frontend/src/uiInteractionContract.test.ts`
- Test: `tests/FrameScopeWebBridgeTests.exe`
- Test: frontend Vitest suite

- [ ] **Step 1: Add failing Bridge and UI contract tests**

```csharp
File.WriteAllText(Path.Combine(runDir, "status.json"),
    "{\"Phase\":\"done\",\"ReportKind\":\"partial\",\"ReportFrameCount\":120,\"ProcessSamplerStatus\":\"healthy\",\"SystemSamplerStatus\":\"failed\"}");
Dictionary<string, object> report = FirstReport(bridge);
AssertEqual("partial", AsString(report, "reportKind"), "Bridge preserves partial classification");
```

```ts
expect(reportsPageSource).toContain('case "partial"');
expect(reportsPageSource).toContain("部分数据");
```

- [ ] **Step 2: Run focused tests to verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeWebBridgeTests.exe; .\tools\Run-Frontend.ps1 test`

Expected: Bridge/UI expectations fail because the partial label and health fields are not exposed.

- [ ] **Step 3: Add typed fields and explicit labels**

```ts
export type ReportKind = "full" | "partial" | "diagnostic" | "error" | "pending";

function formatReportKind(kind: ReportKind) {
  switch (kind) {
    case "full": return "完整数据";
    case "partial": return "部分数据";
    case "diagnostic": return "诊断数据";
    case "error": return "生成失败";
    default: return "生成中";
  }
}
```

Expose process/system sampler status and valid-row counts from the Bridge report payload, show `partial` with warning tone, and keep it openable when a complete report artifact set exists.

- [ ] **Step 4: Verify native and frontend GREEN**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeWebBridgeTests.exe; .\tools\Run-Frontend.ps1 verify`

Expected: Bridge tests pass; TypeScript, 64+ Vitest tests, and production build pass.

- [ ] **Step 5: Commit status propagation**

```powershell
git add src/app/FrameScopeWebBridge.Reports.cs src/frontend/src/bridge/contract.ts src/frontend/src/pages/ReportsPage.tsx src/frontend/src/data/mockPreview.ts tests/FrameScopeWebBridgeTests.cs src/frontend/src/uiInteractionContract.test.ts
git commit -m "fix: surface partial monitoring reports"
```

### Task 5: Stage A integration verification

**Files:**
- Verify only

- [ ] **Step 1: Rebuild all native tests**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`

Expected: `FrameScope tests rebuilt.` with no compiler errors or warnings.

- [ ] **Step 2: Run every native test executable**

Run: `Get-ChildItem .\tests\FrameScope*Tests.exe | ForEach-Object { & $_.FullName; if ($LASTEXITCODE -ne 0) { throw "$($_.Name) failed" } }`

Expected: every executable exits 0.

- [ ] **Step 3: Run the frontend verification**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`

Expected: clean install, typecheck, Vitest, and Vite production build pass.

- [ ] **Step 4: Run the fake monitoring simulation**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1`

Expected: simulator completes, creates a report, and leaves no `FrameScopeNativePresentMon_*` session or FakePresentMon process.
