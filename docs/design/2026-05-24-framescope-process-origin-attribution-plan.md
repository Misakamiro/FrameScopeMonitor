# FrameScope Process Origin Attribution Plan

Date: 2026-05-24

Status: DESIGN ONLY

Scope: design the report and artifact changes needed to explain where high background processes such as `powershell` came from. This document does not change source code, does not build, and does not package.

## Background

The two PowerShell diagnostics on 2026-05-24 found the same product gap from two directions:

- `docs\diagnostics\2026-05-24-framescope-valorant-20260510-powershell-origin-diagnosis.md` showed an old Valorant run where `powershell` was real background load, but the artifacts could not identify which software or script started each PID.
- `docs\diagnostics\2026-05-24-framescope-powershell-cpu-diagnosis.md` showed a controlled reproduction where multiple external PowerShell processes came from Codex, GameLite, and parallel build tasks rather than the FrameScope monitor session itself.

The current report is therefore technically correct but easy to misread. It answers "which process names consumed CPU, memory, or IO during the run", not "which software caused that process". When several `powershell` instances exist at the same time, the report can make them look like one high-usage `powershell`.

## Current State

Current sampler outputs:

| File | Current identity fields | Current limitation |
|---|---|---|
| `process-samples.csv` | `ProcessName`, `Count`, aggregated `Pids` | Metrics are grouped by `ProcessName`; no per-PID parent, command line, executable path, or start time. |
| `topcpu-samples.csv` | `ProcessName`, `Id` | Per-PID rows exist, but no origin metadata. |
| `topio-samples.csv` | `ProcessName`, `Id` | Per-PID rows exist, but no origin metadata. |

Current report data shape:

- `FrameScopeReportGenerator.ProcessData.cs` reads only `process-samples.csv`.
- The generated `DATA.process` object contains `t`, `names`, `cpu`, `mem`, and `stats`.
- The HTML process view and tooltip only know process names and metric values.
- `charts\framescope-interactive-manifest.json` records process counts, not attribution metadata availability.

The existing code already has useful building blocks:

- `FrameScopeProcessSampler` already enumerates all processes every sample and has per-PID CPU, memory, and IO before grouping.
- `FrameScopeNativeMonitor.ProcessCleanup.cs` already demonstrates `Win32_Process` reads for `ProcessId`, `ParentProcessId`, `Name`, `CommandLine`, and `ExecutablePath`.
- `FrameScopeDiagnostics.Redaction.cs` already has a privacy redaction pattern for user profile paths, username, token/password/secret/API-key values.

## Goals

1. When the report shows high `powershell`, the user can expand it and see which PID contributed, its parent PID/process, executable path, start time, and a safe command-line preview.
2. Keep the current "background process by name" chart because it is useful for quick scanning and old reports.
3. Add a PID-level view that prevents multiple same-name processes from being mistaken for one process.
4. Mark FrameScope-owned processes separately so the user can distinguish FrameScope overhead from the rest of the system.
5. Avoid leaking private paths, secrets, tokens, or full command lines into the visible HTML by default.
6. Preserve compatibility with old run directories and old `process-samples.csv` files.

## Non-Goals

- Do not replace the existing process-name aggregate chart.
- Do not claim exact origin for old reports that did not capture origin metadata.
- Do not add always-on heavyweight WMI queries at 100ms for every field if that creates measurable sampling overhead.
- Do not change GameLite or external scripts in this attribution pass.
- Do not make packaging or installer changes as part of the design-only phase.

## Proposed Data Model

### Keep Name Aggregation, Add PID Detail

`process-samples.csv` should remain the stable name-aggregate file:

```text
Time,SampleIndex,ElapsedMs,ProcessName,Count,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,Priorities,Pids
```

Add only low-risk compatibility fields if needed, such as:

```text
DominantPid,DominantPidCpuPct,HasPidBreakdown,FrameScopeComponent
```

Do not put full command lines into `process-samples.csv`. This file is dense and currently optimized for the report matrix. Keeping it mostly stable reduces report and test churn.

Create a new per-PID time-series file:

```text
process-pid-samples.csv
```

Recommended columns:

