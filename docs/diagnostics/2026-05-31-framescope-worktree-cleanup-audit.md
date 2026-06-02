# FrameScope Worktree Cleanup Audit

Date: 2026-05-31
Workspace: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
Mode: audit only. No source deletion, no source move, no refactor, no build, no install, no game/BF6 test, no GitHub/release action.

## 1. Current Worktree Overview

Read-only commands used: `git status --short`, `git diff --stat`, `git diff --name-status`, `git ls-files`, `git ls-files -o --exclude-standard`, targeted `git status --ignored=matching`, `rg`, `rg --files`, and read-only `Get-Content` / `Get-ChildItem`.

Key status:

| Area | Result |
| --- | --- |
| Tracked files | 229 tracked files total |
| Modified tracked files | 69 |
| Untracked files | 404 via `git ls-files -o --exclude-standard` |
| Diff size | 69 files changed, 10043 insertions, 1423 deletions |
| Solution/project files | No `.sln` found. Tool projects only: `tools/WebView2Spike/WebView2Spike.csproj`, `tools/FrameScopeRenderProbe/FrameScopeRenderProbe.csproj`. Primary C# build is `build.ps1`. |
| Frontend entry | `src/frontend/package.json`, `vite.config.ts`, `tsconfig.json`, `package-lock.json`; scripts: `dev`, `build`, `typecheck`, `test`, `preview`. |
| Ignored build/runtime outputs | `dist/`, `artifacts/`, root `FrameScope*.exe`, root WebView2 DLLs, `src/frontend/node_modules/`, `src/frontend/dist/`, tool `bin/obj`, test `.exe`, local config/history/log files. |
| Non-ignored untracked binary dependencies | `BlackSharp.Core.dll`, `DiskInfoToolkit.dll`, `HidSharp.dll`, `LibreHardwareMonitorLib.dll`, `RAMSPDToolkit-NDD.dll`, `System.*.dll`; these are generated/restored hardware telemetry dependencies but are not currently ignored. |

Large directories observed:

| Path | Files | Dirs | Size |
| --- | ---: | ---: | ---: |
| `artifacts/` | 32986 | 12028 | 5147.8 MB |
| `install-backups/` | 28260 | 2684 | 1095.31 MB |
| `script-backups/` | 14689 | 1374 | 1012.51 MB |
| `dist/` | 174 | 49 | 554.07 MB |
| `docs/` | 626 | 58 | 507.43 MB |
| `docs/diagnostics/` | 191 | 26 | 473.99 MB |
| `cs2-monitor-runs/` | 130 | 13 | 255.8 MB |
| `tools/` | 3221 | 1032 | 210.11 MB |
| `src/frontend/node_modules/` | 7396 | 454 | 99.95 MB |
| `docs/test-reports/` | 335 | 22 | 32.38 MB |
| `src/frontend/dist/` | 4 | 1 | 1.68 MB |
| `smoke-temp/` | 4 | 5 | ~0 MB |

Important constraint: `dist/`, `artifacts/`, `docs/`, `tests/`, and `src/` must not be deleted as whole directories. This audit treats them as containers with individual candidates only.

## 2. File Classification Table

