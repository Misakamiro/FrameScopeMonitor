# FrameScope GitHub publish report

Date: 2026-06-03 Asia/Hong_Kong

Workspace: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Final status: PASS

## Scope

This publish pass updated GitHub-facing documentation, regenerated setup artifacts, reran the requested validation commands, and pushed the release documentation update to `origin main`.

No GitHub Release was created or updated. No installer was run in this pass. No real game or BF6 validation was performed in this pass.

## README update summary

- Repositioned FrameScope Monitor as a local Windows game performance troubleshooting tool.
- Documented WebView2 React UI as the default interface.
- Documented GamePP-style FPS report charts while preserving raw PresentMon statistics and `bucketMs=1000`.
- Documented GamePP-style unification across report charts.
- Documented CPU Voltage / Vcore and CPU Core VID as separate metrics:
  - CPU Voltage / Vcore is the overall real-voltage view.
  - CPU Core VID is the requested/target-voltage view.
  - VID is not used as fake Vcore.
- Documented CPU Core VID recording/chart support.
- Documented that deleting the last target can save an empty target list.
- Documented performance improvements for large reports, backend monitoring, process chart interaction, frontend list windowing, UI motion, logging, tail trim, and data-root scan guard.
- Preserved the download/install distinction between Setup and Full Setup.
- Documented local install validation results without claiming real-game or BF6 validation.

## CHANGELOG update summary

Created `CHANGELOG.md` with sections for:

- New features.
- Fixes.
- Performance optimizations.
- Reports/charts.
- Installation/packaging validation.
- Test validation.
- Not included: no GitHub Release update, no real-game validation, and no BF6 real-game validation.

## Packaging artifacts

| Artifact | Exists | Non-empty | Size bytes | SHA256 |
| --- | --- | --- | ---: | --- |
| `dist\FrameScopeMonitor-Setup.exe` | Yes | Yes | 2,703,872 | `59362ABF231719C63760946DBEB6C0A5FF0C70B5A4E2A49818FCBEAA4DDACCDD` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | Yes | Yes | 201,883,136 | `30EFC42A191F9AF56FDDAF7A2385C68D0233228FE51DF7C2796C1F564CC59AE2` |
| `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe` | Yes | Yes | 359,424 | `3C3DE62B5EFF35B65CD8FF45B63F1BBAA7B64C99119E5BBFCDD7792B0F4BA8F4` |

Additional payload exe hashes captured:

| Payload exe | Size bytes | SHA256 |
| --- | ---: | --- |
| `dist\FrameScopeMonitor-payload\FrameScopeProcessSampler.exe` | 14,336 | `ACB7315191E54F134BB462162F6F182D2D212387F5DBAA23D2B8E0D2D51DEB8A` |
| `dist\FrameScopeMonitor-payload\FrameScopeSystemSampler.exe` | 57,344 | `66E89ADAF323A262BD3D06F8387A66047FF2EB7697073743E4BD50AE44B2C6C9` |
| `dist\FrameScopeMonitor-payload\FrameScopeReportGenerator.exe` | 194,560 | `650179CFCFACD8216C60130701EACDB76E26EEB6C9AA9569F6690E6F58C883B1` |

## Verification results

| Command/check | Result |
| --- | --- |
| `git status --short --branch` | PASS, ran before edits and before staging; branch is `main...origin/main`. |
| `git remote -v` | PASS, `origin` is `https://github.com/Misakamiro/FrameScopeMonitor.git`. |
| README encoding check | PASS, `README.md` is valid UTF-8 without BOM after explicit UTF-8 write. |
| CHANGELOG check | PASS, `CHANGELOG.md` was created and is valid UTF-8 without BOM. |
| `.gitignore` / tracking audit | PASS, `dist/`, `*.exe`, frontend build output, node_modules, and runtime data are ignored; tracked docs were identified. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS, exit `0`; typecheck PASS, Vitest `6` files / `63` tests PASS, Vite build PASS. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS, exit `0`; setup and full setup artifacts regenerated. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS, exit `0`; `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS, exit `0`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS, exit `0`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS, exit `0`. |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS, exit `0`. |
| `.\tests\FrameScopeLoggingPolicyTests.exe` | PASS, exit `0`. |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS, exit `0`. |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS, exit `0`. |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS, exit `0`. |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS, exit `0`; output `chart-sampling-tests: PASS`. |
| `git diff --check` | PASS, exit `0`; output contained LF/CRLF working-copy warnings only, no whitespace-error failure. |
| Residual process check | PASS, final status `NO_MATCHING_RESIDUAL_PROCESSES`. |

Command logs were written to local-only `smoke-temp\publish-20260603\logs` and are intentionally not part of the commit.

## Staging decision

- `dist/` artifacts are build outputs and are ignored; they are verified by existence/hash and not submitted.
- Root runtime DLLs and root exe build outputs are not staged.
- `smoke-temp/` is local evidence and is not staged.
- `docs/test-reports` evidence directories are large, about 2.5 GB total, and are not staged.
- `docs/test-reports/*.md` files are small enough to stage as report summaries.
- `docs/diagnostics` contains about 474 MB of diagnostic CSV/JSON/PNG evidence; only small `.md` diagnostic summaries are candidates, not the heavy evidence files.
- GameLite/lightweight diff check returned no changed path matches.

## Git result

Release documentation commit pushed to GitHub: `a31bac557feeec42a66e9ee897044c136b8ff7b1`.

Push result: PASS, `git push origin main` completed successfully with `a17cd18..a31bac5  main -> main`.

This report was updated after the release documentation push so the report itself records the push result. The final assistant response records the final HEAD hash for this report-finalization commit because a Git commit cannot contain its own final content-addressed hash inside the file being committed.

## Explicit non-actions

| Item | Result |
| --- | --- |
| GitHub Release created or updated | No |
| Installer run during this pass | No |
| Real game validation during this pass | No |
| BF6 real-game validation during this pass | No |

## Verdict

PASS. README and CHANGELOG were updated, setup artifacts were regenerated and hashed, all requested validation commands passed, residual process check ended clean, no GitHub Release was updated, and no real-game/BF6 validation was claimed.
