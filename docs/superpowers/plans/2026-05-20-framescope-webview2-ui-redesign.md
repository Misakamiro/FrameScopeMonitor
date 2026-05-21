# FrameScope WebView2 UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move FrameScope Monitor's future UI direction from WinForms self-drawn screens to a WebView2-hosted React/TypeScript frontend, while keeping the current C# monitoring backend and migrating in controlled phases.

**Architecture:** C# remains the backend Module for monitoring, sampling, PresentMon orchestration, config persistence, report generation, diagnostics, process control, and file operations. WebView2 becomes the UI host Module, and a typed JSON bridge is the seam between React and existing C# adapters. The current WinForms UI stays intact until the WebView2 UI reaches verified functional parity.

**Tech Stack:** C#/.NET Windows desktop, Microsoft Edge WebView2 Runtime, React, TypeScript, Vite, Framer Motion, CSS token system, existing FrameScope C# source modules and tests.

---

## Current Conclusion

WebView2 + React/TypeScript is suitable for FrameScope Monitor's next UI architecture.

The current WinForms UI has been split into useful Modules, but the visual layer is still constrained by self-drawn controls, DataGridView styling, manual animation, and duplicated layout code. A macOS-inspired visual system with glass surfaces, spring motion, page transitions, and token-driven styling is significantly easier and safer in a web frontend than by continuing to patch WinForms controls.

Electron is not the recommended path here because the app already has a C# backend and installer. Electron would add a second desktop runtime, a Node process model, and more packaging surface without improving the existing C# integration. Tauri is also not recommended in this stage because it introduces Rust tooling, which the user has not approved.

## Existing Structure Findings

Read and checked:

- `docs\orchestration\FrameScopeMonitor-Orchestrator-Role.md`
- `docs\orchestration\FrameScopeMonitor-Handoff-2026-05-14.md`
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\modules\software-ui.md`
- `docs\modules\ui-interactions.md`
- `docs\modules\backend-monitoring.md`
- `docs\FrameScopeMonitor-progress.md`
- `docs\FrameScopeMonitor-next-prompt.md`
- `README.md`
- `AGENTS.md`
- `build.ps1`
- `packaging\`
- `src\app\`
- `src\ui\`

Observed build/package shape:

- `build.ps1` compiles with .NET Framework `csc.exe`, then copies payload files into `dist\FrameScopeMonitor-payload`, embeds them into `dist\FrameScopeMonitor-Setup.exe`, and creates `dist\FrameScopeMonitor-Installer.zip`.
- The main app compile list is explicit. Adding C# files to the shipped app later makes `build.ps1` an exclusive file.
- Packaging currently copies executables, `tools\PresentMon-2.4.1-x64.exe`, uninstaller command, and README. A real web UI migration must add frontend build output to the payload only after the bridge and host are stable.
- `src\app` owns the main WinForms app, routing, watcher controls, monitor sessions, report orchestration, report opening, status refresh, screenshot harness, and page partials.
- `src\ui` owns visual controls, theme, motion helpers, live data readers, report page layout/actions/details, UI state rules, and sidebar drawing.
- Existing docs already mark `build.ps1`, `UiRouting.cs`, `UiWatcherControls.cs`, `UiProcessCleanup.cs`, report open/status files, report generator entry files, and shared UI theme files as high-conflict or exclusive.

## Spike Added In This Stage

Created a standalone experiment under:

- `tools\WebView2Spike\WebView2Spike.csproj`
- `tools\WebView2Spike\Program.cs`
- `tools\WebView2Spike\wwwroot\index.html`

What it proves:

- A WinForms-hosted WebView2 control can load a local frontend asset.
- C# can send a JSON message to JavaScript with `CoreWebView2.PostWebMessageAsJson`.
- JavaScript can reply to C# with `window.chrome.webview.postMessage`.
- The smoke command can write evidence JSON and capture a PNG preview.

What it deliberately does not do:

- It does not replace or delete the current WinForms UI.
- It does not change `build.ps1`.
- It does not migrate any page.
- It does not call monitoring, reporting, GameLite, WMI, SGuard, sampler, or PresentMon logic.
- It is not a static fake UI deliverable; it is only a transport and host proof.

Smoke command:

```powershell
dotnet run --project .\tools\WebView2Spike\WebView2Spike.csproj -- --smoke --evidence .\artifacts\webview2-spike\roundtrip.json --screenshot .\artifacts\webview2-spike\roundtrip.png --timeout-ms 15000
```

Expected evidence:

- `success = true`
- `pageLoaded = true`
- `pageReady = true`
- `roundTripComplete = true`
- message log contains `host->js` and `js->host`
- screenshot path points to `artifacts\webview2-spike\roundtrip.png`

## Target Directory Design

Do not create this full tree in the spike stage. This is the proposed target for later implementation.

```text
src\
  app\
    FrameScopeNativeMonitor.WebHost.cs
    FrameScopeWebBridge.cs
    FrameScopeWebBridge.Config.cs
    FrameScopeWebBridge.Processes.cs
    FrameScopeWebBridge.Monitoring.cs
    FrameScopeWebBridge.Reports.cs
    FrameScopeWebBridge.Events.cs
  frontend\
    package.json
    package-lock.json
    tsconfig.json
    vite.config.ts
    index.html
    src\
      main.tsx
      App.tsx
      bridge\
        contract.ts
        client.ts
        useBridgeEvent.ts
      theme\
        tokens.css
        motion.ts
      layout\
        AppShell.tsx
        Sidebar.tsx
        StatusBar.tsx
      pages\
        OverviewPage.tsx
        LivePage.tsx
        TargetsPage.tsx
        ReportsPage.tsx
        SettingsPage.tsx
      components\
        GlassPanel.tsx
        MetricCard.tsx
        ActionButton.tsx
        StatusPill.tsx
        ReportProgress.tsx
        DataTable.tsx
        EmptyState.tsx
