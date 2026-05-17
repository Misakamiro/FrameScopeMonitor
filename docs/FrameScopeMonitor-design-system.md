# FrameScopeMonitor Design System

Status: superseded by user-supplied reference images on 2026-05-10.

Selected direction: Reference Image Tech Dashboard.

Reference source of truth:

- `C:/Users/misakamiro/Downloads/ChatGPT Image 2026年5月10日 02_46_23 (2).png`
- `C:/Users/misakamiro/Downloads/ChatGPT Image 2026年5月10日 02_46_23 (5).png`
- `C:/Users/misakamiro/Downloads/ChatGPT Image 2026年5月10日 02_46_23 (4).png`
- `C:/Users/misakamiro/Downloads/ChatGPT Image 2026年5月10日 02_46_23 (3).png`
- `C:/Users/misakamiro/Downloads/ChatGPT Image 2026年5月10日 02_46_22 (1).png`

Hard rule: these images override the earlier B Professional Performance Dashboard implementation. The old visual language must not continue.

## Reference Image Style Extraction

Observed common system:

- Shell: borderless-feeling dark desktop app with thin Windows chrome, not a plain WinForms control sheet.
- Navigation: fixed left sidebar, product logo block, icon + Chinese page labels, active item cyan glow, bottom service status + version card.
- Background: near-black / deep blue-black with subtle radial blue gradients.
- Surfaces: semi-transparent dark blue cards, 1px blue border, soft cyan outer/inner glow, 12-16px radius.
- Page IA: top title area, top status cards, page content cards, persistent bottom report generation card.
- Accent system: cyan/blue primary, green ready/captured, purple analysis/diagnostics, amber warning, red destructive/regenerate/stop.
- Icon language: thin-line tech icons in glowing circular/square frames; no emoji.
- Data display: large numeric metrics, small secondary labels, color dots for status, progress rings/bars, chart grid.
- Motion intent: page fade/slide, hover glow, button pressed state, status light pulse, progress shimmer. Motion must be timer-light and disabled while capture path is busy.
- Language: UI functional text must be Chinese. `FrameScope Monitor` may stay English as product name only.

## Updated Product Fit

FrameScopeMonitor must feel like a finished game performance monitor:

- Before game: user sees service state, enabled targets, output directory readiness, capture chain readiness.
- During game: user sees live capture state, FPS/frame-time stream if available, active process, logs.
- After game: user sees report list, report details, generation progress, support package actions.

Primary design goal: dark tech dashboard matching the reference images, with real state wiring. No decorative-only fake UI.

## Plan Design Review

Reference match score before new implementation: 4/10.

Why low:

- Current stage-4 UI is dark, but still looks like a conventional WinForms form.
- Missing left navigation/page switching.
- Missing glowing translucent cards and icon system.
- Mixed English section titles remain.
- Only target page exists; overview/settings/report/live pages are not separate screens.
- Real-time page and report center are absent.

Target score after implementation: 8/10 minimum.

Must fix before coding final UI:

- Build a page router/seam in `FrameScopeNativeMonitor.cs`, not one giant static layout.
- Centralize UI primitives, semantic tokens, component helpers, and Chinese text.
- Keep all controls wired to current config/report/diagnostic functions.
- Explicitly label demo/live data sources in real-time page.

## Product Fit

FrameScopeMonitor is a game performance diagnosis tool, not a generic hardware monitor. UI must make three moments clear:

- Before game: monitoring readiness, selected targets, permission and capture status.
- During game: current session state, capture chain health, FPS data presence.
- After game: report generation progress, diagnosis result, latest report and export actions.

Primary design goal: reference-matched tech dashboard. Dark, glowing, data-first, Chinese, usable during gameplay.

## Plan Design Review

Initial score: 6/10. Existing UI has dark theme and useful controls, but visual system is still ad hoc and debug-tool shaped.

Target score after this design system: 8.5/10 before implementation.

Resolved decisions:

- Information architecture: top status strip, left target/monitoring workflow, right diagnosis/report panel, bottom session timeline.
- Visual style: low-saturation dark dashboard with electric blue primary, amber warning, red error, green captured/healthy state.
- Motion: 120-180ms state transitions only; no decorative animation during active capture.
- Density: compact desktop-first layout, with 8px grid and no nested cards.
- Diagnostics: report/log actions visible but secondary; they should not distract normal monitoring.

Not in scope for this stage:

- Replacing WinForms with another UI framework.
- Real PUBG verification without PUBG installed.
- Heavy animated visuals, particle backgrounds, or neon decoration.
- Removing full chart data to improve performance.

