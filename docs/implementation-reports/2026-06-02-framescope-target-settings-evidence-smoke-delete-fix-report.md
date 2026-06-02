# FrameScope target/settings evidence smoke delete fix report

Date: 2026-06-02
Workspace: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Scope

This run only investigated and fixed the WebView2 target/settings evidence smoke failure:

`Target delete evidence did not complete.`

No FrameScope install was performed. No setup/full setup executable was run. No real game was started. BF6 was not tested. Nothing was pushed to GitHub and no Release was updated.

## Required Checks

Checked files and evidence:

- `docs\implementation-reports\2026-06-02-framescope-build-artifact-sync-after-optimization-report.md`
- `smoke-temp\artifact-sync-portable-20260602-133654\FrameScopeMonitor-payload\smoke-profile\webview2-ui-smoke.json`
- `src\app\FrameScopeNativeMonitor.WebHost.cs`
- `src\frontend\src\pages\TargetsPage.tsx`
- `src\frontend\src\pages\pages.css`
- `tools\evidence-smoke\Run-TargetSettingsEvidenceSmoke.ps1`

## 1. Failure Reproduced

Yes.

Reproduction command used the built payload `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe` with an empty temporary target list and a config inside the payload root.

Evidence:

- `smoke-temp\tdel-repro1\webview2-ui-smoke.json`
- `smoke-temp\tdel-repro1\webview2-ui-smoke-target-delete-pending.png`

Result:

- `success=false`
- `error="Target delete evidence did not complete."`
- `targetAddSaved=true`
- `targetEditSaved=true`
- `targetDeleteSaved=true`
- `settingsSaved=true`
- final temporary config still contained `QA Evidence Target Edited`

The screenshot showed that after delete the target list was empty and dirty, but the save action could not persist the empty target list.

## 2. Root Cause

Root cause was a UI behavior regression in `TargetsPage.tsx`.

The top save button was disabled when `draftTargets.length === 0`:

```tsx
disabled={!dirty || saveBusy || draftTargets.length === 0}
```

When the smoke starts from an empty target list, it adds one target, edits it, then deletes that only target. After deletion, the draft is correctly empty and dirty, but the save button is disabled, so no `targets.save` request is sent for the empty list. Settings save later reloads/persists the still-saved edited target, so delete is not truly saved.

This was not a missing button, not a selector miss, not a windowing issue, not a CSS visibility issue, and not a WebHost path-policy problem.

## 3. Files Changed

Actual files changed in this run:

- `src\frontend\src\pages\TargetsPage.tsx`
  - Removed `draftTargets.length === 0` from the save button disabled condition.
  - The save button is now disabled only by `!dirty || saveBusy`, allowing deletion of the final target to be saved as an empty list.
- `src\frontend\src\uiInteractionContract.test.ts`
  - Added a regression contract test: `allows saving an empty Targets draft after deleting the last target`.

No smoke check was deleted or weakened. `FrameScopeNativeMonitor.WebHost.cs`, `pages.css`, and `tools\evidence-smoke` were inspected but not changed.

## 4. Smoke Problem Or UI Problem

This was a UI problem.

The smoke exposed a real user-facing edge case: deleting the final target left the page dirty but blocked the user from saving the empty target list.

## 5. Built Payload Target/Settings Evidence Smoke

PASS.

Evidence:

- `smoke-temp\tdel-built-postfix\webview2-ui-smoke.json`
- `smoke-temp\tdel-built-postfix\webview2-ui-smoke.png`

Result:

- exit code `0`
- `success=true`
- `smokePayload.success=true`
- `targetAddSaved=true`
- `targetEditSaved=true`
- `targetDeleteSaved=true`
- `settingsSaved=true`
- saved telemetry sample interval: `1375`
- final target count in temporary config: `0`

## 6. Target Add/Edit/Delete

PASS.

Source/dev evidence:

- `smoke-temp\source-target-delete-evidence\webview2-ui-smoke.json`

Result:

