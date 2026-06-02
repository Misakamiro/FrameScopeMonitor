# FrameScope Monitor BF6 No-FPS Diagnosis

Date: 2026-05-24

Status: PARTIAL

## Verdict

The direct root cause for the Battlefield 6 run is a PresentMon ETW trace start failure, not report UI rendering and not a missing process match.

Evidence:

- `presentmon.stderr.log` says: `error: failed to start trace session: access denied.`
- PresentMon exit code in `status.json` and `summary.json` is `6`.
- `presentmon.csv` was never created for the BF6 run.
- `process-samples.csv` proves `bf6` existed during capture as PID `37832`, with 2319 matching sample rows.
- The same installed PresentMon binary successfully captured Valorant minutes later, so the installed tool file itself is not corrupt.

This is marked PARTIAL rather than PASS because the current artifact proves the immediate failure (`access denied` creating the ETW trace session), but it does not yet prove whether running FrameScope with elevated/Performance Log Users rights fully fixes BF6, or whether EA AntiCheat/fullscreen/ETW policy still blocks frame capture after the permission problem is removed.

## Reproduction Steps From Existing Artifacts

1. Open the BF6 report at:
   `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6\Battlefield-6-20260524-000119\charts\framescope-interactive-report.html`
2. Observe the report has no FPS data and is diagnostic-only:
   `frames=0`, `hasFrameData=false`, `reportKind=diagnostic`, `frameCaptureStatus=no-presentmon-csv`.
3. Open:
   `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6\Battlefield-6-20260524-000119\presentmon.stderr.log`
4. Confirm PresentMon failed before writing CSV with `failed to start trace session: access denied`.
5. Compare with:
   `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260524-000615`
   which contains a 15,926,970 byte `presentmon.csv` and a full report.

## Key Paths

BF6 failed run:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6\Battlefield-6-20260524-000119
```

BF6 key files:

```text
status.json
summary.json
presentmon-info.json
presentmon.stdout.log
presentmon.stderr.log
process-samples.csv
system-samples.csv
sample-alerts.csv
report-generation.log
charts\framescope-interactive-manifest.json
charts\framescope-interactive-report.html
```

Installed config:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-config.json
```

Installed watcher log:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-watcher.log
```

Installed PresentMon:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\tools\PresentMon-2.4.1-x64.exe
```

Source root:

```text
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d
```

Relevant source files:

```text
src\app\FrameScopeNativeMonitor.MonitorSession.cs
src\app\FrameScopeNativeMonitor.MonitorSession.PresentMon.cs
src\app\FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs
src\app\FrameScopeNativeMonitor.ReportOrchestration.cs
src\core\FrameScopeCapturePlanner.cs
```

## Target Configuration

Installed config at `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-config.json` contains:

```json
{
  "Enabled": true,
  "Name": "Battlefield 6",
  "ProcessName": "bf6.exe",
  "SampleIntervalMs": 100,
  "ProcessSampleIntervalMs": 100,
  "SlowSampleIntervalMs": 1000,
  "OpenReportOnComplete": true
}
```

Global config facts:

- `DataRoot`: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs`
- `MonitorScript`: `native-csharp`
- `OpenReportOnComplete`: `true`
- Installed config `PollIntervalMs`: `100`
- Source-tree config `PollIntervalMs`: `1000`, but the live installed run used `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-config.json`.

This rules out disabled target configuration or wrong process name as the cause.

## PresentMon Command, Exit Code, Logs

BF6 command line from `status.json` / `summary.json`:

```text
--process_id 37832 --output_file C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6\Battlefield-6-20260524-000119\presentmon.csv --date_time --terminate_on_proc_exit --no_console_stats --stop_existing_session --session_name FrameScopeNativePresentMon_Battlefield-6
```

BF6 PresentMon facts:

```text
PresentMonCaptureMode: process_id
PresentMonCaptureTarget: 37832
PresentMonExitCode: 6
PresentMonExitedEarly: true
PresentMonForcedStop: false
PresentMonCsvExists: false
PresentMonCsvBytes: 0
PresentMonCsvRows: 0
FrameCaptureStatus: no-presentmon-csv
```

BF6 stdout:

```text
empty
```

BF6 stderr:

```text
error: failed to start trace session: access denied.
       PresentMon requires either administrative privileges or to be run by a user in the
       "Performance Log Users" user group.  View the readme for more details.
