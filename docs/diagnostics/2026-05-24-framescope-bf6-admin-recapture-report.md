# FrameScope Monitor BF6 Admin Recapture Report

Date: 2026-05-24

Status: FAIL

## Verdict

The admin recapture did not restore BF6 FPS capture.

This run still failed at PresentMon ETW trace startup with `access denied`. The report page opened, but it was a diagnostic report with zero frame rows, not a recovered FPS report.

This is not evidence that Battlefield 6 is unsupported. The fresh evidence still points to the PresentMon/ETW permission path first, because PresentMon never reached a successful trace session and never created `presentmon.csv`.

## Scope

Only validation was performed.

No source code was changed. No UI was changed. No package or installer was built.

The only project file produced by this round is this report:

```text
docs\diagnostics\2026-05-24-framescope-bf6-admin-recapture-report.md
```

## Environment And Permission Checks

Installed FrameScope Monitor path:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe
```

Installed config path:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-config.json
```

Configured BF6 target:

```text
Name: Battlefield 6
ProcessName: bf6.exe
Enabled: true
DataRoot: C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs
```

Performance Log Users status:

```text
Performance Log Users members: empty
```

Current token/user group check showed:

```text
Mandatory Label\High Mandatory Level
BUILTIN\Administrators
```

FrameScope watcher process elevation check after the run:

```text
PID 28528
ProcessName FrameScopeMonitor
CommandLine "C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe" --watcher --config "C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-config.json"
TokenElevation elevated=1
```

The original GUI process that launched the watcher, PID `5168`, had already exited by the final token check. Earlier in the session it was observed as the installed FrameScope GUI process and the watcher was launched as its child. The final durable elevated proof is the active watcher PID `28528` returning `elevated=1`.

## Recapture Run

New BF6 run path:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6\Battlefield-6-20260524-024833
```

Watcher log sequence:

```text
2026-05-24T02:43:11.7385051+08:00 web-bridge-monitor-started pid=28528
2026-05-24T02:43:11.7639088+08:00 native-watcher-start config=C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-config.json
2026-05-24T02:48:33.5009832+08:00 monitor-start game=Battlefield 6 process=bf6.exe targetPid=2000 targetBase=bf6 pid=7848
2026-05-24T02:51:01.2874447+08:00 report-generate-partial missing-presentmon run=C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6\Battlefield-6-20260524-024833
2026-05-24T02:51:01.7752388+08:00 report-generate-diagnostic run=C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6\Battlefield-6-20260524-024833 report=C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6\Battlefield-6-20260524-024833\charts\framescope-interactive-report.html
2026-05-24T02:51:01.7782388+08:00 monitor-complete game=Battlefield 6 report=C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6\Battlefield-6-20260524-024833\charts\framescope-interactive-report.html
```

Target process evidence:

```text
bf6.exe PID 2000
EAAntiCheat.GameServiceLauncher.exe PID 29772
EAAntiCheat.GameService.exe PID 19320
FrameScope monitor session PID 7848
```

Monitor session command:

```text
"C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe" --monitor-session --TargetProcessName "bf6.exe" --TargetProcessAliases "bf6" --TargetDisplayName "Battlefield 6" --InitialTargetPid 2000 --WaitSeconds 15 --CaptureSeconds 0 --SampleIntervalMs 100 --ProcessSampleIntervalMs 100 --SlowSampleIntervalMs 1000 --ControlPollIntervalMs 3000 --RunRoot "C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6" --RunNamePrefix "Battlefield-6"
```

PresentMon command from `status.json` / `summary.json`:

```text
--process_id 2000 --output_file C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Battlefield-6\Battlefield-6-20260524-024833\presentmon.csv --date_time --terminate_on_proc_exit --no_console_stats --stop_existing_session --session_name FrameScopeNativePresentMon_Battlefield-6
```

## Required File Checks

| File | Exists | Bytes | Lines | Result |
| --- | --- | ---: | ---: | --- |
| `status.json` | yes | 5,482 | 1 | Final phase `done`; PresentMon exit `6`; no CSV |
| `summary.json` | yes | 3,766 | 1 | PresentMon exit `6`; no CSV |
| `presentmon.stdout.log` | yes | 3 | 0 | Effectively empty |
| `presentmon.stderr.log` | yes | 227 | 3 | `access denied` starting trace session |
| `presentmon.csv` | no | 0 | 0 | Not generated |
| `charts\framescope-interactive-manifest.json` | yes | 1,134 | 1 | Diagnostic report, zero frames |
| `charts\framescope-interactive-report.html` | yes | 40,277 | 102 | Report page opened, but no FPS data |
| `process-samples.csv` | yes | 11,729,111 | 128,822 | Process sampler ran |
| `system-samples.csv` | yes | 23,894 | 146 | System sampler ran |
| `sample-alerts.csv` | yes | 21,833 | 189 | Alerts generated |
| `topcpu-samples.csv` | yes | 1,909,757 | 26,601 | CPU sample data generated |
| `topio-samples.csv` | yes | 1,044,451 | 12,224 | IO sample data generated |
| `event-samples.csv` | yes | 54 | 1 | Header only |
| `report-generation.log` | yes | 1,139 | 1 | Diagnostic report generated |

## PresentMon Result

Exit code:

```text
6
```

stdout:

```text
empty
```

stderr:

```text
error: failed to start trace session: access denied.
       PresentMon requires either administrative privileges or to be run by a user in the
       "Performance Log Users" user group.  View the readme for more details.
