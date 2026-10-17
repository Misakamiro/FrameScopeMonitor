param(
    [string]$OutputDir,
    [ValidateRange(1, 100)]
    [int]$LifecycleRepeats = 10,
    [switch]$PipeOnly
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) {
    $OutputDir = Join-Path $repo ('artifacts/20260918-delivery/pipe/rerun-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$results = [Collections.Generic.List[object]]::new()

function Invoke-CargoGate {
    param([string]$Name, [string[]]$CargoArgs)
    $log = Join-Path $OutputDir ($Name + '.log')
    $started = [DateTimeOffset]::Now
    & cargo @CargoArgs 2>&1 | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
    $results.Add([pscustomobject]@{
        name = $Name
        command = 'cargo ' + ($CargoArgs -join ' ')
        exitCode = $code
        started = $started.ToString('o')
        ended = [DateTimeOffset]::Now.ToString('o')
        log = $log
    })
    $results | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $OutputDir 'gates.json')
    if ($code -ne 0) { throw "$Name failed with exit code $code. See $log" }
}

Push-Location $repo
try {
    Invoke-CargoGate 'service-tests' @(
        'test', '--manifest-path', 'src/hardware-service/Cargo.toml',
        '--locked', '--', '--test-threads=1', '--nocapture'
    )
    if ($PipeOnly) {
        Invoke-CargoGate 'host-pipe-tests' @(
            'test', '--manifest-path', 'desktop-preview/src-tauri/Cargo.toml',
            '--locked', 'pipe_audit', '--', '--test-threads=1', '--nocapture'
        )
    } else {
        Invoke-CargoGate 'host-tests' @(
            'test', '--manifest-path', 'desktop-preview/src-tauri/Cargo.toml',
            '--locked', '--', '--test-threads=1', '--nocapture'
        )
    }
    for ($i = 1; $i -le $LifecycleRepeats; $i++) {
        Invoke-CargoGate ('lifecycle-{0:d2}' -f $i) @(
            'test', '--manifest-path', 'src/hardware-service/Cargo.toml',
            '--locked', '--test', 'pipe_lifecycle', '--', '--test-threads=1', '--nocapture'
        )
    }
    Invoke-CargoGate 'service-release-build' @(
        'build', '--manifest-path', 'src/hardware-service/Cargo.toml', '--locked', '--release'
    )
    Invoke-CargoGate 'host-release-build' @(
        'build', '--manifest-path', 'desktop-preview/src-tauri/Cargo.toml', '--locked', '--release',
        '--features', 'tauri/custom-protocol'
    )
    Invoke-CargoGate 'service-release-lifecycle' @(
        'test', '--manifest-path', 'src/hardware-service/Cargo.toml', '--locked',
        '--release', '--test', 'pipe_lifecycle', '--', '--test-threads=1', '--nocapture'
    )

    $binaries = Join-Path $OutputDir 'binaries'
    New-Item -ItemType Directory -Force -Path $binaries | Out-Null
    $payloads = @(
        @{
            Source = Join-Path $repo 'src/hardware-service/target/release/framescope-hardware-service.exe'
            Name = 'FrameScopeHardwareService.exe'
        },
        @{
            Source = Join-Path $repo 'desktop-preview/src-tauri/target/release/framescope-ui-preview.exe'
            Name = 'FrameScope.exe'
        }
    )
    $hashes = foreach ($payload in $payloads) {
        $destination = Join-Path $binaries $payload.Name
        Copy-Item -LiteralPath $payload.Source -Destination $destination
        $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $payload.Source).Hash
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $destination).Hash
        if ($hash -ne $sourceHash) { throw "Copied payload hash mismatch: $destination" }
        [pscustomobject]@{
            path = $destination
            source = $payload.Source
            length = (Get-Item -LiteralPath $destination).Length
            sha256 = $hash
        }
    }
    $hashes | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $OutputDir 'binary-hashes.json')
    $sourcePaths = @(
        'desktop-preview/src-tauri/src/hardware_service.rs',
        'desktop-preview/src-tauri/src/hardware_service_tests.rs',
        'src/hardware-service/src/main.rs',
        'src/hardware-service/src/lib.rs',
        'src/hardware-service/tests/pipe_lifecycle.rs',
        'src/hardware-service/tests/fixtures/pipe_fake_helper.rs',
        'tests/Run-HardwarePipeAudit.ps1'
    )
    $sourceHashes = foreach ($path in $sourcePaths) {
        [pscustomobject]@{
            path = $path
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $path)).Hash
        }
    }
    $sourceHashes | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $OutputDir 'source-hashes.json')
    Write-Host "All selected pipe audit gates finished. PipeOnly=$PipeOnly Evidence: $OutputDir"
} finally {
    Pop-Location
}
