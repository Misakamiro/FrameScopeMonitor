# FrameScope Monitor Local Install Update Report - 2026-05-23

Status: PASS

Scope: local install update only. This run did not push GitHub, did not update a GitHub Release, did not edit README, and did not perform full release acceptance.

## Local Install Directory

Installed app directory:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor
```

User launch path:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe
```

## Build And Installer

Commands run from:

```text
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d
```

Frontend verification:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result: PASS. `npm ci`, TypeScript, Vitest, and Vite production build completed. Vitest reported 5 test files and 41 tests passed. New built frontend assets were:

```text
src\frontend\dist\assets\index-BbPECq-O.css
src\frontend\dist\assets\index-DXu2r1MM.js
src\frontend\dist\assets\index-DXu2r1MM.js.map
```

Package build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Result: PASS. Generated:

```text
dist\FrameScopeMonitor-Setup.exe
dist\FrameScopeMonitor-Full-Setup.exe
dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe
dist\FrameScopeMonitor-payload\frontend\index.html
dist\FrameScopeMonitor-payload\frontend\assets\*
```

Installer hashes:

| File | SHA256 |
| --- | --- |
| `dist\FrameScopeMonitor-Setup.exe` | `55EC97BD6FADEB1F20AF3F79F6ECCB95C403943A3A2C30D232000180E3003B4E` |
| `dist\FrameScopeMonitor-Full-Setup.exe` | `793F31E2D9599E583F4BDF50AD000852C2A743917B81C06187A6DDE08DF6BFD9` |

## Installation

Installer used:

```text
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\dist\FrameScopeMonitor-Full-Setup.exe
```

Command:

```powershell
.\dist\FrameScopeMonitor-Full-Setup.exe /quiet
```

Result: PASS. Full installer quiet install completed and wrote:

```text
SUCCESS 安装完成。软件已安装到：C:\Users\misakamiro\AppData\Local\FrameScopeMonitor
数据和报告目录：C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs
```

Note: the installer auto-started `FrameScopeMonitor.exe` after installation even with `/quiet`. I recorded and closed the installer-started process:

| PID | Path |
| --- | --- |
| `39448` | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe` |

There were no project `FrameScopeMonitor.exe` processes before installation.

## Payload Hash Comparison

All required current payload files matched the installed files:

| Relative file | Payload SHA256 | Installed SHA256 | Result |
| --- | --- | --- | --- |
| `FrameScopeMonitor.exe` | `14C94ADA7EA84576FD326F3ADFA446F53BC95A485FDC0DFEEEF1DF24E0DAD968` | `14C94ADA7EA84576FD326F3ADFA446F53BC95A485FDC0DFEEEF1DF24E0DAD968` | MATCH |
| `WebView2Loader.dll` | `BB5FDA5F2D3AAE5E0977AB28D8A8A00ACFD63D73748AAAC32DAF4FC6645B6677` | `BB5FDA5F2D3AAE5E0977AB28D8A8A00ACFD63D73748AAAC32DAF4FC6645B6677` | MATCH |
| `frontend\index.html` | `4211917C9DEBD336014988B3AE69F24668506FDA5B8650C7B6989F0AF6074E69` | `4211917C9DEBD336014988B3AE69F24668506FDA5B8650C7B6989F0AF6074E69` | MATCH |
| `frontend\assets\index-BbPECq-O.css` | `6C695D7C69645BF50BD42F06F88CE3C40F4738EC2BA80C65902E86F8471D862E` | `6C695D7C69645BF50BD42F06F88CE3C40F4738EC2BA80C65902E86F8471D862E` | MATCH |
| `frontend\assets\index-DXu2r1MM.js` | `B01869ECBB625A930173DB8839300DD7E5EB6BCE4037F0437ACAA32BCE8AADEC` | `B01869ECBB625A930173DB8839300DD7E5EB6BCE4037F0437ACAA32BCE8AADEC` | MATCH |
| `frontend\assets\index-DXu2r1MM.js.map` | `C95262A2A883052C9E64AA5B3723A4CB802D7C9902BAAC415BCA1AC456484CFE` | `C95262A2A883052C9E64AA5B3723A4CB802D7C9902BAAC415BCA1AC456484CFE` | MATCH |

Installed `frontend\assets` still contains older hashed asset files from previous installs. The current installed `frontend\index.html` matches payload and points to the new assets:

```html
<script type="module" crossorigin src="/assets/index-DXu2r1MM.js"></script>
<link rel="stylesheet" crossorigin href="/assets/index-BbPECq-O.css">
```

## Installed WebView2 Smoke

Command run from source root:

```powershell
%LOCALAPPDATA%\FrameScopeMonitor\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\local-install-update-20260523\webview2-installed-smoke.json --web-ui-screenshot artifacts\local-install-update-20260523\webview2-installed-smoke.png --web-ui-timeout-ms 120000
```

Result: PASS.

Key evidence from `artifacts\local-install-update-20260523\webview2-installed-smoke.json`:

| Field | Value |
| --- | --- |
| `success` | `true` |
| `pageReady` | `true` |
| `usingReactFrontend` | `true` |
| `frontendPath` | `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\frontend` |
| `elapsedMs` | `5550` |

Main smoke screenshot:

```text
artifacts\local-install-update-20260523\webview2-installed-smoke.png
```

## Latest UI Screenshot Checks

Additional installed-frontend screenshots were captured through a short-lived static server pointed at:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\frontend
```

