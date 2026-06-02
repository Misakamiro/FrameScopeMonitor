# FrameScope PresentMon stderr Capture Retest Report

Date: 2026-05-24

Status: PASS

## Verdict

PASS for the requested stderr capture and ETW access-denied diagnostic retest.

The fresh fake PresentMon monitor-session matrix no longer reproduces the previous failure. In new non-`runs-final` directories, fake PresentMon wrote `failed to start trace session: access denied` to stderr and exited `6`; FrameScope persisted `presentmon.stderr.log`; `status.json`, `summary.json`, and `charts\framescope-interactive-manifest.json` all classified the run as:

```text
presentmon-etw-access-denied
```

The missing-CSV regression check also passed. A fake PresentMon run with no stderr text and no `presentmon.csv` stayed classified as:

```text
no-presentmon-csv
```

This PASS only covers stderr capture and the ETW access-denied diagnostic fix. It does not prove BF6 FPS recovery. No real BF6 recapture was run, no real BF6 `presentmon.csv` was generated, and no FPS frames were observed in this retest.

## Scope

Requested scope was respected:

- No source code edits.
- No installer run.
- No GitHub push.
- No release asset update.
- `build.ps1` was run because it was explicitly required; it refreshed local `dist` build artifacts as a script side effect.

This report was added:

```text
docs\test-reports\2026-05-24-framescope-presentmon-stderr-capture-retest-report.md
```

## Required Commands

### Build

Command:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Result:

```text
Build complete: ...\dist\FrameScopeMonitor-Setup.exe
Full setup complete: ...\dist\FrameScopeMonitor-Full-Setup.exe
Exit code: 0
```

### Test Build

Command:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
```

Result:

```text
FrameScope tests rebuilt.
Exit code: 0
```

### Required Test Executables

Commands and results:

```text
.\tests\FrameScopePresentMonDiagnosticsTests.exe
FrameScopePresentMonDiagnosticsTests: PASS
Exit code: 0

.\tests\FrameScopeNativeMonitorChildProcessTests.exe
FrameScopeNativeMonitorChildProcessTests: PASS
Exit code: 0

.\tests\FrameScopeReportManifestTests.exe
FrameScopeReportManifestTests: PASS
Exit code: 0

.\tests\FrameScopeDiagnosticsTests.exe
FrameScopeDiagnosticsTests: PASS
Exit code: 0

