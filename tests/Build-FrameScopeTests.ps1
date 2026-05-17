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
    -OutputName 'FrameScopeCapturePlannerTests.exe' `
    -Sources @('src\core\FrameScopeCapturePlanner.cs', 'tests\FrameScopeCapturePlannerTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeReportProgressTests.exe' `
    -References @('System.Web.Extensions.dll') `
    -Sources @('src\core\FrameScopeReportProgress.cs', 'tests\FrameScopeReportProgressTests.cs')

Invoke-TestBuild `
    -OutputName 'FrameScopeReportManifestTests.exe' `
    -References @('System.Web.Extensions.dll', 'System.Management.dll', 'Microsoft.VisualBasic.dll') `
    -MainType 'FrameScopeReportManifestTests' `
    -Sources @(
        'src\core\FrameScopeReportProgress.cs',
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
    -OutputName 'FrameScopeUiStateTests.exe' `
    -Sources @('src\ui\FrameScopeUiState.cs', 'src\ui\FrameScopeMotion.cs', 'tests\FrameScopeUiStateTests.cs')

'FrameScope tests rebuilt.'
