# FrameScope Monitor WebView2 Full Visual P1 Retest Report

Date: 2026-05-23
Source root: `C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## Verdict

PASS.

本轮复测没有只看上一轮 JSON 结论，重新跑了前端 verify、重新生成 1280x720 和 900x760 截图、人工查看关键 PNG、重新跑 WebView2 live smoke 和 WebView2 reduced-motion smoke，并检查 `git diff --check` 和残留进程。

10 个 P1 均已解除。建议进入最终打包验证，但打包验证应继续保持单独窗口：本报告只证明 WebView2 React UI 视觉 P1 复测通过，不证明安装包、payload 同步、卸载/覆盖安装和发布资产已经通过。

## Score

| Dimension | Previous | Retest | Conclusion |
| --- | ---: | ---: | --- |
| 视觉质量 | 6.2 / 10 | 8.2 / 10 | P1 阻塞已消除，界面从“组件拼装感”提升到可交付桌面工具水准。仍有列表密度和卡片层级偏朴素的问题。 |
| 易用性 | 5.8 / 10 | 8.3 / 10 | compact、失败态、长路径、菜单和大结果集都可用。主要剩余问题是 Targets 多结果在 900 宽下仍需要较多纵向滚动。 |
| 一致性 | 5.5 / 10 | 8.0 / 10 | Sidebar、按钮、菜单、状态 pill、空状态和表单控件明显统一。少量页面内卡片/行样式还可以进一步收敛。 |
| 动效 | 7.4 / 10 | 8.8 / 10 | 普通和 reduced-motion WebView2 smoke 均通过，没有看到 route 旧页残留、低透明主体或横向溢出。 |

## Evidence Directory

Primary retest evidence:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\webview2-full-visual-p1-retest-20260523`

Key evidence files:

- `full-visual-p1-retest-evidence.json`
- `webview2-live-smoke.json`
- `webview2-live-smoke.png`
- `webview2-reduced-motion-smoke.json`
- `webview2-reduced-motion-smoke.png`
- 86 PNG screenshots total, including static browser retest states and WebView2 smoke frame captures.

## P1 Retest Matrix

| P1 | Status | Retest result | Screenshot evidence |
| --- | --- | --- | --- |
| P1-01 compact sidebar lost navigation semantics | PASS | 900 宽 sidebar 现在显示短标签，active 项居中，底部状态不再像不可点击按钮。 | `compact-sidebar-900x760.png` |
| P1-02 sidebar active / hover / focus too heavy | PASS | active、hover、focus 不再互相抢权重；active rail 和选中块视觉较轻，focus 保持键盘位置提示。 | `compact-sidebar-900x760.png` |
| P1-03 Targets 900px list unlabeled | PASS | 900 宽目标行改为带字段标签的 compact card，状态 pill 没有横向铺满，行内容可读无重叠。 | `targets-compact-row-900x760.png` |
| P1-04 Targets process lookup too low and 250 results uncontrolled | PASS | 查找进程面板有总数说明，250 项限制在内部滚动列表中；1280 和 900 截图都能看到内部滚动条和结果卡片。 | `targets-250-results-1280x720.png`, `targets-250-results-900x760.png` |
| P1-05 Reports duplicate main actions | PASS | 页头不再出现“打开最新报告”重复主操作，列表行为更像主任务，详情区为 inspector。 | `reports-default-1280x720.png` |
| P1-06 Reports compact fields unlabeled and menu relation unclear | PASS | 900 宽报告行有帧数/大小标签，更多菜单贴近触发按钮，不再像浮在列表中部的独立卡片。 | `reports-compact-900x760.png`, `reports-menu-900x760.png` |
| P1-07 Empty-state disabled-looking buttons | PASS | 空状态中的 CTA 有真实动作入口，不再出现“看起来能点但实际不可用”的禁用按钮假象。 | `empty-targets-1280x720.png`, `empty-reports-1280x720.png` |
| P1-08 Save/search failure feedback too far from trigger | PASS | Targets 保存失败、查找失败、Settings 保存失败都靠近触发区，输入值保留，并提供重试动作。 | `targets-save-failed-1280x720.png`, `targets-process-search-failed-1280x720.png`, `settings-save-failed-1280x720.png` |
| P1-09 Settings long path control | PASS | 900 宽长路径不撑破、不乱码、不裁切；提供 root+tail 预览和可聚焦完整输入。 | `settings-long-path-1280x720.png`, `settings-long-path-900x760.png` |
| P1-10 complete visual fixture coverage | PASS | 已抽查 empty、loading、success、failure、dirty、saving、saved、many-results、long-strings 视觉夹具截图。 | `fixture-empty-1280x720.png`, `fixture-loading-1280x720.png`, `fixture-success-1280x720.png`, `fixture-failure-1280x720.png`, `fixture-dirty-1280x720.png`, `fixture-saving-1280x720.png`, `fixture-saved-1280x720.png`, `fixture-many-results-1280x720.png`, `fixture-long-strings-1280x720.png` |

