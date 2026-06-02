# FrameScope WebView2 Full Visual P1 Fix Report

Date: 2026-05-23
Scope: React WebView2 frontend visual QA P1 fixes only.
Source root: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Verdict

PASS.

All 10 requested P1 visual QA items were fixed inside the frontend lane and verified with fresh frontend tests, WebView2 live smoke, WebView2 reduced-motion smoke, 1280x720 screenshots, 900x760 screenshots, failure-state screenshots, DOM/layout checks, `git diff --check`, and residual-process inspection.

No packaging, GitHub push, README edit, backend sampling/report-generation change, GameLite/WMI/SGuard change, or C# bridge semantic change was made for this round. The worktree still has pre-existing unrelated dirty files, including `src/app/FrameScopeNativeMonitor.WebHost.cs`; this report does not claim or modify those as part of the P1 visual fix.

## P1 Fix Status

| P1 | Status | Fix |
| --- | --- | --- |
| 1. Compact sidebar lost navigation semantics | PASS | Compact rail now keeps short visible labels / compact labels, title affordance, lighter active treatment, and non-button-like bottom status. |
| 2. Sidebar active/hover/focus too heavy | PASS | Sidebar CSS now separates idle, hover, active, focus, and active-focus; active is current page, focus is keyboard position, hover is light feedback. |
| 3. Targets 900px list unlabeled | PASS | Targets rows become compact labeled cards below 980px: identity, process label, sample/report label-value blocks, and non-stretched status pill. |
| 4. Targets process lookup too low and 250 results uncontrolled | PASS | Process lookup sits in the target side task panel, exposes total/result hint, and caps results with internal scrolling. |
| 5. Reports duplicate main actions | PASS | Reports header keeps list refresh only; row-level open remains primary; selected detail is a lower-weight inspector. |
| 6. Reports compact fields unlabeled and menu positioning | PASS | Compact report rows label frame count/size/status; the more menu is anchored to the trigger button at both 1280 and 900 widths. |
| 7. Empty-state disabled-looking buttons | PASS | `EmptyState` only renders a real button when an action exists; non-action guidance uses note styling instead. |
| 8. Save/search failure feedback too far away | PASS | Targets save, process search, and Settings save errors render near the triggering controls, preserve dirty input, and include retry actions. |
| 9. Settings long path control | PASS | Settings path uses a root+tail preview plus focusable full input; no fake copy/open controls; 900px has no horizontal overflow. |
| 10. Complete visual state fixture | PASS | Added frontend-only `?visualFixture=` mode covering `empty`, `loading`, `success`, `failure`, `dirty`, `saving`, `saved`, `many-results`, and `long-strings`. |

## Modified Files

Frontend/runtime files touched in this P1 lane:

- `src/frontend/src/components/Button.tsx`
- `src/frontend/src/components/ChartShell.tsx`
- `src/frontend/src/components/EmptyState.tsx`
- `src/frontend/src/components/InlineStatus.tsx`
- `src/frontend/src/components/StatusPill.tsx`
- `src/frontend/src/components/ToolbarButton.tsx`
- `src/frontend/src/components/components.css`
- `src/frontend/src/data/mockPreview.ts`
- `src/frontend/src/layout/AppShell.tsx`
- `src/frontend/src/layout/PageTransition.tsx`
- `src/frontend/src/layout/SidebarNav.tsx`
- `src/frontend/src/layout/TopStatusBar.tsx`
- `src/frontend/src/layout/layout.css`
- `src/frontend/src/pages/AboutPage.tsx`
- `src/frontend/src/pages/OverviewPage.tsx`
- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/pages/SettingsPage.tsx`
- `src/frontend/src/pages/TargetsPage.tsx`
- `src/frontend/src/pages/pages.css`
- `src/frontend/src/styles/global.css`
- `src/frontend/src/theme/motion.ts`
- `src/frontend/src/theme/tokens.css`
- `src/frontend/src/uiDesignContract.test.ts`

Evidence/report files added for this round:

- `artifacts/webview2-full-visual-p1-fix-20260523/capture-full-visual-p1-evidence.cjs`
- `artifacts/webview2-full-visual-p1-fix-20260523/full-visual-p1-evidence.json`
- `docs/implementation-reports/2026-05-23-framescope-webview2-full-visual-p1-fix-report.md`

## Screenshot Evidence

All screenshots are under `artifacts/webview2-full-visual-p1-fix-20260523/`.

1280x720 evidence:

- `overview-1280x720.png`
- `targets-default-1280x720.png`
- `targets-250-results-1280x720.png`
- `reports-default-1280x720.png`
- `reports-menu-1280x720.png`
- `settings-long-path-1280x720.png`
- `empty-targets-1280x720.png`
- `empty-reports-1280x720.png`

900x760 evidence:

- `compact-sidebar-900x760.png`
- `targets-compact-row-900x760.png`
- `targets-250-results-900x760.png`
- `reports-compact-900x760.png`
- `reports-menu-900x760.png`
- `settings-long-path-900x760.png`

Failure-state evidence:

- `targets-save-failed-1280x720.png`
- `targets-process-search-failed-1280x720.png`
- `settings-save-failed-1280x720.png`

Additional browser sanity screenshot:

- `in-app-browser-overview-long-strings.png`

Automated visual evidence checks from `full-visual-p1-evidence.json`:

```json
{
  "sidebarFixed": true,
  "reportsMenu1280Anchored": true,
  "reportsMenu900Anchored": true,
  "targetsMany1280InternalScroll": true,
  "targetsMany900InternalScroll": true,
  "reportsHeaderDuplicateRemoved": true,
  "targetsSaveFailureRetainsInput": true,
  "processFailureRetainsInput": true,
  "settingsFailureRetainsInput": true,
  "noHorizontalOverflow900": true
}
```

## Visual Fixture Mode

The frontend now supports a query-only visual preview mode through:

```text
?visualFixture=empty
?visualFixture=loading
?visualFixture=success
?visualFixture=failure
?visualFixture=dirty
?visualFixture=saving
?visualFixture=saved
?visualFixture=many-results
?visualFixture=long-strings
```

This mode is frontend-only and is implemented in `src/frontend/src/data/mockPreview.ts` plus page-level rendering hooks in Targets and Settings. It does not change the live WebView2 bridge contract or backend semantics.

## WebView2 Smoke

Live smoke command:

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-full-visual-p1-fix-20260523\webview2-live-smoke.json --web-ui-screenshot artifacts\webview2-full-visual-p1-fix-20260523\webview2-live-smoke.png --web-ui-timeout-ms 120000
```

