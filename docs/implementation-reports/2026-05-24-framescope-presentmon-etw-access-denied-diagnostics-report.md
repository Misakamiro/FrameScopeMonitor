# FrameScope PresentMon ETW Access Denied Diagnostics Report

Date: 2026-05-24

Status: PARTIAL

## Verdict

The backend diagnostic fix is implemented and verified.

The remaining PARTIAL item is the requested report-page screenshot: the Codex in-app browser refused to open the local `file:///` report URL because of its URL security policy, and the policy explicitly disallowed using an alternate browser workaround for the same blocked action. The simulated report HTML, manifest, and data file were generated and verified on disk, but no screenshot artifact was safely captured in this session.

## Root Cause

The BF6 artifacts already proved that PresentMon exited before writing `presentmon.csv` because Windows denied ETW trace-session startup:

```text
error: failed to start trace session: access denied.
```

Current code previously collapsed that condition into the generic `no-presentmon-csv` status. That was technically true at the file level, but too vague for BF6 because it hid PresentMon's explicit ETW permission failure.

## What Changed

Added a shared PresentMon diagnostic helper:

```text
src\core\FrameScopePresentMonDiagnostics.cs
```

It now classifies stderr containing `failed to start trace session` plus `access denied` as:

```text
FrameCaptureStatus = presentmon-etw-access-denied
FrameCaptureMessage = PresentMon 无法启动 ETW trace，需要管理员/Performance Log Users/系统 ETW 权限检查。
PresentMonFailureCategory = presentmon-etw-access-denied
PresentMonEtwAccessDenied = true
```

Updated monitor-session status and summary writing so startup preflight fields are present in `status.json` and `summary.json` across the run phases:

```text
PresentMonPreflightIsElevated
PresentMonPreflightInPerformanceLogUsers
PresentMonPreflightToolExists
PresentMonPreflightToolPath
PresentMonPreflightEtwProbeAttempted
PresentMonPreflightEtwProbeReason
```

Updated report generation so `framescope-interactive-manifest.json` and `framescope-interactive-data.js` preserve the specific access-denied classification and preflight fields. Older runs that still say `no-presentmon-csv` are also normalized during report generation if `PresentMonStderrTail` contains the ETW access-denied text.

Updated the diagnostic report capture-chain section so generated diagnostic JSON/Markdown includes the access-denied category and preflight fields.

## Scope Guard

No sampling core semantics were changed.

No FPS data is fabricated. Simulated and failing reports still produce:

```text
reportKind = diagnostic
frames = 0
hasFrameData = false
```

No GameLite, WMI, SGuard, or UI visual code was changed by this fix.

No GitHub push was performed.

`build.ps1` was run because it was explicitly listed as a validation command; that script refreshes local `dist` build artifacts by design, but no installer was installed, released, or pushed.

## ETW Probe Decision

I did not add an active PresentMon ETW permission probe before capture.

Reason: PresentMon supports short timed captures, but running it as a preflight would still open an additional ETW trace session before the real capture. For this backend fix I kept the lower-risk path requested in the task: record preflight fields only.

The field proving this is:

```text
PresentMonPreflightEtwProbeAttempted = false
PresentMonPreflightEtwProbeReason = Skipped to avoid opening an extra ETW trace session before capture.
```

## Tests Added

```text
tests\FrameScopePresentMonDiagnosticsTests.cs
```

Covers:

- stderr `failed to start trace session: access denied` -> `presentmon-etw-access-denied`
- message mentions ETW trace and Performance Log Users
- preflight records elevation, Performance Log Users membership, and tool existence fields

Updated:

```text
tests\FrameScopeReportManifestTests.cs
tests\FrameScopeDiagnosticsTests.cs
tests\Build-FrameScopeTests.ps1
```

Covers:

- simulated access-denied run remains diagnostic with `frames=0`
- manifest includes `presentmon-etw-access-denied`
- report data includes the Chinese ETW permission message
- diagnostic report Markdown/JSON includes status, remediation message, and preflight fields

