# FrameScope Monitor installed app WebView2 smoke PARTIAL 澄清诊断

日期：2026-06-13
结论：PASS

## 范围

本轮只调查本地安装验证中的 installed app WebView2 smoke 不稳定问题，并给出 root cause、复现链路、fresh 复跑结果和结论。

本轮工作区：
`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

安装版路径：
`C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe`

本轮明确未做：
- 未重新安装。
- 未打包。
- 未启动真实游戏。
- 未测试 BF6。
- 未推 GitHub。
- 未更新 Release。

## 结论摘要

之前 local install validation 报告里的 installed app smoke `PARTIAL`，根因不是 installed app WebView2 UI/bridge 回归，也不是单实例 Mutex 把 smoke 拦住。

这次澄清诊断确认了两层问题：

1. `-532462766` 的真实异常是 .NET 未处理 `System.IO.PathTooLongException`。
2. 后续“简化重试超时”不是产品 UI 崩，而是 smoke harness 在短路径但空 report history / 空 temp dataRoot 前提下，跑到了需要历史 report 的 live action 步骤，因此拿不到可操作 report，最后超时。

把 smoke 改成：
- 短路径 temp config
- config 放在安装目录内允许的 `smoke-temp` 下
- live/reduced 复用安装版真实 `framescope-history.jsonl` 与真实 data root
- target/settings 使用短路径 evidence / screenshot 输出

之后，fresh installed smoke 全部通过：
- installed live smoke：PASS
- installed reduced-motion smoke：PASS
- target add/edit/delete -> finalTargetCount=0：PASS
- Settings 保存/读取：PASS

因此，之前的 `PARTIAL` 应判定为 smoke harness / 证据路径 / 运行前提问题，不是 installed 产品 bug。

## 背景与现场

### 1. 启动前进程检查

开始调查时，我先检查现有 `FrameScopeMonitor` / `msedgewebview2` 相关进程。

结果：
- 没有残留 `FrameScopeMonitor.exe` 正在运行。
- 系统中存在其他应用创建的 `msedgewebview2.exe` 进程，但没有安装版 `FrameScopeMonitor.exe` 残留。

本轮所有需要关闭的安装版 UI，都先尝试 `CloseMainWindow()`；没有对可响应普通 UI 做 force kill。

### 2. 安装版日志与旧报告比对

确认安装路径存在：
- `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe`

确认安装日志存在：
- `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\install.log`

安装日志显示 2026-06-13 17:46:57 +08:00 quiet 安装完成，WebView2 runtime 可用，和之前 PARTIAL 报告背景一致。

## `-532462766` 的真实 root cause

### 1. Windows 事件日志拿到的真实异常栈

我直接查询了 Windows `Application` 日志中的 `.NET Runtime` / `Application Error` / `Windows Error Reporting` 事件，命中多条和这次 installed smoke 时间一致的崩溃记录。

关键结果：
- `.NET Runtime` Event ID `1026`
- `Application Error` Event ID `1000`
- WER `CLR20r3`
- 异常类型：`System.IO.PathTooLongException`
- 调用栈：
  - `System.IO.PathHelper.GetFullPathName()`
  - `System.IO.Path.LegacyNormalizePath(...)`
  - `System.IO.Path.GetFullPathInternal(...)`
  - `FrameScopeNativeMonitor.RunWebUi(System.String[])`
  - `FrameScopeNativeMonitor.Main(System.String[])`

这说明 `-532462766` 不是“神秘 .NET 崩溃码”，而是典型 `0xe0434352` 托管未处理异常，对应 `PathTooLongException`。

### 2. 为什么会触发 PathTooLong

旧失败证据里，fresh installed smoke 使用的是很深的 repo 下 evidence/config 路径。实际长度如下：

- old live config path length：`287`
- old live evidence path length：`267`
- old reduced evidence path length：`270`
- old target/settings smoke JSON path length：`271` / `279`

而源码里 `RunWebUi(...)` 在进入 host 前，会先对 `--config` 做 `Path.GetFullPath(...)`，并据此派生 state/history 路径。这和事件日志里的栈完全对上。

结论：
- `-532462766` 的 root cause 是 smoke 启动参数里的超长路径触发 `PathTooLongException`。
- 这是 harness/evidence path 设计问题，不是 WebView2 runtime、bridge、前端页面或安装版主程序逻辑回归。

## 为什么“简化重试”还会超时

把路径缩短后，`-532462766` 消失了，但我先故意用“短路径 + 空 temp config/dataRoot/history”重跑 live smoke，结果没有崩溃，页面也能正常加载，但最终超时。

日志显示：
- 页面加载成功
- `config.get` / `targets.get` 成功
- 但 `reports.list` 返回 `count=0`
- live smoke 后续需要从 Reports 页抓一个可 `open/openDirectory/regenerate` 的 report
- 因为空 history / 空 dataRoot，没有可操作 report，最后 timeout

这说明第二层问题也不是产品 bug，而是 harness 前提不完整：
- live/reduced smoke 不是只验证“页面能打开”
- 它还要求有真实历史 report 可做 live action
- 如果给的是孤立 temp config 且没接真实 history/dataRoot，就会因无 report 可操作而超时

## fresh installed smoke 复跑

## 1. live / reduced-motion fresh PASS

我把 live/reduced smoke 改成下面的执行方式：
- temp config 仍放在安装目录内的 `smoke-temp`
- evidence / screenshot 改到短路径 `C:\fs-smoke\...`
- 通过 `--history` / `--state` 指向安装版真实 `framescope-history.jsonl` / `framescope-watcher-state.json`
- temp config 的 `DataRoot` 指向真实 `%LOCALAPPDATA%\FrameScopeMonitorData\framescope-runs`

fresh 结果：
- live smoke：`success=true`
- reduced smoke：`success=true`
- `reportLiveActionSmoke.success=true`
- `bridgeExtensionSmoke.success=true`
- 页面加载、Reports live action、logs open、diagnostics、monitor start/stop 都通过

关键证据：
- `C:\fs-smoke\0613-live3\ev\live.json`
- `C:\fs-smoke\0613-live3\ev\reduced.json`

说明：
- 只要 smoke harness 提供正确的短路径与真实 history/dataRoot 前提，installed live/reduced smoke 可以稳定 fresh PASS。
- 因此 installed WebView2 live smoke 并不存在真实回归。

## 2. target add/edit/delete + Settings 保存/读取 fresh PASS

我再用短路径重跑 installed target/settings smoke：
- temp config 仍放在安装目录 `smoke-temp`
- evidence / screenshot 输出改到 `C:\fs-smoke\0613-ts\...`

fresh 结果：
- `targetAddSaved=true`
- `targetEditSaved=true`
- `targetDeleteSaved=true`
- `targetEditNoPerTargetSampling=true`
- `settingsSaved=true`
- `savedTelemetrySampleIntervalMs=1375`
- `settings restart persistence actualTelemetrySampleIntervalMs=1375`
- 两次 smoke 都在短时间内写出 smoke JSON 与 screenshots
- 不再出现旧报告里的挂住 hidden helper / caller timeout

关键证据：
- `C:\fs-smoke\0613-ts\ev\target.json`
- `C:\fs-smoke\0613-ts\ev\settings.json`

结论：
- target add/edit/delete -> finalTargetCount=0 有 fresh installed PASS 证据。
- Settings 保存/读取有 fresh installed PASS 证据。
- 旧报告里的 target/settings 不稳定，同样是 harness 路径/执行前提问题，不是 installed 产品功能坏掉。

## 单实例锁是否影响 smoke

结论：**不影响。**

证据分三层：

1. 源码层：
   - `src\app\FrameScopeNativeMonitor.SingleInstance.cs` 明确把以下参数排除在普通 UI Mutex 之外：
     - `--web-ui-smoke`
     - `--web-ui-target-settings-evidence-smoke`
     - `--web-ui-settings-persistence-read-smoke`

2. 回归测试层：
   - `FrameScopeSingleInstanceLaunchGuardTests.exe` fresh PASS
   - 其中明确断言 `--web-ui-smoke` 必须 bypass ordinary UI lock

3. 运行态层：
   - 我在普通 UI 已打开时启动 smoke，smoke 自己能正常起 WebView2、完成导航并跑进 smoke 逻辑，说明它并没有在进程入口被单实例锁直接拦住。
   - 那次并未 PASS 的原因是我故意给了空 temp dataRoot/history 前提，导致 live action 无 report 可操作而 timeout；不是 Mutex 拦截。

关于“普通重复启动会提示已运行”：
- 本轮 fresh 自动测试 `FrameScopeSingleInstanceLaunchGuardTests.exe` 已验证重复 UI 保护与中文提示文案。
- 我还手动触发了普通 UI 重复启动，看到重复启动路径会留下一个可见 `FrameScope Monitor` 窗口，行为与“已在运行，请勿重复打开”提示链一致。
- 但本轮并没有再额外做单独截图捕获该提示框，因为这次 root cause 已经明确不在单实例锁上。

因此本轮明确结论是：
- 普通 UI 单实例保护仍然存在。
- smoke 参数设计上应绕过锁。
- smoke 参数实际运行时也确实绕过了锁。
- 单实例锁不是这次 installed smoke `PARTIAL` 的原因。

## 是否确认 installed app 真实功能正常

可以确认，针对本任务要求的 installed app WebView2 smoke 能力，本轮已经拿到 fresh 正常证据：

- live smoke：正常
- reduced-motion smoke：正常
- target add/edit/delete：正常
- Settings 保存/读取：正常
- bridge extension / logs open / diagnostics / monitor start-stop：正常

所以可以明确说：
- 之前的 PARTIAL 不是 installed app 真实功能异常
- 当前确认 installed app 真实功能正常

## 是否改了源码或测试工具

没有改源码，也没有改测试工具。

本轮只做了：
- 读日志
- 查事件日志
- 清理/关闭进程
- 重跑 smoke
- 调整 smoke 启动路径与前提
- 跑 fresh 验证命令
- 写诊断报告

没有修改任何仓库源码文件。

## 必跑验证命令结果

以下命令都已 fresh 运行：

1. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`
   - PASS
   - `6` 个 test files 通过，`64` 个 tests 通过，production build 通过

2. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1`
   - PASS

3. `.\tests\FrameScopeSingleInstanceLaunchGuardTests.exe`
   - PASS

4. `.\tests\FrameScopeNativeMonitorChildProcessTests.exe`
   - PASS

5. `.\tests\FrameScopeProcessCleanupTests.exe`
   - PASS

6. `.\tests\FrameScopeReportManifestTests.exe`
   - PASS

7. bundled Node 运行 `.\tests\chart-sampling-tests.js`
   - PASS

8. `git diff --check`
   - PASS
   - 只有既有 `LF -> CRLF` warning，没有 whitespace error

命令日志目录：
`docs\test-reports\2026-06-13-framescope-installed-webview2-smoke-partial-clarification-evidence\command-logs`

## 最终残留进程检查

最终结果：
`NO_MATCHING_RESIDUAL_PROCESSES`

没有残留 `FrameScopeMonitor` / `FrameScopeProcessSampler` / `FrameScopeSystemSampler` 进程。

## 最终判断

PASS。

具体判断如下：

- `.NET -532462766` root cause：**`System.IO.PathTooLongException`，发生在 `FrameScopeNativeMonitor.RunWebUi(...)` 的路径归一化阶段。**
- installed live smoke 是否 fresh PASS：**PASS**
- installed reduced smoke 是否 fresh PASS：**PASS**
- target add/edit/delete 是否 fresh PASS：**PASS**
- Settings 保存/读取是否 fresh PASS：**PASS**
- 单实例锁是否影响 smoke：**不影响**
- 是否确认 installed app 真实功能正常：**确认正常**
- 是否有源码/测试工具改动：**没有**
- 最终残留进程结果：**`NO_MATCHING_RESIDUAL_PROCESSES`**

因此，旧报告中的 installed app smoke `PARTIAL` 应修正理解为：
- primary cause：**harness long-path false negative**
- secondary cause：**live smoke 使用了空 temp history/dataRoot 前提，导致 live action 无 report 可操作而超时**
- not a product regression

## 主要证据位置

旧失败证据：
- `docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-chart-dropout-and-vid-fix-evidence\installed-webview2-smoke-fresh\summary.json`
- `docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-chart-dropout-and-vid-fix-evidence\liv-ts-fresh\target-settings-evidence-summary.json`

本轮诊断摘要：
- `docs\test-reports\2026-06-13-framescope-installed-webview2-smoke-partial-clarification-evidence\diagnostic-summary.json`

本轮 fresh short-path installed smoke：
- `C:\fs-smoke\0613-live3\ev\live.json`
- `C:\fs-smoke\0613-live3\ev\reduced.json`
- `C:\fs-smoke\0613-ts\ev\target.json`
- `C:\fs-smoke\0613-ts\ev\settings.json`

最终残留进程检查：
- `docs\test-reports\2026-06-13-framescope-installed-webview2-smoke-partial-clarification-evidence\final-residual-process-check.txt`
