# Packs the app payload (main exe + staged resources) into the installer's
# embedded payload.zip. Requires the main app to be built and staged first —
# run tools/Package-Installer.ps1 (or at least its staging section) before
# this script if src-tauri/target/release/FrameScope.exe is missing.
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path,
    [switch]$FunctionsOnly,
    [ValidateSet("Auto","Full","Cumulative")][string]$PackageMode = "Auto",
    [string]$OutputDirectory,
    [switch]$PreserveStaging
)
$ErrorActionPreference = 'Stop'
if ($OutputDirectory) {
    $RepoRoot = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'source'
    if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory 'snapshot-inputs.json') -PathType Leaf)) { throw 'Missing isolated snapshot provenance' }
    & (Join-Path $RepoRoot 'tools\Build-IsolatedInstaller.ps1') -RepoRoot $RepoRoot -OutputDirectory $OutputDirectory -VerifyOnly
}

function Resolve-FrameScopePackageMode([string]$Root, [string]$Mode) {
    if ($Mode -ne 'Auto') { return $Mode }
    $version = (Get-Content -LiteralPath (Join-Path $Root 'VERSION') -Raw).Trim()
    if ($version -match '^(\d+\.\d+\.\d+)-beta\.(\d+)$' -and
        ([version]$Matches[1] -gt [version]'2.1.3' -or ([version]$Matches[1] -eq [version]'2.1.3' -and [int]$Matches[2] -gt 14))) { return 'Cumulative' }
    return 'Full'
}

function Copy-VerifiedHost([string]$Source, [string]$Destination) {
    $hash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    # Tauri may make the branded name a hard link to Cargo's executable.
    if (-not (Test-Path -LiteralPath $Destination -PathType Leaf) -or
        (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $hash) {
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $hash) {
        throw 'Host copy hash mismatch'
    }
}

function Assert-PayloadPath([string]$Path, [string]$Root) {
    $full = [IO.Path]::GetFullPath($Path)
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $full.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw "Path outside root: $full" }
    $part = $full
    while ($part) {
        if (Test-Path -LiteralPath $part) {
            if ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Reparse path is not permitted: $part"
            }
        }
        $part = [IO.Path]::GetDirectoryName($part)
    }
}

