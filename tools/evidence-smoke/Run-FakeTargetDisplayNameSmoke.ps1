param(
    [string]$AppExe = "$env:LOCALAPPDATA\FrameScopeMonitor\FrameScopeMonitor.exe",
    [string]$ReportGeneratorExe = "",
    [string]$EvidenceRoot = "artifacts\qa0530-full-installed\pfix-td",
    [string]$FakePresentMon = "artifacts\qa0530-full-installed\runs\sim-targets\bin\GenericFakePresentMon.exe"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path ".").Path
$evidenceDir = Join-Path $repoRoot $EvidenceRoot
$binDir = Join-Path $evidenceDir "bin"
$runRoot = Join-Path $evidenceDir "runs"
New-Item -ItemType Directory -Force -Path $binDir, $runRoot | Out-Null

if (-not (Test-Path -LiteralPath $AppExe)) {
    throw "FrameScope app exe not found: $AppExe"
}

if ([string]::IsNullOrWhiteSpace($ReportGeneratorExe)) {
    $ReportGeneratorExe = Join-Path (Split-Path -Parent (Resolve-Path -LiteralPath $AppExe).Path) "FrameScopeReportGenerator.exe"
}
if (-not (Test-Path -LiteralPath $ReportGeneratorExe)) {
    throw "FrameScope report generator not found: $ReportGeneratorExe"
}

$fakePresentMonPath = (Resolve-Path -LiteralPath (Join-Path $repoRoot $FakePresentMon)).Path

$fakeSourcePath = Join-Path $binDir "FakeGameTarget.cs"
@"
using System;
using System.Threading;

internal static class FakeGameTarget
{
    private static int Main(string[] args)
    {
        int seconds = 20;
        if (args != null && args.Length > 0)
        {
            int parsed;
            if (int.TryParse(args[0], out parsed) && parsed > 0) seconds = parsed;
        }
        Thread.Sleep(seconds * 1000);
        return 0;
    }
}
"@ | Set-Content -LiteralPath $fakeSourcePath -Encoding ASCII

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found: $csc"
}

$targets = @(
    [pscustomobject][ordered]@{ Code = "CS2"; Name = "Counter-Strike 2"; ProcessName = "cs2.exe"; ForbiddenText = "" },
    [pscustomobject][ordered]@{ Code = "PUBG"; Name = "PUBG: BATTLEGROUNDS"; ProcessName = "TslGame.exe"; ForbiddenText = "" },
    [pscustomobject][ordered]@{ Code = "DF"; Name = "Delta Force"; ProcessName = "DeltaForceClient-Win64-Shipping.exe"; ForbiddenText = "" },
    [pscustomobject][ordered]@{ Code = "NTE"; Name = "Neverness To Everness"; ProcessName = "HTGame.exe"; ForbiddenText = "" },
    [pscustomobject][ordered]@{ Code = "VAL"; Name = "Valorant"; ProcessName = "VALORANT-Win64-Shipping.exe"; ForbiddenText = "PUBG" },
    [pscustomobject][ordered]@{ Code = "BF6"; Name = "Battlefield 6"; ProcessName = "bf6.exe"; ForbiddenText = "" }
)

foreach ($target in $targets) {
    $targetExe = Join-Path $binDir $target.ProcessName
    & $csc /nologo /target:exe /out:$targetExe $fakeSourcePath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile fake target $($target.ProcessName)"
    }
}

