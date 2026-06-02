# FrameScope PresentMon ETW Access Denied Retest Report

Date: 2026-05-24

Status: FAIL

## Verdict

This retest does not pass the requested end-to-end acceptance criteria.

The build, required C# tests, frontend verification, report-generator manifest normalization, `git diff --check`, and residual-process check passed.

However, the fresh fake PresentMon monitor-session retest did not satisfy the required `status.json` / `summary.json` assertions. Both fresh fake runs ended with `PresentMonExitCode=6`, `presentmon.csv` missing, and `frames=0`, but no `presentmon.stderr.log` was written in those fresh runs. Because `PresentMonStderrTail` was empty, FrameScope wrote:

```text
FrameCaptureStatus = no-presentmon-csv
FrameCaptureMessage = PresentMon 已启动，但没有创建 presentmon.csv。PUBG 场景下通常是渲染进程切换、FrameScope 与游戏权限级别不一致、全屏/覆盖层采集限制，或 ETW 采集被游戏/反作弊阻断。
```

The core requirement was to confirm that when PresentMon stderr contains:

```text
failed to start trace session: access denied
```

FrameScope no longer only writes `no-presentmon-csv`, and instead writes `presentmon-etw-access-denied` plus the Chinese permission explanation. Existing old fake evidence still shows that behavior when `presentmon.stderr.log` exists, but the fresh fake run path did not reproduce the stderr file reliably, so this retest cannot be marked PASS.

BF6 FPS is not fixed or recovered by this retest. No real BF6 recapture generated `presentmon.csv` or FPS frames in this run.

## Scope

Requested scope was respected:

- No source code changes.
- No installer run.
- No GitHub push.
- No release asset update.
- `build.ps1` was run as explicitly required; it refreshed local `dist` artifacts as a script side effect, but no installer was installed or published.

The only project document added by this retest is:

```text
docs\test-reports\2026-05-24-framescope-presentmon-etw-access-denied-retest-report.md
```

## Commands Run

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

`FrameScopeReportManifestTests.exe` also printed a generated diagnostic manifest where:

```text
frameCaptureStatus = presentmon-etw-access-denied
frames = 0
hasFrameData = false
reportKind = diagnostic
frameCaptureMessage = PresentMon 无法启动 ETW trace，需要管理员/Performance Log Users/系统 ETW 权限检查。
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

## Simulated Access Denied Report Regeneration

Run directory:

```text
artifacts\presentmon-etw-access-denied-20260524\Battlefield-6-access-denied-simulated
```

Command:

```text
.\FrameScopeReportGenerator.exe artifacts\presentmon-etw-access-denied-20260524\Battlefield-6-access-denied-simulated
```

Result:

```text
Exit code: 0
```

Manifest after regeneration:

```text
frameCaptureStatus = presentmon-etw-access-denied
frameCaptureMessage = PresentMon 无法启动 ETW trace，需要管理员/Performance Log Users/系统 ETW 权限检查。
presentMonFailureCategory = presentmon-etw-access-denied
presentMonEtwAccessDenied = true
frames = 0
reportKind = diagnostic
hasFrameData = false
presentMonCsvBytes = 0
presentMonCsvRows = 0
presentMonPreflightIsElevated = false
presentMonPreflightInPerformanceLogUsers = false
presentMonPreflightToolExists = true
presentMonPreflightEtwProbeAttempted = false
```

Report data checks:

```text
data contains presentmon-etw-access-denied: true
data contains ETW trace text: true
data contains PresentMon text: true
data contains failed to start trace session: access denied: true
```

But the raw simulated `status.json` and `summary.json` still contain legacy placeholders:

```text
status.json FrameCaptureStatus = no-presentmon-csv
summary.json FrameCaptureStatus = no-presentmon-csv
status.json FrameCaptureMessage = legacy placeholder
summary.json FrameCaptureMessage = legacy placeholder
```

Evidence:

```text
artifacts\presentmon-etw-access-denied-20260524\simulated-access-denied-retest-checks.json
```

## Fake PresentMon Monitor-Session Retest

### Fresh retest against `dist\FrameScopeMonitor-payload`

Run directory:

```text
artifacts\presentmon-etw-access-denied-20260524\monitor-session-fake-presentmon\runs-retest\FakeAccessDeniedRetest-20260524-140924
```

Result:

```text
monitorExitCode = 0
PresentMonExitCode = 6
PresentMonCsvExists = false
PresentMonCsvRows = 0
presentmon.stderr.log exists = false
PresentMonStderrTail = empty
status.json FrameCaptureStatus = no-presentmon-csv
summary.json FrameCaptureStatus = no-presentmon-csv
PresentMonFailureCategory = missing-presentmon-csv
PresentMonEtwAccessDenied = false
```

Preflight fields were present:

```text
PresentMonPreflightIsElevated = true
PresentMonPreflightInPerformanceLogUsers = false
PresentMonPreflightToolExists = true
PresentMonPreflightToolPath = ...\FrameScopeFakePresentMon.exe
PresentMonPreflightEtwProbeAttempted = false
PresentMonPreflightEtwProbeReason = Skipped to avoid opening an extra ETW trace session before capture.
```

Evidence:

```text
artifacts\presentmon-etw-access-denied-20260524\monitor-session-fake-presentmon\monitor-session-fake-presentmon-retest-verification.json
```

### Fresh retest against root `FrameScopeMonitor.exe`

Run directory:

```text
artifacts\presentmon-etw-access-denied-20260524\monitor-session-fake-presentmon\runs-retest-root\FakeAccessDeniedRoot-20260524-141120
```

Result:

```text
monitorExitCode = 0
PresentMonExitCode = 6
PresentMonCsvExists = false
PresentMonCsvRows = 0
presentmon.stderr.log exists = false
PresentMonStderrTail = empty
status.json FrameCaptureStatus = no-presentmon-csv
summary.json FrameCaptureStatus = no-presentmon-csv
PresentMonFailureCategory = missing-presentmon-csv
PresentMonEtwAccessDenied = false
```

Evidence:

```text
artifacts\presentmon-etw-access-denied-20260524\monitor-session-fake-presentmon\monitor-session-fake-presentmon-root-retest-verification.json
```

### Existing old fake evidence

Existing old fake run:

```text
artifacts\presentmon-etw-access-denied-20260524\monitor-session-fake-presentmon\runs\FakeAccessDenied-20260524-135833
```

This old run contains `presentmon.stderr.log` with:

```text
error: failed to start trace session: access denied.
       PresentMon requires either administrative privileges or to be run by a user in the
       "Performance Log Users" user group.  View the readme for more details.
