# FrameScope Monitor WebView2 Final Packaging Validation Report

Date: 2026-05-23
Source root: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`
Evidence root: `artifacts\final-packaging-validation-20260523`

## 1. Current Conclusion

PASS.

The current uncommitted WebView2 UI / motion / smoke changes were rebuilt into the release payload, packaged into the normal installer, packaged into the Full installer, installed locally, and revalidated from both the packaged payload and `%LOCALAPPDATA%\FrameScopeMonitor`.

No UI design, animation design, C# bridge business semantics, backend sampling, report generation semantics, diagnostics semantics, GameLite, WMI, or SGuard code was changed during this final packaging validation pass.

One non-blocking installed-directory observation: after an overwrite install, `%LOCALAPPDATA%\FrameScopeMonitor\frontend\assets` still contains old hashed frontend assets from a previous install. The current `frontend\index.html` points only to the new assets, and the new installed `index.html`, CSS, JS, `FrameScopeMonitor.exe`, and `WebView2Loader.dll` hash-match the rebuilt payload. The release payload itself is clean and does not contain those old assets.

## 2. Package Artifacts And SHA256

Fresh package output from `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`:

| Artifact | Size bytes | SHA256 |
| --- | ---: | --- |
| `dist\FrameScopeMonitor-Setup.exe` | `1261056` | `2FBAF77C9D23476B110EA730359C30BA86424AE4C4D9A6546365E93BE4EC3A96` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | `200440320` | `3695AB2A9F9B7D38092B1F96DB22FA131F03CB391F6A22C30B79DACBEC1FE60C` |
| `dist\FrameScopeMonitor-Installer.zip` | `201631825` | `860ED40B083FB2D3E971EAB0D603E3F3707C2A728A589A5B588B7E8003B4B8EC` |

Supporting package files:

| Artifact | Size bytes | Result |
| --- | ---: | --- |
| `dist\FrameScopeMonitor-installer-source\payload.zip` | `1231912` | Present |
| `packaging\MicrosoftEdgeWebView2RuntimeInstallerX64.exe` | `199178960` | Present, used as Full installer embedded Runtime resource |

Installer zip contents:

- `FrameScopeMonitor-Setup.exe`
- `FrameScopeMonitor-Full-Setup.exe`
- `FrameScopeMonitor-LegacyCleanup.exe`
- `README-FrameScopeMonitor.txt`

## 3. Required Command Verification

| Required check | Result |
| --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS. `npm ci`, `tsc --noEmit`, Vitest `5` files / `35` tests, and Vite production build passed. Built assets: `index-Bwr_RKdJ.css`, `index-DrdBxqGR.js`. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS. Rebuilt normal and Full installers. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS. |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS. |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS. |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS. |
| `.\tests\FrameScopeWebView2RuntimeTests.exe` | PASS. Includes bundled Runtime decision coverage. |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS. |
| `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo` | PASS. `0` warnings / `0` errors. |
| `git diff --check` | PASS. Exit code `0`; output only LF-to-CRLF working-copy warnings, no whitespace errors. |

## 4. Payload Integrity

Payload root: `dist\FrameScopeMonitor-payload`

Required files present:

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
- `frontend\assets\index-Bwr_RKdJ.css`
- `frontend\assets\index-DrdBxqGR.js`
- `frontend\assets\index-DrdBxqGR.js.map`
- `Uninstall-FrameScopeMonitor.cmd`
- `README-FrameScopeMonitor.txt`

Forbidden release-payload pollution check: PASS.

Not found in `dist\FrameScopeMonitor-payload`:

- `artifacts`
- `framescope-config.json`
- `framescope-watcher.log`
- smoke temporary files
- runtime `.log` files

Note: the direct packaged smoke run wrote `framescope-config.json` and `framescope-watcher.log` into the payload working directory because the app root is the executable directory. Those two runtime side-effect files were removed after smoke verification, and the final payload directory was rechecked clean. The installer-embedded `payload.zip` did not contain those files.

## 5. Installer Verification

Normal installer:

- Path: `dist\FrameScopeMonitor-Setup.exe`
- SHA256: `2FBAF77C9D23476B110EA730359C30BA86424AE4C4D9A6546365E93BE4EC3A96`
- Install method tested: `/quiet`
- Install log result: `fullPackage=False`, `install-complete`
- Installed WebView2 smoke after normal install: PASS

Full installer:

- Path: `dist\FrameScopeMonitor-Full-Setup.exe`
- SHA256: `3695AB2A9F9B7D38092B1F96DB22FA131F03CB391F6A22C30B79DACBEC1FE60C`
- Install method tested: `/quiet`
- Embedded resources: `FrameScopePayload`, `FrameScopeWebView2RuntimeInstaller`
- Embedded WebView2 Runtime resource length: `199178960`
- Install log result: `fullPackage=True`, Runtime already available, `version=148.0.3967.70`, `install-complete`
- Existing Runtime behavior: skipped bundled Runtime install because Runtime was already installed
- Installed WebView2 smoke after Full install: PASS

Installer zip:

- Path: `dist\FrameScopeMonitor-Installer.zip`
- SHA256: `860ED40B083FB2D3E971EAB0D603E3F3707C2A728A589A5B588B7E8003B4B8EC`
- Contents verified as the rebuilt normal installer, rebuilt Full installer, legacy cleanup exe, and README.

## 6. WebView2 Runtime Verification

Runtime decision tests:

- `FrameScopeWebView2RuntimeTests.exe`: PASS.
- Full package + missing Runtime returns install-needed decision.
- Full package + installed Runtime returns skip-install decision.
- Normal package + missing Runtime does not try to install a non-bundled Runtime.
- Missing-runtime Chinese message contract is covered.

Runtime self-tests:

- Actual local Runtime: PASS, exit code `0`, evidence `available:148.0.3967.70`.
- Installed override: PASS, exit code `0`, evidence `available:test-installed`.
- Missing override: PASS, exit code `3`, evidence writes the missing Runtime message.

Conclusion:

- Full package contains the WebView2 Evergreen Standalone Installer.
- With Runtime present, the Full installer correctly skips installing Runtime.
- Missing Runtime handling has a tested exit path and user-facing message.

## 7. Packaged WebView2 Smoke

Packaged executable used:

- `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe`

Packaged live smoke:

- Evidence: `artifacts\final-packaging-validation-20260523\packaged-webview2-live-smoke.json`
- Screenshot: `artifacts\final-packaging-validation-20260523\packaged-webview2-live-smoke.png`
- Result: PASS
- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=false`
- `frontendPath=...\dist\FrameScopeMonitor-payload\frontend`
- `webview-ready` received
- `navigation-completed success=True status=200`
- `console=[]`
- `errors=[]`

