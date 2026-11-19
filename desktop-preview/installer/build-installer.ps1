# Builds the FrameScope native installer end-to-end:
#   1. build current app/backend via tools/Package-Installer.ps1 -StageOnly
#   2. pack payload.zip
#   3. validate the native payload
#   4. cargo build --release the Win32 installer
#   5. copy the single exe out as FrameScope_<version>_x64-setup.exe
#
# The legacy NSIS pipeline (tools/Package-Installer.ps1) is untouched; this is
# the parallel native-installer path.
param(
    [switch]$SkipPayload,
    [ValidateSet("Auto","Full","Cumulative","Both")][string]$PackageMode = "Auto",
    [string]$PreviousComponents,
    [string]$ComponentOutputDirectory,
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path,
    [string]$OutputDirectory,
    [string]$CandidateHostTransformScript,
    [switch]$PreserveStaging
)
$ErrorActionPreference = 'Stop'

if ($OutputDirectory) {
    if ($SkipPayload) { throw 'Isolated builds require a fresh payload; SkipPayload is not supported with OutputDirectory' }
    & (Join-Path $RepoRoot 'tools\Build-IsolatedInstaller.ps1') -RepoRoot $RepoRoot -OutputDirectory $OutputDirectory -PackageMode $PackageMode -PreviousComponents $PreviousComponents -ComponentOutputDirectory $ComponentOutputDirectory -CandidateHostTransformScript $CandidateHostTransformScript -PreserveStaging:$PreserveStaging
    return
}
if ($SkipPayload -and $CandidateHostTransformScript) { throw 'Candidate transforms require fresh payload staging' }

$installerDir = $PSScriptRoot
$srcTauri     = Join-Path $installerDir 'src-tauri'
$outDir       = Join-Path $RepoRoot 'desktop-preview\src-tauri\target\release\bundle\installer'

$requestedPackageMode = $PackageMode
. (Join-Path $installerDir 'pack-payload.ps1') -RepoRoot $RepoRoot -FunctionsOnly -PackageMode $(if($PackageMode -eq 'Both'){'Full'}else{$PackageMode}) -PreserveStaging:$PreserveStaging
$version = (Get-Content -LiteralPath (Join-Path $RepoRoot 'VERSION') -Raw).Trim()
$resolvedMode = Resolve-FrameScopePackageMode -Root $RepoRoot -Mode $PackageMode
$kinds = @(if ($requestedPackageMode -eq 'Both' -or ($requestedPackageMode -eq 'Auto' -and ($version -eq '2.1.3-beta.14' -or -not $version.Contains('-')))) { 'Full','Cumulative' } else { $resolvedMode })
$stagingMode = $kinds[0]
$buildTimer = [Diagnostics.Stopwatch]::StartNew()

# 1. Payload staging ----------------------------------------------------------
if (-not $SkipPayload) {
    Write-Host '[1/5] staging app payload (Package-Installer.ps1 staging section)...' -ForegroundColor Cyan
    & (Join-Path $RepoRoot 'tools\Package-Installer.ps1') -StageOnly -RepoRoot $RepoRoot -PackageMode $stagingMode -PreviousComponents $PreviousComponents -ComponentOutputDirectory $ComponentOutputDirectory -CandidateHostTransformScript $CandidateHostTransformScript -PreserveStaging:$PreserveStaging
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw "payload staging failed" }
} else {
    Write-Host '[1/5] skipping payload staging (-SkipPayload)' -ForegroundColor Yellow
}

foreach ($kind in $kinds) {
    $packageTimer = [Diagnostics.Stopwatch]::StartNew()
    # 2. pack payload.zip ---------------------------------------------------------
    Write-Host ('[2/5] packing '+$kind+' payload.zip...') -ForegroundColor Cyan
    & (Join-Path $installerDir 'pack-payload.ps1') -RepoRoot $RepoRoot -PackageMode $kind -PreserveStaging:$PreserveStaging
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw "payload packing failed" }

    $buildIdentity = Get-Content -LiteralPath (Join-Path $RepoRoot 'desktop-preview/src-tauri/resources/backend/FrameScopeBuildInfo.json') -Raw | ConvertFrom-Json
    & (Join-Path $RepoRoot 'tools/Test-ReleasePayload.ps1') -Payload (Join-Path $srcTauri 'payload.zip') -Version $version -GitSha $buildIdentity.git_sha -PackageMode $kind

    # Native Win32 UI has no frontend build or WebView runtime dependency.

    # 4. rust ---------------------------------------------------------------------
    Write-Host '[4/5] building installer binary...' -ForegroundColor Cyan
    & (Join-Path $RepoRoot 'tools\Verify-IsolatedBuildStage.ps1') -RepoRoot $RepoRoot
    Push-Location $srcTauri
    $previousRustFlags = $env:RUSTFLAGS
    try {
        $env:RUSTFLAGS = "$previousRustFlags -C target-feature=+crt-static".Trim()
        $payloadHash = (Get-FileHash -LiteralPath (Join-Path $srcTauri 'payload.zip')).Hash
        $messages = @(& cargo build --release --locked --target x86_64-pc-windows-msvc --message-format=json-render-diagnostics)
        if ($LASTEXITCODE -ne 0) { throw "cargo build failed" }
        $decoded = @($messages | ForEach-Object { $_ | ConvertFrom-Json })
        $artifacts = @($decoded | ForEach-Object {
            $message = $_
            if ($message.reason -eq 'compiler-artifact' -and $message.target.name -eq 'framescope-installer' -and $message.executable) { $message }
        })
        if ($artifacts.Count -ne 1) { throw 'Cargo did not identify exactly one installer executable' }
        $bin = Get-Item -LiteralPath $artifacts[0].executable
        if ((Get-FileHash -LiteralPath (Join-Path $srcTauri 'payload.zip')).Hash -ne $payloadHash) { throw 'Payload changed while compiling installer' }
    } finally { $env:RUSTFLAGS = $previousRustFlags; Pop-Location }

    # 5. ship ---------------------------------------------------------------------
    $version = (Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'VERSION')).Trim()

    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $suffix = if ($kind -eq "Cumulative") { "update" } else { "setup" }
    $dest = Join-Path $outDir "FrameScope_${version}_x64-${suffix}.exe"
    Copy-Item -LiteralPath $bin.FullName -Destination $dest -Force
    $installerHash = (Get-FileHash -LiteralPath $dest).Hash
    if ($installerHash -ne (Get-FileHash -LiteralPath $bin.FullName).Hash) { throw 'Installer copy hash mismatch' }
    [ordered]@{ packageMode = $kind; elapsedSeconds = $packageTimer.Elapsed.TotalSeconds; installer = $dest; installerSha256 = $installerHash; payloadSha256 = $payloadHash } |
        ConvertTo-Json | Set-Content -LiteralPath "$dest.sha256.json" -Encoding UTF8

    Write-Host "[5/5] done: $dest ($([math]::Round($bin.Length / 1MB, 1)) MB)" -ForegroundColor Green
    Write-Host "Installer SHA256=$installerHash Payload SHA256=$payloadHash"
}
Write-Host ('Verified installer build completed in '+[math]::Round($buildTimer.Elapsed.TotalSeconds,1)+' seconds')