```text
Time,SampleIndex,ElapsedMs,ProcessName,Pid,ParentPid,ParentProcessName,CpuPct,WorkingSetMB,ReadMBps,WriteMBps,ExecutablePathPreview,CommandLinePreview,CommandLineHash,StartTime,FrameScopeComponent,FrameScopeRelation,OriginConfidence
```

Purpose:

- Preserve one row per PID per sample for processes that matter.
- Power the "PID expanded" report mode.
- Avoid forcing the existing aggregate file to carry multiple nested identities in one CSV cell.

Selection policy for `process-pid-samples.csv`:

- Always include rows from `topcpu-samples.csv` and `topio-samples.csv`.
- Always include rows whose name is commonly ambiguous or script-host-like: `powershell`, `powershell.exe`, `pwsh`, `pwsh.exe`, `cmd`, `python`, `node`, `wscript`, `cscript`, `mshta`, `conhost`, `rundll32`.
- Always include FrameScope-owned process names: `FrameScopeMonitor`, `FrameScopeProcessSampler`, `FrameScopeSystemSampler`, `FrameScopeReportGenerator`, `PresentMon*`, `logman`.
- Include any process contributing more than a small threshold in a sample, for example CPU >= 1.0%, read/write IO >= 1.0 MB/s, or working set >= 512 MB.

This keeps the PID file useful without exploding to every process every 100ms.

### Add Origin Metadata Sidecar

Create a lower-frequency or event-backed sidecar:

```text
process-origin-index.json
```

Recommended shape:

```json
{
  "schemaVersion": 1,
  "captureMode": "snapshot+wmi",
  "privacyMode": "safe-preview",
  "generatedAt": "2026-05-24T00:00:00+08:00",
  "runFrameScope": {
    "monitorPid": 38324,
    "processSamplerPid": 42744,
    "systemSamplerPid": 42040,
    "presentMonPid": 35044
  },
  "processes": {
    "20508": {
      "pid": 20508,
      "processName": "powershell",
      "parentPid": 35516,
      "parentProcessName": "codex.exe",
      "executablePathPreview": "%WINDIR%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
      "commandLinePreview": "powershell -NoProfile -ExecutionPolicy Bypass -File ...\\diagnose.ps1",
      "commandLineHash": "sha256:...",
      "startTime": "2026-05-24T00:23:36.3481728+08:00",
      "firstSeenTime": "2026-05-24T00:43:47.1000000+08:00",
      "lastSeenTime": "2026-05-24T00:43:58.2000000+08:00",
      "frameScopeComponent": false,
      "frameScopeRelation": "external",
      "originConfidence": "high"
    }
  }
}
```

Purpose:

- Store metadata once per PID instead of repeating long fields in every sample.
- Let report generation join PID time-series rows to origin metadata.
- Support future process-start events without changing the chart matrix format.

### Process Start Events

Short-lived PowerShell processes can appear and exit between snapshots. A complete implementation should add one event stream:

```text
process-start-events.jsonl
```

Each event:

```json
{"time":"...","pid":33204,"parentPid":5948,"processName":"powershell.exe","parentProcessName":"WmiPrvSE.exe","commandLinePreview":"...","commandLineHash":"sha256:...","executablePathPreview":"...","startTime":"..."}
```

Preferred source:

- Use WMI `Win32_ProcessStartTrace` first because it is accessible from .NET Framework without new native dependencies.
- Treat ETW `Microsoft-Windows-Kernel-Process` as a later upgrade if WMI misses too many short-lived processes or overhead is unacceptable.

The event stream is important for "blink and gone" PIDs like the old Valorant run's short PowerShell spikes.

## Field-by-Field Decision

### `process-samples.csv`

Do not convert this file to per-PID rows. Keep it as name aggregate for compatibility and fast chart loading.

Add:

- `DominantPid`: PID with the highest CPU contribution inside that process-name group for that sample, when known.
- `DominantPidCpuPct`: that PID's CPU contribution.
- `HasPidBreakdown`: `true` when `process-pid-samples.csv` has rows for this name/sample.
- `FrameScopeComponent`: `none`, `mixed`, or the component group if the name group only contains FrameScope-owned rows.

Keep:

- `Pids`, because it already helps prove that a name is made of multiple PIDs.

### `topcpu-samples.csv`

Add directly:

