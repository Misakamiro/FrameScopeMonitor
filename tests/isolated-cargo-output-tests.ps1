param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
. (Join-Path $RepoRoot 'tools\Build-IsolatedInstaller.ps1') -OutputDirectory 'unused-functions-only' -FunctionsOnly
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('framescope-cargo-output-' + [guid]::NewGuid().ToString('N'))
$snapshot = Join-Path $fixture 'source'
$crate = Join-Path $snapshot 'src\hardware-service'
New-Item -ItemType Directory -Path (Join-Path $crate 'src'), (Join-Path $fixture '.cargo') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $crate 'Cargo.toml'), "[package]`nname = 'isolated_cargo_fixture'`nversion = '0.0.0'`nedition = '2021'`n")
[IO.File]::WriteAllText((Join-Path $crate 'Cargo.lock'), "version = 4`n[[package]]`nname = 'isolated_cargo_fixture'`nversion = '0.0.0'`n")
[IO.File]::WriteAllText((Join-Path $crate 'src\lib.rs'), '')
Assert-IsolatedCargoOutputs -Snapshot $snapshot
Write-Host 'PASS real Cargo metadata accepts the expected private target and build directories'
$outside = (Join-Path $fixture 'must-not-write').Replace('\', '/')
$config = Join-Path $fixture '.cargo\config.toml'
foreach ($text in @("[build]`ntarget-dir = '$outside'", "[build]`n`"target-dir`" = '$outside'", "build.target-dir = '$outside'", "build = { target-dir = '$outside' }", "[build]`n`"build-dir`" = '$outside'")) {
    [IO.File]::WriteAllText($config, $text)
    $rejected = $false
    try { Assert-IsolatedCargoOutputs -Snapshot $snapshot } catch {
        if ($_ -notlike '*Cargo output escapes*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Cargo configuration redirect was accepted: $text" }
    if (Test-Path -LiteralPath (Join-Path $fixture 'must-not-write')) { throw 'Metadata verification wrote redirected build outputs' }
    Write-Host "PASS inherited Cargo output redirection rejected: $text"
}
Write-Host "Retained guarded Cargo metadata fixture: $fixture"
