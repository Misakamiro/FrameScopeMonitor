# 诊断脚本 Windows PowerShell 5.1 编码修复

用户截图中的首次命令在 `.ps1` 后多输入反斜杠，导致文件扩展名检查失败。第二次命令已正确启动，真正中断诊断文件生成的是 `preview-native-settings.json` 的 JSON 解析错误。

旧脚本在末尾读取配置时使用 `Get-Content -Raw`，没有指定编码。在 Windows PowerShell 5.1 的默认 ANSI 编码环境下，无 BOM 的 UTF-8 中文被错误解码；部分字符连带破坏字符串引号，导致 ConvertFrom-Json 终止。由于 `$ErrorActionPreference='Stop'`，之前已经采集的窗口数据也没有机会保存。

本次修改：

- 设置和构建信息统一通过 UTF-8 读取。
- 单个配置文件不存在时保留缺失状态；读取或解析失败时记录到 `configurationReadErrors`，其余数据继续输出。
- 输出保留原 schema=1，并添加 collectorVersion=2，便于区分工具版本。
- 提供 `Run-OverlayDiagnostics.cmd`，使用自身所在目录的绝对脚本路径启动 Windows PowerShell。无需用户手工输入脚本路径。
- 分发脚本使用 UTF-8 BOM 编码，CMD 使用 ASCII / CRLF。

离线回归先在旧脚本下复现 `Unterminated string passed in` 和乱码，再验证四种场景：UTF-8 无 BOM 中文、设置 JSON 损坏、设置与构建信息均损坏、UTF-8 带 BOM 中文。所有场景都检查输出文件存在且包含完整的模拟窗口记录；失败场景还检查对应错误条目。

Windows PowerShell `5.1.26100.6584`：四项场景通过。测试在独立 PowerShell 进程中使用进程内模拟 Capture 类型和 TEMP 配置，不读取真实窗口、不启动 FrameScope／游戏、不弹出 UAC、不修改真实配置。PowerShell 7 的离线兼容结果和 r2 ZIP 内容核对另存于 `artifacts/20261021-overlay-script-fix`。

本次仅修复与分发诊断脚本；没有生成软件安装包、公开软件发行或覆盖本机软件。
