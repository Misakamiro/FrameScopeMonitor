param(
    [string]$AppExe = "$env:LOCALAPPDATA\FrameScopeMonitor\FrameScopeMonitor.exe",
    [string]$EvidenceRoot = "artifacts\qa0530-full-installed\partial-fixes\target-settings",
    [int]$ExpectedTelemetrySampleIntervalMs = 1375
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path ".").Path
$appPath = (Resolve-Path -LiteralPath $AppExe).Path
$appDir = Split-Path -Parent $appPath
$evidenceDir = Join-Path $repoRoot $EvidenceRoot
$smokeDir = Join-Path $evidenceDir "smoke"
$screenshotDir = Join-Path $evidenceDir "screenshots"
$profileDir = Join-Path $appDir "smoke-temp\pfix-target-settings"
$configPath = Join-Path $profileDir "framescope-config.json"
$dataRoot = Join-Path $profileDir "runs"

New-Item -ItemType Directory -Force -Path $smokeDir, $screenshotDir, $profileDir, $dataRoot | Out-Null

if (-not (Test-Path -LiteralPath $AppExe)) {
    throw "FrameScope app exe not found: $AppExe"
}

$config = [ordered]@{
    PollIntervalMs = 1000
    DataRoot = $dataRoot
    OpenReportOnComplete = $true
    MonitorScript = "native-csharp"
    ThemeMode = "dark"
    CloseWindowBehavior = "exit"
    TrayEnabled = $false
    TelemetrySampleIntervalMs = 1000
    CpuTelemetry = [ordered]@{
        CollectPerCoreFrequency = $true
        CollectCpuVoltage = $false
        PerCoreSampleIntervalMs = 1000
        PerCoreVoltageSampleIntervalMs = 1000
        VoltageProvider = "disabled"
    }
    Targets = @(
        [ordered]@{
            Enabled = $true
            Name = "QA Seed Target"
            ProcessName = "QaSeedTarget.exe"
            SampleIntervalMs = 1000
            ProcessSamplingMode = "normal"
            ProcessSampleIntervalMs = 1000
            SlowSampleIntervalMs = 1000
            OpenReportOnComplete = $true
        }
    )
}

$config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $configPath -Encoding UTF8

$targetEvidence = Join-Path $smokeDir "target-settings-crud-smoke.json"
$targetScreenshot = Join-Path $screenshotDir "target-settings-crud.png"
$restartEvidence = Join-Path $smokeDir "settings-restart-persistence-smoke.json"
$restartScreenshot = Join-Path $screenshotDir "settings-restart-persistence.png"

function Join-ProcessArguments([object[]]$Arguments) {
    $parts = @()
    foreach ($argument in $Arguments) {
        $text = [string]$argument
        if ($text -match '[\s"]') {
            $parts += '"' + ($text -replace '"', '\"') + '"'
        } else {
            $parts += $text
        }
    }
    return ($parts -join " ")
}

function Invoke-WaitedProcess([string]$FilePath, [object[]]$Arguments) {
    $argumentLine = Join-ProcessArguments $Arguments
    $process = Start-Process -FilePath $FilePath -ArgumentList $argumentLine -PassThru -Wait -WindowStyle Hidden
    return $process.ExitCode
}

$targetExit = Invoke-WaitedProcess $AppExe @(
    "--web-ui-smoke",
    "--web-ui-target-settings-evidence-smoke",
    "--config", $configPath,
    "--web-ui-evidence", $targetEvidence,
    "--web-ui-screenshot", $targetScreenshot,
    "--web-ui-expected-telemetry-sample", $ExpectedTelemetrySampleIntervalMs,
    "--web-ui-timeout-ms", "70000"
)

$restartExit = Invoke-WaitedProcess $AppExe @(
    "--web-ui-smoke",
    "--web-ui-settings-persistence-read-smoke",
    "--config", $configPath,
    "--web-ui-evidence", $restartEvidence,
    "--web-ui-screenshot", $restartScreenshot,
    "--web-ui-expected-telemetry-sample", $ExpectedTelemetrySampleIntervalMs,
    "--web-ui-timeout-ms", "40000"
)

$targetSmoke = if (Test-Path -LiteralPath $targetEvidence) { Get-Content -LiteralPath $targetEvidence -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
$restartSmoke = if (Test-Path -LiteralPath $restartEvidence) { Get-Content -LiteralPath $restartEvidence -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
$finalConfig = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
$screenshots = Get-ChildItem -LiteralPath $screenshotDir -Filter "*.png" -File | Sort-Object Name | ForEach-Object { $_.FullName }

$summary = [ordered]@{
    generatedAt = (Get-Date).ToString("o")
    appExe = (Resolve-Path -LiteralPath $AppExe).Path
    tempConfigPath = $configPath
    userConfigTouched = $false
    targetCrudExitCode = $targetExit
    settingsRestartExitCode = $restartExit
    targetCrudSuccess = ($targetExit -eq 0 -and $targetSmoke -and $targetSmoke.success -eq $true -and $targetSmoke.smokePayload.success -eq $true)
    settingsRestartSuccess = ($restartExit -eq 0 -and $restartSmoke -and $restartSmoke.success -eq $true -and $restartSmoke.smokePayload.success -eq $true)
    targetAddSaved = if ($targetSmoke) { $targetSmoke.smokePayload.targetAddSaved } else { $false }
    targetEditSaved = if ($targetSmoke) { $targetSmoke.smokePayload.targetEditSaved } else { $false }
    targetDeleteSaved = if ($targetSmoke) { $targetSmoke.smokePayload.targetDeleteSaved } else { $false }
    targetEditNoPerTargetSampling = if ($targetSmoke) { $targetSmoke.smokePayload.targetEditNoPerTargetSampling } else { $false }
    settingsSaved = if ($targetSmoke) { $targetSmoke.smokePayload.settingsSaved } else { $false }
    expectedTelemetrySampleIntervalMs = $ExpectedTelemetrySampleIntervalMs
    savedTelemetrySampleIntervalMs = if ($targetSmoke) { $targetSmoke.smokePayload.savedTelemetrySampleIntervalMs } else { "" }
    restartTelemetrySampleIntervalMs = if ($restartSmoke) { $restartSmoke.smokePayload.actualTelemetrySampleIntervalMs } else { "" }
    finalConfigTelemetrySampleIntervalMs = $finalConfig.TelemetrySampleIntervalMs
    finalConfigTargets = $finalConfig.Targets
    screenshots = $screenshots
    smokeJson = @($targetEvidence, $restartEvidence)
}
$summary["success"] = ($summary.targetCrudSuccess -and $summary.settingsRestartSuccess)

$summaryPath = Join-Path $evidenceDir "target-settings-evidence-summary.json"
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Output $summaryPath

if (-not $summary.success) {
    exit 2
}
