# Builds the FrameScope native installer end-to-end:
#   1. build current app/backend via tools/Package-Installer.ps1 -StageOnly
#   2. pack payload.zip
#   3. vite build the frontend
#   4. cargo build --release the Tauri installer
#   5. copy the single exe out as FrameScope_<version>_x64-setup.exe
#
# The legacy NSIS pipeline (tools/Package-Installer.ps1) is untouched; this is
# the parallel native-installer path.
param(
    [switch]$SkipPayload,
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path,
    [string]$OutputDirectory,
    [string]$CandidateHostTransformScript
)
$ErrorActionPreference = 'Stop'
if ($OutputDirectory) {
    if ($SkipPayload) { throw 'Isolated builds require a fresh payload; SkipPayload is not supported with OutputDirectory' }
    & (Join-Path $RepoRoot 'tools\Build-IsolatedInstaller.ps1') -RepoRoot $RepoRoot -OutputDirectory $OutputDirectory -CandidateHostTransformScript $CandidateHostTransformScript
    return
}
if ($SkipPayload -and $CandidateHostTransformScript) { throw 'Candidate transforms require fresh payload staging' }

$installerDir = $PSScriptRoot
$srcTauri     = Join-Path $installerDir 'src-tauri'
$outDir       = Join-Path $RepoRoot 'desktop-preview\src-tauri\target\release\bundle\installer'

# 1. Payload staging ----------------------------------------------------------
if (-not $SkipPayload) {
    Write-Host '[1/5] staging app payload (Package-Installer.ps1 staging section)...' -ForegroundColor Cyan
    & (Join-Path $RepoRoot 'tools\Package-Installer.ps1') -StageOnly -RepoRoot $RepoRoot -CandidateHostTransformScript $CandidateHostTransformScript
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw "payload staging failed" }
} else {
    Write-Host '[1/5] skipping payload staging (-SkipPayload)' -ForegroundColor Yellow
}

# 2. pack payload.zip ---------------------------------------------------------
Write-Host '[2/5] packing payload.zip...' -ForegroundColor Cyan
& (Join-Path $installerDir 'pack-payload.ps1') -RepoRoot $RepoRoot
if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw "payload packing failed" }

# 3. frontend -----------------------------------------------------------------
Write-Host '[3/5] building frontend...' -ForegroundColor Cyan
Push-Location $installerDir
try {
    if (-not (Test-Path -LiteralPath node_modules)) {
        & npm ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
    }
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "vite build failed" }
} finally { Pop-Location }

# 4. rust ---------------------------------------------------------------------
Write-Host '[4/5] building installer binary...' -ForegroundColor Cyan
& (Join-Path $RepoRoot 'tools\Verify-IsolatedBuildStage.ps1') -RepoRoot $RepoRoot
Push-Location $srcTauri
try {
    $payloadHash = (Get-FileHash -LiteralPath (Join-Path $srcTauri 'payload.zip')).Hash
    $messages = @(& cargo build --release --locked --features tauri/custom-protocol --message-format=json-render-diagnostics)
    if ($LASTEXITCODE -ne 0) { throw "cargo build failed" }
    $decoded = @($messages | ForEach-Object { $_ | ConvertFrom-Json })
    if (-not @($decoded | Where-Object { $_.reason -eq 'compiler-artifact' -and $_.target.name -eq 'tauri' -and $_.features -contains 'custom-protocol' }).Count) {
        throw 'Cargo did not build Tauri in production protocol mode'
    }
    $artifacts = @($decoded | ForEach-Object {
        $message = $_
        if ($message.reason -eq 'compiler-artifact' -and $message.target.name -eq 'framescope-installer' -and $message.executable) { $message }
    })
    if ($artifacts.Count -ne 1) { throw 'Cargo did not identify exactly one installer executable' }
    $bin = Get-Item -LiteralPath $artifacts[0].executable
    if ((Get-FileHash -LiteralPath (Join-Path $srcTauri 'payload.zip')).Hash -ne $payloadHash) { throw 'Payload changed while compiling installer' }
} finally { Pop-Location }

# 5. ship ---------------------------------------------------------------------
$version = '1.0.0'
$mainCargo = Join-Path $RepoRoot 'desktop-preview\src-tauri\Cargo.toml'
if (Test-Path $mainCargo) {
    $line = (Get-Content $mainCargo | Where-Object { $_ -like 'version = "*' } | Select-Object -First 1)
    if ($line) { $version = $line.Split('"')[1] }
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$dest = Join-Path $outDir "FrameScope_${version}_x64-setup.exe"
Copy-Item -LiteralPath $bin.FullName -Destination $dest -Force
$installerHash = (Get-FileHash -LiteralPath $dest).Hash
if ($installerHash -ne (Get-FileHash -LiteralPath $bin.FullName).Hash) { throw 'Installer copy hash mismatch' }
[ordered]@{ installer = $dest; installerSha256 = $installerHash; payloadSha256 = $payloadHash } |
    ConvertTo-Json | Set-Content -LiteralPath "$dest.sha256.json" -Encoding UTF8

Write-Host "[5/5] done: $dest ($([math]::Round($bin.Length / 1MB, 1)) MB)" -ForegroundColor Green
Write-Host "Installer SHA256=$installerHash Payload SHA256=$payloadHash"
