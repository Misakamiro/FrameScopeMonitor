param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [switch]$StageOnly,
    [switch]$PackageOnly,
    [switch]$SnapshotOnly,
    [switch]$VerifyOnly,
    [switch]$FunctionsOnly,
    [string]$CandidateHostTransformScript,
    [string]$StorageRoot = (Join-Path ([IO.Path]::GetPathRoot($RepoRoot)) 'fs-build')
)
$ErrorActionPreference = 'Stop'

function Assert-IsolatedBuildStorage([string]$Directory, [string]$StorageRoot, [long]$FreeBytes) {
    $root = [IO.Path]::GetFullPath($StorageRoot).TrimEnd('\')
    $output = [IO.Path]::GetFullPath($Directory).TrimEnd('\')
    if (-not $output.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputDirectory must be under the managed build root: $root"
    }
    if (-not [IO.Path]::GetDirectoryName($output).Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Each build must be a direct child of the managed build root'
    }
    Assert-PayloadPath -Path $output -Root $root
    if ($FreeBytes -lt 30GB) { throw 'Build requires at least 30 GiB of free disk space; no output was created' }
    # Count failed/incomplete directories too: repeated failures must not create
    # an unlimited collection just because their inventory was never written.
    if ((Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Directory -Force).Count -ge 2) {
        throw 'At most two retained build directories are allowed; inspect and remove obsolete outputs before retrying. Nothing was deleted automatically.'
    }
}

function Get-IsolatedSourceFiles([string]$Root) {
    $gitOutput = Get-PayloadGitOutput ('-C "' + $Root + '" -c core.quotepath=false ls-files --cached --others --exclude-standard')
    $names = @($gitOutput -split "`r?`n" | Where-Object { $_ })
    foreach ($name in ($names | Sort-Object -Unique)) {
        # Neither historical evidence nor generated/installed/user content is a build input.
        if ($name -match '(^|/)(artifacts|backups|reference|node_modules|target|dist|bin|obj|coverage|\.playwright-cli|\.vite|\.cache|framescope-runs|cs2-monitor-runs|script-backups|install-backups|--run)(/|$)' -or
            $name -match '^desktop-preview/src-tauri/resources/' -or
            $name -match '(^|/)(framescope-config\.json|framescope-history\.jsonl|framescope-watcher-state\.json)$' -or
            $name -match '\.(exe|dll|pdb|zip|log|pyc)$') { continue }
        $path = Join-Path $Root $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue } # staged deletions
        Assert-PayloadPath -Path $path -Root $Root
        [ordered]@{ path = $name; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    $lock = Get-Content -LiteralPath (Join-Path $Root 'dependencies.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($entry in @($lock.presentMon, $lock.webView2StandaloneInstaller)) {
        $path = Join-Path $Root $entry.file
        Assert-PayloadPath -Path $path -Root $Root
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($hash -ne $entry.sha256) { throw "Pinned dependency hash mismatch: $($entry.file)" }
        [ordered]@{ path = [string]$entry.file; sha256 = $hash }
    }
    # PawnIO has no dependency-lock entry today. Bind this explicit ignored input
    # to its observed bytes in the immutable snapshot inventory, never to a fallback at build time.
    $pawn = 'packaging/PawnIO_setup.exe'
    if (-not (Test-Path -LiteralPath (Join-Path $Root $pawn) -PathType Leaf)) {
        $pawn = 'reference/nexbox/src-tauri/resources/binaries/PawnIO_setup.exe'
    }
    $path = Join-Path $Root $pawn
    Assert-PayloadPath -Path $path -Root $Root
    [ordered]@{ path = $pawn; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}

function Assert-IsolatedSourceUnchanged([string]$Directory) {
    $provenance = Get-Content -LiteralPath (Join-Path $Directory 'snapshot-inputs.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $snapshot = Join-Path $Directory 'source'
    $head = (& git -C $provenance.sourceRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $head -ne $provenance.sourceHead) { throw 'Original source HEAD changed during isolated build' }
    $current = @(Get-IsolatedSourceFiles -Root $provenance.sourceRoot)
    $currentMap = @{}
    foreach ($entry in $current) { $currentMap[[string]$entry.path] = [string]$entry.sha256 }
    $provenanceMap = @{}
    foreach ($entry in $provenance.files) { $provenanceMap[[string]$entry.path] = [string]$entry.sha256 }
    $different = @()
    foreach ($path in @(($currentMap.Keys + $provenanceMap.Keys) | Sort-Object -Unique)) {
        if (-not $currentMap.ContainsKey($path) -or -not $provenanceMap.ContainsKey($path) -or $currentMap[$path] -cne $provenanceMap[$path]) {
            $different += $path
        }
    }
    if ($different.Count) { throw ('Original source inputs changed; isolated candidate cannot be packed: ' + (($different | Select-Object -First 5) -join ', ')) }
    $snapshotMap = @{}
    foreach ($entry in @(Get-IsolatedSourceFiles -Root $snapshot)) { $snapshotMap[[string]$entry.path] = [string]$entry.sha256 }
    foreach ($path in @(($snapshotMap.Keys + $provenanceMap.Keys) | Sort-Object -Unique)) {
        # This derived output may be regenerated only in the private snapshot.
        if ($path -eq 'framescope-config.example.json') { continue }
        if (-not $snapshotMap.ContainsKey($path) -or -not $provenanceMap.ContainsKey($path) -or $snapshotMap[$path] -cne $provenanceMap[$path]) {
            throw "Snapshot source changed: $path"
        }
    }
    foreach ($file in $provenance.files) {
        # build.ps1 derives this one example from the verified config-store source.
        # Its original bytes remain in the source inventory; regenerated bytes are outputs.
        if ($file.path -eq 'framescope-config.example.json') { continue }
        $path = Join-Path $snapshot $file.path
        Assert-PayloadPath -Path $path -Root $snapshot
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Snapshot source changed: $($file.path)" }
    }
}

function Assert-IsolatedCargoOutputs([string]$Snapshot) {
    foreach ($relative in @('src\hardware-service', 'desktop-preview\src-tauri', 'desktop-preview\installer\src-tauri')) {
        $directory = Join-Path $Snapshot $relative
        $manifest = Join-Path $directory 'Cargo.toml'
        if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { continue }
        Push-Location $directory
        try {
            # Ask Cargo itself: this covers ancestor/global configuration, quoted
            # TOML keys and environment values using the same working directory.
            $json = @(& cargo metadata --manifest-path $manifest --locked --offline --no-deps --format-version 1)
            if ($LASTEXITCODE -ne 0) { throw "Cannot verify isolated Cargo outputs: $manifest" }
            $metadata = ($json -join "`n") | ConvertFrom-Json
            $expected = [IO.Path]::GetFullPath((Join-Path $directory 'target')).TrimEnd('\')
            foreach ($property in @('target_directory', 'build_directory')) {
                $actual = [string]$metadata.$property
                if (-not $actual -and $property -eq 'build_directory') { $actual = [string]$metadata.target_directory }
                if (-not $actual -or -not [string]::Equals([IO.Path]::GetFullPath($actual).TrimEnd('\'), $expected, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Cargo output escapes the expected isolated directory: $property=$actual expected=$expected"
                }
            }
        } finally { Pop-Location }
    }
}

if ($FunctionsOnly) { return }
$isolatedEnvironment = @{}
foreach ($name in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_COMMON_DIR', 'GIT_OBJECT_DIRECTORY', 'GIT_ALTERNATE_OBJECT_DIRECTORIES', 'CARGO_TARGET_DIR', 'CARGO_BUILD_TARGET_DIR', 'CARGO_BUILD_BUILD_DIR', 'FRAMESCOPE_BUILD_SHA')) {
    $isolatedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue
}
try {
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$isolatedOutput = $OutputDirectory
. (Join-Path $RepoRoot 'desktop-preview\installer\pack-payload.ps1') -RepoRoot $RepoRoot -FunctionsOnly
$OutputDirectory = $isolatedOutput
if ($VerifyOnly) {
    Assert-IsolatedSourceUnchanged -Directory $OutputDirectory
    Assert-IsolatedCargoOutputs -Snapshot (Join-Path $OutputDirectory 'source')
    return
}
Assert-IsolatedBuildStorage -Directory $OutputDirectory -StorageRoot $StorageRoot `
    -FreeBytes ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($OutputDirectory)).AvailableFreeSpace)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'OutputDirectory must be a new private directory; refusing reuse or overwrite' }
$sourcePrefix = $RepoRoot.TrimEnd('\') + '\'
if ($OutputDirectory.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase) -and
    -not $OutputDirectory.StartsWith((Join-Path $RepoRoot 'artifacts').TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'An output inside the original repository must be under its ignored artifacts directory'
}
$ancestor = $OutputDirectory
while (-not (Test-Path -LiteralPath $ancestor)) { $ancestor = Split-Path -Parent $ancestor }
Assert-PayloadPath -Path $OutputDirectory -Root $ancestor
$files = @(Get-IsolatedSourceFiles -Root $RepoRoot)
$head = (& git -C $RepoRoot rev-parse HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read source HEAD' }
$env:FRAMESCOPE_BUILD_SHA = $head
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$snapshot = Join-Path $OutputDirectory 'source'
# --no-local avoids writable shared Git objects. No checkout touches dirty inputs;
# the private index is populated only after the exact current bytes are copied.
& git clone --quiet --no-local --no-checkout -- $RepoRoot $snapshot
if ($LASTEXITCODE -ne 0) { throw 'Cannot create private snapshot repository' }
foreach ($file in $files) {
    $source = Join-Path $RepoRoot $file.path
    $destination = Join-Path $snapshot $file.path
    Assert-PayloadPath -Path $destination -Root $snapshot
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $file.sha256) { throw "Source changed during copy: $($file.path)" }
}
# Only inventoried bytes were copied. Force keeps source already tracked by the
# original repository even when a newer ignore rule also matches that source.
& git -C $snapshot add --force -A -- .
if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize private snapshot index' }
[ordered]@{ schema = 1; sourceRoot = $RepoRoot; sourceHead = $head; snapshotRoot = $snapshot;
    generatedPaths = @('framescope-config.example.json', 'obj/FrameScopeBuildMetadata.g.cs'); files = $files } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'snapshot-inputs.json') -Encoding UTF8
Assert-IsolatedSourceUnchanged -Directory $OutputDirectory
foreach ($config in @(Get-ChildItem -LiteralPath $snapshot -Recurse -File | Where-Object { $_.Name -in @('config', 'config.toml', '.npmrc') -and $_.FullName -notmatch '[\\/]\.git[\\/]' })) {
    $text = Get-Content -LiteralPath $config.FullName -Raw
    if ($text -match '(?im)^\s*(target-dir|build-dir|prefix)\s*=') { throw "Output redirection is not allowed in isolated build config: $($config.FullName)" }
}
Assert-IsolatedCargoOutputs -Snapshot $snapshot
if ($SnapshotOnly) { Write-Host "Isolated snapshot verified: $snapshot"; return }
$cargoHome = $env:CARGO_HOME
if (-not $cargoHome) { $cargoHome = Join-Path $env:USERPROFILE '.cargo' }
foreach ($configName in @('config', 'config.toml')) {
    $configPath = Join-Path $cargoHome $configName
    if (Test-Path -LiteralPath $configPath -PathType Leaf) {
        if ((Get-Content -LiteralPath $configPath -Raw) -match '(?im)^\s*(target-dir|build-dir)\s*=') { throw "Inherited Cargo config redirects outputs: $configPath" }
    }
}
# The C# entry packages this retained frontend and requires a current dist.
Push-Location (Join-Path $snapshot 'src\frontend')
try {
    & npm ci
    if ($LASTEXITCODE -ne 0) { throw 'Snapshot retained-frontend npm ci failed' }
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Snapshot retained-frontend build failed' }
} finally { Pop-Location }
Push-Location (Join-Path $snapshot 'desktop-preview')
try {
    & npm ci
    if ($LASTEXITCODE -ne 0) { throw 'Snapshot npm ci failed' }
} finally { Pop-Location }
if ($PackageOnly) {
    & (Join-Path $snapshot 'tools\Package-Installer.ps1') -RepoRoot $snapshot -StageOnly:$StageOnly -CandidateHostTransformScript $CandidateHostTransformScript
} else {
    & (Join-Path $snapshot 'desktop-preview\installer\build-installer.ps1') -RepoRoot $snapshot -CandidateHostTransformScript $CandidateHostTransformScript
}
Assert-IsolatedSourceUnchanged -Directory $OutputDirectory
$outputs = @(Get-ChildItem -LiteralPath $snapshot -Recurse -File | Where-Object {
    $relative = $_.FullName.Substring($snapshot.Length + 1).Replace('\', '/')
    $relative -match '^(obj/|dist/|src/frontend/dist/|desktop-preview/dist/|desktop-preview/installer/dist/|desktop-preview/src-tauri/resources/)' -or
    $relative -match '^[^/]+\.(exe|dll)$' -or $relative -eq 'framescope-config.example.json' -or
    $relative -eq 'desktop-preview/installer/src-tauri/payload.zip' -or
    $relative -match '(^|/)target/release/[^/]+\.(exe|dll|json)$' -or
    $relative -match '^desktop-preview/(installer/)?src-tauri/target/release/bundle/'
} | ForEach-Object { [ordered]@{ path = $_.FullName.Substring($snapshot.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
$generatedOutputs = @(foreach ($path in @('framescope-config.example.json', 'obj/FrameScopeBuildMetadata.g.cs')) {
    $original = @($files | Where-Object { $_.path -eq $path } | Select-Object -First 1)
    [ordered]@{ path = $path; originalInputSha256 = $(if ($original.Count) { $original[0].sha256 } else { $null });
        outputSha256 = (Get-FileHash -LiteralPath (Join-Path $snapshot $path) -Algorithm SHA256).Hash }
})
[ordered]@{ schema = 1; sourceHead = $head; inputsVerified = $true; generatedOutputs = $generatedOutputs; outputs = $outputs } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'isolated-build-result.json') -Encoding UTF8
} finally {
    foreach ($name in $isolatedEnvironment.Keys) {
        if ($null -eq $isolatedEnvironment[$name]) { Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue }
        else { Set-Item -LiteralPath ('Env:' + $name) -Value $isolatedEnvironment[$name] }
    }
}