| Category | Paths / examples | Tracked state | Build/test/tool references | Audit classification |
| --- | --- | --- | --- | --- |
| Source code | `src/app/*.cs`, `src/core/*.cs`, `src/monitoring/*.cs`, `src/reporting/*.cs`, `src/frontend/src/**/*.tsx`, `src/frontend/src/**/*.ts` | 69 tracked modified files plus new untracked source files such as `FrameScopeAppIcon.cs`, `FrameScopeWebHostLifecycle.cs`, `FrameScopeLoggingPolicy.cs`, `FrameScopePresentMonDiagnostics.cs`, `FrameScopeSystemSampler.CpuCoreTelemetry.cs`, `useFrameScopeTheme.ts` | Strongly referenced by `build.ps1`, `tests/Build-FrameScopeTests.ps1`, frontend tests, and recent reports | Keep. Do not cleanup source until after separate no-reference proof and tests. |
| Tests | `tests/*.cs`, `tests/*.js`, `tests/Build-FrameScopeTests.ps1` | Mixed: original tracked tests modified, new test sources untracked, ignored generated `.exe` outputs | `tests/Build-FrameScopeTests.ps1` compiles current C# test suite; `chart-sampling-tests.js` validates report chart code | Keep source tests. Clean generated `.exe` only in cleanup window. |
| Tools | `tools/PresentMon-2.4.1-x64.exe`, `tools/FrameScopePubgSimulator/`, `tools/FrameScopeRenderProbe/`, `tools/WebView2Spike/`, `tools/Probe-ReportHtmlLayout.js`, smoke scripts | Mixed: core tools tracked, new smoke/probe scripts untracked, bin/obj/cache ignored | PresentMon is shipped; simulator and RenderProbe are referenced by tests/docs; `Probe-ReportHtmlLayout.js` is recent report evidence; smoke scripts are evidence generators | Keep referenced tools. Treat old spike/smoke helpers as manual-decision candidates, not immediate delete. |
| Docs | `docs/implementation-reports/`, `docs/test-reports/`, `docs/diagnostics/`, `docs/design/`, `docs/design-reviews/` | Many untracked reports and evidence directories | Recent reports prove FPS, GamePP charts, CPU Voltage / Vcore, CPU Core VID, WebView2 smoke, and full simulated QA | Keep recent evidence chain. Older reports can be archived only after mapping which implementation/test report they support. |
| Artifacts | `artifacts/` dated screenshots, QA runs, generated reports, release/test evidence | Ignored | Referenced by historical reports; many entries are old dated visual/build artifacts | Do not delete entire directory. Archive or remove only low-risk old run folders after evidence mapping. |
| Dist/build outputs | `dist/FrameScopeMonitor-Setup.exe`, `dist/FrameScopeMonitor-Full-Setup.exe`, `dist/FrameScopeMonitor-Installer.zip`, `dist/FrameScopeMonitor-payload/`, root `FrameScope*.exe`, root WebView2 DLLs | Ignored | Created by `build.ps1`; current release/setup artifacts may be needed for manual validation | Build outputs. Do not commit. Clean selected old test subdirs first; preserve current setup/full setup until release decision. |
| Frontend generated files | `src/frontend/node_modules/`, `src/frontend/dist/`, possible `.vite/coverage` | Ignored | `tools/Run-Frontend.ps1 verify/build` regenerates these; `build.ps1` requires `src/frontend/dist/index.html` when packaging | Can clean after knowing the next step will run frontend install/build. Do not commit. |
| Temporary smoke/profile files | `smoke-temp/`, `--run/`, root `framescope-config.json`, `framescope-history.jsonl`, `framescope-watcher.log`, `artifacts.error.txt`, `backups/`, `install-backups/`, `script-backups/`, `cs2-monitor-runs/` | Ignored | Local profiles/logs/backups; not source. Some may be local validation evidence. | Low-risk cleanup candidates after copying needed evidence into `docs/test-reports` or `artifacts`. Never touch `%LOCALAPPDATA%\FrameScopeMonitorData`. |

## 3. Old Code Candidate Table

Risk legend: Low = likely safe after normal verify; Medium = needs targeted test or small follow-up; High = user/manual decision before removal.

