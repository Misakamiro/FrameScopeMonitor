# Builds the self-contained FrameScope NSIS installer.
#
# Pipeline: build.ps1 (C# chain + hardware service) ->
# stage the backend chain into src-tauri/resources -> npm run build ->
# tauri build (NSIS bundle) -> verify fresh timestamps (a stale exe means
# vite succeeded but the link failed with exit 0 - the known silent trap).
#
# -StageOnly: build backend AND current frontend/host, skipping only NSIS.
# The native installer pipeline
# (desktop-preview/installer/build-installer.ps1) reuses this to prepare the
# payload without building the NSIS bundle.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$StageOnly,
    [string]$OutputDirectory,
    [string]$CandidateHostTransformScript
)

$ErrorActionPreference = 'Stop'
if ($OutputDirectory) {
    & (Join-Path $RepoRoot 'tools\Build-IsolatedInstaller.ps1') -RepoRoot $RepoRoot -OutputDirectory $OutputDirectory -StageOnly:$StageOnly -PackageOnly -CandidateHostTransformScript $CandidateHostTransformScript
    return
}
if ($CandidateHostTransformScript) {
    $inventoryPath = Join-Path (Split-Path -Parent $RepoRoot) 'snapshot-inputs.json'
    if (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) { throw 'Candidate host transforms require OutputDirectory isolation' }
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([IO.Path]::GetFullPath($RepoRoot) -ne [IO.Path]::GetFullPath($inventory.snapshotRoot)) { throw 'Candidate transform snapshot root mismatch' }
}
$previewDir = Join-Path $RepoRoot 'desktop-preview'
$targetDir = Join-Path $previewDir 'src-tauri\target\release'
$resourcesDir = Join-Path $previewDir 'src-tauri\resources'

Write-Host '== [1/6] Build only; running applications are never stopped =='
. (Join-Path $RepoRoot 'desktop-preview\installer\pack-payload.ps1') -RepoRoot $RepoRoot -FunctionsOnly
$inputsBefore = Get-PayloadInputs -Root $RepoRoot

Write-Host '== [2/6] Building the C# chain and hardware service (build.ps1) =='
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed with exit code $LASTEXITCODE" }

Write-Host '== [3/6] Staging the backend chain into bundle resources =='
Assert-PayloadPath -Path $resourcesDir -Root $RepoRoot
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
    'FrameScopeReportGenerator.exe',
    'FrameScopeUninstaller.exe',
    'PRIVACY.md',
    'THIRD-PARTY-NOTICES.md'
)
$lock = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $RepoRoot 'dependencies.lock.json') | ConvertFrom-Json
foreach ($property in $lock.runtimeFiles.PSObject.Properties) {
    $files += $property.Name
}
foreach ($file in $files) {
    $source = Join-Path $RepoRoot $file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Backend file missing at repo root (build.ps1 must run first): $source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $backendDir $file) -Force
}
$presentMon = Get-Item -LiteralPath (Join-Path $RepoRoot $lock.presentMon.file)
if ((Get-FileHash -LiteralPath $presentMon.FullName).Hash -ne $lock.presentMon.sha256) { throw 'PresentMon hash does not match dependency lock' }
Copy-Item -LiteralPath $presentMon.FullName -Destination (Join-Path $toolsDir $presentMon.Name) -Force
$font = Join-Path $previewDir 'public\fonts\MiSansVF.ttf'
if (Test-Path -LiteralPath $font) {
    Copy-Item -LiteralPath $font -Destination (Join-Path $fontsDir 'MiSansVF.ttf') -Force
} else {
    Write-Warning "MiSansVF.ttf not found at $font - the GDI overlay will fall back to a system font"
}
# GPL compliance: the license texts ride along with the distributed binary.
foreach ($lic in @('LICENSE', 'LICENSE.EXTRA')) {
    $licPath = Join-Path $previewDir $lic
    if (Test-Path -LiteralPath $licPath -PathType Leaf) {
        Copy-Item -LiteralPath $licPath -Destination (Join-Path $backendDir $lic) -Force
    } else {
        Write-Warning "License file missing: $licPath"
    }
}
# PawnIO driver setup: the app resolves dependencies\PawnIO\PawnIO_setup.exe
# beside the deployed exe; without staging it here neither installer ships it.
# Third-party exes are not committed - prefer the packaging copy, fall back to
# the vendored reference tree.
$pawnIOSources = @(
    (Join-Path $RepoRoot 'packaging\PawnIO_setup.exe'),
    (Join-Path $RepoRoot 'reference\nexbox\src-tauri\resources\binaries\PawnIO_setup.exe')
)
$pawnIOSetup = $pawnIOSources |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if ($pawnIOSetup) {
    $dependenciesDir = Join-Path $resourcesDir 'dependencies\PawnIO'
    New-Item -ItemType Directory -Path $dependenciesDir -Force | Out-Null
    Copy-Item -LiteralPath $pawnIOSetup -Destination (Join-Path $dependenciesDir 'PawnIO_setup.exe') -Force
    Write-Host "Staged PawnIO driver setup from $pawnIOSetup"
} else {
    Write-Warning 'PawnIO_setup.exe not found (packaging\ or reference\nexbox\...) - temperature driver install will be unavailable'
}
# Capture self-diagnosis script ships in tools\ for installed users.
$captureDiagnostics = Join-Path $RepoRoot 'tools\diagnostics\diagnose-pubg-capture.ps1'
if (Test-Path -LiteralPath $captureDiagnostics -PathType Leaf) {
    Copy-Item -LiteralPath $captureDiagnostics -Destination (Join-Path $toolsDir 'diagnose-pubg-capture.ps1') -Force
}
Write-Host ("Staged {0} backend files + PresentMon + font + licenses + PawnIO" -f $files.Count)

