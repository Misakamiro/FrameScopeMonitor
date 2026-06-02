# FrameScope PresentMon stderr Capture Fix Report

Date: 2026-05-24

Status: PASS

## Verdict

The PresentMon stderr capture and persistence chain is fixed for the fresh fake PresentMon monitor-session case.

The final fresh fake run wrote `presentmon.stderr.log`, and `status.json`, `summary.json`, and `charts\framescope-interactive-manifest.json` all classified the same stderr tail as:

```text
presentmon-etw-access-denied
```

No FPS data was fabricated. The manifest remains diagnostic-only:

```text
frames = 0
hasFrameData = false
reportKind = diagnostic
```

No installer was run, no GitHub push was performed, and no UI visual files were edited by this fix.

## Root Cause

The fresh fake run failed for two linked reasons in the PresentMon child-process stderr path, not because of the classifier.

1. `StartNativeMonitorChild(...)` redirected stderr and copied it on a background thread with `ReadToEnd()` followed by `File.WriteAllText(...)`, but the monitor-session finalization path called `Process.WaitForExit(...)` and immediately read `presentmon.stderr.log` without waiting for the pipe-copy thread to finish.

2. The failing fresh fake path was long enough to hit the legacy Windows/.NET Framework path boundary. The final reproduced bad path length was:

```text
presentmon.stderr.log path length = 260
```

The older fake run that classified correctly used a shorter stderr path:

```text
old working presentmon.stderr.log path length = 247
```

On the 260-character path, `Path.GetDirectoryName(...)` / normal `File.WriteAllText(...)` can throw `PathTooLongException`. That exception was swallowed inside the background pipe-copy thread, so the visible symptom was:

```text
presentmon.stderr.log exists = false
PresentMonStderrTail = empty
FrameCaptureStatus = no-presentmon-csv
```

## Changed Files

```text
src\app\FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs
src\app\FrameScopeNativeMonitor.MonitorSession.cs
src\core\FrameScopePresentMonDiagnostics.cs
tests\Build-FrameScopeTests.ps1
tests\FrameScopeNativeMonitorChildProcessTests.cs
docs\implementation-reports\2026-05-24-framescope-presentmon-stderr-capture-fix-report.md
```

## What Changed

`StartNativeMonitorChild(...)` now records stdout/stderr pipe-copy threads and exposes explicit child-output draining:

```text
WaitForNativeMonitorChildExit(...)
WaitForNativeMonitorChildOutput(...)
```

The PresentMon finalization path now waits for the redirected stdout/stderr copy to finish before building `BuildPresentMonCaptureDiagnostics(...)`. That makes `status.json` and `summary.json` use the same final stderr tail.

`FrameScopePresentMonDiagnostics` now has narrow file helpers for PresentMon diagnostic text:

```text
WriteAllText(...)
ReadAllText(...)
FileExists(...)
```

Those helpers avoid the legacy 260-character path failure by using long-path-aware Win32 file open behavior and by avoiding `Path.GetDirectoryName(...)` on already-long paths.

The fallback behavior is preserved: when there is truly no stderr text and no specific classification, missing CSV still remains:

```text
no-presentmon-csv
```

## Regression Coverage

Added:

```text
tests\FrameScopeNativeMonitorChildProcessTests.cs
```

It covers:

- a short-lived child writing access-denied stderr and exiting `6`
- stdout/stderr pipe draining after process exit
- `presentmon.stderr.log` write/read at the Windows legacy path boundary
- a real `FrameScopeMonitor.exe --monitor-session` fake PresentMon run where both `status.json` and `summary.json` must become `presentmon-etw-access-denied`

Updated:

```text
tests\Build-FrameScopeTests.ps1
```

It now builds:

```text
FrameScopeNativeMonitorChildProcessTests.exe
```

## Fresh Fake Run Evidence

Final fresh fake run:

```text
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\pm-stderr-fix\runs-final\FreshADFixedFinal-20260524-150230
```

