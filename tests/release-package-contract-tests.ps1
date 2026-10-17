param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$verifier = Join-Path $RepoRoot 'tools\Test-FrameScopePackages.ps1'
$text = Get-Content -LiteralPath $verifier -Raw
$ast = [Management.Automation.Language.Parser]::ParseFile($verifier, [ref]$null, [ref]$null)
$functions = $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]}, $true)
. ([scriptblock]::Create(($functions.Extent.Text -join "`n")))
$check = [regex]::Match($text, '(?s)\$releaseFiles = @\(Get-ChildItem.*?(?=\r?\n    Write-Host "FrameScope package parity)')
if (-not $check.Success) { throw 'Production release verification block not found' }
$body = [scriptblock]::Create($check.Value)
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('framescope-release-contract-' + [guid]::NewGuid().ToString('N'))
$dist = Join-Path $fixture 'dist'
$releaseRoot = Join-Path $fixture 'extracted'
$files = @('FrameScopeMonitor-Setup.exe', 'FrameScopeMonitor-Full-Setup.exe', 'FrameScopeMonitor-LegacyCleanup.exe', 'README-FrameScopeMonitor.txt',
    'FrameScopeMonitor-installer-source\payload.zip', 'FrameScopeMonitor-installer-source\Install-FrameScopeMonitor.ps1', 'FrameScopeMonitor-installer-source\FrameScopeUninstaller.exe')
foreach ($name in $files) {
    foreach ($root in @($dist, $releaseRoot)) {
        $path = Join-Path $root $name
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, "fixture $name")
    }
}
& $body
Write-Host 'PASS exact seven-file release including stop-only script bundle'
$helper = Join-Path $releaseRoot 'FrameScopeMonitor-installer-source\FrameScopeUninstaller.exe'
[IO.File]::WriteAllText($helper, 'substituted helper')
$rejected = $false
try { & $body } catch { $rejected = $_ -like '*release ZIP hash*' }
if (-not $rejected) { throw 'Substituted helper was accepted' }
Write-Host 'PASS substituted stop-only helper rejected'
[IO.File]::WriteAllText($helper, 'fixture FrameScopeMonitor-installer-source\FrameScopeUninstaller.exe')
[IO.File]::WriteAllText((Join-Path $releaseRoot 'unknown.txt'), 'extra')
$rejected = $false
try { & $body } catch { $rejected = $_ -like '*release ZIP file count*' }
if (-not $rejected) { throw 'Unknown additional release file was accepted' }
Write-Host "PASS unknown additional release file rejected; retained fixture $fixture"
