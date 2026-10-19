param([string]$OutputDirectory = '', [string[]]$TestNames = @(), [string]$RuntimeRoot = '')
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$testRoot = Join-Path $root 'tests'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found: $csc"
}
if (-not $OutputDirectory) { $OutputDirectory = $testRoot }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$manifestPath = Join-Path $OutputDirectory 'test-manifest.json'
$builtTests = [Collections.Generic.List[object]]::new()
[pscustomobject]@{ RepoRoot = $root; Tests = @() } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
$buildMetadata = & (Join-Path $root 'tools\Write-FrameScopeBuildMetadata.ps1') -RepoRoot $root -OutputPath (Join-Path $OutputDirectory 'BuildMetadata.g.cs')

function Invoke-TestBuild {
    param(
        [string]$OutputName,
        [string[]]$Sources,
        [string[]]$References = @(),
        [string]$MainType = ''
    )

    if ($TestNames.Count -and $OutputName -notin $TestNames -and
        -not ($OutputName -eq 'FrameScopePubgFakePresentMon.exe' -and 'FrameScopeTargetLifecycleIntegrationTests.exe' -in $TestNames)) { return }
    $args = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', ('/out:' + (Join-Path $OutputDirectory $OutputName)))
    if (-not [string]::IsNullOrWhiteSpace($MainType)) {
        $args += ('/main:' + $MainType)
    }
    foreach ($reference in $References) {
        $args += ('/reference:' + $reference)
    }
    $args += (Join-Path $root 'src\core\FrameScopeJsonFile.cs')
    $args += (Join-Path $testRoot 'FrameScopeTestPaths.cs')
    $args += $buildMetadata
    foreach ($source in $Sources) {
        $args += (Join-Path $root $source)
    }

    & $csc @args
    if ($LASTEXITCODE -ne 0) { throw "csc failed: $OutputName" }
    if ($OutputName -like '*Tests.exe') {
        $dependencies = @()
        if ($OutputName -eq 'FrameScopeTargetLifecycleIntegrationTests.exe') {
            $dependencies = @((Join-Path $OutputDirectory 'FrameScopePubgFakePresentMon.exe'))
        }
        $builtTests.Add([pscustomobject]@{ Path = (Join-Path $OutputDirectory $OutputName); Arguments = @(); Dependencies = $dependencies })
    }
}

