# FrameScope WebView2 Reports Live Action Evidence Report - 2026-05-21

Time: 2026-05-21 15:11 +08:00
Role: WebView2 Reports live action evidence owner
Scope: FSM-WEB-LIVE-001 only. No Reports Size layout work. No packaging handoff.

## Current Conclusion

PASS.

`FSM-WEB-LIVE-001` is cleared for the current source tree. The WebView2 live smoke now exercises the real React Reports page buttons for:

- `reports.list`
- `reports.open`
- `reports.openDirectory`
- `reports.regenerate`

The successful `reports.open`, `reports.openDirectory`, and `reports.regenerate` paths are triggered by UI button clicks, not by direct bridge calls. The selected report comes from the backend `reports.list` response and uses the host-generated `reportId`:

- `JqhZoVeT4vxbHef3n2-AGsUqR2i-0KUbppVi1cGYh5E`
- Run directory: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260505-101253`
- Report HTML: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260505-101253\charts\framescope-interactive-report.html`
- Frame count: `17472`

No simulator was needed because the machine already had usable real report history and report files.

## Red Check

Before the harness change, the current live smoke passed overall but did not expose the required Reports success-path fields.

Red evidence:

- `artifacts\fsm-web-live-001-red\webview2-live-smoke.json`

Missing fields observed:

- `reportOpenClickOk`
- `reportOpenDirectoryClickOk`
- `reportRegenerateClickAccepted`
- `reportRegenerateClickCompleted`

## Implementation

Modified file:

- `src\app\FrameScopeNativeMonitor.WebHost.cs`

Changes:

- Added a Reports live action smoke pass inside the existing React WebView2 smoke harness.
- The harness now clicks the Reports page refresh button, then selects an eligible backend-returned report from the resulting `reports.list` response.
- The harness clicks the real row buttons for open report, open directory, and regenerate.
- The harness records selected report evidence, response payloads, and `event.reportProgress` accepted/in-flight/completed evidence in the smoke JSON.
- The harness records external browser/Explorer process deltas around the report actions and cleans smoke-started browser processes without touching pre-existing processes.

No real bridge handler bug was found, so `src\app\FrameScopeWebBridge.Reports.cs` was not changed. No frontend, CSS, layout, theme, monitoring, reporting data structure, diagnostics, lightweight script, GameLite, WMI, SGuard, packaging, or build script behavior was changed in this round.

## Final Evidence

Evidence root:

- `artifacts\fsm-web-live-001-final-20260521`

Primary smoke JSON:

- `artifacts\fsm-web-live-001-final-20260521\webview2-live-smoke.json`

Screenshots:

- Reports list after UI refresh: `artifacts\fsm-web-live-001-final-20260521\webview2-live-smoke-reports-list-after-refresh.png`
- Open report clicked: `artifacts\fsm-web-live-001-final-20260521\webview2-live-smoke-reports-open-clicked.png`
- Open report success: `artifacts\fsm-web-live-001-final-20260521\webview2-live-smoke-reports-open-success.png`
- Open directory clicked: `artifacts\fsm-web-live-001-final-20260521\webview2-live-smoke-reports-open-directory-clicked.png`
- Open directory success: `artifacts\fsm-web-live-001-final-20260521\webview2-live-smoke-reports-open-directory-success.png`
- Regenerate clicked: `artifacts\fsm-web-live-001-final-20260521\webview2-live-smoke-reports-regenerate-clicked.png`
- Regenerate accepted/in-flight: `artifacts\fsm-web-live-001-final-20260521\webview2-live-smoke-reports-regenerate-accepted.png`
- Regenerate success: `artifacts\fsm-web-live-001-final-20260521\webview2-live-smoke-reports-regenerate-success.png`
- WinForms fallback: `artifacts\fsm-web-live-001-final-20260521\winforms-fallback-overview-verified.png`

Final smoke booleans:

- `success=true`
- `reportLiveActionSmoke.success=true`
- `reportsListClickOk=true`
- `reportIdCaptured=true`
- `reportOpenClicked=true`
- `reportOpenClickOk=true`
- `reportOpenDirectoryClicked=true`
- `reportOpenDirectoryClickOk=true`
- `reportRegenerateClicked=true`
- `reportRegenerateClickAccepted=true`
- `reportRegenerateClickInFlight=true`
- `reportRegenerateClickCompleted=true`

The existing rejection coverage remains present in the same final smoke JSON under `bridgeExtensionSmoke`:

- `reportOpenPathRejected=true`
- `targetsSavePathRejected=true`
- `reportRegenerateMissingRejected=true`

## External Process Handling

The final smoke recorded `13` new `msedge.exe` processes created while opening the report HTML. Cleanup results are stored in:

- `smokePayload.reportLiveActionSmoke.newExternalProcesses`
- `smokePayload.reportLiveActionSmoke.externalProcessCleanup`

Final residual check:

- Smoke-started external processes still running: `0`
- FrameScope/PresentMon/sampler/report-generator/FakePresentMon/TslGame/GameLite residual processes: `0`
- Project-local Node/esbuild residual processes: `0`

## Verification

| Command | Result |
| --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS |
| WebView2 live smoke with Reports UI success clicks | PASS |
| WinForms fallback screenshot | PASS |
| residual process check | PASS |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS, rerun after this report was written |

## Retest Recommendation

Tester retest is recommended: YES.

The blocker is cleared by direct live UI evidence, but this area touches real external open actions and report regeneration. A tester should retest the Reports page in WebView2 live mode using real report history before any candidate packaging decision.