- `ParentPid`
- `ParentProcessName`
- `CommandLinePreview`
- `CommandLineHash`
- `ExecutablePathPreview`
- `StartTime`
- `FrameScopeComponent`
- `FrameScopeRelation`

Reason: `topcpu-samples.csv` is already per PID and small enough to carry attribution previews.

### `topio-samples.csv`

Add the same fields as `topcpu-samples.csv`:

- `ParentPid`
- `ParentProcessName`
- `CommandLinePreview`
- `CommandLineHash`
- `ExecutablePathPreview`
- `StartTime`
- `FrameScopeComponent`
- `FrameScopeRelation`

Reason: high IO from script hosts is just as actionable as high CPU.

### `process-pid-samples.csv`

Add as the main detailed time-series:

- `Pid`
- `ParentPid`
- `ParentProcessName`
- `CommandLinePreview`
- `CommandLineHash`
- `ExecutablePathPreview`
- `StartTime`
- metric fields: `CpuPct`, `WorkingSetMB`, `ReadMBps`, `WriteMBps`
- attribution fields: `FrameScopeComponent`, `FrameScopeRelation`, `OriginConfidence`

### Full Command Line Storage

Do not write full `CommandLine` to the visible report data by default.

If full command line is needed for local diagnosis, store it only in a local-only sidecar:

```text
process-origin-sensitive.local.json
```

This file should be optional and should not be referenced by `framescope-interactive-data.js`. The HTML report should use only `CommandLinePreview` and `CommandLineHash`.

## Avoiding Same-Name Process Misleading

The report should explicitly maintain two views:

### View A: By Name

This remains the default chart:

- Series key: process name.
- Values: aggregate CPU/memory across all PIDs with that name.
- Label examples: `powershell (6 PIDs)`, `node (3 PIDs)`.
- Tooltip should show:
  - aggregate value,
  - PID count,
  - top contributing PID,
  - "expand by PID" action or hint.

For old reports where only `Pids` exists:

- Show `powershell (multiple PIDs)` when `Pids` contains more than one PID.
- Show "origin not captured in this run" instead of guessing.

### View B: By PID

Add a report mode or segmented control:

```text
Group by: Name | PID
```

In PID mode:

- Series key: `ProcessName#Pid`, for example `powershell#25840`.
- Display label: `powershell PID 25840`.
- Tooltip should include:
  - PID,
  - parent PID/name,
  - start time,
  - executable preview,
  - command-line preview,
  - FrameScope relation,
  - current CPU/memory/IO value.

PID mode should support search by:

- process name,
- PID,
- parent process name,
- command-line preview,
- executable preview.

This makes "six different PowerShell processes" visible as six separate lines instead of one misleading curve.

## FrameScope Component Marking

Add a small classifier in the sampling or report generation layer.

FrameScope component groups:

| Component | Match |
|---|---|
| `monitor` | `FrameScopeMonitor.exe` with `--monitor-session`, `--watcher`, or normal UI path from the FrameScope root/install directory |
| `process-sampler` | `FrameScopeProcessSampler.exe` from the FrameScope root/install directory |
| `system-sampler` | `FrameScopeSystemSampler.exe` from the FrameScope root/install directory |
| `report-generator` | `FrameScopeReportGenerator.exe` from the FrameScope root/install directory |
| `presentmon` | bundled `PresentMon-*.exe` under FrameScope tools |
| `etw-tool` | child `logman.exe` started by the monitor session |
| `legacy-cleanup` | old `framescopewatcher.ps1` or `monitor-cs2-highfreq.ps1` cleanup matches, only when command line and path match FrameScope root |

FrameScope relation values:

| Value | Meaning |
|---|---|
| `self` | This PID is a known FrameScope component. |
| `child` | Its ancestor chain includes the current run's `FrameScopeMonitor.exe --monitor-session`. |
| `external` | No FrameScope ancestry or executable match. |
| `unknown` | Parent/ancestor data was not available. |

Report display:

- Add a small `FrameScope` badge for `self` or `child`.
- Add a summary card: "FrameScope components peak CPU / memory / IO".
- Keep FrameScope components visible, but visually separate them from external background processes.
- In name view, if a group mixes FrameScope and non-FrameScope PIDs, label it `mixed` and let PID mode explain the split.

