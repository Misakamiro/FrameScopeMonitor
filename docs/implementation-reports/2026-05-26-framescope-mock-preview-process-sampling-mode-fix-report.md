# FrameScope mock preview ProcessSamplingMode fix report

Date: 2026-05-26 Asia/Hong_Kong

Source root:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Verdict

PASS.

The frontend typecheck regression found by the integration retest is fixed. `tools\Run-Frontend.ps1 verify` now completes typecheck, Vitest, and Vite build successfully.

No C# host files, sampler files, performance logic, `build.ps1`, packaging flow, installer output, or GitHub release flow were changed by this fix.

## Root cause

`src\frontend\src\bridge\contract.ts` currently defines `FrameScopeTargetConfig.ProcessSamplingMode` as a required field:

```ts
ProcessSamplingMode: "normal" | "high-precision" | string;
```

`src\frontend\src\data\mockPreview.ts` built `mockConfig.Targets` from `targetPreview.map(...)`, but the mapped target object did not include `ProcessSamplingMode`. TypeScript correctly rejected that object as incomplete during `tsc --noEmit`.

## Scope checked

- Read `docs\test-reports\2026-05-26-framescope-theme-tray-cpu-telemetry-performance-integration-retest-report.md`.
- Checked `src\frontend\src\bridge\contract.ts`: `ProcessSamplingMode` is required, so I did not make it optional.
- Checked `src\frontend\src\data\mockPreview.ts`: the mock target builder was the failing source.
- Checked `src\frontend\src\data\mockPreview.test.ts`: added one contract assertion for process sampling profile semantics.
- Checked `src\frontend\src\uiDesignContract.test.ts` and `src\frontend\src\uiInteractionContract.test.ts`: no ProcessSamplingMode-specific sync was needed there.

## Files changed by this fix

- `src\frontend\src\data\mockPreview.ts`
  - Added `ProcessSamplingMode` to each generated mock target.
  - Kept current mock interval behavior unchanged.
  - Targets with `sampleMs <= 100` now map to `"high-precision"` because the mock builder also maps that value to `ProcessSampleIntervalMs`.
  - Other mock targets map to `"normal"`.

- `src\frontend\src\data\mockPreview.test.ts`
  - Added a source-level contract assertion that the mock target builder keeps the process sampling profile mapping explicit.

- `docs\implementation-reports\2026-05-26-framescope-mock-preview-process-sampling-mode-fix-report.md`
  - This report.

Note: the working tree already had unrelated modified and untracked files before this fix. I did not revert or normalize those existing changes.

## Red/green evidence

Initial reproduction:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result: FAIL at `npm run typecheck`.

Key error:

```text
src/data/mockPreview.ts(289,3): error TS2322:
Property 'ProcessSamplingMode' is missing ... but required in type 'FrameScopeTargetConfig'.
```

Added a focused `mockPreview.test.ts` assertion before the mock data fix, then ran:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 test
```

Result before `mockPreview.ts` fix: FAIL. The new test failed because `mockPreview.ts` did not contain `ProcessSamplingMode`.

After the fix, the same test command passed:

```text
Test Files  5 passed (5)
Tests       49 passed (49)
```

## Full frontend verify result

Command:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result: PASS.

Stages completed:

- `npm ci`: PASS, 110 packages installed.
- `npm run typecheck`: PASS.
- `npm test`: PASS, 5 test files and 49 tests passed.
- `npm run build`: PASS, Vite built successfully.

Build output summary:

```text
dist/index.html
dist/assets/index-CZ7x6juY.css
dist/assets/index-Be6Q-U2L.js
```

## git diff --check

Command:

```text
git diff --check
```

Result: PASS, exit code 0.

Only existing LF-to-CRLF working-copy warnings were printed. No whitespace errors were reported.

## Residual process check

Process scan included FrameScope executables, PresentMon/FakePresentMon, and repo-related Node processes.

Result: PASS.

```json
{
  "checkedAt": "2026-05-26T22:32:13.6717010+08:00",
  "matchingResidualCount": 0,
  "matches": []
}
```

## Recommendation

Yes, rerun the integration retest.

The previous integration retest was blocked at the mandatory frontend verify gate. That gate now passes, so the next useful step is to rerun the original integration retest chain from at least:

- frontend verify
- WebView2 live smoke
- WebView2 reduced-motion smoke
- tray lifecycle smoke
- synthetic CPU telemetry enabled/disabled sessions
- focused performance matrix
- `git diff --check`
- residual process check

Do not move to local installed-app update validation or packaging until that retest is green.