function Move-PreservedBuildStaging([string]$Path, [string]$Root) {
    $source = [IO.Path]::GetFullPath($Path)
    Assert-PayloadPath -Path $source -Root $Root
    if (-not (Test-Path -LiteralPath $source)) { return }
    $archive = Join-Path $Root ('obj/build-staging/' + [guid]::NewGuid().ToString('N'))
    $destination = Join-Path $archive ([IO.Path]::GetFileName($source))
    Assert-PayloadPath -Path $destination -Root $Root
    if ($destination.StartsWith($source.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Cannot preserve a build directory inside itself'
    }
    $pending = [Collections.Generic.Stack[string]]::new()
    if (Test-Path -LiteralPath $source -PathType Container) { $pending.Push($source) }
    while ($pending.Count) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse path is not permitted: $($item.FullName)" }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
    New-Item -ItemType Directory -Path $archive | Out-Null
    Move-Item -LiteralPath $source -Destination $destination
    Write-Host "Retained build staging: $destination"
    return $destination
}

function Get-PayloadGitOutput([string]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = 'git'
    $info.Arguments = $Arguments
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $info.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $process = [Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill()
            throw 'Git input enumeration exceeded 30 seconds'
        }
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 5000)) {
            throw 'Git input enumeration output exceeded 5 seconds'
        }
        if ($process.ExitCode -ne 0) { throw "Git input enumeration failed: $($stderr.Result)" }
        $stdout.Result
    } finally { $process.Dispose() }
}
function Get-SourceExportManifest([string]$Root) {
    $manifestPath=Join-Path $Root 'SOURCE_MANIFEST.json'
    Assert-PayloadPath -Path $manifestPath -Root $Root
    $export=Get-Content -Raw -LiteralPath $manifestPath -Encoding UTF8 | ConvertFrom-Json
    if($export.schema -ne 1 -or $export.sourceHead -notmatch '^[a-f0-9]{40}$' -or -not @($export.files).Count){throw 'Invalid source export identity'}
    $generatedConfig=$false
    $inventoryPath=Join-Path (Split-Path -Parent $Root) 'snapshot-inputs.json'
    if(Test-Path -LiteralPath $inventoryPath -PathType Leaf){
        $inventory=Get-Content -Raw -LiteralPath $inventoryPath -Encoding UTF8 | ConvertFrom-Json
        $manifestEntry=@($inventory.files | Where-Object {$_.path -ceq 'SOURCE_MANIFEST.json'})
        $generatedConfig=([IO.Path]::GetFullPath([string]$inventory.snapshotRoot).TrimEnd('\') -eq [IO.Path]::GetFullPath($Root).TrimEnd('\') -and
            $inventory.sourceRoot -ne $inventory.snapshotRoot -and $inventory.generatedPaths -contains 'framescope-config.example.json' -and
            $manifestEntry.Count -eq 1 -and $manifestEntry[0].sha256 -eq (Get-FileHash -LiteralPath $manifestPath).Hash)
    }
    $seen=@{};$table=[Text.StringBuilder]::new()
    foreach($entry in $export.files){
        $name=[string]$entry.path
        if(-not $name -or $name -match '(^/|\\|:|[\x00-\x1f]|(^|/)\.\.?(/|$)|//|[ .](/|$))' -or $name -ieq 'SOURCE_MANIFEST.json'){throw "Unsafe export path: $name"}
        if($seen.ContainsKey($name)){throw "Duplicate export path: $name"}
        $seen[$name]=$true
        $path=Join-Path $Root $name
        Assert-PayloadPath -Path $path -Root $Root
        if($entry.sha256 -notmatch '^[a-f0-9]{64}$'){throw "Invalid export hash: $name"}
        if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw "Export changed: $name"}
        if(-not ($generatedConfig -and $name -ceq 'framescope-config.example.json')){
            if((Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256 -or
                ($null -ne $entry.length -and (Get-Item -LiteralPath $path).Length -ne $entry.length)){throw "Export changed: $name"}
        }
        if($export.sourceSnapshotSha256){
            if([string]$entry.length -notmatch '^\d+$'){throw "Missing export length: $name"}
            [void]$table.Append(([string]$entry.sha256).ToUpperInvariant()).Append("`t").Append([string]$entry.length).Append("`t").Append($name).Append("`n")
        }
    }
    if($export.sourceSnapshotSha256){
        if($export.sourceSnapshotFormat -cne 'sha256-length-path-v1'){throw 'Unsupported source snapshot fingerprint'}
        $sha=[Security.Cryptography.SHA256]::Create()
        try{$fingerprint=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($table.ToString()))).Replace('-','')}finally{$sha.Dispose()}
        if($fingerprint -ne $export.sourceSnapshotSha256){throw 'Source snapshot fingerprint mismatch'}
    }
    # SDK projects and CMake may discover sources by glob. A manifest alone
    # must not let an unlisted file become a new compilation input.

    $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\')
    $pending=[Collections.Generic.Stack[string]]::new()
    $pending.Push($rootPath)
    while($pending.Count){
        foreach($item in Get-ChildItem -LiteralPath $pending.Pop() -Force){
            $name=$item.FullName.Substring($rootPath.Length+1).Replace('\','/')
            if($name -match '(^|/)(node_modules|target|dist|bin|obj|obj-native|publish|publish-original|coverage|__pycache__|\.cache|\.vite)(/|$)' -or
               $name -match '^desktop-preview/src-tauri/resources(/|$)' -or
               $name -match '^src/gamepp-original/(build|ffmpeg-build)(/|$)' -or
               $name -match '^desktop-preview/(report-dist|src-tauri/gen/schemas)(/|$)'){continue}
            Assert-PayloadPath -Path $item.FullName -Root $Root
            if($item.PSIsContainer){$pending.Push($item.FullName);continue}

            if($name -ceq 'SOURCE_MANIFEST.json' -or $seen.ContainsKey($name)){continue}
            if($name -match '^desktop-preview/public/licenses/(ffmpeg-core|ffmpeg-wasm|fluent-emoji-animated|fluent-emoji|unicode)-license\.txt$'){continue}
            if($name -match '\.(exe|dll|pdb|zip|log|pyc|tsbuildinfo)$' -or
               $name -eq 'src/game-filter/sample.generated.cpp'){continue}
            throw "Unlisted export input: $name"
        }
    }
    return $export
}