This prevents a report from implying `powershell` is FrameScope just because the user opened the report from FrameScope.

## Privacy Plan

Command lines and paths can contain private project names, usernames, tokens, access keys, and launch arguments. The report must be useful without exposing full raw values.

### Redaction Rules

Reuse and extend the existing `FrameScopeDiagnostics.RedactForPrivacy` behavior:

- Replace the user profile path with `%USERPROFILE%`.
- Replace the Windows username with `%USERNAME%`.
- Redact values for keys containing `token`, `password`, `secret`, `apikey`, `api_key`, `access_key`, and `account`.
- Redact common CLI forms:
  - `--token value`
  - `--password=value`
  - `/apikey:value`
  - `-Secret value`
  - environment-like `TOKEN=...`
- Normalize repeated whitespace.

### Preview Rules

For HTML-visible fields:

- `ExecutablePathPreview`: keep drive or known root plus executable name; collapse middle folders.
  - Example: `%USERPROFILE%\...\tools\Run-Frontend.ps1`
  - Example: `%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe`
- `CommandLinePreview`: keep executable plus safe first arguments and script filename; truncate to 180 characters.
- `CommandLineHash`: SHA-256 of the full raw command line before redaction. This lets support compare whether two rows are the same command without showing it.
- `CommandLineTruncated`: boolean if the preview was shortened.
- `RedactionApplied`: boolean if any private value was changed.

Do not put raw full command lines into:

- `framescope-interactive-data.js`
- `framescope-interactive-report.html`
- `charts\framescope-interactive-manifest.json`

Optional advanced mode:

- A local-only diagnostics export may include raw command lines, but it must be explicit and should not be generated for normal reports.

## Historical Report Compatibility

The report generator must detect schema availability at runtime.

Compatibility rules:

1. If `process-pid-samples.csv` and `process-origin-index.json` exist, enable Name/PID grouping and origin tooltips.
2. If only old `process-samples.csv` exists:
   - render the current name-aggregate chart unchanged,
   - parse `Pids` when present,
   - label multi-PID groups as "multiple PIDs",
   - show "origin metadata was not captured for this run".
3. If old `topcpu-samples.csv` / `topio-samples.csv` include `Id` only:
   - show PID where available,
   - leave parent/command/path/start-time fields as unavailable.
4. If new columns are missing from any CSV:
   - treat the field as null,
   - do not fail report generation.

Manifest additions:

```json
{
  "processOriginSchemaVersion": 1,
  "processPidSamples": 123,
  "processOriginCaptured": true,
  "processOriginPrivacyMode": "safe-preview"
}
```

Old reports should continue to have no such manifest keys.

## Minimal Implementation Steps

1. Add a process metadata cache in `src\monitoring\FrameScopeProcessSampler.*`.
   - Read process `Id`, name, start time, executable path, and parent PID.
   - Join parent name from the same process map.
   - Use WMI only for fields not available from `System.Diagnostics.Process`.
   - Cache metadata by PID plus start time so PID reuse does not contaminate attribution.

2. Add safe preview and hash helpers.
   - Put shared redaction logic near diagnostics or duplicate a small internal helper in the sampler if sharing would create a build dependency problem.
   - Generate `CommandLinePreview`, `ExecutablePathPreview`, and `CommandLineHash`.
   - Never write raw full command line into normal HTML data.

3. Write `process-origin-index.json`.
   - Update it during the run or flush it at sampler exit.
   - Include first/last seen time and FrameScope relation.

4. Write `process-pid-samples.csv`.
   - Include selected per-PID rows using the selection policy above.
   - Keep `process-samples.csv` as the aggregate file.

5. Extend `topcpu-samples.csv` and `topio-samples.csv`.
   - Add parent/preview/start-time/component columns.
   - Keep current columns in their current order as much as practical; append new columns to reduce compatibility risk.

6. Extend report models.
   - Add `ProcessPidMatrixResult` and `ProcessOriginIndex` in `FrameScopeReportGenerator.Models.cs`.
   - Add readers in `FrameScopeReportGenerator.ProcessData.cs`.
   - Keep the existing `ProcessMatrixResult` path unchanged for old reports.

