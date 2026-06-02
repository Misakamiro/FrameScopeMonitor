# FrameScope Frontend Medium Component Cleanup Report

Date: 2026-05-31

Workspace: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Scope

This pass only handled the medium-risk frontend candidates explicitly allowed for this round:

- `src/frontend/src/components/Toast.tsx`
- `src/frontend/src/components/MetricCard.tsx`
- `.toast*`, `.toast-preview*`, and `.metric-card*` dead selectors in `src/frontend/src/components/components.css`
- The MetricCard-related frontend contract test reference, only because it was a raw old-source contract reference rather than a runtime UI import.

No WebView2Spike, smoke helper, GameLite, lightweight, evidence-chain, packaging, installer, release, GitHub, real game, or BF6 validation work was performed.

## Pre-Cleanup Reference Recheck

Command: `rg -n "Toast|toast|toast-preview" src\frontend\src`

- Found `Toast` only in `src/frontend/src/components/Toast.tsx`.
- Found `.toast-preview*` only in `src/frontend/src/components/components.css`.
- Found `--fs-toast-radius` and `--fs-toast-bg` in `src/frontend/src/theme/tokens.css`.
- Found `background: var(--fs-toast-bg)` in `components.css` through the shared `InlineStatus` styling.
- No runtime import or current page usage of `Toast` was found.

Command: `rg -n "MetricCard|metric-card" src\frontend\src`

- Found `MetricCard` only in `src/frontend/src/components/MetricCard.tsx`.
- Found `.metric-card*` only in `src/frontend/src/components/components.css`.
- Found one raw-source test import in `src/frontend/src/uiInteractionContract.test.ts`.
- No runtime import or current page usage of `MetricCard` was found.

## Test Intent Review

The only MetricCard test reference was:

- `src/frontend/src/uiInteractionContract.test.ts`
- Import form: `import metricCardSource from "./components/MetricCard.tsx?raw";`
- Assertion intent: ensure primary page cards do not use mount fade animation (`initial={{ opacity: 0`).

This was not testing a runtime UI path. It was locking an old component file as a source fixture. Current runtime cards are rendered through `GlassCard` and page-local markup in `OverviewPage.tsx`, including `monitor-panel monitor-panel--primary` and `overview-summary-grid`.

The test was updated to keep the same no-fade interaction contract against current UI code:

- Keep checking `GlassCard.tsx` for no `initial={{ opacity: 0`.
- Check `OverviewPage.tsx` contains the current primary monitor panel and summary grid.
- Check `OverviewPage.tsx` does not contain `initial={{ opacity: 0`.
- Remove the raw `MetricCard.tsx?raw` import so the test no longer preserves a deleted component as a fixture.

## Deleted Files

- Deleted `src/frontend/src/components/Toast.tsx`.
- Deleted `src/frontend/src/components/MetricCard.tsx`.

## CSS Selectors Removed

Removed the following dead component-only selectors and grouped selector references from `src/frontend/src/components/components.css`:

- `.metric-card`
- `.metric-card__topline`
- `.metric-card__value`
- `.metric-card p`
- `.toast-preview span`
- `.toast-preview strong`
- `.toast-preview` from the grouped `.inline-status, .toast-preview` rule
- Standalone `.toast-preview`

Retained current non-target styling:

- `.chart-shell__header`
- `.chart-shell p`
- `.empty-state p`
- `.inline-status`
- `.inline-status span`
- `.inline-status strong`
- `background: var(--fs-toast-bg)` for `.inline-status`
- `--fs-toast-radius` / `--fs-toast-bg` tokens in `tokens.css`

The retained `--fs-toast-*` names are visual token variables, not dead selectors. `--fs-toast-bg` is still used by the current `InlineStatus` rule. Renaming or deleting current tokens was outside this cleanup scope and would risk touching shared visual token behavior.

## Post-Cleanup Reference Recheck

Command: `rg -n "Toast|toast|toast-preview" src\frontend\src`

- Exit code: 0.
- Remaining matches are only token/current-style references:
  - `src/frontend/src/theme/tokens.css`: `--fs-toast-radius`
  - `src/frontend/src/theme/tokens.css`: `--fs-toast-bg`
  - `src/frontend/src/components/components.css`: `background: var(--fs-toast-bg)`
