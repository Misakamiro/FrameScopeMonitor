# FrameScope build artifact sync after target delete fix report

Date: 2026-06-02
Workspace: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Note: the run crossed local midnight during late evidence collection; the requested report filename/date is kept as 2026-06-02.

## Scope

This run only rebuilt/synced artifacts and retested the built payload/setup after the target delete fix.

No FrameScope install was performed. No setup or full setup executable was run. No real game was started. BF6 was not tested. Nothing was pushed to GitHub and no Release was updated.

## Pre-build git status

Command:

```powershell
git status --short
```

Result: non-clean before this run. The workspace already contained many modified/deleted/untracked source, test, doc, asset, and dependency files. Representative entries included:

```text
 M build.ps1
 M framescope-config.example.json
 M packaging/FrameScopeSetupNative.cs
 M src/app/FrameScopeNativeMonitor.WebHost.cs
 M src/frontend/src/pages/TargetsPage.tsx
 M src/reporting/FrameScopeReportGenerator.Html.Scripts.cs
 M tests/Build-FrameScopeTests.ps1
 M tests/FrameScopeWebBridgeTests.cs
 M tests/chart-sampling-tests.js
 D src/frontend/src/components/MetricCard.tsx
 D tools/WebView2Spike/Program.cs
?? docs/implementation-reports/2026-06-02-framescope-target-settings-evidence-smoke-delete-fix-report.md
?? smoke-temp/
?? src/core/FrameScopeLoggingPolicy.cs
?? src/monitoring/FrameScopeSystemSampler.CpuCoreTelemetry.cs
?? tests/FrameScopeLoggingPolicyTests.cs
?? tests/FrameScopeNativeWatcherPolicyTests.cs
```

