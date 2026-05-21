# FrameScope Monitor WebView2 Reports Size Layout Fix Report

Date: 2026-05-21
Scope: Reports page visual layout only
Issue: FSM-WEB-LIVE-002

## Conclusion

FSM-WEB-LIVE-002 is resolved in this frontend pass. The Reports page no longer renders the `Size` value as vertically wrapped characters in the report stat cell at normal WebView2 width or at the resized narrow WebView2 width.

This pass did not modify bridge code, backend code, button behavior, build scripts, packaging, GameLite, WMI, SGuard, monitoring, reporting, diagnostics, or root test files.

## Changed Files

- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/pages/pages.css`
- `docs/implementation-reports/2026-05-21-framescope-webview2-reports-size-layout-fix-report.md`

## UI Changes

- Added a Reports-only nowrap value class for numeric stat values used by `Frames` and `Size`.
- Changed Reports stat cells to use a compact label-over-value layout inside each cell, instead of inheriting the shared two-column snapshot layout that was too wide for narrow report stat cells.
- Increased the `Frames` and `Size` stat column minimum width and added a mid-width responsive layout where `Report ID` spans the full row while `Frames` and `Size` share the next row.
- Kept long `Report ID` wrapping behavior intact so IDs remain readable without forcing the whole card to overflow.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Pre-fix style regression check | FAIL before fix | Confirmed missing Size nowrap binding and compact report meta CSS. |
| Post-fix style regression check | PASS | Confirmed Size has `snapshot-item__value--nowrap` and Reports meta uses compact layout. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | `npm ci`, typecheck, Vitest 2 files / 7 tests, and Vite build passed. |
| WebView2 normal smoke | PASS | `artifacts/webview2-reports-size-fix-20260521/webview2-size-fix-normal.json` has `success=true`. |
| WebView2 narrow smoke | PASS | Window resized to 900x760 before smoke capture; `webview2-size-fix-narrow.json` has `success=true`. |
| WebView2 reduced-motion smoke | PASS | `webview2-size-fix-reduced-motion.json` has `success=true` and `reducedMotion=true`. |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS | Exit 0; existing LF/CRLF warnings only. |

## Screenshot Evidence

Primary evidence directory:

- `artifacts/webview2-reports-size-fix-20260521`

Reports evidence:

- Normal width: `artifacts/webview2-reports-size-fix-20260521/webview2-size-fix-normal-reports.png`
- Narrow width: `artifacts/webview2-reports-size-fix-20260521/webview2-size-fix-narrow-reports.png`
- Reduced motion Reports: `artifacts/webview2-reports-size-fix-20260521/webview2-size-fix-reduced-motion-reports.png`

Basic page screenshots:

- Overview: `artifacts/webview2-reports-size-fix-20260521/webview2-size-fix-normal-overview.png`
- Targets: `artifacts/webview2-reports-size-fix-20260521/webview2-size-fix-normal-targets-result.png`
- Reports: `artifacts/webview2-reports-size-fix-20260521/webview2-size-fix-normal-reports.png`
- Settings: `artifacts/webview2-reports-size-fix-20260521/webview2-size-fix-normal-settings-clean.png`
- About: `artifacts/webview2-reports-size-fix-20260521/webview2-size-fix-normal-about.png`

Motion evidence:

- Normal transition frames: `webview2-size-fix-normal-transition-*.png`
- Reduced-motion transition frames: `webview2-size-fix-reduced-motion-transition-*.png`

Manual visual review confirmed the `Size` value stays on one line, buttons remain wrapped without overlap, status pills stay inside cards, and reduced-motion transition frames do not show old/new page mixing or blank frames.

## Recommendation

Tester should retest FSM-WEB-LIVE-002 against the new evidence directory. FSM-WEB-LIVE-001 remains outside this visual-only fix and still needs its own UI interaction or bridge test coverage pass.