- No `Toast.tsx`, no `Toast` component symbol, no `.toast-preview*` selector, and no runtime import remain.

Command: `rg -n "MetricCard|metric-card" src\frontend\src`

- Exit code: 1.
- No matches remain.

Command: `rg -n -P "\.(?:toast|toast-preview|metric-card)" src\frontend\src\components\components.css`

- Exit code: 1.
- No target dead selectors remain in `components.css`.

Command: `Test-Path 'src\frontend\src\components\Toast.tsx'; Test-Path 'src\frontend\src\components\MetricCard.tsx'`

- Exit code: 0.
- Output: `False`, `False`.

## Skipped Items

No allowed Toast or MetricCard cleanup item was skipped.

Retained by design:

- `--fs-toast-radius` / `--fs-toast-bg` tokens in `src/frontend/src/theme/tokens.css`.
- `background: var(--fs-toast-bg)` in the current `.inline-status` styling.

These were retained because the instruction allowed selector cleanup in `components.css` but also required not deleting current visual tokens or shared variables.

## Explicit Non-Touched Areas

- Did not delete or modify `tools/WebView2Spike`.
- Did not delete or modify `tools/Run-FakeTargetDisplayNameSmoke.ps1`.
- Did not delete or modify `tools/Run-TargetSettingsEvidenceSmoke.ps1`.
- Did not delete or modify any GameLite, lightweight, watcher-lite, or automatic lightweight file or directory, including:
  - `GameLite*.ps1`
  - `GameLite*.cmd`
  - any path containing `GameLite`
  - `Invoke-GameLiteSGuardThrottle.ps1`
  - `tests/lightweight-separation-tests.ps1`
  - `dist/gamelite-*`
  - `dist/watcher-lite-*`
  - `game-lite-watcher.log`
- Did not delete docs evidence chains.
- Did not delete artifacts evidence directories.
- Did not delete `dist` setup or full setup outputs.
- Did not run `git reset --hard`.
- Did not run `git clean -fdx`.

Guard checks:

- `git diff --name-only -- tools\WebView2Spike tools\Run-FakeTargetDisplayNameSmoke.ps1 tools\Run-TargetSettingsEvidenceSmoke.ps1`: exit code 0, no output.
- `git status --short -- *GameLite* *lightweight* Invoke-GameLiteSGuardThrottle.ps1 tests\lightweight-separation-tests.ps1 dist\gamelite-* dist\watcher-lite-* game-lite-watcher.log`: exit code 0, no output.

## Packaging, Install, Game, BF6, GitHub, Release

- Did not run FrameScope packaging.
- Did not build or update installer/setup artifacts.
- Did not run a product install flow.
- Did not launch a real game.
- Did not run a real BF6 test.
- Did not push to GitHub.
- Did not update Release notes or release artifacts.

Notes:

- `Run-Frontend.ps1 verify` internally ran npm package restore and Vite build. This was the requested frontend verification behavior, not a product install, package build, setup build, release update, or user-facing app launch.
- `FrameScopeReportManifestTests.exe` emitted synthetic `bf6.exe` fixture data in test output only. That was not a real BF6 test.

## Verification Results

Command: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`

- Exit code: 0.
- Script used bundled Node: `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`.
- Script emitted `added 110 packages in 4s`.
- TypeScript typecheck passed.
- Vitest passed: 5 test files, 57 tests.
- Vite production build completed successfully.

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

Command: residual process check with `Get-CimInstance Win32_Process`

- Exit code: 0.
- Output: `NO_MATCHING_RESIDUAL_PROCESSES`.

## Final Conclusion

PASS.

Toast and MetricCard were both deleted because neither had runtime imports or current page usage. MetricCard's only remaining reference was an old raw-source test fixture, and that test was updated to preserve the current no-mount-fade UI contract against `GlassCard` and `OverviewPage` instead. Dead `.toast-preview*` and `.metric-card*` selectors were removed from `components.css`. Current visual tokens and active `InlineStatus` styling were retained. All required verification commands passed, and the final residual process check returned `NO_MATCHING_RESIDUAL_PROCESSES`.
