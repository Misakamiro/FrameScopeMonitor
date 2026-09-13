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
    # Layout must match the NSIS resource mapping (resources/backend/* ->
    # install root, backend/tools -> tools/, fonts -> Fonts/): the app
    # resolves its backend chain from EXE-side directories, so the payload
    # must lay backend contents FLAT at the root - nesting a backend/ folder
    # breaks hardware data entirely (measured).
    Copy-Item (Join-Path $staging 'backend\*') $stage -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $stage 'Fonts') -Force | Out-Null
    if (Test-Path (Join-Path $staging 'fonts\*')) {
        Copy-Item (Join-Path $staging 'fonts\*') (Join-Path $stage 'Fonts') -Recurse -Force
    }
    # dependencies\ (PawnIO driver setup) keeps its nested layout beside the
    # exe: pawnio.rs resolves dependencies\PawnIO\PawnIO_setup.exe from the
    # backend root.
    if (Test-Path (Join-Path $staging 'dependencies')) {
        Copy-Item (Join-Path $staging 'dependencies') (Join-Path $stage 'dependencies') -Recurse -Force
    }

    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $outZip -CompressionLevel Optimal
} finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

$size = [math]::Round((Get-Item $outZip).Length / 1MB, 1)
Write-Output "payload.zip 已生成：$outZip ($size MB)"
