# Builds the self-contained FrameScope NSIS installer.
#
# Pipeline: stop processes -> build.ps1 (C# chain + hardware service) ->
# stage the backend chain into src-tauri/resources -> npm run build ->
# tauri build (NSIS bundle) -> verify fresh timestamps (a stale exe means
# vite succeeded but the link failed with exit 0 - the known silent trap).
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$previewDir = Join-Path $RepoRoot 'desktop-preview'
$targetDir = Join-Path $previewDir 'src-tauri\target\release'
$resourcesDir = Join-Path $previewDir 'src-tauri\resources'

Write-Host '== [1/6] Stopping FrameScope processes =='
foreach ($name in @('FrameScope', 'framescope-ui-preview', 'FrameScopeMonitor', 'FrameScopeHardwareService',
        'FrameScopeHardwareProvider', 'FrameScopeProcessSampler', 'FrameScopeSystemSampler',
        'PresentMon-2.4.1-x64')) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 500

Write-Host '== [2/6] Building the C# chain and hardware service (build.ps1) =='
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed with exit code $LASTEXITCODE" }

Write-Host '== [3/6] Staging the backend chain into bundle resources =='
if (Test-Path -LiteralPath $resourcesDir) { Remove-Item -LiteralPath $resourcesDir -Recurse -Force }
$backendDir = Join-Path $resourcesDir 'backend'
$toolsDir = Join-Path $backendDir 'tools'
$fontsDir = Join-Path $resourcesDir 'fonts'
New-Item -ItemType Directory -Path $backendDir -Force | Out-Null
New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
New-Item -ItemType Directory -Path $fontsDir -Force | Out-Null

$files = @(
    'FrameScopeHardwareService.exe',
    'FrameScopeHardwareProvider.exe',
    'FrameScopeMonitor.exe',
    'FrameScopeProcessSampler.exe',
    'FrameScopeSystemSampler.exe',
    'FrameScopeReportGenerator.exe'
)
$lock = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'dependencies.lock.json') | ConvertFrom-Json
foreach ($property in $lock.runtimeFiles.PSObject.Properties) {
    if ($property.Name -notlike 'Microsoft.Web.*' -and $property.Name -ne 'WebView2Loader.dll') {
        $files += $property.Name
    }
}
foreach ($file in $files) {
    $source = Join-Path $RepoRoot $file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Backend file missing at repo root (build.ps1 must run first): $source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $backendDir $file) -Force
}
$presentMon = Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'tools') -Filter 'PresentMon*.exe' -File |
    Select-Object -First 1
if ($null -eq $presentMon) { throw 'PresentMon exe was not found under repo tools\' }
Copy-Item -LiteralPath $presentMon.FullName -Destination (Join-Path $toolsDir $presentMon.Name) -Force
$font = Join-Path $previewDir 'public\fonts\MiSansVF.ttf'
if (Test-Path -LiteralPath $font) {
    Copy-Item -LiteralPath $font -Destination (Join-Path $fontsDir 'MiSansVF.ttf') -Force
} else {
    Write-Warning "MiSansVF.ttf not found at $font - the GDI overlay will fall back to a system font"
}
Write-Host ("Staged {0} backend files + PresentMon + font" -f $files.Count)

Write-Host '== [4/6] Building the frontend (npm run build) =='
Push-Location $previewDir
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE" }
} finally { Pop-Location }

Write-Host '== [5/6] Building the NSIS installer (tauri build) =='
Push-Location $previewDir
try {
    & npx tauri build
    if ($LASTEXITCODE -ne 0) { throw "tauri build failed with exit code $LASTEXITCODE" }
} finally { Pop-Location }

Write-Host '== [6/6] Verifying build outputs =='
# Iron rule: tauri build reports exit 0 even when the final link fails while
# the exe is locked; only a fresh timestamp proves the binary is new. With
# mainBinaryName=FrameScope the renamed copy lands beside the crate bin.
$candidates = @('FrameScope.exe', 'framescope-ui-preview.exe') | ForEach-Object { Join-Path $targetDir $_ }
$fresh = @( $candidates | Where-Object { (Test-Path -LiteralPath $_ -PathType Leaf) -and (((Get-Date) - (Get-Item -LiteralPath $_).LastWriteTime).TotalMinutes -le 10) } )
if (-not $fresh) {
    throw "No fresh host executable in $targetDir (checked FrameScope.exe / framescope-ui-preview.exe) - the link likely failed silently"
}
$bundleDir = Join-Path $targetDir 'bundle\nsis'
$installer = Get-ChildItem -LiteralPath $bundleDir -Filter '*.exe' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $installer) { throw "No installer produced under $bundleDir" }
Write-Host ("Installer ready: {0}" -f $installer.FullName)
Write-Host ("Host binary: {0} (written {1})" -f $fresh[0], (Get-Item -LiteralPath $fresh[0]).LastWriteTime)