| Path | Symbol / file | Why it looks old | `rg` reference result | Build/test/tool/report reference | Recommended action | Risk |
| --- | --- | --- | --- | --- | --- | --- |
| `src/frontend/src/components/ProcessRow.tsx` | `ProcessRow` | Runtime pages no longer import it after the current redesigned React pages render rows inline. | `rg -n "ProcessRow|process-row" src/frontend/src` found the component file and CSS selectors; no `import { ProcessRow }` in app/pages/tests. | No `package.json` script or build entry directly references the file; CSS selectors remain in `components.css`. | `delete` in a cleanup branch together with its unused CSS selectors, then run frontend typecheck/tests/build. | Low |
| `src/frontend/src/components/ReportRow.tsx` | `ReportRow` | Reports page now uses local row markup and `report-row-actions`; this component is not imported. | `rg -n "ReportRow|report-row" src/frontend/src` found the component file and old `.report-row` CSS; runtime `ReportsPage.tsx` uses `report-row-actions`, not `ReportRow`. | No frontend runtime import. | `delete` with unused `.report-row*` CSS after frontend verification. | Low |
| `src/frontend/src/components/SettingsField.tsx` | `SettingsField` | Settings page uses page-local controls; this shared field component is not imported. | `rg -n "SettingsField|settings-field" src/frontend/src` found only component and CSS selectors. | No frontend runtime import. | `delete` with unused `.settings-field*` CSS after frontend verification. | Low |
| `src/frontend/src/components/Toast.tsx` | `Toast` | Looks like a static preview component; current app status feedback uses inline page/state components. | `rg -n "Toast|toast" src/frontend/src` found component file, CSS selectors, and design tokens; no runtime import. | No frontend runtime import. | `delete` only after checking token/CSS references are not reused by future toast work. | Medium |
| `src/frontend/src/components/MetricCard.tsx` | `MetricCard` | Runtime pages do not import it; likely from an older card-heavy dashboard layout. | `rg -n "MetricCard|metric-card" src/frontend/src` found `uiInteractionContract.test.ts` raw import, component file, and CSS selectors. | Still referenced by frontend contract test as raw source, so deletion would break tests unless test intent is updated. | `needs manual decision`; either keep as contract fixture or remove with test rewrite. | Medium |
| `src/frontend/src/components/components.css` | Old `.metric-card`, `.process-row`, `.report-row`, `.settings-field`, `.toast-preview` selector blocks | CSS contains selectors for components that are not imported by current runtime pages. | `rg` found these selector names only in `components.css` plus matching unused component files, except `report-row-actions` in `pages.css` is separate. | CSS is imported globally, so unused selectors do not break runtime but add dead CSS. | `delete` selectors only together with corresponding component cleanup, after visual/frontend tests. | Low |
| `tools/WebView2Spike/` | `WebView2SpikeForm`, spike `.csproj` | It is a WebView2 proof-of-concept from the redesign stage; current app owns WebView2 in `src/app/FrameScopeNativeMonitor.WebHost.cs`. | `rg -n "WebView2Spike"` found self references, historical plan docs, and one `build.ps1` error message telling users to restore the package via this csproj. | Not compiled by `build.ps1` or `tests/Build-FrameScopeTests.ps1`; but `build.ps1` still references the path in fallback instructions. | `needs manual decision`; archive/delete only after replacing the `build.ps1` restore instruction or confirming it remains the intended package restore helper. | Medium |
| Root `GameLite*.ps1/.cmd` wrappers and `Invoke-GameLiteSGuardThrottle.ps1` | GameLite compatibility wrappers | They are not FrameScope monitor source, and GameLite was split to sibling project. | `rg -n "GameLite|SGuard"` found AGENTS/docs explicitly documenting the wrappers and `tests/lightweight-separation-tests.ps1` validating them. | Not referenced by FrameScope build/tests, but docs say old WMI consumers/manual shortcuts may still use these wrappers. | `keep`; do not delete without explicit user decision and WMI/manual compatibility migration. | High |
| `tests/lightweight-separation-tests.ps1` | GameLite boundary test | It validates a separated project and is not part of current `tests/Build-FrameScopeTests.ps1`. | `rg -n "lightweight-separation|GameLite"` shows the test checks sibling `..\gamelite-auto-lightweight` and root wrappers. | Not part of C# build/test entry; still documents compatibility boundary. | `needs manual decision`; keep unless GameLite compatibility wrappers are retired. | Medium |
| `tools/Run-FakeTargetDisplayNameSmoke.ps1` | Installed smoke evidence generator | Untracked script writes temp source, fake exes, runs installed app; it is not a normal source/build tool. | `rg -n "Run-FakeTargetDisplayNameSmoke"` only found evidence status references and the script itself. | No build/test reference. It may support `2026-05-30` installed QA evidence. | `move` to a dedicated evidence-tools folder or keep untracked until QA evidence is finalized; do not delete before evidence is archived. | Medium |
| `tools/Run-TargetSettingsEvidenceSmoke.ps1` | Installed WebView2 settings smoke generator | Untracked script writes `smoke-temp` config and screenshots/evidence. | `rg -n "Run-TargetSettingsEvidenceSmoke"` found evidence status references and the script itself. | No build/test reference, but related to WebView2 full simulated QA. | `move`/archive as an evidence helper, or delete only after docs/test evidence no longer depends on rerun. | Medium |
| `tools/Probe-ReportHtmlLayout.js` | Report layout probe | Untracked but actively used by the latest GamePP, Vcore, VID, and full simulated QA reports. | `rg -n "Probe-ReportHtmlLayout"` found latest `2026-05-31` test reports and implementation reports using it. | Not in build/test entry, but current QA evidence depends on it. | `keep`; consider tracking or moving into a formal test/probe area later. | Low |
| `tools/Generate-FrameScopeIcon.ps1` | Icon generator | Untracked tool, but `build.ps1` calls it if icon assets are missing. | `rg -n "Generate-FrameScopeIcon"` found `build.ps1:30` and evidence status logs. | Referenced by `build.ps1`; generated icons live under `assets/icon/`. | `keep`; not a cleanup candidate. | Low |
| `docs/*` references to old `src/ui/*`, `FrameScopeUiState.cs`, `FrameScopeLiveData.cs`, `FrameScopeReportPage*.cs` | Stale WinForms UI docs | Current tree has no `src/ui` files; React/WebView2 frontend is under `src/frontend`. | `rg --files src tests tools | rg "src/ui|FrameScopeUiState"` found no source/test files; `rg -n "FrameScopeUiState|src\\ui"` found many docs references only. | Not build/test code. The ignored `tests/FrameScopeUiStateTests.exe` remains as old output. | `needs manual decision`; update docs in a documentation cleanup pass, not source deletion. | Medium |
| `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs` old FPS anomaly/spike marker paths | `drawSpikeMarkers`, `fpsAnomalyPoints`, `drawFpsAnomalyMarkers`, `DATA.fps.min` | User asked to check old chart paths. | `rg -n "drawSpikeMarkers|fpsAnomalyPoints|drawFpsAnomalyMarkers|DATA\\.fps\\.min" src/reporting tests/chart-sampling-tests.js` found only negative assertions in `tests/chart-sampling-tests.js`, not production code. | Current report chart path is centralized in `ReportHtmlScripts`; tests assert old paths are absent. | `keep`; no production old chart code found to delete here. | Low |
| CPU Voltage / Vcore and CPU Core VID telemetry code | `FrameScopeSystemSampler.CpuCoreTelemetry.cs`, `.PerfCounters.cs`, report metadata/system/chart code | Looks recently changed, but is part of current critical chain. | `rg -n "CpuVoltage|CpuVid|Vcore|VoltageProvider"` found active references across `src/app`, `src/monitoring`, `src/reporting`, frontend contracts, and tests. | Strong build/test/report references: `build.ps1`, `tests/Build-FrameScopeTests.ps1`, `FrameScopeSystemSamplerCpuCoreTests`, `FrameScopeNativeWatcherPolicyTests`, `FrameScopeReportManifestTests`, `chart-sampling-tests.js`. | `keep`; not old code. | High |

