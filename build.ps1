$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found: $csc"
}

$webView2PackageRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.web.webview2'
$webView2Package = Get-ChildItem -LiteralPath $webView2PackageRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'lib\net462\Microsoft.Web.WebView2.WinForms.dll') } |
    Sort-Object { [version]$_.Name } |
    Select-Object -Last 1
if ($null -eq $webView2Package) {
    throw "Microsoft.Web.WebView2 package was not found. Restore the Microsoft.Web.WebView2 NuGet package into the local NuGet cache before running build.ps1."
}
$webView2Core = Join-Path $webView2Package.FullName 'lib\net462\Microsoft.Web.WebView2.Core.dll'
$webView2WinForms = Join-Path $webView2Package.FullName 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'
$webView2Loader = Join-Path $webView2Package.FullName 'runtimes\win-x64\native\WebView2Loader.dll'
foreach ($requiredWebView2File in @($webView2Core, $webView2WinForms, $webView2Loader)) {
    if (-not (Test-Path -LiteralPath $requiredWebView2File)) {
        throw "WebView2 dependency missing: $requiredWebView2File"
    }
}
$webView2StandaloneUrl = 'https://go.microsoft.com/fwlink/?linkid=2124701'
$webView2StandaloneInstaller = Join-Path $root 'packaging\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
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