```

Recommended hosted asset location after `npm run build`:

```text
dist\FrameScopeMonitor-payload\frontend\
```

Recommended runtime mapping:

- Development: WebView2 loads `http://localhost:<vite-port>` only when an explicit developer flag is used.
- Production: WebView2 uses `CoreWebView2.SetVirtualHostNameToFolderMapping("app.framescope.local", "<payload>\frontend", DenyCors)` and navigates to `https://app.framescope.local/index.html`.
- The host should reject arbitrary external navigation and only allow the local virtual host plus report files opened by existing report-open logic.

## Build Flow Design

Stage 1, current spike:

- Do not modify `build.ps1`.
- Build the spike only with `dotnet run --project tools\WebView2Spike`.

Stage 2, real WebView2 host behind flag:

- Add WebView2 package/assembly strategy for the shipped app.
- Add a `--web-ui` command-line flag or feature setting so the old WinForms shell remains the default fallback.
- Keep the current WinForms screenshot harness intact.
- `build.ps1` becomes exclusive because it must compile the host files and copy frontend assets.

Stage 3, frontend build:

- Add `src\frontend\package.json`, Vite, React, TypeScript, and Framer Motion.
- Add `npm ci` and `npm run build` to the documented developer flow.
- Only after the WebView2 host works, update `build.ps1` to copy `src\frontend\dist\*` into the payload.
- If Node is unavailable, fail early with a clear build error instead of packaging a missing UI.

Stage 4, release packaging:

- Verify payload contains `frontend\index.html`, JS/CSS assets, and WebView2 host executable dependencies.
- Verify installer installs frontend files under `%LOCALAPPDATA%\FrameScopeMonitor\frontend`.
- Do not publish or package from the spike stage.

## Bridge API Design

Use WebView2 web messages. Do not expose broad host objects to JavaScript.

Request envelope from React to C#:

```json
{
  "id": "uuid-or-monotonic-id",
  "type": "config.get",
  "payload": {},
  "sentAt": "2026-05-20T12:00:00.000Z"
}
```

Response envelope from C# to React:

```json
{
  "id": "same-id",
  "type": "config.get.result",
  "ok": true,
  "data": {},
  "error": null
}
```

Push event envelope from C# to React:

```json
{
  "type": "event.reportProgress",
  "data": {
    "phase": "reading-data",
    "percent": 42,
    "message": "Reading frame data"
  },
  "sentAt": "2026-05-20T12:00:00.000Z"
}
```

Required request types:

