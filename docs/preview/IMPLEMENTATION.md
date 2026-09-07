# FrameScope UI Preview implementation

## Frozen sources
- FrameScope: https://github.com/Misakamiro/FrameScopeMonitor at 2c480312a6e883f46e2f5b0c529f84d5c08ecbf3.
- NexBox: https://github.com/MuLiuSaMa/NexBox at c800dc575eb8832086ed4e4bfb223f0a008deed9 (9.5.3).
- Original NexBox reference lives in reference/nexbox and remains unchanged.
- Production C# code, build, installation and data are not part of this preview.

## Execution checklist
- [x] Create isolated clone and branch.
- [x] Freeze upstream reference and copy frontend source.
- [ ] Run upstream shell, hardware and appearance UI using preview data.
- [ ] Prune unsupported routes, settings, commands and navigation.
- [ ] Add FrameScope monitor, targets, report management and settings.
- [ ] Integrate native, vertical and sensor preview windows.
- [ ] Verify provenance, typecheck, tests, desktop build and interaction.
- [ ] Verify visual comparisons, scaling, performance and isolation.

## Recorded deviations from NexBox upstream (c800dc5)
Each entry is an approved, intentional difference. Everything else must stay byte-identical.
- `src/contexts/background-context.tsx`: liquid glass enabled defaults to ON
  (context default, `useState(true)`, persisted on first launch). Owner decision 2026-09-07:
  user wants the upstream liquid-glass look active by default; upstream ships it OFF.
- `src/components/ui/sidebar.tsx`: removed music/tools/optimization/steam/epic/delta-force/custom
  entries; added `/targets`, `/reports`, `/overlay-panel` FrameScope entries (approved trim + business).
- `public/samples/charts/`: real FrameScope interactive report sample (HTML + data.js + manifest)
  copied from the remediation worktree artifacts (2026-09-02 run) so `open_report_sample` works.
- Full per-file provenance manifest is still TODO (see execution checklist).

## Session 2026-09-07 (ZCode takeover): three root-cause fixes
The rebuilt UI rendered only the background in the Tauri host while the identical
bundle rendered fine in a browser. Fixed in order:
1. **CSP blocked all Emotion/Chakra styles.** The custom `csp` string in
   `tauri.conf.json` made Tauri append a nonce; per spec `'unsafe-inline'` is then
   ignored, so every dynamically injected `<style>` was blocked and the whole UI
   fell back to unstyled static flow (sidebar pushed below the fold, titlebar
   unmounted, cards invisible). Fix: `csp: null`, matching NexBox upstream exactly.
2. **Window-creating commands deadlocked the main thread.** `WebviewWindowBuilder::build()`
   called inside the `preview_native` command handler blocked the event loop while
   WebView2 controller init waited on that same pump: windows appeared half-initialized
   (hidden/titleless in async mode, stuck at about:blank in sync mode) and the invoke
   promise never resolved. Fix: every window-creating flow (sensor monitor, report
   sample, vertical overlay, overlay start/toggle/restart, hotkey handler) now runs on
   a worker thread via `std::thread::spawn`; the command returns immediately.
3. **`liquidGlassEnabled` defaults to ON** (owner decision, recorded above) — sidebar
   glass `blur(1px)` + translucent cards confirmed rendering.

Verified after fixes (release build, real desktop): all five main pages, sensor
monitor window (14 mock sensors, groups, search, filters), vertical overlay
(transparent, always-on-top, colored values), native GDI+ overlay panel
(CPU/GPU/RAM/PING strip), and the real interactive report sample window.
Temporary CDP diagnostics (`--remote-debugging-port=9333` via additionalBrowserArgs)
were removed before the final build. To debug the webviews again, temporarily add
`--remote-debugging-port=9333` back to `additionalBrowserArgs`/`additional_browser_args`.

## Session 2026-09-07 (round 2): home redesign + readability fixes
- **Home page redesigned** (owner request): detailed hardware configuration is now the
  primary content (glass card grid reusing HardwarePage row style on LiquidGlassCard);
  monitoring and latest report moved to a secondary right column that stretches to
  match the main area height (`align="stretch"`, cards `flex={1}`).
