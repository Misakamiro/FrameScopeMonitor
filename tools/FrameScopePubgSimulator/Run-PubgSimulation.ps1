param(
    [ValidateSet('stable', 'fluctuating', 'spikes', 'no-data', 'missing-csv')]
    [string]$Scenario = 'spikes',
    [int]$DurationSeconds = 4,
    [string]$OutputRoot = '',
    [switch]$NoInitialPid
)

$ErrorActionPreference = 'Stop'

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

$bin = Join-Path $OutputRoot 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null

$gameExe = Join-Path $bin 'TslGame.exe'
$fakePresentMonExe = Join-Path $bin 'FakePresentMon.exe'
$common = Join-Path $toolRoot 'FrameScopePubgSimulationCommon.cs'

& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /out:$gameExe `
    (Join-Path $root 'src\core\FrameScopeConfigStore.cs') `
    $common `
    (Join-Path $toolRoot 'PubgGameSimulator.cs')
if ($LASTEXITCODE -ne 0) { throw "Failed to build TslGame simulator. csc exit=$LASTEXITCODE" }

& $csc /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 `
    /out:$fakePresentMonExe `
    (Join-Path $root 'src\core\FrameScopeConfigStore.cs') `
    $common `
    (Join-Path $toolRoot 'FakePresentMon.cs')
if ($LASTEXITCODE -ne 0) { throw "Failed to build FakePresentMon simulator. csc exit=$LASTEXITCODE" }

$runRoot = Join-Path $OutputRoot 'runs'
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$gameDuration = [Math]::Max($DurationSeconds + 3, 7)
$gameTitle = "PLAYERUNKNOWN'S BATTLEGROUNDS - PUBG: BATTLEGROUNDS"
$gameArgs = "--duration $gameDuration --title `"$gameTitle`""
$game = Start-Process -FilePath $gameExe -ArgumentList $gameArgs -WorkingDirectory $bin -PassThru
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

$monitorExe = Join-Path $root 'FrameScopeMonitor.exe'
$monitor = Start-Process -FilePath $monitorExe -ArgumentList $monitorArgs -WorkingDirectory $root -PassThru -WindowStyle Hidden
$monitor.WaitForExit()

try {
    if (-not $game.HasExited) {
        $game.CloseMainWindow() | Out-Null
        if (-not $game.WaitForExit(3000)) { $game.Kill() }
    }
}
catch { }

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

[pscustomobject]@{
    scenario = $Scenario
    outputRoot = $OutputRoot
    runDir = $runDir.FullName
    monitorExit = $monitor.ExitCode
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
} | ConvertTo-Json -Depth 4