| Type | Direction | C# owner | Notes |
|---|---|---|---|
| `config.get` | React -> C# | `FrameScopeWebBridge.Config.cs` | Reads `FrameScopeConfigStore.Load(ConfigPath)` and returns normalized config plus enabled target count. |
| `config.save` | React -> C# | `FrameScopeWebBridge.Config.cs` | Validates through `FrameScopeConfigStore.BuildConfigFromEditableTargets` and `Normalize`; returns reloaded config. |
| `processes.refresh` | React -> C# | `FrameScopeWebBridge.Processes.cs` | Uses safe process enumeration rules from current process picker; errors are per-process, not fatal. |
| `monitor.start` | React -> C# | `FrameScopeWebBridge.Monitoring.cs` | Calls the same watcher start adapter as the WinForms button path. Must guard in-flight state. |
| `monitor.stop` | React -> C# | `FrameScopeWebBridge.Monitoring.cs` | Calls the same stop adapter and preserves process cleanup safety rules. |
| `reports.history` | React -> C# | `FrameScopeWebBridge.Reports.cs` | Reads history/run folders and returns list/detail state. |
| `reports.open` | React -> C# | `FrameScopeWebBridge.Reports.cs` | Opens selected report through existing report-open adapter. |
| `reports.openDirectory` | React -> C# | `FrameScopeWebBridge.Reports.cs` | Opens selected run directory through existing safe path open adapter. |
| `reports.regenerate` | React -> C# | `FrameScopeWebBridge.Reports.cs` | Calls existing completed-run report generation path; reports progress through events. |
| `diagnostics.generate` | React -> C# | `FrameScopeWebBridge.Reports.cs` | Calls `FrameScopeDiagnostics.GenerateReport` for selected or latest run. |
| `state.snapshot` | React -> C# | `FrameScopeWebBridge.Events.cs` | Returns watcher status, active page status, latest report progress, and current bridge health. |

Required push event types:

| Event | Data |
|---|---|
| `event.status` | watcher pid/running, active target, last error, enabled target count |
| `event.reportProgress` | phase, percent, ETA, can retry, selected run |
| `event.error` | bridge error code, localized message, recovery action |
| `event.reportsChanged` | latest report path, history count, selected report validity |
| `event.processesRefreshed` | process count and refresh timestamp |

Bridge invariants:

- Every request gets exactly one response.
- Mutating requests are serialized per action group: config, monitor, report generation, process cleanup.
- Long actions immediately return accepted/in-flight status and continue progress through events.
- All file paths are resolved and validated on the C# side; React never gets authority to execute arbitrary paths.
- The bridge is the external seam. Existing C# monitoring and reporting modules stay behind it.
- React receives localized display messages, but durable state values should also include stable English enum codes.

## macOS-Inspired UI Design System Direction

This is inspired by modern desktop OS quality, not a copy of Apple branding, icons, or protected assets.

Primitive tokens:

- Background: deep neutral graphite, blue-black, soft off-white text.
- Materials: translucent dark glass, elevated glass, solid fallback surfaces for reduced transparency.
- Accents: cool blue primary, green success, amber warning, red danger, violet diagnostics.
- Radius: 10, 14, 18, 22 for panels and controls; keep dense tables tighter.
- Shadow: soft layered shadows with low opacity, never neon-heavy.
- Blur: use only for hierarchy and shell material, with a non-blur fallback.

Semantic tokens:

- `--surface-window`
- `--surface-sidebar`
- `--surface-glass`
- `--surface-elevated`
- `--text-primary`
- `--text-secondary`
- `--text-muted`
- `--state-running`
- `--state-warning`
- `--state-error`
- `--state-diagnostics`
- `--focus-ring`

Component tokens:

- Sidebar material, active nav item, glass panel, metric card, data table row, report progress, action button, toggle, modal sheet, toast, chart surface.

Typography:

- Use a system UI stack for desktop fit: `-apple-system`, `BlinkMacSystemFont`, `"Segoe UI"`, `"Microsoft YaHei UI"`, sans-serif.
- Use tabular numeric styling for FPS, frame time, percent, process CPU/memory values.
- Do not use viewport-scaled fonts or negative letter spacing.

Motion:

- Use Framer Motion spring transitions for route changes, panel entry, button press, and status changes.
- Standard spring: stiffness 320, damping 30, mass 0.8.
- Larger route transition: stiffness 220, damping 26, mass 0.9.
- Prefer transform and opacity; do not animate layout-heavy width/height during live monitoring.
- Respect `prefers-reduced-motion`; disable page slides and keep opacity-only feedback.
- Active capture mode should reduce decorative motion and prioritize stable readings.

UX rules:

- All functional controls remain wired to real C# logic.
- Buttons show in-flight, success, and failure states.
- Empty states must say why data is missing and what action can produce it.
- Icon set should be Lucide or another single consistent SVG family.
- No Apple logos, SF Symbols copies, Finder/System Settings icon copies, or protected macOS screenshots.

## Migration Plan

### Task 1: Keep The Spike Isolated

**Files:**
- Create: `tools\WebView2Spike\WebView2Spike.csproj`
- Create: `tools\WebView2Spike\Program.cs`
- Create: `tools\WebView2Spike\wwwroot\index.html`
- Create: `docs\superpowers\plans\2026-05-20-framescope-webview2-ui-redesign.md`

- [ ] Run the WebView2 smoke command and verify message round trip evidence.
- [ ] Run the existing build/test chain to prove no regression in current WinForms app.
- [ ] Keep `build.ps1` unchanged in this task.

### Task 2: Add A Real Host Behind A Flag

**Files:**
- Create: `src\app\FrameScopeNativeMonitor.WebHost.cs`
- Create: `src\app\FrameScopeWebBridge.cs`
- Modify: `src\app\FrameScopeNativeMonitor.cs`
- Modify: `build.ps1`

- [ ] Add `--web-ui` to `Main`.
- [ ] When `--web-ui` is absent, keep the current WinForms flow exactly as it is.
- [ ] When `--web-ui` is present, create a WebView2 form and load a bundled static `index.html`.
- [ ] Add a bridge health request `state.snapshot`.
- [ ] Build and run both `FrameScopeMonitor.exe` and `FrameScopeMonitor.exe --web-ui`.

### Task 3: Add React/Vite Frontend Shell

**Files:**
- Create: `src\frontend\package.json`
- Create: `src\frontend\vite.config.ts`
- Create: `src\frontend\tsconfig.json`
- Create: `src\frontend\index.html`
- Create: `src\frontend\src\main.tsx`
- Create: `src\frontend\src\App.tsx`
- Create: `src\frontend\src\theme\tokens.css`
- Create: `src\frontend\src\layout\AppShell.tsx`

- [ ] Build the shell with sidebar, title bar, content area, and report-progress zone.
- [ ] Keep data read-only in this task.
- [ ] Load the built frontend through WebView2 production mapping.
- [ ] Verify route transitions, reduced motion, keyboard focus, and no horizontal overflow.

### Task 4: Implement Read-Only Bridge State

**Files:**
- Create: `src\app\FrameScopeWebBridge.Config.cs`
- Create: `src\app\FrameScopeWebBridge.Processes.cs`
- Create: `src\app\FrameScopeWebBridge.Reports.cs`
- Create: `src\frontend\src\bridge\contract.ts`
- Create: `src\frontend\src\bridge\client.ts`

- [ ] Implement `config.get`, `processes.refresh`, `reports.history`, and `state.snapshot`.
- [ ] Render Overview, Targets, Reports, and Settings from real read-only data.
- [ ] Add error responses for bad request types and malformed payloads.
- [ ] Verify that report history and config data match current WinForms state.

### Task 5: Implement Mutating Bridge Actions

**Files:**
- Create: `src\app\FrameScopeWebBridge.Monitoring.cs`
- Create: `src\app\FrameScopeWebBridge.Events.cs`
- Modify: `src\frontend\src\pages\TargetsPage.tsx`
- Modify: `src\frontend\src\pages\ReportsPage.tsx`
- Modify: `src\frontend\src\pages\SettingsPage.tsx`

- [ ] Wire `config.save`.
- [ ] Wire `monitor.start` and `monitor.stop`.
- [ ] Wire report open, open directory, regenerate, and diagnostics generation.
- [ ] Add in-flight/disabled states for every long operation.
- [ ] Preserve existing C# watcher and report behavior.

### Task 6: Live Page And Push Events

**Files:**
- Modify: `src\frontend\src\pages\LivePage.tsx`
- Modify: `src\frontend\src\bridge\useBridgeEvent.ts`
- Modify: `src\app\FrameScopeWebBridge.Events.cs`

- [ ] Push status, progress, error, and report-history events from C# to React.
- [ ] Keep the live-page invariant: entering live starts UI refresh; leaving live stops UI refresh.
- [ ] Read from existing run/status/log files; do not invent fake FPS as real data.
- [ ] Show explicit empty/demo state when there is no configured target or active run.

### Task 7: Packaging And Default UI Switch

