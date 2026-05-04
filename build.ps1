$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found: $csc"
}

Push-Location $root
try {
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeMonitor.exe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.Management.dll `
        /reference:System.Web.Extensions.dll `
        .\FrameScopeNativeMonitor.cs

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeProcessSampler.exe `
        .\FrameScopeProcessSampler.cs

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeSystemSampler.exe `
        .\FrameScopeSystemSampler.cs

    & $csc /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeReportGenerator.exe `
        /reference:System.Web.Extensions.dll `
        /reference:System.Management.dll `
        /reference:Microsoft.VisualBasic.dll `
        .\FrameScopeReportGenerator.cs

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeUninstaller.exe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        .\packaging\FrameScopeUninstaller.cs

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeLegacyCleanup.exe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.Management.dll `
        .\packaging\FrameScopeLegacyCleanup.cs

    $dist = Join-Path $root 'dist'
    $payloadRoot = Join-Path $dist 'FrameScopeMonitor-payload'
    $sourceRoot = Join-Path $dist 'FrameScopeMonitor-installer-source'
    foreach ($path in @($payloadRoot, $sourceRoot)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }

    foreach ($file in @(
        'FrameScopeMonitor.exe',
        'FrameScopeProcessSampler.exe',
        'FrameScopeSystemSampler.exe',
        'FrameScopeReportGenerator.exe',
        'FrameScopeUninstaller.exe',
        'packaging\Uninstall-FrameScopeMonitor.cmd',
        'packaging\README-FrameScopeMonitor.txt'
    )) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $payloadRoot -Force
    }

    Copy-Item -LiteralPath (Join-Path $root 'tools') -Destination (Join-Path $payloadRoot 'tools') -Recurse -Force

    $payloadZip = Join-Path $sourceRoot 'payload.zip'
    Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $payloadZip -Force

    $setupExe = Join-Path $dist 'FrameScopeMonitor-Setup.exe'
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:$setupExe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.IO.Compression.dll `
        /reference:System.IO.Compression.FileSystem.dll `
        /resource:$payloadZip,FrameScopePayload `
        .\packaging\FrameScopeSetupNative.cs

    $legacyCleanupExe = Join-Path $dist 'FrameScopeMonitor-LegacyCleanup.exe'
    $distReadme = Join-Path $dist 'README-FrameScopeMonitor.txt'
    Copy-Item -LiteralPath (Join-Path $root 'FrameScopeLegacyCleanup.exe') -Destination $legacyCleanupExe -Force
    Copy-Item -LiteralPath (Join-Path $root 'packaging\README-FrameScopeMonitor.txt') -Destination $distReadme -Force

    $releaseZip = Join-Path $dist 'FrameScopeMonitor-Installer.zip'
    if (Test-Path -LiteralPath $releaseZip) { Remove-Item -LiteralPath $releaseZip -Force }
    Compress-Archive -LiteralPath @($setupExe, $legacyCleanupExe, $distReadme) -DestinationPath $releaseZip -Force

    "Build complete: $setupExe"
}
finally {
    Pop-Location
}
