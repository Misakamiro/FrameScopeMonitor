# FrameScope Frontend Dead Component Cleanup Report

Date: 2026-05-31

## Scope

This pass only handled the Low-risk frontend cleanup candidates explicitly allowed for this round:

- `src/frontend/src/components/ProcessRow.tsx`
- `src/frontend/src/components/ReportRow.tsx`
- `src/frontend/src/components/SettingsField.tsx`
- Dead selectors in `src/frontend/src/components/components.css` that belonged to those components.

No medium/high-risk candidates were processed.

## Deleted Files

- Deleted `src/frontend/src/components/ProcessRow.tsx`.
- Deleted `src/frontend/src/components/ReportRow.tsx`.
- Deleted `src/frontend/src/components/SettingsField.tsx`.

## Removed CSS Selectors

Removed the following target-only selectors from `src/frontend/src/components/components.css`:

- `.process-row`
- `.process-row + .process-row`
- `.process-row__identity`
- `.process-row__identity strong`
- `.process-row__identity small`
- `.process-row__icon`
- `.process-row .status-pill`
- `.report-row`
- `.report-row + .report-row`
- `.report-row strong`
- `.report-row small`
- `.report-row p`
- `.report-row__body`
- `.report-row__body > div`
- `.report-row__body p`
- `.report-row__icon`
- `.report-row__meta`
- `.settings-field`
- `.settings-field strong`
- `.settings-field small`
- `.settings-field input`
- `.settings-field__select`
- `.settings-field__toggle`

Retained non-target current-page styles:

- `src/frontend/src/pages/pages.css` `.report-row-actions`
- `src/frontend/src/pages/ReportsPage.tsx` `report-row-actions`
- `src/frontend/src/pages/pages.css` `process-row-settle`
- `src/frontend/src/uiMotionContract.test.ts` assertion for `process-row-settle`

## Reference Recheck Before Deletion

Command: `rg -n "ProcessRow|process-row" src\frontend\src`

- Found `ProcessRow` only in `src/frontend/src/components/ProcessRow.tsx`.
- Found `.process-row*` only in `src/frontend/src/components/components.css`.
- Also found non-target current animation references `process-row-settle` in `pages.css` and `uiMotionContract.test.ts`; these were not deleted.
- No runtime import, test import, tool reference, or non-dead component usage found for `ProcessRow`.

Command: `rg -n "ReportRow|report-row" src\frontend\src`

- Found `ReportRow` only in `src/frontend/src/components/ReportRow.tsx`.
- Found `.report-row*` dead component selectors in `src/frontend/src/components/components.css`.
- Also found current `.report-row-actions` references in `pages.css` and `ReportsPage.tsx`; these were retained.
- No runtime import, test import, tool reference, or non-dead component usage found for `ReportRow`.

Command: `rg -n "SettingsField|settings-field" src\frontend\src`

- Found `SettingsField` only in `src/frontend/src/components/SettingsField.tsx`.
- Found `.settings-field*` only in `src/frontend/src/components/components.css`.
- No runtime import, test import, tool reference, or non-dead component usage found for `SettingsField`.

## Reference Recheck After Cleanup

Command: `rg -n "ProcessRow|process-row" src\frontend\src`

- Exit code: 0.
- Remaining matches are only non-target `process-row-settle` animation references in `pages.css` and `uiMotionContract.test.ts`.
- No `ProcessRow.tsx`, no `ProcessRow` component symbol, and no `.process-row*` dead selectors remain.

Command: `rg -n "ReportRow|report-row" src\frontend\src`

- Exit code: 0.
- Remaining matches are only current `.report-row-actions` references in `pages.css` and `ReportsPage.tsx`.
- No `ReportRow.tsx`, no `ReportRow` component symbol, and no `.report-row*` dead selectors remain.

Command: `rg -n "SettingsField|settings-field" src\frontend\src`

- Exit code: 1.
- No matches remain.

Additional precision checks:

