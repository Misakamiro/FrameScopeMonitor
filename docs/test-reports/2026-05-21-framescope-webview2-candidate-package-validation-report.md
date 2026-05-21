# FrameScope Monitor WebView2 Candidate Package Validation Report

Date: 2026-05-21
Role: candidate packaging validation owner
Source root: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
Install dir: `%LOCALAPPDATA%\FrameScopeMonitor`

## 1. Current Conclusion

候选通过。

Frontend verification, main build, C# tests, chart sampling, RenderProbe build, payload integrity, payload WebView2 default-entry smoke, local install payload-copy sync, installed WebView2 smoke, `git diff --check`, and residual-process inspection passed for this candidate.

No UI design, C# bridge semantics, backend sampling, report generation, GameLite, WMI, or SGuard source code was changed in this validation pass.

Release-publish refresh: the final pre-release verification reran `build.ps1`, which regenerated the installer binaries. The package hashes below are the hashes of the final files selected for the `v1.1.2` GitHub Release upload after the refresh.

## 2. Package Artifacts

- `dist\FrameScopeMonitor-Setup.exe`
  - Size: `1,234,944` bytes
  - SHA256: `9BCB7BE1B92FB09BDFD4F43C024AEDA078EFD4411F5FA1031C072A2AF070037F`
- `dist\FrameScopeMonitor-Installer.zip`
  - Size: `1,233,905` bytes
  - SHA256: `51E4780C3468DD8E782D0E77E249EFCCB03DE5AD00598709DB20896D2B7435D6`
- `dist\FrameScopeMonitor-LegacyCleanup.exe`
  - Size: `29,184` bytes
  - SHA256: `E4A7741EA75833196BD0ABAF71D5EAD7010DE1D319A63D2991468DD642870F55`
- `dist\README-FrameScopeMonitor.txt`
  - Size: `1,026` bytes
  - SHA256: `568241B0933E97AAB4633F80B02F7C8551946E56D0F60E13FD04AFC2D1EE269C`

## 3. Local Install Update Method

本机安装目录更新方式：payload copy, not full interactive installer.

I copied only the release-item allowlist from `dist\FrameScopeMonitor-payload` into `%LOCALAPPDATA%\FrameScopeMonitor`:

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `FrameScopeUninstaller.exe`
- `Microsoft.Web.WebView2.Core.dll`
- `Microsoft.Web.WebView2.WinForms.dll`
- `WebView2Loader.dll`
- `README-FrameScopeMonitor.txt`
- `Uninstall-FrameScopeMonitor.cmd`
- `tools`
- `frontend`

Runtime state was not copied from payload into install. Existing install runtime files such as `framescope-config.json` and `framescope-watcher.log` were left as install runtime state. Smoke-generated runtime files were also removed from `dist\FrameScopeMonitor-payload` after payload smoke, so the payload directory ended with only release items.

Registry sync was updated under `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\FrameScopeMonitor`:

- `DisplayVersion=1.1.2`
- `InstallLocation=C:\Users\misakamiro\AppData\Local\FrameScopeMonitor`
- `DisplayIcon=C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe`

This was not a full interactive installer flow.

Release-publish refresh: after the final pre-release rebuild, `%LOCALAPPDATA%\FrameScopeMonitor` was updated again with the same payload-copy allowlist. Runtime state such as `framescope-config.json`, watcher logs, and history files was still not copied from the payload.

## 4. Source Changes

No product source code was changed in this candidate validation pass.

This pass generated validation artifacts under:

- `artifacts\candidate-package-20260521`

This report was added under:

- `docs\test-reports\2026-05-21-framescope-webview2-candidate-package-validation-report.md`

## 5. Payload Integrity Result

PASS.

`build.ps1` confirms the frontend packaging path:

