# Packs the app payload (main exe + staged resources) into the installer's
# embedded payload.zip. Requires the main app to be built and staged first —
# run tools/Package-Installer.ps1 (or at least its staging section) before
# this script if src-tauri/target/release/FrameScope.exe is missing.
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path,
    [switch]$FunctionsOnly,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if ($OutputDirectory) {
    $RepoRoot = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'source'
    if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory 'snapshot-inputs.json') -PathType Leaf)) { throw 'Missing isolated snapshot provenance' }
    & (Join-Path $RepoRoot 'tools\Build-IsolatedInstaller.ps1') -RepoRoot $RepoRoot -OutputDirectory $OutputDirectory -VerifyOnly
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
function Get-PayloadInputs([string]$Root) {
    $pathspecs = 'build.ps1 tools packaging src assets VERSION dependencies.lock.json native-packages.lock.json PRIVACY.md THIRD-PARTY-NOTICES.md ' +
        'desktop-preview/src desktop-preview/public desktop-preview/package.json desktop-preview/package-lock.json desktop-preview/index.html ' +
        'desktop-preview/vite.config.ts desktop-preview/tsconfig* desktop-preview/LICENSE desktop-preview/LICENSE.EXTRA ' +
        'desktop-preview/src-tauri/src desktop-preview/src-tauri/vendor desktop-preview/src-tauri/capabilities desktop-preview/src-tauri/icons ' +
        'desktop-preview/src-tauri/Cargo.toml desktop-preview/src-tauri/Cargo.lock desktop-preview/src-tauri/build.rs desktop-preview/src-tauri/tauri.conf.json ' +
        'desktop-preview/installer/src desktop-preview/installer/public desktop-preview/installer/package.json desktop-preview/installer/package-lock.json ' +
        'desktop-preview/installer/index.html desktop-preview/installer/vite.config.ts desktop-preview/installer/tsconfig* desktop-preview/installer/src-tauri/src ' +
        'desktop-preview/installer/src-tauri/Cargo.toml desktop-preview/installer/src-tauri/Cargo.lock desktop-preview/installer/src-tauri/build.rs ' +
        'desktop-preview/installer/src-tauri/tauri.conf.json desktop-preview/installer/build-installer.ps1 desktop-preview/installer/pack-payload.ps1'
    $gitOutput = Get-PayloadGitOutput ('-C "' + $Root + '" -c core.quotepath=false ls-files --cached --others --exclude-standard -- ' + $pathspecs)
    $paths = @($gitOutput -split "`r?`n" | Where-Object { $_ })
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

function Get-PayloadFiles([string]$Root) {
    $resources = Join-Path $Root 'desktop-preview\src-tauri\resources'
    $main = Join-Path $Root 'desktop-preview\src-tauri\target\release\FrameScope.exe'
    $required = @('FrameScopeHardwareService.exe', 'FrameScopeHardwareProvider.exe', 'FrameScopeMonitor.exe',
        'FrameScopeProcessSampler.exe', 'FrameScopeSystemSampler.exe', 'FrameScopeReportGenerator.exe',
        'LICENSE', 'LICENSE.EXTRA', 'PRIVACY.md', 'THIRD-PARTY-NOTICES.md', 'README-FrameScopeMonitor.txt',
        'FrameScopeUninstaller.exe', 'FrameScopeHostCapabilities.json')
    $lock = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $Root 'dependencies.lock.json') | ConvertFrom-Json
    $required += @($lock.runtimeFiles.PSObject.Properties.Name)
    $required += 'tools\' + [IO.Path]::GetFileName([string]$lock.presentMon.file)
    foreach ($name in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $resources "backend\$name") -PathType Leaf)) { throw "Required payload file missing: $name" }
    }
    foreach ($name in @('fonts\MiSansVF.ttf', 'dependencies\PawnIO\PawnIO_setup.exe')) {
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
        foreach ($file in (Get-ChildItem -LiteralPath $dir -File -Recurse | Sort-Object FullName)) {
            $relative = $file.FullName.Substring($dir.Length + 1).Replace('\', '/')
            if ($file.Name -in @('FrameScopeInProcessOSD-D3D11.dll', 'FrameScopeInProcessOSD-D3D12.dll',
                'framescope-osd-inject.exe', 'THIRD-PARTY-NOTICES-hudhook.md')) {
                throw "Obsolete injected OSD artifact remains in staged resources; rebuild required: $relative"
            }
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
$isolatedDirectory = Split-Path -Parent $RepoRoot
$isolatedInventory = Join-Path $isolatedDirectory 'snapshot-inputs.json'
if (Test-Path -LiteralPath $isolatedInventory -PathType Leaf) {
    & (Join-Path $RepoRoot 'tools\Build-IsolatedInstaller.ps1') -RepoRoot $RepoRoot -OutputDirectory $isolatedDirectory -VerifyOnly
}
$receiptPath = Join-Path $RepoRoot 'desktop-preview\src-tauri\target\release\payload-build-receipt.json'
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
    throw 'Missing build receipt. Run tools/Package-Installer.ps1 -StageOnly before packing.'
}
$receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
$inputs = @(Get-PayloadInputs -Root $RepoRoot)
$files = @(Get-PayloadFiles -Root $RepoRoot)
if (($receipt.inputs | ConvertTo-Json -Depth 6 -Compress) -cne ($inputs | ConvertTo-Json -Depth 6 -Compress) -or
    ($receipt.files | ConvertTo-Json -Depth 6 -Compress) -cne ($files | ConvertTo-Json -Depth 6 -Compress)) {
    throw 'Payload inputs or outputs differ from the successful build receipt; rebuild required'
}
$outZip = Join-Path $RepoRoot 'desktop-preview\installer\src-tauri\payload.zip'
Assert-PayloadPath -Path $outZip -Root $RepoRoot
$stage = Join-Path ([IO.Path]::GetTempPath()) ('framescope-payload-' + [guid]::NewGuid().ToString('N'))
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
    if (Test-Path -LiteralPath $isolatedInventory -PathType Leaf) {
        & (Join-Path $RepoRoot 'tools\Build-IsolatedInstaller.ps1') -RepoRoot $RepoRoot -OutputDirectory $isolatedDirectory -VerifyOnly
    }
    Assert-PayloadPath -Path $zipTemp -Root $RepoRoot
    Move-Item -LiteralPath $zipTemp -Destination $outZip -Force
    Write-Host "Payload verified: $outZip SHA256=$((Get-FileHash -LiteralPath $outZip).Hash) files=$($files.Count)"
} finally {
    Assert-PayloadPath -Path $stage -Root ([IO.Path]::GetTempPath())
    Remove-Item -LiteralPath $stage -Recurse -Force
    Assert-PayloadPath -Path $zipTemp -Root $RepoRoot
    if (Test-Path -LiteralPath $zipTemp) { Remove-Item -LiteralPath $zipTemp -Force }
}
