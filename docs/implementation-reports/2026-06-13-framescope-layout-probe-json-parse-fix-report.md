# FrameScope Layout Probe JSON Parse Fix Report

Date: 2026-06-13

Status: PASS

## Scope

- Target tool: `tools\Probe-ReportHtmlLayout.js`
- Evidence directory: `docs\test-reports\2026-06-13-framescope-layout-probe-json-parse-fix-evidence`
- Input report: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260613-152103\charts\framescope-interactive-report.html`
- This round did not change product chart rendering logic.
- This round did not install, package, launch a real game, test BF6, push GitHub, or update Release.

## Root Cause

The previous `report-overflow-probe.json` was valid UTF-8 JSON when read as UTF-8:

- Bundled Node `JSON.parse`: PASS.
- PowerShell `Get-Content -Raw -Encoding UTF8 | ConvertFrom-Json`: PASS.

It failed only when read through the requested default PowerShell path:

- `Get-Content -Raw | ConvertFrom-Json`: FAIL.
- Error: `Invalid object passed in, ':' or '}' expected.`

Byte-level inspection of the latest failing file showed:

- Path: `docs\test-reports\2026-06-13-framescope-chart-screen-space-same-x-artifact-fix-retest-evidence\layout-probe\report-overflow-probe.json`
- Size: `78315` bytes.
- UTF-8 BOM: `False`.
- NUL bytes: `0`.
- Low control characters except TAB/LF/CR: `0`.
- File starts with `{` and ends with `}`; no extra console text was prepended or appended.
- The file was a single JSON object, not concatenated JSON.

The failure was caused by UTF-8-without-BOM localized Chinese strings being decoded by Windows PowerShell 5.1's default `Get-Content` encoding. That mojibake changed valid JSON string text into text that appeared to have broken string termination, so `ConvertFrom-Json` rejected it. Product HTML/report output was not the source of illegal JSON.

## Fix

Updated `tools\Probe-ReportHtmlLayout.js`:

- Keeps using `JSON.stringify(summary, null, 2)` as the source of the JSON document.
- Escapes all non-ASCII JSON output as `\uXXXX`, producing ASCII-safe UTF-8 JSON.
- Writes the file with explicit `utf8`.
- Adds a post-write PowerShell validation step using default `Get-Content -Raw | ConvertFrom-Json`.
- The validation checks:
  - JSON parses through PowerShell.
  - `allNoOverflow` is true unless `--allow-overflow` was explicitly used.
  - Every scenario screenshot path exists and is non-empty.
- Console output remains separate from the JSON file; logs are not appended to the JSON artifact.

## Fresh Layout Probe Evidence

Command:

```powershell
C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tools\Probe-ReportHtmlLayout.js --report "C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260613-152103\charts\framescope-interactive-report.html" --diagnostic "C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs\Counter-Strike-2\Counter-Strike-2-20260613-152103\charts\framescope-interactive-report.html" --out .\docs\test-reports\2026-06-13-framescope-layout-probe-json-parse-fix-evidence\layout-probe
```

Result:

- Probe exit code: `0`.
- Output JSON: `docs\test-reports\2026-06-13-framescope-layout-probe-json-parse-fix-evidence\layout-probe\report-overflow-probe.json`
- `Get-Content -Raw | ConvertFrom-Json`: PASS.
- Bundled Node `JSON.parse`: PASS.
- `allNoOverflow=True`.
- Scenario count: `23`.
- Viewport set: `1280x720`, `900x760`.
- Overflow count: `0`.
- Fresh JSON size: `81544` bytes.
- Fresh JSON UTF-8 BOM: `False`.
- Fresh JSON NUL bytes: `0`.
- Fresh JSON non-ASCII bytes: `0`.

Focus screenshots:

| Screenshot | Nonblank |
| --- | --- |
| `cpu-voltage-1280x720.png` | PASS |
| `background-process-1280x720.png` | PASS |
| `cpu-voltage-900x760.png` | PASS |
| `background-process-900x760.png` | PASS |

## Same-X Regression Guard

Product chart logic was not changed in this round.

Fresh `tests\chart-sampling-tests.js` passed and still covers:

- `Number(null)` invalid telemetry gaps are not converted to `0`.
- Dense Vcore data has no adjacent duplicate screen-x render points.
- Dense Vcore data has no same-screen-x vertical segments.
- Process spike compaction keeps visible peaks while avoiding same-screen-x vertical segments.

The existing CS2 screen-space stats from the same artifact-fix retest remain:

- Vcore duplicate screen x: `0`.
- Vcore same-x vertical: `0`.
- Vcore finite range: `0.948-1.116V`.
- `DATA.cpuVoltage.available=true`.
- `DATA.cpuVid.available=false`.
- `DATA.fps.bucketMs=1000`.
- Process duplicate screen x: `0` for Top 10.
- Process same-x vertical: `0` for Top 10.

## Verification

Passed:

- Bundled Node `--check .\tools\Probe-ReportHtmlLayout.js`
- Bundled Node `.\tools\Probe-ReportHtmlLayout.js` on the CS2 report.
- PowerShell `Get-Content -Raw | ConvertFrom-Json` on fresh `report-overflow-probe.json`.
- Bundled Node `JSON.parse` on fresh `report-overflow-probe.json`.
- Focus screenshot pixel sampling for four key chart screenshots.
- Bundled Node `.\tests\chart-sampling-tests.js`
  - `chart-sampling-tests: PASS`
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
  - TypeScript typecheck passed.
  - Vitest: `6` files, `64` tests passed.
  - Vite production build passed.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
  - `FrameScope tests rebuilt.`
- `.\tests\FrameScopeReportManifestTests.exe`
  - `FrameScopeReportManifestTests: PASS`
- `.\tests\FrameScopeDiagnosticsTests.exe`
  - `FrameScopeDiagnosticsTests: PASS`
- `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe`
  - `FrameScopeSystemSamplerCpuCoreTests: PASS`
- `git diff --check`
  - Exit code `0`; Git printed LF/CRLF working-copy warnings only.
- Final residual process check:
  - `NO_MATCHING_RESIDUAL_PROCESSES`

## Not Done

- Did not package.
- Did not install.
- Did not launch a real game.
- Did not test BF6.
- Did not push GitHub.
- Did not update Release.