## Simulated Access Denied Run

Generated and verified:

```text
artifacts\presentmon-etw-access-denied-20260524\Battlefield-6-access-denied-simulated
```

Key output:

```text
status = presentmon-etw-access-denied
reportKind = diagnostic
frames = 0
message = PresentMon 无法启动 ETW trace，需要管理员/Performance Log Users/系统 ETW 权限检查。
preflightIsElevated = false
preflightInPerformanceLogUsers = false
preflightToolExists = true
```

Evidence file:

```text
artifacts\presentmon-etw-access-denied-20260524\simulated-access-denied-verification.json
```

Report page:

```text
artifacts\presentmon-etw-access-denied-20260524\Battlefield-6-access-denied-simulated\charts\framescope-interactive-report.html
```

## Monitor Session Fake PresentMon Run

Generated and verified a real `FrameScopeMonitor.exe --monitor-session` run with a fake target process and a fake PresentMon executable that writes the same stderr text and exits `6`. This does not touch BF6 and does not open a real ETW session.

Evidence:

```text
artifacts\presentmon-etw-access-denied-20260524\monitor-session-fake-presentmon\monitor-session-fake-presentmon-verification.json
```

Key output:

```text
statusFrameCaptureStatus = presentmon-etw-access-denied
summaryFrameCaptureStatus = presentmon-etw-access-denied
statusMessage = PresentMon 无法启动 ETW trace，需要管理员/Performance Log Users/系统 ETW 权限检查。
summaryMessage = PresentMon 无法启动 ETW trace，需要管理员/Performance Log Users/系统 ETW 权限检查。
statusPreflightIsElevated = true
statusPreflightInPerformanceLogUsers = false
statusPreflightToolExists = true
exitCode = 6
csvExists = false
csvRows = 0
```

## Screenshot

PARTIAL.

Attempted to open the simulated local report with the Codex in-app browser. Browser automation rejected the `file:///` URL under its URL policy and explicitly disallowed using another browser surface as a workaround for the same blocked action.

No screenshot was captured. The report HTML, manifest, and data file were verified on disk instead.

## Verification Results

PASS:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

PASS:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
```

PASS:

```text
.\tests\FrameScopeConfigStoreTests.exe
.\tests\FrameScopeCapturePlannerTests.exe
.\tests\FrameScopeReportProgressTests.exe
.\tests\FrameScopePresentMonDiagnosticsTests.exe
.\tests\FrameScopeReportManifestTests.exe
.\tests\FrameScopeDiagnosticsTests.exe
.\tests\FrameScopePubgSimulatorTests.exe
.\tests\FrameScopeWebBridgeTests.exe
.\tests\FrameScopeWebView2RuntimeTests.exe
```

PASS:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result:

```text
5 frontend test files passed
44 frontend tests passed
vite build passed
```

PASS:

```text
git diff --check
```

Residual process check:

```text
No FrameScopeMonitor.exe, FrameScopeReportGenerator.exe, PresentMon, or dotnet residual process was found.
Only Codex/in-app-browser Node and WebView2 processes were present.
```

## BF6 Retest Requirement

BF6 still needs a real recapture after this backend fix is installed or run from the updated source build.

This code change improves classification and user guidance. It does not prove BF6 frame capture will recover, because the previous admin recapture still failed before PresentMon could start its ETW trace. The next BF6 run should verify:

```text
status.json FrameCaptureStatus
summary.json FrameCaptureStatus
presentmon.stderr.log
presentmon.csv exists/bytes/rows
charts\framescope-interactive-manifest.json frameCaptureStatus/reportKind/frames
```

Expected diagnostic improvement if ETW access is still denied:

```text
presentmon-etw-access-denied
PresentMon 无法启动 ETW trace，需要管理员/Performance Log Users/系统 ETW 权限检查。
```