Evidence summary:

| Check | Evidence | Result |
| --- | --- | --- |
| compact sidebar | `artifacts\local-install-update-20260523\installed-compact-sidebar-900x760.png` | PASS: sidebar width `84`, active nav `reports`, focused nav `settings` |
| Reports more menu | `artifacts\local-install-update-20260523\installed-reports-more-menu-1280x720.png` | PASS: menu open, role `menu`, 3 menu items |
| Targets process search / 250 results | `artifacts\local-install-update-20260523\installed-targets-250-results-1280x720.png` | PASS: 250 rows, internal result-list scrolling works |
| Settings long path | `artifacts\local-install-update-20260523\installed-settings-long-path-1280x720.png` | PASS: dedicated path preview visible; long input remains horizontally scrollable |

Raw UI evidence:

```text
artifacts\local-install-update-20260523\installed-ui-evidence.json
```

The temporary Edge profile used for these screenshots was removed after capture.

## User Data Preservation

No user configuration, log, or report directory was deleted.

Observed preserved paths:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-config.json
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-watcher.log
C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData
C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs
```

Config content hash before install and after smoke remained the same:

```text
0DDFAA55208E6EFD02CA317CDFCA6519D01BD5903246421D3DF2241C247E324E
```

The watcher log was retained and appended during smoke. The WebView2 smoke also exercised the existing report/diagnostics smoke path, so it generated diagnostic evidence and report-regeneration activity under the existing data root. That is smoke harness behavior, not installer deletion or payload copying.

## Cleanup And Residual Process Check

Removed test-only temporary folders:

```text
C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\local-install-update-20260523\installed-edge-cdp-profile
C:\Users\misakamiro\AppData\Local\Temp\FrameScopeMonitorWebView2
```

Final process check result: PASS, count `0`.

Checked for project-related residual:

```text
FrameScopeMonitor.exe
FrameScopeProcessSampler.exe
FrameScopeSystemSampler.exe
FrameScopeReportGenerator.exe
PresentMon-2.4.1-x64.exe
PresentMon.exe
FakePresentMon.exe
TslGame.exe
GameLite.exe
node.exe / esbuild.exe tied to this repo or this evidence run
msedge.exe / msedgewebview2.exe tied to this repo, install dir, WebView2 smoke, or this evidence run
```

No long-running FrameScope, PresentMon, sampler, report generator, fake game, Vite, esbuild, project Node, Edge CDP, or test WebView2 user-data process was left running.

## Final Notes

The local installed app has been updated through the Full installer quiet path and points at the newly built React frontend. The user can manually test with:

```text
C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe
```
