param([switch]$BuildOnly, [switch]$CompileMonitor, [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'artifacts/20260918-repair/stop' }
New-Item -ItemType Directory -Path $out -Force | Out-Null
$manifestPath = Join-Path $out 'graceful-stop-manifest.json'
[pscustomobject]@{ RepoRoot = $root; Tests = @() } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
$buildMetadata = & (Join-Path $root 'tools/Write-FrameScopeBuildMetadata.ps1') -RepoRoot $root -OutputPath (Join-Path $out 'BuildMetadata.g.cs')
$exe = Join-Path $out 'FrameScopeGracefulStopTests.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(
    'src/app/FrameScopeNativeMonitor.Watcher.cs',
    'src/core/FrameScopeJsonFile.cs',
    'src/core/FrameScopeTargetLifecycle.cs',
    'src/core/FrameScopeLoggingPolicy.cs',
    'tests/FrameScopeGracefulStopTests.cs'
) | ForEach-Object { Join-Path $root $_ }
& $csc /nologo /target:exe /platform:x64 /codepage:65001 "/out:$exe" `
    /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll /reference:System.Management.dll @sources
if ($LASTEXITCODE -ne 0) { throw 'Graceful-stop test compilation failed' }
if ($CompileMonitor) {
    # Read only the literal source inventory. Never execute build.ps1:
    # its prologue stops installed processes.
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $root 'build.ps1'), [ref]$null, [ref]$null)
    $assignment = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left.Extent.Text -eq '$monitorSources'
    }, $true)
    if ($null -eq $assignment) { throw 'Production monitor source inventory missing' }
    $production = @($assignment.Right.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.StringConstantExpressionAst]
    }, $true) | ForEach-Object { Join-Path $root $_.Value })
    $production += Join-Path $root 'src/core/FrameScopeJsonFile.cs'
    $production += $buildMetadata
    foreach ($required in @('src\app\FrameScopeNativeMonitor.Watcher.cs',
        'src\app\FrameScopeNativeMonitor.MonitorSession.cs')) {
        if ($production -notcontains (Join-Path $root $required)) {
            throw "Production source inventory omitted $required"
        }
        Write-Host "Production source included: $required"
    }
    $refs = @('System.Core.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll',
        'System.Management.dll', 'System.Web.Extensions.dll',
        (Join-Path $root 'Microsoft.Web.WebView2.Core.dll'),
        (Join-Path $root 'Microsoft.Web.WebView2.WinForms.dll')) |
        ForEach-Object { "/reference:$_" }
    & $csc /nologo /target:winexe /platform:x64 /codepage:65001 `
        "/out:$(Join-Path $out 'FrameScopeMonitor.exe')" @refs @production
    if ($LASTEXITCODE -ne 0) { throw 'Isolated production monitor compilation failed' }
    Write-Host "Production monitor compiled into $out; not executed."
    Get-FileHash -LiteralPath (Join-Path $out 'FrameScopeMonitor.exe') -Algorithm SHA256 |
        Format-List Algorithm, Hash, Path
}
[pscustomobject]@{ RepoRoot = $root; Tests = @(
    [pscustomobject]@{ Path = $exe; Arguments = @() },
    [pscustomobject]@{ Path = $exe; Arguments = @('--test-idle') },
    [pscustomobject]@{ Path = $exe; Arguments = @('--test-report-failed') }
) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
if (-not $BuildOnly) {
    & (Join-Path $PSScriptRoot 'run-all-with-timeout.ps1') -ManifestPath $manifestPath -LogDirectory (Join-Path $out 'logs')
    if ($LASTEXITCODE -ne 0) { throw 'Graceful-stop behavior tests failed' }
}
