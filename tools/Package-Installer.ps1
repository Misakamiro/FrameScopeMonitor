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
    [ValidateSet("Auto","Full","Cumulative")][string]$PackageMode = "Auto",
    [string]$PreviousComponents,
    [string]$ComponentOutputDirectory,
    [string]$OutputDirectory,
    [string]$CandidateHostTransformScript,
    [switch]$PreserveStaging
)

$ErrorActionPreference = 'Stop'

if ($OutputDirectory) {
    & (Join-Path $RepoRoot 'tools\Build-IsolatedInstaller.ps1') -RepoRoot $RepoRoot -OutputDirectory $OutputDirectory -StageOnly:$StageOnly -PackageOnly -PackageMode $PackageMode -PreviousComponents $PreviousComponents -ComponentOutputDirectory $ComponentOutputDirectory -CandidateHostTransformScript $CandidateHostTransformScript -PreserveStaging:$PreserveStaging
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
. (Join-Path $RepoRoot 'desktop-preview\installer\pack-payload.ps1') -RepoRoot $RepoRoot -FunctionsOnly -PackageMode $PackageMode -PreserveStaging:$PreserveStaging
$PackageMode = Resolve-FrameScopePackageMode -Root $RepoRoot -Mode $PackageMode
if ($PackageMode -eq 'Cumulative' -and -not $PreviousComponents) { throw 'Cumulative staging requires the previous release update-components.json' }
if (-not $StageOnly -and $PackageMode -eq 'Cumulative') { throw 'Cumulative packages use the native installer' }
$inputsBefore = Get-PayloadInputs -Root $RepoRoot -Mode $PackageMode

Write-Host '== [2/6] Building the C# chain and hardware service (build.ps1) =='
$buildArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $RepoRoot 'build.ps1'), '-SkipLegacyPackages')
if ($PreserveStaging) { $buildArguments += '-PreserveStaging' }
& powershell @buildArguments
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed with exit code $LASTEXITCODE" }


Write-Host '== [3/6] Staging the backend chain into bundle resources =='
Assert-PayloadPath -Path $resourcesDir -Root $RepoRoot
if ($PreserveStaging) {
    Move-PreservedBuildStaging -Path $resourcesDir -Root $RepoRoot | Out-Null
} elseif (Test-Path -LiteralPath $resourcesDir) { Remove-Item -LiteralPath $resourcesDir -Recurse -Force }
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
# Capture self-diagnosis script ships in tools\ for installed users.
$captureDiagnostics = Join-Path $RepoRoot 'tools\diagnostics\diagnose-pubg-capture.ps1'
if (Test-Path -LiteralPath $captureDiagnostics -PathType Leaf) {
    Copy-Item -LiteralPath $captureDiagnostics -Destination (Join-Path $toolsDir 'diagnose-pubg-capture.ps1') -Force
}
Write-Host ("Staged {0} backend files + PresentMon + font + licenses + PawnIO" -f $files.Count)
if (-not $ComponentOutputDirectory) { $ComponentOutputDirectory = Join-Path $targetDir 'update-components' }
Assert-PayloadPath -Path $ComponentOutputDirectory -Root $RepoRoot
$componentArguments = @('-X','utf8',(Join-Path $RepoRoot 'tools/prepare-update-components.py'),'--root',$RepoRoot,
    '--output',$ComponentOutputDirectory,'--tag',('v'+(Get-Content -LiteralPath (Join-Path $RepoRoot 'VERSION') -Raw).Trim()),
    '--stage',$resourcesDir,'--kind',$PackageMode.ToLowerInvariant())
if ($PreviousComponents) { $componentArguments += @('--previous',[IO.Path]::GetFullPath($PreviousComponents)) }
& python @componentArguments
if ($LASTEXITCODE -ne 0) { throw 'Update component preparation failed' }


Copy-Item -LiteralPath (Join-Path $RepoRoot 'packaging\README-FrameScopeMonitor.txt') -Destination $backendDir -Force

Write-Host '== [4/6] Building the frontend (npm run build) =='
if ($PreserveStaging) {
    Move-PreservedBuildStaging -Path (Join-Path $previewDir 'dist') -Root $RepoRoot | Out-Null
}
Push-Location $previewDir
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE" }
} finally { Pop-Location }

