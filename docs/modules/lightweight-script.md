# GameLite 自动轻量化迁移说明

GameLite 自动轻量化已经从 FrameScope Monitor 源码树中提取为独立项目。

新项目路径：

```text
C:\Users\misakamiro\Documents\Codex\2026-05-02\gamelite-auto-lightweight
```

FrameScope Monitor 主程序、C# 采样器、报告生成器、`build.ps1` 和 `tests\Build-FrameScopeTests.ps1` 不依赖 GameLite，也不要求 WMI trigger 存在。

旧 FrameScope 项目根目录仍保留同名 `.ps1` wrapper 和 `.cmd` 启动器，仅用于兼容旧 WMI consumer、旧快捷方式和用户手动入口。wrapper 会转发到新的独立 GameLite 项目。

## 新项目核心文件

- `Install-GameLiteAutoTrigger.ps1`
- `Check-GameLiteAutoTrigger.ps1`
- `Remove-GameLiteAutoTrigger.ps1`
- `GameLiteSession.ps1`
- `Enter-GameLite.ps1`
- `Exit-GameLite.ps1`
- `Invoke-GameLiteSGuardThrottle.ps1`
- `tests\gamelite-standalone-tests.ps1`

## 当前规则

- SGuard 默认压制。
- `-AllowSGuardThrottle` 只作为兼容参数保留。
- 使用 `-DisableSGuardThrottle` 关闭 SGuard 压制。
- 默认 SGuard 策略：PriorityClass=Idle、IO priority=0、page priority=1、CPU affinity=最后两个逻辑核心。
- `-StrictSGuard` 使用最后一个逻辑核心。
- 默认不允许 kill、suspend、禁用服务、卸载、删除文件、重命名文件或 Job Object CPU hard cap。
- 自动触发应使用 WMI `Win32_ProcessStartTrace` 和 `Win32_ProcessStopTrace`。
- 不要重新引入长期 PowerShell 轮询作为默认游戏进程监测方式。
- `Exit-GameLite.ps1` 只能按本次保存的 snapshot 恢复。
- `Enter-GameLite.ps1` 即使没有可降级进程，也应写入空 JSON snapshot 标记 `[]`，让 SGuard late-start 触发器能识别当前 GameLite 会话处于活动状态；`Exit-GameLite.ps1` 负责移除该状态标记。

## 验证

新项目验证：

```powershell
cd C:\Users\misakamiro\Documents\Codex\2026-05-02\gamelite-auto-lightweight
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\gamelite-standalone-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Check-GameLiteAutoTrigger.ps1
```

旧 FrameScope 项目兼容桥验证：

```powershell
cd C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\lightweight-separation-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Check-GameLiteAutoTrigger.ps1
```

不要在没有用户明确授权时安装、移除或重建 WMI trigger。