## 4. Cleanable Artifact Candidate Table

| Path | Tracked / untracked / ignored | Regenerable? | Evidence chain required? | Recommended action |
| --- | --- | --- | --- | --- |
| `tests/*.exe` | Ignored | Yes, via `tests/Build-FrameScopeTests.ps1` | No source evidence; test logs live in docs/artifacts | `delete` first in cleanup window. Keep `.cs` and `.js` tests. |
| `tests/FrameScopeUiStateTests.exe` | Ignored | Not from current source; orphaned old binary | No current source file. Historical docs mention it. | `delete` as old generated binary after confirming no one launches it manually. |
| `src/frontend/node_modules/` | Ignored | Yes, via `tools/Run-Frontend.ps1 install/verify` or `npm ci` | No. | `delete` when next cleanup window allows reinstall/rebuild. |
| `src/frontend/dist/` | Ignored | Yes, via frontend build | Needed only for packaging input if building immediately | `delete` only if next step will regenerate before packaging. |
| `tools/.cache/` | Ignored | Yes, tool/frontend bootstrap cache | No. | `delete` in cleanup window. |
| `tools/**/bin/`, `tools/**/obj/` | Ignored | Yes, via `dotnet build` for tool projects | No. | `delete` in cleanup window. |
| `smoke-temp/` | Ignored | Yes, local smoke scripts recreate temp configs | No, unless a smoke was just being diagnosed | `delete` after confirming no active smoke run. |
| `--run/` | Ignored | Yes / local temp | No. | `delete` after checking no active process uses it. |
| Root `FrameScope*.exe` | Ignored | Yes, via `build.ps1` | Build outputs, not docs evidence | `delete` only after preserving/confirming current local test needs; do not remove installed app paths. |
| Root WebView2 DLLs | Ignored | Yes, copied by `build.ps1` from NuGet package | Needed by local root executable | `delete` with root exe cleanup only when not running root build outputs. |
| Root hardware dependency DLLs: `LibreHardwareMonitorLib.dll`, `HidSharp.dll`, `BlackSharp.Core.dll`, `DiskInfoToolkit.dll`, `RAMSPDToolkit-NDD.dll`, `System.*.dll` | Untracked, not ignored | Yes, restored/copied by `build.ps1` `Restore-HardwareTelemetryDependencies` | Needed by local root app for built-in voltage provider; also listed in recent diagnostics | `needs manual decision`; either add ignore rule in a future change or clean only after proving build restores them. |
| `dist/FrameScopeMonitor-Setup.exe`, `dist/FrameScopeMonitor-Full-Setup.exe`, `dist/FrameScopeMonitor-Installer.zip` | Ignored | Yes, via `build.ps1` after frontend build and WebView2 installer availability | May be current packaging evidence | `keep` until release/package decision; do not clean first. |
| Old `dist/*test*`, `dist/gamelite-*`, `dist/sampler-*`, `dist/runtime-check-*`, `dist/cleanup-test-*` subdirectories | Ignored | Mostly yes / historical test outputs | Not the latest required evidence | `delete` selected old subdirs only, not all `dist/`. |
| `packaging/MicrosoftEdgeWebView2RuntimeInstallerX64.exe` | Ignored | Yes, downloadable by `build.ps1` if missing | Needed to build full offline setup without redownload | `keep` unless storage cleanup is more important than avoiding redownload. |
| `artifacts/` older dated visual/test folders | Ignored | Usually yes, but some evidence is historical-only | Mixed. Recent GamePP/Vcore/VID/full QA evidence must remain. | `move` or archive old subfolders after mapping report references; never delete entire `artifacts/`. |
| `docs/diagnostics/artifacts/` | Ignored nested artifact cache | Likely regenerable | Unknown; must inspect before deletion | `needs manual decision`; archive or map references first. |
| `install-backups/`, `script-backups/`, `backups/` | Ignored | Usually backup-only | Not report evidence unless referenced manually | `move`/archive/delete after confirming no rollback need. |
| `framescope-config.json`, `framescope-history.jsonl`, `framescope-watcher.log`, `game-lite-watcher.log`, `artifacts.error.txt`, `修改报告.txt` | Ignored local runtime/log files | Yes / local runtime creates them | May contain local debugging context only | `delete` or archive after user confirms local runtime logs are no longer needed. |
| `docs/test-reports/2026-05-31-*evidence/` | Untracked docs evidence | Not cheaply regenerable without rerunning full QA | Yes, required evidence chain | `keep`. |
| `docs/test-reports/gamepp-retest-0531/` | Untracked docs evidence | Regenerable only by rerunning report probes | Yes, required GamePP chart evidence | `keep`. |

