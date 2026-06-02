# FrameScope local install update validation after optimization

Date: 2026-06-03 00:35 Asia/Hong_Kong

Final result: PASS

Evidence root: `docs\test-reports\2026-06-03-framescope-local-install-update-validation-after-optimization-evidence\`

## Scope

Validated the installed local FrameScope build after running:

`dist\FrameScopeMonitor-Full-Setup.exe /quiet`

The installer SHA256 was confirmed from the prior build-artifact sync context:

`4C07D4A3EEB8BE2A9C78BADD11B209C1BC34C13263B52F989A26CA864F50B107`

Hard constraints followed:

- Preserved `%LOCALAPPDATA%\FrameScopeMonitorData`.
- Did not delete historical `runs`, `reports`, `config`, or `log`.
- Did not clear user data.
- Did not start a real game.
- Did not test BF6.
- Did not push GitHub.
- Did not update Release.
- Installer auto-started one installed app process; only that run-started installed app instance was closed after validation.

## Install update result

| Item | Result | Evidence |
| --- | --- | --- |
| Pre-install install dir | Exists: `%LOCALAPPDATA%\FrameScopeMonitor` | `00-pre-install-state.json` |
| Pre-install data dir | Exists: `%LOCALAPPDATA%\FrameScopeMonitorData` | `00-pre-install-state.json` |
| Pre-install matching processes | `0` | `00-pre-install-state.json` |
| Full Setup `/quiet` | PASS, exit code `0`, elapsed `1070 ms` | `01-full-setup-quiet-run.json` |
| Installer auto-started app | PID `54968`, `%LOCALAPPDATA%\FrameScopeMonitor\FrameScopeMonitor.exe` | `02-install-log-and-post-install-state.json` |
| install.log success | PASS, latest `2026-06-03T00:13:37.2913973+08:00 install-complete` | `02-install-log-and-post-install-state.json` |
| WebView2 runtime install log | Available, version `148.0.3967.70`, `fullPackage=True` | `02-install-log-and-post-install-state.json` |
| Post-install data dir | Still exists: `%LOCALAPPDATA%\FrameScopeMonitorData` | `02-install-log-and-post-install-state.json` |

Pre-update key installed hashes showed the machine had the older 2026-05-30 install. After update, the installed payload key hashes moved to the 2026-06-02 build. Examples:

| File | Before SHA256 | After SHA256 |
| --- | --- | --- |
| `FrameScopeMonitor.exe` | `AB517AE1997C7A1469E7ABC79CA1AC4BAB5417003721975DF31EB50A75D7DE36` | `7D0778FEACEB6CDCA0C994966420FE13644BF23BBF432BE45CA7199CD4628858` |
| `FrameScopeReportGenerator.exe` | `0C4E944E68442D8FA3D0F4E4D0B01A324EA702D821655B876605A0ED3C925655` | `3D0C8E0273B65FDB939198C4D241851E43F4F437A2897E884689BD74B3E11260` |
| `FrameScopeProcessSampler.exe` | `F534AEB60962F33D88DC3BDE05BDD5E36F63DFBAC289D1032750ECB851626E79` | `6B0B8870EF22D13EC946029AD3913772947FC9BF340F854DE2568F88CAA25A76` |
| `FrameScopeSystemSampler.exe` | `84FA378D291042E5179FAD1D721ACA80823A6B7029BA81BF8B9DA2195951600D` | `AA4D38096BC22D3EC730D63314310A614130223954B16F2FCD67A459D77D5C5C` |
| `frontend\index.html` | `4126B99F66F125CCB4A2581432BB64DB73CBEF0D5F83AC6F5023D9AEC7362030` | `83D2BB678AF2A5F6361C537657FA6898C454746DB4660E657B1AB1646302031F` |

## Installed file parity

| Item | Result | Evidence |
| --- | --- | --- |
| `%LOCALAPPDATA%\FrameScopeMonitor\FrameScopeMonitor.exe` | Exists | `03-payload-vs-installed-sha256-parity.json` |
| Key payload files | Exists: monitor, report generator, process sampler, system sampler, frontend, WebView2 DLLs, hardware DLL | `03-payload-vs-installed-sha256-parity.json` |
| Source payload files | `30` | `03-payload-vs-installed-sha256-parity.json` |
| Installed files excluding `install.log` | `54` | `03-payload-vs-installed-sha256-parity.json` |
| Payload-vs-installed mismatch count | PASS, `0` | `03-payload-vs-installed-sha256-parity.json` |
| Missing count | PASS, `0` | `03-payload-vs-installed-sha256-parity.json` |
| Hash mismatch count | PASS, `0` | `03-payload-vs-installed-sha256-parity.json` |

Note: parity was captured before later installed smoke files were generated under `%LOCALAPPDATA%\FrameScopeMonitor\smoke-temp\li0603`. The parity assertion is that every source payload file matched the installed copy; runtime logs and smoke artifacts were excluded or ignored.

## Installed smoke results

All installed smoke used synthetic fixtures under `%LOCALAPPDATA%\FrameScopeMonitor\smoke-temp\li0603`; it did not use or clear `%LOCALAPPDATA%\FrameScopeMonitorData`.

| Smoke | Result | Key assertions | Evidence |
| --- | --- | --- | --- |
| Installed WebView2 live | PASS, exit `0` | `success=True`, `pageLoaded=True`, `pageReady=True`, `reducedMotion=False`, `processRefreshObserved=True`, report live actions PASS, theme smoke PASS, bridge extension smoke PASS | `%LOCALAPPDATA%\FrameScopeMonitor\smoke-temp\li0603\web\live-final-run.json` |
| Installed WebView2 reduced motion | PASS, exit `0` | `success=True`, `pageLoaded=True`, `pageReady=True`, `reducedMotion=True`, `processRefreshObserved=True`, report live actions PASS, theme smoke PASS, bridge extension smoke PASS | `%LOCALAPPDATA%\FrameScopeMonitor\smoke-temp\li0603\web\reduced-final-run.json` |
| Target/settings evidence | PASS, exit `0` | add PASS, edit PASS, delete PASS, settings save PASS, `finalTargetCount=0`, `finalTelemetrySampleIntervalMs=1375` | `%LOCALAPPDATA%\FrameScopeMonitor\smoke-temp\li0603\web\target-settings-run.json` |
| Settings persistence read | PASS, exit `0` | read back `actualTelemetrySampleIntervalMs="1375"` | `%LOCALAPPDATA%\FrameScopeMonitor\smoke-temp\li0603\web\settings-read-run.json` |
| Installed report resource smoke | PASS | installed `FrameScopeReportGenerator.exe` generated HTML/data/manifest from synthetic run | `%LOCALAPPDATA%\FrameScopeMonitor\smoke-temp\li0603\report-resource-smoke-checks.json` |

Report resource details:

| Assertion | Result |
| --- | --- |
| HTML exists | PASS, `53191` bytes |
| `framescope-interactive-data.js` exists | PASS, `4699` bytes |
| manifest exists | PASS, `3015` bytes |
| data contains `window.FRAMESCOPE_DATA` | PASS |
| `bucketMs=1000` | PASS |
| `DATA.cpuVoltage` exists | PASS |
| `DATA.cpuVid` exists | PASS |
| HTML references `DATA.cpuVoltage` | PASS |
| HTML references `DATA.cpuVid` | PASS |
| manifest `reportKind` | `full` |
| manifest `frames` | `20` |
| manifest CPU voltage availability | PASS |
| manifest CPU VID availability | PASS |

## Performance optimization regression checks

| Area | Result | Evidence |
| --- | --- | --- |
| P0 report resource smoke | PASS, no report-resource regression | `%LOCALAPPDATA%\FrameScopeMonitor\smoke-temp\li0603\report-resource-smoke-checks.json` |
| Large process list windowing | PASS, 250-row list initially rendered `19/250`, windowed `true`; after scroll rendered `10/250`, first `FixtureProcess-241.exe`, last `FixtureProcess-250.exe`; filtered list `51/51` | `04-frontend-performance-summary.json` |
| UI motion static scan | PASS, `framerMotionImports=0`, `motionElements=0`, `whileTap=0`, `motionConfig=0`, `transitionAll=0`, `boxShadowTransitions=0`, `filterDeclarations=0`, `backdropFilterDeclarations=0`, `blurReferences=0`, `prefersReducedMotionBlocks=3` | `04-frontend-performance-summary.json` |
| UI motion reduced runtime | PASS, reduced max `animated=0`, `infiniteAnimated=0`, `transitionAll=0`, `boxShadowTransitioned=0`, `filterActive=0` | `04-frontend-performance-summary.json` |
| Logging rate limiter / tail trim | PASS | `FrameScopeLoggingPolicyTests.exe`, `05-required-tests-summary.json` |
| Data root scan guard | PASS | `FrameScopeDiagnosticsTests.exe`, `FrameScopeWebBridgeTests.exe`, and extra direct `FrameScopeReportProgressTests.exe`, `05-required-tests-summary.json` |
| P1/P2 bridge/report guard behavior | PASS | installed WebView2 bridge extension smoke plus `FrameScopeWebBridgeTests.exe` |

## Required command results

| Command | Result |
| --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS, exit `0`, `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS, exit `0` |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS, exit `0` |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS, exit `0` |
| `.\tests\FrameScopeLoggingPolicyTests.exe` | PASS, exit `0` |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS, exit `0` |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS, exit `0` |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS, exit `0` |
| `git diff --check` | PASS, exit `0`; emitted LF-to-CRLF working-copy warnings only, no whitespace-error failure |
| extra `.\tests\FrameScopeReportProgressTests.exe` | PASS, exit `0`; added to directly cover data-root scan guard |

