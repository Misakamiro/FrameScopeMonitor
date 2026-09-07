# Copies the hardware backend (service + LHM provider + runtime DLLs) next to
# the preview executable so the Tauri host can spawn the sampling service.
# Run AFTER `npx tauri build --no-bundle`; repo-root DLLs come from build.ps1's
# locked dependency restore.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$target = Join-Path $RepoRoot 'desktop-preview\src-tauri\target\release'
if (-not (Test-Path -LiteralPath (Join-Path $target 'framescope-ui-preview.exe') -PathType Leaf)) {
    throw "Preview executable not found - run 'npx tauri build --no-bundle' first: $target"
}

$lock = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'dependencies.lock.json') | ConvertFrom-Json
$files = @('FrameScopeHardwareService.exe', 'FrameScopeHardwareProvider.exe')
foreach ($property in $lock.runtimeFiles.PSObject.Properties) {
    if ($property.Name -notlike 'Microsoft.Web.*' -and $property.Name -ne 'WebView2Loader.dll') {
        $files += $property.Name
    }
}

foreach ($file in $files) {
    $source = Join-Path $RepoRoot $file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Backend file missing at repo root (run build.ps1 once to restore dependencies): $source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $target $file) -Force
    Write-Host "Deployed $file"
}
Write-Host "Preview backend ready: $target"