## Focus Areas

### Compact Sidebar

Result: PASS.

Evidence:

- `compact-sidebar-900x760.png`
- `full-visual-p1-retest-evidence.json`

Observed:

- compact sidebar width: 84 px
- active nav: `reports`
- focused nav: `settings`
- bottom status cursor: `auto`
- page horizontal overflow at 900 width: false

Manual visual check:

- active rail and selected block are centered and aligned.
- hover/focus/active states are distinguishable without becoming visually heavy.
- the blue rail is visible but not dominant.
- short labels remove the previous pure-icon ambiguity.

Remaining polish note:

- compact sidebar is now usable, but it still feels more utilitarian than premium. A later P2 pass could tune icon weight and active label contrast by a small amount.

### Targets

Result: PASS.

Evidence:

- `targets-compact-row-900x760.png`
- `targets-250-results-1280x720.png`
- `targets-250-results-900x760.png`
- `targets-save-failed-1280x720.png`
- `targets-process-search-failed-1280x720.png`

Observed:

- 250 process rows generated in fixture mode.
- 1280 result list internal scroll: true, client height 331, scroll height 16546.
- 900 result list internal scroll: true, client height 350, scroll height 22768.
- 900 compact layout horizontal overflow: false.
- failed target save retained edited input: `Valorant QA`.
- failed process search retained input: `VALORANT`.

Manual visual check:

- 900 target rows have clear labels for process, sample, and report.
- status pill is compact and no longer stretched across the row.
- 250-result state is readable and scrolls inside its own panel.
- failure feedback is close to the search/save control and visually obvious.

Remaining polish note:

- 900 many-results still becomes a tall operational surface. It is no longer P1 because it is readable and bounded, but a virtualized denser list or filter-first layout would improve premium feel.

### Reports

Result: PASS.

Evidence:

- `reports-default-1280x720.png`
- `reports-menu-1280x720.png`
- `reports-compact-900x760.png`
- `reports-menu-900x760.png`
- `empty-reports-1280x720.png`

Observed:

- 1280 more menu anchored to trigger: true.
- 900 more menu anchored to trigger: true.
- duplicate `open latest report` header action removed: true.
- compact metric labels present.
- 900 compact layout horizontal overflow: false.

Manual visual check:

- at 1280, menu sits below and close to the more button; it does not drift into unrelated content.
- at 900, menu is still near the trigger and does not hide key report information.
- row action remains the primary report action; detail panel is lower weight.

Remaining polish note:

- the contextual menu is functionally correct and much better anchored, but it still has a slightly card-like visual weight. This is P2 polish, not a blocking issue.

### Settings

Result: PASS.

Evidence:

- `settings-long-path-1280x720.png`
- `settings-long-path-900x760.png`
- `settings-save-failed-1280x720.png`

Observed:

- 900 long path horizontal overflow: false.
- failure input retained and ends with `qa-failed-save`: true.
- failure feedback near save trigger: true.
- full input supports horizontal access; scroll width is larger than client width by design.

Manual visual check:

- long path no longer breaks layout at 900 width.
- root/tail preview gives enough context without forcing the whole path into one visible line.
- save failure appears near the top save action and also in the side summary area.

Remaining polish note:

- long-path handling is now product-usable. A later pass could add copy/open affordances only if the live bridge explicitly supports them.

### Visual Fixture States

Result: PASS.

Evidence:

- `fixture-empty-1280x720.png`
- `fixture-loading-1280x720.png`
- `fixture-success-1280x720.png`
- `fixture-failure-1280x720.png`
- `fixture-dirty-1280x720.png`
- `fixture-saving-1280x720.png`
- `fixture-saved-1280x720.png`
- `fixture-many-results-1280x720.png`
- `fixture-long-strings-1280x720.png`

