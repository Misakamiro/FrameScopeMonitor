# Packs the app payload (main exe + staged resources) into the installer's
# embedded payload.zip. Requires the main app to be built and staged first —
# run tools/Package-Installer.ps1 (or at least its staging section) before
# this script if src-tauri/target/release/FrameScope.exe is missing.
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
)
$ErrorActionPreference = 'Stop'

if ($PSScriptRoot -notlike '*installer') { throw "Run from installer/ directory context" }

$staging = Join-Path $RepoRoot 'desktop-preview\src-tauri\resources'
$mainExe = Join-Path $RepoRoot 'desktop-preview\src-tauri\target\release\FrameScope.exe'
$outZip  = Join-Path $PSScriptRoot 'src-tauri\payload.zip'

if (-not (Test-Path $mainExe)) {
    throw "主程序未构建：$mainExe 不存在。先运行 tools/Package-Installer.ps1（构建+staging）后再打包。"
}
if (-not (Test-Path $staging)) {
    throw "资源 staging 目录不存在：$staging。先运行 tools/Package-Installer.ps1 完成资源准备。"
}

if (Test-Path $outZip) { Remove-Item $outZip -Force }

$stage = Join-Path $env:TEMP ("framescope-payload-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Copy-Item $mainExe (Join-Path $stage 'FrameScope.exe')
    # resources/* maps to the install root, same layout the NSIS package uses.
    Copy-Item (Join-Path $staging '*') $stage -Recurse -Force
    # payload.zip itself may linger from a previous run inside staging; never
    # nest it.
    if (Test-Path (Join-Path $stage 'payload.zip')) {
        Remove-Item (Join-Path $stage 'payload.zip') -Force
    }

    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $outZip -CompressionLevel Optimal
} finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

$size = [math]::Round((Get-Item $outZip).Length / 1MB, 1)
Write-Output "payload.zip 已生成：$outZip ($size MB)"
