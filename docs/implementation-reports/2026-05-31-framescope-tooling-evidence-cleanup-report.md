# FrameScope Tooling Evidence Cleanup Report

Date: 2026-05-31
Workspace: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Scope

This pass only handled the medium-risk tooling/evidence-script candidates:

- `tools\WebView2Spike\`
- `tools\Run-FakeTargetDisplayNameSmoke.ps1`
- `tools\Run-TargetSettingsEvidenceSmoke.ps1`

No frontend component cleanup, GameLite/lightweight cleanup, artifact cleanup, packaging, installed-app smoke, real game launch, BF6 validation, GitHub push, or Release update was performed.

## Pre-change Reference Review

Commands run:

- `rg -n "WebView2Spike" .`
- `rg -n "Run-FakeTargetDisplayNameSmoke" .`
- `rg -n "Run-TargetSettingsEvidenceSmoke" .`

Results:

- `WebView2Spike` was referenced by:
  - `build.ps1` line 15, only as a missing `Microsoft.Web.WebView2` package restore fallback message.
  - Its own source files under `tools\WebView2Spike\`.
  - Existing diagnostics/implementation/history docs.
- `Run-FakeTargetDisplayNameSmoke` was referenced only by diagnostics/history/evidence-status docs before the move. It was not referenced by `build.ps1`, tests, active source, or packaging.
- `Run-TargetSettingsEvidenceSmoke` was referenced only by diagnostics/history/evidence-status docs before the move. It was not referenced by `build.ps1`, tests, active source, or packaging.

Additional active-code check:

- `rg -n "WebView2Spike|WebView2Spike\.csproj|tools[\\/]WebView2Spike|tools\\WebView2Spike" build.ps1 tests tools src packaging`
  - Before the change, the only non-self active hit was the `build.ps1` fallback message.

## Safety Checks

Before moving or deleting, these targets were resolved to absolute paths and checked:

- `tools\WebView2Spike`
- `tools\Run-FakeTargetDisplayNameSmoke.ps1`
- `tools\Run-TargetSettingsEvidenceSmoke.ps1`
- `tools\evidence-smoke`

All resolved paths were inside the workspace, under `tools`, and did not match GameLite/lightweight names or protected docs/artifacts/dist/setup/packaging paths.

## WebView2Spike Decision

Decision: **DELETE**

Reason:

- `tools\WebView2Spike\` was a historical WebView2 proof-of-concept.
- Current app WebView2 ownership is in active app/web host source, not this spike project.
- It was not compiled by `build.ps1` or `tests\Build-FrameScopeTests.ps1`.
- After replacing the stale `build.ps1` fallback text, no active source/script/build/test reference required the deleted path.

Deleted tracked files:

- `tools\WebView2Spike\Program.cs`
- `tools\WebView2Spike\WebView2Spike.csproj`
- `tools\WebView2Spike\wwwroot\index.html`

## build.ps1 Update

Decision: **UPDATED**

Changed:

- Replaced the missing `Microsoft.Web.WebView2` package error message.
- Old message pointed users at the now-deleted `.\tools\WebView2Spike\WebView2Spike.csproj`.
- New message says to restore the `Microsoft.Web.WebView2` NuGet package into the local NuGet cache before running `build.ps1`.

No build steps, compiler inputs, packaging logic, or dependency resolution logic were changed in this pass.

## Smoke Helper Decision

Decision: **MOVE**

Reason:

- Both scripts are evidence rerun helpers, not primary build/test entry points.
- They run installed-app or WebView2 evidence smoke flows and write evidence under `artifacts`, so keeping them grouped under an explicit evidence-smoke directory is clearer than leaving them at the `tools` root.
- The move did not break active build/test/source references.

Moved paths:

- Old: `tools\Run-FakeTargetDisplayNameSmoke.ps1`
- New: `tools\evidence-smoke\Run-FakeTargetDisplayNameSmoke.ps1`

- Old: `tools\Run-TargetSettingsEvidenceSmoke.ps1`
- New: `tools\evidence-smoke\Run-TargetSettingsEvidenceSmoke.ps1`

## Post-change Reference Review

Commands run after cleanup:

- `rg -n "WebView2Spike" .`
- `rg -n "Run-FakeTargetDisplayNameSmoke" .`
- `rg -n "Run-TargetSettingsEvidenceSmoke" .`
- `rg -n "WebView2Spike|Run-FakeTargetDisplayNameSmoke|Run-TargetSettingsEvidenceSmoke" build.ps1 tests tools src packaging`
- `rg -n "tools[\\/]Run-FakeTargetDisplayNameSmoke|tools\\Run-FakeTargetDisplayNameSmoke|tools[\\/]Run-TargetSettingsEvidenceSmoke|tools\\Run-TargetSettingsEvidenceSmoke|tools[\\/]WebView2Spike|tools\\WebView2Spike" build.ps1 tests tools src packaging`
- `rg --files | rg "tools[\\/]evidence-smoke[\\/]|tools[\\/]Run-FakeTargetDisplayNameSmoke|tools[\\/]Run-TargetSettingsEvidenceSmoke|tools[\\/]WebView2Spike"`

Results:

- `build.ps1`, `tests`, `tools`, `src`, and `packaging` have no content references to `WebView2Spike` or the old smoke helper names.
- No current source/script/build/test references point to deleted `tools\WebView2Spike` or the old root-level smoke helper paths.
- `rg --files` shows the new helper locations under `tools\evidence-smoke\`.
- Remaining `WebView2Spike` and old smoke-helper hits are in existing diagnostics/history/evidence docs only. These were intentionally not rewritten to avoid disturbing the evidence chain.

## Protected Areas

GameLite/lightweight:

- No `GameLite*.ps1`, `GameLite*.cmd`, path containing `GameLite`, `Invoke-GameLiteSGuardThrottle.ps1`, `tests\lightweight-separation-tests.ps1`, `dist\gamelite-*`, `dist\watcher-lite-*`, or `game-lite-watcher.log` file was modified or deleted.

Docs/artifacts/dist/setup:

- No existing docs evidence-chain files were modified or deleted.
- No `artifacts` evidence directory was modified or deleted.
- No `dist` setup/full setup output was modified or deleted.
- The only docs change was adding this required implementation report.

Explicit non-actions:

- No product packaging was run.
- No product install/update was run.
- `tools\Run-Frontend.ps1 verify` installed frontend npm dependencies and ran Vite build as part of verification; this is recorded as normal verification-script behavior, not product installation or release packaging.
- No real game was launched.
- BF6 was not tested.
- Nothing was pushed to GitHub.
- No Release was updated.

## Verification

All required verification commands were run after the cleanup.

| Command | Result |
| --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS, exit 0. Script used bundled Node, installed 110 npm packages, ran typecheck, Vitest `5` files / `57` tests passed, and completed Vite build. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS, exit 0. Output: `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS, exit 0. Output ended with `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS, exit 0. Output: `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS, exit 0. Output: `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| Bundled Node: `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tests\chart-sampling-tests.js` | PASS, exit 0. Output: `chart-sampling-tests: PASS`. |
| `git diff --check` | PASS, exit 0. Only LF/CRLF warnings were printed; no whitespace errors. |
| Residual process check | PASS, exit 0. Output: `NO_MATCHING_RESIDUAL_PROCESSES`. |

## Final Status

Conclusion: **PASS**

The historical WebView2 spike source was removed after replacing the only active fallback reference. The two evidence smoke helpers were moved into `tools\evidence-smoke\`. Current source/script/build/test references do not point at deleted or old root-level paths. This pass did not touch protected GameLite/lightweight, evidence, artifact, setup, packaging, release, or real-game areas.