This run later added the current report and fresh `smoke-temp\artifact-sync-after-target-delete-fix-20260602-2345\` evidence.

## Build result

| Command | Result |
| --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS. `npm ci` added 110 packages; `tsc --noEmit` passed; Vitest `6` files / `63` tests passed; Vite build emitted `index.html`, `index-Bm6qrZXo.css`, `index-BOkrfFDZ.js`, and map. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS. .NET dependency restore/build completed with `0` warnings and `0` errors. Generated `dist\FrameScopeMonitor-Setup.exe` and `dist\FrameScopeMonitor-Full-Setup.exe`. |

Answer: `build.ps1` passed.

## Artifact existence

| Artifact | Exists | Non-empty | Size bytes | Modified time |
| --- | --- | --- | ---: | --- |
| `dist\FrameScopeMonitor-payload` | yes | yes | directory | `2026-06-02 23:45:01 +08:00` |
| `dist\FrameScopeMonitor-Setup.exe` | yes | yes | `2703872` | `2026-06-02 23:45:02 +08:00` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | yes | yes | `201883136` | `2026-06-02 23:45:02 +08:00` |
| `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe` | yes | yes | `359424` | `2026-06-02 23:45:00 +08:00` |
| `dist\FrameScopeMonitor-payload\FrameScopeReportGenerator.exe` | yes | yes | `194560` | `2026-06-02 23:45:01 +08:00` |
| `dist\FrameScopeMonitor-payload\FrameScopeProcessSampler.exe` | yes | yes | `14336` | `2026-06-02 23:45:01 +08:00` |
| `dist\FrameScopeMonitor-payload\FrameScopeSystemSampler.exe` | yes | yes | `57344` | `2026-06-02 23:45:01 +08:00` |
| `dist\FrameScopeMonitor-payload\frontend\index.html` | yes | yes | `528` | `2026-06-02 23:44:53 +08:00` |
| `dist\FrameScopeMonitor-payload\frontend\assets\index-Bm6qrZXo.css` | yes | yes | `46743` | `2026-06-02 23:44:53 +08:00` |
| `dist\FrameScopeMonitor-payload\frontend\assets\index-BOkrfFDZ.js` | yes | yes | `243150` | `2026-06-02 23:44:53 +08:00` |
| Required DLLs | yes | yes | see payload | WebView2, LibreHardwareMonitor, BlackSharp, DiskInfoToolkit, HidSharp, RAMSPDToolkit, System.* DLLs all present |
| `dist\FrameScopeMonitor-payload\FrameScopeNativeMonitor.exe` | n/a | n/a | n/a | Current `build.ps1` does not generate a separate file by this name; `src\app\FrameScopeNativeMonitor*.cs` is compiled into `FrameScopeMonitor.exe`. |

Answer: setup, full setup, and payload exist and are non-empty. The current build contract has no standalone `FrameScopeNativeMonitor.exe`; the actual native/WebView2 monitor payload executable is `FrameScopeMonitor.exe`.

## Hashes

| Artifact | Size bytes | Modified time | SHA256 |
| --- | ---: | --- | --- |
| `dist\FrameScopeMonitor-Setup.exe` | `2703872` | `2026-06-02 23:45:02 +08:00` | `0946F770391AE56048E65CA7197C23CA48D06501BE4C7E107598662E5525517F` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | `201883136` | `2026-06-02 23:45:02 +08:00` | `4C07D4A3EEB8BE2A9C78BADD11B209C1BC34C13263B52F989A26CA864F50B107` |
| `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe` | `359424` | `2026-06-02 23:45:00 +08:00` | `7D0778FEACEB6CDCA0C994966420FE13644BF23BBF432BE45CA7199CD4628858` |
| `dist\FrameScopeMonitor-payload\FrameScopeReportGenerator.exe` | `194560` | `2026-06-02 23:45:01 +08:00` | `3D0C8E0273B65FDB939198C4D241851E43F4F437A2897E884689BD74B3E11260` |
| `dist\FrameScopeMonitor-payload\FrameScopeProcessSampler.exe` | `14336` | `2026-06-02 23:45:01 +08:00` | `6B0B8870EF22D13EC946029AD3913772947FC9BF340F854DE2568F88CAA25A76` |
| `dist\FrameScopeMonitor-payload\FrameScopeSystemSampler.exe` | `57344` | `2026-06-02 23:45:01 +08:00` | `AA4D38096BC22D3EC730D63314310A614130223954B16F2FCD67A459D77D5C5C` |
| `dist\FrameScopeMonitor-payload\frontend\index.html` | `528` | `2026-06-02 23:44:53 +08:00` | `83D2BB678AF2A5F6361C537657FA6898C454746DB4660E657B1AB1646302031F` |
| `dist\FrameScopeMonitor-payload\frontend\assets\index-Bm6qrZXo.css` | `46743` | `2026-06-02 23:44:53 +08:00` | `663220F672B1CB79C47EDF9C9F489DA212A2EA5C6F16D31BB7517A8AA83E4046` |
| `dist\FrameScopeMonitor-payload\frontend\assets\index-BOkrfFDZ.js` | `243150` | `2026-06-02 23:44:53 +08:00` | `E2A4C2575DF7315E4C7BB66C73D09EE760FB21171C4370D88976E49E03366E64` |
| `dist\FrameScopeMonitor-payload\frontend\assets\index-BOkrfFDZ.js.map` | `659528` | `2026-06-02 23:44:53 +08:00` | `D6A90D2B5F1710FABE9DAD1788E50B72F1FE2FFB937792BA7C5BAFA938AB7BEA` |
| `smoke-temp\artifact-sync-after-target-delete-fix-20260602-2345\report-resource\charts\framescope-interactive-report.html` | `53191` | `2026-06-02 23:51:03 +08:00` | `A0A11BBF508B9FCA27AB8D30437ADE96D77308D8614AF59DDA864199075D9E9D` |
| `smoke-temp\artifact-sync-after-target-delete-fix-20260602-2345\report-resource\charts\framescope-interactive-data.js` | `5191` | `2026-06-02 23:51:03 +08:00` | `4EADC338C8E3EBD2B3936261D8FDDFBBFB069DB96826E39D5799E8E4AF99D5EE` |
| `smoke-temp\artifact-sync-after-target-delete-fix-20260602-2345\report-resource\charts\framescope-interactive-manifest.json` | `3445` | `2026-06-02 23:51:03 +08:00` | `F3782E18029DF7C28AE8737090B57FD275BA9BB7EF899D656E804334B093084C` |

## Setup payload resource check

No installer was run. The setup executables were inspected only by reading managed assembly resources and extracting the embedded `FrameScopePayload` zip to `smoke-temp\artifact-sync-after-target-delete-fix-20260602-2345\setup-payload-resource`.

| Setup artifact | Resource check | Payload zip size | Embedded `FrameScopeMonitor.exe` hash matches dist payload | Embedded frontend JS hash matches dist payload | Extra WebView2 runtime resource |
| --- | --- | ---: | --- | --- | --- |
| `dist\FrameScopeMonitor-Setup.exe` | PASS, resource `FrameScopePayload` present | `2644165` | yes | yes | no |
| `dist\FrameScopeMonitor-Full-Setup.exe` | PASS, resources `FrameScopePayload`, `FrameScopeWebView2RuntimeInstaller` present | `2644165` | yes | yes | yes |

Answer: the rebuilt setup and full setup contain the same payload exe/frontend JS hashes as `dist\FrameScopeMonitor-payload`, so the target delete fix reached the setup payload resource without running the installers.

## Built payload smoke

Primary evidence root:

`smoke-temp\artifact-sync-after-target-delete-fix-20260602-2345\empty-target-delete`

Command shape: ran `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe` with `--web-ui-smoke`, `--web-ui-reduced-motion`, `--web-ui-target-settings-evidence-smoke`, temp config, evidence JSON, screenshot, expected telemetry sample `1375`; then ran `--web-ui-settings-persistence-read-smoke` against the same temp config.

Result: PASS.

| Check | Result |
| --- | --- |
| WebView2 page loaded | PASS, `pageLoaded=true`, `pageReady=true` |
| Reduced-motion smoke | PASS, `reducedMotion=true` |
| React frontend path | `dist\FrameScopeMonitor-payload\frontend` |
| Target add | PASS, `targetAddSaved=true` |
| Target edit | PASS, `targetEditSaved=true` |
| Target delete | PASS, `targetDeleteSaved=true` |
| Delete final target | PASS, final config `target count=0` |
| Settings save | PASS, `settingsSaved=true`, saved telemetry sample `1375` |
| Settings persistence read | PASS, restart/read smoke `actualTelemetrySampleIntervalMs=1375` |
| Smoke exit codes | PASS, target smoke exit `0`, restart/read exit `0` |

Answer: the target delete fix entered the rebuilt payload. The built payload target/settings evidence smoke is PASS.

A secondary script smoke using `tools\evidence-smoke\Run-TargetSettingsEvidenceSmoke.ps1` also passed target add/edit/delete and settings save/read, but that helper keeps a seed target, so its final target count is `1`. It is recorded as supporting evidence only; the empty-target smoke above is the required final-target-delete proof.

## Report resource smoke

Evidence root:

`smoke-temp\artifact-sync-after-target-delete-fix-20260602-2345\report-resource`

Method: synthetic CSV/JSON run data was written to a temp run directory, then the rebuilt payload `dist\FrameScopeMonitor-payload\FrameScopeReportGenerator.exe` generated report resources.

Result: PASS.

| Check | Result |
| --- | --- |
| `FrameScopeReportGenerator.exe` exit | `0` |
| `framescope-interactive-report.html` | exists, `53191` bytes |
| `framescope-interactive-data.js` | exists, `5191` bytes |
| `framescope-interactive-manifest.json` | exists, `3445` bytes |
| Manifest | `reportKind=full`, `frames=120` |
| HTML references data JS | PASS |
| HTML contains canvas | PASS |
| Data JS contains `window.FRAMESCOPE_DATA` | PASS |

## Optimization artifact checks

| Required check | Result | Evidence |
| --- | --- | --- |
| FPS GamePP | PASS | Generated report HTML contains `FPS GamePP chart`. |
| `bucketMs=1000` | PASS | Generated `framescope-interactive-data.js` contains `"bucketMs":1000`; `chart-sampling-tests.js` passed. |
| CPU Voltage/Vcore uses `DATA.cpuVoltage` | PASS | Generated HTML contains `DATA.cpuVoltage`; data has top-level `"cpuVoltage"` and `CPU Voltage / Vcore`. |
| CPU Core VID uses `DATA.cpuVid` | PASS | Generated HTML contains `DATA.cpuVid`; data has top-level `"cpuVid"` and `CPU Core VID`. |
| VID/Vcore separation | PASS | Data contains `cpu-voltage:vcore` and `cpu-vid:*` as separate series. |
| P2 large process list 250 rows | PASS | Payload frontend static probe initial `19 / 250`, `processWindowed=true`; after scroll `10 / 250`, rows `241..250`. Evidence: `smoke-temp\artifact-sync-after-target-delete-fix-20260602-2345\payload-frontend-windowing\payload-windowing-probe.json`. |
| UI motion static state | PASS | Payload CSS has reduced-motion blocks, `animation:none`, `transform:none`; no `transition: all`, no box-shadow transition, no blur/backdrop; source static feedback files contain no `framer-motion`, `<motion.`, or `MotionConfig`. Frontend `uiMotionContract` also passed in verify. |
| Logging rate limiter | PASS | `FrameScopeLoggingPolicyTests.exe` passed. |
| Data root scan guard | PASS | `FrameScopeWebBridgeTests.exe` passed, including report-list noise/data-root scan guard coverage. |

## Validation commands

| Command | Result |
| --- | --- |
| `git status --short` | Recorded; workspace was already non-clean before this run. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS; typecheck, Vitest `63` tests, Vite build passed. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS; `0` warnings, `0` errors; setup/full setup generated. |
| Artifact existence/hash checks | PASS; setup/full setup/payload/frontend/report resource hashes recorded above. |
| Setup payload resource extraction/read-only check | PASS; embedded setup payload hashes match dist payload. No setup was run. |
| Built payload empty-target target/settings WebView2 reduced-motion smoke | PASS; final target count `0`; settings read `1375`. |
| Built payload report resource smoke | PASS; report/data/manifest generated from payload report generator. |
| Payload frontend 250-row windowing probe | PASS; initial `19 / 250`, after scroll `10 / 250`. |
| Payload/source UI motion static scan | PASS; optimized static state preserved. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS; `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS; final line `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS; `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS; `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| `.\tests\FrameScopeLoggingPolicyTests.exe` | PASS; `FrameScopeLoggingPolicyTests: PASS`. |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS; `FrameScopeWebBridgeTests: PASS`. |
| `.\tests\FrameScopeNativeWatcherPolicyTests.exe` | PASS; `FrameScopeNativeWatcherPolicyTests: PASS`. |
| `.\tests\FrameScopeNativeMonitorChildProcessTests.exe` | PASS; `FrameScopeNativeMonitorChildProcessTests: PASS`. |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS; `chart-sampling-tests: PASS`. |
| `git diff --check` | PASS, exit `0`; output contained LF-to-CRLF warnings only, no whitespace errors. |
| Residual process check | PASS; `NO_MATCHING_RESIDUAL_PROCESSES`. Checked FrameScope app/sampler/report/setup/test process names. |

## Explicitly not performed

| Action | Performed |
| --- | --- |
| Install FrameScope | No |
| Run `dist\FrameScopeMonitor-Setup.exe` | No |
| Run `dist\FrameScopeMonitor-Full-Setup.exe` | No |
| Start a real game | No |
| Test BF6 | No |
| Push GitHub | No |
| Update Release | No |

`build.ps1` generated setup/full setup as allowed. The setup executables were inspected read-only for embedded resources but never executed.

## Final status

Final result: PASS.

Reason: frontend verify, `build.ps1`, artifact existence/hash checks, setup embedded payload resource checks, built payload target/settings smoke with final target count `0`, settings persistence read `1375`, report resource smoke, optimization artifact probes, all requested tests, `git diff --check`, and residual process check all passed. The historical/alternate `FrameScopeNativeMonitor.exe` filename is not produced by the current `build.ps1`; the corresponding native monitor code is compiled into `FrameScopeMonitor.exe`, which was hashed and smoked successfully.
