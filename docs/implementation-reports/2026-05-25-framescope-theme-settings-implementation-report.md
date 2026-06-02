# FrameScope Monitor WebView2 Theme And Settings Implementation Report

Date: 2026-05-25

Source root:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Design input:

`docs\design\2026-05-24-framescope-theme-tray-telemetry-performance-plan.md`

## Scope

This pass implemented only the requested theme, Settings, and config work:

- C# persisted config fields for `ThemeMode`, `CloseWindowBehavior`, `TrayEnabled`, and `CpuTelemetry`.
- TypeScript bridge contract, browser mock preview, Settings draft/save, and tests for the new fields.
- React theme resolution for `light`, `dark`, and `system`.
- Token-driven dark mode through `document.documentElement.dataset.theme`, with dark values centralized in `tokens.css`.
- Settings UI section for appearance and close-window behavior.
- Save-failure behavior that keeps the edited draft and dirty state.
- WebView2 smoke evidence for light/dark/system on Settings, Overview, and Reports.

## Explicit Non-Goals

These items were intentionally not implemented in this pass:

- No C# host tray/minimize behavior was changed.
- No `NotifyIcon`, tray menu, or window-close lifecycle implementation was added.
- No CPU telemetry collection logic was enabled.
- No CPU performance optimization was done.
- No install/update of the local installed app was performed.
- No GitHub push or release publishing was performed.
- `build.ps1` was run only because it was part of the requested verification chain. That script generated `dist\FrameScopeMonitor-Setup.exe` and `dist\FrameScopeMonitor-Full-Setup.exe` as its existing build side effect; this was not treated as packaging/release work for this task.

## Implementation Details

### 1. C# Config Fields And Normalization

Updated `src\core\FrameScopeConfigStore.cs`.

Added defaults:

- `ThemeMode = "system"`
- `CloseWindowBehavior = "minimize-to-tray"`
- `TrayEnabled = true`
- `CpuTelemetry = new FrameScopeCpuTelemetryConfig()`

Added normalization:

- `ThemeMode` accepts only `light`, `dark`, or `system`; invalid values fall back to `system`.
- `CloseWindowBehavior` accepts only `exit` or `minimize-to-tray`; invalid values fall back to `minimize-to-tray`.
- `CpuTelemetry` is created if missing.
- `CpuTelemetry.PerCoreSampleIntervalMs` is normalized to `1000` if missing/invalid, then clamped to `500..5000`.
- `CpuTelemetry.VoltageProvider` accepts only `auto`, `disabled`, `wmi`, or `sensor`; invalid values fall back to `auto`.

Added `FrameScopeCpuTelemetryConfig` fields:

- `CollectPerCoreFrequency`, default `false`
- `CollectCpuVoltage`, default `false`
- `PerCoreSampleIntervalMs`, default `1000`
- `VoltageProvider`, default `auto`

`CollectPerCoreFrequency` and `CollectCpuVoltage` default to `false` because this pass only adds configuration fields and does not enable collection logic.

Also updated:

- `framescope-config.example.json`
- `src\diagnostics\FrameScopeDiagnostics.Sections.cs`
- `tests\FrameScopeConfigStoreTests.cs`
- `tests\FrameScopeWebBridgeTests.cs`

### 2. TypeScript Contract, Mock Preview, And Settings Draft

Updated `src\frontend\src\bridge\contract.ts`:

- `FrameScopeThemeMode = "light" | "dark" | "system"`
- `FrameScopeCloseWindowBehavior = "exit" | "minimize-to-tray"`
- `FrameScopeVoltageProvider = "auto" | "disabled" | "wmi" | "sensor"`
- `FrameScopeCpuTelemetryConfig`
- Added `ThemeMode`, `CloseWindowBehavior`, `TrayEnabled`, and `CpuTelemetry` to `FrameScopeConfig`.

Updated `src\frontend\src\data\mockPreview.ts` so browser mock preview uses the same new config fields.

Updated Settings draft/save flow in `src\frontend\src\pages\SettingsPage.tsx`:

- The draft now includes and saves the new fields through the existing `config.save` path.
- On save failure, the page does not reload over a dirty local draft.
- The dirty state remains visible after failed save.
- The retry action keeps using the current draft.

