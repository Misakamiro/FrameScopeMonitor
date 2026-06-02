# FrameScope Full Simulated QA Final Rerun Report

- Date: 2026-05-30
- Verdict: PASS
- Scope: final full simulated QA rerun only
- Evidence root: `artifacts\qa0530-final-rerun`
- Extra short-path WebView2 evidence: `artifacts\qf\v172733`

## Boundary Result

PASS. This rerun did not start Valorant, BF6, PUBG, CS2, Delta Force, NTE, or any other real game.

The target runs used fake target executables and fake PresentMon only. Some fake target processes intentionally used game-like process names such as `VALORANT-Win64-Shipping.exe` and `bf6.exe` so FrameScope could exercise target matching, but their executable paths were under the QA artifact/fake-target bins, not real game install directories.

No packaging, installer, install, GitHub push, or Release update was run. `build.ps1` was not run because it can emit setup artifacts. The final output created by this window is this report file plus QA artifacts/logs.

## Required Answers

| Question | Result | Evidence |
|---|---:|---|
| Overall PASS / PARTIAL / FAIL | PASS | All required lanes below passed. |
| No real game launched | PASS | Residual check found zero real-game/fake-target/FrameScope leftovers. |
| Last 3 PARTIAL items all fixed | PASS | See `Last PARTIAL Retest` section. |
| All enabled target simulated full reports | PASS | 6/6 enabled targets passed full report simulation. |
| silent no-csv / access denied | PASS | Failure branch summary `allPass=true`. |
| VID-only / removed Vcore | PASS | Tests, chart sampling, manifest/report metadata all passed. |
| UI / Settings / Targets / Reports / Tray / Theme / Logs | PASS | WebView2 live/reduced/tray and target/settings evidence smoke passed. |
| Residual process check | PASS | `NO_MATCHING_RESIDUAL_PROCESSES`. |
| Recommend separate packaging window | YES | Simulated QA is clean; packaging should be handled in a separate packaging-only window. |

## Last PARTIAL Retest

| Prior PARTIAL item | Final rerun result | Evidence |
|---|---:|---|
| Full report displayed process name instead of configured target display name | PASS | `target-display-name-summary.json`, `allPass=true`; CS2 report uses `Counter-Strike 2`, PUBG uses `PUBG: BATTLEGROUNDS`, Valorant uses `Valorant`, BF6 uses `Battlefield 6`; `htmlHasProcessName=false` for each target. |
| Report chart page overflowed horizontally at 1280x720 | PASS | `report-overflow-probe.json`, `allNoOverflow=true`, 10/10 scenarios passed. `1280x720` and `900x760` pages had `scrollWidth <= clientWidth`. |
| Missing independent screenshots for Target CRUD, edit modal, and Settings restart persistence | PASS | `target-settings-evidence-summary.json`, `success=true`; includes add/edit/delete screenshots, edit modal no per-target sampling evidence, and restart persistence screenshots. |

## Full Simulated Target Reports

Source: `artifacts\qa0530-final-rerun\target-display\target-display-name-summary.json`

| Enabled target | Simulated process | Full report | Display-name check |
|---|---|---:|---:|
| Counter-Strike 2 | `cs2.exe` | PASS | PASS |
| PUBG: BATTLEGROUNDS | `TslGame.exe` | PASS | PASS |
| Delta Force | `DeltaForceClient-Win64-Shipping.exe` | PASS | PASS |
| Neverness To Everness | `HTGame.exe` | PASS | PASS |
| Valorant | `VALORANT-Win64-Shipping.exe` | PASS | PASS |
| Battlefield 6 | `bf6.exe` | PASS | PASS |

Disabled targets such as Cyberpunk 2077, Hogwarts Legacy, and OPUS Prism Peak were not treated as required enabled-target report lanes.

## Failure Branches

Source: `artifacts\qa0530-final-rerun\failure-branches\failure-branches-summary.json`

| Branch | Result | Key checks |
|---|---:|---|
| silent no-csv | PASS | `FrameCaptureStatus=presentmon-no-csv-silent`, diagnostic report, no fake frame data. |
| access denied | PASS | `FrameCaptureStatus=presentmon-etw-access-denied`, `PresentMonEtwAccessDenied=true`, diagnostic report. |
| missing csv | PASS | `FrameCaptureStatus=no-presentmon-csv`, `PresentMonFailureCategory=missing-presentmon-csv`. |

