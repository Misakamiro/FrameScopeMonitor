param(
    [ValidateSet('stable', 'fluctuating', 'spikes', 'no-data', 'missing-csv')]
    [string]$Scenario = 'spikes',
    [int]$DurationSeconds = 4,
    [string]$OutputRoot = '',
    [switch]$NoInitialPid,
    [ValidateRange(1, 3600)]
    [int]$MonitorTimeoutSeconds = 120,
    [string]$OwnershipPath = '',
    [switch]$CompileOnly
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-RunningProcessExecutablePath {
    param([Diagnostics.Process]$Process)
    try {
        $Process.Refresh()
        if ($Process.HasExited) { return $null }
        return $Process.MainModule.FileName
    }
    catch {
        $cim = Get-CimInstance -ClassName Win32_Process -Filter ("ProcessId = {0}" -f $Process.Id) -ErrorAction SilentlyContinue
        if ($null -ne $cim -and -not [string]::IsNullOrWhiteSpace([string]$cim.ExecutablePath)) {
            return [string]$cim.ExecutablePath
        }
        throw "Unable to resolve executable path for owned PID $($Process.Id)."
    }
}

function Stop-OwnedProcess {
    param(
        [Diagnostics.Process]$Process,
        [string]$ExpectedExecutablePath,
        [int]$WaitMilliseconds = 5000,
        [switch]$IncludeDescendants
    )

    if ($null -eq $Process) { return $false }
    $Process.Refresh()
    if ($Process.HasExited) { return $false }

    $actualPath = Get-RunningProcessExecutablePath -Process $Process
    if ([string]::IsNullOrWhiteSpace($actualPath)) { return $false }
    if (-not [string]::Equals([IO.Path]::GetFullPath($ExpectedExecutablePath), [IO.Path]::GetFullPath($actualPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to stop PID $($Process.Id): expected executable $ExpectedExecutablePath but found $actualPath."
    }

    if ($IncludeDescendants) {
        & taskkill.exe /PID $Process.Id /T /F *> $null
        $Process.Refresh()
        if (-not $Process.HasExited) { $Process.Kill() }
    }
    else {
        $Process.Kill()
    }
    if (-not $Process.WaitForExit($WaitMilliseconds)) {
        throw "Timed out waiting for owned PID $($Process.Id) to exit."
    }
    return $true
}

function Write-OwnershipRecord {
    param([string]$Path, [Collections.IDictionary]$Record)
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $temp = $Path + '.tmp.' + [guid]::NewGuid().ToString('N')
    try {
        [IO.File]::WriteAllText($temp, (($Record | ConvertTo-Json -Depth 6) + [Environment]::NewLine), $utf8NoBom)
        Move-Item -LiteralPath $temp -Destination $Path -Force
    }
    finally {
        Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
    }
}

$toolRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $toolRoot)
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found: $csc"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $root ("artifacts\pubg-simulator\" + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + "-" + $Scenario)
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
if ([string]::IsNullOrWhiteSpace($OwnershipPath)) {
    $OwnershipPath = Join-Path $OutputRoot 'simulator-ownership.json'
}

$bin = Join-Path $OutputRoot 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null

$gameExe = Join-Path $bin 'TslGame.exe'
$fakePresentMonExe = Join-Path $bin 'FakePresentMon.exe'
$common = Join-Path $toolRoot 'FrameScopePubgSimulationCommon.cs'

& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /out:$gameExe `
    (Join-Path $root 'src\core\FrameScopeJsonFile.cs') `
    (Join-Path $root 'src\core\FrameScopeConfigStore.cs') `
    $common `
    (Join-Path $toolRoot 'PubgGameSimulator.cs')
if ($LASTEXITCODE -ne 0) { throw "Failed to build TslGame simulator. csc exit=$LASTEXITCODE" }

& $csc /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 `
    /out:$fakePresentMonExe `
    (Join-Path $root 'src\core\FrameScopeJsonFile.cs') `
    (Join-Path $root 'src\core\FrameScopeConfigStore.cs') `
    $common `
    (Join-Path $toolRoot 'FakePresentMon.cs')
if ($LASTEXITCODE -ne 0) { throw "Failed to build FakePresentMon simulator. csc exit=$LASTEXITCODE" }

if ($CompileOnly) {
    [ordered]@{
        gameExecutable = $gameExe
        fakePresentMonExecutable = $fakePresentMonExe
    } | ConvertTo-Json
    return
}

$runRoot = Join-Path $OutputRoot 'runs'
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$gameDuration = [Math]::Max($DurationSeconds + 3, 7)
$gameTitle = "PLAYERUNKNOWN'S BATTLEGROUNDS - PUBG: BATTLEGROUNDS"
$gameArgs = "--duration $gameDuration --title `"$gameTitle`""
$game = $null
$monitor = $null
$monitorExe = Join-Path $root 'FrameScopeMonitor.exe'
$monitorExit = $null
$reportExit = $null
$simulationResult = $null
$operationFailure = $null
$cleanupErrors = New-Object Collections.Generic.List[string]
$ownership = [ordered]@{
    schemaVersion = 1
    gamePid = $null
    gameExecutable = (Resolve-Path -LiteralPath $gameExe).Path
    startedAt = $null
    cleanupAttempted = $false
    cleanupCompleted = $false
    cleanupError = $null
}

try {
    $game = Start-Process -FilePath $gameExe -ArgumentList $gameArgs -WorkingDirectory $bin -PassThru
    $ownership.gamePid = $game.Id
    $ownership.startedAt = [DateTime]::UtcNow.ToString('o')
    Write-OwnershipRecord -Path $OwnershipPath -Record $ownership

    for ($i = 0; $i -lt 25; $i++) {
        Start-Sleep -Milliseconds 200
        try {
            $probe = Get-Process -Id $game.Id -ErrorAction Stop
            if ($probe.MainWindowHandle -ne 0 -and -not [string]::IsNullOrWhiteSpace($probe.MainWindowTitle)) { break }
        }
        catch { break }
    }

    $env:FRAMESCOPE_FAKE_PRESENTMON_SCENARIO = $Scenario
    $env:FRAMESCOPE_FAKE_PRESENTMON_ROWS = '240'

    $monitorArgs = @(
        '--monitor-session',
        '--TargetProcessName', 'TslGame.exe',
        '--TargetProcessAliases', 'TslGame;TslGame-Win64-Shipping',
        '--TargetDisplayName', 'PUBG: BATTLEGROUNDS',
        '--InitialTargetPid', $(if ($NoInitialPid) { '0' } else { $game.Id.ToString() }),
        '--WaitSeconds', '8',
        '--CaptureSeconds', [Math]::Max(1, $DurationSeconds).ToString(),
        '--SampleIntervalMs', '100',
        '--ProcessSampleIntervalMs', '100',
        '--SlowSampleIntervalMs', '1000',
        '--ControlPollIntervalMs', '1000',
        '--RunRoot', $runRoot,
        '--RunNamePrefix', 'SyntheticPUBG',
        '--PresentMonExe', $fakePresentMonExe,
        '--ProcessSamplerExe', (Join-Path $root 'FrameScopeProcessSampler.exe'),
        '--SystemSamplerExe', (Join-Path $root 'FrameScopeSystemSampler.exe')
    )

    $monitor = Start-Process -FilePath $monitorExe -ArgumentList $monitorArgs -WorkingDirectory $root -PassThru -WindowStyle Hidden
    if (-not $monitor.WaitForExit($MonitorTimeoutSeconds * 1000)) {
        Stop-OwnedProcess -Process $monitor -ExpectedExecutablePath $monitorExe -WaitMilliseconds 5000 -IncludeDescendants | Out-Null
        throw "FrameScope monitor timed out after $MonitorTimeoutSeconds seconds."
    }
    $monitorExit = $monitor.ExitCode

    $runDir = Get-ChildItem -LiteralPath $runRoot -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $runDir) {
        throw "Simulator did not create a run directory under $runRoot"
    }

    $progressPath = Join-Path $runDir.FullName 'report-progress.json'
    $reportExe = Join-Path $root 'FrameScopeReportGenerator.exe'
    & $reportExe $runDir.FullName --progress $progressPath | Out-Null
    $reportExit = $LASTEXITCODE

    $statusPath = Join-Path $runDir.FullName 'status.json'
    $summaryPath = Join-Path $runDir.FullName 'summary.json'
    $manifestPath = Join-Path $runDir.FullName 'charts\framescope-interactive-manifest.json'
    $status = Get-Content -Raw -LiteralPath $statusPath | ConvertFrom-Json
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    $manifestText = Get-Content -Raw -LiteralPath $manifestPath
    $manifestFramesMatch = [regex]::Match($manifestText, '"frames"\s*:\s*(\d+)')
    $manifestHasFrameDataMatch = [regex]::Match($manifestText, '"hasFrameData"\s*:\s*(true|false)')
    $manifestReportKindMatch = [regex]::Match($manifestText, '"reportKind"\s*:\s*"([^"]*)"')
    $manifestFrames = if ($manifestFramesMatch.Success) { [int]$manifestFramesMatch.Groups[1].Value } else { -1 }
    $manifestHasFrameData = if ($manifestHasFrameDataMatch.Success) { [bool]::Parse($manifestHasFrameDataMatch.Groups[1].Value) } else { $false }
    $manifestReportKind = if ($manifestReportKindMatch.Success) { $manifestReportKindMatch.Groups[1].Value } else { 'unknown' }

    $simulationResult = [ordered]@{
        scenario = $Scenario
        outputRoot = $OutputRoot
        runDir = $runDir.FullName
        monitorExit = $monitorExit
        reportExit = $reportExit
        phase = $status.Phase
        presentMonCaptureMode = $status.PresentMonCaptureMode
        presentMonCaptureTarget = $status.PresentMonCaptureTarget
        presentMonCsvRows = $status.PresentMonCsvRows
        frameCaptureStatus = $status.FrameCaptureStatus
        hasFrameData = $manifestHasFrameData
        frames = $manifestFrames
        reportKind = $manifestReportKind
        targetWindowTitle = $status.TargetWindowTitle
        targetHasMainWindow = $status.TargetHasMainWindow
        summaryFrameCaptureStatus = $summary.FrameCaptureStatus
        usedInitialPid = -not $NoInitialPid
        gamePid = $game.Id
        gameExecutable = $ownership.gameExecutable
        ownershipPath = $OwnershipPath
        gameCleanupCompleted = $false
    }
}
catch {
    $operationFailure = $_
}
finally {
    if ($null -ne $monitor) {
        try {
            Stop-OwnedProcess -Process $monitor -ExpectedExecutablePath $monitorExe -WaitMilliseconds 5000 -IncludeDescendants | Out-Null
        }
        catch {
            $cleanupErrors.Add("monitor cleanup: $($_.Exception.Message)")
        }
    }
    if ($null -ne $game) {
        $ownership.cleanupAttempted = $true
        try {
            Stop-OwnedProcess -Process $game -ExpectedExecutablePath $gameExe -WaitMilliseconds 5000 | Out-Null
            $ownership.cleanupCompleted = $true
        }
        catch {
            $ownership.cleanupError = $_.Exception.Message
            $cleanupErrors.Add("game cleanup: $($_.Exception.Message)")
        }
    }
    try {
        Write-OwnershipRecord -Path $OwnershipPath -Record $ownership
    }
    catch {
        $cleanupErrors.Add("ownership record: $($_.Exception.Message)")
    }
}

if ($cleanupErrors.Count -gt 0) {
    $cleanupMessage = $cleanupErrors -join '; '
    if ($null -ne $operationFailure) {
        throw "$($operationFailure.Exception.Message) Cleanup failures: $cleanupMessage"
    }
    throw "Simulator cleanup failed: $cleanupMessage"
}
if ($null -ne $operationFailure) { throw $operationFailure }

$simulationResult.gameCleanupCompleted = [bool]$ownership.cleanupCompleted
$simulationResult | ConvertTo-Json -Depth 4
