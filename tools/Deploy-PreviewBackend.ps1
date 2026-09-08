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

# Stop any running FrameScope processes first - Copy-Item -Force fails on
# in-use executables, which would leave a half-updated deployment.
foreach ($name in @('framescope-ui-preview', 'FrameScopeMonitor', 'FrameScopeHardwareService',
        'FrameScopeHardwareProvider', 'FrameScopeProcessSampler', 'FrameScopeSystemSampler',
        'PresentMon-2.4.1-x64')) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 500

$lock = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'dependencies.lock.json') | ConvertFrom-Json
$files = @(
    'FrameScopeHardwareService.exe',
    'FrameScopeHardwareProvider.exe',
    'FrameScopeMonitor.exe',
    'FrameScopeProcessSampler.exe',
    'FrameScopeSystemSampler.exe',
    'FrameScopeReportGenerator.exe'
)
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

# PresentMon must sit under tools\ next to the preview exe (the monitor
# session resolves it at <root>\tools\PresentMon*.exe).
$toolsTarget = Join-Path $target 'tools'
New-Item -ItemType Directory -Path $toolsTarget -Force | Out-Null
$presentMon = Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'tools') -Filter 'PresentMon*.exe' -File |
    Select-Object -First 1
if ($null -eq $presentMon) { throw 'PresentMon exe was not found under repo tools\' }
Copy-Item -LiteralPath $presentMon.FullName -Destination (Join-Path $toolsTarget $presentMon.Name) -Force
Write-Host "Deployed tools\$($presentMon.Name)"
Write-Host "Preview backend ready: $target"
