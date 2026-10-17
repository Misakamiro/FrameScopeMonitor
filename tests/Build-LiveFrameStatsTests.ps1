param([string]$OutputDirectory = '', [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\20261011-report-live-low' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'build.ps1'),[ref]$null,[ref]$null)
function Source-List([string]$name) {
    $assignment = $ast.FindAll({param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and $node.Left.VariablePath.UserPath -eq $name},$true)[0]
    @($assignment.Right.FindAll({param($node) $node -is [Management.Automation.Language.StringConstantExpressionAst]},$true) |
        ForEach-Object { Join-Path $root $_.Value })
}
$common = @(Source-List 'commonCoreSources')
$report = @(Source-List 'reportSources')
$monitor = @(Source-List 'monitorSources')
$metadata = & (Join-Path $root 'tools\Write-FrameScopeBuildMetadata.ps1') -RepoRoot $root -OutputPath (Join-Path $OutputDirectory 'BuildMetadata.g.cs')
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$baseArgs = @('/nologo','/platform:x64','/optimize+','/codepage:65001','/r:System.Web.Extensions.dll','/r:System.Management.dll')
$reportOptions = @($baseArgs + @('/r:Microsoft.VisualBasic.dll'))
$reportSources = @($common + $report + @($metadata, (Join-Path $root 'src\core\FrameScopeCapturePlanner.cs')))
& $csc @reportOptions /target:exe ('/out:' + (Join-Path $OutputDirectory 'FrameScopeReportGenerator.exe')) @reportSources
if ($LASTEXITCODE) { throw 'Report build failed' }
& $csc @reportOptions /target:exe /main:FrameScopeLiveFrameStatsTests ('/out:' + (Join-Path $OutputDirectory 'FrameScopeLiveFrameStatsTests.exe')) @reportSources (Join-Path $root 'tests\FrameScopeLiveFrameStatsTests.cs')
if ($LASTEXITCODE) { throw 'Live statistics test build failed' }
if (-not $BuildOnly) {
    & (Join-Path $OutputDirectory 'FrameScopeLiveFrameStatsTests.exe')
    if ($LASTEXITCODE) { throw 'Live statistics tests failed' }
}
$sdk = Join-Path $root 'desktop-preview\src-tauri\resources\backend'
if (-not (Test-Path (Join-Path $sdk 'Microsoft.Web.WebView2.Core.dll'))) { $sdk = 'C:\Program Files\FrameScope' }
$monitorOptions = @($baseArgs + @('/r:System.Windows.Forms.dll','/r:System.Drawing.dll',
    ('/r:' + (Join-Path $sdk 'Microsoft.Web.WebView2.Core.dll')),('/r:' + (Join-Path $sdk 'Microsoft.Web.WebView2.WinForms.dll'))))
$monitorSources = @($common + $monitor + @($metadata))
& $csc @monitorOptions /target:winexe ('/out:' + (Join-Path $OutputDirectory 'FrameScopeMonitor.exe')) @monitorSources
if ($LASTEXITCODE) { throw 'Monitor worker build failed' }
if ($BuildOnly) { Write-Output 'PASS: isolated live statistics test and monitor/report builds; tests not executed' }
else { Write-Output 'PASS: live statistics tests and isolated monitor/report builds' }