.\tests\FrameScopeWebBridgeTests.exe
FrameScopeWebBridgeTests: PASS
Exit code: 0
```

`FrameScopeReportManifestTests.exe` also printed a generated diagnostic manifest containing:

```text
frameCaptureStatus = presentmon-etw-access-denied
frames = 0
hasFrameData = false
reportKind = diagnostic
```

### Frontend Verify

Command:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result:

```text
added 110 packages
typecheck passed
5 test files passed
44 tests passed
vite build passed
Exit code: 0
```

## Fresh Fake PresentMon Monitor-Session Matrix

Final evidence root:

```text
artifacts\pmrt-164814
```

Structured evidence:

```text
artifacts\pmrt-164814\matrix.json
artifacts\pmrt-164814\report-generator-explicit-exit-codes.json
```

The final matrix used fresh directories and did not reuse old `runs-final` evidence:

```text
artifacts\pmrt-164814\adr\ADRoot-20260524-164815
artifacts\pmrt-164814\adp\ADPayload-20260524-164828
artifacts\pmrt-164814\mcr\MCRoot-20260524-164841
artifacts\pmrt-164814\mcp\MCPayload-20260524-164853
```

The `ad-root` and `ad-payload` runs used `tests\FrameScopeNativeMonitorChildProcessTests.exe` as fake PresentMon. That fake writes the PresentMon access-denied stderr text and exits `6`.

The `mc-root` and `mc-payload` runs used `tools\FrameScopePubgSimulator\bin\FakePresentMon.exe` with `FRAMESCOPE_FAKE_PRESENTMON_SCENARIO=missing-csv`. That fake generated no `presentmon.csv` and no stderr text. The redirected `presentmon.stderr.log` file exists with only a UTF-8 BOM (`3` bytes), so the semantic stderr text length is `0`.

## Access-Denied Results

### Root binary run

Run directory:

```text
artifacts\pmrt-164814\adr\ADRoot-20260524-164815
```

Key checks:

```text
presentmon.stderr.log exists = true
presentmon.stderr.log bytes = 227
presentmon.stderr.log contains "failed to start trace session: access denied" = true
PresentMonExitCode = 6
PresentMonCsvExists = false
PresentMonCsvRows = 0
status.json FrameCaptureStatus = presentmon-etw-access-denied
summary.json FrameCaptureStatus = presentmon-etw-access-denied
status.json PresentMonEtwAccessDenied = true
summary.json PresentMonEtwAccessDenied = true
status.json PresentMonFailureCategory = presentmon-etw-access-denied
summary.json PresentMonFailureCategory = presentmon-etw-access-denied
manifest frameCaptureStatus = presentmon-etw-access-denied
manifest frames = 0
manifest hasFrameData = false
manifest reportKind = diagnostic
```

### Payload binary run

Run directory:

```text
artifacts\pmrt-164814\adp\ADPayload-20260524-164828
```

Key checks:

```text
presentmon.stderr.log exists = true
presentmon.stderr.log bytes = 227
presentmon.stderr.log contains "failed to start trace session: access denied" = true
PresentMonExitCode = 6
PresentMonCsvExists = false
PresentMonCsvRows = 0
status.json FrameCaptureStatus = presentmon-etw-access-denied
summary.json FrameCaptureStatus = presentmon-etw-access-denied
status.json PresentMonEtwAccessDenied = true
summary.json PresentMonEtwAccessDenied = true
status.json PresentMonFailureCategory = presentmon-etw-access-denied
summary.json PresentMonFailureCategory = presentmon-etw-access-denied
manifest frameCaptureStatus = presentmon-etw-access-denied
manifest frames = 0
manifest hasFrameData = false
manifest reportKind = diagnostic
```

Report generator was rerun explicitly against both access-denied run directories and returned:

```text
ad-root report generator exit code = 0
ad-payload report generator exit code = 0
stderr bytes = 0
```

## Missing-CSV Regression Results

### Root binary run

Run directory:

```text
artifacts\pmrt-164814\mcr\MCRoot-20260524-164841
```

Key checks:

```text
presentmon.stderr.log exists = true
presentmon.stderr.log bytes = 3
presentmon.stderr.log semantic text length = 0
presentmon.stderr.log contains "failed to start trace session: access denied" = false
PresentMonExitCode = 0
PresentMonCsvExists = false
PresentMonCsvRows = 0
status.json FrameCaptureStatus = no-presentmon-csv
summary.json FrameCaptureStatus = no-presentmon-csv
status.json PresentMonEtwAccessDenied = false
summary.json PresentMonEtwAccessDenied = false
status.json PresentMonFailureCategory = missing-presentmon-csv
summary.json PresentMonFailureCategory = missing-presentmon-csv
manifest frameCaptureStatus = no-presentmon-csv
manifest frames = 0
manifest hasFrameData = false
manifest reportKind = diagnostic
```

### Payload binary run

Run directory:

```text
artifacts\pmrt-164814\mcp\MCPayload-20260524-164853
```

Key checks:

```text
presentmon.stderr.log exists = true
presentmon.stderr.log bytes = 3
presentmon.stderr.log semantic text length = 0
presentmon.stderr.log contains "failed to start trace session: access denied" = false
PresentMonExitCode = 0
PresentMonCsvExists = false
PresentMonCsvRows = 0
status.json FrameCaptureStatus = no-presentmon-csv
summary.json FrameCaptureStatus = no-presentmon-csv
status.json PresentMonEtwAccessDenied = false
summary.json PresentMonEtwAccessDenied = false
status.json PresentMonFailureCategory = missing-presentmon-csv
summary.json PresentMonFailureCategory = missing-presentmon-csv
manifest frameCaptureStatus = no-presentmon-csv
manifest frames = 0
manifest hasFrameData = false
manifest reportKind = diagnostic
```

Report generator was rerun explicitly against both missing-CSV run directories and returned:

```text
mc-root report generator exit code = 0
mc-payload report generator exit code = 0
stderr bytes = 0
```

## Harness Notes

During the retest I first tried a longer `artifacts\pm-stderr-capture-retest-20260524\...` path. That produced valid access-denied evidence, but one payload missing-CSV attempt failed to create a run directory because the temporary evidence path and run prefix pushed the legacy .NET path length boundary. I did not use that partial matrix as final evidence.

I then reran the matrix under the shorter `artifacts\pmrt-164814` path. That final matrix is the evidence used for this PASS.

A later `pmrt2-*` attempt was only a harness experiment to capture monitor process exit codes through a different process-launch path. The experiment passed malformed arguments to `FrameScopeMonitor.exe`, causing it to enter the normal UI path and time out. It produced no product evidence and left no residual FrameScope process. It is excluded from the product verdict.

The stable final matrix did produce complete `done` phase `status.json` / `summary.json` artifacts and report manifests. The product assertions required by this retest were verified from those files.

## File URL Screenshot

No report-page `file://` screenshot was required by this retest checklist, and I did not attempt to bypass the previously observed Codex in-app browser `file://` security policy.