## 5. Must Keep List

Recent implementation and retest reports:

| Evidence area | Keep paths |
| --- | --- |
| FPS GamePP evidence | `docs/implementation-reports/2026-05-30-framescope-fps-chart-gamepp-style-report.md`; `docs/test-reports/2026-05-30-framescope-fps-chart-gamepp-style-retest-report.md`; `docs/test-reports/gamepp-retest-0531/` |
| Full report GamePP evidence | `docs/implementation-reports/2026-05-30-framescope-all-report-charts-gamepp-style-report.md`; `docs/test-reports/2026-05-31-framescope-all-report-charts-gamepp-style-retest.md`; `docs/test-reports/gamepp-retest-0531/screenshots/synthetic-report-probe/` |
| CPU Voltage / Vcore evidence | `docs/implementation-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-report.md`; `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest.md`; `docs/test-reports/2026-05-31-framescope-cpu-voltage-gamepp-alignment-retest-evidence/` |
| CPU Core VID evidence | `docs/implementation-reports/2026-05-31-framescope-cpu-core-vid-recording-chart-report.md`; `docs/test-reports/2026-05-31-framescope-cpu-core-vid-recording-chart-retest.md`; `docs/test-reports/2026-05-31-framescope-cpu-core-vid-recording-chart-retest-evidence/` |
| Full simulated QA evidence | `docs/test-reports/2026-05-31-framescope-full-simulated-qa.md`; `docs/test-reports/2026-05-31-framescope-full-simulated-qa-evidence/`; `docs/test-reports/2026-05-30-framescope-full-simulated-qa-final-rerun-report.md`; `docs/test-reports/2026-05-30-framescope-full-installed-simulated-qa-report.md` |
| Packaging/local update evidence | `docs/test-reports/2026-05-30-framescope-packaging-and-local-update-validation-report.md`; `docs/test-reports/2026-05-30-framescope-formal-icon-local-install-validation-report.md`; `docs/implementation-reports/2026-05-23-framescope-local-install-update-report.md`; `docs/implementation-reports/2026-05-23-framescope-webview2-final-packaging-validation-report.md` |
| Layout/smoke probe tool evidence | `tools/Probe-ReportHtmlLayout.js` plus latest logs under `docs/test-reports/*evidence/` and `docs/test-reports/gamepp-retest-0531/verification/` |

