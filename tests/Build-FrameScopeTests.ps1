$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$testRoot = Join-Path $root 'tests'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found: $csc"
}

function Invoke-TestBuild {
    param(
        [string]$OutputName,
        [string[]]$Sources,
        [string[]]$References = @(),
        [string]$MainType = ''
    )

    $args = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', ('/out:' + (Join-Path $testRoot $OutputName)))
    if (-not [string]::IsNullOrWhiteSpace($MainType)) {
        $args += ('/main:' + $MainType)
    }
    foreach ($reference in $References) {
        $args += ('/reference:' + $reference)
    }
    foreach ($source in $Sources) {
        $args += (Join-Path $root $source)
    }

    & $csc @args
    if ($LASTEXITCODE -ne 0) { throw "csc failed: $OutputName" }
}

Invoke-TestBuild `
    -OutputName 'FrameScopeConfigStoreTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('src\core\FrameScopeConfigStore.cs', 'tests\FrameScopeConfigStoreTests.cs')

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
        'src\core\FrameScopeConfigStore.cs',
        'src\core\FrameScopeReportProgress.cs',
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
        'src\core\FrameScopeReportProgress.cs',
        'src\core\FrameScopePresentMonDiagnostics.cs',
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

'FrameScope tests rebuilt.'