What already exists:

- Dark WinForms UI in `FrameScopeNativeMonitor.cs`.
- Target grid, start/stop controls, report progress label/bar.
- Report HTML renderer in `FrameScopeReportGenerator.cs`.
- Existing screenshots in `artifacts/ui-final.png` and report screenshots.

## Token Architecture

Use three layers: primitive -> semantic -> component.

### Reference Primitive Tokens

| Token | Value | Reference use |
|---|---:|---|
| `ref-bg-black` | `#030812` | outer app background |
| `ref-bg-blueblack` | `#06111F` | main workspace gradient base |
| `ref-card` | `rgba(8, 24, 42, 0.82)` | translucent card fill |
| `ref-card-strong` | `rgba(12, 34, 58, 0.92)` | selected/elevated card |
| `ref-border` | `rgba(60, 132, 190, 0.42)` | normal 1px card border |
| `ref-border-hot` | `rgba(0, 221, 255, 0.92)` | active/hover border |
| `ref-text` | `#F1F7FF` | primary Chinese text |
| `ref-text-soft` | `#AFC3D9` | secondary text |
| `ref-text-dim` | `#6F879F` | metadata / empty state |
| `ref-cyan` | `#00D9FF` | icons, active nav, FPS |
| `ref-blue` | `#147DFF` | primary buttons |
| `ref-green` | `#46FF6A` | ready/captured/normal |
| `ref-purple` | `#9A5CFF` | analysis/diagnostics |
| `ref-amber` | `#FFD84A` | warning |
| `ref-red` | `#FF4F78` | stop/error/regenerate |

### Reference Semantic Tokens

| Token | Maps to | Use |
|---|---|---|
| `surface-shell` | `ref-bg-black` | form background |
| `surface-workspace` | `ref-bg-blueblack` | page background |
| `surface-card` | `ref-card` | all cards/panels |
| `surface-card-selected` | `ref-card-strong` | active nav/selected rows |
| `border-card` | `ref-border` | card outlines |
| `border-active` | `ref-border-hot` | active/hover outlines |
| `text-main` | `ref-text` | main UI text |
| `text-secondary` | `ref-text-soft` | helper text |
| `text-empty` | `ref-text-dim` | empty data |
| `state-idle` | `ref-cyan` | idle/waiting display |
| `state-running` | `ref-green` | monitoring/captured |
| `state-warning` | `ref-amber` | degraded/no data |
| `state-error` | `ref-red` | failed/stopped |

### Reference Component Tokens

| Component | Required style |
|---|---|
| App shell | 1536x1024 reference ratio; left nav 196px; content margin 24px; all functional text Chinese |
| Sidebar | logo top; nav items 48-56px; active cyan border/fill; bottom service/version card |
| Card | 12px radius; semi-transparent blue fill; 1px blue border; subtle cyan glow |
| Stat card | icon left, small label, large value, secondary line; status color dot when useful |
| Button | 40-44px height; icon + Chinese label; blue primary; dark secondary; red danger; hover glow |
| Toggle | blue pill switch or checkbox fallback; labels Chinese; saved to config |
| Table | dark transparent rows; selected cyan outline/fill; status dot + Chinese status text |
| Capture chain | icon nodes connected by arrows; green dots under healthy stages; text below nodes |
| Progress bar | bottom persistent card; centered percent; stage text; green ready dot; blue animated fill when generating |
| Charts | dark grid; cyan FPS, purple 1% low, green frame time; readable axes and Chinese units |

### Primitive Colors

| Token | Value | Use |
|---|---:|---|
| `color-ink-950` | `#080C12` | App base background |
| `color-ink-900` | `#0B1118` | Window background |
| `color-ink-850` | `#101923` | Panel background |
| `color-ink-800` | `#152130` | Elevated surface |
| `color-ink-700` | `#203044` | Borders, table headers |
| `color-text-100` | `#EEF6FF` | Primary text |
| `color-text-300` | `#B9C8D8` | Secondary text |
| `color-text-500` | `#7F93A8` | Muted text |
| `color-blue-400` | `#3BA7FF` | Primary action, active chart |
| `color-blue-500` | `#198CEB` | Primary hover |
| `color-cyan-400` | `#29E6FF` | Live/capture accent |
| `color-green-400` | `#7DFA72` | Captured/healthy |
| `color-amber-400` | `#FFD35B` | Warning/needs attention |
| `color-red-400` | `#FF5D7D` | Error/capture failed |
| `color-purple-400` | `#8E7CFF` | Secondary chart series only |

### Semantic Colors