Critical source chains that must stay intact:

| Chain | Active paths |
| --- | --- |
| FPS / PresentMon | `tools/PresentMon-2.4.1-x64.exe`; `src/core/FrameScopeCapturePlanner.cs`; `src/core/FrameScopePresentMonDiagnostics.cs`; `src/app/FrameScopeNativeMonitor.MonitorSession.PresentMon.cs`; `src/reporting/FrameScopeReportGenerator.PresentMon.cs`; `src/reporting/FrameScopeReportGenerator.Analysis.cs`; `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`; `tests/chart-sampling-tests.js`; `tests/FrameScopePresentMonDiagnosticsTests.cs`; `tests/FrameScopeNativeMonitorChildProcessTests.cs` |
| CPU Voltage / Vcore | `src/monitoring/FrameScopeSystemSampler.CpuCoreTelemetry.cs`; `src/monitoring/FrameScopeSystemSampler.PerfCounters.cs`; `src/app/FrameScopeNativeMonitor.Watcher.cs`; `src/app/FrameScopeNativeMonitor.MonitorSession.Paths.cs`; `src/reporting/FrameScopeReportGenerator.Metadata.cs`; `src/reporting/FrameScopeReportGenerator.SystemData.cs`; `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`; `tests/FrameScopeSystemSamplerCpuCoreTests.cs`; `tests/FrameScopeNativeWatcherPolicyTests.cs`; `tests/FrameScopeReportManifestTests.cs`; `tests/chart-sampling-tests.js` |
| CPU Core VID | Same telemetry/report paths as Vcore plus `cpu-vid-samples.csv` / `cpu-vid-telemetry-status.json` handling in `MonitorSession.Paths`, `ReportGenerator.Metadata`, `ReportGenerator.SystemData`, and `ReportHtmlScripts`; tests prove VID stays separate from Vcore. |
| WebView2 smoke/full QA | `src/app/FrameScopeNativeMonitor.WebHost.cs`; `src/app/FrameScopeWebHostLifecycle.cs`; `src/app/FrameScopeWebBridge*.cs`; `src/frontend/src/**/*`; `tools/Run-TargetSettingsEvidenceSmoke.ps1`; `docs/test-reports/2026-05-31-framescope-full-simulated-qa-evidence/` |

## 6. Risk Checks

| Check | Result |
| --- | --- |
| `build.ps1` source references | New/untracked current C# files are referenced: `FrameScopeLoggingPolicy.cs`, `FrameScopePresentMonDiagnostics.cs`, `FrameScopeWebHostLifecycle.cs`, `FrameScopeAppIcon.cs`, `FrameScopeSystemSampler.CpuCoreTelemetry.cs`. Do not delete. |
| `tests/Build-FrameScopeTests.ps1` references | Current new tests and source files are included. It no longer builds old `FrameScopeUiStateTests.exe`. |
| csproj/sln references | No `.sln`. Tool csproj files are `FrameScopeRenderProbe` and `WebView2Spike`; RenderProbe remains used by documented verification. WebView2Spike is only a restore/spike helper. |
| frontend references | Current runtime imports `useFrameScopeTheme`, `GlassCard`, `ChartShell`, `InlineStatus`, `StatusPill`, etc. Unused component candidates are not imported by pages. |
| report chart references | `ReportHtmlScripts` is the active single report chart path. Old red anomaly/spike marker paths were not found in production; tests assert absence. |
| WebView2 smoke references | Web host and lifecycle code are active. `Run-TargetSettingsEvidenceSmoke.ps1` is not build entry but is related to recent full QA evidence. |
| FPS chain impact | No FPS source deletion recommended. Clean only generated outputs, not PresentMon or report generator source. |
| CPU Voltage / Vcore impact | Hardware telemetry DLLs are generated/untracked but required by local root app. Clean only after proving build regenerates and local app is not being used. |
| CPU Core VID impact | VID source/report/tests are active and must be kept. |