- `rg -n "ProcessRow|ReportRow|SettingsField" src\frontend\src`: exit code 1, no matches.
- `rg -n -P "\.process-row|\.report-row(?!-actions)|\.settings-field" src\frontend\src`: exit code 1, no dead target selectors remain.
- `rg -n "\.process-row|\.report-row|\.settings-field" src\frontend\src\components\components.css`: exit code 1, no target selectors remain in `components.css`.
- `rg -n "report-row-actions" src\frontend\src\pages\pages.css src\frontend\src\pages\ReportsPage.tsx`: exit code 0, current page selector remains present.
- `Test-Path` for all three deleted component files returned `False`.

## Skipped Items

No allowed cleanup candidate was skipped.

Non-target matches intentionally retained:

- `process-row-settle` animation references, because they belong to current `process-result-row--updated` motion behavior and are covered by `uiMotionContract.test.ts`.
- `.report-row-actions`, because it is a current Reports page selector and was explicitly protected from accidental deletion.

## Explicit Non-Touched Areas

- Did not delete or modify `src/frontend/src/components/Toast.tsx`.
- Did not delete or modify `src/frontend/src/components/MetricCard.tsx`.
- Did not delete or modify `tools/WebView2Spike`.
- Did not delete or modify `tools/Run-FakeTargetDisplayNameSmoke.ps1`.
- Did not delete or modify `tools/Run-TargetSettingsEvidenceSmoke.ps1`.
- Did not delete or modify any GameLite, lightweight, watcher-lite, or gamelite file or directory.
- Did not delete docs evidence chains, artifacts evidence directories, dist setup, or full setup.
- Did not run `git reset --hard`.
- Did not run `git clean -fdx`.

Status notes:

- `git diff --name-status --` for the allowed component files only showed the three deletions plus `components.css` modification.
- Filtered GameLite/lightweight status check returned no matching status output.
- The two smoke helper scripts appeared as pre-existing untracked files in the initial status and remained untracked after this pass; this cleanup did not modify or delete them.

## Packaging, Install, Game, BF6, GitHub, Release

- Did not run FrameScope packaging.
- Did not build or update installer/setup artifacts.
- Did not run a product install flow.
- Did not launch a real game.
- Did not run a real BF6 test. `FrameScopeReportManifestTests.exe` emitted a synthetic `bf6.exe` fixture in test output only.
- Did not push to GitHub.
- Did not update Release notes or release artifacts.

Note: The required `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` verification command internally emitted `added 110 packages` and ran `vite build`. This was part of the requested frontend verification path, not a FrameScope product install, packaging run, setup build, or release update.

## Verification Results

Command: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`

- Exit code: 0.
- TypeScript typecheck passed.
- Vitest passed: 5 test files, 57 tests.
- Vite build completed successfully.

Command: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`

- Exit code: 0.
- Output: `FrameScope tests rebuilt.`

Command: `.\tests\FrameScopeReportManifestTests.exe`

- Exit code: 0.
- Output ended with `FrameScopeReportManifestTests: PASS`.

Command: `.\tests\FrameScopeDiagnosticsTests.exe`

- Exit code: 0.
- Output: `FrameScopeDiagnosticsTests: PASS`.

Command: `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`

- Exit code: 0.
- Output: `FrameScopeSystemSamplerCpuCoreTests: PASS`.

Command: `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tests\chart-sampling-tests.js`

- Exit code: 0.
- Output: `chart-sampling-tests: PASS`.

Command: `git diff --check`

- Exit code: 0.
- No whitespace errors reported.
- Git emitted line-ending normalization warnings for existing modified files.

Command: residual process check via `Get-CimInstance Win32_Process`

- Exit code: 0.
- Output: `NO_MATCHING_RESIDUAL_PROCESSES`.

## Final Conclusion

PASS.

All allowed Low-risk dead frontend components and their component-local dead CSS selectors were removed. No allowed cleanup candidate was skipped. Current non-target selectors and animation names were retained. Required verification commands passed, and the final residual process check returned `NO_MATCHING_RESIDUAL_PROCESSES`.
