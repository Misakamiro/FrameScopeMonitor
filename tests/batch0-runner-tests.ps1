param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$source = Join-Path $output 'RunnerFixture.cs'
$fixture = @'
using System;
using System.IO;
using System.Threading;
public static class RunnerFixture {
    public static int Main(string[] args) {
        if (args[0] == "missingRuntime") {
            try { string path = FrameScopeTestPaths.RuntimeRoot; return 8; }
            catch (InvalidOperationException) { return 9; }
        }
        if (FrameScopeTestPaths.SourceRoot != Environment.GetEnvironmentVariable("FRAMESCOPE_TEST_REPO_ROOT") ||
            FrameScopeTestPaths.RuntimeRoot != Environment.GetEnvironmentVariable("FRAMESCOPE_TEST_RUNTIME_ROOT")) return 6;
        if (args[0] == "quoted") return args.Length == 5 && args[1] == "with spaces" &&
            args[2] == "word \"quoted\"" && args[3] == "" && args[4] == "C:\\path with spaces\\" ? 0 : 10;
        if (args[0] == "stale") File.WriteAllText(args[1], "unexpected stale execution");
        if (args[0] == "sleep") Thread.Sleep(10000);
        Console.WriteLine("fixture " + args[0]);
        return args[0] == "fail" ? 7 : 0;
    }
}
'@
[IO.File]::WriteAllText($source, $fixture)
$exe = Join-Path $output 'RunnerFixture.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /target:exe "/out:$exe" $source (Join-Path $PSScriptRoot 'FrameScopeTestPaths.cs')
if ($LASTEXITCODE) { throw 'Fixture compilation failed' }
$staleExe = Join-Path $output 'StaleTests.exe'
Copy-Item -LiteralPath $exe -Destination $staleExe
$manifest = Join-Path $output 'fixture-manifest.json'
[pscustomobject]@{ RepoRoot = $root; RuntimeRoot = $output; Tests = @(
    [pscustomobject]@{ Path = $exe; Arguments = @('pass') },
    [pscustomobject]@{ Path = $exe; Arguments = @('fail') },
    [pscustomobject]@{ Path = $exe; Arguments = @('sleep') }
) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding UTF8
$logs = Join-Path $output 'logs'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-all-with-timeout.ps1') -ManifestPath $manifest -LogDirectory $logs -TimeoutSeconds 1
if ($LASTEXITCODE -ne 1) { throw 'Runner must fail when a test fails or times out' }
$results = Get-Content -Raw (Join-Path $logs 'results.json') | ConvertFrom-Json
if ($results.Count -ne 3) { throw 'Runner must execute only the three manifest entries, excluding stale exe' }
if ($results[0].ExitCode -ne 0 -or $results[1].ExitCode -ne 7 -or $results[2].ExitCode -ne -1 -or -not $results[2].TimedOut) { throw 'Pass/failure/timeout result mismatch' }
foreach ($result in $results) {
    if ($result.ExecutionPath -eq $result.Path) { throw 'Each test must execute its own copy' }
    if (-not (Test-Path $result.Stdout) -or -not (Test-Path $result.Stderr)) { throw 'Per-test logs missing' }
}
if (@(Get-Process RunnerFixture -ErrorAction SilentlyContinue).Count) { throw 'Timed-out fixture process remains' }
[pscustomobject]@{ RepoRoot = $root; Tests = @([pscustomobject]@{ Path = $exe; Arguments = @('missingRuntime') }) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding UTF8
$missingLogs = Join-Path $output 'missing-runtime-logs'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-all-with-timeout.ps1') -ManifestPath $manifest -LogDirectory $missingLogs
if ($LASTEXITCODE -ne 1) { throw 'Missing runtime must fail explicitly' }
$missing = Get-Content -Raw (Join-Path $missingLogs 'results.json') | ConvertFrom-Json
if ($missing[0].ExitCode -ne 9) { throw 'Missing runtime helper must reject fallback to old repo executables' }
[pscustomobject]@{ RepoRoot = $root; RuntimeRoot = $output; Tests = @([pscustomobject]@{
    Path = $exe; Arguments = @('quoted', 'with spaces', 'word "quoted"', '', 'C:\path with spaces\')
}) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding UTF8
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-all-with-timeout.ps1') -ManifestPath $manifest -LogDirectory (Join-Path $output 'quoted-logs')
if ($LASTEXITCODE -ne 0) { throw 'Runner must preserve spaces, quotes, empty arguments and trailing backslashes' }
foreach ($invalid in @('{"RepoRoot":"unused"}', '{"Tests":null}', '{"Tests":[]}', '{"Tests":[null]}')) {
    $invalid | Set-Content -LiteralPath $manifest -Encoding UTF8
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-all-with-timeout.ps1') -ManifestPath $manifest -LogDirectory (Join-Path $output ('invalid-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)))
    if ($LASTEXITCODE -eq 0) { throw 'Invalid or empty manifest must never report zero-test success' }
}
'PASS: manifest only, independent copies, failure and timeout codes, per-test logs, no fixture remains, source/runtime root propagation, missing runtime rejected'