**Files:**
- Modify: `build.ps1`
- Modify: `packaging\FrameScopeSetupNative.cs` only if installer payload extraction needs a folder rule change.
- Modify: `README.md`
- Modify: `docs\modules\software-ui.md`
- Modify: `docs\modules\ui-interactions.md`

- [ ] Add frontend build and copy to payload.
- [ ] Verify installed `%LOCALAPPDATA%\FrameScopeMonitor\frontend` assets.
- [ ] Keep old WinForms as fallback until WebView2 parity is accepted.
- [ ] Only switch WebView2 to default after tester sign-off.

## Parallel File Boundaries

Can run in parallel:

- UI design implementation can own `src\frontend\src\theme\`, `src\frontend\src\components\`, `src\frontend\src\layout\`, and visual-only page layout files.
- UI interaction implementation can own `src\frontend\src\bridge\client.ts`, `src\frontend\src\bridge\useBridgeEvent.ts`, and page-level event wiring after `contract.ts` is frozen.
- Backend bridge implementation can own `src\app\FrameScopeWebBridge*.cs` and `src\app\FrameScopeNativeMonitor.WebHost.cs`.
- Tester can own `docs\test-reports\*`, test execution, screenshots, and evidence files under ignored `artifacts\`.

Must be exclusive:

- `build.ps1`
- `src\app\FrameScopeNativeMonitor.cs`
- `src\frontend\src\bridge\contract.ts`
- `src\app\FrameScopeWebBridge.cs`
- `src\app\FrameScopeNativeMonitor.WebHost.cs`
- package asset copy rules
- any C# adapter that starts/stops watcher or kills FrameScope process trees

Do not modify in this migration unless the task explicitly requires it:

- `src\monitoring\FrameScopeProcessSampler*.cs`
- `src\monitoring\FrameScopeSystemSampler*.cs`
- `src\app\FrameScopeNativeMonitor.MonitorSession*.cs`
- `src\app\FrameScopeNativeMonitor.Watcher.cs`
- `src\reporting\FrameScopeReportGenerator*.cs`
- `src\diagnostics\FrameScopeDiagnostics*.cs`
- `scripts\lightweight\`
- sibling `..\gamelite-auto-lightweight\`

## Verification Plan

Required after the spike:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeUiStateTests.exe
.\tests\FrameScopeReportProgressTests.exe
node .\tests\chart-sampling-tests.js
dotnet run --project .\tools\WebView2Spike\WebView2Spike.csproj -- --smoke --evidence .\artifacts\webview2-spike\roundtrip.json --screenshot .\artifacts\webview2-spike\roundtrip.png --timeout-ms 15000
& 'C:\Program Files\Git\cmd\git.exe' diff --check
```

Residual process check:

```powershell
Get-CimInstance Win32_Process | Where-Object {
  $_.Name -match '^(FrameScopeMonitor|FrameScopeProcessSampler|FrameScopeSystemSampler|FrameScopeReportGenerator|PresentMon|FakePresentMon|TslGame|GameLite).*'
} | Select-Object ProcessId,Name,CommandLine
```

Expected:

- Build and tests pass.
- WebView2 evidence JSON records a successful host/page round trip.
- The screenshot exists and is non-empty.
- `git diff --check` has no whitespace errors.
- No residual FrameScope, PresentMon, sampler, report generator, FakePresentMon, TslGame, or GameLite process remains.

## Next-Round UI Design Implementation Prompt

Act as the FrameScope Monitor Web UI design owner.

Project path:
`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Required skills:
- `ui-ux-pro-max`
- `design-system`
- `design-review`
- `verification-before-completion`

Read first:
- `AGENTS.md`
- `docs\superpowers\plans\2026-05-20-framescope-webview2-ui-redesign.md`
- `docs\modules\software-ui.md`
- `docs\modules\ui-interactions.md`

Scope:
- Design the React/Vite frontend shell and token system only.
- Work under `src\frontend\src\theme\`, `src\frontend\src\layout\`, and visual-only `src\frontend\src\components\`.
- Use a macOS-inspired glass desktop style without copying Apple trademarks, icons, screenshots, or protected assets.
- Build real empty/loading/error/in-flight visual states.

Non-goals:
- Do not change C# monitoring, reporting, GameLite, WMI, SGuard, samplers, or report generator logic.
- Do not wire buttons to fake static behavior.
- Do not modify `build.ps1` unless this prompt is explicitly expanded.

Final report must include changed files, design tokens, page shell screenshots, accessibility/motion checks, and verification commands.

## Next-Round UI Interaction Implementation Prompt

Act as the FrameScope Monitor Web UI interaction owner.

Project path:
`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Required skills:
- `diagnose`
- `improve-codebase-architecture`
- `tdd`
- `verification-before-completion`

