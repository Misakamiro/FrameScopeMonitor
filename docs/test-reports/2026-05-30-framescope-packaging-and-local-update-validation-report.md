# FrameScope Packaging And Local Update Validation Report

- Date: 2026-05-30
- Verdict: PASS
- Scope: packaging and local install/update validation only
- Source root: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
- Evidence root: `artifacts\packaging-local-update-20260530`
- Install path: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor`
- User data path: `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData`

## Boundary

This run did not perform a full simulated target matrix, did not launch any real game, did not push GitHub, and did not update a Release.

The required precondition was checked before packaging. The latest final full simulated QA rerun report is `docs\test-reports\2026-05-30-framescope-full-simulated-qa-final-rerun-report.md`, and it states `Verdict: PASS`.

## Required Answers

| Question | Result | Evidence |
|---|---:|---|
| Packaging succeeded | PASS | `build.ps1` exit code `0`; `dist\FrameScopeMonitor-Setup.exe` and `dist\FrameScopeMonitor-Full-Setup.exe` were regenerated at `2026-05-30 18:04:36`. |
| Install/update succeeded | PASS | `install.log` recorded `2026-05-30T18:05:28.6316366+08:00 install-complete`; installed key file hashes match `dist\FrameScopeMonitor-payload`. |
| User data retained | PASS | Data directory still exists. `framescope-runs` stayed `3 -> 3`; `diagnostic-reports` changed `258 -> 259` because the UI smoke generated one diagnostic report, not because data was deleted. |
| Installed minimal smoke passed | PASS | Installed live WebView2 smoke and tray smoke both returned `success=true`. |
| Residual process check | PASS | Final scan result: `NO_MATCHING_RESIDUAL_PROCESSES`. |
| Recommend real-game manual acceptance | YES | Packaging/local update is clean; real-game behavior still needs human acceptance because this run intentionally did not start games. |

## Packaging

Command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Result: PASS.

Artifacts:

| Artifact | Exists | LastWriteTime | Size | SHA256 |
|---|---:|---|---:|---|
| `dist\FrameScopeMonitor-Setup.exe` | PASS | `2026-05-30 18:04:36` | `2903552` | `C4CCB7FCB6997C8AA26BB9567BFA43E97407443B61EDBCF681746315C37E400B` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | PASS | `2026-05-30 18:04:36` | `202082816` | `81AF03D56D9BF2B4615BC2FA5C01BE628ABCDEF33F4123F7F63E6121B039DA88` |
| `dist\FrameScopeMonitor-Installer.zip` | PASS | `2026-05-30 18:04:41` | `204929469` | `9B63E978CF96B733380BC848FC657EB289F82B9A160025B755D1D2E74A0203E0` |

Primary evidence: `artifacts\packaging-local-update-20260530\01-build.log` and `02-package-artifacts.json`.

## Local Install Update

Command:

```powershell
dist\FrameScopeMonitor-Full-Setup.exe /quiet
```

The outer command wait hit the 300 second guard, but the installer itself completed. I did not rerun the installer after the timeout. Instead, I checked the installer log, process state, and installed payload hashes.

Install evidence:

- `install.log` contains `2026-05-30T18:05:28.5501263+08:00 install-start`.
- `install.log` contains `2026-05-30T18:05:28.6316366+08:00 install-complete`.
- No setup process remained after the timeout investigation.
- The installer auto-started `FrameScopeMonitor.exe`, which was used for the main-window smoke and later cleaned up.

Installed key file verification:

| Relative path | Exists | Installed time | Hash matches payload |
|---|---:|---|---:|
| `FrameScopeMonitor.exe` | PASS | `2026-05-30 18:04:34` | PASS |
| `FrameScopeSystemSampler.exe` | PASS | `2026-05-30 18:04:34` | PASS |
| `FrameScopeProcessSampler.exe` | PASS | `2026-05-30 18:04:34` | PASS |
| `FrameScopeReportGenerator.exe` | PASS | `2026-05-30 18:04:34` | PASS |
| `frontend\index.html` | PASS | `2026-05-30 16:55:44` | PASS |
| `assets\icon\framescope-icon.ico` | PASS | `2026-05-30 07:41:40` | PASS |
| `assets\icon\framescope-icon.png` | PASS | `2026-05-30 07:41:40` | PASS |

The executable timestamps moved from the pre-install `16:24:48-16:24:50` install to the new `18:04:34` payload. Static icon files did not need a new timestamp, but their hashes match the new payload.

Primary evidence: `06-post-timeout-process-check.json`, `07-installed-payload-file-comparison.json`, and `08-after-install-and-smoke-state.json`.

## User Data

Before install:

- `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData` existed.
- `framescope-runs` directory count: `3`.
- `diagnostic-reports` directory count: `258`.

After install and installed UI smoke:

- `C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData` still exists.
- `framescope-runs` directory count: `3`.
- `diagnostic-reports` directory count: `259`.

The one diagnostic count increase came from the installed UI smoke exercising diagnostics through the bridge. No user data deletion or reset was performed.

## Installed UI Smoke

The installed app was first observed as an opened Windows app window named `FrameScope Monitor Web UI`. Computer Use could list the window, but screenshot capture failed with `GetCursorPos failed: 拒绝访问。 (0x80070005)`, so no coordinate clicking was attempted.

The authoritative UI evidence is from the installed app's native WebView2 smoke entry:

```powershell
%LOCALAPPDATA%\FrameScopeMonitor\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\packaging-local-update-20260530\smoke\installed-live-minimal-smoke.json --web-ui-screenshot artifacts\packaging-local-update-20260530\smoke\installed-live-minimal-smoke.png --web-ui-timeout-ms 70000
```

Result: PASS.

| Check | Result | Evidence |
|---|---:|---|
| Main window opens | PASS | Installed process/window existed; smoke reported `pageLoaded=true`, `pageReady=true`. |
| Installed React frontend is used | PASS | `frontendPath=C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\frontend`. |
| Overview opens | PASS | `reactOverviewLoaded=true`; screenshot `installed-live-minimal-smoke-overview.png`. |
| Reports opens | PASS | `reactReportsLoaded=true`; screenshot `installed-live-minimal-smoke-reports.png`. |
| Settings opens | PASS | `reactSettingsLoaded=true`; screenshot `installed-live-minimal-smoke-settings-clean.png`. |
| Reports actions usable | PASS | `reportLiveActionSmoke.success=true`. |
| Log directory button usable | PASS | `logsOpenDirectoryOk=true`; path injection guard also passed with `logsOpenPathRejected=true`. |
| Console/errors | PASS | `consoleCount=0`, `errorCount=0`. |

Tray smoke:

```powershell
%LOCALAPPDATA%\FrameScopeMonitor\FrameScopeMonitor.exe --web-ui-tray-smoke --web-ui-evidence artifacts\packaging-local-update-20260530\smoke\installed-tray-minimal-smoke.json --web-ui-timeout-ms 30000
```

Result: PASS.

| Tray check | Result |
|---|---:|
| Initial window visible | PASS |
| Initial tray visible | PASS |
| Hide to tray | PASS |
| Restore from tray | PASS |
| Duplicate tray icons prevented | PASS |
| Active-monitoring exit guard blocks exit | PASS |
| Exit allowed without active monitoring | PASS |

Primary evidence: `smoke\installed-live-minimal-smoke.json`, `smoke\installed-live-minimal-smoke*.png`, and `smoke\installed-tray-minimal-smoke.json`.

## Residual Processes

Before cleanup, the only FrameScope-family process was the installed app that the installer auto-started:

```text
FrameScopeMonitor.exe
"C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe"
```

That process was owned by this validation run and was stopped for cleanup. The final scan checked FrameScope helpers, PresentMon/fake PresentMon, fake target names, and real-game-like process names.

Final result:

```text
NO_MATCHING_RESIDUAL_PROCESSES
```

Primary evidence: `10-final-residual-process-check.json`.

## Final Decision

PASS.

The current source packaged successfully, Setup and Full Setup artifacts exist, the Full Setup installer updated the local installation without deleting user data, installed key files match the new payload, installed UI minimal smoke passed for main window, Settings, Reports, log-directory action, and tray lifecycle, and the final residual process scan is clean.

Recommendation: enter real-game manual acceptance next. This run intentionally did not start any real game, so real-game acceptance should still cover actual game launch, real PresentMon/ETW behavior, anti-cheat interaction, and manual UX observation during a live session.