### 3. React Theme Resolution

Added `src\frontend\src\theme\useFrameScopeTheme.ts`.

Behavior:

- `light` resolves to fixed light mode.
- `dark` resolves to fixed dark mode.
- `system` follows `window.matchMedia("(prefers-color-scheme: dark)")`.
- The resolved value is written to `document.documentElement.dataset.theme`.

Updated `src\frontend\src\App.tsx` to apply the hook from the loaded persisted config:

`useFrameScopeTheme(bridgeState.config.data?.config.ThemeMode)`

### 4. Token-Driven Dark Mode

Updated `src\frontend\src\theme\tokens.css`.

The theme switch is centralized at:

` :root[data-theme="dark"] `

No component-level dark-mode fork was added. The contract test checks that layout/component/page CSS does not define its own `[data-theme="dark"]` or `prefers-color-scheme` branch.

### 5. Settings UI

Added Settings group: `外观与窗口行为`.

Controls:

- `主题`: `浅色`, `深色`, `跟随系统`
- `关闭窗口`: `直接退出`, `退出到托盘`
- `托盘入口`: stored as config only, with helper copy saying this pass does not change host tray behavior.

The current summary panel also shows the selected theme and close-window behavior.

### 6. WebView2 Smoke Harness

Updated `src\app\FrameScopeNativeMonitor.WebHost.cs` smoke harness to capture and assert the theme states for:

- Settings light/dark/system
- Overview light/dark/system
- Reports light/dark/system

This harness change is evidence-only. It overrides the DOM theme during smoke capture and does not implement tray behavior or host lifecycle changes.

## Verification

All commands below were rerun from the source root on 2026-05-25.

### Frontend Verify

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result: PASS

Observed output:

- `npm ci` completed.
- `tsc --noEmit` completed.
- Vitest: `5 passed`, `48 passed`.
- Vite production build completed.

### Build

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Result: PASS

Observed output:

- `Build complete: ...\dist\FrameScopeMonitor-Setup.exe`
- `Full setup complete: ...\dist\FrameScopeMonitor-Full-Setup.exe`

Note: this is the existing behavior of `build.ps1`; no install, GitHub push, or release publishing was done.