function Get-PinnedPawnIoInput([string]$Root) {
    $lockPath=Join-Path $Root 'build-dependencies.lock.json'
    Assert-PayloadPath -Path $lockPath -Root $Root
    $lock=Get-Content -Raw -LiteralPath $lockPath -Encoding UTF8 | ConvertFrom-Json
    $entries=@($lock.files | Where-Object {$_.file -ceq 'packaging/PawnIO_setup.exe'})
    if($lock.schema -ne 1 -or $entries.Count -ne 1){throw 'Missing or duplicate PawnIO dependency lock'}
    $name='packaging/PawnIO_setup.exe'
    if(-not (Test-Path -LiteralPath (Join-Path $Root $name) -PathType Leaf)){$name='reference/nexbox/src-tauri/resources/binaries/PawnIO_setup.exe'}
    $path=Join-Path $Root $name
    Assert-PayloadPath -Path $path -Root $Root
    $hash=(Get-FileHash -LiteralPath $path).Hash
    if($hash -ne $entries[0].sha256){throw 'Pinned dependency hash mismatch: PawnIO'}
    [ordered]@{path=$name;sha256=$hash}
}

function Get-PayloadInputs([string]$Root, [string]$Mode = "Full") {
    $pathspecs = 'build.ps1 tools packaging src assets VERSION EDITION dependencies.lock.json build-dependencies.lock.json native-packages.lock.json PRIVACY.md THIRD-PARTY-NOTICES.md ' +
        'desktop-preview/src desktop-preview/public desktop-preview/package.json desktop-preview/package-lock.json desktop-preview/index.html ' +
        'desktop-preview/vite.config.ts desktop-preview/scripts/build-report.mjs desktop-preview/tsconfig* desktop-preview/LICENSE desktop-preview/LICENSE.EXTRA ' +
        'desktop-preview/src-tauri/src desktop-preview/src-tauri/vendor desktop-preview/src-tauri/capabilities desktop-preview/src-tauri/icons ' +
        'desktop-preview/src-tauri/Cargo.toml desktop-preview/src-tauri/Cargo.lock desktop-preview/src-tauri/build.rs desktop-preview/src-tauri/tauri.conf.json ' +
        'desktop-preview/installer/src desktop-preview/installer/public desktop-preview/installer/package.json desktop-preview/installer/package-lock.json ' +
        'desktop-preview/installer/index.html desktop-preview/installer/vite.config.ts desktop-preview/installer/tsconfig* desktop-preview/installer/src-tauri/src ' +
        'desktop-preview/installer/src-tauri/Cargo.toml desktop-preview/installer/src-tauri/Cargo.lock desktop-preview/installer/src-tauri/build.rs ' +
        'desktop-preview/installer/src-tauri/tauri.conf.json desktop-preview/installer/build-installer.ps1 desktop-preview/installer/pack-payload.ps1'
    if(-not (Test-Path -LiteralPath (Join-Path $Root '.git'))){
        $export=Get-SourceExportManifest -Root $Root
        $paths=@($export.files.path)+@('SOURCE_MANIFEST.json')
    } else {
        $gitOutput = Get-PayloadGitOutput ('-C "' + $Root + '" -c core.quotepath=false ls-files --cached --others --exclude-standard -- ' + $pathspecs)
        $paths = @($gitOutput -split "`r?`n" | Where-Object { $_ -and $_ -notmatch '^src/(game-filter|amd-tuning)/vendor/' -and $_ -notmatch '^src/(game-filter|amd-tuning|feature-worker)/(publish|publish-original|obj-native)/' -and $_ -ne 'src/game-filter/sample.generated.cpp' })
    }
    $fullEdition = (Get-Content -LiteralPath (Join-Path $Root 'EDITION') -Raw).Trim() -eq 'full'
    $dependencyLock = Get-Content -LiteralPath (Join-Path $Root 'dependencies.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($Mode -eq 'Full') {
        $paths += @($dependencyLock.webView2StandaloneInstaller.file, $dependencyLock.dotNet48OfflineInstaller.file)
        $paths += (Get-PinnedPawnIoInput -Root $Root).path
    } else {
        $excluded = @($dependencyLock.webView2StandaloneInstaller.file, $dependencyLock.dotNet48OfflineInstaller.file, 'packaging/PawnIO_setup.exe')
        $paths = @($paths | Where-Object { $_ -notmatch '^packaging/toolbox/files/' -and $_ -notin $excluded })
    }

    if (-not $paths.Count) { throw 'Cannot enumerate source inputs for payload provenance' }
    foreach ($path in ($paths | Sort-Object -Unique)) {
        $full = Join-Path $Root $path
        Assert-PayloadPath -Path $full -Root $Root
        [ordered]@{ path = $path; sha256 = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash }
    }
}