function Restore-HardwareTelemetryDependencies {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        throw "dotnet.exe was not found. It is required to restore LibreHardwareMonitorLib for built-in CPU voltage telemetry."
    }

    $temp = Join-Path $env:TEMP ('framescope-hardware-telemetry-deps-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    try {
        $project = Join-Path $temp 'FrameScopeHardwareTelemetryDeps.csproj'
        @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <OutputType>Exe</OutputType>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="LibreHardwareMonitorLib" Version="0.9.6" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath $project -Encoding UTF8
        'internal static class Program { private static void Main() { } }' | Set-Content -LiteralPath (Join-Path $temp 'Program.cs') -Encoding UTF8
        Push-Location $temp
        try {
            dotnet build -c Release | Out-Host
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
        return @($dlls | Select-Object -ExpandProperty Name)
    }
    finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$hardwareTelemetryDependencyNames = Restore-HardwareTelemetryDependencies

Push-Location $root
try {
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /win32icon:$appIcon `
        /out:FrameScopeMonitor.exe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.Management.dll `
        /reference:System.Web.Extensions.dll `
        /reference:$webView2Core `
        /reference:$webView2WinForms `
        .\src\core\FrameScopeConfigStore.cs `
        .\src\core\FrameScopeLoggingPolicy.cs `
        .\src\core\FrameScopeCapturePlanner.cs `
        .\src\core\FrameScopePresentMonDiagnostics.cs `
        .\src\core\FrameScopeProcessPicker.cs `
        .\src\core\FrameScopeTargetEditRules.cs `
        .\src\diagnostics\FrameScopeDiagnostics.cs `
        .\src\diagnostics\FrameScopeDiagnostics.Models.cs `
        .\src\diagnostics\FrameScopeDiagnostics.Sections.cs `
        .\src\diagnostics\FrameScopeDiagnostics.Markdown.cs `
        .\src\diagnostics\FrameScopeDiagnostics.Redaction.cs `
        .\src\diagnostics\FrameScopeDiagnostics.Retention.cs `
        .\src\diagnostics\FrameScopeDiagnostics.IO.cs `
        .\src\core\FrameScopeReportProgress.cs `
        .\src\app\FrameScopeWebBridge.Contracts.cs `
        .\src\app\FrameScopeWebBridge.cs `
        .\src\app\FrameScopeWebBridge.State.cs `
        .\src\app\FrameScopeWebBridge.Config.cs `
        .\src\app\FrameScopeWebBridge.Processes.cs `
        .\src\app\FrameScopeWebBridge.Monitoring.cs `
        .\src\app\FrameScopeWebBridge.Reports.cs `
        .\src\app\FrameScopeWebBridge.Diagnostics.cs `
        .\src\app\FrameScopeWebBridge.Targets.cs `
        .\src\app\FrameScopeWebView2Runtime.cs `
        .\src\app\FrameScopeWebHostLifecycle.cs `
        .\src\app\FrameScopeAppIcon.cs `
        .\src\app\FrameScopeNativeMonitor.cs `
        .\src\app\FrameScopeNativeMonitor.SingleInstance.cs `
        .\src\app\FrameScopeNativeMonitor.WebHost.cs `
        .\src\app\FrameScopeNativeMonitor.ReportOrchestration.cs `
        .\src\app\FrameScopeNativeMonitor.ReportOrchestration.Models.cs `
        .\src\app\FrameScopeNativeMonitor.ReportStatus.cs `
        .\src\app\FrameScopeNativeMonitor.ReportOpen.cs `
        .\src\app\FrameScopeNativeMonitor.ReportOpen.Browser.cs `
        .\src\app\FrameScopeNativeMonitor.ReportOpen.Status.cs `
        .\src\app\FrameScopeNativeMonitor.ProcessCleanup.cs `
        .\src\app\FrameScopeNativeMonitor.Watcher.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.Models.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.Paths.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.Targets.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.Tools.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.PresentMon.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.Status.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeMonitor.exe" }

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeProcessSampler.exe `
        .\src\monitoring\FrameScopeProcessSampler.cs `
        .\src\monitoring\FrameScopeProcessSampler.Models.cs `
        .\src\monitoring\FrameScopeProcessSampler.Selection.cs `
        .\src\monitoring\FrameScopeProcessSampler.IO.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeProcessSampler.exe" }

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeSystemSampler.exe `
        /reference:System.Core.dll `
        /reference:System.Management.dll `
        .\src\monitoring\FrameScopeSystemSampler.cs `
        .\src\monitoring\FrameScopeSystemSampler.Models.cs `
        .\src\monitoring\FrameScopeSystemSampler.PerfCounters.cs `
        .\src\monitoring\FrameScopeSystemSampler.CpuCoreTelemetry.cs `
        .\src\monitoring\FrameScopeSystemSampler.Gpu.cs `
        .\src\monitoring\FrameScopeSystemSampler.Processes.cs `
        .\src\monitoring\FrameScopeSystemSampler.IO.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeSystemSampler.exe" }

    & $csc /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeReportGenerator.exe `
        /reference:System.Web.Extensions.dll `
        /reference:System.Management.dll `
        /reference:Microsoft.VisualBasic.dll `
        .\src\core\FrameScopeReportProgress.cs `
        .\src\core\FrameScopePresentMonDiagnostics.cs `
        .\src\reporting\FrameScopeReportGenerator.cs `
        .\src\reporting\FrameScopeReportGenerator.Models.cs `
        .\src\reporting\FrameScopeReportGenerator.Cli.cs `
        .\src\reporting\FrameScopeReportGenerator.Progress.cs `
        .\src\reporting\FrameScopeReportGenerator.Diagnostics.cs `
        .\src\reporting\FrameScopeReportGenerator.PresentMon.cs `
        .\src\reporting\FrameScopeReportGenerator.SystemData.cs `
        .\src\reporting\FrameScopeReportGenerator.ProcessData.cs `
        .\src\reporting\FrameScopeReportGenerator.Analysis.cs `
        .\src\reporting\FrameScopeReportGenerator.Metadata.cs `
        .\src\reporting\FrameScopeReportGenerator.Csv.cs `
        .\src\reporting\FrameScopeReportGenerator.Html.Layout.cs `
        .\src\reporting\FrameScopeReportGenerator.Html.Styles.cs `
        .\src\reporting\FrameScopeReportGenerator.Html.Sections.cs `
        .\src\reporting\FrameScopeReportGenerator.Html.Scripts.cs `
        .\src\reporting\FrameScopeReportGenerator.Html.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeReportGenerator.exe" }

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /win32icon:$appIcon `
        /out:FrameScopeUninstaller.exe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        .\packaging\FrameScopeUninstaller.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeUninstaller.exe" }

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /win32icon:$appIcon `
        /out:FrameScopeLegacyCleanup.exe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.Management.dll `
        .\packaging\FrameScopeLegacyCleanup.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeLegacyCleanup.exe" }

    Copy-Item -LiteralPath $webView2Core -Destination (Join-Path $root 'Microsoft.Web.WebView2.Core.dll') -Force
    Copy-Item -LiteralPath $webView2WinForms -Destination (Join-Path $root 'Microsoft.Web.WebView2.WinForms.dll') -Force
    Copy-Item -LiteralPath $webView2Loader -Destination (Join-Path $root 'WebView2Loader.dll') -Force

    $dist = Join-Path $root 'dist'
    $payloadRoot = Join-Path $dist 'FrameScopeMonitor-payload'
    $sourceRoot = Join-Path $dist 'FrameScopeMonitor-installer-source'
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

    $payloadTools = Join-Path $payloadRoot 'tools'
    New-Item -ItemType Directory -Path $payloadTools -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'tools\PresentMon-2.4.1-x64.exe') -Destination $payloadTools -Force

    $payloadFrontend = Join-Path $payloadRoot 'frontend'
    Copy-Item -LiteralPath $frontendDist -Destination $payloadFrontend -Recurse -Force

    $payloadIconDir = Join-Path $payloadRoot 'assets\icon'
    New-Item -ItemType Directory -Path $payloadIconDir -Force | Out-Null
    Copy-Item -LiteralPath $appIcon -Destination $payloadIconDir -Force
    Copy-Item -LiteralPath $appIconPng -Destination $payloadIconDir -Force

    $payloadZip = Join-Path $sourceRoot 'payload.zip'
    Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $payloadZip -Force

    $setupExe = Join-Path $dist 'FrameScopeMonitor-Setup.exe'
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /win32icon:$appIcon `
        /out:$setupExe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.IO.Compression.dll `
        /reference:System.IO.Compression.FileSystem.dll `
        /resource:$payloadZip,FrameScopePayload `
        .\src\app\FrameScopeWebView2Runtime.cs `
        .\packaging\FrameScopeSetupNative.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeMonitor-Setup.exe" }

    if (-not (Test-Path -LiteralPath $webView2StandaloneInstaller)) {
        Write-Host "Downloading WebView2 Evergreen Standalone Installer x64..."
        Invoke-WebRequest -UseBasicParsing -Uri $webView2StandaloneUrl -OutFile $webView2StandaloneInstaller
    }
    if (-not (Test-Path -LiteralPath $webView2StandaloneInstaller)) {
        throw "WebView2 Evergreen Standalone Installer x64 was not found: $webView2StandaloneInstaller"
    }
    $runtimeInstallerInfo = Get-Item -LiteralPath $webView2StandaloneInstaller
    if ($runtimeInstallerInfo.Length -lt 50000000) {
        throw "WebView2 installer is too small for the offline x64 standalone package ($($runtimeInstallerInfo.Length) bytes): $webView2StandaloneInstaller"
    }

    $fullSetupExe = Join-Path $dist 'FrameScopeMonitor-Full-Setup.exe'
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /win32icon:$appIcon `
        /out:$fullSetupExe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.IO.Compression.dll `
        /reference:System.IO.Compression.FileSystem.dll `
        /resource:$payloadZip,FrameScopePayload `
        /resource:$webView2StandaloneInstaller,FrameScopeWebView2RuntimeInstaller `
        .\src\app\FrameScopeWebView2Runtime.cs `
        .\packaging\FrameScopeSetupNative.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeMonitor-Full-Setup.exe" }

    $legacyCleanupExe = Join-Path $dist 'FrameScopeMonitor-LegacyCleanup.exe'
    $distReadme = Join-Path $dist 'README-FrameScopeMonitor.txt'
    Copy-Item -LiteralPath (Join-Path $root 'FrameScopeLegacyCleanup.exe') -Destination $legacyCleanupExe -Force
    Copy-Item -LiteralPath (Join-Path $root 'packaging\README-FrameScopeMonitor.txt') -Destination $distReadme -Force

    $releaseZip = Join-Path $dist 'FrameScopeMonitor-Installer.zip'
    if (Test-Path -LiteralPath $releaseZip) { Remove-Item -LiteralPath $releaseZip -Force }
    Compress-Archive -LiteralPath @($setupExe, $fullSetupExe, $legacyCleanupExe, $distReadme) -DestinationPath $releaseZip -Force

    "Build complete: $setupExe"
    "Full setup complete: $fullSetupExe"
}
finally {
    Pop-Location
}
