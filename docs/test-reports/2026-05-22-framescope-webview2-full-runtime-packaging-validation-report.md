# FrameScope Monitor WebView2 Full Runtime Packaging Validation Report

Date: 2026-05-22
Role: WebView2 Runtime packaging owner
Conclusion: PASS

## 1. Current Conclusion

候选通过。

This round adds and validates `FrameScopeMonitor-Full-Setup.exe` while preserving the existing `FrameScopeMonitor-Setup.exe`.

No UI design, C# bridge semantics, backend sampling, report generation, GameLite, WMI, or SGuard behavior was changed.

## 2. Packaging Outputs

- `dist\FrameScopeMonitor-Setup.exe`
  - Size: `1245184`
  - SHA256: `AB483C71C349A1B69AE876B0553D3BA7FCCA364BD6167DC3531D5A58B4AA70D0`
- `dist\FrameScopeMonitor-Full-Setup.exe`
  - Size: `200423936`
  - SHA256: `806CF3DF8AE8FD2F03257FDF999064E256D4DDC320DEEF6944F67DFEC5845D98`
- `dist\FrameScopeMonitor-Installer.zip`
  - Size: `201599865`
  - SHA256: `1DB395F98A14CBC945C2A99264FB2FEB2F8B796FDC5F4611D3A79ECE673F4F07`
- `dist\FrameScopeMonitor-LegacyCleanup.exe`
  - Size: `29184`
  - SHA256: `96B5DF9D2663CAF569AC56D7CEE1BFA08C6640C9EA2F002CB0C735811AD5F974`

Bundled WebView2 Runtime installer:

- `packaging\MicrosoftEdgeWebView2RuntimeInstallerX64.exe`
  - Official x64 standalone fwlink resolved from `https://go.microsoft.com/fwlink/?linkid=2124701`
  - Size: `199178960`
  - SHA256: `E8464B185B4786F43E9C7357EEA6A0E64F25B1E3BF841E1DB0F7A0E9E8A9D090`

Resource check:

- `FrameScopeMonitor-Setup.exe`: `FrameScopePayload`
- `FrameScopeMonitor-Full-Setup.exe`: `FrameScopePayload`, `FrameScopeWebView2RuntimeInstaller`

## 3. Source Changes

Changed source intentionally:

- `src\app\FrameScopeWebView2Runtime.cs`
  - Shared WebView2 Runtime detection helper.
  - Checks WebView2 Runtime `pv` under EdgeUpdate Clients for HKLM/HKCU and 64/32-bit views.
  - Uses the WebView2 API version check as fallback when available.
  - Ignores Edge browser client registration.
  - Supports test simulation through `FRAMESCOPE_WEBVIEW2_RUNTIME_TEST_MODE`.
- `src\app\FrameScopeNativeMonitor.cs`
  - Startup guard before WebView2 UI launch.
  - Missing Runtime shows the required Chinese message and exits instead of blank/crashing.
  - Adds `--webview2-runtime-self-test` evidence mode for validation.
- `packaging\FrameScopeSetupNative.cs`
  - Keeps normal installer behavior.
  - Full installer detects Runtime and runs bundled installer only when missing.
  - Silent command is `MicrosoftEdgeWebView2RuntimeInstallerX64.exe /silent /install`.
  - Adds `/quiet` installer mode for automated local installer verification.
- `build.ps1`
  - Builds both normal and Full setup executables.
  - Downloads/reuses official x64 standalone Runtime installer.
  - Embeds Runtime installer only in the Full setup.
  - Includes Full setup in `FrameScopeMonitor-Installer.zip`.
- `tests\FrameScopeWebView2RuntimeTests.cs`, `tests\Build-FrameScopeTests.ps1`
  - Covers HKLM/HKCU `pv`, invalid `pv`, Edge browser false positive, test overrides, Full installer decision, and exact Chinese message.
- `README.md`, `packaging\README-FrameScopeMonitor.txt`, `docs\implementation-reports\2026-05-21-framescope-webview2-github-release-notes.md`
  - Adds normal vs Full installer guidance and final SHA256.

## 4. Payload Integrity

`dist\FrameScopeMonitor-payload` contains all required release items:

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
- `frontend\assets\*` (`3` files)
- `README-FrameScopeMonitor.txt`
- `Uninstall-FrameScopeMonitor.cmd`

## 5. Runtime Detection Validation

Automated test result:

- `FrameScopeWebView2RuntimeTests.exe`: PASS.

Covered cases:

- Detects Runtime from HKLM `pv`.
- Detects Runtime from HKCU `pv`.
- Ignores empty and `0.0.0.0` `pv`.
- Does not treat Edge browser client `{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}` as WebView2 Runtime.
- Simulates missing/installed Runtime.
- Full installer installs bundled Runtime only when Runtime is missing.
- Startup missing Runtime message matches the required Chinese text.

Manual simulation:

- `FRAMESCOPE_WEBVIEW2_RUNTIME_TEST_MODE=missing`
  - `FrameScopeMonitor.exe --webview2-runtime-self-test`
  - Exit code: `3`
  - Evidence starts with `missing:` and contains the required Chinese message.
- `FRAMESCOPE_WEBVIEW2_RUNTIME_TEST_MODE=installed`
  - Exit code: `0`
  - Evidence: `available:test-installed`

