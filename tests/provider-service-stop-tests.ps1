param(
    [Parameter(Mandatory)][string]$ServicePath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$fixture = Join-Path $output 'FakeProvider.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 "/out:$fixture" (Join-Path $PSScriptRoot 'FrameScopeFakeHardwareProvider.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fake provider compile failed' }
$service = [IO.Path]::GetFullPath($ServicePath)
if (-not $service.StartsWith((Join-Path $root 'artifacts\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Tests require a service built in the isolated artifacts tree'
}
$results = @()
foreach ($mode in @('normal-exit', 'hang-exit')) {
    $helper = Join-Path $output ($mode + '.exe')
    Copy-Item -LiteralPath $fixture -Destination $helper -Force
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $service
    $start.Arguments = '--lhm-helper "' + $helper + '"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables['TEMP'] = $output
    $start.EnvironmentVariables['TMP'] = $output
    $process = [Diagnostics.Process]::Start($start)
    $stderr = $process.StandardError.ReadToEndAsync()
    $ownedProvider = $null
    try {
        $process.StandardInput.WriteLine('{"id":"read","method":"get_snapshot","params":{}}')
        $read = $process.StandardOutput.ReadLineAsync()
        if (-not $read.Wait(25000)) { throw 'Snapshot deadline exceeded' }
        $snapshot = $read.Result | ConvertFrom-Json
        if (-not $snapshot.ok -or $snapshot.result.availability -ne 'available') { throw 'Fake snapshot unavailable' }
        $providerPid = [int](Get-Content -LiteralPath (Join-Path $output ($mode + '.pid')))
        $ownedProvider = [Diagnostics.Process]::GetProcessById($providerPid)
        if ($ownedProvider.MainModule.FileName -ne $helper) { throw 'Unexpected provider identity' }
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $process.StandardInput.WriteLine('{"id":"stop","method":"stop","params":{}}')
        $reply = $process.StandardOutput.ReadLineAsync()
        if (-not $reply.Wait(12000)) { throw 'Stop reply deadline exceeded' }
        $stop = $reply.Result | ConvertFrom-Json
        if (-not $stop.ok -or -not $stop.result.shutdownComplete) { throw 'Cleanup not confirmed' }
        if (-not $ownedProvider.WaitForExit(1000)) { throw 'Provider still running after cleanup ACK' }
        if (-not $process.WaitForExit(2000) -or $process.ExitCode -ne 0) { throw 'Service failed to exit' }
        if (-not $stderr.Wait(1000)) { throw 'Service stderr drain deadline exceeded' }
        $stderr.Result | Set-Content -LiteralPath (Join-Path $output ($mode + '-stderr.log')) -Encoding utf8
        $results += [pscustomobject]@{ Mode=$mode; ProviderPid=$providerPid; ServicePid=$process.Id; StopMs=$timer.ElapsedMilliseconds; ShutdownComplete=$true; ExitCode=$process.ExitCode }
    }
    finally {
        # Only fixture processes created above may be stopped on test failure.
        if ($ownedProvider) {
            if (-not $ownedProvider.HasExited -and $ownedProvider.MainModule.FileName -eq $helper) {
                $ownedProvider.Kill()
                $ownedProvider.WaitForExit(2000) | Out-Null
            }
            $ownedProvider.Dispose()
        }
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(2000) | Out-Null }
        $process.Dispose()
    }
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
$results | Format-Table
Write-Output 'PASS: production service cleanup ACK follows actual isolated provider exit'