```

PresentMon tool info:

```text
Path: C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\tools\PresentMon-2.4.1-x64.exe
Length: 927304
LastWriteTime: 2026-05-03T01:15:16+08:00
```

Installed/source binary hash comparison showed the installed and source copies matched for:

```text
FrameScopeMonitor.exe
FrameScopeProcessSampler.exe
FrameScopeSystemSampler.exe
FrameScopeReportGenerator.exe
tools\PresentMon-2.4.1-x64.exe
```

So this is not explained by a stale installed PresentMon or source/install mismatch.

## BF6 Process Match Evidence

The monitor did find and follow BF6:

```text
TargetProcess: bf6.exe
TargetPid: 37832
TargetResolvedProcess: bf6.exe
TargetProcessCandidates: bf6
TargetHasMainWindow: false
MonitorPid: 39312
SampleCount: 88
```

`process-samples.csv` contains 2319 `bf6` rows. First rows:

```text
2026-05-24T00:01:19.5852720+08:00,0,0,bf6,1,,263,0,0,,37832
2026-05-24T00:01:19.6902988+08:00,1,105,bf6,1,0,263,0,0,,37832
2026-05-24T00:01:19.8003239+08:00,2,215.1,bf6,1,0,263,0,0.001,,37832
2026-05-24T00:01:19.9088535+08:00,3,323.6,bf6,1,0.9,269,29.853,0.059,,37832
```

`system-samples.csv` also shows `TargetRunning=True` while BF6 was sampled:

```text
2026-05-24T00:01:20.0318770+08:00,0,False,True,24.99,...
2026-05-24T00:01:21.0398631+08:00,1,False,True,54.69,...
```

There are EA AntiCheat process samples during the BF6 capture:

```text
EAAntiCheat.GameService, pid 39056
EAAntiCheat.GameServiceLauncher, pid 33308
```

These are relevant clues for a possible second-stage restriction, but they are not needed to explain the current missing FPS because PresentMon already failed at ETW trace startup with an explicit access-denied error.

## Watcher And Report Generation Evidence

Installed watcher log around BF6:

```text
2026-05-24T00:01:19.0856453+08:00 monitor-start game=Battlefield 6 process=bf6.exe targetPid=37832 targetBase=bf6 pid=39312
2026-05-24T00:05:43.8049233+08:00 report-generate-partial missing-presentmon run=...\Battlefield-6-20260524-000119
2026-05-24T00:05:43.8084329+08:00 report-generate-start run=...\Battlefield-6-20260524-000119
2026-05-24T00:05:44.5261471+08:00 report-generate-diagnostic run=...\Battlefield-6-20260524-000119 report=...\charts\framescope-interactive-report.html
2026-05-24T00:05:44.5281473+08:00 monitor-complete game=Battlefield 6 report=...\charts\framescope-interactive-report.html
```

The report generator did the expected fallback:

```json
{
  "frames": 0,
  "rawPresentMonRows": 0,
  "validPresentMonRows": 0,
  "presentMonSelectionMode": "missing",
  "hasFrameData": false,
  "reportKind": "diagnostic",
  "processSamples": 2319,
  "systemSamples": 258,
  "frameCaptureStatus": "no-presentmon-csv",
  "presentMonCsvBytes": 0,
  "presentMonCsvRows": 0
}
```

Source logic confirms this behavior:

- `src\app\FrameScopeNativeMonitor.MonitorSession.cs` starts PresentMon after resolving the target PID and records `PresentMonArgs`.
- `src\app\FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs` starts child processes with redirected stdout/stderr, so BF6's PresentMon stderr is the real child-process stderr.
- `src\app\FrameScopeNativeMonitor.MonitorSession.PresentMon.cs` classifies missing `presentmon.csv` as `no-presentmon-csv` and writes stderr tail into status/summary.
- `src\app\FrameScopeNativeMonitor.ReportOrchestration.cs` still generates a diagnostic report when `presentmon.csv` is missing but process/system sample CSVs exist.

## Comparison With Working Runs

### BF6 failed run

```text
Run: C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6\Battlefield-6-20260524-000119
Target: bf6.exe
TargetPid: 37832
PresentMonExitCode: 6
PresentMon stdout: empty
PresentMon stderr: access denied starting trace session
presentmon.csv: missing
FrameCaptureStatus: no-presentmon-csv
Report frames: 0
Report kind: diagnostic
Process samples: 2319
System samples: 258
```

### Valorant working run

```text
Run: C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Valorant\Valorant-20260524-000615
Target: VALORANT-Win64-Shipping.exe
TargetPid: 40944
PresentMonExitCode: 0
PresentMon stdout: Started recording.
PresentMon stderr: empty
presentmon.csv: 15,926,970 bytes
PresentMonCsvRows: 60,318
FrameCaptureStatus: captured
Report frames: 60,317
Report kind: full
```

### CS2 working frame-data run

```text
Run: C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260505-101253
Target: cs2.exe
TargetPid: 17904
PresentMon stdout: Started recording.
PresentMon stderr: empty
presentmon.csv: 4,351,016 bytes
Manifest frames: 17,472
Manifest kind: full
```

Note: this older CS2 run's later `status.json` was touched by report regeneration and contains a report-generation lock error from 2026-05-23, but the manifest and CSV prove the original frame capture was successful.

### PUBG / TslGame available comparison

No real installed `PUBG: BATTLEGROUNDS` run exists under the current main data root. The available PUBG-like evidence is in temp/probe runs.

Useful synthetic/probe run:

```text
C:\Users\misakamiro\AppData\Local\Temp\framescope-pubg-monitor3-20260509-234024\PUBG-BATTLEGROUNDS-20260509-234024
Target: TslGame.exe
TargetPid: 29940
WindowTitle: FrameScope D3D11 Render Probe
PresentMonExitCode: 0
presentmon.csv: 47,850 bytes
PresentMonCsvRows: 177
FrameCaptureStatus: captured
Manifest frames: 177
Report kind: full
```

This proves the FrameScope/PresentMon chain can work with a TslGame-named D3D11 probe. It does not prove real PUBG anticheat behavior.

## Differences That Matter

The decisive difference is PresentMon's startup result:

| Run | PresentMon stderr | Exit | CSV | Report |
| --- | --- | ---: | ---: | --- |
| BF6 | `failed to start trace session: access denied` | 6 | missing | diagnostic |
| Valorant | empty | 0 | 15,926,970 bytes | full |
| CS2 | empty | captured in older run | 4,351,016 bytes | full |
| PUBG probe | empty | 0 | 47,850 bytes | full |

Other observed differences:

- BF6 had `EAAntiCheat.GameService` and `EAAntiCheat.GameServiceLauncher` active during capture.
- BF6 target had no main window recorded (`TargetHasMainWindow=false`), but the monitor did intentionally accept the target process after fallback and sampled it continuously.
- BF6 used `process_id` capture because only one configured process name (`bf6.exe`) exists. Current source only switches to `process_name` for PUBG/multiple aliases, so this is expected behavior and not by itself a failure.
- The current user token includes Administrators and High Mandatory Level when checked from this Codex shell, but the BF6 artifact itself proves the FrameScope-launched PresentMon process did not have permission to start that trace at `2026-05-24T00:01:19`. A current shell token does not retroactively prove the earlier GUI-launched FrameScope process had the same elevation.
- `Performance Log Users` local group currently lists no members. PresentMon's own stderr says it needs either administrative privileges or membership in that group.

## Root Cause

Root cause proven by current artifacts:

```text
PresentMon failed to start its ETW trace session for the BF6 run because Windows denied trace-session access. Because PresentMon exited with code 6 before creating presentmon.csv, FrameScope had no frame rows to parse and correctly generated a diagnostic report with FPS N/A.
```

This is not a proven "Battlefield 6 does not support FPS capture" conclusion. The evidence points to PresentMon/ETW permissions first.

Secondary factors that remain plausible but not yet proven:

- FrameScope was launched non-elevated while BF6/EA AntiCheat ran elevated or protected.
- The user is not in `Performance Log Users`, and the FrameScope GUI session did not have sufficient ETW trace rights.
- EA AntiCheat or BF6 may still block PresentMon/ETW after the permission issue is fixed.
- Fullscreen exclusive or overlay mode may still affect capture after ETW trace startup succeeds.

## What Evidence Is Still Missing

To close this from PARTIAL to PASS/FAIL, we need one controlled BF6 recapture:

1. Start FrameScope Monitor explicitly as Administrator, or add the user to `Performance Log Users` and sign out/in.
2. Start BF6 normally and capture until game exit.
3. Collect the new run's:
   `status.json`, `summary.json`, `presentmon.stderr.log`, `presentmon.stdout.log`, `presentmon.csv` size, and manifest.
4. Check whether PresentMon now exits `0` and writes CSV.

Possible outcomes:

- If exit becomes `0` and CSV has rows, the fix path is permissions/elevation guidance.
- If trace startup still says `access denied`, then the launch/elevation path is still wrong or system trace permissions are still blocked.
- If trace starts but CSV remains empty/no rows, then investigate BF6/EA AntiCheat/fullscreen/render-process/ETW provider restrictions.

## Next Fix Recommendations

Do not change report data structures or UI for this root cause. The report already reflects the real data state.

Recommended next changes after authorization:

1. Add a preflight diagnostic before starting PresentMon:
   - Detect whether the FrameScope process is elevated.
   - Detect whether the current user is in `Performance Log Users`.
   - If neither is true, show/log a clear permission warning before capture starts.

2. Improve capture diagnostic wording:
   - When `presentmon.stderr.log` contains `failed to start trace session: access denied`, classify it as a permission/ETW startup failure instead of the broader generic `no-presentmon-csv` message.
   - Keep the existing status fields; add no UI/data-structure change unless explicitly authorized.

3. Add a BF6-specific recapture checklist:
   - Run FrameScope as Administrator.
   - Start capture after BF6 reaches the final render window.
   - Prefer borderless/windowed fullscreen for the first retest.
   - Verify `presentmon.stdout.log` says `Started recording.`
   - Verify `presentmon.csv` exists and grows during capture.

4. Only if the elevated recapture still fails:
   - Test PresentMon manually against a low-risk process to confirm ETW permission.
   - Compare `--process_id bf6Pid` vs `--process_name bf6.exe`.
   - Capture BF6 with/without overlays and fullscreen exclusive.
   - Look for anti-cheat-specific ETW denial evidence in Event Viewer / security logs.
