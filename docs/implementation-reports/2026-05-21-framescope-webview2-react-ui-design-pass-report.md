# FrameScope WebView2 React UI Design Pass Report - 2026-05-21

## Current Conclusion

PASS. This round completed a UI-only design pass for the WebView2 React frontend. The shell no longer imitates a fake macOS window, the React root now fits the WebView2 client area, Chinese copy is clearer across the main workflow pages, and technical bridge details are concentrated in About.

Scope stayed inside `src/frontend` plus this report and visual evidence. No C# bridge, backend sampling, report generation, GameLite, WMI, SGuard, packaging, installer, or release code was edited.

## Before / After Problem Translation

| User feedback / screenshot problem | Design target | Implemented result |
| --- | --- | --- |
| UI looked like a rounded macOS mini-window embedded inside a Windows host window. | Make React feel like the WebView2 client area itself. | Replaced `.mac-window` with `.app-shell`, removed outer margin, large radius, outer border, and large shell shadow. |
| Red/yellow/green window controls were decorative and had no function. | Remove fake platform controls unless they perform a real app action. | Deleted `.window-controls` markup and styles. |
| Search, notification, and shortcut buttons were occupying primary visual space without real behavior. | Keep only meaningful page actions in the main chrome. | Removed disabled topbar Search / refresh shortcut / notification toolbar and the floating mock banner. |
| Main pages exposed bridge implementation details too early. | Overview should answer: is monitoring running, what target is monitored, what can I do next? | Overview now leads with current monitoring state, enabled targets, start/stop/refresh actions, recent report, and next-step guidance. |
| Chinese copy was mixed with implementation wording and hard to scan. | Use short user-facing Chinese text and place technical terms only where needed. | Rewrote page headings, subtitles, status labels, empty states, button labels, mock/live messages, and default bridge-state messages. |
| Targets and Reports needed clearer dense-tool layouts. | Keep form/list layouts readable without horizontal clipping. | Targets uses a wider primary target table at WebView2 widths; Reports uses readable report metadata cards with labels like `报告编号 / 帧数 / 大小`. |
| Settings had too much technical phrasing. | Explain settings as user-facing behavior. | Settings labels now describe save location, refresh interval, log retention, and diagnostic toggles directly. |
| Technical boundary still needs to be visible somewhere. | Move WebView2 / bridge / requestId / mock adapter details into About. | About now owns the technical boundary and disabled-feature explanation. |

## Modified Files

- `src/frontend/src/layout/AppShell.tsx`
- `src/frontend/src/layout/SidebarNav.tsx`
- `src/frontend/src/layout/TopStatusBar.tsx`
- `src/frontend/src/layout/layout.css`
- `src/frontend/src/pages/OverviewPage.tsx`
- `src/frontend/src/pages/TargetsPage.tsx`
- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/pages/SettingsPage.tsx`
- `src/frontend/src/pages/AboutPage.tsx`
- `src/frontend/src/pages/pages.css`
- `src/frontend/src/styles/global.css`
- `src/frontend/src/theme/tokens.css`
- `src/frontend/src/components/components.css`
- `src/frontend/src/data/mockPreview.ts`
- `src/frontend/src/state/useFrameScopeBridgeState.ts`
- `src/frontend/src/uiDesignContract.test.ts`
- `src/frontend/src/vite-env.d.ts`
- `docs/implementation-reports/2026-05-21-framescope-webview2-react-ui-design-pass-report.md`

## Page Changes

### Shell / Sidebar / Top Status

- Removed fake macOS window controls, fake toolbar buttons, and the floating mock adapter banner.
- Kept the shell full-window and flush to the WebView2 client area.
- Changed connection labels to user-facing text: `预览模式 / 本机连接` and `状态正常 / 正在读取 / 连接异常`.
- Sidebar status now says whether the UI is a preview or whether host actions will be handled by the native app.

### Overview

- Rebuilt the first screen around current monitoring state.
- Put `启动监控 / 停止监控 / 刷新状态` in the page action group.
- Shows enabled targets, monitor status, recent report, data directory, and next-step status without mentioning bridge request names.
- Removed the disabled data-directory button from the primary workflow because it had no backed action in this design scope.

### Targets

- Rewrote heading, helper text, labels, empty state, save messages, and process-refresh messages in plain Chinese.
- Removed the disabled `新增目标待接入` button from the main action area.
- Reworked the editable target table so 1280-wide WebView2 preview gives the table enough room; the side process panel moves below at this width.
- Tightened row spacing and column sizing so process names, sampling interval, and report mode do not collide or overflow.

### Reports

- Replaced technical list wording with report-history wording.
- Changed metadata labels to `报告编号 / 帧数 / 大小`, with values kept readable inside cards.
- Kept available report actions only where the existing UI state already has real bridge semantics.
- Moved diagnostic status and file paths into a quieter side panel.

### Settings

- Rewrote copy around what the setting changes for the user: data directory, refresh interval, log retention, auto-open report, detailed logs, diagnostic logs, and auto diagnostic report.
- Summary rows now use user-facing labels and long paths use a single-line value with full text in the title attribute to avoid broken vertical wrapping.

### About

- About now holds the technical boundary: WebView2 bridge status, mock adapter preview, request/response contract, requestId/event handling, and features deliberately not faked.
- Main workflow pages no longer put WebView2, bridge, requestId, mock adapter, or request names in first-screen copy.

## Screenshot Evidence

Evidence root:

- `artifacts/webview2-react-ui-design-pass-20260521`

Screenshots:

- `browser-overview-1280x720.png`
- `browser-targets-1280x720.png`
- `browser-reports-1280x720.png`
- `browser-settings-1280x720.png`
- `browser-about-1280x720.png`
- `browser-overview-900x760.png`
- `browser-targets-900x760.png`

Layout audit:

- `browser-layout-audit.json`

Audit result summary:

- `fakeShellControls`: 0 on all audited pages.
- `bodyHorizontalOverflow`: false on all audited pages.
- `viewportHorizontalOverflow`: false on all audited pages.
- `hasBadText`: false on all audited pages.
- `technicalLeak`: empty on Overview / Targets / Reports / Settings.
- `englishPhrases`: empty for fake/unwired toolbar phrases.
- `elementIssues`, `clippedText`, and `cardOverflow`: 0 on all audited pages.

## Verification Results

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`: PASS.
  - `npm ci`: completed.
  - `tsc --noEmit`: PASS.
  - `vitest run`: 3 files, 10 tests passed.
  - `vite build`: PASS.
- Browser preview screenshot audit with Edge + Playwright: PASS.
- `git diff --check`: exit code 0. Git printed LF-to-CRLF working-copy warnings only; no whitespace errors were reported.

## Self-Check

- No fake red/yellow/green window controls remain.
- No decorative topbar Search / Notification / fake shortcut controls remain.
- WebView2 and bridge details are not shown in Overview, Targets, Reports, or Settings page copy.
- About is the only page that intentionally explains WebView2, bridge, requestId, and mock adapter boundaries.
- Five core pages were screenshot-tested.
- A 900x760 narrow layout screenshot was generated, plus an extra 900x760 Targets screenshot because Targets was the densest layout.
- No C# bridge, backend sampling, report generation, GameLite, WMI, SGuard, packaging, or release files were changed.

## Recommendation For UI Interaction Lane

Yes. This design pass should be handed to the UI interaction window next for behavior-specific review: confirm keyboard flow, disabled/enabled timing, target editing ergonomics, save failure recovery, and whether any removed fake shortcuts should become real backed actions later. That follow-up should stay in the interaction lane and should not reinterpret this visual pass as permission to change backend bridge or packaging code.