| Token | Maps to | Use |
|---|---|---|
| `surface-app` | `color-ink-900` | Main form |
| `surface-panel` | `color-ink-850` | Sections |
| `surface-raised` | `color-ink-800` | Cards, controls |
| `border-subtle` | `color-ink-700` | Grid and panel borders |
| `text-primary` | `color-text-100` | Main labels |
| `text-secondary` | `color-text-300` | Metadata |
| `text-muted` | `color-text-500` | Low-priority help |
| `state-idle` | `color-text-500` | Not monitoring |
| `state-live` | `color-cyan-400` | Monitoring/running |
| `state-ok` | `color-green-400` | Captured/generated |
| `state-warning` | `color-amber-400` | No FPS yet / waiting |
| `state-error` | `color-red-400` | Failed |

## Typography

WinForms implementation should use system fonts for reliable rendering:

- Primary font: `Segoe UI`.
- Numeric font: `Consolas` or `Cascadia Mono` when available.
- Title: 22px, weight 700.
- Section title: 14px, weight 700.
- Body: 12-13px.
- Caption/status: 11-12px.
- Metrics: 18-24px, tabular or monospace.

No viewport-scaled font sizes. No negative letter spacing.

## Spacing

Base grid: 8px.

- Window padding: 20px.
- Panel gap: 12px.
- Section padding: 16px.
- Control height: 34-38px.
- Dense table row: 34px.
- Main card radius: 8px max.
- Button radius: 6px.

No card inside card. Use panels for layout, cards only for repeated metrics or report items.

## Component Specs

### Buttons

| Variant | Use | Background | Text |
|---|---|---|---|
| Primary | Start monitoring | `color-blue-400` | `#06121E` |
| Secondary | Stop/open report | `surface-raised` | `text-primary` |
| Ghost | Open folder/copy diagnostics | transparent | `text-secondary` |
| Danger | Clear logs/stop forced | `color-red-400` | `#18070B` |

States:

- Hover: +8% brightness.
- Active: -8% brightness.
- Disabled: 45% opacity, no hover.
- Loading: keep label visible, add compact progress/status text beside button.

### Status Badges

| State | Label example | Color |
|---|---|---|
| Idle | Waiting | `state-idle` |
| Armed | Watching games | `state-live` |
| Capturing | FPS stream active | `state-ok` |
| Degraded | Process found, no FPS yet | `state-warning` |
| Failed | Capture failed | `state-error` |

Every badge needs text plus color. Never rely on color alone.

### Main Layout

Desktop target size: 1120x720 minimum.

- Header: product name, global monitoring state, last run summary.
- Left column: targets and start/stop workflow.
- Right column: capture chain health, latest report, diagnostics/log actions.
- Bottom band: report generation progress and recent session timeline.

### Settings

Group by user intent:

- Monitoring: poll interval, target list, sample intervals.
- Reports: auto open, report output, chart mode default.
- Diagnostics: verbose logs, performance diagnostics, auto diagnostic report.
- Retention: log days and max disk budget.

### Diagnostics Report

Report/log button lives in right panel under latest report. It should be visible but secondary to Start/Stop.

Privacy rules:

- Redact user profile path segments where possible.
- Do not include tokens, account IDs, full browser profiles, or unrelated environment variables.
- Include run paths only when needed for support, and mark them local-only.

## Chart UI Tokens

Chart palette:

- FPS: `#3BA7FF`
- Frame time / latency: `#FFD35B`
- CPU: `#29E6FF`
- GPU: `#7DFA72`
- Memory / VRAM: `#8E7CFF`
- Disk/network: `#FF9F43`
- Error/drop marker: `#FF5D7D`

Chart background: `#0B1118`.
Plot background: `#101923`.
Grid: `rgba(185, 200, 216, 0.14)`.
Axis text: `#B9C8D8`.
Tooltip: `#101923` with `#203044` border.

Interactions:

- Wheel zoom around cursor.
- Left-drag pan.
- Reset view visible.
- Visible-window stats always shown.
- Raw dense, spike-preserving, and trend modes must display source and drawn point counts.

## Animation Rules

- State color fades: 150ms.
- Panel hover: 150ms border/brightness only.
- Progress bar: smooth value updates, no pulsing during capture.
- No decorative background animation.
- No animation that runs while FPS capture is active unless it communicates state.

## Implementation Rules

- Centralize colors and sizing in helper methods or constants before UI rebuild.
- Use table/layout panels where possible; avoid fixed magic coordinates for new sections.
- Preserve offscreen screenshot command.
- Preserve no-foreground-interruption behavior.
- After UI changes, verify source and installed GUI screenshots.