Current machine Runtime:

- `HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`
- `pv=148.0.3967.70`

## 6. Installer Validation

Release refresh:

- Version bumped to `v1.1.3`.
- Rebuilt `FrameScopeMonitor-Setup.exe`, `FrameScopeMonitor-Full-Setup.exe`, and `FrameScopeMonitor-Installer.zip`.
- `FrameScopeMonitor-Full-Setup.exe` still embeds `FrameScopeWebView2RuntimeInstaller`.

Normal installer:

- Ran `dist\FrameScopeMonitor-Setup.exe /quiet`.
- Installed successfully.
- Install log showed:
  - `available=True`
  - `version=148.0.3967.70`
  - `fullPackage=False`
- Normal installer did not include or run the WebView2 Runtime installer.

Full installer:

- Ran `dist\FrameScopeMonitor-Full-Setup.exe /quiet`.
- Installed successfully.
- Install log showed:
  - `available=True`
  - `version=148.0.3967.70`
  - `fullPackage=True`
- Because Runtime was already present, Full installer skipped Runtime installation.
- Runtime registry `pv` before and after Full install remained `148.0.3967.70`.

Uninstall safety:

- Ran `%LOCALAPPDATA%\FrameScopeMonitor\FrameScopeUninstaller.exe /quiet`.
- Install directory was removed.
- WebView2 Runtime registry `pv` remained unchanged.
- This confirms uninstall does not remove user/system WebView2 Runtime.

Local install final update method:

- Full installer `/quiet`, not payload copy.
- Final install directory: `%LOCALAPPDATA%\FrameScopeMonitor`
- Registry after restore:
  - `DisplayVersion=1.1.3`
  - `InstallLocation=%LOCALAPPDATA%\FrameScopeMonitor`
  - `DisplayIcon=%LOCALAPPDATA%\FrameScopeMonitor\FrameScopeMonitor.exe`

## 7. Installed SHA256 Sync

After final Full installer restore, payload and installed directory hashes matched for:

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
- `tools\PresentMon-2.4.1-x64.exe`
- `frontend\index.html`
- `frontend\assets\index-6fiQLqQg.css`
- `frontend\assets\index-D0pIZWdG.js`
- `frontend\assets\index-D0pIZWdG.js.map`

Mismatch count: `0`.

## 8. WebView2 Smoke Results

Installed normal-package smoke after normal installer:

- `artifacts\webview2-full-runtime-packaging\installed-normal-smoke.json`
- Exit code: `0`
- `success=true`
- `frontendPath=%LOCALAPPDATA%\FrameScopeMonitor\frontend`
- live actions success: `true`
- bridge smoke success: `true`

Installed Full-package smoke after Full installer:

- `artifacts\webview2-full-runtime-packaging\installed-full-smoke.json`
- Exit code: `0`
- `success=true`
- `frontendPath=%LOCALAPPDATA%\FrameScopeMonitor\frontend`
- live actions success: `true`
- bridge smoke success: `true`

Installed Full-package reduced-motion smoke:

- `artifacts\webview2-full-runtime-packaging\installed-full-reduced-motion-smoke.json`
- Exit code: `0`
- `success=true`
- `reducedMotion=true`
- live actions success: `true`
- bridge smoke success: `true`

Final restored Full install smoke:

- `artifacts\release-v1.1.3\installed-final-full-webview2-smoke.json`
- Exit code: `0`
- `success=true`
- `frontendPath=%LOCALAPPDATA%\FrameScopeMonitor\frontend`
- live actions success: `true`
- bridge smoke success: `true`

Result:

- Default entry is WebView2 React UI.
- No `--web-ui` is needed.
- No old WinForms UI fallback was observed.

## 9. Command Validation

| Command | Result |
| --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS |
| `.\tests\FrameScopeReportProgressTests.exe` | PASS |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS |
| `.\tests\FrameScopeWebBridgeTests.exe` | PASS |
| `.\tests\FrameScopeWebView2RuntimeTests.exe` | PASS |
| bundled Node `.\tests\chart-sampling-tests.js` | PASS |
| `dotnet build .\tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj -c Release --nologo` | PASS, `0` warnings, `0` errors |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS; only LF-to-CRLF working-copy warnings |

## 10. Residual Process Check

Checked:

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`
- `Vite`
- `esbuild`
- test WebView2 user-data related processes

Result:

- No FrameScope/test residual processes found.
- Codex-owned `node.exe` / `node_repl.exe` processes are still present and unrelated to FrameScope.
- Existing `msedgewebview2.exe` processes belong to Windows Search (`SearchHost.exe`) and Clash Verge (`clash-verge.exe`), not FrameScope.

## 11. Release Recommendation

建议提交并推送 GitHub，但不要自动发布 GitHub Release，除非 release owner 明确授权。

Release notes should mention:

- `FrameScopeMonitor-Setup.exe`: normal installer for most systems with WebView2 Runtime already present.
- `FrameScopeMonitor-Full-Setup.exe`: full offline installer for no-WebView2, offline, or stripped Windows systems.
- `FrameScopeMonitor-Full-Setup.exe` includes Microsoft Edge WebView2 Evergreen Standalone Installer x64 and skips Runtime install when already present.
- Keep SHA256 values listed above.
