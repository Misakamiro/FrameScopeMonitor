# FrameScope UI Refresh Interval Strategy

Date: 2026-05-28

Scope: analysis and design only. This document does not implement code, build, package, or publish anything.

Source root inspected:
`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Executive conclusion

The current Settings item named "监控刷新 / 状态刷新间隔" is misleading.

Current `PollIntervalMs` is persisted in `FrameScopeConfig` and shown in the React Settings page, but the React frontend does not use it as a recurring UI polling interval. In the current source, there is no frontend `setInterval` tied to `PollIntervalMs`; the frontend refreshes by initial bridge requests, manual buttons, and bridge events.

The actual runtime use of `PollIntervalMs` is in the native watcher loop. It controls how often the watcher reloads config, scans enabled target processes, checks completed monitor sessions, and writes watcher state. It does not control PresentMon raw frame capture, process sampler rows, system sampler rows, CPU core telemetry rows, CPU VID rows, or CPU voltage rows.

Recommendation:

- Remove the user-facing Settings control for `PollIntervalMs`.
- Keep the config field only as a legacy/internal compatibility field, fixed to `1000 ms`.
- Use a separate frontend-internal status refresh policy:
  - window visible and idle: `1000 ms`
  - window visible and monitoring: `1000 ms`
  - user actions: immediate refresh, not waiting for the next timer
  - window hidden or minimized to tray: `3000 ms`
  - failure/error events: immediate reconcile refresh once, then return to the normal interval
- Do not use `100 ms` as a steady UI/status polling interval. `100 ms` belongs only to high-precision capture paths where explicitly configured, not to UI refresh.

## Evidence from current source

### Settings currently exposes the field

`src/frontend/src/pages/SettingsPage.tsx` renders a Settings group for "监控刷新" and binds the number input directly to `draft.PollIntervalMs`:

- `SettingsGroup title="监控刷新"` around lines 197-217.
- input `data-smoke-field="poll-interval"` around lines 206-211.
- helper text says smaller values make UI updates more frequent around line 215.

That label describes a UI behavior, but the runtime path does not match that description.

### Config persists the field

`src/core/FrameScopeConfigStore.cs` defines and normalizes `PollIntervalMs`:

- default config sets `PollIntervalMs = 1000` around lines 30-35.
- `Normalize` restores non-positive values to `1000` around line 103.
- editable target save preserves existing `PollIntervalMs` around lines 158-161.
- `FrameScopeConfig.PollIntervalMs` is a config property around lines 293-314.

Tests also currently preserve custom values such as `2222` and `333`, so implementation needs to update those tests if this becomes an internal fixed value.

### React frontend does not use it as a timer

`src/frontend/src/state/useFrameScopeBridgeState.ts` contains direct refresh functions:

- `refreshSnapshot()` calls `state.snapshot` around lines 190-221.
- `refreshConfig()` calls `config.get` around lines 223-235.
- `refreshReports()` calls `reports.list` around lines 250-258.
- `refreshTargets()` calls `targets.get` around lines 277-285.

The hook performs initial reads once on mount around lines 885-888:

- `refreshSnapshot()`
- `refreshConfig()`
- `refreshReports()`
- `refreshTargets()`

Search evidence:

- no frontend `setInterval` exists in `useFrameScopeBridgeState.ts`, `App.tsx`, or page files.
- `PollIntervalMs` appears in the frontend as a config/type/mock/Settings value, not as a polling scheduler.

Current UI status therefore comes from:

- initial bridge load,
- manual Overview refresh,
- manual Reports refresh,
- process refresh events,
- monitor start/stop events,
- report progress / report changed events,
- config/targets save responses and follow-up refreshes.

### `state.snapshot` payload is status metadata, not samples

`src/app/FrameScopeWebBridge.State.cs` builds `state.snapshot` by reading:

- config,
- watcher process state,
- watcher state file,
- host window state,
- report history path metadata.

The payload includes watcher running state, pid, completed run count, last report, last error, config path/counts, data root, host visibility, and report history existence. It does not read or sample PresentMon frame data, `system-samples.csv`, `process-samples.csv`, CPU core CSV, CPU VID CSV, or CPU voltage CSV.

### `PollIntervalMs` controls native watcher loop sleep

`src/app/FrameScopeNativeMonitor.Watcher.cs` uses `config.PollIntervalMs` around lines 144-148:

- fallback is `1000 ms`,
- minimum is clamped to `500 ms`,
- the watcher sleeps for that interval at the end of each loop.

That watcher loop does the following work:

- reloads config,
- applies retention periodically,
- recovers stale missing reports once,
- checks active monitor processes for completion,
- ensures reports/history entries after completed runs,
- scans enabled target processes,
- starts monitor sessions for detected targets,
- writes watcher state when changed or after a status interval.

So `PollIntervalMs` currently affects target-detection latency and completed-run/report-recognition latency. It is not a pure frontend refresh interval.

## Sampling boundary

The current field does not control the real sampling intervals.

PresentMon raw frame data:

- `src/core/FrameScopeCapturePlanner.cs` creates PresentMon arguments around lines 48-99.
- PresentMon arguments contain process id/name, output file, date/time, terminate behavior, session name, and optional timed capture.
- There is no `PollIntervalMs` argument in the PresentMon plan.

Background process sampler:

- `src/app/FrameScopeNativeMonitor.Watcher.cs` passes `--ProcessSampleIntervalMs` from `target.ProcessSampleIntervalMs` around lines 164-186.
- `src/monitoring/FrameScopeProcessSampler.cs` reads `--interval`, clamps it to at least `100 ms`, and sleeps from that interval in its sampling loop around lines 19-22 and 171-174.
- This writes `process-samples.csv`, `topcpu-samples.csv`, `topio-samples.csv`, and `sample-alerts.csv`.

System sampler:

- `src/app/FrameScopeNativeMonitor.MonitorSession.cs` passes `--interval` from `slowSampleIntervalMs` to the system sampler around lines 220-227.
- `src/monitoring/FrameScopeSystemSampler.cs` reads `--interval`, clamps it to at least `500 ms`, and writes `system-samples.csv` on that cadence around lines 19-22 and 144-175.

CPU core telemetry:

- `src/app/FrameScopeNativeMonitor.MonitorSession.cs` passes `--cpu-core-interval` around lines 228-232.
- `src/monitoring/FrameScopeSystemSampler.CpuCoreTelemetry.cs` uses `options.SampleIntervalMs`, clamps below `500 ms`, and writes `cpu-core-samples.csv` only when due around lines 76-105 and 124-150.

CPU voltage and CPU VID telemetry:

- `src/app/FrameScopeNativeMonitor.MonitorSession.cs` passes `--cpu-voltage-interval` and `--cpu-vid-interval` around lines 232-240.
- `src/monitoring/FrameScopeSystemSampler.cs` parses and clamps those intervals around lines 28-63.
- These paths write `cpu-voltage-samples.csv` and `cpu-vid-samples.csv` through their telemetry sessions, not through `PollIntervalMs`.

Monitor-session control polling:

- `src/app/FrameScopeNativeMonitor.Watcher.cs` currently starts monitor sessions with `--ControlPollIntervalMs 3000` around line 192.
- `src/app/FrameScopeNativeMonitor.MonitorSession.cs` clamps that to at least `1000 ms` and uses it to wait for target exit / control loop progress around lines 42-60 and 298-313.
- This is not a data sampling interval either.

## Fixed strategy

### 1. Backend watcher policy

Use an internal fixed watcher poll interval:

```text
WatcherPollIntervalMs = 1000
```

Reasoning:

- `1000 ms` keeps automatic target detection responsive enough for normal use.
- `1000 ms` keeps completed-run/report recognition prompt enough that the UI can update shortly after capture ends.
- The watcher process already runs below normal priority and monitor child processes are set to lower priorities.
- `3000 ms` would reduce polling but can delay auto-capture start and completed-report recognition by up to roughly three seconds.
- `500 ms` or lower increases process scanning frequency without a clear user-facing need.

Do not tie the watcher interval to window visibility or tray state. The watcher is responsible for background auto-monitoring; it should remain responsive even when the main window is hidden.

### 2. Frontend status policy

Use frontend-internal constants, not user settings:

```text
VisibleStateSnapshotIntervalMs = 1000
HiddenOrTrayStateSnapshotIntervalMs = 3000
ImmediateRefreshCoalesceMs = 150-250
```

The recurring frontend refresh should call `state.snapshot`, not every bridge endpoint. `state.snapshot` is the low-cost status endpoint and already includes watcher state and host visibility.

Do not call `config.get`, `targets.get`, or `reports.list` every second as a blanket policy. Those should refresh only when needed:

- initial app load,
- manual page refresh action,
- after config/target save success,
- after bridge events that indicate data changed,
- when `state.snapshot.watcher.completedRuns` or `state.snapshot.watcher.lastReport` changes.

### 3. Immediate refresh triggers

These user-visible operations should not wait for the next timer:

- app mount / first bridge ready,
- monitor start accepted,
- `event.status` = `monitor.started`,
- monitor stop accepted,
- `event.status` = `monitor.stopped`,
- `event.error` for monitor/config/report/process actions,
- `config.save` success,
- `targets.save` success,
- `reports.regenerate` progress completion or error,
- `event.reportsChanged`,
- manual Overview refresh,
- manual Reports refresh.

The implementation should coalesce duplicate immediate refreshes within roughly `150-250 ms` so a direct response and a matching event do not cause a burst of duplicate bridge calls.

### 4. Report list refresh policy

Report list freshness should be event-driven plus snapshot-driven:

- call `reports.list` on initial load;
- call `reports.list` after `event.reportsChanged`;
- call `reports.list` after `reports.regenerate` completes;
- compare each successful `state.snapshot` against the previous snapshot, and call `reports.list` if `completedRuns` or `lastReport` changed.

This avoids steady `reports.list` polling while still letting reports appear quickly after the watcher generates a report in a separate process.

Expected visible-window latency:

- start/stop/save feedback: immediate local state plus bridge event, typically below one second;
- report completion visible in list: next `state.snapshot` tick plus `reports.list`, normally around `1-1.5 s`;
- hidden/tray state reconciliation: up to `3 s`, with an immediate refresh when the window becomes visible again.

## Should Settings remove this item?

Yes.

普通用户不需要配置这个值，而且当前文案会让用户误以为它控制真实采样质量或报告数据精度。这个设置应该从普通 Settings 页面移除。

Recommended compatibility behavior:

- Keep `PollIntervalMs` in the C# config model for old config files.
- Stop exposing it in React Settings.
- Normalize legacy, missing, invalid, or user-edited values to the internal fixed value `1000`.
- Do not let old config values such as `100`, `333`, or `3000` change watcher behavior after the migration.
- Consider keeping the serialized field for one release if compatibility is important, but document it as legacy/internal and no longer user-controlled.

## Implementation window: files to change

This analysis did not change these files. A future implementation window should update the following areas.

Frontend:

- `src/frontend/src/pages/SettingsPage.tsx`
  - Remove the "监控刷新" group and `data-smoke-field="poll-interval"` input.
  - Keep the real "采样间隔" section because it controls actual process/system/CPU telemetry sampling.

- `src/frontend/src/state/useFrameScopeBridgeState.ts`
  - Add internal refresh constants.
  - Add a snapshot scheduler with visible `1000 ms` and hidden/tray `3000 ms`.
  - Prevent overlapping `state.snapshot` requests.
  - Add immediate/coalesced refresh helper.
  - Compare previous and current watcher `completedRuns` / `lastReport` and refresh reports when they change.
  - Subscribe to `event.hostWindowChanged` or derive visibility from successful snapshots so hidden/tray cadence can change.

- `src/frontend/src/App.tsx`
  - If the scheduler should be page-aware, pass `activePage` into `useFrameScopeBridgeState`.
  - Otherwise keep scheduling inside the hook and avoid page coupling.

- `src/frontend/src/bridge/contract.ts`
  - Keep `PollIntervalMs` only if config payload compatibility requires it.
  - Add or type `event.hostWindowChanged` payload if the scheduler uses that event directly.

- `src/frontend/src/data/mockPreview.ts`
  - Keep mock config aligned with the fixed value.
  - Mock host visibility / host window change events if frontend scheduler tests need them.

Tests:

- `src/frontend/src/uiDesignContract.test.ts`
  - Replace the current expectation that Settings contains "监控刷新" with an expectation that the normal Settings UI does not expose that group.

- `src/frontend/src/uiInteractionContract.test.ts`
  - Add coverage that no visible Settings input uses `data-smoke-field="poll-interval"`.

- frontend state/bridge tests, if present or added
  - Verify visible snapshot interval is `1000 ms`.
  - Verify hidden/tray interval is `3000 ms`.
  - Verify immediate refresh after monitor start/stop/save/report completion.
  - Verify report list refreshes when snapshot `completedRuns` or `lastReport` changes.

Backend/config:

- `src/core/FrameScopeConfigStore.cs`
  - Introduce a single fixed internal value, for example `FixedPollIntervalMs = 1000`.
  - Normalize `PollIntervalMs` to that value rather than preserving arbitrary old values.
  - Preserve schema compatibility but remove user-tunability.

- `src/app/FrameScopeNativeMonitor.Watcher.cs`
  - Use the internal fixed watcher poll interval instead of `config.PollIntervalMs`.
  - Keep watcher polling independent of window visibility.

- `framescope-config.example.json`
  - Either remove `PollIntervalMs` from the public example or keep it with a clear legacy/internal comment if the format supports comments. Since JSON does not support comments, the cleaner public example is to omit it if the loader tolerates omission.

- `tests/FrameScopeConfigStoreTests.cs`
  - Update tests that currently expect arbitrary `PollIntervalMs` values to be preserved.
  - Add a test proving old/custom values normalize to the fixed internal value.

- `tests/FrameScopeWebBridgeTests.cs`
  - If config payload remains serialized with `PollIntervalMs`, verify it returns `1000`.
  - If it is removed from the frontend contract only, verify config save/load still works with old configs.

Documentation:

- Update `docs/modules/software-ui.md` or the relevant UI module doc to state that status refresh is internal and not user-configurable.
- Update `docs/modules/backend-monitoring.md` to keep the distinction between watcher polling, monitor-session control polling, and actual sampling intervals.

## Acceptance criteria

Functional:

- Settings no longer shows a normal-user control named "监控刷新" or "状态刷新间隔".
- Settings still shows actual sampling controls for process/system/CPU telemetry if those remain part of the product.
- Start monitor and stop monitor show local progress immediately and reconcile with `state.snapshot` without waiting for a slow timer.
- Config save and target save show success/error from the direct bridge response and perform an immediate status refresh.
- Report regeneration completion refreshes the report list immediately from the progress event.
- Reports generated by the background watcher appear after a snapshot detects `completedRuns` or `lastReport` changed.
- Hidden/tray windows reduce frontend snapshot cadence to `3000 ms`.
- Returning from hidden/tray to visible triggers an immediate snapshot refresh.

Sampling safety:

- PresentMon raw frame data remains controlled only by PresentMon capture arguments, not by UI status refresh.
- `process-samples.csv` remains controlled by `ProcessSampleIntervalMs`.
- `system-samples.csv` remains controlled by `SlowSampleIntervalMs`.
- `cpu-core-samples.csv` remains controlled by `PerCoreSampleIntervalMs`.
- `cpu-vid-samples.csv` and `cpu-voltage-samples.csv` remain controlled by `PerCoreVoltageSampleIntervalMs` / telemetry options.
- Existing `ControlPollIntervalMs` remains a monitor-session control loop value, not a user-facing UI refresh setting.

Performance:

- No steady frontend `100 ms` polling exists.
- The normal visible steady-state bridge call is `state.snapshot` every `1000 ms`.
- Hidden/tray steady-state bridge call is `state.snapshot` every `3000 ms`.
- `reports.list`, `config.get`, and `targets.get` are not called every second unless a specific action or detected state change requires them.
- Snapshot calls do not overlap if the previous request is still in flight.

Compatibility:

- Existing config files with missing, invalid, low, or high `PollIntervalMs` still load.
- Legacy `PollIntervalMs` values cannot force the watcher below or above the fixed internal `1000 ms`.
- Old configs do not lose target sampling fields when saved.

Verification for the future implementation window:

- Run focused frontend tests for Settings visibility and bridge-state scheduler behavior.
- Run `tests\FrameScopeConfigStoreTests.cs` and bridge tests covering config payload/save compatibility.
- Run a WebView smoke path or mock bridge smoke path proving start/stop/save/report UI feedback remains prompt.
- Run a synthetic monitor/report completion scenario proving the report list refreshes without manual clicking.
- Inspect generated `status.json` / `summary.json` from a synthetic run to prove actual sampling interval fields are unchanged.
- Confirm no FrameScope watcher, monitor session, sampler, PresentMon, or WebView smoke process remains after validation.
