$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found: $csc"
}
$lockPath = Join-Path $root 'dependencies.lock.json'
if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
    throw "Native dependency lock was not found: $lockPath"
}
$lock = Get-Content -Raw -LiteralPath (Join-Path $root 'dependencies.lock.json') | ConvertFrom-Json
$webView2Version = [string]$lock.microsoftWebView2
$libreHardwareMonitorVersion = [string]$lock.libreHardwareMonitorLib
$nugetCache = Join-Path $root 'tools\.cache\nuget'
$version = (Get-Content -Raw -LiteralPath (Join-Path $root 'VERSION')).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid VERSION: $version"
}

function Assert-LockedFileHash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [object]$Entry
    )

    $relativePath = [string]$Entry.file
    $expectedSha256 = ([string]$Entry.sha256).ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($relativePath) -or $expectedSha256 -notmatch '^[0-9A-F]{64}$') {
        throw "Invalid locked file entry for ${Name}: file=$relativePath sha256=$expectedSha256"
    }

    $fullPath = Join-Path $root ($relativePath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Locked file is missing for ${Name}: $fullPath"
    }

    $actualSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $fullPath).Hash.ToUpperInvariant()
    if ($actualSha256 -cne $expectedSha256) {
        throw "Locked file hash mismatch for ${Name}: expected $expectedSha256, found $actualSha256 ($fullPath)"
    }

    Write-Host "Verified pinned file ${Name}: path=$relativePath SHA256=$actualSha256"
    return $fullPath
}

$presentMon = Assert-LockedFileHash -Name 'PresentMon' -Entry $lock.presentMon
$webView2StandaloneInstaller = Assert-LockedFileHash -Name 'WebView2 standalone installer' -Entry $lock.webView2StandaloneInstaller