Invoke-TestBuild `
    -OutputName 'FrameScopeJsonFileTests.exe' `
    -Sources @('tests\FrameScopeJsonFileTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeFullInstallerTests.exe' `
    -Sources @('packaging\FrameScopeFullInstaller.Core.cs', 'tests\FrameScopeFullInstallerTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeHardwareProviderTruthTests.exe' `
    -References @('System.Web.Extensions.dll', 'System.Management.dll') `
    -MainType 'FrameScopeHardwareProviderTruthTests' `
    -Sources @('src\app\FrameScopeHardwareProvider.cs', 'tests\FrameScopeHardwareProviderTruthTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeHardwareMaintenanceTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -MainType 'FrameScopeHardwareMaintenanceTests' `
    -Sources @('src\monitoring\FrameScopeHardwareServiceClient.cs', 'tests\FrameScopeHardwareMaintenanceTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeCaptureControlTests.exe' `
    -Sources @('src\core\FrameScopeTargetLifecycle.cs', 'src\core\FrameScopeRunContract.cs', 'tests\FrameScopeCaptureControlTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeVersionTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('tests\FrameScopeVersionTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeReportRecoveryTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @(
        'src\core\FrameScopeReportArtifacts.cs',
        'src\core\FrameScopeReportRecoveryPolicy.cs',
        'tests\FrameScopeReportRecoveryTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeReportProcessTests.exe' `
    -Sources @('src\core\FrameScopeBoundedProcessRunner.cs', 'tests\FrameScopeReportProcessTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeRunRetentionTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('src\core\FrameScopeRunRetention.cs', 'src\core\FrameScopeHistoryFile.cs', 'tests\FrameScopeRunRetentionTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeConfigStoreTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('src\core\FrameScopeConfigStore.cs', 'tests\FrameScopeConfigStoreTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeConfigMutationTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('src\core\FrameScopeConfigStore.cs', 'tests\FrameScopeConfigMutationTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeConfigLockFixture.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('src\core\FrameScopeConfigStore.cs', 'tests\FrameScopeConfigLockFixture.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeHotkeyConfigTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('src\core\FrameScopeConfigStore.cs', 'tests\FrameScopeHotkeyConfigTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeLoggingPolicyTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('src\core\FrameScopeConfigStore.cs', 'src\core\FrameScopeLoggingPolicy.cs', 'tests\FrameScopeLoggingPolicyTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeRunContractTests.exe' `
    -Sources @('src\core\FrameScopeRunContract.cs', 'tests\FrameScopeRunContractTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeReportStatusTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -MainType 'FrameScopeNativeMonitor' `
    -Sources @(
        'src\core\FrameScopeHistoryFile.cs',
        'src\core\FrameScopeConfigStore.cs',
        'src\core\FrameScopeReportProgress.cs',
        'src\core\FrameScopeReportRecoveryPolicy.cs',
        'src\app\FrameScopeNativeMonitor.ReportOrchestration.Models.cs',
        'src\app\FrameScopeNativeMonitor.ReportStatus.cs',
        'tests\FrameScopeReportStatusTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeNativeWatcherPolicyTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('src\core\FrameScopeConfigStore.cs', 'tests\FrameScopeNativeWatcherPolicyTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeCapturePlannerTests.exe' `
    -Sources @('src\core\FrameScopeCapturePlanner.cs', 'tests\FrameScopeCapturePlannerTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeReportProgressTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('src\core\FrameScopeReportProgress.cs', 'tests\FrameScopeReportProgressTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopePresentMonDiagnosticsTests.exe' `
    -Sources @('src\core\FrameScopePresentMonDiagnostics.cs', 'tests\FrameScopePresentMonDiagnosticsTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeMonitoringReliabilityTests.exe' `
    -Sources @('src\core\FrameScopePresentMonSessionPolicy.cs', 'src\core\FrameScopeTargetLifecycle.cs', 'tests\FrameScopeMonitoringReliabilityTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeSystemSamplerCpuCoreTests.exe' `
    -References @('System.Core.dll', 'System.Web.Extensions.dll', 'System.Management.dll') `
    -MainType 'FrameScopeSystemSamplerCpuCoreTests' `
    -Sources @(
        'src\core\FrameScopeTargetLifecycle.cs',
        'src\monitoring\FrameScopeSystemSampler.cs',
        'src\monitoring\FrameScopeSystemSampler.Models.cs',
        'src\monitoring\FrameScopeSystemSampler.PerfCounters.cs',
        'src\monitoring\FrameScopeSystemSampler.CpuCoreTelemetry.cs',
        'src\monitoring\FrameScopeSystemSampler.Gpu.cs',
        'src\monitoring\FrameScopeSystemSampler.Processes.cs',
        'src\monitoring\FrameScopeSystemSampler.IO.cs',
        'tests\FrameScopeSystemSamplerCpuCoreTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeProcessSamplerTests.exe' `
    -MainType 'FrameScopeProcessSamplerTests' `
    -Sources @(
        'src\core\FrameScopeTargetLifecycle.cs',
        'src\monitoring\FrameScopeProcessSampler.cs',
        'src\monitoring\FrameScopeProcessSampler.Models.cs',
        'src\monitoring\FrameScopeProcessSampler.Selection.cs',
        'src\monitoring\FrameScopeProcessSampler.IO.cs',
        'tests\FrameScopeProcessSamplerTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeNativeMonitorChildProcessTests.exe' `
    -References @('System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Management.dll', 'System.Web.Extensions.dll') `
    -Sources @(
        'src\core\FrameScopeRunContract.cs',
        'src\core\FrameScopePresentMonDiagnostics.cs',
        'src\app\FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs',
        'tests\FrameScopeNativeMonitorChildProcessTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeSingleInstanceLaunchGuardTests.exe' `
    -References @('System.Windows.Forms.dll') `
    -MainType 'FrameScopeSingleInstanceLaunchGuardTests' `
    -Sources @(
        'src\app\FrameScopeNativeMonitor.SingleInstance.cs',
        'tests\FrameScopeSingleInstanceLaunchGuardTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeReportManifestTests.exe' `
    -References @('System.Web.Extensions.dll', 'System.Management.dll', 'Microsoft.VisualBasic.dll') `
    -MainType 'FrameScopeReportManifestTests' `
    -Sources @(
        'src\core\FrameScopeLiveFrameStats.cs',
        'src\core\FrameScopeReportProgress.cs',
        'src\core\FrameScopePresentMonDiagnostics.cs',
        'src\core\FrameScopeReportArtifacts.cs',
        'src\core\FrameScopeReportPublisher.cs',
        'src\core\FrameScopeRunContract.cs',
        'src\reporting\FrameScopeReportGenerator.cs',
        'src\reporting\FrameScopeReportGenerator.Models.cs',
        'src\reporting\FrameScopeReportGenerator.Cli.cs',
        'src\reporting\FrameScopeReportGenerator.Progress.cs',
        'src\reporting\FrameScopeReportGenerator.Diagnostics.cs',
        'src\reporting\FrameScopeReportGenerator.PresentMon.cs',
        'src\reporting\FrameScopeReportGenerator.SystemData.cs',
        'src\reporting\FrameScopeReportGenerator.ProcessData.cs',
        'src\reporting\FrameScopeReportGenerator.Analysis.cs',
        'src\reporting\FrameScopeReportGenerator.Metadata.cs',
        'src\reporting\FrameScopeReportGenerator.Csv.cs',
        'src\reporting\FrameScopeReportGenerator.Html.cs',
        'src\reporting\FrameScopeReportGenerator.Html.Layout.cs',
        'src\reporting\FrameScopeReportGenerator.Html.Styles.cs',
        'src\reporting\FrameScopeReportGenerator.Html.Sections.cs',
        'src\reporting\FrameScopeReportGenerator.Html.Scripts.cs',
        'tests\FrameScopeOsdVoltageReportTests.cs',
        'src\monitoring\FrameScopeHardwareServiceClient.cs',
        'tests\FrameScopeReportManifestTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeDiagnosticsTests.exe' `
    -References @('System.Web.Extensions.dll', 'System.Management.dll') `
    -Sources @(
        'src\core\FrameScopeConfigStore.cs',
        'src\core\FrameScopeCapturePlanner.cs',
        'src\core\FrameScopePresentMonDiagnostics.cs',
        'src\core\FrameScopeReportProgress.cs',
        'src\diagnostics\FrameScopeDiagnostics.cs',
        'src\diagnostics\FrameScopeDiagnostics.Models.cs',
        'src\diagnostics\FrameScopeDiagnostics.Sections.cs',
        'src\diagnostics\FrameScopeDiagnostics.Markdown.cs',
        'src\diagnostics\FrameScopeDiagnostics.Redaction.cs',
        'src\diagnostics\FrameScopeDiagnostics.Retention.cs',
        'src\diagnostics\FrameScopeDiagnostics.IO.cs',
        'tests\FrameScopeDiagnosticsTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopePubgSimulatorTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @(
        'src\core\FrameScopeConfigStore.cs',
        'src\core\FrameScopeCapturePlanner.cs',
        'tools\FrameScopePubgSimulator\FrameScopePubgSimulationCommon.cs',
        'tests\FrameScopePubgSimulatorTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopePubgFakePresentMon.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @(
        'src\core\FrameScopeConfigStore.cs',
        'tools\FrameScopePubgSimulator\FrameScopePubgSimulationCommon.cs',
        'tools\FrameScopePubgSimulator\FakePresentMon.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeTargetLifecycleIntegrationTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -MainType 'FrameScopeTargetLifecycleIntegrationTests' `
    -Sources @(
        'src\core\FrameScopeConfigStore.cs',
        'src\core\FrameScopeTargetLifecycle.cs',
        'tools\FrameScopePubgSimulator\FrameScopePubgSimulationCommon.cs',
        'tests\FrameScopeTargetLifecycleIntegrationTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeWebBridgeTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @(
        'src\core\FrameScopeReportArtifacts.cs',
        'src\core\FrameScopeConfigStore.cs',
        'src\core\FrameScopeReportProgress.cs',
        'src\core\FrameScopeProcessPicker.cs',
        'src\core\FrameScopeTargetEditRules.cs',
        'src\app\FrameScopeWebBridge.Contracts.cs',
        'src\app\FrameScopeWebBridge.cs',
        'src\app\FrameScopeWebBridge.State.cs',
        'src\app\FrameScopeWebBridge.Config.cs',
        'src\app\FrameScopeWebBridge.Processes.cs',
        'src\app\FrameScopeWebBridge.Monitoring.cs',
        'src\app\FrameScopeWebBridge.Reports.cs',
        'src\app\FrameScopeWebBridge.Diagnostics.cs',
        'src\app\FrameScopeWebBridge.Targets.cs',
        'src\app\FrameScopeWebHostLifecycle.cs',
        'tests\FrameScopeWebBridgeTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeWebHostLifecycleTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @(
        'src\core\FrameScopeConfigStore.cs',
        'src\app\FrameScopeWebHostLifecycle.cs',
        'tests\FrameScopeWebHostLifecycleTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeProcessCleanupTests.exe' `
    -References @('System.Management.dll', 'System.Web.Extensions.dll') `
    -Sources @(
        'src\app\FrameScopeNativeMonitor.ProcessCleanup.cs',
        'src\core\FrameScopeWatcherShutdown.cs',
        'tests\FrameScopeProcessCleanupTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeWebView2RuntimeTests.exe' `
    -References @('System.Windows.Forms.dll') `
    -Sources @(
        'src\app\FrameScopeWebView2Runtime.cs',
        'tests\FrameScopeWebView2RuntimeTests.cs'
    )

Invoke-TestBuild `
    -OutputName 'FrameScopeIconTests.exe' `
    -References @('System.Drawing.dll', 'System.Windows.Forms.dll') `
    -Sources @('src\app\FrameScopeAppIcon.cs', 'tests\FrameScopeIconTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeSampledFpsAudit.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('tests\FrameScopeSampledFpsAudit.cs')

if (-not $TestNames.Count -or 'FrameScopeLiveFrameStatsTests.exe' -in $TestNames) {
    & (Join-Path $testRoot 'Build-LiveFrameStatsTests.ps1') -OutputDirectory $OutputDirectory -BuildOnly
    $builtTests.Add([pscustomobject]@{ Path = (Join-Path $OutputDirectory 'FrameScopeLiveFrameStatsTests.exe'); Arguments = @() })
}
Invoke-TestBuild `
    -OutputName 'FrameScopeHardwareSnapshotTests.exe' `
    -MainType 'FrameScopeHardwareSnapshotTests' `
    -References @('System.Management.dll', 'System.Web.Extensions.dll') `
    -Sources @('src\app\FrameScopeHardwareProvider.cs', 'tests\FrameScopeHardwareSnapshotTests.cs')
Invoke-TestBuild `
    -OutputName 'FrameScopeInstallationShutdownTests.exe' `
    -MainType 'FrameScopeInstallationShutdownTests' `
    -References @('System.Web.Extensions.dll', 'System.Management.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll') `
    -Sources @('packaging\FrameScopeInstallationShutdown.cs', 'src\core\FrameScopeWatcherShutdown.cs', 'packaging\FrameScopeUninstaller.cs', 'packaging\FrameScopeLegacyCleanup.cs', 'tests\FrameScopeInstallationShutdownTests.cs')
[pscustomobject]@{ RepoRoot = $root; RuntimeRoot = $RuntimeRoot; Tests = @($builtTests.ToArray()) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
'FrameScope tests rebuilt. Manifest: ' + $manifestPath
