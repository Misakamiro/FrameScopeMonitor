# FrameScope P2 UI Animation Performance Optimization Report

Date: 2026-06-01

Final conclusion: PASS

## Scope

This round only handled P2 frontend UI animation / transition / repaint cost. It did not handle large-list windowing implementation, report generation, report chart process interaction, backend monitoring, logging optimization, data-root recursive scans, build artifact sync, installer/setup packaging, installation, real game launch, BF6 testing, GitHub push, or Release update.

Hard boundary result:

| Boundary | Result |
| --- | --- |
| Packaging / installer build | Not run |
| FrameScope install | Not run |
| Real game launch | Not run |
| BF6 test | Not run |
| GitHub push | Not run |
| Release update | Not run |
| `build.ps1` | Not run |
| New frontend animation library | Not added |
| FPS raw statistics | Not touched |
| `bucketMs=1000` | Not touched |
| CPU Voltage / Vcore | Not touched |
| CPU Core VID | Not touched |

`tools\Run-Frontend.ps1 verify` ran `npm ci` and Vite build as normal verification-script behavior. This is validation only, not product packaging, installer generation, product install, or Release work.

## Evidence And Method

I added `tools\Probe-FrontendUiAnimation.js` for repeatable UI animation measurement. It starts the local Vite frontend, drives headless Edge through CDP, emulates ordinary and reduced motion, navigates Overview / Targets / Reports / Settings, records computed style inventory, browser Performance metrics, interaction deltas, and screenshots. It also performs optional target/settings/reports smoke.

Evidence files:

| Evidence | Path |
| --- | --- |
| before UI animation probe, 2 runs per motion mode | `artifacts\p2-ui-animation-optimization-20260601\before\before-frontend-ui-animation-probe.json` |
| after UI animation probe, 2 runs per motion mode + smoke | `artifacts\p2-ui-animation-optimization-20260601\after\after-frontend-ui-animation-probe.json` |
| after large-list guard probe, 2 runs + smoke | `artifacts\p2-ui-animation-optimization-20260601\large-list-guard\after-ui-animation-guard-frontend-large-list-probe.json` |

Visual evidence:

| Mode | Screenshot paths |
| --- | --- |
| Ordinary motion before | `artifacts\p2-ui-animation-optimization-20260601\before\ordinary-overview.png`, `ordinary-targets.png`, `ordinary-reports.png`, `ordinary-settings.png` |
| Reduced motion before | `artifacts\p2-ui-animation-optimization-20260601\before\reduced-overview.png`, `reduced-targets.png`, `reduced-reports.png`, `reduced-settings.png` |
| Ordinary motion after | `artifacts\p2-ui-animation-optimization-20260601\after\ordinary-overview.png`, `ordinary-targets.png`, `ordinary-reports.png`, `ordinary-settings.png` |
| Reduced motion after | `artifacts\p2-ui-animation-optimization-20260601\after\reduced-overview.png`, `reduced-targets.png`, `reduced-reports.png`, `reduced-settings.png` |
| Smoke screenshots | `artifacts\p2-ui-animation-optimization-20260601\after\smoke-targets.png`, `smoke-settings.png`, `smoke-reports.png` |

Manual screenshot inspection checked after ordinary Targets and after reduced Settings. The pages were not blank, text did not obviously overlap, and controls remained aligned.

## Before Baseline

Static scan before:

| Metric | Before |
| --- | ---: |
| `framer-motion` imports in measured frontend runtime files | 6 |
| `<motion.*>` elements | 3 |
| `whileTap` handlers | 2 |
| `MotionConfig` references | 3 |
| Static `transition: all` declarations | 0 |
| Static transition declarations | 14 |
| Static animation declarations | 8 |
| `@keyframes` | 4 |
| Static box-shadow transition candidates | 2 |
| Filter declarations | 0 |
| Backdrop-filter declarations | 0 |
| Blur references | 0 |
| `prefers-reduced-motion` blocks | 3 |