function ConvertTo-SafeName([string]$Value) {
    $safe = ($Value -replace '[^A-Za-z0-9._-]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safe)) { return "Target" }
    return $safe
}

function Read-ReportData([string]$DataPath) {
    $text = Get-Content -LiteralPath $DataPath -Raw -Encoding UTF8
    $prefix = "window.FRAMESCOPE_DATA = "
    if (-not $text.StartsWith($prefix)) {
        throw "Unexpected report data prefix: $DataPath"
    }
    $json = $text.Substring($prefix.Length).Trim()
    if ($json.EndsWith(";")) { $json = $json.Substring(0, $json.Length - 1) }
    return $json | ConvertFrom-Json
}

function Read-ReportDataText([string]$DataPath) {
    return Get-Content -LiteralPath $DataPath -Raw -Encoding UTF8
}

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

$results = @()

foreach ($target in $targets) {
    $fakeProcess = $null
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $runNamePrefix = "$($target.Code)-$timestamp"
    $targetExe = Join-Path $binDir $target.ProcessName
    $targetBase = [System.IO.Path]::GetFileNameWithoutExtension($target.ProcessName)

    try {
        $fakeProcess = Start-Process -FilePath $targetExe -ArgumentList "20" -PassThru -WindowStyle Hidden
        Start-Sleep -Milliseconds 500
        if ($fakeProcess.HasExited) {
            throw "Fake target exited before monitor session started: $($target.ProcessName)"
        }

        $monitorExit = Invoke-WaitedProcess $AppExe @(
            "--monitor-session",
            "--TargetProcessName", $target.ProcessName,
            "--TargetProcessAliases", $targetBase,
            "--TargetDisplayName", $target.Name,
            "--InitialTargetPid", $fakeProcess.Id,
            "--WaitSeconds", "10",
            "--CaptureSeconds", "3",
            "--SampleIntervalMs", "1000",
            "--ProcessSampleIntervalMs", "1000",
            "--SlowSampleIntervalMs", "1000",
            "--ControlPollIntervalMs", "1000",
            "--RunRoot", $runRoot,
            "--RunNamePrefix", $runNamePrefix,
            "--PresentMonExe", $fakePresentMonPath
        )

        $runDir = Get-ChildItem -LiteralPath $runRoot -Directory |
            Where-Object { $_.Name -like "$runNamePrefix*" } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if (-not $runDir) {
            throw "Run directory not found for $($target.Name)"
        }

        & $ReportGeneratorExe $runDir.FullName | Out-Null
        $reportExit = $LASTEXITCODE

        $manifestPath = Join-Path $runDir.FullName "charts\framescope-interactive-manifest.json"
        $dataPath = Join-Path $runDir.FullName "charts\framescope-interactive-data.js"
        $htmlPath = Join-Path $runDir.FullName "charts\framescope-interactive-report.html"
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $dataText = Read-ReportDataText $dataPath
        $data = Read-ReportData $dataPath
        $html = Get-Content -LiteralPath $htmlPath -Raw -Encoding UTF8
        $forbiddenOk = [string]::IsNullOrWhiteSpace($target.ForbiddenText) -or (($html + $dataText).IndexOf($target.ForbiddenText, [StringComparison]::OrdinalIgnoreCase) -lt 0)

        $result = [ordered]@{
            target = $target.Name
            processName = $target.ProcessName
            fakePid = $fakeProcess.Id
            runDir = $runDir.FullName
            monitorExitCode = $monitorExit
            reportExitCode = $reportExit
            manifestTargetDisplayName = $manifest.targetDisplayName
            manifestTargetProcessName = $manifest.targetProcessName
            dataTargetDisplayName = $data.target.displayName
            dataTargetProcessName = $data.target.processName
            dataJsHasTargetName = $dataText.Contains($target.Name)
            staticHtmlHasTargetName = $html.Contains($target.Name)
            htmlHasProcessName = $html.Contains($target.ProcessName)
            forbiddenText = $target.ForbiddenText
            forbiddenTextAbsent = $forbiddenOk
            hasFrameData = $manifest.hasFrameData
        }
        $result["pass"] = (
            $monitorExit -eq 0 -and
            $reportExit -eq 0 -and
            $result["manifestTargetDisplayName"] -eq $target.Name -and
            $result["manifestTargetProcessName"] -eq $target.ProcessName -and
            $result["dataTargetDisplayName"] -eq $target.Name -and
            $result["dataTargetProcessName"] -eq $target.ProcessName -and
            $result["dataJsHasTargetName"] -and
            $forbiddenOk -and
            $result["hasFrameData"] -eq $true
        )
        $results += [pscustomobject]$result
    }
    finally {
        if ($fakeProcess -and -not $fakeProcess.HasExited) {
            Stop-Process -Id $fakeProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

$summary = [ordered]@{
    generatedAt = (Get-Date).ToString("o")
    appExe = (Resolve-Path -LiteralPath $AppExe).Path
    reportGeneratorExe = (Resolve-Path -LiteralPath $ReportGeneratorExe).Path
    fakePresentMon = $fakePresentMonPath
    fakeTargetBin = $binDir
    runRoot = $runRoot
    results = $results
}
$summary["allPass"] = @($results | Where-Object { -not $_.pass }).Count -eq 0

$summaryPath = Join-Path $evidenceDir "target-display-name-summary.json"
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Output $summaryPath

if (-not $summary["allPass"]) {
    exit 2
}