Command logs: `docs\test-reports\2026-06-03-framescope-local-install-update-validation-after-optimization-evidence\command-logs\`

## Residual process check

Installer auto-started PID `54968` from `%LOCALAPPDATA%\FrameScopeMonitor\FrameScopeMonitor.exe`.

Cleanup result:

- `CloseMainWindow()` returned `True`.
- No force kill was needed for PID `54968`.
- Final process match policy: FrameScope executables plus `msedgewebview2.exe` command lines containing `FrameScopeMonitor`.
- Final match count: `0`.
- Final status: `NO_MATCHING_RESIDUAL_PROCESSES`.

Evidence:

- Cleanup process: `06-residual-process-check.json`
- Final clean check: `07-final-residual-process-check.json`

## Explicit non-actions

| Item | Result |
| --- | --- |
| Started real game | No |
| Tested BF6 | No |
| Pushed GitHub | No |
| Updated Release | No |
| Deleted `%LOCALAPPDATA%\FrameScopeMonitorData` | No |
| Cleared historical runs/reports/config/log | No |

## Verdict

PASS. Full Setup `/quiet` updated the local installed FrameScope successfully, install.log recorded `install-complete`, `%LOCALAPPDATA%\FrameScopeMonitorData` was preserved, installed payload parity matched the payload with `0` mismatches, installed WebView2 live/reduced smoke passed, target add/edit/delete and final `finalTargetCount=0` passed, Settings persistence read returned `1375`, installed report resource generation passed with `bucketMs=1000`, `DATA.cpuVoltage`, and `DATA.cpuVid`, performance optimization checks did not regress, all required tests passed, and final residual process status was `NO_MATCHING_RESIDUAL_PROCESSES`.