## VID / Vcore

PASS. The rerun confirms the app does not present removed real per-core Vcore as valid telemetry, and keeps CPU Core VID separate.

Evidence:

- `tests\FrameScopeSystemSamplerCpuCoreTests.exe`, `tests\FrameScopeReportManifestTests.exe`, and related native monitor/report tests passed in the 18/18 C# test run.
- `tests\chart-sampling-tests.js` passed.
- Generated report metadata states `cpuVoltageStatus=unavailable` with reason `Real per-core Vcore telemetry has been removed; CPU Core VID is recorded separately.`
- Generated report metadata states CPU Core VID is request/target voltage and not real per-core Vcore.
- Report overflow screenshots include `cpu-core-vid-1280x720.png` and `cpu-core-vid-900x760.png`.

## UI And Bridge

Source: `artifacts\qa0530-final-rerun\webview2-final-rerun-validated-summary.json`

| Lane | Result | Evidence |
|---|---:|---|
| WebView2 live smoke | PASS | Exit code 0, `success=true`, `pageLoaded=true`, `pageReady=true`. |
| WebView2 reduced-motion smoke | PASS | Exit code 0, `success=true`, `reducedMotion=true`. |
| WebView2 tray smoke | PASS | Exit code 0, `success=true`, tray hide/show and duplicate icon prevention passed. |
| Overview / Targets / Reports / Settings / About navigation | PASS | Live and reduced smoke loaded all pages. |
| Reports open/open directory/regenerate | PASS | `reportLiveActionSmoke.success=true`. |
| Theme | PASS | Light/dark/system checks passed. |
| Settings save | PASS | Dirty/saving/saved states observed. |
| Targets bridge | PASS | `targetsGetOk=true`, `targetsSavePathRejected=true`. |
| Logs | PASS | Path injection rejected and host-resolved log directory opened. |
| Diagnostics and monitor start/stop | PASS | Diagnostics completed; monitor start/stop completed and stopped process count returned to zero. |

The earlier WebView2 attempts that failed were harness issues: empty history, deep path/timeout, or config outside app root. The final validated rerun used a short profile under the repository root and passed.

## Core Test Chain

| Check | Result | Evidence |
|---|---:|---|
| Frontend verify | PASS | `command-logs\01-run-frontend-verify.log`, exit code 0, 57/57 frontend tests passed and Vite build passed. |
| C# test rebuild | PASS | `command-logs\02-build-tests.log`, exit code 0. |
| C# tests | PASS | `command-logs\03-framescope-tests-summary.json`, 18/18 test executables passed. |
| Chart sampling tests | PASS | `command-logs\04-chart-sampling-tests.log`, exit code 0. |
| Fake target display/full reports | PASS | `command-logs\05-fake-target-display-full-report.log`, summary `allPass=true`. |
| Synthetic monitor session | PASS | `command-logs\06-pubg-synthetic-stable.log`, `frames=240`, `reportKind=full`. |
| Target/Settings evidence smoke | PASS | `command-logs\07-target-settings-evidence-smoke.log`, summary `success=true`. |
| Report overflow probe | PASS | `command-logs\08-report-overflow-probe.log`, summary `allNoOverflow=true`. |
| `git diff --check` | PASS | `command-logs\09-git-diff-check.log`, exit code 0; only baseline LF/CRLF warnings were emitted. |

## Residual Process Check

Source: `artifacts\qa0530-final-rerun\residual-process-check.json`

Result: PASS.

The check matched zero processes across FrameScope app helpers, report generator, fake PresentMon, fake targets, and real-game process names:

- `totalMatches=0`
- `qaOwnedMatches=0`
- `realGameNameMatches=0`
- `verdict=NO_MATCHING_RESIDUAL_PROCESSES`

## Worktree And Local Config

The repository already had many pre-existing modified/untracked files before this final rerun. This verification window did not commit, push, package, install, or update Release.

`framescope-config.json` is an ignored local config file. A previous smoke attempt had left it at the temporary WebView2 test default; it was restored to the pre-smoke captured hash:

- Restored hash: `8AE07B7F6ED4986F439FDC3C14C61DBDCB8220FFDF87C76D0C62AB17495A43BF`

## Packaging Recommendation

YES, this codebase is ready to move into a separate packaging window.

Do not package from this verification window. The packaging window should be a separate task with its own boundaries: package/build installer only, then run a packaging-specific validation pass against the produced artifacts.
