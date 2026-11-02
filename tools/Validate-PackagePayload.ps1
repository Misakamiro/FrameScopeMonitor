param([string]$RepoRoot=(Split-Path -Parent $PSScriptRoot),[Parameter(Mandatory=$true)][string]$DistRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RepoRoot).Path
$dist=[IO.Path]::GetFullPath($DistRoot)
$payloadRoot=Join-Path $dist 'FrameScopeMonitor-payload'
$version=(Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
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

function Test-ReservedWindowsPackagePathSegment {
    param([string]$Segment)

    $deviceName = if ($null -eq $Segment) { '' } else { $Segment }
    $extensionIndex = $deviceName.IndexOf('.')
    if ($extensionIndex -ge 0) { $deviceName = $deviceName.Substring(0, $extensionIndex) }
    $deviceName = $deviceName.TrimEnd([char[]]@(' ', '.'))
    return $deviceName -match '^(CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[1-9\u00B9\u00B2\u00B3]|LPT[1-9\u00B9\u00B2\u00B3])$'
}

function ConvertTo-NormalizedPackageArchivePath {
    param([string]$Path)

    $normalized = if ($null -eq $Path) { '' } else { $Path.Replace('\', '/') }
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized.StartsWith('/', [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($normalized)) {
        throw "Unsafe ZIP entry path: $normalized"
    }

    $rawSegments = $normalized.Split([char[]]@('/'), [StringSplitOptions]::None)
    $segments = New-Object Collections.Generic.List[string]
    for ($index = 0; $index -lt $rawSegments.Length; $index++) {
        $segment = $rawSegments[$index]
        if ($segment.Length -eq 0) {
            if ($index -eq ($rawSegments.Length - 1) -and $segments.Count -gt 0) { continue }
            throw "Unsafe ZIP entry path: $normalized"
        }
        if ($segment -eq '.' -or $segment -eq '..' -or
            $segment -cne $segment.TrimEnd([char[]]@(' ', '.')) -or
            $segment.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
            (Test-ReservedWindowsPackagePathSegment -Segment $segment)) {
            throw "Unsafe ZIP entry path: $normalized"
        }
        $segments.Add($segment)
    }
    if ($segments.Count -eq 0) { throw "Unsafe ZIP entry path: $normalized" }
    return $segments -join '/'
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
            $entryPath = ConvertTo-NormalizedPackageArchivePath -Path $entry.FullName
            $relative = $entryPath.Replace('/', '\')
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
    if (-not $fileVersion.StartsWith($version.Split('-')[0] + '.', [StringComparison]::Ordinal)) {
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

$temp = Join-Path $env:TEMP ('framescope-package-verify-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
    $setupPath = Join-Path $dist 'FrameScopeMonitor-Setup.exe'
    $fullSetupPath = Join-Path $dist 'FrameScopeMonitor-Full-Setup.exe'
    $setupPayload = Join-Path $temp 'setup-payload'
    $fullPayload = Join-Path $temp 'full-payload'
    $setupInfo = Expand-PayloadResource -AssemblyPath $setupPath -Destination $setupPayload
    $fullInfo = Expand-PayloadResource -AssemblyPath $fullSetupPath -Destination $fullPayload

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
    Assert-Equal 7 $releaseFiles.Count 'release ZIP file count'
    $requiredReleaseNames = @('FrameScopeMonitor-Setup.exe', 'FrameScopeMonitor-Full-Setup.exe', 'FrameScopeMonitor-LegacyCleanup.exe', 'README-FrameScopeMonitor.txt',
        'FrameScopeMonitor-installer-source/payload.zip', 'FrameScopeMonitor-installer-source/Install-FrameScopeMonitor.ps1', 'FrameScopeMonitor-installer-source/FrameScopeUninstaller.exe')
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
