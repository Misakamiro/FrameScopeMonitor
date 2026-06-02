# FrameScope build artifact sync after optimization report

Date: 2026-06-02
Workspace: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Scope

This run only rebuilt/synced artifacts and performed lightweight payload/portable smoke plus the requested validation commands.

No source logic was changed in this run. No installer was executed. No real game was started.

## Pre-build git status

Command:

```powershell
git status --short
```

Result: non-clean before this run. The workspace already contained many modified/deleted/untracked source, doc, test, asset, and dependency files. Representative entries included:

```text
 M build.ps1
 M framescope-config.example.json
 M packaging/FrameScopeSetupNative.cs
 M src/app/FrameScopeNativeMonitor.WebHost.cs
 M src/frontend/src/App.tsx
 M src/reporting/FrameScopeReportGenerator.Html.Scripts.cs
 M src/reporting/FrameScopeReportGenerator.SystemData.cs
 M tests/Build-FrameScopeTests.ps1
 M tests/FrameScopeReportManifestTests.cs
 M tests/chart-sampling-tests.js
 D src/frontend/src/components/MetricCard.tsx
 D src/frontend/src/components/ProcessRow.tsx
 D tools/WebView2Spike/Program.cs
?? BlackSharp.Core.dll
?? DiskInfoToolkit.dll
?? HidSharp.dll
?? LibreHardwareMonitorLib.dll
?? assets/
?? docs/implementation-reports/
?? docs/test-reports/
?? src/app/FrameScopeAppIcon.cs
?? src/core/FrameScopeLoggingPolicy.cs
?? src/monitoring/FrameScopeSystemSampler.CpuCoreTelemetry.cs
?? tests/FrameScopeSystemSamplerCpuCoreTests.cs
```

The build/smoke run later added `smoke-temp/` evidence and this report file.

## Build commands

| Command | Result | Evidence |
| --- | --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | `npm ci` added 110 packages; `tsc --noEmit` passed; Vitest `6` files / `62` tests passed; Vite build emitted `dist/index.html`, `index-Bm6qrZXo.css`, `index-CiA_VsKz.js`, and map. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | .NET dependency restore/build completed with `0` warnings and `0` errors; output printed `Build complete: ...\dist\FrameScopeMonitor-Setup.exe` and `Full setup complete: ...\dist\FrameScopeMonitor-Full-Setup.exe`. |

Answer: `build.ps1` was run.

## Artifact update result

Answer: `dist`, `dist\FrameScopeMonitor-payload`, `dist\FrameScopeMonitor-Setup.exe`, and `dist\FrameScopeMonitor-Full-Setup.exe` were generated/updated by this run.

Before the run, the key setup artifacts were from 2026-05-31. After the run:

| Artifact | Exists | Non-empty | Size bytes | Modified time |
| --- | --- | --- | ---: | --- |
| `dist\FrameScopeMonitor-payload` | yes | yes | directory | `2026-06-02 13:30:33 +08:00` |
| `dist\FrameScopeMonitor-Setup.exe` | yes | yes | `2703872` | `2026-06-02 13:30:33 +08:00` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | yes | yes | `201883136` | `2026-06-02 13:30:34 +08:00` |
| `src\frontend\dist\index.html` | yes | yes | `528` | `2026-06-02 13:30:25 +08:00` |
| `src\frontend\dist\assets\index-Bm6qrZXo.css` | yes | yes | `46743` | `2026-06-02 13:30:25 +08:00` |
| `src\frontend\dist\assets\index-CiA_VsKz.js` | yes | yes | `243164` | `2026-06-02 13:30:25 +08:00` |

## Payload completeness

Answer: payload key files are present and non-empty.

