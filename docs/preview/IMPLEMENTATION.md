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

## Delivery boundary
Standalone FrameScope UI Preview. Real window behavior, simulated business data.
No production installer or hardware/game sampling. Existing HTML report layout stays unchanged.
Final backend and production host architecture remain a later decision.