- **StatCard title overflow** (hardware page): the title HStack was content-width so
  the GPU-name marquee never detected overflow and the card clipped the text; the
  HStack now fills the card (`w="full" minW={0}`) so the existing marquee scrolls.
- **Unreadable controls on bright wallpapers** (dark color mode + light preset):
  floating controls now use the same translucent-pill treatment as GlobalSearch —
  hardware-page top buttons (`floatBtnStyle`) and the targets/reports search fields
  (`useFloatingFieldStyle` in preview/pages/shared.tsx). Color mode itself is NOT
  auto-switched; recorded as an approved visual adaptation.
- **Background seam (断层) mitigation**: the fixed full-screen cross-fade layers in
  MainLayout got `transform: translateZ(0)` + `willChange: opacity` to stop WebView2
  tile-rasterization hairlines. Intermittent issue — if it still appears, next step
  is disabling the bg carousel fade or compositing hints on glass cards.

## Session 2026-09-07 (round 3): alignment + remaining faint controls
- **Home secondary column is now sticky**: `position: sticky; top: 68px; height:
  calc(100vh - 92px)`, cards `flex={1} minH={0}` with inner scroll. The column always
  fits the visible area next to the scrolling hardware grid (the earlier stretch made
  the column taller than the viewport, which read as "not aligned"). Removing the page
  root's `overflowX: hidden` was required — that overflow makes the page root the
  sticky container and silently disables stickiness.
- **Remaining faint buttons got the pill treatment** via the new shared
  `useFloatingButtonStyle` hook: home "查看报告", overlay-panel "导出报告" (was
  theme-primary outline, invisible on light wallpaper) and "清除数据" (translucent bg
  added under the red text).

## Session 2026-09-07 (round 4): real backend bridge (M2)

Frontend layout untouched. Desktop data commands now answer from the real
sampling chain instead of fixtures; browser preview keeps the fixture fallback.

- `desktop-preview/src-tauri/src/hardware_service.rs` (new): named-pipe client
  for FrameScopeHardwareService (`framescope-hardware-service/1`). Spawns the
  service on demand next to the preview exe (override: `FRAMESCOPE_BACKEND_DIR`),
  `--lhm-helper` points at the sibling provider; persistent connection, one
  reconnect on transport failure, 5s spawn cooldown. GPL boundary: none of the
  service code comes from NexBox except `game_fps.rs` (ported in M1, noted there).
- Deviation from preview-fixture contract: `preview_set_hardware` no longer
  feeds the GDI panel — a host-side 1 Hz poller reads `get_overlay_data` from
  the service, publishes a rounded copy for the integer-typed GDI struct
  (`sanitize_for_gdi`: clocks/percents/VRAM f64 → int) and emits the raw
  full-precision snapshot as `vertical-overlay-data`. The frontend fixture
  push in `state.ts` tick is desktop-skipped.
- New host commands (whitelist additions): `get_hardware`/`get_hardware_info`,
  `get_all_sensors`, `get_overlay_hardware_data`, `get_memory_status`,
  `get_disk_status`, `is_nvidia_gpu` (active GPU name check), 
  `set_active_gpu_index` → service `set_active_gpu`, `clear_hardware_data` →
  `clear_cache`, `get_system_locale` (GetUserDefaultLocaleName),
  `get_system_username`. Error propagation: service unavailability surfaces
  as a command error (fail-closed, no synthetic data).
- `export_hardware_report` (desktop, JSON): content now assembled host-side
  from live overlay + static hardware; frontend no longer ships fixture text.
  HTML export path unchanged (still writes the passed-through content).
- Frontend `core.ts`: the ten data commands forward to the host on desktop
  (`isDesktop()`), fixtures remain the browser-preview path. `nativeCommands`
  whitelist unchanged.
- `tools/Deploy-PreviewBackend.ps1` (new): copies service/provider/LHM runtime
  DLLs from repo root (locked restore output) next to the built preview exe.
