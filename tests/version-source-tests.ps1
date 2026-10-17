$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$expected = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if ($expected -ne '2.0.0-beta.1') { throw 'Initial target must be 2.0.0-beta.1' }
foreach ($relative in @('desktop-preview/package.json','desktop-preview/src-tauri/tauri.conf.json','desktop-preview/installer/src-tauri/tauri.conf.json','src/frontend/package.json')) {
    $actual = (Get-Content (Join-Path $root $relative) -Raw | ConvertFrom-Json).version
    if ($actual -ne $expected) { throw "Version mismatch: $relative ($actual)" }
}
$output = Join-Path ([IO.Path]::GetTempPath()) ('framescope-metadata-' + [guid]::NewGuid().ToString('N') + '.cs')
try {
    & (Join-Path $root 'tools/Write-FrameScopeBuildMetadata.ps1') -OutputPath $output | Out-Null
    $source = Get-Content -Raw $output
    if ($source -notmatch 'AssemblyFileVersion\("2\.0\.0\.0"\)' -or $source -notmatch '2\.0\.0-beta\.1\+[0-9a-f]{7,40}') { throw 'Missing numeric core or full Git version' }
} finally { if(Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output } }
Write-Output 'Version source contracts: PASS'