## 7. Recommended Cleanup Execution Window

Do this in a separate cleanup branch/window, not in this audit turn:

1. Snapshot current status: `git status --short --ignored=matching` and save a cleanup implementation report path.
2. Remove only low-risk generated outputs first:
   - `tests/*.exe`
   - `src/frontend/node_modules/`
   - `src/frontend/dist/`
   - `tools/.cache/`
   - `tools/**/bin/`, `tools/**/obj/`
   - `smoke-temp/`
   - `--run/`
3. Then clean selected old build/test subdirs inside `dist/`, not the whole `dist/` directory.
4. Archive or delete old `artifacts/` subfolders only after mapping each to a report. Keep all 2026-05-30/2026-05-31 GamePP, Vcore, VID, and full QA evidence.
5. Handle root build outputs:
   - root `FrameScope*.exe`, WebView2 DLLs, and hardware telemetry DLLs can be cleaned only when no root local executable is needed and build regeneration is permitted.
   - Consider a future `.gitignore` update for restored hardware telemetry DLLs.
6. Review old frontend components and CSS:
   - remove `ProcessRow`, `ReportRow`, `SettingsField`, and their CSS together;
   - decide whether `MetricCard` remains a contract fixture;
   - run frontend typecheck/tests/build afterward.
7. Review `tools/WebView2Spike/`:
   - either keep as WebView2 package restore helper or replace the `build.ps1` fallback instruction before archiving it.
8. Review stale docs mentioning `src/ui/*` and `FrameScopeUiStateTests.exe`; update docs after code cleanup so future prompts do not point at removed WinForms UI files.
9. Run verification after cleanup:
   - frontend verify;
   - `build.ps1`;
   - `tests/Build-FrameScopeTests.ps1`;
   - relevant generated test exes;
   - `node tests/chart-sampling-tests.js`;
   - report layout probe;
   - WebView2 smoke if UI/profile files changed.
10. Write a cleanup implementation report documenting exactly what was removed and why.

Suggested first 10 low-risk cleanup items:

| Order | Candidate | Why first |
| ---: | --- | --- |
| 1 | `tests/*.exe` | Ignored generated test binaries; sources remain. |
| 2 | `src/frontend/node_modules/` | Ignored dependency install output; regenerable. |
| 3 | `src/frontend/dist/` | Ignored frontend build output; regenerable before packaging. |
| 4 | `tools/.cache/` | Ignored bootstrap/cache output. |
| 5 | `tools/FrameScopeRenderProbe/bin/` and `obj/` | Ignored tool build output; source/project remain. |
| 6 | `tools/WebView2Spike/bin/` and `obj/` | Ignored tool build output; source/project remain. |
| 7 | `tools/FrameScopePubgSimulator/bin/` | Ignored simulator build output; source remains. |
| 8 | `smoke-temp/` | Local temp smoke profiles; not user data. |
| 9 | `--run/` | Local temp run output. |
| 10 | selected old `dist/*test*`, `dist/gamelite-*`, `dist/sampler-*`, `dist/runtime-check-*` subdirs | Old ignored test/build outputs; do not remove setup/full setup yet. |

## 8. Final Conclusion

Conclusion: **PARTIAL**.

The cleanup scheme is complete for low-risk generated outputs and clear dead frontend component candidates. It remains partial because several high/medium-risk items require manual decisions before any deletion:

1. Root GameLite compatibility wrappers and `tests/lightweight-separation-tests.ps1` are old relative to FrameScope, but documented as compatibility bridges for legacy WMI/manual entry. Highest-risk cleanup candidate.
2. `tools/WebView2Spike/` is a historical spike but still named by `build.ps1` as the restore fallback for WebView2 package availability.
3. Untracked hardware telemetry DLLs are build-restored dependencies and needed by local root executables for CPU Voltage / Vcore and CPU Core VID validation; they should be ignored or regenerated deliberately, not casually deleted.
4. Stale docs pointing to `src/ui/*` should be updated in a documentation cleanup pass, but they are not source-code deletion candidates.

No source deletion is recommended in this audit turn. The next safe action is a generated-output cleanup window, followed by tests and a cleanup implementation report.