### Test Build

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
```

Result: PASS

Observed output:

- `FrameScope tests rebuilt.`

### Config Store Tests

Command:

```powershell
.\tests\FrameScopeConfigStoreTests.exe
```

Result: PASS

Observed output:

- `FrameScopeConfigStoreTests: PASS`

Coverage added in this area:

- default theme/window/tray/CPU telemetry fields
- enum normalization and casing
- CPU telemetry interval/provider normalization
- explicit `TrayEnabled = false` preservation
- target-save merge preserving the new global config fields

### Web Bridge Tests

Command:

```powershell
.\tests\FrameScopeWebBridgeTests.exe
```

Result: PASS

Observed output:

- `FrameScopeWebBridgeTests: PASS`

Coverage added in this area:

- `config.save` round-trips theme/window/tray/CPU telemetry fields
- `targets.save` preserves theme/window/tray/CPU telemetry fields instead of dropping them

### WebView2 Live Smoke

Command:

```powershell
.\dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke.json --web-ui-screenshot artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke.png --web-ui-timeout-ms 120000
```

Result: PASS

Evidence:

`artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke.json`

Key fields:

- `success=true`
- `pageLoaded=true`
- `pageReady=true`
- `smokePayload.success=true`
- `themeSmoke.success=true`
- `settingsLight=true`
- `settingsDark=true`
- `settingsSystem=true`
- `overviewLight=true`
- `overviewDark=true`
- `overviewSystem=true`
- `reportsLight=true`
- `reportsDark=true`
- `reportsSystem=true`
- `resolvedSystemTheme=light`

Theme screenshots:

- `artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke-settings-light.png`
- `artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke-settings-dark.png`
- `artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke-settings-system.png`
- `artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke-overview-light.png`
- `artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke-overview-dark.png`
- `artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke-overview-system.png`
- `artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke-reports-light.png`
- `artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke-reports-dark.png`
- `artifacts\webview2-theme-settings-20260525\live-fresh-final\smoke-reports-system.png`

All listed screenshot files were present and non-empty.

### WebView2 Reduced-Motion Smoke

Command:

```powershell
.\dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke.json --web-ui-screenshot artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke.png --web-ui-timeout-ms 120000
```

Result: PASS

Evidence:

`artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke.json`

Key fields:

- `success=true`
- `reducedMotion=true`
- `pageLoaded=true`
- `pageReady=true`
- `smokePayload.success=true`
- `themeSmoke.success=true`
- `settingsLight=true`
- `settingsDark=true`
- `settingsSystem=true`
- `overviewLight=true`
- `overviewDark=true`
- `overviewSystem=true`
- `reportsLight=true`
- `reportsDark=true`
- `reportsSystem=true`
- `resolvedSystemTheme=light`

Theme screenshots:

- `artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke-settings-light.png`
- `artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke-settings-dark.png`
- `artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke-settings-system.png`
- `artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke-overview-light.png`
- `artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke-overview-dark.png`
- `artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke-overview-system.png`
- `artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke-reports-light.png`
- `artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke-reports-dark.png`
- `artifacts\webview2-theme-settings-20260525\reduced-motion-fresh-final\smoke-reports-system.png`

All listed screenshot files were present and non-empty.

### Git Diff Check

Command:

```powershell
git diff --check
```

Result: PASS

Observed output:

- Exit code `0`.
- Git printed LF/CRLF working-copy warnings only.
- No whitespace error was reported.

### Residual Process Check

Command:

```powershell
$repo = (Resolve-Path '.').Path; $artifact = Join-Path $repo 'artifacts\webview2-theme-settings-20260525'; $selfPid = $PID; $matches = Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $selfPid } | Where-Object { $name = $_.Name; $cmd = [string]$_.CommandLine; ($name -in @('FrameScopeMonitor.exe','FrameScopeProcessSampler.exe','FrameScopeSystemSampler.exe','PresentMon-2.4.1-x64.exe','FakeGame.exe','FakePresentMon.exe')) -or ($cmd -like "*$repo*" -and $cmd -match 'FrameScopeMonitor|FrameScopeProcessSampler|FrameScopeSystemSampler|PresentMon|webview2-theme-settings|--web-ui-smoke') -or ($cmd -like "*$artifact*") } | Select-Object ProcessId,Name,CommandLine; if ($matches) { $matches | Format-List; exit 1 } else { 'NO_MATCHING_RESIDUAL_PROCESSES' }
```

Result: PASS

Observed output:

- `NO_MATCHING_RESIDUAL_PROCESSES`

## Files Changed For This Scope

Primary implementation files:

- `framescope-config.example.json`
- `src\core\FrameScopeConfigStore.cs`
- `src\diagnostics\FrameScopeDiagnostics.Sections.cs`
- `src\app\FrameScopeNativeMonitor.WebHost.cs`
- `src\frontend\src\App.tsx`
- `src\frontend\src\bridge\contract.ts`
- `src\frontend\src\data\mockPreview.ts`
- `src\frontend\src\pages\SettingsPage.tsx`
- `src\frontend\src\pages\pages.css`
- `src\frontend\src\theme\tokens.css`
- `src\frontend\src\theme\useFrameScopeTheme.ts`

Primary tests:

- `src\frontend\src\data\mockPreview.test.ts`
- `src\frontend\src\uiDesignContract.test.ts`
- `src\frontend\src\uiInteractionContract.test.ts`
- `tests\FrameScopeConfigStoreTests.cs`
- `tests\FrameScopeWebBridgeTests.cs`

Report:

- `docs\implementation-reports\2026-05-25-framescope-theme-settings-implementation-report.md`

The repository already had unrelated modified and untracked files before this final report step. They were not reverted and are not treated as part of this scoped implementation.

## Current Status

PASS for the requested implementation scope.

The config schema, TypeScript contract, mock preview, Settings draft/save, token-driven theme resolution, WebView2 live smoke, reduced-motion smoke, required screenshots, and residual process check have all been verified. Host tray behavior and CPU telemetry collection remain intentionally unimplemented for a later scoped pass.
