$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$expected = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if ($expected -notmatch '^(?<core>[2-9][0-9]*\.[0-9]+\.[0-9]+)(-beta\.[1-9][0-9]*)?$') { throw 'Expected a 2.x or later product version' }
$core=$Matches.core
foreach ($relative in @('desktop-preview/package.json','desktop-preview/src-tauri/tauri.conf.json','desktop-preview/installer/src-tauri/tauri.conf.json','src/frontend/package.json')) {
    $actual = (Get-Content (Join-Path $root $relative) -Raw | ConvertFrom-Json).version
    if ($actual -ne $expected) { throw "Version mismatch: $relative ($actual)" }
}
$output = Join-Path ([IO.Path]::GetTempPath()) ('framescope-metadata-' + [guid]::NewGuid().ToString('N') + '.cs')
try {
    & (Join-Path $root 'tools/Write-FrameScopeBuildMetadata.ps1') -OutputPath $output | Out-Null
    $source = Get-Content -Raw $output
    if ($source -notmatch ('AssemblyFileVersion\("'+[regex]::Escape($core)+'.0"\)') -or $source -notmatch ([regex]::Escape($expected)+'\+[0-9a-f]{40}')) { throw 'Missing numeric core or full Git version' }
} finally { if(Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output } }
Write-Output 'Version source contracts: PASS'