| Payload file | Size bytes | Modified time |
| --- | ---: | --- |
| `FrameScopeMonitor.exe` | `359424` | `2026-06-02 13:30:32 +08:00` |
| `FrameScopeProcessSampler.exe` | `14336` | `2026-06-02 13:30:32 +08:00` |
| `FrameScopeSystemSampler.exe` | `57344` | `2026-06-02 13:30:32 +08:00` |
| `FrameScopeReportGenerator.exe` | `194560` | `2026-06-02 13:30:32 +08:00` |
| `FrameScopeUninstaller.exe` | `38400` | `2026-06-02 13:30:32 +08:00` |
| `Microsoft.Web.WebView2.Core.dll` | `650080` | `2026-05-04 18:06:12 +08:00` |
| `Microsoft.Web.WebView2.WinForms.dll` | `38792` | `2026-05-04 18:06:24 +08:00` |
| `WebView2Loader.dll` | `159112` | `2026-05-04 18:06:34 +08:00` |
| `LibreHardwareMonitorLib.dll` | `1203200` | `2026-02-14 19:16:28 +08:00` |
| `BlackSharp.Core.dll` | `32768` | `2026-01-06 02:20:34 +08:00` |
| `DiskInfoToolkit.dll` | `903680` | `2026-01-08 19:02:50 +08:00` |
| `HidSharp.dll` | `262792` | `2025-10-14 22:27:26 +08:00` |
| `RAMSPDToolkit-NDD.dll` | `233472` | `2025-11-28 17:18:12 +08:00` |
| `System.Buffers.dll` | `23816` | `2025-03-20 04:55:38 +08:00` |
| `System.CodeDom.dll` | `34056` | `2025-12-13 00:51:16 +08:00` |
| `System.Memory.dll` | `145200` | `2025-04-04 07:00:54 +08:00` |
| `System.Numerics.Vectors.dll` | `110344` | `2025-03-20 04:55:42 +08:00` |
| `System.Runtime.CompilerServices.Unsafe.dll` | `19256` | `2025-04-04 07:00:52 +08:00` |
| `System.Security.AccessControl.dll` | `35952` | `2021-10-23 07:45:08 +08:00` |
| `System.Security.Principal.Windows.dll` | `18312` | `2020-10-20 02:46:28 +08:00` |
| `System.Threading.AccessControl.dll` | `34568` | `2026-01-26 01:33:42 +08:00` |
| `tools\PresentMon-2.4.1-x64.exe` | `927304` | `2026-05-03 01:15:16 +08:00` |
| `frontend\index.html` | `528` | `2026-06-02 13:30:25 +08:00` |
| `frontend\assets\index-Bm6qrZXo.css` | `46743` | `2026-06-02 13:30:25 +08:00` |
| `frontend\assets\index-CiA_VsKz.js` | `243164` | `2026-06-02 13:30:25 +08:00` |
| `frontend\assets\index-CiA_VsKz.js.map` | `659575` | `2026-06-02 13:30:25 +08:00` |
| `assets\icon\framescope-icon.ico` | `29649` | `2026-05-30 07:41:41 +08:00` |
| `assets\icon\framescope-icon.png` | `13801` | `2026-05-30 07:41:41 +08:00` |

## Hashes

| Artifact | Size bytes | Modified time | SHA256 |
| --- | ---: | --- | --- |
| `dist\FrameScopeMonitor-Setup.exe` | `2703872` | `2026-06-02 13:30:33 +08:00` | `FB3095C84F1298294EF272B004EAF01421F2B2C5E2B02F3DE9E9C37C69772AF3` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | `201883136` | `2026-06-02 13:30:34 +08:00` | `0ED585E23B6CED214539FF2CE52CE82B806D7B6CE8D8A614625C2E8DDA2BFF3E` |
| `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe` | `359424` | `2026-06-02 13:30:32 +08:00` | `B9C78F8D794DEB1FC4216B86B2C75F671EC79E6281D450C8F08CC369123192A6` |
| `dist\FrameScopeMonitor-payload\FrameScopeProcessSampler.exe` | `14336` | `2026-06-02 13:30:32 +08:00` | `86AF01A92B7BF90A2226535914F66A0F93818D16BA53C550493EED8060E71890` |
| `dist\FrameScopeMonitor-payload\FrameScopeSystemSampler.exe` | `57344` | `2026-06-02 13:30:32 +08:00` | `4F1A8AABFF2E96CA92686AEE209F3F4B04895A232EA467B117ABE37F350F0194` |
| `dist\FrameScopeMonitor-payload\FrameScopeReportGenerator.exe` | `194560` | `2026-06-02 13:30:32 +08:00` | `642AA43DF28F15B1A6115743802CC6824B5D6A6782792B073161FCD00A2A7524` |
| `dist\FrameScopeMonitor-payload\frontend\index.html` | `528` | `2026-06-02 13:30:25 +08:00` | `0F3583905CC90DF7DD0EBD1D85581C8F581C8073DCED4C897A18BC4CCB2E8B04` |
| `dist\FrameScopeMonitor-payload\frontend\assets\index-Bm6qrZXo.css` | `46743` | `2026-06-02 13:30:25 +08:00` | `663220F672B1CB79C47EDF9C9F489DA212A2EA5C6F16D31BB7517A8AA83E4046` |
| `dist\FrameScopeMonitor-payload\frontend\assets\index-CiA_VsKz.js` | `243164` | `2026-06-02 13:30:25 +08:00` | `8E4C56102FA046AB24782A7D2C93B12AB90186C4535751A898D02DE4567C38DA` |
| `dist\FrameScopeMonitor-payload\frontend\assets\index-CiA_VsKz.js.map` | `659575` | `2026-06-02 13:30:25 +08:00` | `5D9DC526998672C640208F210FF8E841C01D4A7612DC1FDC5457BB75415E9800` |

## Payload smoke

Answer: PARTIAL.

Passed smoke evidence:

| Smoke | Result | Evidence |
| --- | --- | --- |
| WebView2 runtime self-test from portable payload copy | PASS | `available:148.0.3967.70` |
| WebView2 settings persistence read smoke from portable payload copy | PASS | exit `0`; `success=true`; `pageLoaded=true`; `pageReady=true`; `reducedMotion=true`; `settingsLoaded=true`; `inputLoaded=true`; `actualTelemetrySampleIntervalMs=1375`. Evidence: `smoke-temp\artifact-sync-portable-20260602-133654\FrameScopeMonitor-payload\smoke-profile\webview2-settings-persistence-smoke.json`. |
| Report resource generation/load smoke using payload `FrameScopeReportGenerator.exe` | PASS | exit `0`; generated `framescope-interactive-report.html`, `framescope-interactive-data.js`, and manifest; manifest `reportKind=full`, `frames=120`; HTML references `framescope-interactive-data.js` and contains canvas; data contains `window.FRAMESCOPE_DATA`. Evidence root: `smoke-temp\artifact-sync-report-20260602-134224`. |