- reads `src\frontend\dist`
- requires `src\frontend\dist\index.html`
- copies it to `dist\FrameScopeMonitor-payload\frontend`
- embeds `dist\FrameScopeMonitor-payload\*` into `dist\FrameScopeMonitor-installer-source\payload.zip`
- compiles that payload into `dist\FrameScopeMonitor-Setup.exe`

Required payload files were present:

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `FrameScopeUninstaller.exe`
- `Microsoft.Web.WebView2.Core.dll`
- `Microsoft.Web.WebView2.WinForms.dll`
- `WebView2Loader.dll`
- `tools\PresentMon-2.4.1-x64.exe`
- `frontend\index.html`
- `frontend\assets\index-6fiQLqQg.css`
- `frontend\assets\index-D0pIZWdG.js`
- `frontend\assets\index-D0pIZWdG.js.map`
- `README-FrameScopeMonitor.txt`
- `Uninstall-FrameScopeMonitor.cmd`

After cleanup, no runtime-only payload items remained:

- `framescope-config.json`: absent
- `framescope-watcher.log`: absent
- `framescope-history.jsonl`: absent
- `framescope-watcher-state.json`: absent
- `artifacts`: absent
- `framescope-runs`: absent

## 6. Payload Default-Entry WebView2 Smoke

PASS.

Command used the payload executable directly and did not pass `--web-ui`:

```powershell
dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\candidate-package-20260521\payload-webview2-smoke.json --web-ui-screenshot artifacts\candidate-package-20260521\payload-webview2-smoke.png --web-ui-timeout-ms 45000
```

Evidence:

- `artifacts\candidate-package-20260521\payload-webview2-smoke.json`
- `artifacts\candidate-package-20260521\payload-webview2-smoke.png`
- `artifacts\candidate-package-20260521\payload-webview2-reduced-motion-smoke.json`
- `artifacts\candidate-package-20260521\payload-webview2-reduced-motion-smoke.png`

Observed:

- Default app entry is WebView2 React UI; no `--web-ui` flag was needed.
- `frontendPath=C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\dist\FrameScopeMonitor-payload\frontend`
- Normal smoke: `success=true`, `smokePayload.success=true`
- Reduced-motion smoke: `success=true`, `reducedMotion=true`
- Reports live actions: open, open directory, regenerate accepted, in-flight, and completed.
- Bridge extension actions: `reports.list`, `targets.get`, unsafe path rejection, diagnostics, monitor start, and monitor stop passed.
- Transition screenshots were generated with no blank page or full-page spinner evidence.
- Fake macOS controls remain absent based on the merge retest and interaction audit evidence.

Reports/narrow-layout regression check:

- Latest merge retest evidence: `artifacts\webview2-react-ui-merge-retest-20260521\cdp-900x760-reports.png`
- Afterfix interaction evidence: `artifacts\webview2-react-ui-interaction-review-20260521-afterfix\browser-reports-900x760.png`
- `browser-interaction-audit.json`: `horizontalOverflow=false`, `fakeWindowControls=0`, `clippedButtons=0` for 900x760 Reports.

## 7. Local Install SHA256 Sync Result

PASS.

Payload and installed files matched for all checked release items:

- `FrameScopeMonitor.exe`: `533624C847876472`
- `FrameScopeProcessSampler.exe`: `F22529B9D1B17FCD`
- `FrameScopeSystemSampler.exe`: `A8C99C99C0EFDB8F`
- `FrameScopeReportGenerator.exe`: `6FB6D0E9BEBC90A8`
- `FrameScopeUninstaller.exe`: `87F49E668FC19DBF`
- `Microsoft.Web.WebView2.Core.dll`: `FCC07F6B8DDB7C90`
- `Microsoft.Web.WebView2.WinForms.dll`: `1935405EAEDE5C98`
- `WebView2Loader.dll`: `BB5FDA5F2D3AAE5E`
- `README-FrameScopeMonitor.txt`: `568241B0933E97AA`
- `Uninstall-FrameScopeMonitor.cmd`: `574D77857529129A`
- `tools\PresentMon-2.4.1-x64.exe`: `D74183E7AE630F72`
- `frontend\index.html`: `7DF61A282036A0B2`
- `frontend\assets\index-6fiQLqQg.css`: `CDD847010DA8B73D`
- `frontend\assets\index-D0pIZWdG.js`: `BBF5D527AC3137C5`
- `frontend\assets\index-D0pIZWdG.js.map`: `25AEE126D47EE3E8`