Write-Host '== [5/6] Building and verifying the production host =='
& (Join-Path $RepoRoot 'tools\Verify-IsolatedBuildStage.ps1') -RepoRoot $RepoRoot
# Cargo checks source freshness even when the final artifact is incremental.
Push-Location (Join-Path $previewDir 'src-tauri')
$previousHostConfig = $env:TAURI_CONFIG
try {
    if ($PackageMode -eq 'Cumulative') {
        # The native installer validates and merges components; this host build
        # does not produce a Tauri bundle containing those omitted files.
        $hostConfig = if ($previousHostConfig) { $previousHostConfig | ConvertFrom-Json } else { [pscustomobject]@{} }
        if (-not $hostConfig.bundle) { $hostConfig | Add-Member -NotePropertyName bundle -NotePropertyValue ([pscustomobject]@{}) }
        $hostConfig.bundle | Add-Member -NotePropertyName resources -NotePropertyValue @() -Force
        $env:TAURI_CONFIG = $hostConfig | ConvertTo-Json -Depth 40 -Compress
    }
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
} finally { $env:TAURI_CONFIG = $previousHostConfig; Pop-Location }
# All host transforms (manifest/signing) must precede this marker and receipt.
# A later transform invalidates both; this entry deliberately has no refresh bypass.
Write-HostCapabilities -Root $RepoRoot
$version = (Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'VERSION')).Trim()
$sha = $env:FRAMESCOPE_BUILD_SHA
if (-not $sha) { $sha = (& git -C $RepoRoot rev-parse HEAD) }
if ($sha -notmatch '^[a-f0-9]{40}$') { throw 'Missing full build SHA' }
$edition=(Get-Content -LiteralPath (Join-Path $RepoRoot 'EDITION') -Raw).Trim()
if($edition -notin @('offline','full')){throw 'Invalid build edition'}
if($edition -eq 'full' -and -not $env:FRAMESCOPE_ACCOUNT_ORIGIN){throw 'Full build requires private FRAMESCOPE_ACCOUNT_ORIGIN'}
$buildInfo = [ordered]@{version=$version; git_sha=$sha; channel=$(if($version.Contains('-')){'beta'}else{'stable'}); edition=$edition}
[IO.File]::WriteAllText((Join-Path $backendDir 'FrameScopeBuildInfo.json'),($buildInfo | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $RepoRoot 'packaging/Restore-FrameScope.cmd') -Destination (Join-Path $backendDir 'Restore-FrameScope.cmd')
$hostTransform = $null
if ($CandidateHostTransformScript) { $hostTransform = Invoke-CandidateHostTransform -Root $RepoRoot -Script $CandidateHostTransformScript }
$inputsAfter = Get-PayloadInputs -Root $RepoRoot -Mode $PackageMode
if (($inputsBefore | ConvertTo-Json -Depth 5 -Compress) -cne ($inputsAfter | ConvertTo-Json -Depth 5 -Compress)) {
    throw 'Sources changed during build; rebuild before packaging'
}
$receipt = [ordered]@{ packageMode = $PackageMode; componentCatalog = (Join-Path $ComponentOutputDirectory "update-components.json"); inputs = @($inputsAfter); files = @(Get-PayloadFiles -Root $RepoRoot); hostTransform = $hostTransform }
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $targetDir 'payload-build-receipt.json') -Encoding UTF8

if ($StageOnly) {
    Write-Host 'StageOnly: current backend, frontend and host verified; NSIS skipped'
    return
}
Push-Location $previewDir
try {
    & npx tauri bundle --bundles nsis --ci --features tauri/custom-protocol
    if ($LASTEXITCODE -ne 0) { throw 'NSIS bundling failed' }
} finally { Pop-Location }
$bundleDir = Join-Path $targetDir 'bundle\nsis'
$installer = Get-ChildItem -LiteralPath $bundleDir -Filter '*.exe' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $installer) { throw "No installer produced under $bundleDir" }
Write-Host ("Installer ready: {0}" -f $installer.FullName)
Write-Host ("Host binary verified: {0}" -f (Join-Path $targetDir 'FrameScope.exe'))
