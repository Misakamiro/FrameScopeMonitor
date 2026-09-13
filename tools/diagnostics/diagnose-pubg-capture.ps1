# FrameScope 采集异常自助诊断工具
# 用法:
#   powershell -NoProfile -ExecutionPolicy Bypass -File diagnose-pubg-capture.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File diagnose-pubg-capture.ps1 -RunDir "<运行目录>"
# 适用: 监控界面显示「采集进程异常退出，未能生成报告」等采集失败场景。
param([string]$RunDir)

$ErrorActionPreference = 'Continue'

if (-not $RunDir) {
    $runsRoot = Join-Path $env:LOCALAPPDATA 'FrameScopeMonitorData\framescope-runs'
    $latest = Get-ChildItem -LiteralPath $runsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'PUBG*' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($latest) { $RunDir = $latest.FullName }
}

Write-Host ''
Write-Host '============================================' -ForegroundColor Cyan
Write-Host ' FrameScope 采集异常诊断 (PUBG)' -ForegroundColor Cyan
Write-Host (' 生成时间: {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -ForegroundColor DarkGray
Write-Host '============================================' -ForegroundColor Cyan

if (-not $RunDir -or -not (Test-Path -LiteralPath $RunDir)) {
    Write-Host ''
    Write-Host ' [X] 未找到 PUBG 运行目录。' -ForegroundColor Red
    Write-Host "     期望位置: $env:LOCALAPPDATA\FrameScopeMonitorData\framescope-runs\PUBG*" -ForegroundColor Yellow
    Write-Host '     若刚失败, 请先用 FrameScope 复现一次采集失败, 再运行本脚本。' -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host (" 运行目录: {0}" -f $RunDir)

# --- 管理员权限 ---------------------------------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    Write-Host ' [OK] 当前以管理员身份运行' -ForegroundColor Green
} else {
    Write-Host ' [!] 当前不是管理员 - ETW 采集可能因此被拒 (权限不足是常见根因之一)' -ForegroundColor Yellow
}

# --- status.json --------------------------------------------------------------
$statusPath = Join-Path $RunDir 'status.json'
if (-not (Test-Path -LiteralPath $statusPath)) {
    Write-Host ' [X] status.json 不存在 - 会话可能未正常启动' -ForegroundColor Red
    Write-Host '     请把整个运行目录打包反馈给开发者。' -ForegroundColor Yellow
    exit 1
}

$status = Get-Content -Raw -LiteralPath $statusPath -Encoding UTF8 | ConvertFrom-Json

Write-Host ''
Write-Host ' [1] 会话状态' -ForegroundColor Yellow
Write-Host ("     Phase                : {0}" -f $status.Phase)
Write-Host ("     ExitCode             : {0}" -f $status.ExitCode)
Write-Host ("     FrameCaptureStatus   : {0}" -f $status.FrameCaptureStatus)
Write-Host ("     PresentMonExitCode   : {0}" -f $status.PresentMonExitCode)
Write-Host ("     PresentMonExitedEarly: {0}" -f $status.PresentMonExitedEarly)
if ($status.FrameCaptureMessage) { Write-Host ("     消息: {0}" -f $status.FrameCaptureMessage) }
if ($status.ReportError) { Write-Host ("     ReportError          : {0}" -f $status.ReportError) -ForegroundColor Red }

Write-Host ''
Write-Host ' [2] 根因判断' -ForegroundColor Yellow
switch ("$($status.FrameCaptureStatus)") {
    'presentmon-no-csv-silent' {
        Write-Host '     [X] PresentMon 静默无数据 - 大概率是反作弊 (BattlEye) 在内核层' -ForegroundColor Red
        Write-Host '         拦截了 ETW trace 会话; PresentMon 进程正常但收不到帧事件。' -ForegroundColor Red
        Write-Host '     建议:' -ForegroundColor Yellow
        Write-Host '       1. 以管理员身份运行 FrameScope 后重试' -ForegroundColor White
        Write-Host '       2. 游戏切换到「无边框窗口」模式' -ForegroundColor White
        Write-Host '       3. 检查下方 hardware-samples.csv 是否有数据 (自动降级数据源)' -ForegroundColor White
    }
    'presentmon-etw-access-denied' {
        Write-Host '     [X] ETW 权限不足 - trace 会话启动被拒绝。' -ForegroundColor Red
        Write-Host '     建议: 以管理员身份运行 FrameScope; 或将用户加入' -ForegroundColor Yellow
        Write-Host '           "Performance Log Users" 组后重新登录。' -ForegroundColor Yellow
    }
    'timeout-waiting-for-target' {
        Write-Host '     [X] 超时未检测到 PUBG 渲染进程 (TslGame.exe)。' -ForegroundColor Red
        Write-Host '     建议: 确认已进入游戏主界面 (不是启动器/大厅加载页);' -ForegroundColor Yellow
        Write-Host '           在任务管理器确认 PUBG 实际进程名后反馈开发者。' -ForegroundColor Yellow
    }
    'presentmon-exited-early' {
        Write-Host '     [X] PresentMon 提前退出 (游戏尚未结束就崩了)。' -ForegroundColor Red
        Write-Host '     建议: 关闭其他性能监控工具 (RTSS/Afterburner 等, 存在 ETW' -ForegroundColor Yellow
        Write-Host '           会话冲突), 重试; 仍复现请把运行目录反馈给开发者。' -ForegroundColor Yellow
    }
    default {
        Write-Host ("     状态: {0}" -f $status.FrameCaptureStatus)
        if ($status.FrameCaptureMessage) { Write-Host ("     消息: {0}" -f $status.FrameCaptureMessage) }
    }
}

# --- 采样数据文件 ---------------------------------------------------------------
Write-Host ''
Write-Host ' [3] 采样数据文件' -ForegroundColor Yellow
$dataFiles = [ordered]@{
    'presentmon.csv'       = 'PresentMon 帧数据 (ETW 直采)'
    'process-samples.csv'  = '进程 CPU/内存/IO 采样'
    'system-samples.csv'   = '系统采样'
    'hardware-samples.csv' = '硬件采样 (含 1Hz FPS, 自动降级数据源)'
}
foreach ($entry in $dataFiles.GetEnumerator()) {
    $file = Join-Path $RunDir $entry.Key
    if (Test-Path -LiteralPath $file) {
        $lines = 0
        try { $lines = @(Get-Content -LiteralPath $file).Count } catch {}
        Write-Host ("     [OK] {0,-22} {1} 行" -f $entry.Key, $lines) -ForegroundColor Green
    } else {
        Write-Host ("     [X]  {0,-22} 不存在" -f $entry.Key) -ForegroundColor Red
    }
}
$hwCsv = Join-Path $RunDir 'hardware-samples.csv'
if ((Test-Path -LiteralPath $hwCsv) -and ((Get-Item -LiteralPath $hwCsv).Length -gt 200)) {
    Write-Host '     -> hardware-samples.csv 有数据: 即使 PresentMon 被拦, 报告生成器' -ForegroundColor DarkGray
    Write-Host '        也可从该文件重建 FPS 序列 (标记 etw-fps-sampled)。' -ForegroundColor DarkGray
}

# --- PresentMon 日志摘要 ---------------------------------------------------------
$stderrLog = Join-Path $RunDir 'presentmon.stderr.log'
if (Test-Path -LiteralPath $stderrLog) {
    Write-Host ''
    Write-Host ' [4] PresentMon stderr (前 8 行)' -ForegroundColor Yellow
    Get-Content -LiteralPath $stderrLog -TotalCount 8 | ForEach-Object { Write-Host ("     {0}" -f $_) -ForegroundColor DarkGray }
}

# --- 硬件服务 --------------------------------------------------------------------
Write-Host ''
Write-Host ' [5] Hardware Service 状态' -ForegroundColor Yellow
$hw = Get-Process -Name 'FrameScopeHardwareService', 'FrameScopeHardwareProvider' -ErrorAction SilentlyContinue
if ($hw) {
    $hw | ForEach-Object { Write-Host ("     [OK] {0} (PID {1})" -f $_.Name, $_.Id) -ForegroundColor Green }
} else {
    Write-Host '     [!] 未在运行 (监控未启动时属正常; 监控中缺失说明服务没起来)' -ForegroundColor Yellow
}

# --- PawnIO 驱动 -------------------------------------------------------------------
$pawnKey = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO' -ErrorAction SilentlyContinue
if ($pawnKey) {
    Write-Host ''
    Write-Host ' [6] PawnIO 温度驱动: 已安装' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host ' [6] PawnIO 温度驱动: 未安装 (温度监控不可用)' -ForegroundColor Yellow
    Write-Host '     在 FrameScope 设置中点击「安装温度驱动」(需管理员权限)。' -ForegroundColor Yellow
}

# --- 残留 ETW 会话 ------------------------------------------------------------------
$etw = logman query -ets 2>$null | Select-String -Pattern 'PresentMon|NT Kernel Logger|FrameScope'
if ($etw) {
    Write-Host ''
    Write-Host ' [7] 检测到可能冲突的残留 ETW 会话:' -ForegroundColor Yellow
    $etw | ForEach-Object { Write-Host ("     {0}" -f $_.Line.Trim()) -ForegroundColor DarkGray }
    Write-Host '     若 FrameScope 未在监控中, 可用 logman stop "<会话名>" -ets 清理。' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '============================================' -ForegroundColor Cyan
Write-Host ' 反馈问题请连同整个运行目录一并打包发送。' -ForegroundColor Cyan
Write-Host '============================================' -ForegroundColor Cyan