## Git Diff Check

Command:

```text
git -c safe.directory='C:/Users/misakamiro/Documents/Codex/2026-05-02/files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d' diff --check
```

Result:

```text
Exit code: 0
```

Git printed LF-to-CRLF warnings for existing modified files, but no whitespace errors were reported.

## Residual Process Check

Checked for:

```text
FrameScopeMonitor
FrameScopeReportGenerator
FrameScopeProcessSampler
FrameScopeSystemSampler
PresentMon-2.4.1-x64
FrameScopeFakePresentMon
FrameScopeFakeTarget
FrameScopeNativeMonitorChildProcessTests
FakePresentMon
```

Result:

```text
NO_MATCHING_RESIDUAL_PROCESSES
```

## Final Requirement Matrix

| Requirement | Result | Evidence |
| --- | --- | --- |
| Run `build.ps1` | PASS | Exit code 0 |
| Run `tests\Build-FrameScopeTests.ps1` | PASS | Exit code 0 |
| Run `FrameScopePresentMonDiagnosticsTests.exe` | PASS | Exit code 0 |
| Run `FrameScopeNativeMonitorChildProcessTests.exe` | PASS | Exit code 0 |
| Run `FrameScopeReportManifestTests.exe` | PASS | Exit code 0 |
| Run `FrameScopeDiagnosticsTests.exe` | PASS | Exit code 0 |
| Run `FrameScopeWebBridgeTests.exe` | PASS | Exit code 0 |
| Run `tools\Run-Frontend.ps1 verify` | PASS | 5 files / 44 tests passed, Vite build passed |
| Fresh fake access-denied `presentmon.stderr.log` exists | PASS | `ad-root`, `ad-payload` |
| Fresh fake access-denied stderr contains `failed to start trace session: access denied` | PASS | `ad-root`, `ad-payload` |
| Fresh fake access-denied `PresentMonExitCode=6` | PASS | status/summary |
| Fresh fake access-denied `PresentMonCsvExists=false` | PASS | status/summary |
| Fresh fake access-denied `PresentMonCsvRows=0` | PASS | status/summary |
| Fresh fake access-denied status/summary classify `presentmon-etw-access-denied` | PASS | `ad-root`, `ad-payload` |
| Fresh fake access-denied status/summary ETW flag and category | PASS | `PresentMonEtwAccessDenied=true`, `PresentMonFailureCategory=presentmon-etw-access-denied` |
| Fresh fake access-denied manifest classification | PASS | `frameCaptureStatus=presentmon-etw-access-denied` |
| Fresh fake access-denied manifest diagnostic shape | PASS | `frames=0`, `hasFrameData=false`, `reportKind=diagnostic` |
| True no-stderr-text missing-CSV remains `no-presentmon-csv` | PASS | `mc-root`, `mc-payload` |
| `git diff --check` | PASS | Exit code 0 |
| Residual process check | PASS | `NO_MATCHING_RESIDUAL_PROCESSES` |

## Conclusion

PASS.

The stderr capture fix and ETW access-denied diagnostic classification pass the requested fresh fake PresentMon monitor-session retest. The previous FAIL condition is not reproduced in the final fresh matrix: `presentmon.stderr.log` is now persisted, and status, summary, and manifest all classify access-denied stderr as `presentmon-etw-access-denied`.

The missing-CSV regression remains correct: when there is no access-denied stderr text, FrameScope does not misclassify the run and keeps `no-presentmon-csv`.

BF6 FPS recovery is not claimed. This retest did not run a real BF6 capture and did not generate real BF6 FPS frame data.
