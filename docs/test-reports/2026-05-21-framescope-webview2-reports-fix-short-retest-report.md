# FrameScope Monitor WebView2 Reports fix short retest report

Date: 2026-05-21
Tester role: WebView2 Reports fix short retest
Scope: `FSM-WEB-LIVE-001` and `FSM-WEB-LIVE-002` only
Source edits: none

## 1. Current conclusion

通过。

The two requested fixes were retested with command validation, WebView2 live smoke evidence, Reports layout screenshots, reduced-motion smoke, WinForms fallback screenshot, `git diff --check`, and residual process inspection.

## 2. Issues retested

### FSM-WEB-LIVE-001: Reports live actions

Result: 解除。

Evidence file:

- `artifacts\webview2-reports-short-retest-20260521\webview2-reports-live-smoke.json`

Observed WebView2 live UI click path:

- `reports.list`: PASS, real UI refresh button clicked.
- Backend-returned `reportId`: `JqhZoVeT4vxbHef3n2-AGsUqR2i-0KUbppVi1cGYh5E`.
- `reports.open`: PASS, UI button clicked with the selected real `reportId`; response status `opened`.
- `reports.openDirectory`: PASS, UI button clicked with the same real `reportId`; response status `directory_opened`.
- `reports.regenerate`: PASS, UI button clicked; response status `accepted`; progress event `report.regenerating`; final event `completed`.

Selected report evidence:

- Game: `Counter-Strike-2`
- Report kind: `full`
- Frames: `17472`
- Report size: `40277` bytes
- Run directory: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260505-101253`
- HTML report: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260505-101253\charts\framescope-interactive-report.html`

This was not replaced by unit tests or direct bridge-only calls. The smoke uses the Reports page buttons with `data-smoke-action` selectors and records click, response, and event evidence.

### FSM-WEB-LIVE-002: Reports Size narrow layout

Result: 解除。

Normal WebView2 Reports screenshot:

- `artifacts\webview2-reports-short-retest-20260521\webview2-reports-live-smoke-reports.png`

Narrow layout evidence:

- `artifacts\webview2-reports-short-retest-20260521\webview2-reports-narrow-equivalent-edge-900x760.png`
- Size: `900x760`

Observation:

- Normal width: `Size` value is a single line (`39.3 KB`), not split vertically.
- Narrow width near `900x760`: `Size` value is a single line (`822.4 KB` in equivalent React layout evidence), not split vertically.
- Report title, status pill, and action buttons did not show new overlap or unreadable clipping in the inspected screenshots.

Note: WebView2 smoke currently has no CLI width/height option and the smoke window remains at its default capture size. I therefore used an equivalent narrow layout screenshot from the same built React dist through Vite preview plus Edge headless at `900x760`. This verifies the CSS/layout fix for the Reports page. The live action success path remains covered by WebView2 live smoke.

## 3. Command validation

| Command | Result | Notes |
| --- | --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | Used bundled Node and project npm CLI; `npm ci`, `tsc --noEmit`, Vitest `2` files / `7` tests, and Vite build passed. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | Produced `dist\FrameScopeMonitor-Setup.exe`. |
| WebView2 live smoke with reports open/openDirectory/regenerate | PASS | Evidence in `webview2-reports-live-smoke.json`; all requested Reports live actions succeeded. |
| WebView2 reduced-motion smoke | PASS | Evidence in `webview2-reports-reduced-motion-smoke.json`; `success=true`, `reducedMotion=true`, Reports live action smoke also passed. |
| WinForms fallback screenshot | PASS | `winforms-fallback-overview.png` generated; default WinForms overview rendered normally. |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS | Exit code `0`; only existing CRLF normalization warnings were printed. |

## 4. Screenshots and evidence

- WebView2 Reports normal: `artifacts\webview2-reports-short-retest-20260521\webview2-reports-live-smoke-reports.png`
- WebView2 Reports live click states:
  - `webview2-reports-live-smoke-reports-list-after-refresh.png`
  - `webview2-reports-live-smoke-reports-open-clicked.png`
  - `webview2-reports-live-smoke-reports-open-success.png`
  - `webview2-reports-live-smoke-reports-open-directory-clicked.png`
  - `webview2-reports-live-smoke-reports-open-directory-success.png`
  - `webview2-reports-live-smoke-reports-regenerate-clicked.png`
  - `webview2-reports-live-smoke-reports-regenerate-accepted.png`
  - `webview2-reports-live-smoke-reports-regenerate-success.png`
- Narrow Reports equivalent layout: `artifacts\webview2-reports-short-retest-20260521\webview2-reports-narrow-equivalent-edge-900x760.png`
- Reduced motion evidence: `artifacts\webview2-reports-short-retest-20260521\webview2-reports-reduced-motion-smoke.json`
- WinForms fallback overview: `artifacts\webview2-reports-short-retest-20260521\winforms-fallback-overview.png`

## 5. Reduced motion regression

Result: PASS.

Reduced-motion smoke returned:

- `success=true`
- `reducedMotion=true`
- `reactReportsLoaded=true`
- `reportLiveActionSmoke.success=true`

Inspected frames:

- `webview2-reports-reduced-motion-smoke-transition-targets-reports-02.png`
- `webview2-reports-reduced-motion-smoke-transition-reports-settings-02.png`

Observation: no mixed old/new page body, no blank content area, and no full-page spinner was observed in the checked frames.

## 6. WinForms fallback

Result: PASS.

Command generated:

- `artifacts\webview2-reports-short-retest-20260521\winforms-fallback-overview.png`

Observation: default, non-`--web-ui` path still renders the legacy WinForms overview. It did not default into WebView2.

## 7. Residual process check

Required monitored process list:

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`

Result: PASS. No required residual processes were found after the retest.

Observed non-required browser/runtime processes:

- Existing or externally opened `msedge.exe` processes were present after real `reports.open` evidence.
- Existing `msedgewebview2.exe` processes from 2026-05-17 remained present.
- Codex runtime `node.exe` remained present.

I did not terminate the default-profile Edge processes because they were not part of the required residual process list and could overlap with user/browser state.

## 8. Remaining issues

No blocking issues found in this short retest.

Non-blocking note:

- WebView2 smoke does not expose a width/height parameter, so exact narrow WebView2-host capture requires either a future smoke-size option or external GUI automation. The current retest used an equivalent 900x760 React dist screenshot for the narrow layout portion.

## 9. Packaging recommendation

可以进入候选打包。

This recommendation is limited to the two retested Reports fixes. It does not replace a full packaging verification pass.