Partial/failing smoke evidence:

| Smoke | Result | Evidence |
| --- | --- | --- |
| First WebView2 target/settings smoke with config outside app root | FAIL as expected for path-policy trial | frontend loaded, but bridge rejected temp config as `Config path is outside the application root.` Evidence: `smoke-temp\artifact-sync-20260602-133322\webview2-ui-smoke.json`. |
| Portable-copy WebView2 target/settings evidence smoke with config inside portable root | PARTIAL | `pageLoaded=true`, `pageReady=true`, `reducedMotion=true`, `targetsLoaded=true`, `targetAddSaved=true`, `targetEditSaved=true`, `targetDeleteSaved=true`, `settingsLoaded=true`, `settingsSaved=true`, `savedTelemetrySampleIntervalMs=1375`, but overall `success=false` with `error="Target delete evidence did not complete."` Evidence: `smoke-temp\artifact-sync-portable-20260602-133654\FrameScopeMonitor-payload\smoke-profile\webview2-ui-smoke.json`. |

No real game was launched during smoke. The smoke used a copied portable payload under `smoke-temp`, temporary config/profile/state/history/data-root, and synthetic report data.

## Key optimization/result checks in build artifact

The checks below used the newly built payload report generator and generated report artifacts from `smoke-temp\artifact-sync-report-20260602-134224`.

| Check | Result | Evidence |
| --- | --- | --- |
| FPS GamePP style entered artifact behavior | PASS | Generated report HTML contains `FPS GamePP chart`; data contains `"fps":{"bucketMs":1000,...}`. |
| `bucketMs=1000` | PASS | `framescope-interactive-data.js` contains `"bucketMs":1000`. |
| CPU Voltage/Vcore chart | PASS | Generated report HTML contains `DATA.cpuVoltage`; data contains top-level `"cpuVoltage"` with `series[0].name="CPU Voltage / Vcore"` and Vcore samples. |
| CPU Core VID chart | PASS | Generated report HTML contains `DATA.cpuVid`; data contains top-level `"cpuVid"` with per-core VID series. |
| VID/Vcore separation | PASS | Manifest has `cpuVoltageVcoreAvailable=true` and `cpuVidAvailable=true`; data keeps `cpuVoltage.series[0].key="cpu-voltage:vcore"` separate from `cpuVid.series[*].key="cpu-vid:*"`; generated note states VID is CPU request/target voltage and not real per-core Vcore. |
| P0/P1/P2 related validations | PASS for requested test commands | `FrameScopeReportManifestTests.exe`, `FrameScopeSystemSamplerCpuCoreTests.exe`, `FrameScopeWebBridgeTests.exe`, and `chart-sampling-tests.js` all returned `0` / PASS. |

## Validation commands

| Command | Result | Notes |
| --- | --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | `6` Vitest files / `62` tests passed; Vite build passed. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | setup and full setup generated. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | Output: `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | Output ended with `FrameScopeReportManifestTests: PASS`. This is a unit/synthetic report test; it did not start BF6 or any real game. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS | Output: `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS | Output: `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| `.\tests\FrameScopeLoggingPolicyTests.exe` | PASS | Output: `FrameScopeLoggingPolicyTests: PASS`. |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS | Output: `FrameScopeWebBridgeTests: PASS`. |
| `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe .\tests\chart-sampling-tests.js` | PASS | Output: `chart-sampling-tests: PASS`. |
| `git diff --check` | PASS | Exit code `0`; output contained LF-to-CRLF warnings only, no whitespace errors. |
| Residual process check | PASS | `NO_MATCHING_RESIDUAL_PROCESSES`. Checked `FrameScopeMonitor`, `FrameScopeProcessSampler`, `FrameScopeSystemSampler`, `FrameScopeReportGenerator`, `PresentMon-2.4.1-x64`, `FrameScopeMonitor-Setup`, and `FrameScopeMonitor-Full-Setup`. |

## Explicitly not performed

| Action | Performed? |
| --- | --- |
| Install FrameScope | No |
| Run `dist\FrameScopeMonitor-Full-Setup.exe /quiet` | No |
| Run setup/full setup interactively | No |
| Start a real game | No |
| Test BF6 as a real game | No |
| Push GitHub | No |
| Update Release | No |
| Expand into full functional retest | No |
| Modify source logic | No |

## Final status

Final result: PARTIAL.

Reason: all build commands, artifact existence/hash checks, report resource smoke, settings persistence smoke, requested tests, `git diff --check`, and residual process checks passed. The only non-pass item is the built-in WebView2 target/settings evidence smoke, whose portable-copy run loaded the React UI and completed most actions but reported `success=false` with `Target delete evidence did not complete.` This was recorded only; no source logic was changed.
