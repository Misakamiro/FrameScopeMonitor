param(
    [string]$ConfigPath = '',
    [switch]$ExitAfterFirstRun,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'

try {
    [Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
}
catch {
}

$root = $PSScriptRoot
if (-not $ConfigPath) {
    $ConfigPath = Join-Path $root 'framescope-config.json'
}

$statePath = Join-Path $root 'framescope-watcher-state.json'
$logPath = Join-Path $root 'framescope-watcher.log'
$historyPath = Join-Path $root 'framescope-history.jsonl'
$monitorScript = Join-Path $root 'Monitor-CS2-HighFreq.ps1'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

function Write-FrameScopeLog {
    param([string]$Message)
    $line = '{0} {1}' -f (Get-Date).ToString('o'), $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

function ConvertTo-SafeName {
    param([string]$Name, [string]$Fallback = 'game')
    $text = if ($Name) { $Name } else { $Fallback }
    $safe = [Regex]::Replace($text, '[^\p{L}\p{Nd}\._-]+', '-').Trim('-')
    if (-not $safe) { $safe = $Fallback }
    return $safe
}

function Get-DefaultConfig {
    [pscustomobject]@{
        PollIntervalMs = 500
        DataRoot = (Join-Path $root 'framescope-runs')
        OpenReportOnComplete = $true
        MonitorScript = $monitorScript
        Targets = @(
            [pscustomobject]@{ Enabled = $true;  Name = 'Counter-Strike 2'; ProcessName = 'cs2.exe'; SampleIntervalMs = 100; ProcessSampleIntervalMs = 100; SlowSampleIntervalMs = 1000; OpenReportOnComplete = $true },
            [pscustomobject]@{ Enabled = $true;  Name = 'Delta Force'; ProcessName = 'DeltaForceClient-Win64-Shipping.exe'; SampleIntervalMs = 100; ProcessSampleIntervalMs = 100; SlowSampleIntervalMs = 1000; OpenReportOnComplete = $true },
            [pscustomobject]@{ Enabled = $true;  Name = 'Neverness To Everness'; ProcessName = 'HTGame.exe'; SampleIntervalMs = 100; ProcessSampleIntervalMs = 100; SlowSampleIntervalMs = 1000; OpenReportOnComplete = $true },
            [pscustomobject]@{ Enabled = $false; Name = 'Valorant'; ProcessName = 'VALORANT-Win64-Shipping.exe'; SampleIntervalMs = 100; ProcessSampleIntervalMs = 100; SlowSampleIntervalMs = 1000; OpenReportOnComplete = $true },
            [pscustomobject]@{ Enabled = $false; Name = 'Cyberpunk 2077'; ProcessName = 'Cyberpunk2077.exe'; SampleIntervalMs = 100; ProcessSampleIntervalMs = 100; SlowSampleIntervalMs = 1000; OpenReportOnComplete = $true },
            [pscustomobject]@{ Enabled = $false; Name = 'Battlefield 6'; ProcessName = 'bf6.exe'; SampleIntervalMs = 100; ProcessSampleIntervalMs = 100; SlowSampleIntervalMs = 1000; OpenReportOnComplete = $true },
            [pscustomobject]@{ Enabled = $false; Name = 'Hogwarts Legacy'; ProcessName = 'HogwartsLegacy.exe'; SampleIntervalMs = 100; ProcessSampleIntervalMs = 100; SlowSampleIntervalMs = 1000; OpenReportOnComplete = $true },
            [pscustomobject]@{ Enabled = $false; Name = 'OPUS Prism Peak'; ProcessName = 'OPUS_ Prism Peak.exe'; SampleIntervalMs = 100; ProcessSampleIntervalMs = 100; SlowSampleIntervalMs = 1000; OpenReportOnComplete = $true }
        )
    }
}

function Read-FrameScopeConfig {
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        $default = Get-DefaultConfig
        $default | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ConfigPath -Encoding UTF8
        return $default
    }

    try {
        $config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        Write-FrameScopeLog "config-read-failed $($_.Exception.Message)"
        return Get-DefaultConfig
    }

    if (-not $config.DataRoot) { $config | Add-Member -NotePropertyName DataRoot -NotePropertyValue (Join-Path $root 'framescope-runs') -Force }
    if (-not $config.PollIntervalMs) { $config | Add-Member -NotePropertyName PollIntervalMs -NotePropertyValue 500 -Force }
    if (-not $config.MonitorScript -or -not (Test-Path -LiteralPath ([string]$config.MonitorScript))) {
        $config | Add-Member -NotePropertyName MonitorScript -NotePropertyValue $monitorScript -Force
    }
    if ($null -eq $config.OpenReportOnComplete) { $config | Add-Member -NotePropertyName OpenReportOnComplete -NotePropertyValue $true -Force }
    return $config
}

function Get-TargetBaseName {
    param([string]$ProcessName)
    $base = [IO.Path]::GetFileNameWithoutExtension($ProcessName)
    if (-not $base) { $base = $ProcessName }
    return $base
}

function Get-LatestRun {
    param([string]$RunRoot)
    if (-not (Test-Path -LiteralPath $RunRoot)) { return $null }
    return Get-ChildItem -LiteralPath $RunRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

function Read-RunStatus {
    param([string]$RunDir)
    $statusPath = Join-Path $RunDir 'status.json'
    if (-not (Test-Path -LiteralPath $statusPath)) { return $null }
    try {
        return Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Add-HistoryEntry {
    param(
        $Target,
        [string]$RunDir,
        $Status,
        [int]$MonitorExitCode
    )

    $entry = [ordered]@{
        Time = (Get-Date).ToString('o')
        Game = [string]$Target.Name
        ProcessName = [string]$Target.ProcessName
        RunDir = $RunDir
        ReportHtml = if ($Status -and $Status.ReportHtml) { [string]$Status.ReportHtml } else { Join-Path $RunDir 'charts\framescope-interactive-report.html' }
        PresentMonCsv = if ($Status -and $Status.PresentMonCsv) { [string]$Status.PresentMonCsv } else { Join-Path $RunDir 'presentmon.csv' }
        ProcessCsv = if ($Status -and $Status.ProcessCsv) { [string]$Status.ProcessCsv } else { Join-Path $RunDir 'process-samples.csv' }
        SystemCsv = if ($Status -and $Status.SamplesCsv) { [string]$Status.SamplesCsv } else { Join-Path $RunDir 'system-samples.csv' }
        SummaryPath = if ($Status -and $Status.SummaryPath) { [string]$Status.SummaryPath } else { Join-Path $RunDir 'summary.json' }
        MonitorExitCode = $MonitorExitCode
    }
    ($entry | ConvertTo-Json -Compress -Depth 8) | Add-Content -LiteralPath $historyPath -Encoding UTF8
    return [pscustomobject]$entry
}

function Test-ProcessIdRunning {
    param([int]$ProcessId)
    if ($ProcessId -le 0) { return $false }
    return [bool](Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

function Get-MonitorPid {
    param($Item)
    if ($Item.MonitorPid) { return [int]$Item.MonitorPid }
    try {
        if ($Item.Process) { return [int]$Item.Process.Id }
    }
    catch {
    }
    return 0
}

function Test-MonitorExited {
    param($Item)
    try {
        if ($Item.Process) {
            $Item.Process.Refresh()
            return [bool]$Item.Process.HasExited
        }
    }
    catch {
    }

    $pid = Get-MonitorPid -Item $Item
    if ($pid -gt 0) {
        return -not (Test-ProcessIdRunning -ProcessId $pid)
    }
    return $true
}

function Get-MonitorExitCode {
    param($Item, $Status)
    try {
        if ($Item.Process) { return [int]$Item.Process.ExitCode }
    }
    catch {
    }
    try {
        if ($Status -and $null -ne $Status.ExitCode) { return [int]$Status.ExitCode }
    }
    catch {
    }
    return -1
}

function Write-WatcherState {
    param(
        [string]$Phase,
        [hashtable]$ActiveMonitors,
        [int]$CompletedRuns,
        [string]$LastReport = ''
    )

    $active = @()
    foreach ($key in $ActiveMonitors.Keys) {
        $item = $ActiveMonitors[$key]
        $active += [pscustomobject]@{
            Key = $key
            Game = $item.Target.Name
            ProcessName = $item.Target.ProcessName
            MonitorPid = $(try {
                $pid = Get-MonitorPid -Item $item
                if ($pid -gt 0 -and (Test-ProcessIdRunning -ProcessId $pid)) { $pid } else { $null }
            } catch { $null })
            RunRoot = $item.RunRoot
        }
    }

    [pscustomobject]@{
        Time = (Get-Date).ToString('o')
        Phase = $Phase
        ConfigPath = $ConfigPath
        WatcherPid = $PID
        CompletedRuns = $CompletedRuns
        LastReport = $LastReport
        HistoryPath = $historyPath
        LogPath = $logPath
        ActiveMonitors = $active
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding UTF8
}

if (-not (Test-Path -LiteralPath $monitorScript)) {
    throw "Monitor script not found: $monitorScript"
}

if ($ValidateOnly) {
    $config = Read-FrameScopeConfig
    if (-not (Test-Path -LiteralPath $config.MonitorScript)) {
        throw "Monitor script not found: $($config.MonitorScript)"
    }
    "FrameScopeWatcher validation OK. Targets=$(@($config.Targets).Count)"
    return
}

Write-FrameScopeLog "watcher-start config=$ConfigPath"

$activeMonitors = @{}
$completedRuns = 0
$lastReport = ''

while ($true) {
    $config = Read-FrameScopeConfig
    $dataRoot = [string]$config.DataRoot
    if (-not [IO.Path]::IsPathRooted($dataRoot)) {
        $dataRoot = Join-Path $root $dataRoot
    }
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null

    foreach ($key in @($activeMonitors.Keys)) {
        $item = $activeMonitors[$key]
        if (Test-MonitorExited -Item $item) {
            $run = Get-LatestRun -RunRoot $item.RunRoot
            $status = if ($run) { Read-RunStatus -RunDir $run.FullName } else { $null }
            $exitCode = Get-MonitorExitCode -Item $item -Status $status
            $entry = if ($run) { Add-HistoryEntry -Target $item.Target -RunDir $run.FullName -Status $status -MonitorExitCode $exitCode } else { $null }
            if ($entry) {
                $lastReport = [string]$entry.ReportHtml
                Write-FrameScopeLog "monitor-complete game=$($item.Target.Name) report=$lastReport"
                $targetOpen = if ($null -ne $item.Target.OpenReportOnComplete) { [bool]$item.Target.OpenReportOnComplete } else { [bool]$config.OpenReportOnComplete }
                if ($targetOpen -and (Test-Path -LiteralPath $lastReport)) {
                    Start-Process -FilePath $lastReport | Out-Null
                }
            }
            else {
                Write-FrameScopeLog "monitor-complete-no-run game=$($item.Target.Name)"
            }
            $activeMonitors.Remove($key)
            $completedRuns++
        }
    }

    $targets = @($config.Targets | Where-Object { $_.Enabled -and $_.ProcessName })
    foreach ($target in $targets) {
        $base = Get-TargetBaseName -ProcessName ([string]$target.ProcessName)
        if (-not $base) { continue }
        $key = $base.ToLowerInvariant()
        if ($activeMonitors.ContainsKey($key)) { continue }

        $running = @(Get-Process -Name $base -ErrorAction SilentlyContinue)
        if ($running.Count -eq 0) { continue }

        $safeName = ConvertTo-SafeName -Name ([string]$target.Name) -Fallback $base
        $targetRunRoot = Join-Path $dataRoot $safeName
        New-Item -ItemType Directory -Path $targetRunRoot -Force | Out-Null

        $sampleMs = if ($target.SampleIntervalMs) { [int]$target.SampleIntervalMs } else { 100 }
        $processSampleMs = if ($target.ProcessSampleIntervalMs) { [int]$target.ProcessSampleIntervalMs } else { 100 }
        if ($processSampleMs -lt 100) { $processSampleMs = 100 }
        $slowSampleMs = if ($target.SlowSampleIntervalMs) { [int]$target.SlowSampleIntervalMs } else { 1000 }
        $args = @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', $monitorScript,
            '-TargetProcessName', ([string]$target.ProcessName),
            '-WaitSeconds', '15',
            '-CaptureSeconds', '0',
            '-SampleIntervalMs', [string]$sampleMs,
            '-ProcessSampleIntervalMs', [string]$processSampleMs,
            '-SlowSampleIntervalMs', [string]$slowSampleMs,
            '-RunRoot', $targetRunRoot,
            '-RunNamePrefix', $safeName
        )
        $process = Start-Process -FilePath $powershell -ArgumentList $args -PassThru -WindowStyle Hidden
        $activeMonitors[$key] = [pscustomobject]@{
            Target = $target
            Process = $process
            MonitorPid = $process.Id
            RunRoot = $targetRunRoot
            StartedAt = Get-Date
        }
        Write-FrameScopeLog "monitor-start game=$($target.Name) process=$($target.ProcessName) pid=$($process.Id)"
    }

    $phase = if ($activeMonitors.Count -gt 0) { 'monitoring' } else { 'idle' }
    Write-WatcherState -Phase $phase -ActiveMonitors $activeMonitors -CompletedRuns $completedRuns -LastReport $lastReport

    if ($ExitAfterFirstRun -and $completedRuns -ge 1 -and $activeMonitors.Count -eq 0) {
        Write-FrameScopeLog 'watcher-exit-after-first-run'
        break
    }

    $pollMs = if ($config.PollIntervalMs) { [int]$config.PollIntervalMs } else { 500 }
    if ($pollMs -lt 250) { $pollMs = 250 }
    Start-Sleep -Milliseconds $pollMs
}