- exit code `0`
- loaded frontend: `src\frontend\dist`
- `targetAddSaved=true`
- `targetEditSaved=true`
- `targetDeleteSaved=true`
- final target count: `0`

Built payload evidence:

- `smoke-temp\tdel-built-postfix\webview2-ui-smoke.json`

Result:

- loaded frontend: `dist\FrameScopeMonitor-payload\frontend`
- `targetAddSaved=true`
- `targetEditSaved=true`
- `targetDeleteSaved=true`
- final target count: `0`

## 7. Settings Save/Read

PASS.

Target/settings smoke saved Settings:

- `settingsSaved=true`
- saved telemetry sample interval: `1375`

Restart/read smoke evidence:

- `smoke-temp\tdel-built-postfix\settings-persistence-read-smoke.json`

Result:

- exit code `0`
- `success=true`
- `settingsLoaded=true`
- `inputLoaded=true`
- `actualTelemetrySampleIntervalMs=1375`

## 8. P2 Large List Windowing

PASS, not regressed.

Evidence:

- `smoke-temp\p2-windowing-postfix\target-delete-fix-frontend-large-list-probe.json`

Result for `targets-large-process-250`:

- initial `processWindowed=true`
- initial rendered rows: `19 / 250`
- after scroll rendered rows: `10 / 250`
- after filter: `51` rows, `processWindowed=false` because it is below the 250-row threshold
- included target/settings/report smoke: `smokeSuccess=true`

## 9. Explicitly Not Performed

| Action | Performed |
| --- | --- |
| Install FrameScope | No |
| Run setup installer | No |
| Run full setup installer | No |
| Start a real game | No |
| Test BF6 | No |
| Push GitHub | No |
| Update Release | No |

`build.ps1` generated `dist\FrameScopeMonitor-Setup.exe` and `dist\FrameScopeMonitor-Full-Setup.exe`, but neither executable was run.

## 10. Validation Commands

| Command | Result |
| --- | --- |
| Built payload repro before fix | FAIL reproduced: `success=false`, `Target delete evidence did not complete`; evidence `smoke-temp\tdel-repro1\webview2-ui-smoke.json`. |
| `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\node_modules\vitest\vitest.mjs run .\src\uiInteractionContract.test.ts` before UI fix | RED as expected: 1 failed test, found `draftTargets.length === 0`. |
| Same Vitest command after UI fix | PASS: `12` tests passed. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS: `tsc --noEmit` passed; Vitest `6` files / `63` tests passed; Vite build passed. |
| Source/dev target/settings evidence smoke | PASS: exit `0`, final target count `0`; evidence `smoke-temp\source-target-delete-evidence\webview2-ui-smoke.json`. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS: `0` warnings, `0` errors; setup and full setup artifacts generated only. |
| Built payload target/settings evidence smoke | PASS: exit `0`, final target count `0`; evidence `smoke-temp\tdel-built-postfix\webview2-ui-smoke.json`. |
| Built payload settings persistence read smoke | PASS: exit `0`, `actualTelemetrySampleIntervalMs=1375`; evidence `smoke-temp\tdel-built-postfix\settings-persistence-read-smoke.json`. |
| `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tools\Probe-FrontendLargeLists.js --out .\smoke-temp\p2-windowing-postfix --label target-delete-fix --runs 1 --include-smoke` | PASS: 250-row windowing preserved, smoke success true. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS: `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS. |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS. |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS. |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS. |
| `git diff --check` | PASS: exit `0`; output contained LF-to-CRLF warnings only, no whitespace errors. |
| Residual process check | PASS: `NO_MATCHING_RESIDUAL_PROCESSES`. |

## 11. Final Status

Final result: PASS.

The target/settings evidence smoke delete failure was reproduced, diagnosed as a real UI edge case, fixed minimally, and verified against source/dev and newly built payload paths. Target add/edit/delete, Settings save/read, P2 large-list windowing, report-related tests, FPS/chart sampling, CPU Voltage/Vcore, and CPU Core VID regression tests all remain passing.
