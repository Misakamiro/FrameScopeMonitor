# FrameScope Monitor WebView2 Candidate Packaging And Install Validation Report

Date: 2026-05-21 16:00 +08:00
Role: WebView2 candidate packaging and install validation owner

## 1. Current Conclusion

候选通过。

The current source produced a candidate package, the packaged payload was verified directly, and the local install directory was updated by payload copy. The packaged and installed WebView2 paths both load the React UI from `frontend`, not the old embedded smoke HTML and not `src\frontend\dist`.

This is not a full interactive installer run. The local machine update was a corrected payload-copy sync from:

- `dist\FrameScopeMonitor-payload`

to:

- `%LOCALAPPDATA%\FrameScopeMonitor`

## 2. Packaging Fix

Modified file:

- `build.ps1`

Root cause:

- `build.ps1` was already compiling WebView2 host files and copying WebView2 runtime DLLs, but the payload assembly step did not copy `src\frontend\dist` into `dist\FrameScopeMonitor-payload\frontend`.
- In a packaged directory, `FrameScopeNativeMonitor.WebHost.cs` resolves `frontend\index.html` first. Without that folder, `--web-ui` can fall back to embedded HTML.

Fix:

- `build.ps1` now requires `src\frontend\dist\index.html`.
- It copies the React dist directory to `dist\FrameScopeMonitor-payload\frontend`.
- The generated installer payload zip now includes:
  - `frontend\index.html`
  - `frontend\assets\index-BS04C48Z.js`
  - `frontend\assets\index-BS04C48Z.js.map`
  - `frontend\assets\index-Cah-GVqH.css`

## 3. Candidate Package Paths

- Payload: `dist\FrameScopeMonitor-payload`
- Setup exe: `dist\FrameScopeMonitor-Setup.exe`
- Release zip: `dist\FrameScopeMonitor-Installer.zip`
- Embedded payload zip: `dist\FrameScopeMonitor-installer-source\payload.zip`
- Evidence root: `artifacts\candidate-packaging-20260521`

Hashes:

- `FrameScopeMonitor-Setup.exe`: `F3DF5DBC4DEC33B1CAE81BBF091D2FCE8091743B01C8D0EBBDE421DF51135595`
- `FrameScopeMonitor-Installer.zip`: `5E975C41DAB2BF7F198C68F338927EA27C2CA56C55B997CB28115D2E3610B6C6`
- `payload.zip`: `57E526751E7AE7E734770910F90EED6200E219B8EF674CC375DE51EF5E77B26D`

## 4. Payload Integrity

Payload file presence: PASS.

Checked required payload files:

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
- `frontend\assets\index-BS04C48Z.js`
- `frontend\assets\index-BS04C48Z.js.map`
- `frontend\assets\index-Cah-GVqH.css`

Payload React asset hashes:

- `frontend\index.html`: `9B0FD00B9FA2837A438B2DEC0F5CD928C133B62FC55F76C17BAAEACACB83F8A8`
- `frontend\assets\index-BS04C48Z.js`: `2020222D5471C3595BEAC6FA4B5301C543AB08B687ACDCC8CAAE7F96E7B0DFD8`
- `frontend\assets\index-BS04C48Z.js.map`: `198FA787B00E6359899C7BB7D111DA273CE1789EF5C451FD4973D490DB9E5339`
- `frontend\assets\index-Cah-GVqH.css`: `4B6CED0AF64538FC7ABA7B4598CB1A78494C1D6B1B23692CB9791CE91881347E`

Installer embedded `payload.zip` entry check: PASS. It contains the same frontend files and WebView2 DLLs.

## 5. Source Verification