Read first:
- `AGENTS.md`
- `docs\superpowers\plans\2026-05-20-framescope-webview2-ui-redesign.md`
- `docs\modules\ui-interactions.md`
- `docs\modules\software-ui.md`

Scope:
- Implement React bridge client behavior after the C# bridge contract exists.
- Keep the live-page rule: entering live starts UI refresh; leaving live stops UI refresh.
- Add visible success/failure/in-flight feedback for config save, watcher start/stop, process refresh, report open/open directory/regenerate, diagnostics generation, log pause, and log clear.
- Work mainly in `src\frontend\src\bridge\`, `src\frontend\src\pages\`, and state hooks.

Non-goals:
- Do not design new backend behavior.
- Do not touch monitoring/reporting/GameLite files.
- Do not edit `build.ps1`.

Final report must include each wired action, real backend request type used, failure-state behavior, tests, and residual process check.

## Next-Round Backend Bridge Prompt

Act as the FrameScope Monitor WebView2 backend bridge owner.

Project path:
`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Required skills:
- `diagnose`
- `improve-codebase-architecture`
- `tdd`
- `review`
- `verification-before-completion`

Read first:
- `AGENTS.md`
- `docs\superpowers\plans\2026-05-20-framescope-webview2-ui-redesign.md`
- `docs\modules\ui-interactions.md`
- `docs\modules\backend-monitoring.md`

Scope:
- Add the real WebView2 host behind `--web-ui`.
- Implement `FrameScopeWebBridge*.cs` with typed JSON request/response/event envelopes.
- Start with `state.snapshot`, `config.get`, `processes.refresh`, and `reports.history`; then add mutating actions only after read-only tests pass.
- Reuse existing C# adapters for config, watcher, process enumeration, report open, report regenerate, and diagnostics.
- Keep old WinForms default path intact.

Allowed files:
- `src\app\FrameScopeNativeMonitor.cs`
- `src\app\FrameScopeNativeMonitor.WebHost.cs`
- `src\app\FrameScopeWebBridge*.cs`
- `build.ps1` as exclusive if new C# files are compiled or frontend assets are copied
- focused tests under `tests\`

Denied files:
- `src\monitoring\*`
- `src\reporting\FrameScopeReportGenerator*.cs`
- `src\diagnostics\*` unless only consuming public diagnostics methods
- `scripts\lightweight\*`
- `..\gamelite-auto-lightweight\*`

Final report must include bridge contract, request/response examples, thread/in-flight handling, old UI fallback proof, build/test results, and WebView2 screenshot/evidence.

## Next-Round Tester Prompt

Act as the FrameScope Monitor WebView2 migration tester.

Project path:
`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Required skills:
- `diagnose`
- `health`
- `review`
- `verification-before-completion`

Read first:
- `AGENTS.md`
- `docs\superpowers\plans\2026-05-20-framescope-webview2-ui-redesign.md`
- `docs\FrameScopeMonitor-Project-Overview.md`
- `docs\modules\software-ui.md`
- `docs\modules\ui-interactions.md`
- `docs\modules\backend-monitoring.md`

Test scope:
- Verify old WinForms default path still builds and launches.
- Verify `--web-ui` path loads local frontend assets.
- Verify bridge round trips for every implemented request type.
- Verify config save persists and reloads.
- Verify process refresh returns real process data without freezing.
- Verify watcher start/stop uses real C# behavior and leaves no residual processes.
- Verify report open/open directory/regenerate/diagnostics use real run folders and errors are visible.
- Verify live page starts/stops UI refresh on enter/leave.
- Verify reduced-motion behavior, keyboard focus, contrast, and no text overlap at common desktop sizes.

Required commands:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1
.\tests\FrameScopeUiStateTests.exe
.\tests\FrameScopeReportProgressTests.exe
node .\tests\chart-sampling-tests.js
& 'C:\Program Files\Git\cmd\git.exe' diff --check
```

Final report must include PASS/FAIL per command, screenshot paths, bridge evidence paths, residual process output, and clear blockers before any packaging or default-UI switch.