Runtime before, averaged across Overview / Targets / Reports / Settings with 2 runs per mode:

| Mode | Nav ms | Transitioned elems | Animated elems | Box-shadow transitioned | Transition-all elems | Task ms | Recalc ms | Layout ms | JS heap MB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Ordinary | 41.50 | 22.75 | 0.63 | 4.63 | 0.00 | 67.22 | 5.34 | 7.65 | 22.83 |
| Reduced | 42.80 | 18.13 | 0.00 | 4.75 | 0.63 | 64.89 | 4.84 | 2.74 | 67.81 |

Interaction before, averaged across Targets process refresh, Reports menu open, and Settings save:

| Mode | Interaction ms | Transitioned elems | Animated elems | Box-shadow transitioned | Transition-all elems | Task delta ms | Recalc delta ms | Layout delta ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Ordinary | 28.80 | 31.33 | 0.67 | 7.33 | 0.00 | 6.43 | 0.54 | 0.51 |
| Reduced | 26.95 | 28.00 | 0.00 | 9.00 | 1.67 | 6.53 | 0.35 | 0.31 |

Baseline findings:

1. Normal pages had no blur, backdrop-filter, or filter pressure.
2. Static `transition: all` was not present in source, but runtime reduced-motion produced `transition-property: all` on Settings segmented controls because the reduced-motion block set `transition-duration: 1ms` on elements that had no explicit base transition.
3. Page commits and button press feedback still imported and mounted Framer Motion runtime even though page variants were visually static and buttons already had CSS `:active` feedback.
4. Button/toolbar and monitor-dot feedback transitioned `box-shadow`, which is paint-heavy and not necessary for preserving visual recognition.
5. Reports menu and process-row update animations were already constrained to opacity/transform or color/background state, and were kept.

## Changes

Modified files:

| File | Change |
| --- | --- |
| `src\frontend\src\App.tsx` | Removed `useReducedMotion` because page commits no longer use JS animation variants. |
| `src\frontend\src\main.tsx` | Removed `MotionConfig` wrapper; no runtime Framer path remains in app shell. |
| `src\frontend\src\layout\PageTransition.tsx` | Replaced static `motion.div` with plain `div`. |
| `src\frontend\src\components\Button.tsx` | Replaced `motion.button` with native `button`; kept existing class, props, icon, disabled, and click behavior. |
| `src\frontend\src\components\ToolbarButton.tsx` | Replaced `motion.button` with native `button`; kept icon-only toolbar semantics. |
| `src\frontend\src\theme\motion.ts` | Removed Framer type imports; kept token objects for CSS/test contract continuity. |
| `src\frontend\src\components\components.css` | Removed `box-shadow` from button transition list and added explicit reduced-motion active-state transform override. |
| `src\frontend\src\pages\pages.css` | Removed monitor-dot `box-shadow` transition and removed segmented controls from the reduced-motion duration-only override. |
| `src\frontend\src\uiMotionContract.test.ts` | Added contract coverage for no static Framer runtime path and stronger reduced-motion button active override. |
| `tools\Probe-FrontendUiAnimation.js` | Added dedicated UI animation performance/static/runtime/smoke probe. |
| `docs\implementation-reports\2026-06-01-framescope-p2-ui-animation-performance-optimization-report.md` | This report. |

Why each optimization is safe:

| Optimization | Safety reason |
| --- | --- |
| Remove Framer from page commit path | Current page transition variants only set `opacity: 1`; replacing with a plain `div` keeps page layout and visual state identical while avoiding unnecessary JS animation runtime work. |
| Remove Framer from Button/ToolbarButton | CSS already provides hover/focus/active feedback. Native buttons preserve click, disabled, aria, className, and data-smoke attributes. |
| Keep CSS `:active` scale for ordinary motion | Press feedback remains transform-only and short; no visual feedback was removed wholesale. |
| Add reduced-motion active override | Reduced-motion users no longer get press scaling from the higher-specificity `:active` selector; focus, color, border, and text feedback remain. |
| Remove box-shadow transitions | Shadows may still appear as state styling, but they no longer animate. This avoids paint-heavy shadow interpolation without flattening the UI hierarchy. |
| Remove reduced segmented-control duration override | Segmented controls had no base transition, so the reduced-motion duration-only override created runtime `transition-property: all`. Removing it keeps the existing instant feedback and removes the implicit transition-all path. |

