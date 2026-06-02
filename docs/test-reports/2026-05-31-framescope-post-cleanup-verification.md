# FrameScope post-cleanup verification

Date: 2026-05-31

Scope: only verification after cleanup. No source bug fixes, no source refactors, no GitHub push, no release update, no installer/package build, no FrameScope install, no real game launch, no BF6 test.

Final conclusion: PARTIAL.

Core project validation passed, but this verification run has a boundary deviation: an accidental `FrameScopeReportGenerator.exe --help` probe treated `--help` as a run directory and created a non-source generated `--help/` report folder outside `docs/test-reports`. It was not deleted or moved because this round explicitly forbids deletion/move operations. No source file was edited by this round.

## Direct answers

1. Cleanup project structure usable: Yes. Frontend verify, C# test build, report manifest, diagnostics, CPU sampler, chart sampling, and layout probe all passed after using valid report inputs.
2. Old frontend components still referenced by current source: No. `rg -n "ProcessRow|ReportRow|SettingsField|Toast|MetricCard|WebView2Spike" src tests tools build.ps1` returned exit code 1 with no matches.
3. WebView2Spike still referenced by current build/test/tool entries: No. Same `rg` command returned no matches in `src`, `tests`, `tools`, or `build.ps1`.
4. Smoke helper new paths exist: Yes. `Test-Path .\tools\evidence-smoke\Run-FakeTargetDisplayNameSmoke.ps1` and `Test-Path .\tools\evidence-smoke\Run-TargetSettingsEvidenceSmoke.ps1` both returned `True`.
5. GameLite/lightweight untouched: Yes by tracked-worktree evidence. `git status --short -- "*GameLite*" "*lightweight*"`, `git diff --name-status -- "*GameLite*" "*lightweight*"`, and `git ls-files -d -- "*GameLite*" "*lightweight*"` returned no output.
6. FPS GamePP / `bucketMs=1000` / raw stats semantics retained: Yes. `chart-sampling-tests: PASS`; current source contains `FPS GamePP chart` and the note that bucketed FPS display keeps raw PresentMon statistics intact; v3 evidence report has `fps.bucketMs=1000`, `presentMon.rawRows=240`, and `presentMon.validRows=240`.
7. CPU Voltage / Vcore still independent: Yes. Source uses `DATA.cpuVoltage`, title `CPU Voltage / Vcore`, unit `V`; v3 evidence report has `cpuVoltage.available=true`, `cpuVoltage.unit="V"`, and series `cpu-voltage:vcore:CPU Voltage / Vcore`.
8. CPU Core VID still independent: Yes. Source uses `DATA.cpuVid`, title `CPU Core VID`, unit `V`; v3 evidence report has `cpuVid.available=true`, `cpuVid.unit="V"`, and independent `cpu-vid:*` series.
9. VID/Vcore bidirectional isolation: Yes. Source note says VID is request/target voltage and not real Vcore; CPU Voltage note says VID/SOC/Package/VBAT/VIN are not used for CPU Voltage. v3 evidence keeps `cpuVoltage` and `cpuVid` as separate data objects and separate chart views.
10. Layout probe `allNoOverflow=true`: Yes. Bundled Node layout probe on v3 evidence output `allNoOverflow=true`, 23 scenarios. Pixel checks over 16 key chart screenshots all returned `nonBlank=true`.
11. This round source modifications/delete/move: Source modifications: No. Delete/move operations: No. New files were generated under `docs/test-reports/...post-cleanup-verification-evidence/` and the final report. Boundary deviation: accidental generated `--help/` folder outside the allowed report directory.
12. Packaging/install/real game/BF6/GitHub/Release: Not performed. `Run-Frontend.ps1 verify` installed frontend npm packages and generated frontend `dist` as part of the verification script; that is recorded as normal verification behavior, not product install or packaging.

## Required command results

| Command | Result |
| --- | --- |
| `git status --short` | Exit 0. Existing cleanup worktree was already dirty at start, including many modified source files, deleted old frontend components, deleted `tools/WebView2Spike/*`, and many untracked docs/artifacts. Final status additionally shows this round's `docs/test-reports/2026-05-31-framescope-post-cleanup-verification-evidence/`, this report, and accidental `--help/`. |
| `rg -n "ProcessRow|ReportRow|SettingsField|Toast|MetricCard|WebView2Spike" src tests tools build.ps1` | Exit 1, no output. No current source/test/tool/build entry references those old component names or WebView2Spike text. |
| `rg -n "Run-FakeTargetDisplayNameSmoke|Run-TargetSettingsEvidenceSmoke" src tests tools build.ps1` | Exit 1, no output. Current source/scripts do not reference old smoke helper path strings. |
| `Test-Path .\tools\evidence-smoke\Run-FakeTargetDisplayNameSmoke.ps1` | Exit 0, `True`. |
| `Test-Path .\tools\evidence-smoke\Run-TargetSettingsEvidenceSmoke.ps1` | Exit 0, `True`. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | Exit 0. Installed 110 npm packages; typecheck passed; Vitest passed `5` files / `57` tests; Vite build succeeded. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | Exit 0, `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | Exit 0, ended with `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | Exit 0, `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | Exit 0, `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| Bundled Node `.\tests\chart-sampling-tests.js` | Exit 0, `chart-sampling-tests: PASS`. |
| Bundled Node `tools\Probe-ReportHtmlLayout.js` without args | Exit 1, `Report HTML not found`; script requires `--report`. Rerun with current v3 report below is the effective layout validation. |
| Bundled Node `tools\Probe-ReportHtmlLayout.js --report ...synthetic-240-current-v3...\framescope-interactive-report.html --diagnostic ... --out ...layout-probe-v3` | Exit 0. Output JSON: `docs\test-reports\2026-05-31-framescope-post-cleanup-verification-evidence\layout-probe-v3\report-overflow-probe.json`; `allNoOverflow=true`. |
| Layout screenshot pixel check | Exit 0. Checked FPS, CPU Voltage, CPU Core VID, performance/GPU, system/GPU, IO, temperature, and process charts at `1280x720` and `900x760`; all 16 were `nonBlank=true`. CPU Voltage unique sampled colors: `735` at 1280, `566` at 900. |
| `git diff --check` | Exit 0. Only LF-to-CRLF warnings were printed; no whitespace error was reported. |
| Residual process check | Exit 0, `NO_MATCHING_RESIDUAL_PROCESSES`. |

## Evidence artifacts

- Current full report for layout probe: `docs\test-reports\2026-05-31-framescope-post-cleanup-verification-evidence\synthetic-240-current-v3\charts\framescope-interactive-report.html`
- Layout probe output: `docs\test-reports\2026-05-31-framescope-post-cleanup-verification-evidence\layout-probe-v3\report-overflow-probe.json`
- Note: `synthetic-240-current` and `synthetic-240-current-v2` remain in the evidence directory. `current` was an empty diagnostic due an initial copy wildcard mistake; `current-v2` proved layout but had CPU Voltage rejected by current Vcore rules; `current-v3` is the valid final layout evidence.

## Final assessment

Functional verification result: PASS.

Work boundary result: PARTIAL because of the accidental generated `--help/` folder outside `docs/test-reports`.

Overall result: PARTIAL.
