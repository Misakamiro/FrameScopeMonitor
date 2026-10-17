param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'Use a new isolated output directory.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$fixturePath = (Join-Path $PSScriptRoot 'FrameScopeBoundedProcessFixture.rs').Replace('\','/')
@"
[package]
name="report-process-fixture"
version="0.1.0"
edition="2021"
[workspace]
[[bin]]
name="report-process-fixture"
path="$fixturePath"
[features]
baseline=[]
[dependencies]
windows-sys={version="0.59",features=["Win32_Foundation","Win32_Security","Win32_System_Diagnostics_ToolHelp","Win32_System_Threading","Win32_System_JobObjects","Win32_System_Pipes"]}
"@ | Set-Content -LiteralPath (Join-Path $outputRoot 'Cargo.toml') -Encoding UTF8
& cargo build --manifest-path (Join-Path $outputRoot 'Cargo.toml') --target-dir (Join-Path $outputRoot 'target') 2>&1 | Tee-Object -FilePath (Join-Path $outputRoot 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Fixture build failed: $LASTEXITCODE" }
$exe = Join-Path $outputRoot 'target\debug\report-process-fixture.exe'
$results = foreach ($mode in @('inherited','output','nonzero','hang','hang-tree','flood')) {
    $info = [Diagnostics.ProcessStartInfo]::new($exe, "fixed $mode")
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(8000)) { $process.Kill(); throw "Fixture $mode exceeded its external deadline." }
    $result = [pscustomobject]@{ Mode=$mode; ExitCode=$process.ExitCode; Stdout=$stdout.GetAwaiter().GetResult(); Stderr=$stderr.GetAwaiter().GetResult() }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot "$mode.json")
    if ($result.ExitCode -ne 0) { throw "Fixture $mode failed: $($result.Stderr)" }
    Write-Host $result.Stdout.Trim()
    $process.Dispose()
    $result
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'results.json')
@($fixturePath, (Join-Path $repoRoot 'desktop-preview\src-tauri\src\bounded_process.rs'), $exe) | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'build-source-hashes.json')