```

CSV:

```text
presentmon.csv exists: false
presentmon.csv bytes: 0
presentmon.csv rows: 0
```

`status.json` key facts:

```text
Phase: done
PresentMonExitCode: 6
PresentMonExitedEarly: true
PresentMonForcedStop: false
PresentMonCsvExists: false
PresentMonCsvBytes: 0
PresentMonCsvRows: 0
FrameCaptureStatus: no-presentmon-csv
ReportHasFrameData: false
ReportKind: diagnostic
ReportFrameCount: 0
ReportProcessSampleCount: 1331
ReportSystemSampleCount: 145
```

`summary.json` key facts:

```text
PresentMonExitCode: 6
PresentMonExitedEarly: true
PresentMonForcedStop: false
PresentMonCsvExists: false
PresentMonCsvBytes: 0
PresentMonCsvRows: 0
FrameCaptureStatus: no-presentmon-csv
```

Manifest key facts:

```text
frames: 0
rawPresentMonRows: 0
validPresentMonRows: 0
presentMonSelectionMode: missing
hasFrameData: false
reportKind: diagnostic
frameCaptureStatus: no-presentmon-csv
presentMonCsvBytes: 0
presentMonCsvRows: 0
processSamples: 1331
systemSamples: 145
```

## FPS Recovery

FPS did not recover.

The user saw the chart/report page, but the manifest proves this was a diagnostic report:

```text
frames=0
hasFrameData=false
reportKind=diagnostic
```

So the UI/report generator path is working, but there are no PresentMon frame rows to display.

## Interpretation

This recapture matches the requested second branch:

```text
If still access denied, administrator/permission path did not truly take effect.
```

The nuance is that the FrameScope watcher process itself did report an elevated token (`elevated=1`). Despite that, PresentMon still failed to start the ETW trace session with the same `access denied` message, and the user is not a member of `Performance Log Users`.

This means the immediate failing condition is still PresentMon ETW trace-session access. It does not yet justify a conclusion that BF6 is unsupported, because the trace never started successfully.

The evidence does not reach the third branch:

```text
trace started successfully but CSV still had no frames
```

That branch would be the point where EA AntiCheat, fullscreen mode, render process selection, or ETW provider limitations become the primary suspects. In this run, PresentMon failed earlier than that.

## Backend Fix Need

Yes, a backend diagnostic fix is needed if the product should guide users correctly.

Recommended backend-only fixes after authorization:

1. Detect elevation and `Performance Log Users` membership before starting PresentMon.
2. When PresentMon stderr contains `failed to start trace session: access denied`, classify the capture as a specific `presentmon-etw-access-denied` failure instead of only `no-presentmon-csv`.
3. Surface the concrete remediation in status/report diagnostics:
   - run FrameScope elevated,
   - add the user to `Performance Log Users` and sign out/in,
   - verify with a lightweight PresentMon/manual ETW preflight before launching BF6.
4. Add a preflight command or internal probe that attempts a harmless ETW trace start before a game run, so the app can fail fast before the user spends time launching BF6.

Do not change the report UI as the first fix. The UI is accurately showing no FPS because there are no frame rows.

## Next Verification Step

The next verification should isolate whether this is Windows trace permission state or BF6-specific anticheat state.

Recommended order:

1. Add the current user to `Performance Log Users`, sign out and sign back in, then verify `whoami /groups` includes that group.
2. Run the installed FrameScope Monitor again and repeat the BF6 capture.
3. If PresentMon still says `access denied`, run a direct PresentMon permission preflight against a harmless process from the same elevated environment.
4. Only if PresentMon starts trace successfully but CSV remains empty should the investigation shift to EA AntiCheat, fullscreen mode, render process, or ETW provider behavior.