Evidence:

- `artifacts\candidate-package-20260521\install-payload-copy-hash-log.json`
- `artifacts\candidate-package-20260521\install-payload-copy-summary.json`
- `artifacts\release-publish-20260521\installed-webview2-smoke-after-sync.json`
- `artifacts\release-publish-20260521\installed-webview2-reduced-motion-smoke-after-sync.json`

## 8. Installed WebView2 Smoke

PASS.

Command used the installed executable directly and did not pass `--web-ui`:

```powershell
%LOCALAPPDATA%\FrameScopeMonitor\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\candidate-package-20260521\installed-webview2-smoke.json --web-ui-screenshot artifacts\candidate-package-20260521\installed-webview2-smoke.png --web-ui-timeout-ms 45000
```

Evidence:

- `artifacts\candidate-package-20260521\installed-webview2-smoke.json`
- `artifacts\candidate-package-20260521\installed-webview2-smoke.png`
- `artifacts\candidate-package-20260521\installed-webview2-reduced-motion-smoke.json`
- `artifacts\candidate-package-20260521\installed-webview2-reduced-motion-smoke.png`

Observed:

- Default app entry is WebView2 React UI; no `--web-ui` flag was needed.
- `frontendPath=C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\frontend`
- Normal smoke: `success=true`, `smokePayload.success=true`
- Reduced-motion smoke: `success=true`, `reducedMotion=true`
- Live actions: Reports open/open-directory/regenerate, Settings save, Diagnostics, Targets, monitor start/stop passed.
- No old WinForms UI fallback was observed in the installed smoke path.

Release-publish refresh smoke also passed after the second payload-copy sync:

- `artifacts\release-publish-20260521\installed-webview2-smoke-after-sync.json`: `success=true`, `smokePayload.success=true`
- `artifacts\release-publish-20260521\installed-webview2-reduced-motion-smoke-after-sync.json`: `success=true`, `smokePayload.success=true`

## 9. Command Verification Results

| Command | Result | Notes |
| --- | --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | `npm ci`, `tsc --noEmit`, Vitest `4` files / `12` tests, Vite build passed. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | Rebuilt `dist\FrameScopeMonitor-Setup.exe`. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | Test executables rebuilt. |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS | `FrameScopeReportProgressTests: PASS`. |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS | `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS | `FrameScopeWebBridgeTests: PASS`. |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS | `chart-sampling-tests: PASS`. |
| `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo` | PASS | `0` warnings, `0` errors. |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS | Exit code `0`; Git printed LF-to-CRLF working-copy warnings only, no whitespace errors. |

## 10. Residual Process Check

PASS.

Checked:

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`
- Vite / esbuild / node / test WebView2 user-data related processes

Result:

- No FrameScope app, sampler, report generator, PresentMon, FakePresentMon, TslGame, GameLite, Vite, esbuild, or test WebView2 user-data residual process was found after smoke runs.
- Existing `msedgewebview2.exe` processes belonged to Windows Search (`SearchHost.exe`) and Clash Verge (`clash-verge.exe`) user-data directories, not FrameScope.
- One `node.exe` was the active Codex desktop Node REPL process for this validation session, not a frontend dev server or test WebView2 process.

## 11. Recommendation

建议提交并推送 GitHub PR/branch for the already validated UI and candidate packaging state.

The release-publish refresh has reviewed/staged the existing UI changes, this validation report, and candidate artifacts policy. The final upload assets should be the refreshed files listed in section 2.
