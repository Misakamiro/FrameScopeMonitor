param(
    [string]$EvidenceDirectory = '',
    [switch]$SkipBrowser
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root ('artifacts\20260918-repair\csharp-reporttruth-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('fsc-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $temporary -Force | Out-Null
$oldTemp = $env:TEMP
$oldTmp = $env:TMP
$results = @()
try {
    $env:TEMP = $temporary
    $env:TMP = $temporary
    & (Join-Path $PSScriptRoot 'Build-FrameScopeTests.ps1') *> (Join-Path $evidence 'build.log')
    $results += [pscustomobject]@{ Suite = 'Build-FrameScopeTests'; ExitCode = 0 }
    $suites = @(
        'FrameScopeJsonFileTests', 'FrameScopeConfigStoreTests', 'FrameScopeLoggingPolicyTests',
        'FrameScopeRunContractTests', 'FrameScopeReportRecoveryTests', 'FrameScopeReportStatusTests',
        'FrameScopeReportProgressTests', 'FrameScopeReportManifestTests', 'FrameScopeReportProcessTests',
        'FrameScopeCapturePlannerTests', 'FrameScopePresentMonDiagnosticsTests', 'FrameScopeMonitoringReliabilityTests',
        'FrameScopeSystemSamplerCpuCoreTests', 'FrameScopeProcessSamplerTests', 'FrameScopeNativeWatcherPolicyTests',
        'FrameScopeWebBridgeTests', 'FrameScopeWebHostLifecycleTests', 'FrameScopeRunRetentionTests',
        'FrameScopeDiagnosticsTests', 'FrameScopeSingleInstanceLaunchGuardTests', 'FrameScopeWebView2RuntimeTests', 'FrameScopePubgSimulatorTests',
        'FrameScopeSampledFpsAudit'
    )
    foreach ($suite in $suites) {
        $exe = Join-Path $PSScriptRoot ($suite + '.exe')
        $suiteArgs = @()
        if ($suite -eq 'FrameScopeSampledFpsAudit') {
            $suiteArgs = @((Join-Path $PSScriptRoot 'FrameScopeReportManifestTests.exe'), $evidence)
        }
        & $exe @suiteArgs *> (Join-Path $evidence ($suite + '.log'))
        $code = $LASTEXITCODE
        $results += [pscustomobject]@{ Suite = $suite; ExitCode = $code; Sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash }
        Write-Host "$suite EXIT=$code"
    }
    $runtime = & (Join-Path $PSScriptRoot 'Build-FrameScopeAuditRuntime.ps1') -EvidenceDirectory $evidence
    foreach ($suite in @('FrameScopeVersionTests', 'FrameScopeIconTests')) {
        $source = Join-Path $PSScriptRoot ($suite + '.exe')
        $exe = Join-Path $runtime ('tests\' + $suite + '.exe')
        Copy-Item -LiteralPath $source -Destination $exe
        & $exe *> (Join-Path $evidence ($suite + '.log'))
        $code = $LASTEXITCODE
        $results += [pscustomobject]@{ Suite = $suite; ExitCode = $code; Sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash; Scope = 'Fresh compiled PE metadata/icon fixtures, not installer payload validation' }
        Write-Host "$suite EXIT=$code"
    }
    $cleanupRoot = Join-Path $temporary ('cleanup-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $cleanupRoot | Out-Null
    $cleanupExe = Join-Path $cleanupRoot 'FrameScopeProcessCleanupTests.exe'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FrameScopeProcessCleanupTests.exe') -Destination $cleanupExe
    & $cleanupExe *> (Join-Path $evidence 'FrameScopeProcessCleanupTests.log')
    $code = $LASTEXITCODE
    $results += [pscustomobject]@{ Suite = 'FrameScopeProcessCleanupTests'; ExitCode = $code; Sha256 = (Get-FileHash -LiteralPath $cleanupExe -Algorithm SHA256).Hash; IsolatedRoot = $cleanupRoot }
    Write-Host "FrameScopeProcessCleanupTests EXIT=$code"
    $subsets = @()
    foreach ($flag in @('--pipe-join-regression-only', '--long-path-regression-only')) {
        & (Join-Path $PSScriptRoot 'FrameScopeNativeMonitorChildProcessTests.exe') $flag *> (Join-Path $evidence ('child' + $flag + '.log'))
        $subsets += [pscustomobject]@{ Arguments = $flag; ExitCode = $LASTEXITCODE }
    }
    $results += [pscustomobject]@{
        Suite = 'FrameScopeNativeMonitorChildProcessTests'; ExitCode = $null; Status = 'NOT_RUN'
        Reason = 'Full suite starts a hardware recorder using the fixed installed-service pipe; only non-capture subsets executed'
        Subsets = $subsets
    }
    $results += [pscustomobject]@{
        Suite = 'FrameScopeTargetLifecycleIntegrationTests'; ExitCode = $null; Status = 'NOT_RUN'
        Reason = 'Fresh binaries are available but hardware recording cannot be isolated from the fixed installed-service pipe'
    }
    & node (Join-Path $PSScriptRoot 'chart-sampling-tests.js') *> (Join-Path $evidence 'chart-sampling.log')
    $results += [pscustomobject]@{ Suite = 'chart-sampling-tests'; ExitCode = $LASTEXITCODE }
    if (-not $SkipBrowser) {
        & node (Join-Path $PSScriptRoot 'report-sampled-fps.browser.cjs') $evidence *> (Join-Path $evidence 'browser.log')
        $results += [pscustomobject]@{ Suite = 'report-sampled-fps.browser'; ExitCode = $LASTEXITCODE }
    }
    else {
        $results += [pscustomobject]@{ Suite = 'report-sampled-fps.browser'; ExitCode = $null; Status = 'NOT_RUN_BY_SCOPE' }
    }
}
finally {
    $env:TEMP = $oldTemp
    $env:TMP = $oldTmp
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'suite-results.json') -Encoding UTF8
}
Write-Host "Evidence: $evidence"
if (@($results | Where-Object { $null -ne $_.ExitCode -and $_.ExitCode -ne 0 }).Count -gt 0 -or
    @($subsets | Where-Object { $_.ExitCode -ne 0 }).Count -gt 0) { exit 1 }
exit 0