Packaged reduced-motion smoke:

- Evidence: `artifacts\final-packaging-validation-20260523\packaged-webview2-reduced-motion-smoke.json`
- Screenshot: `artifacts\final-packaging-validation-20260523\packaged-webview2-reduced-motion-smoke.png`
- Result: PASS
- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=true`
- `frontendPath=...\dist\FrameScopeMonitor-payload\frontend`

This proves packaged smoke loaded the dist payload frontend, not source `src\frontend\dist` and not fallback HTML.

## 8. Installed WebView2 Smoke

Installed root:

- `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor`

Final installed state was produced by the Full installer `/quiet`, not by manual payload copy.

Installed Full live smoke:

- Evidence: `artifacts\final-packaging-validation-20260523\installed-full-webview2-live-smoke.json`
- Screenshot: `artifacts\final-packaging-validation-20260523\installed-full-webview2-live-smoke.png`
- Result: PASS
- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=false`
- `frontendPath=C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\frontend`
- `webview-ready` received
- `navigation-completed success=True status=200`
- `console=[]`
- `errors=[]`

Installed Full reduced-motion smoke:

- Evidence: `artifacts\final-packaging-validation-20260523\installed-full-webview2-reduced-motion-smoke.json`
- Screenshot: `artifacts\final-packaging-validation-20260523\installed-full-webview2-reduced-motion-smoke.png`
- Result: PASS
- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=true`
- `frontendPath=C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\frontend`

## 9. Installed Payload Sync

Hash comparison evidence:

- `artifacts\final-packaging-validation-20260523\installed-payload-hash-compare.json`

Hash-matched between `dist\FrameScopeMonitor-payload` and `%LOCALAPPDATA%\FrameScopeMonitor`:

- `FrameScopeMonitor.exe`
- `frontend\index.html`
- `frontend\assets\index-Bwr_RKdJ.css`
- `frontend\assets\index-DrdBxqGR.js`
- `WebView2Loader.dll`

Installed asset comparison evidence:

- `artifacts\final-packaging-validation-20260523\installed-frontend-assets-compare.json`

Current installed frontend has no missing payload assets. Extra old installed assets from a prior install:

- `index-Cci9OgVZ.css`
- `index-JeBY-Un3.js`
- `index-JeBY-Un3.js.map`

These extra files are not in the release payload and are not referenced by the current installed `frontend\index.html`.

## 10. UI Regression Screenshots

Regression audit evidence:

- `artifacts\final-packaging-validation-20260523\packaged-installed-ui-regression-audit.json`

Packaged screenshots:

- Targets search area: `artifacts\final-packaging-validation-20260523\packaged-ui-targets-search-area-1280x720.png`
- Reports more menu: `artifacts\final-packaging-validation-20260523\packaged-ui-reports-more-menu-open-1280x720.png`
- Sidebar normal: `artifacts\final-packaging-validation-20260523\packaged-ui-sidebar-normal-active-hover-focus-1280x720.png`
- Sidebar compact: `artifacts\final-packaging-validation-20260523\packaged-ui-sidebar-compact-active-hover-focus-900x760.png`
- Overview single primary CTA: `artifacts\final-packaging-validation-20260523\packaged-ui-overview-single-primary-cta-1280x720.png`

Installed screenshots:

- Targets search area: `artifacts\final-packaging-validation-20260523\installed-ui-targets-search-area-1280x720.png`
- Reports more menu: `artifacts\final-packaging-validation-20260523\installed-ui-reports-more-menu-open-1280x720.png`
- Sidebar normal: `artifacts\final-packaging-validation-20260523\installed-ui-sidebar-normal-active-hover-focus-1280x720.png`
- Sidebar compact: `artifacts\final-packaging-validation-20260523\installed-ui-sidebar-compact-active-hover-focus-900x760.png`
- Overview single primary CTA: `artifacts\final-packaging-validation-20260523\installed-ui-overview-single-primary-cta-1280x720.png`

UI assertions:

- Packaged: PASS.
- Installed: PASS.
- Overview primary CTA count: `1`.
- Reports more menu role: `menu`, item count `3`.
- Targets search icon is inside the input field.
- Sidebar normal active/focus states are distinct.
- Sidebar compact width is `84`.

## 11. Sidebar Fixed Scroll Verification

Packaged sidebar scroll:

- Before: `sidebarTop=0`, `brandTop=16`, `activeNavTop=182`, `scrollTop=0`
- After right viewport scroll: `sidebarTop=0`, `brandTop=16`, `activeNavTop=182`, `scrollTop=627`
- Result: PASS

Installed sidebar scroll:

- Before: `sidebarTop=0`, `brandTop=16`, `activeNavTop=182`, `scrollTop=0`
- After right viewport scroll: `sidebarTop=0`, `brandTop=16`, `activeNavTop=182`, `scrollTop=627`
- Result: PASS

## 12. Git Diff Check

Command:

```powershell
git diff --check
```

Result: PASS.

Git returned exit code `0`. Output contained only LF-to-CRLF working-copy warnings and no whitespace errors.

## 13. Residual Process Check

Process check result: PASS.

No matching residual processes found for this validation scope:

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `PresentMon.exe`
- `PresentMon-2.4.1-x64.exe`
- validation `node.exe`
- validation `msedge.exe`
- validation `msedgewebview2.exe`

Port check result: PASS.

No matching listen ports remained for validation ports:

- `4219`
- `4297`
- `4491`
- `4492`
- `9389`
- `9397`
- `9491`
- `9492`

## 14. Release Recommendation

Recommended next actions:

- Commit: YES.
- Push GitHub: YES.
- Update GitHub Release: YES, from packaging/install validation perspective.

Use only the fresh package assets and SHA256 values from this report. Do not reuse older SHA values from previous reports or release-note drafts.

Recommended release assets:

- `dist\FrameScopeMonitor-Setup.exe`
- `dist\FrameScopeMonitor-Full-Setup.exe`
- `dist\FrameScopeMonitor-Installer.zip`
- `dist\FrameScopeMonitor-LegacyCleanup.exe`
- `dist\README-FrameScopeMonitor.txt`
