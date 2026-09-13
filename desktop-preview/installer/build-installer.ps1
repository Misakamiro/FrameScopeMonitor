# Builds the FrameScope native installer end-to-end:
#   1. (optional) stage the app payload via tools/Package-Installer.ps1 -StageOnly
#   2. pack payload.zip
#   3. vite build the frontend
#   4. cargo build --release the Tauri installer
#   5. copy the single exe out as FrameScope_<version>_x64-setup.exe
#
# The legacy NSIS pipeline (tools/Package-Installer.ps1) is untouched; this is
# the parallel native-installer path.
param(
    [switch]$SkipPayload,
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
)
$ErrorActionPreference = 'Stop'

$installerDir = $PSScriptRoot
$srcTauri     = Join-Path $installerDir 'src-tauri'
$outDir       = Join-Path $RepoRoot 'desktop-preview\src-tauri\target\release\bundle\installer'

# 1. Payload staging ----------------------------------------------------------
if (-not $SkipPayload) {
    Write-Host '[1/5] staging app payload (Package-Installer.ps1 staging section)...' -ForegroundColor Cyan
    & (Join-Path $RepoRoot 'tools\Package-Installer.ps1') -StageOnly -RepoRoot $RepoRoot
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
if (-not (Test-Path node_modules)) { npm install; if ($LASTEXITCODE -ne 0) { throw "npm install failed" } }
npm run build
if ($LASTEXITCODE -ne 0) { throw "vite build failed" }
Pop-Location

# 4. rust ---------------------------------------------------------------------
Write-Host '[4/5] building installer binary...' -ForegroundColor Cyan
Push-Location $srcTauri
cargo build --release
if ($LASTEXITCODE -ne 0) { throw "cargo build failed" }
Pop-Location

# 5. ship ---------------------------------------------------------------------
$version = '1.0.0'
$mainCargo = Join-Path $RepoRoot 'desktop-preview\src-tauri\Cargo.toml'
if (Test-Path $mainCargo) {
    $line = (Get-Content $mainCargo | Where-Object { $_ -like 'version = "*' } | Select-Object -First 1)
    if ($line) { $version = $line.Split('"')[1] }
}

# Iron rule: verify the freshly-linked exe actually got built (the main app
# was bitten by "vite succeeded, link failed, exit 0" before).
$binName = 'framescope-installer.exe'
$bin = Get-ChildItem (Join-Path $srcTauri 'target\release') -Filter *.exe |
    Where-Object { $_.Name -in @('framescope-installer.exe', 'FrameScope Installer.exe') } |
    Select-Object -First 1
if ($null -eq $bin) { throw "installer exe not found in target\release" }
$age = (Get-Date) - $bin.LastWriteTime
if ($age.TotalMinutes -gt 15) { throw "installer exe looks stale ($($bin.LastWriteTime)) - link may have failed silently" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$dest = Join-Path $outDir "FrameScope_${version}_x64-setup.exe"
Copy-Item $bin.FullName $dest -Force

Write-Host "[5/5] done: $dest ($([math]::Round($bin.Length / 1MB, 1)) MB)" -ForegroundColor Green
