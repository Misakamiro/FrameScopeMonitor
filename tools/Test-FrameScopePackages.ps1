param(
    [string]$RepoRoot = '',
    [string]$DistRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$root = (Resolve-Path -LiteralPath $RepoRoot).Path
if ([string]::IsNullOrWhiteSpace($DistRoot)) {
    $DistRoot = Join-Path $root 'dist'
}
$dist = [IO.Path]::GetFullPath($DistRoot)
$payloadRoot = Join-Path $dist 'FrameScopeMonitor-payload'
$version = (Get-Content -Raw -LiteralPath (Join-Path $root 'VERSION')).Trim()
$required = @(
    'FrameScopeMonitor-Setup.exe',
    'FrameScopeMonitor-Full-Setup.exe',
    'FrameScopeMonitor-Installer.zip',
    'FrameScopeMonitor-LegacyCleanup.exe',
    'FrameScopeMonitor-payload/FrameScopeBuildManifest.json'
)

foreach ($relativePath in $required) {
    $path = Join-Path $dist ($relativePath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing package: $relativePath"
    }
}

function Assert-InstallerPathGuards {
    param([string]$AssemblyPath)

    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($AssemblyPath))
    $type = $assembly.GetType('FrameScopeSetupNative', $true)
    $flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
    $normalize = $type.GetMethod('NormalizePayloadArchivePath', $flags)
    $safeCombine = $type.GetMethod('SafeCombine', $flags)
    if ($null -eq $normalize -or $null -eq $safeCombine) { throw 'Installer path guard methods were not found.' }
    $normalizePath = [Delegate]::CreateDelegate([Func[string, string]], $normalize)
    $combinePath = [Delegate]::CreateDelegate([Func[string, string, string]], $safeCombine)

    Assert-Equal 'dir/file.bin' ([string]$normalizePath.Invoke('dir/file.bin')) 'installer normalized path'
    foreach ($invalid in @('../file.bin', 'dir/../file.bin', 'dir/./file.bin', '/file.bin', 'C:/file.bin', 'dir//file.bin')) {
        $rejected = $false
        try { $null = $normalizePath.Invoke([string]$invalid) }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Installer accepted unsafe payload path: $invalid" }
    }

    $guardRoot = Join-Path $env:TEMP ('framescope-safe-root-' + [guid]::NewGuid().ToString('N'))
    $valid = [string]$combinePath.Invoke($guardRoot, 'dir\file.bin')
    $rootPrefix = [IO.Path]::GetFullPath($guardRoot).TrimEnd('\') + '\'
    if (-not $valid.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer SafeCombine rejected its own root: $valid"
    }
    $escaped = $false
    try { $null = $combinePath.Invoke($guardRoot, ('..\' + (Split-Path -Leaf $guardRoot) + '-sibling\file.bin')) }
    catch { $escaped = $true }
    if (-not $escaped) { throw 'Installer SafeCombine accepted a prefix-sibling escape.' }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-Sha256Bytes {
    param([byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$Path
    )

    $base = [IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $baseUri = [Uri]$base
    $pathUri = [Uri]([IO.Path]::GetFullPath($Path))
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
}

function Get-FileHashMap {
    param([string]$Directory)

    $map = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File) {
        $relative = Get-RelativePath -BasePath $Directory -Path $file.FullName
        if ($map.ContainsKey($relative)) { throw "Duplicate package path: $relative" }
        $map[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    }
    return $map
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -cne $Actual) {
        throw "$Message expected=$Expected actual=$Actual"
    }
}

function Assert-HashMapsEqual {
    param(
        [hashtable]$Expected,
        [hashtable]$Actual,
        [string]$Label
    )

    Assert-Equal $Expected.Count $Actual.Count "$Label file count"
    foreach ($path in $Expected.Keys) {
        if (-not $Actual.ContainsKey($path)) { throw "$Label missing file: $path" }
        Assert-Equal $Expected[$path] $Actual[$path] "$Label hash mismatch: $path"
    }
}

function Expand-ZipSafe {
    param(
        [string]$ZipPath,
        [string]$Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $destinationRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $seenEntries = @{}
        foreach ($entry in $archive.Entries) {
            $entryPath = $entry.FullName.Replace('\', '/')
            if ($entryPath.StartsWith('/', [StringComparison]::Ordinal) -or $entryPath -match '(^|/)\.{1,2}(/|$)') {
                throw "Unsafe ZIP entry path: $entryPath"
            }
            $relative = $entry.FullName.Replace('/', '\')
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $relative))
            if (-not $target.StartsWith($destinationRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Unsafe ZIP entry: $($entry.FullName)"
            }
            $normalizedTarget = $target.Substring($destinationRoot.Length).Replace('\', '/')
            if ($seenEntries.ContainsKey($normalizedTarget)) { throw "Duplicate ZIP target: $entryPath" }
            $seenEntries[$normalizedTarget] = $true
            if ([string]::IsNullOrEmpty($entry.Name)) {
                New-Item -ItemType Directory -Path $target -Force | Out-Null
                continue
            }
            $parent = Split-Path -Parent $target
            if (-not [string]::IsNullOrWhiteSpace($parent)) {
                New-Item -ItemType Directory -Path $parent -Force | Out-Null
            }
            $source = $entry.Open()
            try {
                $destinationStream = [IO.File]::Open($target, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $source.CopyTo($destinationStream) }
                finally { $destinationStream.Dispose() }
            }
            finally {
                $source.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Expand-PayloadResource {
    param(
        [string]$AssemblyPath,
        [string]$Destination
    )

    $assemblyBytes = [IO.File]::ReadAllBytes($AssemblyPath)
    $assembly = [Reflection.Assembly]::Load($assemblyBytes)
    $stream = $assembly.GetManifestResourceStream('FrameScopePayload')
    if ($null -eq $stream) { throw "FrameScopePayload resource missing: $AssemblyPath" }
    try {
        $memory = New-Object IO.MemoryStream
        try {
            $stream.CopyTo($memory)
            $payloadBytes = $memory.ToArray()
        }
        finally {
            $memory.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    $zipPath = Join-Path (Split-Path -Parent $Destination) ((Split-Path -Leaf $Destination) + '.zip')
    [IO.File]::WriteAllBytes($zipPath, $payloadBytes)
    Expand-ZipSafe -ZipPath $zipPath -Destination $Destination

    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($AssemblyPath).FileVersion
    if (-not $fileVersion.StartsWith($version + '.', [StringComparison]::Ordinal)) {
        throw "Assembly version mismatch: $AssemblyPath version=$fileVersion expected=$version"
    }

    return [pscustomobject]@{
        PayloadSha256 = Get-Sha256Bytes -Bytes $payloadBytes
        FileVersion = $fileVersion
    }
}

function Assert-Manifest {
    param([string]$Directory)

    $manifestPath = Join-Path $Directory 'FrameScopeBuildManifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    Assert-Equal 'FrameScope Monitor' ([string]$manifest.product) 'manifest product'
    Assert-Equal $version ([string]$manifest.version) 'manifest version'
    if ([string]$manifest.buildId -notmatch '^[0-9a-fA-F]{32}$') {
        throw "Invalid manifest buildId: $($manifest.buildId)"
    }

    $locked = Get-Content -Raw -LiteralPath (Join-Path $root 'dependencies.lock.json') | ConvertFrom-Json
    $lockedJson = $locked | ConvertTo-Json -Depth 20 -Compress
    $manifestDependenciesJson = $manifest.dependencies | ConvertTo-Json -Depth 20 -Compress
    Assert-Equal $lockedJson $manifestDependenciesJson 'manifest dependency lock'

    $declared = @{}
    foreach ($entry in @($manifest.files)) {
        $path = ([string]$entry.path).Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($path) -or $path.StartsWith('/') -or $path.Contains('../')) {
            throw "Invalid manifest file path: $path"
        }
        if ($declared.ContainsKey($path)) { throw "Duplicate manifest file path: $path" }
        $filePath = Join-Path $Directory ($path -replace '/', '\')
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) { throw "Manifest file missing: $path" }
        Assert-Equal ([long]$entry.length) (Get-Item -LiteralPath $filePath).Length "Manifest length mismatch: $path"
        $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToUpperInvariant()
        Assert-Equal ([string]$entry.sha256).ToUpperInvariant() $actualHash "Manifest hash mismatch: $path"
        $declared[$path] = $true
    }

    $actualFiles = @(Get-ChildItem -LiteralPath $Directory -Recurse -File | Where-Object {
            (Get-RelativePath -BasePath $Directory -Path $_.FullName) -cne 'FrameScopeBuildManifest.json'
        })
    Assert-Equal $declared.Count $actualFiles.Count "manifest payload file count: $Directory"
    foreach ($file in $actualFiles) {
        $relative = Get-RelativePath -BasePath $Directory -Path $file.FullName
        if (-not $declared.ContainsKey($relative)) { throw "Payload file absent from manifest: $relative" }
    }
    return $manifest
}

$temp = Join-Path $env:TEMP ('framescope-package-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
    $setupPath = Join-Path $dist 'FrameScopeMonitor-Setup.exe'
    $fullSetupPath = Join-Path $dist 'FrameScopeMonitor-Full-Setup.exe'
    $setupPayload = Join-Path $temp 'setup-payload'
    $fullPayload = Join-Path $temp 'full-payload'
    $setupInfo = Expand-PayloadResource -AssemblyPath $setupPath -Destination $setupPayload
    $fullInfo = Expand-PayloadResource -AssemblyPath $fullSetupPath -Destination $fullPayload
    Assert-InstallerPathGuards -AssemblyPath $setupPath

    $payloadManifest = Assert-Manifest -Directory $payloadRoot
    $setupManifest = Assert-Manifest -Directory $setupPayload
    $fullManifest = Assert-Manifest -Directory $fullPayload
    Assert-Equal ([string]$payloadManifest.buildId) ([string]$setupManifest.buildId) 'Setup buildId'
    Assert-Equal ([string]$payloadManifest.buildId) ([string]$fullManifest.buildId) 'Full Setup buildId'
    Assert-Equal $setupInfo.PayloadSha256 $fullInfo.PayloadSha256 'embedded payload SHA256'

    $expectedMap = Get-FileHashMap -Directory $payloadRoot
    Assert-HashMapsEqual -Expected $expectedMap -Actual (Get-FileHashMap -Directory $setupPayload) -Label 'Setup payload'
    Assert-HashMapsEqual -Expected $expectedMap -Actual (Get-FileHashMap -Directory $fullPayload) -Label 'Full Setup payload'

    $releaseRoot = Join-Path $temp 'release-zip'
    Expand-ZipSafe -ZipPath (Join-Path $dist 'FrameScopeMonitor-Installer.zip') -Destination $releaseRoot
    $releaseFiles = @(Get-ChildItem -LiteralPath $releaseRoot -Recurse -File)
    Assert-Equal 4 $releaseFiles.Count 'release ZIP file count'
    $requiredReleaseNames = @('FrameScopeMonitor-Setup.exe', 'FrameScopeMonitor-Full-Setup.exe', 'FrameScopeMonitor-LegacyCleanup.exe', 'README-FrameScopeMonitor.txt')
    $actualReleaseNames = @($releaseFiles | ForEach-Object { Get-RelativePath -BasePath $releaseRoot -Path $_.FullName } | Sort-Object)
    Assert-Equal (($requiredReleaseNames | Sort-Object) -join '|') ($actualReleaseNames -join '|') 'release ZIP file set'
    foreach ($name in $requiredReleaseNames) {
        $zipped = Join-Path $releaseRoot $name
        $sibling = Join-Path $dist $name
        if (-not (Test-Path -LiteralPath $zipped -PathType Leaf)) { throw "Release ZIP missing: $name" }
        Assert-Equal (Get-FileHash -LiteralPath $sibling -Algorithm SHA256).Hash (Get-FileHash -LiteralPath $zipped -Algorithm SHA256).Hash "release ZIP hash: $name"
    }

    Write-Host "FrameScope package parity passed. buildId=$($payloadManifest.buildId) payloadSha256=$($setupInfo.PayloadSha256)"
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