Evidence file:

```text
artifacts\pm-stderr-fix\monitor-session-fake-presentmon-fixed-verification.json
```

Key result:

```text
monitorExitCode = 0
reportExitCode = 0
presentmon.stderr.log exists = true
presentmon.stderr.log bytes = 227
PresentMonExitCode = 6
PresentMonCsvExists = false
PresentMonCsvRows = 0
```

`presentmon.stderr.log` contains:

```text
error: failed to start trace session: access denied.
PresentMon requires either administrative privileges or to be run by a user in the
"Performance Log Users" user group.
```

`status.json` key fields:

```text
Phase = done
FrameCaptureStatus = presentmon-etw-access-denied
PresentMonFailureCategory = presentmon-etw-access-denied
PresentMonEtwAccessDenied = true
PresentMonExitCode = 6
PresentMonCsvExists = false
PresentMonCsvRows = 0
PresentMonStderrTail includes failed to start trace session: access denied
```

`summary.json` key fields:

```text
FrameCaptureStatus = presentmon-etw-access-denied
PresentMonFailureCategory = presentmon-etw-access-denied
PresentMonEtwAccessDenied = true
PresentMonExitCode = 6
PresentMonCsvExists = false
PresentMonCsvRows = 0
PresentMonStderrTail includes failed to start trace session: access denied
```

Manifest key fields:

```text
frameCaptureStatus = presentmon-etw-access-denied
frameCaptureMessage = PresentMon 无法启动 ETW trace，需要管理员/Performance Log Users/系统 ETW 权限检查。
presentMonFailureCategory = presentmon-etw-access-denied
presentMonEtwAccessDenied = true
frames = 0
hasFrameData = false
reportKind = diagnostic
```

## Verification Results

PASS:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Result:

```text
Build complete: ...\dist\FrameScopeMonitor-Setup.exe
Full setup complete: ...\dist\FrameScopeMonitor-Full-Setup.exe
Exit code: 0
```

PASS:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
```

Result:

```text
FrameScope tests rebuilt.
Exit code: 0
```

PASS:

```text
.\tests\FrameScopePresentMonDiagnosticsTests.exe
.\tests\FrameScopeNativeMonitorChildProcessTests.exe
.\tests\FrameScopeReportManifestTests.exe
.\tests\FrameScopeDiagnosticsTests.exe
```

Results:

```text
FrameScopePresentMonDiagnosticsTests: PASS
FrameScopeNativeMonitorChildProcessTests: PASS
FrameScopeReportManifestTests: PASS
FrameScopeDiagnosticsTests: PASS
```

PASS:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result:

```text
typecheck passed
5 test files passed
44 tests passed
vite build passed
Exit code: 0
```

PASS:

```text
git -c safe.directory='C:/Users/misakamiro/Documents/Codex/2026-05-02/files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d' diff --check
```

Result:

```text
Exit code: 0
```

Git printed LF-to-CRLF warnings for existing modified files, but no whitespace errors.

PASS:

```text
Residual process check
```

Result:

```text
NO_MATCHING_RESIDUAL_PROCESSES
```

Checked for:

```text
FrameScopeMonitor
FrameScopeReportGenerator
FrameScopeProcessSampler
FrameScopeSystemSampler
PresentMon-2.4.1-x64
FrameScopeFakePresentMon
FrameScopeFakeTarget
```

## Retest Recommendation

Yes. The tester should retest the original fresh fake PresentMon monitor-session path.

Expected result:

```text
presentmon.stderr.log exists = true
status.json FrameCaptureStatus = presentmon-etw-access-denied
summary.json FrameCaptureStatus = presentmon-etw-access-denied
manifest frameCaptureStatus = presentmon-etw-access-denied
frames = 0
hasFrameData = false
reportKind = diagnostic
```

If BF6 still fails to create `presentmon.csv`, this fix should make the failure explicit as an ETW permission/access-denied diagnostic instead of collapsing it into generic `no-presentmon-csv`.