Result: PASS. Key fields: `success=true`, `pageReady=true`, `usingReactFrontend=true`, `reducedMotion=false`, report live actions PASS, bridge extension smoke PASS, console count 0, error count 0.

Reduced-motion smoke command:

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-full-visual-p1-fix-20260523\webview2-reduced-motion-smoke.json --web-ui-screenshot artifacts\webview2-full-visual-p1-fix-20260523\webview2-reduced-motion-smoke.png --web-ui-timeout-ms 120000
```

Result: PASS. Key fields: `success=true`, `pageReady=true`, `usingReactFrontend=true`, `reducedMotion=true`, report live actions PASS, bridge extension smoke PASS, console count 0, error count 0.

## Command Verification

Frontend verification:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result: PASS. It completed `npm ci`, `tsc --noEmit`, Vitest, and Vite build. Vitest result: 5 files passed, 41 tests passed.

Visual evidence capture:

```powershell
& "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe" .\artifacts\webview2-full-visual-p1-fix-20260523\capture-full-visual-p1-evidence.cjs
```

Result: PASS. It generated required screenshots and the automated visual checks listed above.

In-app browser sanity check:

- Opened `http://127.0.0.1:4261/?visualFixture=long-strings`.
- Result: `overviewReady=true`, `horizontalOverflow=false`, `scrollWidth=1280`, `width=1280`.

Diff whitespace check:

```powershell
git diff --check
```

Result: PASS. No whitespace errors. Git printed existing LF-to-CRLF warnings only.

## Residual Process Check

Checked for project-related residual processes:

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `PresentMon*.exe`
- project/visual-evidence `node.exe`
- project/visual-evidence `msedge.exe` / WebView2 smoke processes

Result: PASS. No matching project-related residual process remained after closing the temporary browser/static server. The temporary TCP ports `4253`, `9423`, and `4261` had no active listener after cleanup.

## Notes And Risks

- The first `Run-Frontend.ps1 verify` attempt hit `EPERM unlink` on Rollup's native module because a project-local Vite `node.exe` was still running on port 5177. I stopped only that matching repo-local Vite process and reran verify successfully.
- The first visual evidence pass caught a real remaining issue: the Reports more menu was positioned relative to the whole row, not the trigger. I fixed `pages.css`, reran `Run-Frontend.ps1 verify`, and regenerated the screenshots/JSON. Final menu anchoring checks are PASS at both 1280 and 900 widths.
- This round did not package, push to GitHub, edit README, or change backend/C# bridge semantics.

## Retest Recommendation

Yes. This is ready to hand back to full-application visual QA for a fresh retest. The previous visual QA score was 6.2 / 5.8 / 5.5; this pass addresses the 10 P1 blockers and includes a fixture suite so QA can retest state coverage instead of only default happy paths.