Manual visual check:

- empty states show clear next-step action without fake disabled CTA.
- loading/saving states use visible busy status without obscuring content.
- success/saved states are readable and calm.
- failure states are close to trigger areas and preserve user input.
- dirty state gives clear unsaved status.
- many-results and long-strings are covered by reproducible query fixtures.

## Required Verification

### Frontend Verify

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

Result: PASS.

Evidence:

- `npm ci` completed.
- `tsc --noEmit` completed.
- Vitest: 5 files passed, 41 tests passed.
- Vite production build completed.

### Browser Screenshot Retest

Command:

```powershell
& "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe" .\artifacts\webview2-full-visual-p1-retest-20260523\capture-full-visual-p1-retest-evidence.cjs
```

Result: PASS.

Automated checks:

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

### WebView2 Live Smoke

Command:

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-evidence artifacts\webview2-full-visual-p1-retest-20260523\webview2-live-smoke.json --web-ui-screenshot artifacts\webview2-full-visual-p1-retest-20260523\webview2-live-smoke.png --web-ui-timeout-ms 120000
```

Result: PASS.

Key fields:

- `success=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=false`
- `console=[]`
- `errors=[]`
- `smokePayload.reportLiveActionSmoke.success=true`
- `smokePayload.bridgeExtensionSmoke.success=true`

### WebView2 Reduced-Motion Smoke

Command:

```powershell
.\FrameScopeMonitor.exe --web-ui-smoke --web-ui-reduced-motion --web-ui-evidence artifacts\webview2-full-visual-p1-retest-20260523\webview2-reduced-motion-smoke.json --web-ui-screenshot artifacts\webview2-full-visual-p1-retest-20260523\webview2-reduced-motion-smoke.png --web-ui-timeout-ms 120000
```

Result: PASS.

Key fields:

- `success=true`
- `pageReady=true`
- `usingReactFrontend=true`
- `reducedMotion=true`
- `console=[]`
- `errors=[]`
- `smokePayload.reportLiveActionSmoke.success=true`
- `smokePayload.bridgeExtensionSmoke.success=true`

### Motion Frame Evidence

Result: PASS.

Evidence:

- 12 normal WebView2 transition PNG files.
- 12 reduced-motion WebView2 transition PNG files.
- Screenshots show stable route pages without mixed old/new page frames.

### Git Diff Check

Command:

```powershell
git diff --check
```

Result: PASS.

Notes:

- Exit code was 0.
- Git printed existing LF-to-CRLF warnings for dirty files, but no whitespace errors.

### Residual Process Check

Result: PASS.

Checked:

- `FrameScopeMonitor.exe`
- `FrameScopeProcessSampler.exe`
- `FrameScopeSystemSampler.exe`
- `FrameScopeReportGenerator.exe`
- `PresentMon*.exe`
- project-related `node.exe`
- project/WebView2 smoke or retest `msedge.exe`
- listener ports `4253`, `9423`, `4261`, `5173`, `5177`

No matching project-related residual process or listener remained in the final filtered checks. General `msedge.exe` processes existed on the machine, but none matched this retest directory, WebView2 temp profile, or the retest CDP/static ports.

## Worktree Notes

The worktree was already dirty from the previous P1 fix lane and related reports before this retest. This retest did not modify product UI source code or C# bridge semantics. New retest outputs are limited to:

- `artifacts\webview2-full-visual-p1-retest-20260523\...`
- `docs\test-reports\2026-05-23-framescope-webview2-full-visual-p1-retest-report.md`

## Final Recommendation

建议进入最终打包验证。

Reason:

- 10 个 P1 已通过截图复测和 WebView2 smoke。
- 900 宽和 1280 宽关键布局均无横向撑破。
- Reports 菜单不再飘、不跑偏、不遮挡关键内容。
- Targets 大结果集可读且内部滚动。
- Settings 长路径和失败保存不撑破、不乱码、不裁切。
- reduced-motion smoke 和普通 live smoke 都通过。

Packaging validation should still verify installer payload sync, installed app launch, WebView2 frontend assets copied into payload, clean install/upgrade behavior, and installed residual process cleanup. This retest is sufficient to unblock that next stage.