## After Results

Static scan after:

| Metric | Before | After | Change |
| --- | ---: | ---: | ---: |
| `framer-motion` imports in measured frontend runtime files | 6 | 0 | -6 |
| `<motion.*>` elements | 3 | 0 | -3 |
| `whileTap` handlers | 2 | 0 | -2 |
| `MotionConfig` references | 3 | 0 | -3 |
| Static `transition: all` declarations | 0 | 0 | 0 |
| Static transition declarations | 14 | 14 | 0 |
| Static animation declarations | 8 | 8 | 0 |
| `@keyframes` | 4 | 4 | 0 |
| Static box-shadow transition candidates | 2 | 0 | -2 |
| Filter / backdrop / blur declarations | 0 | 0 | 0 |
| `prefers-reduced-motion` blocks | 3 | 3 | 0 |

Runtime page averages after:

| Mode | Nav ms | Transitioned elems | Animated elems | Box-shadow transitioned | Transition-all elems | Task ms | Recalc ms | Layout ms | JS heap MB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Ordinary before | 41.50 | 22.75 | 0.63 | 4.63 | 0.00 | 67.22 | 5.34 | 7.65 | 22.83 |
| Ordinary after | 40.24 | 22.50 | 0.75 | 0.00 | 0.00 | 62.38 | 5.25 | 7.88 | 20.61 |
| Reduced before | 42.80 | 18.13 | 0.00 | 4.75 | 0.63 | 64.89 | 4.84 | 2.74 | 67.81 |
| Reduced after | 40.13 | 18.25 | 0.00 | 0.00 | 0.00 | 60.09 | 4.95 | 2.71 | 57.19 |

Runtime interaction averages after:

| Mode | Interaction ms | Transitioned elems | Animated elems | Box-shadow transitioned | Transition-all elems | Task delta ms | Recalc delta ms | Layout delta ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Ordinary before | 28.80 | 31.33 | 0.67 | 7.33 | 0.00 | 6.43 | 0.54 | 0.51 |
| Ordinary after | 25.00 | 31.33 | 0.67 | 0.00 | 0.00 | 5.00 | 0.46 | 0.45 |
| Reduced before | 26.95 | 28.00 | 0.00 | 9.00 | 1.67 | 6.53 | 0.35 | 0.31 |
| Reduced after | 28.68 | 26.33 | 0.00 | 0.00 | 0.00 | 5.74 | 0.37 | 0.36 |

Important interpretation:

1. The strongest measurable cleanup is qualitative/static: no runtime Framer path for static page/button feedback, no box-shadow transitions, and no reduced-motion transition-all.
2. Browser task/heap metrics improved in the averaged probe, but this is a small P2 change and CDP heap/task numbers are noisy. I am not claiming a P0/P1-level performance win.
3. Ordinary Settings and reduced Targets had small metric noise increases in some per-page values; screenshots and smoke stayed normal.
4. Vite verification build output also reflects the runtime-library removal: after verify, JS output was `dist/assets/index-CiA_VsKz.js` at 243.16 kB gzip 72.66 kB. This is validation evidence only, not a packaging/release artifact.

## Functional Smoke

Smoke from `Probe-FrontendUiAnimation.js --include-smoke`:

| Area | Result | Evidence |
| --- | --- | --- |
| Target add/edit/delete | PASS | start rows 4, final rows 4, add visible true, edit visible true, save disabled true |
| Settings save/read | PASS | saved true, value read back as `1375` |
| Reports basic flow | PASS | start rows 3, final rows 3, operation status visible true |
| Smoke overall | PASS | `smoke.success=true` |

