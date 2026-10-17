param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = @()
$diagnostics = Get-Content -Raw (Join-Path $root 'src/diagnostics/FrameScopeDiagnostics.cs')
if ($diagnostics -notmatch 'string diagnosticOutputRoot = null, Action<string> cleanupObserver = null') {
    $failures += 'GenerateReport isolated output and cleanup observer interface missing; dangerous legacy method was not called.'
}
foreach ($script in @('Build-FrameScopeTests.ps1', 'Build-GracefulStopTests.ps1')) {
    $source = Get-Content -Raw (Join-Path $PSScriptRoot $script)
    if ($source -notmatch '\[string\]\$OutputDirectory' -or $source -notmatch '-OutputPath') {
        $failures += "$script isolated output/metadata interface missing."
    }
}
$runner = Get-Content -Raw (Join-Path $PSScriptRoot 'run-all-with-timeout.ps1')
if ($runner -notmatch 'FRAMESCOPE_TEST_RUNTIME_ROOT') { $failures += 'Runner explicit runtime root interface missing.' }
foreach ($name in @('ConfigStore', 'Version', 'MonitoringReliability', 'ReportProcess', 'Icon')) {
    $source = Get-Content -Raw (Join-Path $PSScriptRoot ("FrameScope${name}Tests.cs"))
    if ($source -notmatch 'FrameScopeTestPaths.SourceRoot') { $failures += "$name source root isolation missing." }
}
foreach ($name in @('NativeMonitorChildProcess', 'TargetLifecycleIntegration')) {
    $source = Get-Content -Raw (Join-Path $PSScriptRoot ("FrameScope${name}Tests.cs"))
    if ($source -notmatch 'FrameScopeTestPaths.RuntimeRoot') { $failures += "$name explicit runtime isolation missing." }
}
if ($runner -notmatch 'ManifestPath' -or $runner -match "Get-ChildItem 'tests'") {
    $failures += 'Runner must use explicit build manifest instead of scanning stale executables.'
}
$stop = Get-Content -Raw (Join-Path $PSScriptRoot 'FrameScopeGracefulStopTests.cs')
if ($stop -notmatch 'RecoverStaleMissingReports\(string root, FrameScopeConfig config, Action<string>') {
    $failures += 'GracefulStop recovery substitute callback missing.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if ($failures.Count) {
    $failures | Tee-Object -FilePath (Join-Path $OutputDirectory 'safety-last.log') | Write-Output
    exit 1
}
'PASS: batch0 safety interfaces and manifest runner' | Tee-Object -FilePath (Join-Path $OutputDirectory 'safety-last.log')