- windows-sys features added to the preview host: `Win32_Security`,
  `Win32_Storage_FileSystem`, `Win32_System_IO`, `Win32_System_Pipes`,
  `Win32_Globalization` (CreateFileW is Security-gated, Read/WriteFile IO-gated,
  GENERIC_READ/WRITE live in Foundation).
- Verified live: home/hardware pages real values, sensor window 219 real
  sensors at 1 Hz, GDI panel `CPU 8% 4950MHz 0.594V 32.3W 1286RPM | GPU 31%
  49°C 225MHz | RAM 51%`, FPS `--` with no game foreground (fail-closed).

## Session 2026-09-07 (round 5): real capture pipeline bridge (M3)

Targets / monitor / reports leave the in-memory simulation on desktop; the
browser preview keeps every simulated path untouched.

- `desktop-preview/src-tauri/src/backend.rs` (new): a Rust re-implementation of
  the C# WebView2 bridge semantics against the same file contracts:
  - `framescope-config.json` (PascalCase FrameScopeConfigStore schema; default
    targets created verbatim on first access, atomic tmp+rename writes).
    `targets_get` maps to the camelCase PreviewTarget shape (ProcessName is the
    id); `targets_save` replaces only the Targets array, normalizing process
    names per FrameScopeTargetEditRules (`.exe` suffix, `;` alias cleanup).
  - `monitor_start` spawns `FrameScopeMonitor.exe --watcher --config <path>`
    (idempotent when the watcher lives); `monitor_stop` kills the pipeline
    process family (monitor/samplers/PresentMon) and removes the state file.
    `monitor_status` reads `framescope-watcher-state.json` and maps
    Phase/ActiveMonitors onto the frontend machine (idle/waiting/capturing,
    elapsed from the run-directory timestamp).
  - `reports_list` merges `framescope-history.jsonl` (newest first) with a
    bounded data-root scan; reportId = SHA256-Base64url of the normalized
    uppercase runDir (C# algorithm); frame stats are parsed out of the
    report's `framescope-interactive-data.js` (`window.FRAMESCOPE_DATA`).
  - `report_open` / `report_open_directory` shell-open; `report_regenerate`
    runs `FrameScopeReportGenerator.exe <runDir> --progress ... --input-fingerprint ...`
    (fingerprint = SHA256 over the three monitor CSVs' name|size|mtime).
  - `processes_refresh`: sysinfo enumeration + EnumWindows titles, front-
    window-first ordering, 250 cap (v1.2.2 semantics).
  - Backend root discovery: `FRAMESCOPE_BACKEND_DIR` > exe directory > up to
    four ancestors containing FrameScopeMonitor.exe (dev tree layout).
- Frontend (mixed mode): `platform.ts` gains `backendInvoke` for backend-only
  commands (desktop forwards, browser rejects). `state.ts` syncs targets and
  reports from the backend on startup, persists target edits through
  `targets_save`, routes monitor.start/stop to the real watcher, and each tick
  reconciles the local monitor phase with `monitor_status` (backend is the
  source of truth; completed edge triggers a reports refresh). TargetsPage
  fills its process dropdown from real `processes_refresh`; ReportsPage lists
  backend reports, opens the real HTML in the system browser and the run
  directory in Explorer, and regenerates through the real generator.
- `tools/Deploy-PreviewBackend.ps1` now also deploys the capture chain
  (FrameScopeMonitor/samplers/ReportGenerator + tools\PresentMon).
- End-to-end verified live: added a `cmd.exe` target in the UI (persisted to
  framescope-config.json), started monitoring from the home page (watcher →
  monitor-session → PresentMon + both samplers, status "monitoring"), closed
  the target processes, and the watcher wrote a diagnostic-kind report
  (18.7k process-sample rows) with history entry; the report page listed the
  real run and the watcher auto-opened the interactive HTML report.

## Delivery boundary
Standalone FrameScope UI Preview. Real window behavior; hardware data now
served by the real FrameScopeHardwareService chain (M0–M2). Business flows
around targets/capture/reports remain simulated until M3. Existing HTML report
layout stays unchanged.