function Write-HostCapabilities([string]$Root) {
    $main = Join-Path $Root 'desktop-preview\src-tauri\target\release\FrameScope.exe'
    $marker = Join-Path $Root 'desktop-preview\src-tauri\resources\backend\FrameScopeHostCapabilities.json'
    Assert-PayloadPath -Path $main -Root $Root
    Assert-PayloadPath -Path $marker -Root $Root
    [ordered]@{ schema = 1; requestExitProtocol = 1; hostSha256 = (Get-FileHash -LiteralPath $main -Algorithm SHA256).Hash } |
        ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
}

function Invoke-CandidateHostTransform([string]$Root, [string]$Script) {
    $directory = Split-Path -Parent $Root
    $inventory = Join-Path $directory 'snapshot-inputs.json'
    if (-not (Test-Path -LiteralPath $inventory -PathType Leaf)) { throw 'Host transforms require a private isolated snapshot' }
    $provenance = Get-Content -LiteralPath $inventory -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([IO.Path]::GetFullPath($Root) -ne [IO.Path]::GetFullPath($provenance.snapshotRoot)) { throw 'Host transform root does not match snapshot provenance' }
    $Script = (Resolve-Path -LiteralPath $Script).Path
    $scriptHash = (Get-FileHash -LiteralPath $Script -Algorithm SHA256).Hash
    $inputs = @(Get-PayloadInputs -Root $Root)
    $snapshotInputs = @(foreach ($file in $provenance.files) {
        $path = Join-Path $Root $file.path
        Assert-PayloadPath -Path $path -Root $Root
        [ordered]@{ path = $file.path; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    })
    Write-HostCapabilities -Root $Root
    $files = @(Get-PayloadFiles -Root $Root)
    $hostPath = Join-Path $Root 'desktop-preview\src-tauri\target\release\FrameScope.exe'
    Assert-PayloadPath -Path $hostPath -Root $Root
    $before = (Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).Hash
    $previousNativeExit = $global:LASTEXITCODE
    try {
        $global:LASTEXITCODE = 0
        $transformOutput = @(& $Script -HostExecutable $hostPath)
        $transformSucceeded = $?
        $transformExitCode = $global:LASTEXITCODE
        if (-not $transformSucceeded -or $transformExitCode -ne 0) { throw 'Candidate host transform failed' }
    } finally { $global:LASTEXITCODE = $previousNativeExit }
    Assert-PayloadPath -Path $hostPath -Root $Root
    if ((Get-FileHash -LiteralPath $Script -Algorithm SHA256).Hash -ne $scriptHash) { throw 'Host transform script changed during execution' }
    if (($inputs | ConvertTo-Json -Depth 6 -Compress) -cne (@(Get-PayloadInputs -Root $Root) | ConvertTo-Json -Depth 6 -Compress)) { throw 'Host transform changed source inputs' }
    foreach ($file in $snapshotInputs) {
        $path = Join-Path $Root $file.path
        Assert-PayloadPath -Path $path -Root $Root
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Host transform changed source inputs: $($file.path)" }
    }
    foreach ($file in $files) {
        if ($file.path -eq 'FrameScope.exe') { continue }
        $path = Join-Path $Root $file.source
        Assert-PayloadPath -Path $path -Root $Root
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Host transform changed non-host payload: $($file.path)" }
    }
    # Validate the complete set too, after the one permitted host-byte change.
    Write-HostCapabilities -Root $Root
    $afterFiles = @(Get-PayloadFiles -Root $Root)
    if (($files.path | ConvertTo-Json -Compress) -cne ($afterFiles.path | ConvertTo-Json -Compress)) { throw 'Host transform changed payload file set' }
    & (Join-Path $Root 'tools\Build-IsolatedInstaller.ps1') -RepoRoot $Root -OutputDirectory $directory -VerifyOnly
    [ordered]@{ script = $Script; scriptSha256 = $scriptHash; cargoArtifactSha256 = $before;
        hostBeforeSha256 = $before; hostAfterSha256 = (Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).Hash }
}

function Get-PayloadFiles([string]$Root, [string]$Mode = "") {
    $resources = Join-Path $Root 'desktop-preview\src-tauri\resources'
    if (-not $Mode) {
        $kindPath = Join-Path $resources 'backend/FrameScopePackage.json'
        $Mode = if (Test-Path -LiteralPath $kindPath) { (Get-Content -LiteralPath $kindPath -Raw | ConvertFrom-Json).kind } else { 'Full' }
    }
    $main = Join-Path $Root 'desktop-preview\src-tauri\target\release\FrameScope.exe'
    $required = @('FrameScopeHardwareService.exe', 'FrameScopeHardwareProvider.exe', 'FrameScopeMonitor.exe',
        'FrameScopeProcessSampler.exe', 'FrameScopeSystemSampler.exe', 'FrameScopeReportGenerator.exe',
        'LICENSE', 'LICENSE.EXTRA', 'PRIVACY.md', 'THIRD-PARTY-NOTICES.md', 'README-FrameScopeMonitor.txt',
        'FrameScopeUninstaller.exe', 'FrameScopeHostCapabilities.json', 'FrameScopeBuildInfo.json', 'Restore-FrameScope.cmd')
    $lock = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $Root 'dependencies.lock.json') | ConvertFrom-Json
    $required += @($lock.runtimeFiles.PSObject.Properties.Name)
    $required += 'tools\' + [IO.Path]::GetFileName([string]$lock.presentMon.file)
    $fullEdition = (Get-Content -LiteralPath (Join-Path $Root 'EDITION') -Raw).Trim() -eq 'full'


    foreach ($name in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $resources "backend\$name") -PathType Leaf)) { throw "Required payload file missing: $name" }
    }
    $requiredResources = @('fonts/MiSansVF.ttf')
    if ($Mode -eq 'Full') { $requiredResources += @('dependencies/PawnIO/PawnIO_setup.exe', 'dependencies/Offline/WebView2.exe', 'dependencies/Offline/DotNet48.exe') }
    foreach ($name in $requiredResources) {
        if (-not (Test-Path -LiteralPath (Join-Path $resources $name) -PathType Leaf)) { throw "Required payload file missing: $name" }
    }

    Assert-PayloadPath -Path $main -Root $Root
    $markerPath = Join-Path $resources 'backend\FrameScopeHostCapabilities.json'
    Assert-PayloadPath -Path $markerPath -Root $Root
    $capabilities = Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($capabilities.schema -ne 1 -or $capabilities.requestExitProtocol -ne 1 -or
        [string]$capabilities.hostSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
        $capabilities.hostSha256 -ne (Get-FileHash -LiteralPath $main -Algorithm SHA256).Hash) {
        throw 'Host capability marker does not match the candidate host; rebuild required'
    }
    $sources = @([pscustomobject]@{ source = $main; path = 'FrameScope.exe' })
    foreach ($mapping in @(@('backend', ''), @('fonts', 'Fonts/'), @('dependencies', 'dependencies/'))) {
        $dir = Join-Path $resources $mapping[0]
        Assert-PayloadPath -Path $dir -Root $Root
        if ($Mode -eq 'Cumulative' -and $mapping[0] -eq 'dependencies' -and -not (Test-Path -LiteralPath $dir)) { continue }
        foreach ($file in (Get-ChildItem -LiteralPath $dir -File -Recurse | Sort-Object FullName)) {
            $relative = $file.FullName.Substring($dir.Length + 1).Replace('\', '/')
            if (-not $fullEdition -and $relative -match '^(Ai/|toolbox/|tools/(feature-suite/|FrameScope(Feature|Ray|Amd|Filter|Original)|FrameScope\.Original|original-shade))') {
                throw "Closed feature payload is forbidden in the offline edition: $relative"
            }
            if ($mapping[0] -eq 'backend' -and ($relative -eq 'tools/feature-suite/3dmark-profiles.json' -or
                $relative.StartsWith('tools/filter/', [StringComparison]::OrdinalIgnoreCase))) {
                throw "Retired benchmark or filter runtime remains in staged resources; rebuild required: $relative"
            }
            if ($file.Name -in @('FrameScopeInProcessOSD-D3D11.dll', 'FrameScopeInProcessOSD-D3D12.dll',
                'framescope-osd-inject.exe', 'THIRD-PARTY-NOTICES-hudhook.md')) {
                throw "Obsolete injected OSD artifact remains in staged resources; rebuild required: $relative"
            }
            if ($Mode -eq 'Cumulative' -and ($mapping[0] -eq 'dependencies' -or $relative.StartsWith('toolbox/files/'))) { throw 'Cumulative staging contains reusable component binaries' }
            $sources += [pscustomobject]@{ source = $file.FullName; path = $mapping[1] + $relative }
        }
    }
    $names = @{}
    foreach ($entry in ($sources | Sort-Object path)) {
        Assert-PayloadPath -Path $entry.source -Root $Root
        if ($names.ContainsKey($entry.path)) { throw "Duplicate payload destination: $($entry.path)" }
        $names[$entry.path] = $true
        [ordered]@{
            source = $entry.source.Substring([IO.Path]::GetFullPath($Root).TrimEnd('\').Length + 1).Replace('\', '/')
            path = $entry.path
            length = (Get-Item -LiteralPath $entry.source).Length
            sha256 = (Get-FileHash -LiteralPath $entry.source -Algorithm SHA256).Hash
        }
    }
}

if ($FunctionsOnly) { return }
& (Join-Path $RepoRoot 'tools\Verify-IsolatedBuildStage.ps1') -RepoRoot $RepoRoot
$receiptPath = Join-Path $RepoRoot 'desktop-preview\src-tauri\target\release\payload-build-receipt.json'
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
    throw 'Missing build receipt. Run tools/Package-Installer.ps1 -StageOnly before packing.'
}
$receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
$PackageMode = Resolve-FrameScopePackageMode -Root $RepoRoot -Mode $PackageMode
$stagedMode = if ($receipt.packageMode) { $receipt.packageMode } else { 'Full' }
if ($PackageMode -eq 'Full' -and $stagedMode -ne 'Full') { throw 'Full package requires full staging' }
$inputs = @(Get-PayloadInputs -Root $RepoRoot -Mode $stagedMode)
$files = @(Get-PayloadFiles -Root $RepoRoot -Mode $stagedMode)
if (($receipt.inputs | ConvertTo-Json -Depth 6 -Compress) -cne ($inputs | ConvertTo-Json -Depth 6 -Compress) -or
    ($receipt.files | ConvertTo-Json -Depth 6 -Compress) -cne ($files | ConvertTo-Json -Depth 6 -Compress)) {
    throw 'Payload inputs or outputs differ from the successful build receipt; rebuild required'
}
if ($PackageMode -eq 'Cumulative') {
    if (-not @($files | Where-Object {$_.path -ceq 'FrameScopeComponents.json'}).Count) { throw 'Cumulative package requires a component inventory' }
    $files = @($files | Where-Object {$_.path -notmatch '^(toolbox/files/|dependencies/)'})
}
$outZip = Join-Path $RepoRoot 'desktop-preview\installer\src-tauri\payload.zip'
Assert-PayloadPath -Path $outZip -Root $RepoRoot
$stageRoot = if ($PreserveStaging) {
    Join-Path $RepoRoot 'desktop-preview/installer/src-tauri/target/payload-staging'
} else { [IO.Path]::GetTempPath() }
$stage = Join-Path $stageRoot ('framescope-payload-' + [guid]::NewGuid().ToString('N'))
Assert-PayloadPath -Path $stage -Root $stageRoot
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stage | Out-Null
$zipTemp = "$outZip.$([guid]::NewGuid().ToString('N')).zip"
try {
    foreach ($file in $files) {
        $dest = Join-Path $stage $file.path
        Assert-PayloadPath -Path $dest -Root $stage
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $RepoRoot $file.source) -Destination $dest
        if ((Get-FileHash -LiteralPath $dest).Hash -ne $file.sha256) { throw "Payload changed during copy: $($file.path)" }
    }
    $kindPath = Join-Path $stage 'FrameScopePackage.json'
    [IO.File]::WriteAllText($kindPath, ([ordered]@{schema=1;kind=$PackageMode.ToLowerInvariant()} | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    $files = @($files | Where-Object {$_.path -cne 'FrameScopePackage.json'}) + @([ordered]@{
        source='generated-package-kind';path='FrameScopePackage.json';length=(Get-Item -LiteralPath $kindPath).Length;sha256=(Get-FileHash -LiteralPath $kindPath).Hash
    })
    $manifest = [ordered]@{ schema = 1; inputs = $inputs; files = $files }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'FrameScopePayloadManifest.json') -Encoding UTF8
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipTemp -CompressionLevel Optimal
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($zipTemp)
    try {
        foreach ($file in $files) {
            $entry = @($zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq $file.path })
            if ($entry.Count -ne 1 -or $entry[0].Length -ne $file.length) { throw "ZIP entry mismatch: $($file.path)" }
            $stream = $entry[0].Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $sha.Dispose(); $stream.Dispose() }
            if ($hash -ne $file.sha256) { throw "ZIP hash mismatch: $($file.path)" }
        }
    } finally { $zip.Dispose() }
    & (Join-Path $RepoRoot 'tools\Verify-IsolatedBuildStage.ps1') -RepoRoot $RepoRoot
    Assert-PayloadPath -Path $zipTemp -Root $RepoRoot
    $previousZip = Join-Path $stage 'previous-payload.zip'
    if ($PreserveStaging -and (Test-Path -LiteralPath $outZip)) {
        Assert-PayloadPath -Path $outZip -Root $RepoRoot
        Assert-PayloadPath -Path $previousZip -Root $stage
        Move-Item -LiteralPath $outZip -Destination $previousZip
    }
    try { Move-Item -LiteralPath $zipTemp -Destination $outZip -Force }
    catch {
        if ($PreserveStaging -and -not (Test-Path -LiteralPath $outZip) -and (Test-Path -LiteralPath $previousZip)) {
            Move-Item -LiteralPath $previousZip -Destination $outZip
        }
        throw
    }
    Write-Host "Payload verified: $outZip SHA256=$((Get-FileHash -LiteralPath $outZip).Hash) files=$($files.Count)"
} finally {
    if ($PreserveStaging) {
        Write-Host "Retained payload staging: $stage"
        if (Test-Path -LiteralPath $zipTemp) { Write-Host "Retained incomplete archive: $zipTemp" }
    } else {
        Assert-PayloadPath -Path $stage -Root $stageRoot
        Remove-Item -LiteralPath $stage -Recurse -Force
        Assert-PayloadPath -Path $zipTemp -Root $RepoRoot
        if (Test-Path -LiteralPath $zipTemp) { Remove-Item -LiteralPath $zipTemp -Force }
    }
}
