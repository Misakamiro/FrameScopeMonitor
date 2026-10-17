param(
    [string[]]$ManifestPath = @((Join-Path $PSScriptRoot 'test-manifest.json')),
    [string]$LogDirectory = '',
    [int]$TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
if ($TimeoutSeconds -lt 1) { throw 'TimeoutSeconds must be positive' }
if (-not $LogDirectory) { $LogDirectory = Join-Path (Split-Path -Parent $ManifestPath[0]) 'test-logs' }
New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
$LogDirectory = [IO.Path]::GetFullPath($LogDirectory)
$results = @()
$executionBase = Join-Path ([IO.Path]::GetTempPath()) ('fs-tests-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $executionBase -Force | Out-Null
$previousRoot = $env:FRAMESCOPE_TEST_REPO_ROOT
$previousRuntimeRoot = $env:FRAMESCOPE_TEST_RUNTIME_ROOT
function Quote-TestArgument([string]$Value) {
    if ([string]::IsNullOrEmpty($Value)) { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}
try {
    foreach ($manifestFile in $ManifestPath) {
        $manifest = Get-Content -Raw -LiteralPath $manifestFile | ConvertFrom-Json
        if ($null -eq $manifest.PSObject.Properties['Tests'] -or $null -eq $manifest.Tests -or @($manifest.Tests).Count -eq 0) {
            throw "Manifest has no tests; complete a successful build first: $manifestFile"
        }
        foreach ($entry in @($manifest.Tests)) {
            if ($null -eq $entry -or $null -eq $entry.PSObject.Properties['Path'] -or [string]::IsNullOrWhiteSpace([string]$entry.Path)) {
                throw "Manifest contains an invalid test entry: $manifestFile"
            }
        }
        $env:FRAMESCOPE_TEST_REPO_ROOT = $manifest.RepoRoot
        $env:FRAMESCOPE_TEST_RUNTIME_ROOT = $manifest.RuntimeRoot
        $outputRoot = [IO.Path]::GetFullPath((Split-Path -Parent $manifestFile)).TrimEnd('\') + '\'
        foreach ($test in $manifest.Tests) {
            $exe = [IO.Path]::GetFullPath($test.Path)
            if (-not $exe.StartsWith($outputRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Manifest executable outside build directory: $exe" }
            if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Missing manifest executable: $exe" }
            $label = [IO.Path]::GetFileNameWithoutExtension($exe) + '-' + $results.Count
            $stdout = Join-Path $LogDirectory "$label.stdout.log"
            $stderr = Join-Path $LogDirectory "$label.stderr.log"
            $executionRoot = Join-Path $executionBase ('e' + $results.Count)
            New-Item -ItemType Directory -Path $executionRoot -Force | Out-Null
            $executionExe = Join-Path $executionRoot ([IO.Path]::GetFileName($exe))
            Copy-Item -LiteralPath $exe -Destination $executionExe
            foreach ($dependency in @($test.Dependencies)) {
                if (-not $dependency) { continue }
                $dependencyPath = [IO.Path]::GetFullPath($dependency)
                if (-not $dependencyPath.StartsWith($outputRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Dependency outside build directory: $dependencyPath" }
                Copy-Item -LiteralPath $dependencyPath -Destination $executionRoot
            }
            $start = @{ FilePath = $executionExe; PassThru = $true; WindowStyle = 'Hidden'; WorkingDirectory = $manifest.RepoRoot;
                RedirectStandardOutput = $stdout; RedirectStandardError = $stderr }
            if ($null -ne $test.Arguments -and @($test.Arguments).Count) {
                $start.ArgumentList = (@($test.Arguments | ForEach-Object { Quote-TestArgument ([string]$_) }) -join ' ')
            }
            $p = Start-Process @start
            $processHandle = $p.Handle
            $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
            if ($timedOut) {
                & taskkill.exe /PID $p.Id /T /F | Out-Null
                if (-not $p.WaitForExit(5000)) { throw "Timed-out test process could not be stopped: $($p.Id)" }
            }
            $p.Refresh()
            $exitCode = if ($timedOut) { -1 } else { $p.ExitCode }
            $results += [pscustomobject]@{ Path = $exe; ExecutionPath = $executionExe; Arguments = @($test.Arguments); ExitCode = $exitCode; TimedOut = $timedOut; Stdout = $stdout; Stderr = $stderr }
            Write-Output "$label exit=$exitCode timeout=$timedOut"
            $p.Dispose()
        }
    }
} finally {
    $env:FRAMESCOPE_TEST_REPO_ROOT = $previousRoot
    $env:FRAMESCOPE_TEST_RUNTIME_ROOT = $previousRuntimeRoot
    ConvertTo-Json -InputObject @($results) -Depth 5 | Set-Content -LiteralPath (Join-Path $LogDirectory 'results.json') -Encoding UTF8
}
$failed = @($results | Where-Object { $_.ExitCode -ne 0 })
Write-Output "PASS=$($results.Count - $failed.Count) FAIL=$($failed.Count)"
if ($failed.Count) { exit 1 }
exit 0
