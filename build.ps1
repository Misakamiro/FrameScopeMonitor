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
        /reference:System.Web.Extensions.dll `
        .\FrameScopeNativeMonitor.cs

    $dist = Join-Path $root 'dist'
    $payloadRoot = Join-Path $dist 'FrameScopeMonitor-payload'
    $sourceRoot = Join-Path $dist 'FrameScopeMonitor-installer-source'
    foreach ($path in @($payloadRoot, $sourceRoot)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }

    foreach ($file in @(
        'FrameScopeMonitor.exe',
        'FrameScopeWatcher.ps1',
        'Monitor-CS2-HighFreq.ps1',
        'Generate-CS2-FrameScope-Interactive-Report.py',
        'packaging\Uninstall-FrameScopeMonitor.cmd',
        'packaging\README-FrameScopeMonitor.txt'
    )) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $payloadRoot -Force
    }

    Copy-Item -LiteralPath (Join-Path $root 'tools') -Destination (Join-Path $payloadRoot 'tools') -Recurse -Force

    $pythonRoot = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python'
    if (-not (Test-Path -LiteralPath (Join-Path $pythonRoot 'python.exe'))) {
        throw "Portable Python runtime not found: $pythonRoot"
    }
    New-Item -ItemType Directory -Path (Join-Path $payloadRoot 'runtime') -Force | Out-Null
    robocopy $pythonRoot (Join-Path $payloadRoot 'runtime\python') /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "robocopy failed with exit code $LASTEXITCODE" }

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

    "Build complete: $setupExe"
}
finally {
    Pop-Location
}
