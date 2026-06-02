# FrameScope Settings Open Log Directory Implementation Report

Conclusion: PASS

## Scope

Added a Settings -> 日志与诊断 action for opening the current FrameScope log directory through the real WebView2 bridge and C# host adapter.

Non-goals honored:
- Did not test BF6.
- Did not start a real game.
- Did not run an installer.
- Did not push GitHub or update a Release.

## Actual Directories

Actual log storage in the current source-host implementation:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

Actual watcher log file:

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\framescope-watcher.log`

This is different from the report/data root:

`%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs`

This is also different from the diagnostic-report root:

`%LOCALAPPDATA%\FrameScopeMonitorData\diagnostic-reports`

The new Settings button opens only the current log directory. It does not open reports, runs, or charts.

## Implementation

Bridge action name:

`logs.openDirectory`

Host-side authority:

- The frontend sends `logs.openDirectory` with an empty payload.
- `FrameScopeWebBridge` rejects frontend path authority such as `path`, `runDir`, `reportHtml`, `directory`, or `file`.
- `FrameScopeWebBridgeHostContext.LogDirectory` is resolved on the C# side from the current app root, matching the existing `WriteFrameScopeLog(Path.Combine(Root, "framescope-watcher.log"))` policy.
- `FrameScopeNativeWebBridgeHostAdapter.OpenLogsDirectory` creates the directory if needed and opens it through the existing ShellExecute/Explorer path-opening helper.

Directory-missing behavior:

- Host calls `Directory.CreateDirectory(logDirectory)` before opening.
- If creation fails, it returns `log_directory_create_failed` with a Chinese error message for frontend display.
- If Explorer/Shell opening fails, it returns `log_directory_open_failed`.

Frontend behavior:

- Settings -> 日志与诊断 now shows a secondary `打开日志目录` button.
- Click state disables the button while opening.
- Success shows `日志目录已打开`.
- Failure shows `日志目录打开失败` with the returned error.
- Mock preview implements the same action and rejects path authority for UI/contract tests.

Logging policy:

- Did not change default logging volume.
- Did not change verbose logs, performance diagnostics logs, or automatic diagnostics toggles.
- Did not move watcher logging to a new folder.

## Tests Added Or Updated

- `tests\FrameScopeWebBridgeTests.cs`
  - `logs.openDirectory` rejects frontend path payload.
  - `logs.openDirectory` uses the host-resolved log directory.
  - Missing log directory is created by host before opening.

- `src\frontend\src\uiInteractionContract.test.ts`
  - Verifies Settings exposes `打开日志目录`.
  - Verifies frontend calls `logs.openDirectory` without a path.
  - Verifies mock preview keeps the same path-authority boundary.

- WebView2 smoke harness:
  - Verifies `logs.openDirectory` path payload is rejected.
  - Verifies `logs.openDirectory` succeeds through the live host bridge.

## Verification

PASS:

- `tools\Run-Frontend.ps1 verify`
  - Typecheck PASS
  - Vitest PASS: 5 files, 56 tests
  - Frontend production build PASS
- `build.ps1` PASS
- `tests\Build-FrameScopeTests.ps1` PASS
- `tests\FrameScopeWebBridgeTests.exe` PASS
- `tests\FrameScopeConfigStoreTests.exe` PASS
- `tests\FrameScopeDiagnosticsTests.exe` PASS
- `tests\FrameScopeLoggingPolicyTests.exe` PASS
- WebView2 live smoke PASS
  - `artifacts\logs-open-directory-20260529\webview2-live-smoke.json`
  - `logsOpenPathRejected=true`
  - `logsOpenDirectoryOk=true`
- WebView2 reduced-motion smoke PASS
  - `artifacts\logs-open-directory-20260529\webview2-reduced-motion-smoke.json`
  - `logsOpenPathRejected=true`
  - `logsOpenDirectoryOk=true`
- Screenshot evidence PASS
  - `artifacts\logs-open-directory-20260529\webview2-live-smoke-settings-sampling.png`
  - Shows Settings -> 日志与诊断 -> `打开日志目录`
- `git diff --check` PASS
  - Only existing LF/CRLF warnings were printed.
- Residual process check PASS
  - No `FrameScope*` or `PresentMon*` process remained after smoke.
  - Existing `explorer.exe` and older `msedgewebview2.exe` processes were observed and were not started by this run.

## Recommendation

建议进入复测窗口: Yes.

Reason: implementation, unit/contract tests, live WebView2 smoke, reduced-motion smoke, screenshot evidence, diff check, and residual process check all passed. Retest should focus on an installed-app click of `打开日志目录` after the next packaging/update window, because this task intentionally did not run the installer.