function Restore-NativeDependencies {
    param(
        [Parameter(Mandatory = $true)]
        [string]$NugetCache,

        [Parameter(Mandatory = $true)]
        [string]$WebView2Version,

        [Parameter(Mandatory = $true)]
        [string]$LibreHardwareMonitorVersion
    )

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        throw "dotnet.exe was not found. It is required to restore locked native dependencies."
    }

    New-Item -ItemType Directory -Path $NugetCache -Force | Out-Null
    $temp = Join-Path $env:TEMP ('framescope-native-deps-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    try {
        $project = Join-Path $temp 'FrameScopeNativeDependencies.csproj'
        $escapedNugetCache = [Security.SecurityElement]::Escape($NugetCache)
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <OutputType>Exe</OutputType>
    <PlatformTarget>x64</PlatformTarget>
    <RestorePackagesPath>$escapedNugetCache</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="[$webView2Version]" ExcludeAssets="all" />
    <PackageReference Include="LibreHardwareMonitorLib" Version="[$libreHardwareMonitorVersion]" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding UTF8
        'internal static class Program { private static void Main() { } }' | Set-Content -LiteralPath (Join-Path $temp 'Program.cs') -Encoding UTF8

        Push-Location $temp
        try {
            & $dotnet.Source restore $project --packages $NugetCache --force-evaluate | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet restore failed for locked native dependencies."
            }

            $webView2Package = Join-Path (Join-Path $nugetCache 'microsoft.web.webview2') $webView2Version
            $libreHardwareMonitorPackage = Join-Path (Join-Path $nugetCache 'librehardwaremonitorlib') $libreHardwareMonitorVersion
            foreach ($package in @(
                    @{ Name = 'Microsoft.Web.WebView2'; Version = $WebView2Version; Path = $webView2Package },
                    @{ Name = 'LibreHardwareMonitorLib'; Version = $LibreHardwareMonitorVersion; Path = $libreHardwareMonitorPackage }
                )) {
                if (-not (Test-Path -LiteralPath $package.Path -PathType Container)) {
                    throw "Locked NuGet package was not restored: $($package.Name) $($package.Version) ($($package.Path))"
                }
                Write-Host "Resolved NuGet dependency $($package.Name): version=$($package.Version) path=$($package.Path)"
            }

            $webView2Core = Join-Path $webView2Package 'lib\net462\Microsoft.Web.WebView2.Core.dll'
            $webView2WinForms = Join-Path $webView2Package 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'
            $webView2Loader = Join-Path $webView2Package 'runtimes\win-x64\native\WebView2Loader.dll'
            $libreHardwareMonitor = Join-Path $libreHardwareMonitorPackage 'runtimes\win-x64\lib\net472\LibreHardwareMonitorLib.dll'
            foreach ($requiredFile in @($webView2Core, $webView2WinForms, $webView2Loader, $libreHardwareMonitor)) {
                if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
                    throw "Locked NuGet package file is missing: $requiredFile"
                }
            }

            & $dotnet.Source build $project -c Release --no-restore | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet build failed while resolving locked hardware telemetry runtime files."
            }
        }
        finally {
            Pop-Location
        }

        $output = Join-Path $temp 'bin\Release\net472'
        $dlls = Get-ChildItem -LiteralPath $output -Filter '*.dll' -File
        if (-not ($dlls | Where-Object { $_.Name -eq 'LibreHardwareMonitorLib.dll' })) {
            throw "LibreHardwareMonitorLib.dll was not produced by dependency restore."
        }

        foreach ($dll in $dlls) {
            Copy-Item -LiteralPath $dll.FullName -Destination (Join-Path $root $dll.Name) -Force
        }
        return [pscustomobject]@{
            HardwareTelemetryDependencyNames = @($dlls | Select-Object -ExpandProperty Name)
            WebView2Core = $webView2Core
            WebView2WinForms = $webView2WinForms
            WebView2Loader = $webView2Loader
        }
    }
    finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$nativeDependencies = Restore-NativeDependencies `
    -NugetCache $nugetCache `
    -WebView2Version $webView2Version `
    -LibreHardwareMonitorVersion $libreHardwareMonitorVersion
$hardwareTelemetryDependencyNames = @($nativeDependencies.HardwareTelemetryDependencyNames)
$webView2Core = $nativeDependencies.WebView2Core
$webView2WinForms = $nativeDependencies.WebView2WinForms
$webView2Loader = $nativeDependencies.WebView2Loader

$buildMetadata = & (Join-Path $root 'tools\Write-FrameScopeBuildMetadata.ps1') -RepoRoot $root
& (Join-Path $root 'tools\Export-FrameScopeDefaultConfig.ps1') -RepoRoot $root | Out-Host

$appIcon = Join-Path $root 'assets\icon\framescope-icon.ico'
$appIconPng = Join-Path $root 'assets\icon\framescope-icon.png'
if ((-not (Test-Path -LiteralPath $appIcon)) -or (-not (Test-Path -LiteralPath $appIconPng))) {
    $iconGenerator = Join-Path $root 'tools\Generate-FrameScopeIcon.ps1'
    if (-not (Test-Path -LiteralPath $iconGenerator)) {
        throw "FrameScope icon assets are missing and the generator was not found: $iconGenerator"
    }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $iconGenerator | Out-Host
}
if (-not (Test-Path -LiteralPath $appIcon)) {
    throw "FrameScope icon was not found: $appIcon"
}

function Invoke-CSharpBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,

        [Parameter(Mandatory = $true)]
        [ValidateSet('exe', 'winexe')]
        [string]$Target,

        [Parameter(Mandatory = $true)]
        [string[]]$Sources,

        [string[]]$References = @(),
        [string[]]$Resources = @(),
        [string]$Win32Icon
    )

    $arguments = @(
        '/nologo'
        "/target:$Target"
        '/platform:x64'
        '/optimize+'
        '/codepage:65001'
        "/out:$OutputPath"
    )
    if (-not [string]::IsNullOrWhiteSpace($Win32Icon)) {
        $arguments += "/win32icon:$Win32Icon"
    }
    foreach ($reference in $References) {
        $arguments += "/reference:$reference"
    }
    foreach ($resource in $Resources) {
        $arguments += "/resource:$resource"
    }
    $arguments += $buildMetadata
    $arguments += $Sources

    & $csc @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "csc failed: $OutputPath"
    }
}

$commonCoreSources = @(
    'src\core\FrameScopeJsonFile.cs'
)
$monitorSources = @(
    'src\core\FrameScopeBoundedProcessRunner.cs'
    'src\core\FrameScopeHistoryFile.cs'
    'src\core\FrameScopeRunRetention.cs'
    'src\core\FrameScopeReportArtifacts.cs'
    'src\core\FrameScopeReportRecoveryPolicy.cs'
    'src\core\FrameScopeConfigStore.cs'
    'src\core\FrameScopeLoggingPolicy.cs'
    'src\core\FrameScopeCapturePlanner.cs'
    'src\core\FrameScopePresentMonDiagnostics.cs'
    'src\core\FrameScopePresentMonSessionPolicy.cs'
    'src\core\FrameScopeRunContract.cs'
    'src\core\FrameScopeTargetLifecycle.cs'
    'src\core\FrameScopeProcessPicker.cs'
    'src\core\FrameScopeTargetEditRules.cs'
    'src\core\FrameScopeReportProgress.cs'
    'src\diagnostics\FrameScopeDiagnostics.cs'
    'src\diagnostics\FrameScopeDiagnostics.Models.cs'
    'src\diagnostics\FrameScopeDiagnostics.Sections.cs'
    'src\diagnostics\FrameScopeDiagnostics.Markdown.cs'
    'src\diagnostics\FrameScopeDiagnostics.Redaction.cs'
    'src\diagnostics\FrameScopeDiagnostics.Retention.cs'
    'src\diagnostics\FrameScopeDiagnostics.IO.cs'
    'src\app\FrameScopeWebBridge.Contracts.cs'
    'src\app\FrameScopeWebBridge.cs'
    'src\app\FrameScopeWebBridge.State.cs'
    'src\app\FrameScopeWebBridge.Config.cs'
    'src\app\FrameScopeWebBridge.Processes.cs'
    'src\app\FrameScopeWebBridge.Monitoring.cs'
    'src\app\FrameScopeWebBridge.Reports.cs'
    'src\app\FrameScopeWebBridge.Diagnostics.cs'
    'src\app\FrameScopeWebBridge.Targets.cs'
    'src\app\FrameScopeWebView2Runtime.cs'
    'src\app\FrameScopeWebHostLifecycle.cs'
    'src\app\FrameScopeAppIcon.cs'
    'src\app\FrameScopeNativeMonitor.cs'
    'src\app\FrameScopeNativeMonitor.SingleInstance.cs'
    'src\app\FrameScopeNativeMonitor.WebHost.cs'
    'src\app\FrameScopeNativeMonitor.ReportOrchestration.cs'
    'src\app\FrameScopeNativeMonitor.ReportProcess.cs'
    'src\app\FrameScopeNativeMonitor.ReportOrchestration.Models.cs'
    'src\app\FrameScopeNativeMonitor.Retention.cs'
    'src\app\FrameScopeNativeMonitor.ReportStatus.cs'
    'src\app\FrameScopeNativeMonitor.ReportOpen.cs'
    'src\app\FrameScopeNativeMonitor.ReportOpen.Browser.cs'
    'src\app\FrameScopeNativeMonitor.ReportOpen.Status.cs'
    'src\app\FrameScopeNativeMonitor.ProcessCleanup.cs'
    'src\app\FrameScopeNativeMonitor.Watcher.cs'
    'src\app\FrameScopeNativeMonitor.MonitorSession.cs'
    'src\app\FrameScopeNativeMonitor.MonitorSession.Models.cs'
    'src\app\FrameScopeNativeMonitor.MonitorSession.Paths.cs'
    'src\app\FrameScopeNativeMonitor.MonitorSession.Targets.cs'
    'src\app\FrameScopeNativeMonitor.MonitorSession.Tools.cs'
    'src\app\FrameScopeNativeMonitor.MonitorSession.PresentMon.cs'
    'src\app\FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs'
    'src\app\FrameScopeNativeMonitor.MonitorSession.Status.cs'
)
$samplerSources = @(
    'src\core\FrameScopeTargetLifecycle.cs'
    'src\monitoring\FrameScopeProcessSampler.cs'
    'src\monitoring\FrameScopeProcessSampler.Models.cs'
    'src\monitoring\FrameScopeProcessSampler.Selection.cs'
    'src\monitoring\FrameScopeProcessSampler.IO.cs'
)
$systemSamplerSources = @(
    'src\core\FrameScopeTargetLifecycle.cs'
    'src\monitoring\FrameScopeSystemSampler.cs'
    'src\monitoring\FrameScopeSystemSampler.Models.cs'
    'src\monitoring\FrameScopeSystemSampler.PerfCounters.cs'
    'src\monitoring\FrameScopeSystemSampler.CpuCoreTelemetry.cs'
    'src\monitoring\FrameScopeSystemSampler.Gpu.cs'
    'src\monitoring\FrameScopeSystemSampler.Processes.cs'
    'src\monitoring\FrameScopeSystemSampler.IO.cs'
)
$reportSources = @(
    'src\core\FrameScopeReportProgress.cs'
    'src\core\FrameScopePresentMonDiagnostics.cs'
    'src\core\FrameScopeReportArtifacts.cs'
    'src\core\FrameScopeReportPublisher.cs'
    'src\core\FrameScopeRunContract.cs'
    'src\reporting\FrameScopeReportGenerator.cs'
    'src\reporting\FrameScopeReportGenerator.Models.cs'
    'src\reporting\FrameScopeReportGenerator.Cli.cs'
    'src\reporting\FrameScopeReportGenerator.Progress.cs'
    'src\reporting\FrameScopeReportGenerator.Diagnostics.cs'
    'src\reporting\FrameScopeReportGenerator.PresentMon.cs'
    'src\reporting\FrameScopeReportGenerator.SystemData.cs'
    'src\reporting\FrameScopeReportGenerator.ProcessData.cs'
    'src\reporting\FrameScopeReportGenerator.Analysis.cs'
    'src\reporting\FrameScopeReportGenerator.Metadata.cs'
    'src\reporting\FrameScopeReportGenerator.Csv.cs'
    'src\reporting\FrameScopeReportGenerator.Html.Layout.cs'
    'src\reporting\FrameScopeReportGenerator.Html.Styles.cs'
    'src\reporting\FrameScopeReportGenerator.Html.Sections.cs'
    'src\reporting\FrameScopeReportGenerator.Html.Scripts.cs'
    'src\reporting\FrameScopeReportGenerator.Html.cs'
)
$setupSources = @(
    'src\core\FrameScopeConfigStore.cs'
    'src\app\FrameScopeWebView2Runtime.cs'
    'packaging\FrameScopeSetupNative.cs'
)
$uninstallerSources = @(
    'packaging\FrameScopeUninstaller.cs'
)
$legacyCleanupSources = @(
    'packaging\FrameScopeLegacyCleanup.cs'
)
$packagingOnlySources = @()

$monitorReferences = @(
    'System.Windows.Forms.dll'
    'System.Drawing.dll'
    'System.Management.dll'
    'System.Web.Extensions.dll'
    $webView2Core
    $webView2WinForms
)
$systemSamplerReferences = @(
    'System.Core.dll'
    'System.Management.dll'
)
$reportReferences = @(
    'System.Web.Extensions.dll'
    'System.Management.dll'
    'Microsoft.VisualBasic.dll'
)
$windowsFormsReferences = @(
    'System.Windows.Forms.dll'
    'System.Drawing.dll'
)
$setupReferences = @(
    'System.Windows.Forms.dll'
    'System.Drawing.dll'
    'System.IO.Compression.dll'
    'System.IO.Compression.FileSystem.dll'
    'System.Web.Extensions.dll'
)

function Resolve-GeneratedReleasePath {
    param(
        [string]$Path,
        [string]$ExpectedPrefix
    )
    $fullRoot = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    $fullPath = [IO.Path]::GetFullPath($Path)
    $leaf = Split-Path -Leaf $fullPath
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase) -or -not $leaf.StartsWith($ExpectedPrefix, [StringComparison]::Ordinal)) {
        throw "Unsafe generated release path: $fullPath"
    }
    return $fullPath
}

$dist = Join-Path $root 'dist'
$releaseToken = [guid]::NewGuid().ToString('N')
$distStage = Resolve-GeneratedReleasePath -Path (Join-Path $root ("dist.stage.$releaseToken")) -ExpectedPrefix 'dist.stage.'
$distBackup = Resolve-GeneratedReleasePath -Path (Join-Path $root ("dist.backup.$releaseToken")) -ExpectedPrefix 'dist.backup.'
$distPublished = $false

Push-Location $root
try {
    Invoke-CSharpBuild -OutputPath 'FrameScopeMonitor.exe' -Target 'winexe' `
        -Sources @($commonCoreSources + $monitorSources) `
        -References $monitorReferences -Win32Icon $appIcon
    Invoke-CSharpBuild -OutputPath 'FrameScopeProcessSampler.exe' -Target 'winexe' `
        -Sources $samplerSources
    Invoke-CSharpBuild -OutputPath 'FrameScopeSystemSampler.exe' -Target 'winexe' `
        -Sources @($commonCoreSources + $systemSamplerSources) `
        -References $systemSamplerReferences
    Invoke-CSharpBuild -OutputPath 'FrameScopeReportGenerator.exe' -Target 'exe' `
        -Sources @($commonCoreSources + $reportSources) `
        -References $reportReferences
    Invoke-CSharpBuild -OutputPath 'FrameScopeUninstaller.exe' -Target 'winexe' `
        -Sources $uninstallerSources -References $windowsFormsReferences -Win32Icon $appIcon
    Invoke-CSharpBuild -OutputPath 'FrameScopeLegacyCleanup.exe' -Target 'winexe' `
        -Sources $legacyCleanupSources `
        -References @($windowsFormsReferences + 'System.Management.dll') -Win32Icon $appIcon

    Copy-Item -LiteralPath $webView2Core -Destination (Join-Path $root 'Microsoft.Web.WebView2.Core.dll') -Force
    Copy-Item -LiteralPath $webView2WinForms -Destination (Join-Path $root 'Microsoft.Web.WebView2.WinForms.dll') -Force
    Copy-Item -LiteralPath $webView2Loader -Destination (Join-Path $root 'WebView2Loader.dll') -Force

    New-Item -ItemType Directory -Path $distStage -Force | Out-Null
    $payloadRoot = Join-Path $distStage 'FrameScopeMonitor-payload'
    $sourceRoot = Join-Path $distStage 'FrameScopeMonitor-installer-source'
    $frontendDist = Join-Path $root 'src\frontend\dist'
    if (-not (Test-Path -LiteralPath (Join-Path $frontendDist 'index.html'))) {
        throw "Frontend dist was not found. Run: powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 build"
    }
    foreach ($path in @($payloadRoot, $sourceRoot)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }

    $payloadFiles = @(
        'FrameScopeMonitor.exe',
        'FrameScopeProcessSampler.exe',
        'FrameScopeSystemSampler.exe',
        'FrameScopeReportGenerator.exe',
        'FrameScopeUninstaller.exe',
        'Microsoft.Web.WebView2.Core.dll',
        'Microsoft.Web.WebView2.WinForms.dll',
        'WebView2Loader.dll',
        'packaging\Uninstall-FrameScopeMonitor.cmd',
        'packaging\README-FrameScopeMonitor.txt'
    )
    $payloadFiles += $hardwareTelemetryDependencyNames
    foreach ($file in $payloadFiles) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $payloadRoot -Force
    }
    Copy-Item -LiteralPath (Join-Path $root 'framescope-config.example.json') `
        -Destination (Join-Path $payloadRoot 'framescope-default-config.json') -Force

    $payloadTools = Join-Path $payloadRoot 'tools'
    New-Item -ItemType Directory -Path $payloadTools -Force | Out-Null
    Copy-Item -LiteralPath $presentMon -Destination $payloadTools -Force

    $payloadFrontend = Join-Path $payloadRoot 'frontend'
    Copy-Item -LiteralPath $frontendDist -Destination $payloadFrontend -Recurse -Force

    $payloadIconDir = Join-Path $payloadRoot 'assets\icon'
    New-Item -ItemType Directory -Path $payloadIconDir -Force | Out-Null
    Copy-Item -LiteralPath $appIcon -Destination $payloadIconDir -Force
    Copy-Item -LiteralPath $appIconPng -Destination $payloadIconDir -Force

    $buildId = [guid]::NewGuid().ToString('N')
    $payloadRootUri = [Uri]([IO.Path]::GetFullPath($payloadRoot).TrimEnd('\') + '\')
    $payloadEntries = @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | ForEach-Object {
            $fileUri = [Uri]([IO.Path]::GetFullPath($_.FullName))
            $relative = [Uri]::UnescapeDataString($payloadRootUri.MakeRelativeUri($fileUri).ToString()).Replace('\', '/')
            [ordered]@{
                path = $relative
                length = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
            }
        } | Sort-Object path)
    $payloadManifest = [ordered]@{
        product = 'FrameScope Monitor'
        version = $version
        buildId = $buildId
        dependencies = $lock
        files = $payloadEntries
    }
    $payloadManifestPath = Join-Path $payloadRoot 'FrameScopeBuildManifest.json'
    $payloadManifestTemp = $payloadManifestPath + '.tmp.' + [guid]::NewGuid().ToString('N')
    $utf8NoBom = New-Object Text.UTF8Encoding($false)
    try {
        [IO.File]::WriteAllText($payloadManifestTemp, (($payloadManifest | ConvertTo-Json -Depth 20) + [Environment]::NewLine), $utf8NoBom)
        Move-Item -LiteralPath $payloadManifestTemp -Destination $payloadManifestPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $payloadManifestTemp) {
            Remove-Item -LiteralPath $payloadManifestTemp -Force -ErrorAction SilentlyContinue
        }
    }

    $payloadZip = Join-Path $sourceRoot 'payload.zip'
    Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $payloadZip -Force
    $payloadZipSha256 = (Get-FileHash -LiteralPath $payloadZip -Algorithm SHA256).Hash.ToUpperInvariant()
    Write-Host "Payload provenance: version=$version buildId=$buildId SHA256=$payloadZipSha256 files=$($payloadEntries.Count)"

    $setupExe = Join-Path $distStage 'FrameScopeMonitor-Setup.exe'
    Invoke-CSharpBuild -OutputPath $setupExe -Target 'winexe' `
        -Sources @($commonCoreSources + $setupSources) `
        -References $setupReferences -Resources @("$payloadZip,FrameScopePayload") -Win32Icon $appIcon

    $fullSetupExe = Join-Path $distStage 'FrameScopeMonitor-Full-Setup.exe'
    Invoke-CSharpBuild -OutputPath $fullSetupExe -Target 'winexe' `
        -Sources @($commonCoreSources + $setupSources) `
        -References $setupReferences `
        -Resources @("$payloadZip,FrameScopePayload", "$webView2StandaloneInstaller,FrameScopeWebView2RuntimeInstaller") `
        -Win32Icon $appIcon

    $legacyCleanupExe = Join-Path $distStage 'FrameScopeMonitor-LegacyCleanup.exe'
    $distReadme = Join-Path $distStage 'README-FrameScopeMonitor.txt'
    Copy-Item -LiteralPath (Join-Path $root 'FrameScopeLegacyCleanup.exe') -Destination $legacyCleanupExe -Force
    Copy-Item -LiteralPath (Join-Path $root 'packaging\README-FrameScopeMonitor.txt') -Destination $distReadme -Force

    $releaseZip = Join-Path $distStage 'FrameScopeMonitor-Installer.zip'
    if (Test-Path -LiteralPath $releaseZip) { Remove-Item -LiteralPath $releaseZip -Force }
    Compress-Archive -LiteralPath @($setupExe, $fullSetupExe, $legacyCleanupExe, $distReadme) -DestinationPath $releaseZip -Force

    & (Join-Path $root 'tools\Test-FrameScopePackages.ps1') -RepoRoot $root -DistRoot $distStage | Out-Host

    $hadPreviousDist = Test-Path -LiteralPath $dist -PathType Container
    if ($hadPreviousDist) {
        Move-Item -LiteralPath $dist -Destination $distBackup
    }
    try {
        Move-Item -LiteralPath $distStage -Destination $dist
        $distPublished = $true
    }
    catch {
        if ($hadPreviousDist -and -not (Test-Path -LiteralPath $dist) -and (Test-Path -LiteralPath $distBackup)) {
            Move-Item -LiteralPath $distBackup -Destination $dist
        }
        throw
    }
    if (Test-Path -LiteralPath $distBackup) {
        Remove-Item -LiteralPath $distBackup -Recurse -Force
    }

    "Build complete: $(Join-Path $dist 'FrameScopeMonitor-Setup.exe')"
    "Full setup complete: $(Join-Path $dist 'FrameScopeMonitor-Full-Setup.exe')"
}
finally {
    if (-not $distPublished -and (Test-Path -LiteralPath $distStage)) {
        Remove-Item -LiteralPath $distStage -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $distBackup) {
        if (-not (Test-Path -LiteralPath $dist)) {
            Move-Item -LiteralPath $distBackup -Destination $dist -ErrorAction SilentlyContinue
        }
        else {
            Remove-Item -LiteralPath $distBackup -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    Pop-Location
}