All required source verification commands passed in this run:

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`: PASS. `npm ci`, typecheck, Vitest 2 files / 7 tests, and Vite build passed.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`: PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`: PASS.
- `.\tests\FrameScopeUiStateTests.exe`: PASS.
- `.\tests\FrameScopeReportProgressTests.exe`: PASS.
- `.\tests\FrameScopeReportManifestTests.exe`: PASS.
- `.\tests\FrameScopeWebBridgeTests.exe`: PASS.
- bundled Node `.\tests\chart-sampling-tests.js`: PASS.
- `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo`: PASS, 0 warnings / 0 errors.
- `"C:\Program Files\Git\cmd\git.exe" diff --check`: PASS with existing LF/CRLF normalization warnings only.

## 6. Packaged Payload Verification

Packaged WinForms fallback: PASS.

- Command source: `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe`
- Args: `--ui-screenshot <path> --ui-page overview`
- Evidence: `artifacts\candidate-packaging-20260521\packaged-winforms-overview-startprocess.png`
- Exit code: `0`
- Screenshot: `1586x992`, `212804` bytes

Packaged WebView2 React live smoke: PASS.

- Evidence: `artifacts\candidate-packaging-20260521\packaged-webview2-live-smoke.json`
- Screenshot set prefix: `artifacts\candidate-packaging-20260521\packaged-webview2-live-smoke-*.png`
- `success=true`
- `frontendPath=...\dist\FrameScopeMonitor-payload\frontend`
- `reactOverviewLoaded=true`
- `reactTargetsLoaded=true`
- `reactReportsLoaded=true`
- `reactSettingsLoaded=true`
- `reactAboutLoaded=true`
- `reportLiveActionSmoke.success=true`
- `bridgeExtensionSmoke.success=true`

Packaged live actions: PASS.

The packaged WebView2 smoke covered:

- `state.snapshot`
- `config.get`
- `config.save`
- `processes.refresh`
- `reports.list`
- `reports.open`
- `reports.openDirectory`
- `reports.regenerate`
- `targets.get`
- `targets.save`
- `monitor.start`
- `monitor.stop`
- `diagnostics.generate`

Reports live action details:

- `reportsListClickOk=true`
- `reportOpenClickOk=true`
- `reportOpenDirectoryClickOk=true`
- `reportRegenerateClickAccepted=true`
- `reportRegenerateClickCompleted=true`

Packaged reduced motion: PASS.

- Evidence: `artifacts\candidate-packaging-20260521\packaged-webview2-reduced-motion-smoke.json`
- `success=true`
- `reducedMotion=true`
- `frontendPath=...\dist\FrameScopeMonitor-payload\frontend`
- Visual spot check: `packaged-webview2-reduced-motion-smoke-transition-targets-reports-02.png` shows a complete Reports page with no old/new page body mix and no blank content area.

Packaged Reports narrow layout: PASS.

- Evidence: `artifacts\candidate-packaging-20260521\packaged-webview2-narrow-900x760-smoke-v2.json`
- Screenshot: `artifacts\candidate-packaging-20260521\packaged-webview2-narrow-900x760-smoke-v2-reports.png`
- Window was resized to `900x760`; captured WebView2 content area was `884x721`.
- Visual spot check: `Size` is readable on one line as `39.3 KB`; it is not vertically wrapped.

## 7. Local Install Update

Install directory:

- `%LOCALAPPDATA%\FrameScopeMonitor`

Update method:

- payload copy, not full interactive installer.

Backup:

- `install-backups\FrameScopeMonitor-payload-copy-20260521-155627`

Important correction:

- A first broad payload copy also copied smoke-generated runtime files from the payload working directory (`artifacts`, `framescope-config.json`, `framescope-watcher.log`).
- This was corrected immediately.
- The install directory runtime `framescope-config.json` and `framescope-watcher.log` were restored from backup.
- The final installed state uses a release-item whitelist: exe, DLLs, `tools`, `frontend`, README, and uninstall command.

Installed WebView2 React smoke: PASS.

- Evidence: `artifacts\candidate-packaging-20260521\installed-webview2-live-smoke.json`
- `success=true`
- `frontendPath=C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\frontend`
- `reportLiveActionSmoke.success=true`
- `bridgeExtensionSmoke.success=true`

Installed WinForms fallback: PASS.

- Evidence: `artifacts\candidate-packaging-20260521\installed-winforms-overview.png`
- Exit code: `0`
- Screenshot: `1586x992`, `212911` bytes

## 8. SHA256 Install Comparison

Result: PASS. Payload and installed files match for all checked release files.

Evidence:

- `artifacts\candidate-packaging-20260521\install-sha256-compare.json`

Checked file groups:

- core exe files
- WebView2 runtime DLLs
- PresentMon tool
- React frontend assets
- README and uninstall command

Not treated as release payload hash targets:

- `framescope-config.json`
- `framescope-history.jsonl`
- `framescope-watcher.log`
- `install.log`

Those runtime files were preserved in the install directory.

## 9. Residual Processes

Required residual process check: PASS.

No residual process found for:

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`
- project-local Vite / esbuild / node from this run

Observed processes not killed:

- Existing `msedge.exe` default-profile browser processes.
- Existing `msedgewebview2.exe` processes for Windows Search and Clash Verge user-data directories.

No remaining WebView2 process command line pointed at this run's temporary `FrameScopeMonitorWebView2` user-data folder.

## 10. Release Recommendation

Packaging validation conclusion:

- 候选通过.

GitHub / Releases:

- From packaging validation, this candidate can move to GitHub push and Release update preparation.
- Do not publish a final GitHub Release until the current dirty worktree is intentionally committed/reviewed and the release notes are written or approved.
- This run did not modify README or release notes because release wording was not requested.
