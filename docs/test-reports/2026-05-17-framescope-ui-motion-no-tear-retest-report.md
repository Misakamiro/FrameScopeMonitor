# FrameScope Monitor UI Motion No-Tear 轻量复测报告

日期：2026-05-17
角色：FrameScope Monitor 测试员
项目路径：`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

## 1. 当前复测结论

不通过。

本轮只复测 UI motion，不重新阻塞 manifest JSON 和 Settings polish。轻量构建、测试和 `git diff --check` 均通过，但 `FSM-RETEST-MOTION-001` 未解除：新连续帧中仍能看到旧页面和新页面混在同一帧，且存在 active nav 与页面内容不同步的中间帧。

## 2. `FSM-RETEST-MOTION-001` 是否解除阻塞

未解除。

阻塞现象仍集中在页面切换 commit 过程：

- `targets -> reports`：`transition-targets-reports-02.png` 仍出现 Targets 页面左侧主内容与 Reports/Settings 右侧区域横向混绘，画面中间有明显竖向割裂；同时 sidebar active 仍停在“监控目标”，但右侧已经出现新页面内容。
- `reports -> settings`：`transition-reports-settings-02.png` 显示 Settings 页面主体，但 sidebar active 仍停在“报告”；该帧还保留旧页面右侧/底部区域视觉痕迹，属于 active nav 和页面内容不同步。

## 3. 读取的文件

- `docs\test-reports\2026-05-17-framescope-ui-motion-manifest-retest-report.md`
- `docs\implementation-reports\2026-05-17-framescope-ui-motion-implementation-report.md`
- `src\app\FrameScopeNativeMonitor.UiRouting.cs`
- `src\ui\FrameScopeMotion.cs`

本轮调用并应用的 skills：`health`、`verification-before-completion`、`diagnose`、`review`。其中 `review` 仅用于只读复核，不执行自动修复。

## 4. 命令验证结果

| 命令 | 结果 | 备注 |
|---|---:|---|
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | 输出 `Build complete: ...\dist\FrameScopeMonitor-Setup.exe` |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1` | PASS | 输出 `FrameScope tests rebuilt.` |
| `.\tests\FrameScopeUiStateTests.exe` | PASS | 输出 `FrameScopeUiStateTests: PASS` |
| `"C:\Program Files\Git\cmd\git.exe" diff --check` | PASS | exit 0，仅保留既有 LF/CRLF warning：`README.md`、`build.ps1`、`framescope-config.example.json` |

## 5. 连续帧目录

检查目录：

`artifacts\ui-motion-20260517-no-tear-frames`

该目录包含：

- `baseline-overview-00.png`
- `transition-overview-targets-00.png` 到 `transition-overview-targets-09.png`
- `transition-overview-targets-contact.png`
- `transition-targets-reports-00.png` 到 `transition-targets-reports-09.png`
- `transition-targets-reports-contact.png`
- `transition-reports-settings-00.png` 到 `transition-reports-settings-09.png`
- `transition-reports-settings-contact.png`

帧文件时间为 2026-05-17 10:10:55 到 10:12:41，晚于本轮 motion 相关源码 `FrameScopeNativeMonitor.UiRouting.cs` 的 2026-05-17 10:09:38 和 `FrameScopeMotion.cs` 的 2026-05-17 10:07:59。现有帧覆盖本轮要求的三个 transition，因此未重新生成连续帧。

## 6. 逐帧检查结论

| Transition | 逐帧结论 | 结果 |
|---|---|---:|
| `overview -> targets` | `00-02` 保持 Overview，`03-09` 进入 Targets；未见旧/新横向并排，也未见空卡片骨架；active nav 与页面内容基本同步。 | PASS |
| `targets -> reports` | `00-01` 为 Targets；`02` 出现明显旧 Targets 左半页 + 新 Reports/Settings 右侧区域混绘，并且 active nav 仍在“监控目标”；`03-09` 进入 Reports。 | FAIL |
| `reports -> settings` | `00-01` 为 Reports；`02` 已显示 Settings 主体，但 active nav 仍在“报告”，并可见旧页面右侧/底部区域痕迹；`03-09` 进入 Settings 且 active nav 同步。 | FAIL |

## 7. 是否仍有混绘、撕裂、空骨架、active nav 不同步

- 旧页/新页横向并排或混绘：仍存在，最明显证据是 `transition-targets-reports-02.png`。
- 旧页残影和新页同时混在一个 frame：仍存在，`transition-targets-reports-02.png` 和 `transition-reports-settings-02.png` 均可见。
- 空卡片/控件骨架：本轮未作为主要问题复现，没有看到上一轮那种大面积空骨架帧。
- active nav 和页面内容同步：未完全同步，`transition-targets-reports-02.png` 和 `transition-reports-settings-02.png` 均不同步。
- 页面切换稳定瞬时 commit：未达到，仍能捕获中间撕裂帧。
- nav/button/status 局部动效：本轮未观察到独立的局部抖动或闪烁，主要失败点仍是页面内容替换过程。

## 8. 残留进程检查

最终检查未发现以下残留进程：

- `FrameScopeMonitor`
- `PresentMon`
- `FrameScopeProcessSampler`
- `FrameScopeSystemSampler`
- `FrameScopeReportGenerator`
- `FakePresentMon`
- `TslGame`
- `GameLite`

执行的检查命令基于 `Win32_Process`，过滤上述进程名后无输出。

## 9. 仍有问题

### FSM-RETEST-MOTION-001

- 严重级别：高，阻塞 motion 专项验收。
- 复现步骤：
  1. 打开 `artifacts\ui-motion-20260517-no-tear-frames\transition-targets-reports-contact.png`，重点查看第 `02` 帧。
  2. 单独打开 `artifacts\ui-motion-20260517-no-tear-frames\transition-targets-reports-02.png`。
  3. 打开 `artifacts\ui-motion-20260517-no-tear-frames\transition-reports-settings-contact.png`，重点查看第 `02` 帧。
  4. 单独打开 `artifacts\ui-motion-20260517-no-tear-frames\transition-reports-settings-02.png`。
- 实际结果：页面切换中间帧仍存在旧页/新页混绘、横向割裂，以及 active nav 与页面内容不同步。
- 期望结果：页面切换期间要么保留完整旧页，要么一次性切到完整新页；active nav 与页面内容必须在同一个可见 commit 点同步。
- 可能涉及文件：
  - `src\app\FrameScopeNativeMonitor.UiRouting.cs`
  - `src\ui\FrameScopeMotion.cs`
  - `src\ui\FrameScopeButtons.cs`
- 建议交给：UI 交互窗口修复。若继续保留 WinForms 原生控件，应优先重新审视 contentHost/form redraw lock 是否覆盖了实际截图捕获时机，以及是否需要先隐藏旧页、完整添加新页、一次性恢复 redraw 后再更新 active nav。

## 10. 是否建议进入最终 bug 修复及打包

建议进入 bug 修复，不建议进入最终打包。

理由：本轮轻量命令验证通过，但唯一复测目标 `FSM-RETEST-MOTION-001` 仍可复现。该问题是 UI motion 专项的阻塞项，建议先交给 UI 交互窗口继续修复，再进行一次同范围轻量复测；通过后再进入最终打包。