7. Extend generated `DATA.process`.
   - Keep current keys: `t`, `names`, `cpu`, `mem`, `stats`.
   - Add optional keys: `pidT`, `pidSeries`, `pidCpu`, `pidMem`, `pidIoRead`, `pidIoWrite`, `origins`, `originAvailable`.

8. Extend report UI.
   - Add `Name | PID` grouping control only on the background process tab.
   - In Name mode, keep current behavior and show multi-PID/origin hints in tooltip.
   - In PID mode, plot PID series and show origin metadata in tooltip/details.

9. Add FrameScope component summary.
   - Report peak and average resource usage for known FrameScope components.
   - Show it near the background process panel or as a filter/badge.

10. Update tests and fixtures.
   - Add report generator fixture with two `powershell` PIDs sharing one name but different parents/commands.
   - Add old-schema fixture to prove old reports still generate.

## Acceptance Tests

### Sampler Artifact Tests

Pass criteria:

- A run with two simultaneous `powershell.exe` processes writes:
  - one aggregate `powershell` row in `process-samples.csv`,
  - two distinct PID rows in `process-pid-samples.csv`,
  - two origin entries in `process-origin-index.json`.
- `topcpu-samples.csv` and `topio-samples.csv` rows include appended origin fields when metadata is available.
- PID reuse is handled by PID plus `StartTime`, not PID alone.
- Missing WMI permissions or inaccessible process fields do not crash sampling; unavailable fields are empty and `OriginConfidence` becomes `low` or `unknown`.

### Report Tests

Pass criteria:

- Old run directories with only current `process-samples.csv` still generate the report.
- New run directories expose both grouping modes.
- `powershell` name mode shows PID count and does not imply it is one process.
- PID mode shows separate `powershell PID <id>` series.
- Tooltip for a PID shows parent PID/name, start time, executable preview, command-line preview, command-line hash, and FrameScope relation.
- FrameScope components show a badge and a separate summary, while external `powershell` remains external.

### Privacy Tests

Pass criteria:

- `framescope-interactive-data.js` does not contain the actual Windows username.
- `framescope-interactive-data.js` does not contain full `%USERPROFILE%` paths.
- Tokens/passwords/secrets/API keys are replaced with `[redacted]`.
- Long command lines are truncated and flagged.
- `CommandLineHash` remains stable for identical raw command lines.

### Performance Tests

Pass criteria:

- The sampler still runs at the configured 100ms minimum interval without systematic drift on a normal run.
- Metadata refresh is cached and does not query full WMI data for every process every 100ms.
- The report still opens large runs without significant regression; PID mode can lazily build series or filter to high-impact PIDs if needed.

### End-to-End Verification

When implementation happens, the minimum verification chain should be:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeReportManifestTests.exe
node .\tests\chart-sampling-tests.js
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario stable -DurationSeconds 4
& 'C:\Program Files\Git\cmd\git.exe' diff --check
```

For this specific feature, add a targeted synthetic run that starts two distinguishable PowerShell commands during monitoring and verifies that the report can attribute them separately.

## Open Design Decisions

Recommended defaults:

- `process-samples.csv` stays aggregate.
- `process-pid-samples.csv` becomes the detailed time-series.
- `process-origin-index.json` stores one metadata record per PID/start-time identity.
- HTML shows safe previews only.
- Raw full command lines are not part of normal report generation.

Decisions to revisit during implementation:

- Whether WMI `Win32_ProcessStartTrace` is reliable enough for very short-lived PowerShell processes.
- Whether `process-pid-samples.csv` should include all PIDs or only selected high-impact/script-host/FrameScope PIDs.
- Whether command-line previews should be enabled by default or controlled by a privacy setting in `framescope-config.json`.

## Expected User Impact

After this design is implemented, the user seeing high `powershell` in a FrameScope report should be able to answer:

- Was it one PowerShell or several?
- Which PID had the spike?
- Which parent process launched it?
- What script or software does the safe command-line preview point to?
- Was it part of FrameScope itself, a child of the monitor session, or an external background process?
- Is the report showing name aggregation or PID-level detail?

For old reports, the answer remains limited: FrameScope can show that multiple PIDs were aggregated, but it cannot invent parent process or command-line data that was never captured.