```

In that old run, the status and summary are correct:

```text
status.json FrameCaptureStatus = presentmon-etw-access-denied
summary.json FrameCaptureStatus = presentmon-etw-access-denied
FrameCaptureMessage = PresentMon 无法启动 ETW trace，需要管理员/Performance Log Users/系统 ETW 权限检查。
PresentMonFailureCategory = presentmon-etw-access-denied
PresentMonEtwAccessDenied = true
PresentMonExitCode = 6
PresentMonCsvExists = false
PresentMonCsvRows = 0
```

This proves the classifier path can work when stderr is present. It does not clear the fresh-run failure, because the retest requirement asked to use a simulated or fake PresentMon run and validate `status.json` / `summary.json` now.

## Report Page Open / Screenshot Attempt

Attempted URL:

```text
file:///C:/Users/misakamiro/Documents/Codex/2026-05-02/files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d/artifacts/presentmon-etw-access-denied-20260524/Battlefield-6-access-denied-simulated/charts/framescope-interactive-report.html
```

Result:

```text
opened = false
screenshotCaptured = false
currentUrl = about:blank
```

Codex in-app browser rejected the `file://` URL under its URL security policy and explicitly said not to work around it with indirect execution, raw browser commands, alternate browser surfaces, or policy circumvention. I did not bypass the policy.

Evidence:

```text
artifacts\presentmon-etw-access-denied-20260524\report-page-open-attempt.json
```

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
| Run `FrameScopeReportManifestTests.exe` | PASS | Exit code 0 |
| Run `FrameScopeDiagnosticsTests.exe` | PASS | Exit code 0 |
| Run `FrameScopeWebBridgeTests.exe` | PASS | Exit code 0 |
| Run `tools\Run-Frontend.ps1 verify` | PASS | 5 files / 44 tests passed, Vite build passed |
| `status.json FrameCaptureStatus=presentmon-etw-access-denied` in fresh fake run | FAIL | Fresh fake runs wrote `no-presentmon-csv` |
| `summary.json FrameCaptureStatus=presentmon-etw-access-denied` in fresh fake run | FAIL | Fresh fake runs wrote `no-presentmon-csv` |
| Manifest `frameCaptureStatus=presentmon-etw-access-denied` | PASS | Simulated report regeneration output |
| `frames=0` | PASS | Manifest output |
| `reportKind=diagnostic` | PASS | Manifest output |
| `hasFrameData=false` | PASS | Manifest output |
| Message includes `PresentMon 无法启动 ETW trace` | PARTIAL | Manifest has it; fresh fake status/summary do not |
| Preflight fields exist | PASS | Fresh fake status/summary and manifest include preflight fields |
| Report page open/screenshot | BLOCKED | `file://` rejected by in-app browser policy |
| `git diff --check` | PASS | Exit code 0 |
| Residual process check | PASS | No matching residual processes |

## Conclusion

FAIL.

The regression tests and report-generator normalization path pass, but the fresh fake monitor-session retest does not prove the required product behavior for `status.json` and `summary.json`. The likely immediate issue is that the fresh fake PresentMon run did not persist `presentmon.stderr.log`, leaving `PresentMonStderrTail` empty and causing the monitor-session status writer to fall back to `no-presentmon-csv`.

BF6 FPS remains unrecovered and unproven. A PASS would require either a reliable fresh simulated/fake run where stderr is captured and `status.json` / `summary.json` both become `presentmon-etw-access-denied`, or a real BF6 recapture that creates `presentmon.csv` with FPS frame rows.
