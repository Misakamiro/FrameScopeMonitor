# FrameScope UI Refresh Interval Implementation Report

Date: 2026-05-28

Source root:
`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Design input:
`docs\design\2026-05-28-framescope-ui-refresh-interval-strategy.md`

## Conclusion

PASS.

The Settings normal UI no longer exposes the misleading monitor/status refresh interval control. `PollIntervalMs` remains only as a legacy compatibility field in the config contract/model and is normalized to the internal fixed value. The native watcher loop now uses an internal fixed 1000 ms sleep. The React status refresh path now uses internal scheduler constants and immediate coalesced refreshes after user-visible operations. Real sampling intervals remain controlled by their dedicated data sampling settings.

No BF6 test was run. No real game was launched. No installer was run. No GitHub push or Release update was performed.

## Implementation

### `PollIntervalMs`

Final handling: compatibility-only.

- `FrameScopeConfigStore.InternalPollIntervalMs` is defined as `1000`.
- Defaults set `PollIntervalMs` to `InternalPollIntervalMs`.
- `FrameScopeConfigStore.Normalize()` pins any loaded value, including legacy values such as `333`, back to `1000`.
- `BuildConfigFromEditableTargets()` also writes the internal value, so Settings save cannot preserve or create a user-selected poll interval.
- `FrameScopeConfig.PollIntervalMs` is retained so old config JSON can still deserialize and bridge/config responses remain compatible.
- `framescope-config.example.json` no longer advertises `PollIntervalMs` as a public setting.

### Native Watcher Loop

Fixed watcher loop value: `1000 ms`.

`src\app\FrameScopeNativeMonitor.Watcher.cs` no longer computes sleep from `config.PollIntervalMs`. It writes performance diagnostics with `interval=1000` and sleeps via:

```csharp
System.Threading.Thread.Sleep(FrameScopeConfigStore.InternalPollIntervalMs);
```

The watcher still reloads config, scans targets, checks completed monitor sessions, and writes watcher state on this internal cadence. This value is not user-editable.

### Frontend Status Refresh

Internal frontend strategy:

- visible window: `1000 ms`
- hidden/tray state: `3000 ms`
- user operations: coalesced immediate refresh after `200 ms`
- no steady `100 ms` UI/status polling

Implemented in `src\frontend\src\state\useFrameScopeBridgeState.ts` with:

- `VISIBLE_STATE_SNAPSHOT_INTERVAL_MS = 1000`
- `HIDDEN_OR_TRAY_STATE_SNAPSHOT_INTERVAL_MS = 3000`
- `IMMEDIATE_REFRESH_COALESCE_MS = 200`
- a recurring `state.snapshot` scheduler
- in-flight and queued request guards
- immediate refresh scheduling after monitor start/stop, config save, target save, report progress/completion, report changes, and errors
- report list refresh when watcher `completedRuns|lastReport` changes
- host-window visibility/tray event handling for lowering or restoring the cadence

### Settings UI

Removed from the normal Settings page:

- the `监控刷新` group
- the `状态刷新间隔` control
- `data-smoke-field="poll-interval"`
- all Settings UI writes to `PollIntervalMs`

Preserved data sampling controls:

- background process sampling interval
- slow/system sampling interval
- CPU core frequency sampling interval
- CPU core voltage sampling interval

The WebView2 smoke path was updated to edit `process-sample-interval` instead of the removed poll interval field.

### Sampling Boundary

No real sampling data interval is controlled by `PollIntervalMs` after this change.

Unaffected:

- PresentMon raw frame data capture
- `SampleIntervalMs`
- `ProcessSampleIntervalMs`
- `SlowSampleIntervalMs`
- `CpuCoreSampleIntervalMs` / `CpuTelemetry.PerCoreSampleIntervalMs`
- `CpuVoltageSampleIntervalMs` / `CpuTelemetry.PerCoreVoltageSampleIntervalMs`
- `CpuVidSampleIntervalMs` path, which follows the CPU voltage telemetry interval in the current native monitor/session argument path

`FrameScopeNativeMonitor.Watcher.cs` still passes sampler intervals through dedicated arguments such as `--ProcessSampleIntervalMs`, `--SlowSampleIntervalMs`, `--CpuCoreSampleIntervalMs`, and `--CpuVoltageSampleIntervalMs`. The new watcher sleep constant is separate from those arguments.

## Tests Added Or Updated

- `tests\FrameScopeNativeWatcherPolicyTests.cs`
  - verifies `InternalPollIntervalMs == 1000`
  - verifies watcher sleep uses `FrameScopeConfigStore.InternalPollIntervalMs`
  - verifies watcher no longer sleeps from `config.PollIntervalMs`
  - verifies sampler argument construction does not use `PollIntervalMs`
- `tests\FrameScopeConfigStoreTests.cs`
  - verifies legacy `PollIntervalMs` values load without crash
  - verifies legacy values normalize to internal `1000`
  - verifies sampler intervals are not changed by `PollIntervalMs`
- `src\frontend\src\uiDesignContract.test.ts`
  - verifies Settings no longer contains `监控刷新`, `状态刷新间隔`, `poll-interval`, or `PollIntervalMs`
  - verifies `采样间隔` remains
- `src\frontend\src\uiInteractionContract.test.ts`
  - verifies frontend internal refresh constants
  - verifies hidden/tray host-window event path
  - verifies immediate snapshot refresh scheduling after user operations

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| `tools\Run-Frontend.ps1 verify` | PASS | typecheck PASS; Vitest 5 files / 53 tests PASS; Vite build PASS |
| `build.ps1` | PASS | exit 0; generated `dist\FrameScopeMonitor-Setup.exe` and `dist\FrameScopeMonitor-Full-Setup.exe`; installer not run |
| `tests\Build-FrameScopeTests.ps1` | PASS | `FrameScope tests rebuilt.` |
| full C# test sweep | PASS | all `tests\FrameScope*Tests.exe` passed, including ConfigStore, WebBridge, WebHostLifecycle, NativeMonitorChildProcess, NativeWatcherPolicy, sampler/report/runtime tests |
| WebView2 live smoke | PASS | `artifacts\ui-refresh-interval-20260528\webview2-live-smoke.json`, screenshot set including `webview2-live-smoke-settings-clean.png`, `webview2-live-smoke-settings-dirty.png`, `webview2-live-smoke-settings-saving.png`, `webview2-live-smoke-settings-saved.png` |
| WebView2 reduced-motion smoke | PASS | `artifacts\ui-refresh-interval-20260528\webview2-reduced-motion-smoke.json`, screenshot set including reduced-motion Settings dirty/saving/saved images |
| WebView2 console/errors | PASS | both smoke JSON files recorded console count 0 and error count 0 |
| Settings no monitor refresh item | PASS | source contract test plus Settings screenshots listed above |
| Settings sampling interval area remains | PASS | source contract test plus Settings screenshots listed above |
| start/stop/save status feedback | PASS | smoke JSON observed config dirty/saving/saved and monitor start/stop accepted/completed |
| config snapshot restoration after smoke | PASS | both smoke JSON files include `host:config-snapshot restored` |
| residual source-tree process check | PASS | `NO_MATCHING_RESIDUAL_PROCESSES` after smoke/tests |
| `git diff --check` before report write | PASS | exit 0; only CRLF conversion warnings |
| final `git diff --check` after report write | PASS | exit 0; only CRLF conversion warnings |
| final residual process check after report write | PASS | `NO_MATCHING_RESIDUAL_PROCESSES` |

One earlier sequential C# sweep in this implementation run had a transient `FrameScopeNativeMonitorChildProcessTests.exe` exit-code mismatch in the previous agent phase. The test passed on immediate solo rerun and the fresh full sweep recorded in this report passed all C# tests.

## Artifacts

- `artifacts\ui-refresh-interval-20260528\webview2-live-smoke.json`
- `artifacts\ui-refresh-interval-20260528\webview2-live-smoke.png`
- `artifacts\ui-refresh-interval-20260528\webview2-live-smoke-settings-clean.png`
- `artifacts\ui-refresh-interval-20260528\webview2-live-smoke-settings-dirty.png`
- `artifacts\ui-refresh-interval-20260528\webview2-live-smoke-settings-saving.png`
- `artifacts\ui-refresh-interval-20260528\webview2-live-smoke-settings-saved.png`
- `artifacts\ui-refresh-interval-20260528\webview2-reduced-motion-smoke.json`
- `artifacts\ui-refresh-interval-20260528\webview2-reduced-motion-smoke.png`
- `artifacts\ui-refresh-interval-20260528\webview2-reduced-motion-smoke-settings-clean.png`
- `artifacts\ui-refresh-interval-20260528\webview2-reduced-motion-smoke-settings-dirty.png`
- `artifacts\ui-refresh-interval-20260528\webview2-reduced-motion-smoke-settings-saving.png`
- `artifacts\ui-refresh-interval-20260528\webview2-reduced-motion-smoke-settings-saved.png`

## Retest Recommendation

Recommend entering a source-tree retest window for this change.

Scope for retest:

- Settings normal UI verification
- watcher start/stop responsiveness
- source-tree WebView2 live/reduced-motion smoke
- synthetic or existing-report flows only

Do not include BF6 or real-game testing unless explicitly authorized. Do not run installers or publish GitHub Release as part of this retest window.