Copy-Item -LiteralPath (Join-Path $RepoRoot 'packaging\README-FrameScopeMonitor.txt') -Destination $backendDir -Force

Write-Host '== [4/6] Building the frontend (npm run build) =='
Push-Location $previewDir
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE" }
} finally { Pop-Location }

Write-Host '== [5/6] Building the NSIS installer (tauri build) =='
& (Join-Path $RepoRoot 'tools\Verify-IsolatedBuildStage.ps1') -RepoRoot $RepoRoot
Push-Location $previewDir
try {
    if ($StageOnly) { & npx tauri build --no-bundle --ci }
    else { & npx tauri build --ci }
    if ($LASTEXITCODE -ne 0) { throw "tauri build failed with exit code $LASTEXITCODE" }
} finally { Pop-Location }

Write-Host '== [6/6] Verifying build outputs =='
# Cargo checks source freshness even when the final artifact is incremental.
Push-Location (Join-Path $previewDir 'src-tauri')
try {
    $messages = @(& cargo build --release --locked --features tauri/custom-protocol --message-format=json-render-diagnostics)
    if ($LASTEXITCODE -ne 0) { throw 'Host cargo verification failed' }
    $decoded = @($messages | ForEach-Object { $_ | ConvertFrom-Json })
    if (-not @($decoded | Where-Object { $_.reason -eq 'compiler-artifact' -and $_.target.name -eq 'tauri' -and $_.features -contains 'custom-protocol' }).Count) {
        throw 'Cargo did not build Tauri in production protocol mode'
    }
    $artifacts = @($decoded | ForEach-Object {
        $message = $_
        if ($message.reason -eq 'compiler-artifact' -and $message.target.name -eq 'framescope-ui-preview' -and $message.executable) { $message }
    })
    if ($artifacts.Count -ne 1) { throw 'Cargo did not identify exactly one main host executable' }
    Copy-VerifiedHost -Source $artifacts[0].executable -Destination (Join-Path $targetDir 'FrameScope.exe')
} finally { Pop-Location }
# All host transforms (manifest/signing) must precede this marker and receipt.
# A later transform invalidates both; this entry deliberately has no refresh bypass.
Write-HostCapabilities -Root $RepoRoot
$hostTransform = $null
if ($CandidateHostTransformScript) { $hostTransform = Invoke-CandidateHostTransform -Root $RepoRoot -Script $CandidateHostTransformScript }
$inputsAfter = Get-PayloadInputs -Root $RepoRoot
if (($inputsBefore | ConvertTo-Json -Depth 5 -Compress) -cne ($inputsAfter | ConvertTo-Json -Depth 5 -Compress)) {
    throw 'Sources changed during build; rebuild before packaging'
}
$receipt = [ordered]@{ inputs = @($inputsAfter); files = @(Get-PayloadFiles -Root $RepoRoot); hostTransform = $hostTransform }
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $targetDir 'payload-build-receipt.json') -Encoding UTF8
if ($StageOnly) {
    Write-Host 'StageOnly: current backend, frontend and host verified; NSIS skipped'
    return
}
$bundleDir = Join-Path $targetDir 'bundle\nsis'
$installer = Get-ChildItem -LiteralPath $bundleDir -Filter '*.exe' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $installer) { throw "No installer produced under $bundleDir" }
Write-Host ("Installer ready: {0}" -f $installer.FullName)
Write-Host ("Host binary verified: {0}" -f (Join-Path $targetDir 'FrameScope.exe'))