Large-list guard from `Probe-FrontendLargeLists.js --runs 2 --include-smoke` after this UI animation change:

| Check | Result |
| --- | --- |
| 250-row process list still windowed | PASS; rendered 19 of 250 rows initially |
| End scroll still reaches list end | PASS; after scroll rendered 10 rows, total 250, `windowed=true` |
| Filtered small list de-windows | PASS; 51 of 51 rows, `windowed=false` |
| Large-list smoke | PASS; target/settings/reports smoke success true |

## FPS / Voltage / VID Guardrails

This round changed only frontend UI animation/runtime files and probes. It did not modify reporting, chart sampling, system sampling, or telemetry parsing.

| Requirement | Result |
| --- | --- |
| FPS raw statistics | Not touched; `FrameScopeReportManifestTests.exe` and `chart-sampling-tests.js` passed |
| `bucketMs=1000` | Not touched; `chart-sampling-tests.js` passed |
| CPU Voltage / Vcore | Not touched; manifest/diagnostics tests passed |
| CPU Core VID | Not touched; `FrameScopeSystemSamplerCpuCoreTests.exe` passed |

## Verification Commands

| Command / check | Result |
| --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 test` before production-code change | Expected RED; 2 motion-contract failures proved Framer runtime and reduced-motion active override were still present. |
| Bundled Node `.\tools\Probe-FrontendUiAnimation.js --label before --runs 2 --out .\artifacts\p2-ui-animation-optimization-20260601\before` | PASS; before baseline recorded 16 page samples and screenshots. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 test` after production-code change | PASS; 6 files / 62 tests passed. |
| Bundled Node `.\tools\Probe-FrontendUiAnimation.js --label after --runs 2 --include-smoke --out .\artifacts\p2-ui-animation-optimization-20260601\after` | PASS; after data recorded 16 page samples, screenshots, and smoke success true. |
| Bundled Node `.\tools\Probe-FrontendLargeLists.js --label after-ui-animation-guard --runs 2 --include-smoke --out .\artifacts\p2-ui-animation-optimization-20260601\large-list-guard` | PASS; 250-row list still windowed, smoke success true. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS; `npm ci` added 110 packages as normal verify behavior; typecheck PASS; Vitest 6 files / 62 tests PASS; Vite build PASS. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS; `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeReportManifestTests.exe` | PASS; final line `FrameScopeReportManifestTests: PASS`. |
| `.\tests\FrameScopeDiagnosticsTests.exe` | PASS; `FrameScopeDiagnosticsTests: PASS`. |
| `.\tests\FrameScopeSystemSamplerCpuCoreTests.exe` | PASS; `FrameScopeSystemSamplerCpuCoreTests: PASS`. |
| Bundled Node `.\tests\chart-sampling-tests.js` | PASS; `chart-sampling-tests: PASS`. |
| Target/settings/report smoke | PASS; covered by UI animation probe and large-list guard probe. |
| Ordinary motion screenshot/probe | PASS; screenshots and computed-style probe recorded. |
| Reduced-motion screenshot/probe | PASS; screenshots and computed-style probe recorded; runtime `transition-all` average went 0.63 to 0.00. |
| `git diff --check` | PASS; exit 0 with existing LF-to-CRLF warnings only, no whitespace errors. |
| Residual process check | PASS; refined process check returned `NO_MATCHING_RESIDUAL_PROCESSES`. |

## Final Result

PASS.

The scoped P2 UI animation optimization removed unnecessary JS motion runtime from static page/button feedback, removed paint-heavy box-shadow transitions, and fixed a reduced-motion path that produced runtime `transition-property: all`. Ordinary and reduced motion remain visually usable, hover/focus/active/menu/busy/list feedback remains present, target/settings/reports smoke passed, and large-list windowing did not regress. The measured browser Performance gains are modest and noisy, so this should be treated as a low-risk cleanup rather than a major hotspot fix.
