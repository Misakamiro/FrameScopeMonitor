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

## Session 2026-09-07 (round 6): hardening (M4)

- Admin manifest: `build.rs` overrides the Windows manifest via
  `tauri_build::WindowsAttributes::app_manifest` with `requireAdministrator`
  (ETW capture, driver-backed LHM sensors and PawnIO need elevation; spawned
  children inherit the token) plus `PerMonitorV2` DPI awareness. The override
  MUST keep Tauri's default `Microsoft.Windows.Common-Controls` dependency —
  a manifest without it SxS-fails ("无法找到入口"/parallel-config) or hangs
  the process at 4.6MB; a self `assemblyIdentity` also breaks SxS.
- PawnIO: new `pawnio.rs` + commands `check_pawnio_status` (uninstall
  registry key), `install_pawnio_driver` / `uninstall_pawnio_driver`
  (elevated PawnIO_setup.exe from `dependencies\PawnIO\` — not redistributed
  in the current build, so they fail closed with a clear message),
  `restart_monitor_process` → hardware-service `restart_provider`. The
  frontend PawnioInstallModal (HardwarePage) now reaches the real backend.
- Settings: the fixture-scenario card is hidden on desktop (real backend has
  no scenarios); About text updated to reflect the real pipeline. Browser
  preview keeps the scenario switch and fixtures.
- Test updated: `install_pawnio_driver` now expects the desktop-only refusal
  message in browser mode; a truly unknown command still fails with
  "Unsupported preview command".

## Session 2026-09-08: data-consistency pass (hardware source unification)

Goal: the report must record the same values the overlay displays. FPS stays
dual-source by user decision (overlay ETW realtime vs PresentMon per-PID
recording).

- `src/hardware-service/src/main.rs`: the named pipe is now a multi-instance
  accept loop - each client (Tauri host, capture worker) gets its own session
  thread sharing the process-wide resident provider; the process exits only on
  a global stop request. Handles are passed into session threads as usize
  (raw pointers defeat the closure Send analysis even inside a Send wrapper).
- New `src/monitoring/FrameScopeHardwareServiceClient.cs`: short-lived-connection
  pipe client (connect 800ms, read 4s, auto-spawn the service from the app
  directory when absent). Written for the in-box csc 4.0.30319 (C# 5: no
  `using` nesting shorthand, no `when` filters, explicit lambdas - method
  groups are ambiguous over Action/Func<T>).
- `MonitorSession` starts a `FrameScopeHardwareSampleRecorder` when capture
  begins: one hardware-samples.csv row per second with the overlay's own
  snapshot fields (Time, CpuUsage/TempC/ClockMHz/PowerW/Voltage, GpuUtilPct/
  TempC/PowerW/ClockMHz/MemClockMHz/VramUsedMiB/VramTotalMiB, MemoryUsedPct,
  SsdTempC, GamePing). Paths/models/build.ps1 extended; System.Core reference
  added to the monitor build.
- ReportGenerator: `MergeHardwareOverlaySamples` overlays each system-samples
  row's GPU columns with the nearest hardware-samples row within two seconds
  (non-null fields only), so GPU stats/charts share the overlay's data source
  instead of nvidia-smi. 21-column contract, fingerprints, and older runs are
  untouched.
- Live-verified: a cmd.exe capture round produced 76 hardware-samples rows and
  a regenerated report whose GPU series (37/37/36/36/39...) matched the
  hardware-samples LHM values, while nvidia-smi had recorded 2%.

## Delivery boundary
Standalone FrameScope UI Preview. Real window behavior; hardware data now
served by the real FrameScopeHardwareService chain (M0–M2). Business flows
around targets/capture/reports remain simulated until M3. Existing HTML report
layout stays unchanged.

## Session 2026-09-08 (evening): round-2 audit, live test pass, and two fix batches

Round-2 audit report: `docs/audit/2026-09-08-full-audit-round2.md` (three-way
code re-review of the five fix commits plus a full live test pass: page
walkthrough, 1Hz capture verification, chart pipeline, performance, CS2 and
Battlefield 6 end-to-end). This session entry also back-fills the gap noted in
the audit: the audit itself and the five fix commits (28fb00d..a9f36e4) had no
IMPLEMENTATION entries - their detail lives in git log and the audit reports.

### Fix batch 1 (e76d079, found live)
- `src/hardware-service/src/main.rs`: 5fb6492 wrote
  FILE_FLAG_FIRST_PIPE_INSTANCE as 0x0005_0000 (real value 0x0008_0000); the
  undefined bit made CreateNamedPipeW fail with ERROR_INVALID_PARAMETER on
  every start, so the built service exited within seconds - caught live, not
  by cargo test (19/19 still green) or the static audit. The flag is now
  asserted only on the first accept-loop creation (MSDN: later instances must
  not set it - the old loop would have self-terminated with a misleading
  ACCESS_DENIED after the first client), and the redundant-spawn exit drains
  accepted sessions first.
- `src/hardware-service/src/provider.rs`: kill() bounded the graceful-exit
  wait to 3s (try_wait polling) before falling back to kill - the unbounded
  `Child::wait` could hold the provider mutex forever on a hung provider.

### Fix batch 2 (e92b7c9)
- `desktop-preview/src/preview/runtime/state.ts`: tick() no longer runs the
  browser fixture capture machine on desktop (a single missed reconciliation
  let it finish a simulated run and inject a simulated report into the real
  list - reproduced live during the Battlefield 6 test). targets_get sync now
  lifts its one-shot latch only on success, retries every 5s, and
  persistTargetsToBackend refuses to save before the first successful sync
  (H8). completedRuns baseline is also captured on the capturing transition
  (R-C1: starting a capture with the game already running misreported every
  successful run as an error).

### Fix batch 3 (this commit)
- `src/monitoring/FrameScopeSystemSampler.Gpu.cs` (R-B2): nvidia-smi
  StandardOutput.ReadLine ran synchronously before WaitForExit - a hung driver
  stalled the sampler loop forever. Read now runs on a pool thread with a
  2.5s bounded wait (same shape as the hardware-service client); killing the
  process releases the stranded read.
- `src/hardware-service/src/overlay.rs` (R-A4): storage temperature no longer
  uses name hints (the empty-hint call was dead code and "Drive"/"SSD" hints
  missed most NVMe models); storage temperature sensors are unambiguous, so
  the overlay reports the hottest drive.
- `src/hardware-service/src/game_fps.rs` (R-A6): switching away from a game
  clears 1%/0.1% Low alongside FPS - the expiry path is gated on
  SMOOTHED_FPS != 0 and never ran after the switch-away zeroed it.
- Delta Force password row (M5/R-C2/R-C3, three stacked breaks):
  `desktop-preview/src-tauri/src/delta_password.rs` (new) ports the upstream
  NexBox password fetch (primary JSON API + plain-text fallback, 60s cache)
  and lib.rs exposes `get_delta_passwords` through preview_native + COMMANDS;
  core.ts routes it (browser preview returns an empty list).
  `core.ts` update_overlay_settings no longer filters display items down to
  the preview default list (that deleted the native-only rows on first save)
  - it now appends missing defaults and passes delta_password_maps through.
  defaultOverlay and OverlayPanelPage carry the full 22-item set.
- **NexBox upstream file diff note (rule 3)**:
  `desktop-preview/src/pages/OverlayPanelPage.tsx` DEFAULT_DISPLAY_ITEMS was
  missing the upstream `{ id: "delta_password", label: "三角洲密码" }` entry;
  restored verbatim from `reference/nexbox/src/pages/OverlayPanelPage.tsx`.
  No other upstream page files touched this session.

### Live verification
cargo test 19/19, vitest 13/13 (source-boundaries command-table parity covers
the new command). CS2 full pipeline (25466 frames, full report), Battlefield 6
(EA Javelin): hardware/OSD/resident-ETW FPS work, PresentMon's ETW trace is
blocked by the anti-cheat and the pipeline degrades to a diagnostic report
with presentmon-etw-access-denied - expected boundary, not a defect. Whole
monitoring chain ~1.1% CPU / ~340MB. Config changes made during testing:
CMD Test target disabled, Battlefield 6 target enabled (also exercised
targets_save).


## Session 2026-09-09: fix batch 3 (service lifecycle, merge perf, config persistence, timeouts)

- **M3** (`desktop-preview/src-tauri/src/hardware_service.rs`): spawned hardware
  services now join a KILL_ON_JOB_CLOSE job owned by the host - a host killed
  from Task Manager no longer leaves an orphan service+provider chain (the
  provider follows its service via stdin EOF). Verified live: killing the host
  used to leave the service running (it blocked a deploy during this session).
- **M4** (`src/hardware-service/src/main.rs`): session threads capped at 16
  (overloaded connects are shed with a short sleep); JoinHandles are dropped
  (detach semantics) instead of accumulating in a Vec forever.
- **R-B1** (`src/app/FrameScopeNativeMonitor.ProcessCleanup.cs` +
  `WebHost.cs`): the explicit host-exit path now also reaps the resident
  hardware service + LHM provider (they live outside the watcher PID tree).
  StopMonitor intentionally does NOT kill them - the Tauri host shares the
  service across capture stop/start.
- **M9** (`src/reporting/FrameScopeReportGenerator.SystemData.cs`):
  MergeHardwareOverlaySamples is now a two-pointer O(N+M) merge over the two
  time-sorted lists instead of O(N*M) (which projected 40-90s for 24h
  captures and a generation timeout). Nearest-within-2s semantics preserved
  (ties resolve to the earlier overlay row, exactly as before).
- **M6** (`backend.rs` + `lib.rs` + `platform.ts` + `state.ts`): new
  `config_save` backend command; the settings page now persists on desktop.
  Only fields the capture chain honors are mapped (TelemetrySampleIntervalMs,
  PollIntervalMs, OpenReportOnComplete, verbose/diagnostic logs) into the
  PascalCase contract; unknown fields stay untouched (M7 intact).
  **Regression caught live and fixed in the same batch**: the first cut wrote
  mapped ints as JSON floats ("1000.0"); C# JavaScriptSerializer refuses
  float-for-Int32 and ConfigStore.Load silently fell back to the default
  config - every user target vanished from the watcher's view mid-session
  (Battlefield 6 stopped being captured). config_save now writes whole
  numbers, and the runtime config shipped broken in this window was repaired.
  Add a cross-language contract test for Rust-written configs before touching
  the writer again.
- **M17 (partial)** (`build.ps1`): the build now stops the FrameScope
  process chain first (locked in-use files used to fail the build halfway),
  and a locked dist backup cleanup degrades to a warning instead of failing
  an already-successful build. Full staging-based atomic outputs remain a
  larger refactor (see audit).
- **H7 tail** (`hardware_service.rs`): pipe reads are tracked by a watchdog
  that fires CancelIoEx after 12s (a cold LHM scan can legitimately take
  ~10s), so a hung service can no longer pin the CLIENT lock forever; the
  aborted read is treated as a transport failure and the connection recycles.
- Live test: CS2 full pipeline (49624 frames, full report, auto-opened);
  Battlefield 6 captured after the config regression was fixed, PresentMon
  ETW blocked by Javelin as before, diagnostic report degradation confirmed
  (presentmon-etw-access-denied, 175 system samples). Overhead measured
  0.79% CPU / ~333MB for the whole monitoring chain.


## Session 2026-09-09 (late): anti-cheat FPS fallback (user-requested feature)

User goal: games whose anti-cheat blocks PresentMon must still produce fully
usable reports. BF6 live evidence showed the resident ETW FPS session keeps
working while PresentMon's per-launch trace is denied, so the report now
rebuilds its FPS series from the recorder's samples when PresentMon yields
no frames:

- `FrameScopeHardwareServiceClient.cs`: hardware-samples.csv gained a `Fps`
  column (the overlay snapshot's own ETW FPS value). Old CSVs without it read
  back as null - no migration needed.
- `FrameScopeReportGenerator.cs` + `.SystemData.cs`: when PresentMon yields
  no frames, the frame series is synthesized from the Fps samples
  (frame ms = 1000/fps, 1 Hz) and the run classifies full;
  frameCaptureStatus becomes `etw-fps-sampled` with a message naming the
  anti-cheat substitution. source-boundaries locks the column on both sides.
- `backend.rs` build_report_entry trusts the generator manifest's reportKind,
  so a regenerate can upgrade a worker-preclassified diagnostic stub.
- Live BF6 verification: a real run regenerated from diagnostic to full with
  178 samples / averageFps 125.7. Future runs synthesize automatically at
  capture end. PresentMon's absence stays honestly reported (access denied,
  1 Hz sampling instead of per-frame), which is the hard anti-cheat boundary.
- Batch-3 hardening that rode along: SensorMonitorPage fetchingRef reset
  (R-C7), LazyStore guarded localStorage writes (R-C9).

Process note: build.ps1 csc failures were masked twice by shell output
filters - always verify the deployed exe timestamp after building, never
trust a filtered log line.


## Decision (2026-09-09, user): dual-writer architecture stays as-is (Option A)

FPS data keeps two purpose-built channels: PresentMon remains the
authoritative per-frame source for reports (frame-level percentiles, hitch
detection) whenever its trace is allowed, and the resident ETW FPS session
stays the real-time overlay source plus the automatic fallback for
anti-cheat-blocked runs (the etw-fps-sampled path shipped in 6aba278).
Retiring PresentMon in favor of an in-house per-frame ETW writer remains a
possible future project, not a current task.


## Session 2026-09-09 (final): remaining low-risk hardening batch

- **R-A5** (`overlay.rs`): find_sensor now ranks candidates by hint priority
  (hint list order = priority) instead of row order, so a broad bare "GPU"
  last-resort hint can never let GPU memory temperature or SOC power win
  over "GPU Core".
- **R-A8** (`lib.rs` + `overlay_panel.rs`): update_overlay_settings holds
  OVERLAY_OPERATION for its whole teardown/rebuild; OVERLAY_ACTIVE start gate
  is a swap (no check-then-act double-create).
- **R-A12** (`overlay_panel.rs`): WM_QUIT breaks the entire message pump
  (labeled loop) instead of spinning the outer overlay loop after quit.
- **R-C5** (`lib.rs` poller + `hardware_service.rs`): three consecutive poll
  failures broadcast an all-null overlay snapshot (new empty_overlay_value),
  so overlays show "--" while the service is down instead of freezing on the
  last snapshot.
- **R-C11** (`backend.rs`): monitor_stop prefers taskkill /PID /T rooted at
  the watcher from watcher-state; the by-image-name sweep is fallback only.
  Verified live: tree kill leaves zero FrameScope/PresentMon processes after
  a real BF6 capture.
- **status.json evidence** (`MonitorSession.cs` + `.Status.cs` +
  `HardwareServiceClient.cs`): the recorder exposes RowsWritten/LastError and
  the capture end merges HardwareSampleRows/HardwareSampleLastError into
  status.json - a silently dead recorder is now diagnosable.
- **H3 tail** (`ReportGenerator.cs`): report metadata now carries
  OverlayMergedRows and FramesFromHardwareSamples, making silent zero merges
  visible in the artifact.
- **Frontend**: HardwarePage poll gained generation-based stale-response
  protection (same shape as OverlayPanelPage); TargetsPage surfaces
  "process list unavailable" and fixes option keys; ReportsPage shows a
  simulated badge; browser-mode reports capped at 50; target ids append a
  counter; monitorBusy() shared helper replaces three drifted copies.
- **Group 1 (earlier commit)**: format_unix_local via chrono Local (was 8h
  behind), parse_run_timestamp DST earliest(), pipe response-id validation,
  reused 1MB read buffer, provider stderr PID suffix, safe strip_exe_suffix,
  FPS_STARTED 5s retry throttle, minimal stderr logger in both Rust binaries.
- Deliberately skipped: openTask volatile (closure local - not expressible;
  audit judged practical risk nil), WinEventHook removal (dead but harmless;
  removal touches the ETW file for zero behavior change), shell_open /
  taskkill-by-image / pipeline ACL / backend_root signature (local threat
  model hardening - need a user call), dual-writer retirement (decided
  Option A, see above).

Final test state: cargo 19/19 + 5/5, vitest 14/14, C# 27/27. Live: CS2 full
report (16765 frames) captured with the game already running (R-C1 scenario
- no error misreport), BF6 capture + tree-kill stop with zero leftover
processes, anti-cheat FPS fallback report verified end to end (125.7 FPS).


## Session 2026-09-09 (late II): remaining pure-tech low-risk batch

- **R-A7** (`pawnio.rs` + `lib.rs` + `hardware_service.rs`): PawnIO
  install/uninstall now raises a shared poller-pause flag for the whole
  elevated-installer wait, so the 1 Hz poller cannot resurrect the sampling
  service the setup just killed; stop_sampling_service kills by PID scoped to
  the deployment directory (sysinfo exe path check) instead of a global
  taskkill /IM sweep.
- **H6 tail** (`lib.rs`): report_regenerate waits with recv_timeout(150s) -
  the generator itself enforces 120s - so a stuck worker cannot pin the async
  worker forever.
- **M2** (`hardware_service.rs`): message-mode reads drain ERROR_MORE_DATA
  (234) into a growing buffer instead of failing the whole connection.
- **R-A10** (`backend.rs`): frame_stats cached by (path, mtime) - reports_list
  no longer re-reads every multi-MB data.js for four numbers.
- **R-B3** (`Watcher.cs`): workers dying within 5s of start trigger a 5s
  backoff after three consecutive fast failures (no more respawn-per-second
  storms).
- **R-B4** (`FrameScopeNativeMonitor.cs`): Quote() doubles trailing
  backslashes so a trailing-backslash config value cannot split the argument.
- **R-B5** (`FrameScopeSetupNative.cs` + `FrameScopeUninstaller.cs`): install
  and uninstall now also stop the hardware service + provider (file locks and
  leftovers).
- **R-B6** (`ConfigStore.cs`): NormalizeTarget keeps a configured
  high-precision ProcessSamplingMode instead of silently forcing "normal"
  (the option had become unreachable).
- **R-B7** (`ProcessCleanup.cs`): IsWatcherRunning requires the PID to be a
  FrameScopeMonitor image, not just any recycled PID.
- **R-B8** (`MonitorSession.cs` + `HardwareServiceClient.cs`): the session
  error path stops the hardware recorder explicitly (static StopActive());
  recorder rows/last-error are captured too.
- **R-C6** (`OverlayPanelPage.tsx`): removed the never-rendered
  hardwareData state and its 1 Hz poll (wasted IPC).
- **R-C10** (`state.ts`): report.regenerate no longer uses non-null
  assertions across a list refresh boundary.
- **M18 tail** (`TargetsPage.tsx`): the process dropdown consumes
  displayText (process name + window title) instead of bare names.
- Live verification: BF6 capture shows HardwareSampleRows counting in
  status.json mid-capture, tree-kill stop leaves zero processes, all suites
  green (cargo 19/19 + 5/5, vitest 14/14, C# 27/27).
