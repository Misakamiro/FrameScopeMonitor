param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepoRoot).Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "csc.exe not found: $csc" }
$obj = Join-Path $root 'obj'
New-Item -ItemType Directory -Path $obj -Force | Out-Null
$exe = Join-Path $obj 'FrameScopeConfigExporter.exe'
& $csc /nologo /target:exe /optimize+ /codepage:65001 /reference:System.Web.Extensions.dll /out:$exe `
    (Join-Path $root 'src\core\FrameScopeJsonFile.cs') `
    (Join-Path $root 'src\core\FrameScopeConfigStore.cs') `
    (Join-Path $root 'tools\FrameScopeConfigExporter.cs')
if ($LASTEXITCODE -ne 0) { throw 'csc failed: FrameScopeConfigExporter.exe' }
& $exe (Join-Path $root 'framescope-config.example.json')
if ($LASTEXITCODE -ne 0) { throw 'FrameScopeConfigExporter failed.' }
Write-Host 'FrameScope default config exported.'
